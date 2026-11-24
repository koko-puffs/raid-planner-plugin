using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.Game;

/// <summary>
/// The meter's game side: with Show the meter on (in duties, or everywhere: Settings), it listens to
/// the recorder's combat parser (<see cref="FightRecorder.Combat"/>, which asks the hooks to stay on
/// for it), and each frame tells its session (<see cref="MeterSession"/>, the window's
/// <see cref="Source"/>) where the party is, who's in it (names read once each, kept only in the
/// plugin), their HP and whether anyone's in combat, the recorder's pull, and four times a second
/// during an encounter which enemies can be targeted (the phases). The duty's wipe and clear, a zone
/// change and logging out end an encounter. On the framework thread.
///
/// Since 0.5.0 the party is <see cref="PartyMembers"/>'s: a cross-world party outside a duty counts (before, only you did).
///
/// It turns itself off rather than show nonsense: when the parser has dropped three effects that make
/// no sense (<see cref="CombatParser.Anomalies"/>) in one stretch while the meter listens, the game has
/// probably moved something it reads; the session says so (<see cref="MeterSession.Problem"/>). The
/// stretches and how long a stop lasts (until the next zone; the third for good) are <see cref="AnomalyGuard"/>'s.
///
/// The limit break gauge is read for the cooldown tracker too (<see cref="GaugeNow"/>), whether or not
/// the meter's on.
///
/// Since 0.4.0 it also tells the session the limit break gauge each frame, which party members carry
/// Weakness or Brink of Death (during an encounter), and, four times a second with the targetability,
/// the enemies' casts (whether they can be interrupted) and the bosses' targets (whether they're casting at
/// them); and every cast the game says stopped (<see cref="FightRecorder.CastStopped"/>).
/// </summary>
public sealed unsafe class CombatMeter : IDisposable
{
    /// <summary>How often the enemies' targetability is read during an encounter (seconds).</summary>
    private const double TargetsEvery = 0.25;

    private const uint InvalidId = 0xE0000000;

    private readonly Configuration config;
    private readonly GameData data;
    private readonly FightRecorder recorder;
    private readonly PartyState[] party = new PartyState[24];
    /// <summary>Party members' names, jobs and roles by entity id, with the ClassJob they were read for (a job changed is read again).</summary>
    private readonly Dictionary<uint, (uint ClassJob, string Name, string Job, byte Role)> known = new();
    private readonly Dictionary<uint, string> places = new();
    private bool listening;
    private double nextTargets;
    private bool disposed;
    private DateTime failedAt;
    private readonly AnomalyGuard odd = new();
    private bool wasInEncounter;

