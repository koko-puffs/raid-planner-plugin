using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using RaidPlanner.Core;

namespace RaidPlanner.Game;

/// <summary>
/// Records the fights you're in. In a duty, a pull starts when combat does and ends when it's been
/// over for a moment (or the duty is wiped, completed or left). While it goes, every frame: each
/// enemy's cast as it begins (from the cast bar the game keeps for it), tethers (from each
/// character's own), the statuses enemies put on the party, deaths, enemies going untargetable and
/// back, the waymarks once a second, and where everyone is twice a second; the hits and head
/// markers come from <see cref="GameHooks"/>. The plan window follows a pull through
/// <see cref="PullStarted"/>, <see cref="CastBegan"/>, <see cref="HitLanded"/> and <see cref="PullEnded"/>. At the end, a pull worth keeping (long
/// enough, something happened) is written to the store on a worker thread.
///
/// With recording off (or a duty it doesn't record), a pull is still followed when the plan window
/// shows a plan for the duty (<see cref="FollowWanted"/>): the same pull, hooks and events, but
/// follow-only, so it's never kept, sent or summed up, and the window and the bar don't show it.
///
/// Everything here runs on the game's framework thread, the hooks' detours too, so nothing is shared
/// with another thread but the finished recording.
/// </summary>
public sealed unsafe class FightRecorder : IDisposable
{
    /// <summary>Out of combat this long ends the pull (a raise, a brief gap between phases doesn't).</summary>
    private const double EndAfter = 2.0;

    /// <summary>With everyone down, how long to wait for the game to call it a wipe.</summary>
    private const double WipeWait = 15.0;

    private readonly Configuration config;
    private readonly GameData data;
    private readonly RecordingStore store;
    private readonly string version;
    private readonly Stopwatch clock = new();
    private readonly Dictionary<int, (uint Action, double Start)> casting = new();
    private readonly GameHooks hooks;
    private RecordingBuilder? current;
    private Outcome? outcome;
    private double outOfCombat;
    private double nextWaymarks;
    private uint pullTerritory;
    private int pullInZone;
    private bool stopAsked;
    private DateTime sampleFailedAt;

    /// <summary>The pull going on is only followed (for the plan window), not recorded.</summary>
    private bool followOnly;

    /// <summary>In combat last frame: a pull is only followed from the frame combat begins (never mid-fight, from 0).</summary>
    private bool wasInCombat;

    /// <summary>Unloaded: nothing heard after this is acted on.</summary>
    private volatile bool disposed;

    /// <summary>The id the game gives objects that exist only on your screen: never anyone in the fight.</summary>
    private const uint InvalidId = 0xE0000000;

    /// <summary>Stopped by hand while the fight went on: not started again until combat is over.</summary>
    private bool waitForCombatToEnd;

    /// <summary>
    /// A finished pull that ended without the game saying how, kept back for a moment before it's
    /// saved: the game says "duty complete" (or wiped) a little after combat ends, and then it's a clear.
    /// </summary>
    private (RecordingBuilder B, double T, DateTime Until)? held;

    /// <summary>How long a pull that just ended waits for the game to say it was a clear or a wipe.</summary>
    private static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(10);

    private DateTime tickFailedAt;

    /// <summary>Pulls being written on workers.</summary>
    private readonly List<Task> saving = new();
    private readonly List<(int, int, uint)> tetherBuffer = new();

