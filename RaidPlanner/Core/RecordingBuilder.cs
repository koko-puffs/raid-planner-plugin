using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPlanner.Core;

/// <summary>
/// A pull being recorded. The game side (Game/FightRecorder) hands it plain facts as they happen,
/// timed in seconds from the pull; this keeps the recording's own numbers for everyone (party 1
/// up, enemies 101 up), the actions' data once each, tethers as they come and go, waymarks when
/// they change, and where everyone stands twice a second. Game-free, so it's tested.
/// </summary>
public sealed class RecordingBuilder
{
    /// <summary>How often positions are kept, in seconds.</summary>
    public const double FrameEvery = 0.5;

    /// <summary>The most kept of each, so a very long pull still makes a file the site takes (about 2 MB at most).</summary>
    public const int MaxEvents = 20000;

    public const int MaxFrames = 4800;

    /// <summary>A pull shorter than this, or with no mechanic in it, isn't kept.</summary>
    public const double ShortestKept = 10;

    /// <summary>A death's cause: the last enemy action that hit them this long before, at most.</summary>
    public const double DeathCause = 3.0;

    /// <summary>
    /// Every enemy gone this long or more, then one back, is a new phase; a shorter while (a boss
    /// jumping away for a mechanic) isn't. The site's <c>PHASE_GAP</c>, the same rule.
    /// </summary>
    public const double PhaseGap = 8.0;

    private readonly Func<uint, ActionInfo?> actionInfo;
    private readonly Func<uint, StatusInfo?> statusInfo;
    private readonly Recording recording;
    private readonly Dictionary<ulong, int> partyIds = new();
    private readonly Dictionary<ulong, int> actorIds = new();
    private readonly Dictionary<int, (string Name, float Hitbox, uint DataId)> actorFacts = new();
    private readonly HashSet<int> targetable = new();
    private readonly Dictionary<int, uint> maxHp = new();
    private readonly HashSet<(int From, int To, uint Kind)> tethers = new();
    private Dictionary<string, (float X, float Z)>? waymarks;
    private double lastFrame = double.NegativeInfinity;
    private int nextActor = 101;

    // Statuses on the party: those recorded (kept) and those that aren't (the party's own), by
    // (party member, status); what was seen this frame, and whose statuses were read.
    private readonly Dictionary<(int On, uint Id), bool> statuses = new();
    private readonly HashSet<(int On, uint Id)> statusSeen = new();
    private readonly HashSet<int> statusRead = new();
    private readonly List<(int On, uint Id)> statusGone = new();

    // Deaths: who's down now, and the last enemy action that hit each.
    private readonly Dictionary<int, bool> down = new();
    private readonly Dictionary<int, (double T, uint Action, int Source)> lastHit = new();

    // Targetability of the enemies that have been targetable, and those reported this frame.
    private readonly Dictionary<int, bool> canTarget = new();
    private readonly HashSet<int> targetSeen = new();
    private readonly List<int> targetGone = new();
    private int targetableNow;
    private double allGoneAt = -1;

    public RecordingBuilder(DutyInfo duty, int pull, DateTimeOffset startedAt, bool partyPositions, string pluginVersion, Func<uint, ActionInfo?> actionInfo,
        Func<uint, StatusInfo?>? statusInfo = null)
    {
        this.actionInfo = actionInfo;
        this.statusInfo = statusInfo ?? (_ => null);
        recording = new Recording
        {
            Duty = duty,
            Pull = pull,
            RecordedAt = startedAt,
            PartyPositions = partyPositions,
            PluginVersion = pluginVersion,
        };
    }

    public DutyInfo Duty => recording.Duty;

    public int Pull => recording.Pull;

    public DateTimeOffset StartedAt => recording.RecordedAt;

    /// <summary>Enemy casts so far (for the window's live count).</summary>
    public int Casts { get; private set; }

    public int Markers { get; private set; }

    /// <summary>
    /// The furthest phase, from 1: one more each time every enemy that had been targetable was gone
    /// for <see cref="PhaseGap"/> seconds or more and then one became targetable (again, or for the
    /// first time: a new boss).
    /// </summary>
    public int Phase { get; private set; } = 1;

