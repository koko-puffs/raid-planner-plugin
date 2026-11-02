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

    private readonly Func<uint, ActionInfo?> actionInfo;
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

    public RecordingBuilder(DutyInfo duty, int pull, DateTimeOffset startedAt, bool partyPositions, string pluginVersion, Func<uint, ActionInfo?> actionInfo)
    {
        this.actionInfo = actionInfo;
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
        recording.Events.Add(new HitEvent(t, source, action, x, z, rotation, hitX, hitZ, targets.Where(id => id > 0).Distinct().ToList()));
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
