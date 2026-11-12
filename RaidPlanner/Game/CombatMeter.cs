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
/// no sense (<see cref="CombatParser.Anomalies"/>), the game has moved something it reads; the session
/// says so (<see cref="MeterSession.Problem"/>) until the plugin is updated.
///
/// Since 0.4.0 it also tells the session the limit break gauge each frame, which party members carry
/// Weakness or Brink of Death (during an encounter), and, four times a second with the targetability,
/// the enemies' casts (whether they can be interrupted) and the bosses' targets (whether they're casting at
/// them); and every cast the game says stopped (<see cref="FightRecorder.CastStopped"/>).
/// </summary>
public sealed unsafe class CombatMeter : IDisposable
{
    /// <summary>Anomalies before the meter stops.</summary>
    private const int TooOdd = 3;

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

    private bool Wanted => config.ShowMeter && (config.MeterEverywhere || InDuty) && Session.Problem == null;

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
        Services.Log.Error(ex, "Raid Planner's meter couldn't read this frame");
    }

    /// <summary>Every frame: <see cref="Update"/>, guarded (not through <see cref="Guarded"/>, which would make a delegate each frame).</summary>
    private void OnUpdate(IFramework framework)
    {
        if (disposed) return;
        try
        {
            Update();
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
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
        }
        recorder.MeterWants = true;
        if (recorder.Combat.Anomalies >= TooOdd)
        {
            Session.Stop();
            Session.Problem = "The meter has stopped: what it reads from the game made no sense (a hit or a heal too big to be real), "
                + "so the game has probably moved something. An update to the plugin should bring it back.";
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
            var weak = Weakness(m.EntityId, Session.InEncounter);
            anyInCombat |= m.InCombat;
            party[count++] = new PartyState(m.EntityId, name, job, role, m.Hp, m.MaxHp, m.InCombat, m.IsYou, weak);
        }
        if (count == 0)
        {
            var (name, job, role) = Who(me.EntityId, me.ClassJob.RowId, null);
            var weak = Weakness(me.EntityId, Session.InEncounter);
            party[count++] = new PartyState(me.EntityId, name, job, role, me.CurrentHp, me.MaxHp, anyInCombat, true, weak);
        }
        var lb = LimitBreakController.Instance();
        if (lb != null) Session.Gauge(lb->CurrentUnits, lb->BarCount, lb->BarUnits);
        var t = recorder.Now;
        Session.Frame(t, party.AsSpan(0, count), anyInCombat);

        if (Session.InEncounter && t >= nextTargets)
        {
            nextTargets = t + TargetsEvery;
            ReportTargets();
        }
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

    /// <summary>During an encounter, Weakness (1) or Brink of Death (2) on a party member, from their status list (0 when they aren't near).</summary>
    private byte Weakness(uint entityId, bool statuses)
    {
        if (!statuses) return 0;
        var manager = CharacterManager.Instance();
        if (manager == null) return 0;
        var bc = manager->LookupBattleCharaByEntityId(entityId);
        if (bc == null) return 0;
        byte weak = 0;
        var list = bc->StatusManager.Status;
        for (var i = 0; i < list.Length; i++)
            if (list[i].StatusId != 0 && data.Kinds.Weakness(list[i].StatusId) is var w and > 0)
                weak = (byte)Math.Max(weak, w);
        return weak;
    }

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