    /// <summary>The party, in the party list's order, each given their spot. Called once, as the pull starts.</summary>
    public void SetParty(IReadOnlyList<Partier> party)
    {
        recording.Party.Clear();
        partyIds.Clear();
        var id = 1;
        foreach (var (who, slot) in Roles.Assign(party))
        {
            if (partyIds.ContainsKey(who.Key)) continue;
            partyIds[who.Key] = id;
            recording.Party.Add(new PartyMember(id, slot, who.Job, Roles.RoleWord(who.Role)));
            id++;
        }
    }

    /// <summary>A party member's number, or null for someone not in it.</summary>
    public int? PartyId(ulong key) => partyIds.TryGetValue(key, out var id) ? id : null;

    public bool IsParty(ulong key) => partyIds.ContainsKey(key);

    /// <summary>An enemy's (or helper's) number, given one the first time it's seen.</summary>
    public int Actor(ulong key, string name, float hitbox, uint dataId)
    {
        if (actorIds.TryGetValue(key, out var id))
        {
            // A name that only shows once it's targetable (some bosses come in nameless).
            if (name.Length > 0 && actorFacts[id].Name.Length == 0) actorFacts[id] = (name, hitbox, dataId);
            return id;
        }
        id = nextActor++;
        actorIds[key] = id;
        actorFacts[id] = (name, hitbox, dataId);
        return id;
    }

    public int? KnownActor(ulong key) => actorIds.TryGetValue(key, out var id) ? id : null;

    /// <summary>Is its name still wanted: never seen, or seen without one?</summary>
    public bool NeedsName(ulong key) => !actorIds.TryGetValue(key, out var id) || actorFacts[id].Name.Length == 0;

    /// <summary>Who someone is in the recording: a party member's number, an enemy's, or 0.</summary>
    public int Who(ulong key) => partyIds.TryGetValue(key, out var p) ? p : actorIds.TryGetValue(key, out var a) ? a : 0;

    /// <summary>The enemy could be targeted at some point (it's drawn: a boss or an add, not a helper), with its most HP.</summary>
    public void SeenTargetable(int actor, uint hp)
    {
        targetable.Add(actor);
        if (!maxHp.TryGetValue(actor, out var had) || hp > had) maxHp[actor] = hp;
    }

    private void Remember(uint action)
    {
        if (recording.Actions.ContainsKey(action)) return;
        recording.Actions[action] = actionInfo(action) ?? new ActionInfo(string.Empty, 0, 0, 0, string.Empty, 0);
    }

    private bool Room => recording.Events.Count < MaxEvents;

    public void Cast(double t, int source, uint action, float duration, float x, float z, float rotation, int target, float? targetX, float? targetZ)
    {
        if (!Room || source == 0 || action == 0) return;
        Remember(action);
        recording.Events.Add(new CastEvent(t, source, action, duration, x, z, rotation, target, targetX, targetZ));
        Casts++;
    }

    public void Hit(double t, int source, uint action, float x, float z, float rotation, float hitX, float hitZ, IReadOnlyList<int> targets)
    {
        if (!Room || source == 0 || action == 0) return;
        Remember(action);
        var hit = targets.Where(id => id > 0).Distinct().ToList();
        foreach (var id in hit)
            if (id <= 100) lastHit[id] = (t, action, source);
        recording.Events.Add(new HitEvent(t, source, action, x, z, rotation, hitX, hitZ, hit));
    }

    public void Marker(double t, int on, uint icon)
    {
        if (!Room || on == 0) return;
        recording.Events.Add(new MarkerEvent(t, on, icon));
        Markers++;
    }

    /// <summary>The arena changed (a map effect).</summary>
    public void MapEffect(double t, byte index, uint state)
    {
        if (Room) recording.Events.Add(new MapEffectEvent(t, index, state));
    }

    // ---------------------------------------------------------------------------------------------
    // Statuses on the party, read every frame: BeginStatuses, then for each party member read
    // StatusesOf and, for each of their statuses, StatusKnown (and StatusGained for a new one), then EndStatuses.

    public void BeginStatuses()
    {
        statusSeen.Clear();
        statusRead.Clear();
    }

    /// <summary>A party member whose statuses are being read this frame (one not read keeps theirs).</summary>
    public void StatusesOf(int on) => statusRead.Add(on);