    public CombatMeter(Configuration config, GameData data, FightRecorder recorder)
    {
        this.config = config;
        this.data = data;
        this.recorder = recorder;
        Session = new MeterSession(data.ActionName, data.StatusName, data.ItemName, data.Kinds);
        recorder.CastStopped += OnCastStopped;
        Services.Framework.Update += OnUpdate;
        Services.DutyState.DutyWiped += OnWiped;
        Services.DutyState.DutyCompleted += OnCompleted;
        Services.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    /// <summary>The session the meter's window reads.</summary>
    public MeterSession Session { get; }

    public IMeterSource Source => Session;

    private static bool InDuty =>
        Services.Condition[ConditionFlag.BoundByDuty] || Services.Condition[ConditionFlag.BoundByDuty56] || Services.Condition[ConditionFlag.BoundByDuty95];

    private bool Wanted => (config.ShowMeter || AlsoWanted?.Invoke() == true) && (config.MeterEverywhere || InDuty) && Session.Problem == null;

    /// <summary>
    /// Since 0.9.0: something else reads the meter's numbers with the meter's window off (the death recap, set by the plugin): it
    /// listens all the same, where the meter would (in duties, or everywhere).
    /// </summary>
    public Func<bool>? AlsoWanted { get; set; }

    /// <summary>Since 0.9.0: which statuses are the death recap's mitigations (set by the plugin); null, none are read.</summary>
    public PrepData? Prep { get; set; }

    private void OnWiped(Dalamud.Game.DutyState.IDutyStateEventArgs _) => Guarded(() => Session.Said(Core.Outcome.Wipe));

    private void OnCompleted(Dalamud.Game.DutyState.IDutyStateEventArgs _) => Guarded(() => Session.Said(Core.Outcome.Clear));

    private void OnCastStopped(uint entity, uint action, bool interrupted)
    {
        if (disposed || !listening) return;
        try
        {
            Session.Cancelled(recorder.Now, entity, action, interrupted);
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    private void OnTerritoryChanged(uint territory) => Guarded(() =>
    {
        Session.Stop();
        recorder.Combat.Forget();
        owners.Clear();
        // A stop that isn't for good lasts until here: the meter tries again in the new zone.
        if (odd.NewZone(recorder.Combat.Anomalies)) Session.Problem = null;
    });

    private void Guarded(Action action)
    {
        if (disposed) return;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    private void Failed(Exception ex)
    {
        if (DateTime.UtcNow - failedAt < TimeSpan.FromSeconds(10)) return;
        failedAt = DateTime.UtcNow;
        Services.Log.Error(ex, "Raid Codex's meter couldn't read this frame");
    }

    /// <summary>Every frame: <see cref="Update"/>, guarded (not through <see cref="Guarded"/>, which would make a delegate each frame).</summary>
    private void OnUpdate(IFramework framework)
    {
        if (disposed) return;
        var started = Stalls.Start();
        try
        {
            Update();
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
        Stalls.End("the meter's frame", started);
    }

    private void Update()
    {
        if (!Services.ClientState.IsLoggedIn || Services.Objects.LocalPlayer is not { } me || !Wanted)
        {
            Detach();
            return;
        }
        if (!listening)
        {
            recorder.Combat.Add(Session);
            listening = true;
            // Only nonsense while the meter listens counts against it (the recorder's parser runs without it too).
            odd.Restart(recorder.Combat.Anomalies);
        }
        recorder.MeterWants = true;
        if (odd.Check(recorder.Combat.Anomalies))
        {
            Session.Stop();
            Session.Problem = odd.ForGood
                ? "The meter has stopped: what it reads from the game made no sense (a hit or a heal too big to be real), "
                    + "so the game has probably moved something. An update to the plugin should bring it back."
                : "The meter has stopped: what it reads from the game made no sense (a hit or a heal too big to be real). "
                    + "It tries again in the next zone.";
            Detach();
            return;
        }
        Session.Keep = Math.Clamp(config.MeterKeep, 1, 60);
        Session.EndAfter = Math.Clamp(config.MeterEndAfter, 1, 30);

        var territory = Services.ClientState.TerritoryType;
        var duty = data.Duty(territory);
        Session.Place(territory, duty?.Content ?? 0, duty?.Name ?? Place(territory));
        Session.RecordedPull(recorder.PullNumber);

        var count = 0;
        var anyInCombat = Services.Condition[ConditionFlag.InCombat];
        // The party (since 0.5.0 a cross-world party's group too, outside a duty: PartyMembers), each with a known entity id.
        foreach (var m in PartyMembers.Read())
        {
            if (m.EntityId is 0 or InvalidId || count >= party.Length) continue;
            var (name, job, role) = Who(m.EntityId, m.ClassJob, m.Name);
            var (weak, mits, read) = Statuses(m.EntityId, Session.InEncounter);
            anyInCombat |= m.InCombat;
            party[count++] = new PartyState(m.EntityId, name, job, role, m.Hp, m.MaxHp, m.InCombat, m.IsYou, weak, mits, read);
        }
        if (count == 0)
        {
            var (name, job, role) = Who(me.EntityId, me.ClassJob.RowId, null);
            var (weak, mits, read) = Statuses(me.EntityId, Session.InEncounter);
            party[count++] = new PartyState(me.EntityId, name, job, role, me.CurrentHp, me.MaxHp, anyInCombat, true, weak, mits, read);
        }
        ReadGauge();
        var t = recorder.Now;
        Session.Frame(t, party.AsSpan(0, count), anyInCombat);
        // An encounter's end (here, or a wipe or a clear since the last frame) begins a new stretch.
        if (wasInEncounter && !Session.InEncounter) odd.Restart(recorder.Combat.Anomalies);
        wasInEncounter = Session.InEncounter;

        if (Session.InEncounter && t >= nextTargets)
        {
            nextTargets = t + TargetsEvery;
            ReportTargets();
        }
    }

    /// <summary>
    /// The limit break gauge now, for the cooldown tracker (asked as it draws, while it shows the gauge): the one the
    /// meter read this frame while it's on, else read here, so it keeps moving while the meter is off (outside a duty
    /// without Everywhere, turned off, or stopped) without the meter listening again.
    /// </summary>
    public LimitBreakGauge? GaugeNow()
    {
        if (disposed) return null;
        if (listening) return Session.LimitBreak;
        try
        {
            ReadGauge();
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
        return Session.LimitBreak;
    }

    /// <summary>The game's limit break gauge into the session (none when logged out).</summary>
    private void ReadGauge()
    {
        if (!Services.ClientState.IsLoggedIn)
        {
            Session.Gauge(0, 0, 0);
            return;
        }
        var lb = LimitBreakController.Instance();
        if (lb != null) Session.Gauge(lb->CurrentUnits, lb->BarCount, lb->BarUnits);
    }

    /// <summary>
    /// A party member's name, job and role, kept the first time they're seen and again when their job
    /// changes (outside a duty, with the meter everywhere). <paramref name="name"/> null: yours, read here.
    /// </summary>
    private (string Name, string Job, byte Role) Who(uint entityId, uint classJob, string? name)
    {
        if (known.TryGetValue(entityId, out var was) && was.ClassJob == classJob && was.Job.Length > 0) return (was.Name, was.Job, was.Role);
        name ??= Services.Objects.LocalPlayer is { } local && local.EntityId == entityId ? local.Name.TextValue : string.Empty;
        var (job, role) = data.Job(classJob);
        if (known.Count > 256) known.Clear();
        known[entityId] = (classJob, name, job, role);
        return (name, job, role);
    }

    /// <summary>
    /// During an encounter, from a party member's status list: Weakness (1) or Brink of Death (2) on them, and (since 0.9.0) the
    /// mitigations on them: as a <see cref="Mitigations"/> mask for the death recap, and each with who put it on for the
    /// Mitigation tab and shields absorbed; nothing read when they aren't near.
    /// </summary>
    private (byte Weakness, ulong Mitigation, bool Read) Statuses(uint entityId, bool statuses)
    {
        if (!statuses) return (0, 0, false);
        var manager = CharacterManager.Instance();
        if (manager == null) return (0, 0, false);
        var bc = manager->LookupBattleCharaByEntityId(entityId);
        if (bc == null) return (0, 0, false);
        byte weak = 0;
        ulong mits = 0;
        var prep = Prep;
        Session.Wearing(entityId);
        var list = bc->StatusManager.Status;
        for (var i = 0; i < list.Length; i++)
        {
            var id = (uint)list[i].StatusId;
            if (id == 0) continue;
            if (data.Kinds.Weakness(id) is var w and > 0) weak = (byte)Math.Max(weak, w);
            if (prep != null) mits |= Mitigations.Bit(prep.MitigationIndex(id));
            if (data.Kinds.Mitigation(id) != MitigationKind.None) Session.Wears(entityId, id, MemberFrom(list[i].SourceObject.ObjectId));
        }
        return (weak, mits, prep != null);
    }

    /// <summary>Since 0.9.0: the party's mitigations on an enemy (Reprisal, Addle, Feint, Dismantle), from its status list.</summary>
    private ulong EnemyMitigation(BattleChara* bc)
    {
        if (Prep is not { } prep || bc == null) return 0;
        ulong mits = 0;
        var list = bc->StatusManager.Status;
        for (var i = 0; i < list.Length; i++)
            if (list[i].StatusId != 0)
                mits |= Mitigations.Bit(prep.MitigationIndex(list[i].StatusId));
        return mits & Mitigations.OnEnemy;
    }

    /// <summary>The party's debuffs on an enemy that lower its damage (Reprisal, Addle, Feint, Dismantle), with who put each on (since 0.9.0).</summary>
    private void EnemyStatuses(uint entityId, BattleChara* bc)
    {
        Session.EnemyWearing(entityId);
        var list = bc->StatusManager.Status;
        for (var i = 0; i < list.Length; i++)
        {
            var id = (uint)list[i].StatusId;
            if (id != 0 && data.Kinds.Mitigation(id) == MitigationKind.Enemy) Session.EnemyWears(entityId, id, MemberFrom(list[i].SourceObject.ObjectId));
        }
    }

    /// <summary>The party member a status's source stands for: themselves, or a pet's or summon's owner (looked up once and kept); 0 for none.</summary>
    private uint MemberFrom(uint source)
    {
        if (source is 0 or InvalidId) return 0;
        var parser = recorder.Combat;
        if (parser.IsParty(source)) return source;
        if (!owners.TryGetValue(source, out var owner))
        {
            var manager = CharacterManager.Instance();
            var bc = manager == null ? null : manager->LookupBattleCharaByEntityId(source);
            owner = bc == null ? 0 : bc->Character.GameObject.OwnerId;
            if (owner == InvalidId) owner = 0;
            if (owners.Count > 256) owners.Clear();
            owners[source] = owner;
        }
        return parser.IsParty(owner) ? owner : 0;
    }

    private readonly Dictionary<uint, uint> owners = new();

    private string Place(uint territory)
    {
        if (places.TryGetValue(territory, out var name)) return name;
        name = data.PlaceName(territory);
        places[territory] = name;
        return name;
    }

    /// <summary>
    /// The enemies' targetability, for the phases: in a duty that isn't a field zone every enemy (as the
    /// recorder reads them), else only those the party has fought this encounter (a field zone's or the
    /// open world's other mobs aren't the party's fight).
    /// </summary>
    private void ReportTargets()
    {
        var all = InDuty && !data.IsFieldZone(Services.ClientState.TerritoryType);
        var t = recorder.Now;
        var biggest = 0u;
        ulong onEnemies = 0;
        bosses.Clear();
        foreach (var obj in Services.Objects)
        {
            if (obj is not IBattleNpc npc || npc.BattleNpcKind is not (BattleNpcSubKind.Combatant or BattleNpcSubKind.BNpcPart)) continue;
            if (npc.OwnerId is not (0 or InvalidId) || npc.EntityId is 0 or InvalidId) continue;
            if (!all && !Session.Engaged(npc.EntityId)) continue;
            var targetable = npc.IsTargetable && !npc.IsDead;
            Session.Targetable(npc.EntityId, targetable);
            if (!targetable) continue;
            // Its cast (since 0.4.0), and, for the bosses below, whom it's on.
            var bc = (BattleChara*)npc.Address;
            onEnemies |= EnemyMitigation(bc);
            if (bc != null) EnemyStatuses(npc.EntityId, bc);
            var cast = bc == null ? null : bc->GetCastInfo();
            uint castAt = 0;
            if (cast != null && cast->IsCasting && cast->ActionType == 1 && cast->ActionId != 0)
            {
                Session.EnemyCast(t - cast->CurrentCastTime, npc.EntityId, cast->ActionId, cast->TotalCastTime, cast->Interruptible);
                castAt = (uint)cast->TargetId.ObjectId;
            }
            biggest = Math.Max(biggest, npc.MaxHp);
            bosses.Add((npc.EntityId, npc.MaxHp, (uint)npc.TargetObjectId, castAt));
        }
        Session.EndTargets();
        Session.EnemyMitigation(onEnemies);
        // The bosses (as the recording sorts them: half the biggest's HP or more): whom each is on.
        foreach (var (id, hp, on, castAt) in bosses)
            if ((ulong)hp * 2 >= biggest)
                Session.Aggro(t, id, on is InvalidId ? 0 : on, on != 0 && on == castAt);
    }

    private readonly List<(uint Id, uint MaxHp, uint On, uint CastAt)> bosses = new();

    private void Detach()
    {
        recorder.MeterWants = false;
        if (!listening) return;
        listening = false;
        recorder.Combat.Remove(Session);
        Session.Stop();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            Services.Framework.Update -= OnUpdate;
            Services.DutyState.DutyWiped -= OnWiped;
            Services.DutyState.DutyCompleted -= OnCompleted;
            Services.ClientState.TerritoryChanged -= OnTerritoryChanged;
            recorder.CastStopped -= OnCastStopped;
        }
        finally
        {
            recorder.MeterWants = false;
            if (listening) recorder.Combat.Remove(Session);
            listening = false;
        }
    }
}
