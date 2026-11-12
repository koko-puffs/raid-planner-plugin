using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.Core;

/// <summary>Whose a status is, as the recording keeps it.</summary>
public enum StatusFrom
{
    /// <summary>An enemy's, a helper's, or no one's (the environment): a <c>status</c> event.</summary>
    Enemy,

    /// <summary>The party's (a member or their pet): a row of the combat block's <c>st</c>.</summary>
    Party,

    /// <summary>Someone else's (another player, another party's pet): not kept, only remembered so it isn't looked at again.</summary>
    Other,
}

/// <summary>
/// A pull being recorded. The game side (Game/FightRecorder) hands it plain facts as they happen,
/// timed in seconds from the pull; this keeps the recording's own numbers for everyone (party 1
/// up, enemies 101 up), the actions' data once each, tethers as they come and go, waymarks when
/// they change, and where everyone stands twice a second. Since 0.3.0 also HP (the party's max at
/// the start, the enemies' most, everyone's in the frames), what each enemy hit did, statuses'
/// stacks and refreshes, the enemies' own statuses, and the party's own fight (the combat block:
/// <see cref="CombatLog"/>). Since 0.4.0 also every action and item the party used (and in the 30
/// seconds before the pull), ground placements, enemy casts stopped (and who interrupted them), raises,
/// the enemies' targets, the recording player's target's enmity list, MP and barriers, the limit break
/// gauge, head signs, the countdown, each hit's barriers and places, a damage effect's bonus %, and why a
/// party cast was stopped. Game-free, so it's tested.
/// </summary>
public sealed class RecordingBuilder
{
    /// <summary>How often positions are kept by default, in seconds (twice a second).</summary>
    public const double FrameEvery = 0.5;

    /// <summary>Settings → Recording → Smoother replays (since 0.4.0): four times a second, and twice as many kept.</summary>
    public const double SmoothFrameEvery = 0.25;

    public const int SmoothMaxFrames = 9600;

    /// <summary>An enemy's cast stopped this close to its end (or after) is taken as finished: the game says "cancelled" on some that went off.</summary>
    public const double CancelBeforeEnd = 0.25;

    /// <summary>A member's MP and barrier are kept at most this often each (seconds), and MP only when it moved this much.</summary>
    public const double ResEvery = 0.5;

    public const int ResMpStep = 500;

    /// <summary>An enmity row is kept when a member's place on the list changed, or their % moved this much.</summary>
    public const int HateStep = 5;

    /// <summary>The most kept of each, so a very long pull still makes a file the site takes (about 2 MB at most).</summary>
    public const int MaxEvents = 20000;

    public const int MaxFrames = 4800;

    /// <summary>How often positions are kept for this pull (<see cref="FrameEvery"/>, or <see cref="SmoothFrameEvery"/>).</summary>
    public double FrameStep { get; init; } = FrameEvery;

    /// <summary>The most frames kept for this pull (<see cref="MaxFrames"/>, or <see cref="SmoothMaxFrames"/>).</summary>
    public int FrameCap { get; init; } = MaxFrames;

    /// <summary>A pull shorter than this, or with no mechanic in it, isn't kept.</summary>
    public const double ShortestKept = 10;

    /// <summary>A death's cause: the last enemy action (or bleed) that hit them this long before, at most.</summary>
    public const double DeathCause = 3.0;

    /// <summary>
    /// Every enemy gone this long or more, then one back, is a new phase; a shorter while (a boss
    /// jumping away for a mechanic) isn't. The site's <c>PHASE_GAP</c>, the same rule.
    /// </summary>
    public const double PhaseGap = 8.0;

    /// <summary>A status's time left going up by more than this (seconds) is it being put on again.</summary>
    public const float RefreshJump = 0.5f;

    /// <summary>A party member's cast with no action of theirs this long after it should have ended was cancelled.</summary>
    public const double CastGrace = 1.5;

    private readonly Func<uint, ActionInfo?> actionInfo;
    private readonly Func<uint, StatusInfo?> statusInfo;
    private readonly Recording recording;
    private readonly CombatLog combat = new();
    private readonly Dictionary<ulong, int> partyIds = new();
    private readonly Dictionary<ulong, int> actorIds = new();
    private readonly Dictionary<int, (string Name, float Hitbox, uint DataId)> actorFacts = new();
    private readonly HashSet<int> targetable = new();
    private readonly Dictionary<int, uint> maxHp = new();
    private readonly Dictionary<int, long> mostHp = new();
    private readonly HashSet<(int From, int To, uint Kind)> tethers = new();
    private Dictionary<string, (float X, float Z)>? waymarks;
    private double lastFrame = double.NegativeInfinity;
    private int nextActor = 101;