    /// <summary>Is this status on them already known (recorded, or one of the party's own)? Marks it seen either way.</summary>
    public bool StatusKnown(int on, uint id)
    {
        var key = (on, id);
        if (!statuses.ContainsKey(key)) return false;
        statusSeen.Add(key);
        return true;
    }

    /// <summary>
    /// A status new on a party member: recorded when <paramref name="keep"/> (from an enemy, a
    /// helper, or no one: <paramref name="source"/> 0), else only remembered (the party's own).
    /// </summary>
    public void StatusGained(double t, int on, uint id, int source, float remaining, bool keep)
    {
        if (on == 0 || id == 0) return;
        var key = (on, id);
        statuses[key] = keep;
        statusSeen.Add(key);
        if (!keep || !Room) return;
        if (!recording.Statuses.ContainsKey(id)) recording.Statuses[id] = statusInfo(id) ?? new StatusInfo(string.Empty, 0);
        recording.Events.Add(new StatusEvent(t, on, id, source, float.IsFinite(remaining) ? Math.Max(0, remaining) : 0, true));
    }

    /// <summary>The frame's statuses are read: those gone from the party members read are recorded as gone.</summary>
    public void EndStatuses(double t)
    {
        if (statuses.Count == 0) return;
        statusGone.Clear();
        foreach (var (key, kept) in statuses)
        {
            if (statusSeen.Contains(key) || !statusRead.Contains(key.On)) continue;
            statusGone.Add(key);
            if (kept && Room) recording.Events.Add(new StatusEvent(t, key.On, key.Id, 0, 0, false));
        }
        foreach (var key in statusGone) statuses.Remove(key);
    }

    // ---------------------------------------------------------------------------------------------
    // Deaths and targetability.

    /// <summary>
    /// A party member as they are this frame: going down is a death, with the last enemy action that
    /// hit them within <see cref="DeathCause"/> seconds. Down already when first seen isn't one.
    /// </summary>
    public void Alive(double t, int who, bool dead)
    {
        if (who == 0) return;
        if (!down.TryGetValue(who, out var was))
        {
            down[who] = dead;
            return;
        }
        if (was == dead) return;
        down[who] = dead;
        if (!dead || !Room) return;
        var (by, from) = lastHit.TryGetValue(who, out var h) && t - h.T <= DeathCause && t >= h.T - 0.5 ? (h.Action, h.Source) : (0u, 0);
        recording.Events.Add(new DeathEvent(t, who, by, from));
    }

    /// <summary>
    /// An enemy as it is this frame (call <see cref="EndTargets"/> after them all): one becoming
    /// targetable (the first time too: a new boss) is recorded, and so is one that has been going
    /// untargetable; they count toward the phase. One never targetable isn't recorded.
    /// </summary>
    public void Targetable(double t, int actor, bool on)
    {
        if (actor == 0) return;
        targetSeen.Add(actor);
        var known = canTarget.TryGetValue(actor, out var was);
        if (known ? was == on : !on) return;
        canTarget[actor] = on;
        if (on) BecameTargetable(t);
        else BecameUntargetable(t);
        if (Room) recording.Events.Add(new TargetEvent(t, actor, on));
    }

    /// <summary>The frame's enemies are reported: a targetable one that's gone from the game (not reported) is untargetable now.</summary>
    public void EndTargets(double t)
    {
        if (canTarget.Count > 0)
        {
            targetGone.Clear();
            foreach (var (actor, on) in canTarget)
                if (on && !targetSeen.Contains(actor)) targetGone.Add(actor);
            foreach (var actor in targetGone)
            {
                canTarget[actor] = false;
                BecameUntargetable(t);
                if (Room) recording.Events.Add(new TargetEvent(t, actor, false));
            }
        }
        targetSeen.Clear();
    }

    private void BecameTargetable(double t)
    {
        if (targetableNow == 0 && allGoneAt >= 0 && t - allGoneAt >= PhaseGap) Phase++;
        targetableNow++;
        allGoneAt = -1;
    }

    private void BecameUntargetable(double t)
    {
        targetableNow = Math.Max(0, targetableNow - 1);
        if (targetableNow == 0 && allGoneAt < 0) allGoneAt = t;
    }