    public FightRecorder(Configuration config, GameData data, RecordingStore store, string version)
    {
        this.config = config;
        this.data = data;
        this.store = store;
        this.version = version;
        hooks = new GameHooks(this);
        Services.Framework.Update += OnUpdate;
        Services.DutyState.DutyWiped += OnWiped;
        Services.DutyState.DutyCompleted += OnCompleted;
        Services.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    /// <summary>Why hits or head markers can't be read (the game changed under the plugin), or null.</summary>
    public string? HookProblem => hooks.Problem;

    /// <summary>The pull being recorded, or null (a pull only followed isn't).</summary>
    public RecordingBuilder? Current => followOnly ? null : current;

    /// <summary>A pull is being recorded.</summary>
    public bool Recording => current != null && !followOnly;

    /// <summary>A pull is going on, recorded or only followed: the plan window follows either.</summary>
    public bool InPull => current != null;

    /// <summary>
    /// Is a pull wanted followed though it won't be recorded (set by the plugin: the plan window
    /// shows a plan for this duty)? Asked as combat begins.
    /// </summary>
    public Func<bool>? FollowWanted { get; set; }

    /// <summary>Seconds since the pull began.</summary>
    public double Elapsed => clock.Elapsed.TotalSeconds;

    /// <summary>A kept pull (on the framework thread, once it's written).</summary>
    public event Action<RecordingInfo>? Kept;

    /// <summary>A kept pull's summary for the progress tracker (<see cref="PullSummary"/>, as JSON), just after <see cref="Kept"/>.</summary>
    public event Action<RecordingInfo, string>? Summarised;

    /// <summary>A pull began (on the framework thread, as every event here).</summary>
    public event Action? PullStarted;

    /// <summary>The pull ended.</summary>
    public event Action? PullEnded;

    /// <summary>An enemy's cast began: the action, seconds into the pull, and seconds until it goes off. Called once per cast.</summary>
    public event Action<uint, double, double>? CastBegan;

    /// <summary>An enemy's action landed: the action, seconds into the pull.</summary>
    public event Action<uint, double>? HitLanded;

    private DateTime listenerFailedAt;

    /// <summary>A listener that threw: logged now and then, never thrown back into the game or the recording.</summary>
    private void ListenerFailed(Exception ex)
    {
        if (DateTime.UtcNow - listenerFailedAt < TimeSpan.FromSeconds(10)) return;
        listenerFailedAt = DateTime.UtcNow;
        Services.Log.Error(ex, "Raid Planner's plan couldn't follow the pull");
    }

    private void Tell(Action? listeners)
    {
        if (listeners == null) return;
        try
        {
            listeners();
        }
        catch (Exception ex)
        {
            ListenerFailed(ex);
        }
    }

    /// <summary>What became of the last pull that wasn't kept, for the window ("Too short to keep"), with when.</summary>
    public (string Why, DateTime At)? NotKept { get; private set; }

    /// <summary>In a duty right now (where a pull would be recorded): its name, or null.</summary>
    public DutyInfo? DutyHere => InDuty ? data.Duty(Services.ClientState.TerritoryType) : null;

    private static bool InDuty =>
        Services.Condition[ConditionFlag.BoundByDuty] || Services.Condition[ConditionFlag.BoundByDuty56] || Services.Condition[ConditionFlag.BoundByDuty95];

    /// <summary>Ends the pull being recorded now and keeps it (the window's Stop, /raidplan stop).</summary>
    public void Stop()
    {
        if (Recording) stopAsked = true;
    }

    private void OnWiped(Dalamud.Game.DutyState.IDutyStateEventArgs _) => Guarded(() =>
    {
        if (!disposed) Said(Outcome.Wipe);
    });

    private void OnCompleted(Dalamud.Game.DutyState.IDutyStateEventArgs _) => Guarded(() =>
    {
        if (!disposed) Said(Outcome.Clear);
    });

    /// <summary>A game event's handler: what goes wrong is logged, never thrown back into the game.</summary>
    private static void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Raid Planner couldn't handle a game event");
        }
    }

    /// <summary>The game said how it went: the pull going on, or the one just ended and held back.</summary>
    private void Said(Outcome how)
    {
        if (current == null && held is { } h)
        {
            held = null;
            Keep(h.B, h.T, how);
            return;
        }
        outcome = how;
    }

    private void OnTerritoryChanged(uint territory) => Guarded(() =>
    {
        if (disposed) return;
        if (current != null) Finish(outcome ?? Outcome.Ended);
        ReleaseHeld();
        pullInZone = 0;
        pullTerritory = territory;
    });

    /// <summary>The held pull, saved as it is (the game didn't say).</summary>
    private void ReleaseHeld()
    {
        if (held is not { } h) return;
        held = null;
        Keep(h.B, h.T, Outcome.Ended);
    }

    /// <summary>
    /// Is the fight still on: you're in combat, or someone in your party is (you can be down while
    /// they fight on, and that's still the same pull).
    /// </summary>
    private static bool FightGoesOn()
    {
        if (Services.Condition[ConditionFlag.InCombat]) return true;
        foreach (var m in Services.Party)
            if (m.EntityId != 0 && m.GameObject is ICharacter c && (c.StatusFlags & StatusFlags.InCombat) != 0)
                return true;
        return false;
    }

    /// <summary>Does this zone get its pulls recorded by itself?</summary>
    private bool RecordsHere() => InDuty && (config.RecordAllDuties || data.IsFight(Services.ClientState.TerritoryType));

    private void OnUpdate(IFramework framework)
    {
        if (disposed) return;
        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            // A frame that can't be read is lost, not the pull, nor the game (and said once in a while, not every frame).
            if (DateTime.UtcNow - tickFailedAt > TimeSpan.FromSeconds(10))
            {
                Services.Log.Error(ex, "Raid Planner couldn't read this frame");
                tickFailedAt = DateTime.UtcNow;
            }
        }
    }

    private void Tick()
    {
        // Released on time even at the title screen (a held pull is only in memory until then).
        if (held is { } h && DateTime.UtcNow >= h.Until) ReleaseHeld();
        if (!Services.ClientState.IsLoggedIn || Services.Objects.LocalPlayer == null)
        {
            wasInCombat = false;
            if (current != null) Finish(outcome ?? Outcome.Ended);
            return;
        }
        var combat = Services.Condition[ConditionFlag.InCombat];
        var began = combat && !wasInCombat;
        wasInCombat = combat;
        if (current == null)
        {
            stopAsked = false;
            if (!combat && !FightGoesOn()) waitForCombatToEnd = false;
            if (!combat || waitForCombatToEnd) return;
            if (config.AutoRecord && RecordsHere()) Start(false);
            // Followed only from the pull: a plan opened (or read) mid-fight waits for the next one.
            else if (began && InDuty && WantsFollowing()) Start(true);
            return;
        }
        combat = combat || FightGoesOn();

        var t = Elapsed;
        try
        {
            Sample(t);
        }
        catch (Exception ex)
        {
            // A frame that can't be read is lost, never the end of the pull.
            if (DateTime.UtcNow - sampleFailedAt > TimeSpan.FromSeconds(10))
            {
                Services.Log.Error(ex, "Raid Planner couldn't read this frame");
                sampleFailedAt = DateTime.UtcNow;
            }
        }
        if (stopAsked)
        {
            waitForCombatToEnd = combat;
            Finish(outcome ?? Outcome.Ended, true);
            return;
        }
        if (combat && outcome == null)
        {
            outOfCombat = 0;
            return;
        }
        outOfCombat += Services.Framework.UpdateDelta.TotalSeconds;
        // A wipe or a clear ends it once combat is over; else a little after, or, with the whole
        // party down, once the game has had time to say it's a wipe (it says so at the fade-out).
        var wait = outcome == null && PartyDown() ? WipeWait : EndAfter;
        if (!combat && (outcome != null || outOfCombat >= wait)) Finish(outcome ?? (PartyDown() ? Outcome.Wipe : Outcome.Ended));
    }

    /// <summary>Would a pull starting here now be recorded (recording on, in a duty it records)?</summary>
    public bool RecordsThisDuty => config.AutoRecord && RecordsHere();

    /// <summary>Does the plan window want this pull followed (asked once, on the frame combat begins)?</summary>
    private bool WantsFollowing()
    {
        if (FollowWanted is not { } wanted) return false;
        try
        {
            return wanted();
        }
        catch (Exception ex)
        {
            ListenerFailed(ex);
            return false;
        }
    }

    /// <summary>A pull begins: recorded, or (<paramref name="follow"/>) only followed for the plan window.</summary>
    private void Start(bool follow)
    {
        ReleaseHeld();
        followOnly = follow;
        var territory = Services.ClientState.TerritoryType;
        if (territory != pullTerritory)
        {
            pullTerritory = territory;
            pullInZone = 0;
        }
        pullInZone++;
        var duty = data.Duty(territory) ?? new DutyInfo(data.PlaceName(territory), territory, 0);
        current = new RecordingBuilder(duty, pullInZone, DateTimeOffset.Now, config.RecordParty, version, data.Action, data.Status);
        current.SetParty(Party());
        casting.Clear();
        outcome = null;
        outOfCombat = 0;
        nextWaymarks = 0;
        stopAsked = false;
        clock.Restart();
        hooks.Enable();
        Tell(PullStarted);
        Sample(0);
        Services.Log.Information(follow ? $"Following {duty.Name}, pull {pullInZone}, for the plan (not recorded)" : $"Recording {duty.Name}, pull {pullInZone}");
    }

    /// <summary>Is everyone in the party down (a wipe on its way)?</summary>
    private static bool PartyDown()
    {
        var any = false;
        foreach (var m in Services.Party)
        {
            if (m.EntityId == 0) continue;
            any = true;
            if (m.CurrentHP > 0) return false;
        }
        return any ? true : Services.Objects.LocalPlayer is { IsDead: true };
    }

    /// <summary>The party as the game lists it (just you when you're alone).</summary>
    private List<Partier> Party()
    {
        var list = new List<Partier>();
        foreach (var m in Services.Party)
        {
            if (m.EntityId == 0) continue;
            var (job, role) = data.Job(m.ClassJob.RowId);
            list.Add(new Partier(m.EntityId, job, role));
        }
        if (list.Count == 0 && Services.Objects.LocalPlayer is { } me)
        {
            var (job, role) = data.Job(me.ClassJob.RowId);
            list.Add(new Partier(me.EntityId, job, role));
        }
        return list;
    }

    private void Finish(Outcome how, bool stopped = false)
    {
        var b = current;
        var follow = followOnly;
        current = null;
        followOnly = false;
        hooks.Disable();
        if (b == null) return;
        Tell(PullEnded);
        var t = Elapsed;
        clock.Stop();
        casting.Clear();
        outcome = null;
        // Only followed: let go of, never kept.
        if (follow) return;
        if (!b.Worthwhile(t))
        {
            NotKept = (t < RecordingBuilder.ShortestKept ? "That pull was too short to keep." : "No mechanics went off in that pull, so it wasn't kept.", DateTime.UtcNow);
            return;
        }
        NotKept = null;
        // Ended without the game saying how: held a moment for its "duty complete".
        if (how == Outcome.Ended && !stopped)
        {
            held = (b, t, DateTime.UtcNow + HoldFor);
            return;
        }
        Keep(b, t, how);
    }

    /// <summary>A finished pull, written to the store.</summary>
    private void Keep(RecordingBuilder b, double t, Outcome how)
    {
        var recording = b.Finish(t, how);
        var phase = b.Phase;
        // Written on a worker (a long pull is a big file); the window hears of it back on the framework thread.
        // Kept track of, so unloading waits for it rather than leaving it half done.
        saving.RemoveAll(x => x.IsCompleted);
        saving.Add(Task.Run(() =>
        {
            try
            {
                var info = store.Save(recording);
                string? summary = null;
                try
                {
                    summary = PullSummary.Build(recording, phase).ToJsonString();
                }
                catch (Exception ex)
                {
                    Services.Log.Warning(ex, "Raid Planner couldn't sum the pull up");
                }
                if (disposed) return;
                Services.Framework.RunOnFrameworkThread(() => Guarded(() =>
                {
                    // Unloaded meanwhile: no one's listening any more.
                    if (disposed) return;
                    Kept?.Invoke(info);
                    if (summary != null) Summarised?.Invoke(info, summary);
                }));
            }
            catch (Exception ex)
            {
                Services.Log.Error(ex, "Raid Planner couldn't save the pull");
                if (!disposed) Services.Framework.RunOnFrameworkThread(() => NotKept = ("The pull couldn't be saved (see /xllog).", DateTime.UtcNow));
            }
        }));
    }

    // ---------------------------------------------------------------------------------------------
    // Every frame while a pull goes.

    private void Sample(double t)
    {
        var b = current!;
        var frame = b.WantsFrame(t) ? new Dictionary<int, (float, float, float)>() : null;
        var tethers = tetherBuffer;
        tethers.Clear();
        b.BeginStatuses();
        foreach (var obj in Services.Objects)
        {
            if (obj == null || !obj.IsValid() || obj.EntityId is 0 or InvalidId) continue;
            var key = (ulong)obj.EntityId;
            int id;
            var member = false;
            if (b.PartyId(key) is { } p)
            {
                id = p;
                member = true;
            }
            else if (obj is IBattleNpc npc && npc.BattleNpcKind == BattleNpcSubKind.Combatant)
            {
                // Its name read only while it's needed (a new enemy, or one still nameless): reading it makes a string.
                id = b.Actor(key, b.NeedsName(key) ? npc.Name.TextValue : string.Empty, npc.HitboxRadius, npc.BaseId);
                var targetable = npc.IsTargetable && !npc.IsDead;
                if (targetable) b.SeenTargetable(id, npc.MaxHp);
                b.Targetable(t, id, targetable);
                ReadCast(b, t, id, npc);
            }
            else continue;

            if (frame != null) frame[id] = (obj.Position.X, obj.Position.Z, obj.Rotation);
            if (obj is ICharacter)
            {
                var chara = (Character*)obj.Address;
                if (chara == null) continue;
                foreach (var tether in chara->Vfx.Tethers)
                {
                    if (tether.Id == 0) continue;
                    var to = b.Who(tether.TargetId.ObjectId);
                    if (to != 0) tethers.Add((id, to, tether.Id));
                }
                if (member)
                {
                    b.Alive(t, id, chara->CharacterData.Health == 0 && chara->CharacterData.MaxHealth > 0);
                    if (obj is IBattleChara) ReadStatuses(b, t, id, (BattleChara*)chara);
                }
            }
        }
        b.EndStatuses(t);
        b.EndTargets(t);
        if (frame != null) b.Positions(t, frame);
        b.Tethers(t, tethers);
        if (t >= nextWaymarks)
        {
            nextWaymarks = t + 1;
            ReadWaymarks(b, t);
        }
    }

    /// <summary>A cast that's begun since the last frame: recorded once, from when it began.</summary>
    private void ReadCast(RecordingBuilder b, double t, int id, IBattleNpc npc)
    {
        var bc = (BattleChara*)npc.Address;
        if (bc == null) return;
        var cast = bc->GetCastInfo();
        if (cast == null || !cast->IsCasting || cast->ActionType != 1 || cast->ActionId == 0)
        {
            casting.Remove(id);
            return;
        }
        var start = t - cast->CurrentCastTime;
        if (casting.TryGetValue(id, out var had) && had.Action == cast->ActionId && Math.Abs(had.Start - start) < 0.3) return;
        casting[id] = (cast->ActionId, start);
        var target = cast->TargetId.ObjectId is var tid and not 0 and not 0xE0000000 ? b.Who(tid) : 0;
        var loc = cast->TargetLocation;
        var aimed = loc.X != 0 || loc.Z != 0;
        b.Cast(Math.Max(0, start), id, cast->ActionId, cast->TotalCastTime, npc.Position.X, npc.Position.Z, cast->Rotation, target,
            aimed ? loc.X : null, aimed ? loc.Z : null);
        if (CastBegan is { } listeners)
        {
            try
            {
                listeners(cast->ActionId, t, cast->TotalCastTime - cast->CurrentCastTime);
            }
            catch (Exception ex)
            {
                ListenerFailed(ex);
            }
        }
    }

    /// <summary>
    /// A party member's statuses this frame: each new one put on them by an enemy, a helper or no one
    /// is recorded (the party's own are only remembered, so they're not looked at again), and those
    /// gone are recorded as gone. Only a new one's source is looked into.
    /// </summary>
    private static void ReadStatuses(RecordingBuilder b, double t, int on, BattleChara* chara)
    {
        // The field itself, not the virtual GetStatusManager (never a call through the game's table).
        var sm = &chara->StatusManager;
        b.StatusesOf(on);
        var list = sm->Status;
        for (var i = 0; i < list.Length; i++)
        {
            var id = (uint)list[i].StatusId;
            if (id == 0 || b.StatusKnown(on, id)) continue;
            var (keep, source) = StatusSource(b, list[i].SourceObject.ObjectId);
            b.StatusGained(t, on, id, source, list[i].RemainingTime, keep);
        }
    }

    /// <summary>
    /// Who put a status on: no one (kept, source 0), the party (not kept), an enemy or a helper (kept,
    /// its number), or another player or a pet (not kept). One gone from the game already is no one.
    /// </summary>
    private static (bool Keep, int Source) StatusSource(RecordingBuilder b, uint src)
    {
        if (src is 0 or InvalidId) return (true, 0);
        if (b.IsParty(src)) return (false, 0);
        if (b.KnownActor(src) is { } known) return (true, known);
        var obj = Services.Objects.SearchByEntityId(src);
        if (obj == null) return (true, 0);
        if (obj is IBattleNpc npc && npc.BattleNpcKind is BattleNpcSubKind.Combatant or BattleNpcSubKind.BNpcPart && npc.OwnerId is 0 or InvalidId)
            return (true, b.Actor(src, npc.Name.TextValue, npc.HitboxRadius, npc.BaseId));
        return (false, 0);
    }

    private static readonly string[] MarkNames = WaymarksEvent.Names;

    private static void ReadWaymarks(RecordingBuilder b, double t)
    {
        var marking = MarkingController.Instance();
        if (marking == null) return;
        var marks = new Dictionary<string, (float, float)>();
        var markers = marking->FieldMarkers;
        for (var i = 0; i < markers.Length && i < MarkNames.Length; i++)
            if (markers[i].Active) marks[MarkNames[i]] = (markers[i].Position.X, markers[i].Position.Z);
        b.Waymarks(t, marks);
    }

    // ---------------------------------------------------------------------------------------------
    // From the hooks (on the framework thread).

    /// <summary>An action landed: an enemy's (a party member's own aren't kept).</summary>
    internal void OnHit(uint casterId, Character* caster, float hitX, float hitZ, uint action, IReadOnlyList<uint> targetIds)
    {
        if (disposed) return;
        var b = current;
        if (b == null || casterId is 0 or InvalidId || b.IsParty(casterId)) return;
        var src = b.KnownActor(casterId);
        if (src == null)
        {
            // A helper never seen in the object table (it came and went between frames): an enemy's,
            // never a pet or a summon (theirs are the party's own actions).
            if (caster == null || caster->GameObject.ObjectKind != FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.BattleNpc) return;
            var kind = (BattleNpcSubKind)caster->GameObject.SubKind;
            if (kind is not (BattleNpcSubKind.Combatant or BattleNpcSubKind.BNpcPart)) return;
            if (caster->GameObject.OwnerId is not (0 or InvalidId)) return;
            src = b.Actor(casterId, string.Empty, caster->GameObject.HitboxRadius, caster->GameObject.BaseId);
        }
        float x = hitX, z = hitZ, rot = 0;
        if (caster != null)
        {
            x = caster->GameObject.Position.X;
            z = caster->GameObject.Position.Z;
            rot = caster->GameObject.Rotation;
        }
        var targets = new List<int>(targetIds.Count);
        foreach (var tid in targetIds)
            if (b.PartyId(tid) is { } p) targets.Add(p);
        var t = Elapsed;
        b.Hit(t, src.Value, action, x, z, rot, hitX, hitZ, targets);
        if (HitLanded is { } listeners)
        {
            try
            {
                listeners(action, t);
            }
            catch (Exception ex)
            {
                ListenerFailed(ex);
            }
        }
    }

    /// <summary>A head marker appeared over someone.</summary>
    internal void OnMarker(uint entityId, uint icon)
    {
        if (disposed) return;
        var b = current;
        if (b == null) return;
        var on = b.Who(entityId);
        if (on != 0) b.Marker(Elapsed, on, icon);
    }

    /// <summary>The arena changed.</summary>
    internal void OnMapEffect(byte index, uint state)
    {
        if (!disposed) current?.MapEffect(Elapsed, index, state);
    }

    /// <summary>A pull is going on, recorded or followed (the hooks read nothing otherwise).</summary>
    internal bool Listening => current != null && !disposed;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            // The game's functions are let go of first, whatever happens after.
            Guarded(hooks.Dispose);
            Guarded(() => Services.Framework.Update -= OnUpdate);
            Guarded(() => Services.DutyState.DutyWiped -= OnWiped);
            Guarded(() => Services.DutyState.DutyCompleted -= OnCompleted);
            Guarded(() => Services.ClientState.TerritoryChanged -= OnTerritoryChanged);
        }
        finally
        {
            // Unloaded (an update, a reload) mid-pull, or just after one: what was recorded is kept, written now,
            // and the pulls still being written are waited for (a few seconds at most).
            SaveNow(held is { } h ? (h.B, h.T) : null);
            held = null;
            if (current is { } b)
            {
                current = null;
                // A pull only followed is never kept.
                if (!followOnly) SaveNow((b, Elapsed));
                followOnly = false;
            }
            try
            {
                Task.WaitAll(saving.ToArray(), TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // A save that failed has said so already.
            }
        }
    }

    private void SaveNow((RecordingBuilder B, double T)? pull)
    {
        if (pull is not { } p) return;
        try
        {
            if (p.B.Worthwhile(p.T)) store.Save(p.B.Finish(p.T, Outcome.Ended));
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Raid Planner couldn't save the pull while unloading");
        }
    }
}