    // Statuses on the party and the enemies, by (on, status, whose: the source's own key): how each
    // is kept, and as it was last seen (its stacks, its time left); what was seen this frame, and
    // whose statuses were read.
    private readonly Dictionary<(int On, uint Id, ulong From), Kept> statuses = new();
    private readonly HashSet<(int On, uint Id, ulong From)> statusSeen = new();
    private readonly HashSet<int> statusRead = new();
    private readonly List<(int On, uint Id, ulong From)> statusGone = new();

    private struct Kept
    {
        public StatusFrom From;
        public int Source;
        public int Stacks;
        public float Remaining;
        public uint Action;
    }

    // Deaths: who's down now, and the last enemy action (or status, by a tick) that hit each, and the
    // last that could kill them (an instant death, or as much as the HP they had before it).
    private readonly Dictionary<int, bool> down = new();
    private readonly Dictionary<int, (double T, uint By, int Source, bool ByStatus)> lastHit = new();
    private readonly Dictionary<int, (double T, uint By, int Source, bool ByStatus)> lastLethal = new();

    // Targetability of the enemies that have been targetable, and those reported this frame.
    private readonly Dictionary<int, bool> canTarget = new();
    private readonly HashSet<int> targetSeen = new();
    private readonly List<int> targetGone = new();
    private int targetableNow;
    private double allGoneAt = -1;

    // The party's casts not known to have finished yet: by caster, the row and its action.
    private readonly Dictionary<int, (int Row, uint Action, double End)> casting = new();

    // Since 0.4.0. What's what (interrupts, raises…), the items' names, and the game's own recasts for the recording player.
    private readonly CombatKinds kinds;
    private readonly Func<uint, ItemInfo?> itemInfo;

    // The enemies' casts as they began (by actor: the action, when it should end), for a cast stopped.
    private readonly Dictionary<int, (uint Action, double End)> enemyCasting = new();
    private readonly CastWatch interrupts = new();

    // Each enemy's target as last recorded; the enmity list's last row per (enemy, member); MP and barrier per member; the gauge; the signs.
    private readonly Dictionary<int, int> aggro = new();
    private readonly Dictionary<(int On, int Who), (int Rank, int Pct)> hateLast = new();
    private readonly Dictionary<int, (double T, int Mp, int Shield)> resLast = new();
    private (int Value, int Max)? gaugeLast;
    private Dictionary<int, int>? signs;

    // Raises: the Raise status put on each member since they died (when, by whom); the last raise used on them while down.
    private readonly Dictionary<int, (double T, int By)> raiseStatus = new();
    private readonly Dictionary<int, (double T, int By, bool Lb)> raiseUse = new();

    /// <summary>
    /// The recording player's own recast for one of their actions, in seconds, and its most charges (0: not known), as the game
    /// works them out with their traits at the synced level (<c>actions{}.rc</c>; set by the game side, asked at <see cref="Finish"/>).
    /// </summary>
    public Func<uint, (float Recast, int Charges)?>? OwnRecast { get; set; }