    /// <summary>The tethers there are now: a new one is recorded as it appears, one gone as it goes.</summary>
    public void Tethers(double t, IReadOnlyCollection<(int From, int To, uint Kind)> now)
    {
        // Most frames: none then, none now.
        if (now.Count == 0 && tethers.Count == 0) return;
        var current = now.Where(x => x.From != 0 && x.To != 0 && x.From != x.To).ToHashSet();
        foreach (var gone in tethers.Except(current).ToList())
        {
            tethers.Remove(gone);
            if (Room) recording.Events.Add(new TetherEvent(t, gone.From, gone.To, gone.Kind, false));
        }
        foreach (var added in current.Except(tethers).ToList())
        {
            tethers.Add(added);
            if (Room) recording.Events.Add(new TetherEvent(t, added.From, added.To, added.Kind, true));
        }
    }

    /// <summary>The waymarks on the floor now (only those placed): recorded when they're not as they were.</summary>
    public void Waymarks(double t, IReadOnlyDictionary<string, (float X, float Z)> now)
    {
        if (waymarks != null && waymarks.Count == now.Count
            && now.All(kv => waymarks.TryGetValue(kv.Key, out var p) && MathF.Abs(p.X - kv.Value.X) < 0.05f && MathF.Abs(p.Z - kv.Value.Z) < 0.05f))
            return;
        waymarks = now.ToDictionary(kv => kv.Key, kv => kv.Value);
        if (Room && (now.Count > 0 || recording.Events.OfType<WaymarksEvent>().Any()))
            recording.Events.Add(new WaymarksEvent(t, waymarks));
    }

    /// <summary>
    /// Where everyone is now (by the recording's numbers): kept twice a second. The party's places
    /// only when they're being recorded.
    /// </summary>
    public bool WantsFrame(double t) => t - lastFrame >= FrameEvery - 0.001 && recording.Frames.Count < MaxFrames;

    public void Positions(double t, IReadOnlyDictionary<int, (float X, float Z, float Rotation)> at)
    {
        if (!WantsFrame(t)) return;
        lastFrame = t;
        var kept = at.Where(kv => kv.Key > 100 || recording.PartyPositions).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (kept.Count > 0) recording.Frames.Add(new Frame(t, kept));
    }

    /// <summary>
    /// Is it worth keeping: long enough, with a mechanic in it (an enemy's named cast, or a hit that
    /// isn't an auto-attack)? A trash pack that only swung at the tank isn't.
    /// </summary>
    public bool Worthwhile(double t) => t >= ShortestKept && Timeline.Build(recording).Count > 0;

    /// <summary>
    /// The pull, finished: how long it took and how it ended; the enemies sorted into bosses (the
    /// targetable ones with the most HP, and any with half as much or more), adds (the other
    /// targetable ones) and helpers (never targetable: only their attacks are kept, not where they stood).
    /// </summary>
    public Recording Finish(double t, Outcome outcome)
    {
        recording.Duration = Math.Max(0, t);
        recording.Outcome = outcome;
        recording.Actors.Clear();
        var top = targetable.Count == 0 ? 0u : targetable.Max(id => maxHp.TryGetValue(id, out var hp) ? hp : 0u);
        foreach (var (id, (name, hitbox, dataId)) in actorFacts.OrderBy(kv => kv.Key))
        {
            ActorKind kind;
            if (!targetable.Contains(id)) kind = ActorKind.Helper;
            else
            {
                var hp = maxHp.TryGetValue(id, out var h) ? h : 0u;
                kind = top == 0 || (ulong)hp * 2 >= top ? ActorKind.Boss : ActorKind.Add;
            }
            recording.Actors.Add(new Actor(id, name, kind, hitbox, dataId));
        }
        var helpers = recording.Actors.Where(a => a.Kind == ActorKind.Helper).Select(a => a.Id).ToHashSet();
        if (helpers.Count > 0)
        {
            for (var i = 0; i < recording.Frames.Count; i++)
            {
                var f = recording.Frames[i];
                if (!f.At.Keys.Any(helpers.Contains)) continue;
                recording.Frames[i] = f with { At = f.At.Where(kv => !helpers.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value) };
            }
            recording.Frames.RemoveAll(f => f.At.Count == 0);
        }
        return recording;
    }
}
