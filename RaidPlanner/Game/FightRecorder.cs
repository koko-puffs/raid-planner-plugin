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
/// character's own), the waymarks once a second, and where everyone is twice a second; the hits
/// and head markers come from <see cref="GameHooks"/>. At the end, a pull worth keeping (long
/// enough, something happened) is written to the store on a worker thread.
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

    /// <summary>The pull being recorded, or null.</summary>
    public RecordingBuilder? Current => current;

    public bool Recording => current != null;

    /// <summary>Seconds since the pull began.</summary>
    public double Elapsed => clock.Elapsed.TotalSeconds;

    /// <summary>A kept pull (on the framework thread, once it's written).</summary>
    public event Action<RecordingInfo>? Kept;

    /// <summary>What became of the last pull that wasn't kept, for the window ("Too short to keep"), with when.</summary>
    public (string Why, DateTime At)? NotKept { get; private set; }

    /// <summary>In a duty right now (where a pull would be recorded): its name, or null.</summary>
    public DutyInfo? DutyHere => InDuty ? data.Duty(Services.ClientState.TerritoryType) : null;

    private static bool InDuty =>
        Services.Condition[ConditionFlag.BoundByDuty] || Services.Condition[ConditionFlag.BoundByDuty56] || Services.Condition[ConditionFlag.BoundByDuty95];

    /// <summary>Ends the pull now and keeps it (the window's Stop, /raidplan stop).</summary>
    public void Stop() => stopAsked = true;

    private void OnWiped(Dalamud.Game.DutyState.IDutyStateEventArgs _) => Guarded(() => Said(Outcome.Wipe));

    private void OnCompleted(Dalamud.Game.DutyState.IDutyStateEventArgs _) => Guarded(() => Said(Outcome.Clear));

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
            if (current != null) Finish(outcome ?? Outcome.Ended);
            return;
        }
        var combat = Services.Condition[ConditionFlag.InCombat];
        if (current == null)
        {
            stopAsked = false;
            if (!combat && !FightGoesOn()) waitForCombatToEnd = false;
            if (config.AutoRecord && combat && !waitForCombatToEnd && RecordsHere()) Start();
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

    private void Start()
    {
        ReleaseHeld();
        var territory = Services.ClientState.TerritoryType;
        if (territory != pullTerritory)
        {
            pullTerritory = territory;
            pullInZone = 0;
        }
        pullInZone++;
        var duty = data.Duty(territory) ?? new DutyInfo(data.PlaceName(territory), territory, 0);
        current = new RecordingBuilder(duty, pullInZone, DateTimeOffset.Now, config.RecordParty, version, data.Action);
        current.SetParty(Party());
        casting.Clear();
        outcome = null;
        outOfCombat = 0;
        nextWaymarks = 0;
        stopAsked = false;
        clock.Restart();
        hooks.Enable();
        Sample(0);
        Services.Log.Information($"Recording {duty.Name}, pull {pullInZone}");
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
        current = null;
        hooks.Disable();
        if (b == null) return;
        var t = Elapsed;
        clock.Stop();
        casting.Clear();
        outcome = null;
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
        // Written on a worker (a long pull is a big file); the window hears of it back on the framework thread.
        // Kept track of, so unloading waits for it rather than leaving it half done.
        saving.RemoveAll(x => x.IsCompleted);
        saving.Add(Task.Run(() =>
        {
            try
            {
                var info = store.Save(recording);
                Services.Framework.RunOnFrameworkThread(() => Kept?.Invoke(info));
            }
            catch (Exception ex)
            {
                Services.Log.Error(ex, "Raid Planner couldn't save the pull");
                Services.Framework.RunOnFrameworkThread(() => NotKept = ("The pull couldn't be saved (see /xllog).", DateTime.UtcNow));
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
        foreach (var obj in Services.Objects)
        {
            if (obj == null || !obj.IsValid() || obj.EntityId is 0 or InvalidId) continue;
            var key = (ulong)obj.EntityId;
            int id;
            if (b.PartyId(key) is { } p) id = p;
            else if (obj is IBattleNpc npc && npc.BattleNpcKind == BattleNpcSubKind.Combatant)
            {
                // Its name read only while it's needed (a new enemy, or one still nameless): reading it makes a string.
                id = b.Actor(key, b.NeedsName(key) ? npc.Name.TextValue : string.Empty, npc.HitboxRadius, npc.BaseId);
                if (npc.IsTargetable && !npc.IsDead) b.SeenTargetable(id, npc.MaxHp);
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
            }
        }
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
        b.Hit(Elapsed, src.Value, action, x, z, rot, hitX, hitZ, targets);
    }

    /// <summary>A head marker appeared over someone.</summary>
    internal void OnMarker(uint entityId, uint icon)
    {
        var b = current;
        if (b == null) return;
        var on = b.Who(entityId);
        if (on != 0) b.Marker(Elapsed, on, icon);
    }

    /// <summary>The arena changed.</summary>
    internal void OnMapEffect(byte index, uint state) => current?.MapEffect(Elapsed, index, state);

    /// <summary>A pull is being recorded (the hooks read nothing otherwise).</summary>
    internal bool Listening => current != null;

    public void Dispose()
    {
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
                SaveNow((b, Elapsed));
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