    public RecordingBuilder(DutyInfo duty, int pull, DateTimeOffset startedAt, bool partyPositions, string pluginVersion, Func<uint, ActionInfo?> actionInfo,
        Func<uint, StatusInfo?>? statusInfo = null, CombatKinds? kinds = null, Func<uint, ItemInfo?>? itemInfo = null)
    {
        this.actionInfo = actionInfo;
        this.statusInfo = statusInfo ?? (_ => null);
        this.kinds = kinds ?? CombatKinds.None;
        this.itemInfo = itemInfo ?? (_ => null);
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

    /// <summary>Tethers seen to appear so far (the window's live count, as a pull's screen counts them: each one put on).</summary>
    public int TethersSeen { get; private set; }

    /// <summary>
    /// The furthest phase, from 1: one more each time every enemy that had been targetable was gone
    /// for <see cref="PhaseGap"/> seconds or more and then one became targetable (again, or for the
    /// first time: a new boss).
    /// </summary>
    public int Phase { get; private set; } = 1;

    /// <summary>When each phase began (the first at 0), the site's <c>phaseStarts</c> with 0 before them.</summary>
    public List<double> PhaseStarts { get; } = new() { 0 };

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
            recording.Party.Add(new PartyMember(id, slot, who.Job, Roles.RoleWord(who.Role), Math.Max(0, who.MaxHp)));
            id++;
        }
    }

    /// <summary>The recording player (their spot is the summary's <c>self</c>): set once, after <see cref="SetParty"/>.</summary>
    public void SetSelf(ulong key)
    {
        if (!partyIds.TryGetValue(key, out var id)) return;
        for (var i = 0; i < recording.Party.Count; i++)
            recording.Party[i] = recording.Party[i] with { Self = recording.Party[i].Id == id };
    }

    /// <summary>The recording player's gear as the pull began (since 0.5.0; null: not read).</summary>
    public void SetGear(Gear? gear) => recording.Gear = gear;

    /// <summary>The recording player's party id (0 not known).</summary>
    public int SelfId => recording.Party.FirstOrDefault(p => p.Self)?.Id ?? 0;

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
        SeenMaxHp(actor, hp);
    }

    /// <summary>An enemy's max HP as seen (the most is kept, for <c>actors[].maxHp</c>).</summary>
    public void SeenMaxHp(int actor, long hp)
    {
        if (hp > 0 && (!mostHp.TryGetValue(actor, out var had) || hp > had)) mostHp[actor] = hp;
    }

    private void Remember(uint action)
    {
        if (recording.Actions.ContainsKey(action)) return;
        recording.Actions[action] = actionInfo(action) ?? new ActionInfo(string.Empty, 0, 0, 0, string.Empty, 0);
    }

    private void RememberStatus(uint id)
    {
        if (id != 0 && !recording.Statuses.ContainsKey(id)) recording.Statuses[id] = statusInfo(id) ?? new StatusInfo(string.Empty, 0);
    }

    private bool Room => recording.Events.Count < MaxEvents;

    public void Cast(double t, int source, uint action, float duration, float x, float z, float rotation, int target, float? targetX, float? targetZ,
        bool interruptible = false)
    {
        if (source == 0 || action == 0) return;
        enemyCasting[source] = (action, t + Math.Max(0, duration));
        if (!Room) return;
        Remember(action);
        recording.Events.Add(new CastEvent(t, source, action, duration, x, z, rotation, target, targetX, targetZ, interruptible));
        Casts++;
    }

    /// <summary>
    /// An enemy's action landed on these party members (or none). With <paramref name="damage"/>,
    /// <paramref name="fx"/> and <paramref name="hp"/> (aligned with the targets: what each took, the
    /// hit's flags, their HP before), the death cause only counts a hit that did something (damage, or
    /// an instant death); without them, any hit (as before 0.3.0).
    /// </summary>
    public void Hit(double t, int source, uint action, float x, float z, float rotation, float hitX, float hitZ, IReadOnlyList<int> targets,
        IReadOnlyList<long>? damage = null, IReadOnlyList<int>? fx = null, IReadOnlyList<long>? hp = null, IReadOnlyList<int>? shield = null,
        IReadOnlyList<float>? px = null, IReadOnlyList<float>? pz = null)
    {
        if (!Room || source == 0 || action == 0) return;
        Remember(action);
        if (damage != null && damage.Count != targets.Count) damage = null;
        if (fx != null && fx.Count != targets.Count) fx = null;
        if (hp != null && hp.Count != targets.Count) hp = null;
        if (shield != null && shield.Count != targets.Count) shield = null;
        if (px == null || pz == null || px.Count != targets.Count || pz.Count != targets.Count) px = pz = null;
        // Each target once (the first), the lists kept in step.
        var keep = new List<int>(targets.Count);
        var seen = new HashSet<int>();
        for (var i = 0; i < targets.Count; i++)
            if (targets[i] > 0 && seen.Add(targets[i]))
                keep.Add(i);
        var hit = keep.Select(i => targets[i]).ToList();
        var dmg = damage == null ? null : keep.Select(i => Math.Max(0, damage[i])).ToList();
        var flags = fx == null ? null : keep.Select(i => fx[i]).ToList();
        var before = hp == null ? null : keep.Select(i => hp[i] < 0 ? -1 : hp[i]).ToList();
        var sh = shield == null ? null : keep.Select(i => shield[i] is >= 0 and <= 100 ? shield[i] : -1).ToList();
        var xs = px == null ? null : keep.Select(i => px[i]).ToList();
        var zs = pz == null ? null : keep.Select(i => pz![i]).ToList();
        for (var k = 0; k < hit.Count; k++)
        {
            if (hit[k] > 100) continue;
            var instant = flags != null && (flags[k] & Meter.Effects.FxInstantDeath) != 0;
            var did = dmg == null || dmg[k] > 0 || instant;
            if (did) Struck(hit[k], (t, action, source, false), instant || (dmg != null && before != null && before[k] >= 0 && dmg[k] >= before[k]));
        }
        recording.Events.Add(new HitEvent(t, source, action, x, z, rotation, hitX, hitZ, hit, dmg, flags, before, sh, xs, zs));
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
    // Statuses, read every frame from the party and the enemies: BeginStatuses, then for each whose
    // list is read StatusesOf and, for each of their statuses, StatusKnown (and StatusGained for a new
    // one), then EndStatuses. Each (on, status, whose) is kept as it's first classed (StatusFrom):
    // an enemy's (or no one's) as a status event, the party's as a combat row, anyone else's not at all.

    public void BeginStatuses()
    {
        statusSeen.Clear();
        statusRead.Clear();
    }

    /// <summary>Someone whose statuses are being read this frame (one not read keeps theirs).</summary>
    public void StatusesOf(int on) => statusRead.Add(on);

    /// <summary>Is this status on them already known (recorded, or one of the party's own)? Marks it seen either way. (As before 0.3.0: no stacks or refreshes.)</summary>
    public bool StatusKnown(int on, uint id) => StatusKnown(0, on, id, 0, 0, float.NaN);

    /// <summary>
    /// Is this status, from this source (<paramref name="from"/>: the source's own key, 0 for none),
    /// on them already known? Marks it seen; one whose stacks changed, or whose time left went up (put
    /// on again), is recorded as such (a status event with <c>re</c>, or a combat row).
    /// </summary>
    public bool StatusKnown(double t, int on, uint id, ulong from, int stacks, float remaining)
    {
        var key = (on, id, from);
        if (!statuses.TryGetValue(key, out var k)) return false;
        statusSeen.Add(key);
        if (k.From == StatusFrom.Other || float.IsNaN(remaining)) return true;
        // Put on again: its stacks changed, or its time went up (one read with no time yet, a self-cast
        // status before the game's confirmation, isn't: its first time is just its time).
        var again = stacks != k.Stacks || (k.Remaining > 0 && float.IsFinite(remaining) && remaining > k.Remaining + RefreshJump);
        k.Stacks = stacks;
        k.Remaining = float.IsFinite(remaining) ? remaining : 0;
        statuses[key] = k;
        if (!again) return true;
        if (k.From == StatusFrom.Enemy)
        {
            if (Room) recording.Events.Add(new StatusEvent(t, on, id, k.Source, Math.Max(0, k.Remaining), true, stacks, true));
        }
        else
        {
            AddStatusRow(new StatusRow(t, on, k.Source, id, k.Action, Math.Max(0, k.Remaining), stacks, true));
        }
        return true;
    }

    /// <summary>
    /// A status new on a party member: recorded when <paramref name="keep"/> (from an enemy, a
    /// helper, or no one: <paramref name="source"/> 0), else only remembered (the party's own). (As before 0.3.0.)
    /// </summary>
    public void StatusGained(double t, int on, uint id, int source, float remaining, bool keep) =>
        StatusGained(t, on, id, 0, keep ? StatusFrom.Enemy : StatusFrom.Other, source, 0, remaining);

    /// <summary>
    /// A status new on someone, from <paramref name="from"/> (the source's own key), classed by whose
    /// it is: an enemy's (or no one's: <paramref name="source"/> its number, or 0) is a status event, the
    /// party's (<paramref name="source"/> the member's number, <paramref name="action"/> the action that
    /// put it on if known) a combat row, anyone else's only remembered.
    /// </summary>
    public void StatusGained(double t, int on, uint id, ulong from, StatusFrom kind, int source, int stacks, float remaining, uint action = 0)
    {
        if (on == 0 || id == 0) return;
        var key = (on, id, from);
        var left = float.IsFinite(remaining) ? Math.Max(0, remaining) : 0;
        statuses[key] = new Kept { From = kind, Source = source, Stacks = stacks, Remaining = left, Action = action };
        statusSeen.Add(key);
        switch (kind)
        {
            case StatusFrom.Enemy:
                // A Raise status the game names no one for: when it landed, if not by whom.
                if (on <= 100 && kinds.RaiseStatus(id) && Down(on)) raiseStatus[on] = (t, 0);
                if (!Room) return;
                RememberStatus(id);
                recording.Events.Add(new StatusEvent(t, on, id, source, left, true, stacks));
                return;
            case StatusFrom.Party:
                AddStatusRow(new StatusRow(t, on, source, id, action, left, stacks, true));
                return;
        }
    }

    private void AddStatusRow(StatusRow row)
    {
        // The Raise status put on a dead member: by whom, and when it landed (rule 1 of a raise's credit).
        if (row.Gain && row.On <= 100 && kinds.RaiseStatus(row.Status) && Down(row.On)) raiseStatus[row.On] = (row.T, row.Source);
        if (combat.Statuses.Count >= CombatLog.MaxRows) return;
        RememberStatus(row.Status);
        if (row.Action != 0) Remember(row.Action);
        combat.Statuses.Add(row);
    }

    /// <summary>The frame's statuses are read: those gone from the ones read are recorded as gone.</summary>
    public void EndStatuses(double t)
    {
        if (statuses.Count == 0) return;
        statusGone.Clear();
        foreach (var (key, k) in statuses)
        {
            if (statusSeen.Contains(key) || !statusRead.Contains(key.On)) continue;
            statusGone.Add(key);
            if (k.From == StatusFrom.Enemy && Room) recording.Events.Add(new StatusEvent(t, key.On, key.Id, 0, 0, false));
            else if (k.From == StatusFrom.Party) AddStatusRow(new StatusRow(t, key.On, k.Source, key.Id, k.Action, 0, 0, false));
        }
        foreach (var key in statusGone) statuses.Remove(key);
    }

    // ---------------------------------------------------------------------------------------------
    // The party's own fight (the combat block), from the combat parser by way of the game side.

    /// <summary>One of the party's effects: <paramref name="kind"/> is the kind and flags (<c>combat.fx.k</c>); <paramref name="bonus"/> a damage effect's bonus % (<c>b</c>).</summary>
    public void CombatFx(double t, uint action, int origin, int recipient, long amount, int kind, long hpBefore, int bonus = 0)
    {
        if (origin == 0 || action == 0 || combat.Fx.Count >= CombatLog.MaxRows) return;
        Remember(action);
        combat.Fx.Add(new FxRow(t, action, origin, recipient, Math.Max(0, amount), kind, hpBefore < 0 ? -1 : hpBefore, bonus));
    }

    // ---------------------------------------------------------------------------------------------
    // Since 0.4.0.

    private bool Down(int who) => down.TryGetValue(who, out var d) && d;

    private void RememberItem(uint id)
    {
        if (id == 0 || combat.Items.ContainsKey(id)) return;
        var hq = id > CombatLists.HqOffset;
        var info = itemInfo(id) ?? itemInfo(hq ? id - CombatLists.HqOffset : id);
        combat.Items[id] = (info ?? new ItemInfo(string.Empty, 0, hq)) with { Hq = hq };
    }

    /// <summary>
    /// One of the party's actions or items used (<c>combat.use</c>): who, the action (an item's id as sent), the first
    /// target (a recording id, 0 none), how many targets, its flags (<see cref="UseFlags"/>). Also what an interrupt
    /// and a raise are credited by.
    /// </summary>
    public void PartyUse(double t, int source, uint action, int target, int count, int flags)
    {
        if (source == 0 || action == 0) return;
        var f = (UseFlags)flags;
        if ((f & UseFlags.Item) != 0) RememberItem(action);
        else Remember(action);
        if (combat.Uses.Count < CombatLog.MaxRows) combat.Uses.Add(new UseRow(t, source, action, target, Math.Clamp(count, 0, 32), flags));
        if ((f & UseFlags.Item) != 0) return;
        if (target > 100) interrupts.Used(t, (uint)target, (uint)source, action, (f & UseFlags.Interrupted) != 0, kinds.Interrupt(action));
        // The healers' limit break 3 raises everyone down; a raise, the one it's on.
        if ((f & UseFlags.Revive) != 0)
        {
            foreach (var (who, isDown) in down)
                if (isDown) raiseUse[who] = (t, source, true);
        }
        else if (target is > 0 and <= 100 && Down(target) && kinds.RaiseAction(action))
        {
            raiseUse[target] = (t, source, false);
        }
    }

    /// <summary>A ground-placed party action (<c>combat.area</c>): where it was put.</summary>
    public void Area(double t, int source, uint action, float x, float z)
    {
        if (source == 0 || action == 0 || combat.Areas.Count >= CombatLog.MaxRows || !float.IsFinite(x) || !float.IsFinite(z)) return;
        Remember(action);
        combat.Areas.Add(new AreaRow(t, source, action, x, z));
    }

    /// <summary>
    /// The game said someone's cast stopped (<paramref name="interrupted"/>, or cancelled): a party member's cast still waiting is
    /// marked so (<c>cast.why</c>); an enemy's that the recording holds, before its end less <see cref="CancelBeforeEnd"/>, is a
    /// <c>cancel</c> event, credited (when interrupted) to the member whose interrupt hit it in the second before.
    /// </summary>
    public void Cancel(double t, int actor, uint action, bool interrupted)
    {
        if (actor == 0 || action == 0) return;
        if (actor <= 100)
        {
            if (!casting.TryGetValue(actor, out var c) || c.Action != action || t >= c.End - CancelBeforeEnd) return;
            casting.Remove(actor);
            combat.Casts[c.Row] = combat.Casts[c.Row] with { Cancelled = true, Why = interrupted ? 2 : 1 };
            return;
        }
        if (!enemyCasting.TryGetValue(actor, out var e) || e.Action != action || t >= e.End - CancelBeforeEnd) return;
        enemyCasting.Remove(actor);
        if (!Room) return;
        var credit = interrupted ? interrupts.Credit(t, (uint)actor) : null;
        recording.Events.Add(new CancelEvent(t, actor, action, interrupted, (int)(credit?.Member ?? 0), credit?.Action ?? 0));
        if (credit is { } cr) Remember(cr.Action);
    }

    /// <summary>A targetable enemy's target now (a recording id, 0 none): an <c>aggro</c> event when it changed.</summary>
    public void Aggro(double t, int actor, int on)
    {
        if (actor == 0) return;
        if (aggro.TryGetValue(actor, out var was) && was == on) return;
        aggro[actor] = on;
        if (Room) recording.Events.Add(new AggroEvent(t, actor, on));
    }

    /// <summary>
    /// The recording player's target's enmity list (<paramref name="on"/> the enemy), top first: a row for a member whose place
    /// changed or whose % moved <see cref="HateStep"/> or more, and −1 for one who left the list.
    /// </summary>
    public void Hate(double t, int on, ReadOnlySpan<(int Who, int Pct)> list)
    {
        if (on == 0) return;
        Span<int> seen = stackalloc int[Math.Min(list.Length, 64)];
        var n = 0;
        for (var rank = 0; rank < list.Length && rank < seen.Length; rank++)
        {
            var (who, pct) = list[rank];
            if (who == 0) continue;
            seen[n++] = who;
            var p = Math.Clamp(pct, 0, 100);
            if (hateLast.TryGetValue((on, who), out var last) && last.Rank == rank && Math.Abs(last.Pct - p) < HateStep) continue;
            hateLast[(on, who)] = (rank, p);
            if (combat.Hate.Count < CombatLog.MaxRows) combat.Hate.Add(new HateRow(t, on, who, p));
        }
        List<(int, int)>? gone = null;
        foreach (var key in hateLast.Keys)
            if (key.On == on && seen[..n].IndexOf(key.Who) < 0)
                (gone ??= new()).Add(key);
        if (gone == null) return;
        foreach (var key in gone)
        {
            hateLast.Remove(key);
            if (combat.Hate.Count < CombatLog.MaxRows) combat.Hate.Add(new HateRow(t, on, key.Item2, -1));
        }
    }

    /// <summary>A member's MP (0–10000) and barrier (% of max HP): the first, then when MP moved <see cref="ResMpStep"/> or the barrier changed, at most every <see cref="ResEvery"/>.</summary>
    public void Resources(double t, int who, int mp, int shield)
    {
        if (who == 0) return;
        mp = Math.Max(0, mp);
        shield = Math.Clamp(shield, 0, 100);
        if (resLast.TryGetValue(who, out var last) && (t - last.T < ResEvery || (Math.Abs(mp - last.Mp) < ResMpStep && shield == last.Shield))) return;
        resLast[who] = (t, mp, shield);
        if (combat.Res.Count < CombatLog.MaxRows) combat.Res.Add(new ResRow(t, who, mp, shield));
    }

    /// <summary>The limit break gauge: its units now and the most (bars × units a bar); a row when it changed.</summary>
    public void LimitGauge(double t, int units, int max)
    {
        units = Math.Max(0, units);
        max = Math.Max(0, max);
        if (gaugeLast is { } g && g.Value == units && g.Max == max) return;
        // Nothing to show before anything's been in it: a party that has no gauge.
        if (gaugeLast == null && units == 0 && max == 0) return;
        gaugeLast = (units, max);
        if (combat.Gauge.Count < CombatLog.MaxRows) combat.Gauge.Add(new GaugeRow(t, units, max));
    }

    /// <summary>The head signs now (by the game's index, the recording id marked): a <c>signs</c> event when they changed.</summary>
    public void Signs(double t, IReadOnlyDictionary<int, int> now)
    {
        // Compared as they'd be kept (a sign on no one, or past the 17, isn't one), so one of those never makes an event every second.
        var placed = 0;
        var same = signs != null;
        foreach (var (k, v) in now)
        {
            if (v == 0 || k is < 0 or >= SignsEvent.Count) continue;
            placed++;
            if (same && (!signs!.TryGetValue(k, out var was) || was != v)) same = false;
        }
        if (same && signs!.Count == placed) return;
        if (signs == null && placed == 0)
        {
            signs = new Dictionary<int, int>();
            return;
        }
        signs = now.Where(kv => kv.Value != 0 && kv.Key is >= 0 and < SignsEvent.Count).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (Room) recording.Events.Add(new SignsEvent(t, signs));
    }

    /// <summary>
    /// What the party used before the pull (times ≤ 0 from its start: who, the action or item, the first target, the flags),
    /// and the countdown that ended near the start (its length; start − zero).
    /// </summary>
    public void PrePull(IEnumerable<(double T, int Source, uint Action, int Target, int Flags)> uses, (double Length, double End)? countdown)
    {
        foreach (var u in uses)
        {
            if (u.Source == 0 || u.Action == 0 || u.T > 0 || u.T < -CombatLog.PreFrom) continue;
            if ((u.Flags & (int)UseFlags.Item) != 0) RememberItem(u.Action);
            else Remember(u.Action);
            combat.Pre.Add(new UseRow(u.T, u.Source, u.Action, u.Target, 0, u.Flags));
        }
        combat.Countdown = countdown;
    }

    /// <summary>
    /// A DoT or HoT tick: on whom, credited to whom (0 when the game didn't say), and the HP the one it
    /// ticked on had before it when the game side read it (<paramref name="hpBefore"/>, −1 not; kept in
    /// the row, <c>combat.tick.h</c>: a HoT's overheal is measured against it). A damage tick on a party
    /// member with its status known can be what killed them (<see cref="DeathEvent.ByStatus"/>): one as
    /// big as the HP they had could kill.
    /// </summary>
    public void CombatTick(double t, int on, int source, uint status, long amount, bool heal, long hpBefore = -1)
    {
        if (on == 0 || amount <= 0) return;
        if (!heal && on <= 100 && status != 0 && partyIds.ContainsValue(on))
        {
            Struck(on, (t, status, source > 100 ? source : 0, true), hpBefore >= 0 && amount >= hpBefore);
            RememberStatus(status);
        }
        if (combat.Ticks.Count >= CombatLog.MaxRows) return;
        if (status != 0) RememberStatus(status);
        combat.Ticks.Add(new TickRow(t, on, source, status, amount, heal, hpBefore < 0 ? -1 : hpBefore));
    }

    /// <summary>A party member began a cast (back-dated to when it began): known as finished when their action goes off, cancelled otherwise.</summary>
    public void PartyCast(double t, int source, uint action, float duration)
    {
        if (source == 0 || action == 0 || duration <= 0) return;
        Settle(source, t, true);
        if (combat.Casts.Count >= CombatLog.MaxRows) return;
        Remember(action);
        casting[source] = (combat.Casts.Count, action, t + duration);
        combat.Casts.Add(new CastRow(t, source, action, duration, false));
    }

    /// <summary>A party member's action went off: the cast of it they began is finished, not cancelled.</summary>
    public void PartyUsed(double t, int source, uint action)
    {
        if (casting.TryGetValue(source, out var c) && c.Action == action && t >= c.End - 2 * CastGrace) casting.Remove(source);
    }

    /// <summary>A cast still waiting: cancelled when another began (<paramref name="anyway"/>) or it's long past its end.</summary>
    private void Settle(int source, double t, bool anyway)
    {
        if (!casting.TryGetValue(source, out var c) || (!anyway && t < c.End + CastGrace)) return;
        casting.Remove(source);
        combat.Casts[c.Row] = combat.Casts[c.Row] with { Cancelled = true };
    }

    // ---------------------------------------------------------------------------------------------
    // Deaths and targetability.

    /// <summary>
    /// A party member as they are this frame: going down is a death, with the last enemy action (or
    /// bleed) that hit them within <see cref="DeathCause"/> seconds. Down already when first seen isn't one.
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
        if (!dead)
        {
            Raised(t, who);
            return;
        }
        raiseStatus.Remove(who);
        raiseUse.Remove(who);
        if (!Room) return;
        // What killed them: the last that could (it landed within the time, and was as big as their HP
        // or an instant death), else the last that hit them. A hit's damage is taken off a moment after the
        // game says it landed, so a bleed's tick can come between it and their HP reaching 0.
        var c = lastLethal.TryGetValue(who, out var l) && Within(l.T) ? l : lastHit.TryGetValue(who, out var h) && Within(h.T) ? h : default;
        recording.Events.Add(new DeathEvent(t, who, c.By, c.Source, c.ByStatus));

        bool Within(double at) => t - at <= DeathCause && t >= at - 0.5;
    }

    /// <summary>
    /// A dead member alive again (they accepted a raise, or the healers' limit break 3): a <c>raise</c> event, credited (in this
    /// order) to the Raise status put on them since they died (its source and time), else the limit break 3 (<c>lb</c>), else the
    /// last raise used on them while down; else no one.
    /// </summary>
    private void Raised(double t, int who)
    {
        var by = 0;
        double? at = null;
        var lb = false;
        if (raiseStatus.TryGetValue(who, out var st))
        {
            by = st.By;
            at = st.T;
        }
        if (raiseUse.TryGetValue(who, out var use))
        {
            if (by == 0) by = use.By;
            at ??= use.T;
            lb = use.Lb;
        }
        raiseStatus.Remove(who);
        raiseUse.Remove(who);
        if (Room) recording.Events.Add(new RaiseEvent(t, who, by, at, lb));
    }

    /// <summary>Something that did them harm: the last to hit them, and (<paramref name="lethal"/>) the last that could kill.</summary>
    private void Struck(int who, (double T, uint By, int Source, bool ByStatus) what, bool lethal)
    {
        lastHit[who] = what;
        if (lethal) lastLethal[who] = what;
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
        if (targetableNow == 0 && allGoneAt >= 0 && t - allGoneAt >= PhaseGap)
        {
            Phase++;
            if (PhaseStarts.Count < Meter.PhaseClock.MostPhases) PhaseStarts.Add(t);
        }
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
            TethersSeen++;
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
    /// only when they're being recorded. With <paramref name="hp"/>, everyone's HP too (by the same rule).
    /// </summary>
    public bool WantsFrame(double t) => t - lastFrame >= FrameStep - 0.001 && recording.Frames.Count < FrameCap;

    public void Positions(double t, IReadOnlyDictionary<int, (float X, float Z, float Rotation)> at, IReadOnlyDictionary<int, long>? hp = null)
    {
        if (!WantsFrame(t)) return;
        lastFrame = t;
        var kept = at.Where(kv => kv.Key > 100 || recording.PartyPositions).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (kept.Count == 0) return;
        var hpKept = hp?.Where(kv => kept.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value));
        recording.Frames.Add(new Frame(t, kept, hpKept is { Count: > 0 } ? hpKept : null));
    }

    /// <summary>
    /// Is it worth keeping: long enough, with a mechanic in it (an enemy's named cast, or a hit that
    /// isn't an auto-attack)? A trash pack that only swung at the tank isn't.
    /// </summary>
    public bool Worthwhile(double t) => t >= ShortestKept && Timeline.Build(recording).Count > 0;

    /// <summary>
    /// The pull, finished: how long it took and how it ended; the enemies sorted into bosses (the
    /// targetable ones with the most HP, and any with half as much or more), adds (the other
    /// targetable ones) and helpers (never targetable: only their attacks are kept, not where they
    /// stood); casts still waiting settled; the combat block attached.
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
            recording.Actors.Add(new Actor(id, name, kind, hitbox, dataId, mostHp.TryGetValue(id, out var most) ? most : 0));
        }
        var helpers = recording.Actors.Where(a => a.Kind == ActorKind.Helper).Select(a => a.Id).ToHashSet();
        if (helpers.Count > 0)
        {
            for (var i = 0; i < recording.Frames.Count; i++)
            {
                var f = recording.Frames[i];
                if (!f.At.Keys.Any(helpers.Contains)) continue;
                var hp = f.Hp?.Where(kv => !helpers.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
                recording.Frames[i] = f with
                {
                    At = f.At.Where(kv => !helpers.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
                    Hp = hp is { Count: > 0 } ? hp : null,
                };
            }
            recording.Frames.RemoveAll(f => f.At.Count == 0);
        }
        foreach (var source in casting.Keys.ToList()) Settle(source, t, false);
        OwnRecasts();
        recording.Combat = combat;
        return recording;
    }

    /// <summary>The recording player's own non-GCD actions: their recast (and charges) as the game works them out (<c>rc</c>).</summary>
    private void OwnRecasts()
    {
        var self = SelfId;
        if (OwnRecast == null || self == 0) return;
        foreach (var action in combat.Uses.Concat(combat.Pre).Where(u => u.Source == self && (u.Flags & (int)(UseFlags.Item | UseFlags.Pet)) == 0)
                     .Select(u => u.Action).Distinct().ToList())
        {
            if (!recording.Actions.TryGetValue(action, out var a) || CombatLists.IsGcd(a.CooldownGroup, a.CooldownGroup2) || a.Recast <= 0) continue;
            try
            {
                if (OwnRecast(action) is not { } own) continue;
                recording.Actions[action] = a with
                {
                    AdjustedRecast = own.Recast > 0 && float.IsFinite(own.Recast) ? own.Recast : a.AdjustedRecast,
                    Charges = own.Charges > 0 ? own.Charges : a.Charges,
                };
            }
            catch (Exception)
            {
                // The game's answer isn't to be had: the sheet's recast stands.
            }
        }
    }
}
