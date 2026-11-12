using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Client.Game;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.Game;
using RaidPlanner.UI.Model;

namespace RaidPlanner.UI;

/// <summary>
/// The cooldown tracker's game side. It listens to the recorder's combat parser (<c>ICombatSink.Used</c>:
/// every action a party member or their pet uses, as the game's message has it) for the cooldowns
/// <see cref="CooldownCatalog"/> lists, found by their English names on the Action sheet (read once on a
/// worker); each use goes into <see cref="Book"/>, which learns the others' recasts. Your own cooldowns are
/// read from the game's own timers each frame (<c>ActionManager</c>: exact, charges and all). The party is
/// the party list's (spots by <see cref="Roles"/>), read four times a second. A new zone forgets every
/// use, as does the duty starting again after a wipe (the game resets recasts then). The hooks are kept on
/// while it's wanted (<see cref="WantsHooks"/>: the plugin asks the recorder). Never throws into the game.
/// </summary>
public sealed unsafe class PartyCooldowns : ICombatSink, IDisposable
{
    private const uint InvalidId = 0xE0000000;

    private readonly GameData data;
    private readonly FightRecorder recorder;
    private readonly Task<CooldownRows> lookup;
    private readonly Dictionary<string, OwnCooldown> own = new();
    private readonly Dictionary<string, double> roleRecast = new();
    private readonly Dictionary<string, CooldownAction> named = new();
    private readonly List<CooldownMember> party = new();
    private readonly List<Partier> partiers = new();
    private readonly Dictionary<uint, int> levels = new();
    private bool listening;
    private bool disposed;
    private double nextParty;
    private DateTime failedAt;

    public PartyCooldowns(GameData data, FightRecorder recorder)
    {
        this.data = data;
        this.recorder = recorder;
        lookup = Task.Run(Read);
        Services.ClientState.TerritoryChanged += OnTerritory;
        Services.DutyState.DutyRecommenced += OnRecommenced;
    }

    /// <summary>The others' uses and what's learned from them.</summary>
    public CooldownBook Book { get; } = new();

    /// <summary>The party now, in the party list's order with their spots.</summary>
    public IReadOnlyList<CooldownMember> Party => party;

    /// <summary>The tracker is shown and wants the parser's uses (set by the window each frame).</summary>
    public bool Wanted { get; set; }

    /// <summary>The recorder's hooks should be on for it.</summary>
    public bool WantsHooks => Wanted && listening;

    /// <summary>The tracker's clock: the recorder's (the parser's times are on it).</summary>
    public double Now => recorder.Now;

    /// <summary>The sheet has been read.</summary>
    public bool Ready => lookup.IsCompletedSuccessfully;

    /// <summary>Each cooldown's rows on the Action sheet in English, and the row standing for it (<see cref="CooldownCatalog.Match"/>).</summary>
    private static CooldownRows Read()
    {
        try
        {
            return CooldownCatalog.Match(Rows());
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Raid Planner couldn't read the game's actions for the cooldown tracker");
            return new CooldownRows(new Dictionary<uint, string>(), new Dictionary<string, ActionRowInfo>(), new Dictionary<string, int>());
        }
    }

    private static IEnumerable<ActionRowInfo> Rows()
    {
        foreach (var row in Services.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>(ClientLanguage.English))
            yield return new ActionRowInfo(row.RowId, row.Name.ExtractText(), row.IsPlayerAction, row.IsPvP, row.ClassJobCategory.RowId, row.ClassJobLevel, row.Icon,
                row.Recast100ms / 10.0, Math.Max(1, (int)row.MaxCharges), row.IsRoleAction);
    }

    /// <summary>A cooldown as the game has it (its name in the client's language), or null while the sheet is read or when it has no row.</summary>
    public CooldownAction? Action(string key)
    {
        if (named.TryGetValue(key, out var a)) return a;
        if (!Ready || !lookup.Result.Rows.TryGetValue(key, out var r)) return null;
        a = new CooldownAction(key, r.Id, data.ActionName(r.Id), r.Icon, r.Recast, r.Charges, lookup.Result.Level(key));
        named[key] = a;
        return a;
    }

    /// <summary>The recast the others' uses are counted by: for a role action you have too, the game's for you (your role's traits at this level), else the sheet's.</summary>
    private double RecastFor(string key, CooldownAction a) => roleRecast.TryGetValue(key, out var r) && r > 0 ? r : a.Recast;

    /// <summary>One of your own cooldowns now (exact), or null when it isn't yours.</summary>
    public OwnCooldown? Own(string key) => own.TryGetValue(key, out var o) ? o : null;

    // ---------------------------------------------------------------------------------------------
    // The parser's listener (on the game's thread, from the hooks: cheap, and never throws).

    public void Used(double t, uint member, uint caster, uint action)
    {
        try
        {
            if (!Ready || !lookup.Result.KeyOf.TryGetValue(action, out var key)) return;
            if (Action(key) is not { } a) return;
            Book.Use(member, key, t, RecastFor(key, a), a.Charges);
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    public void Effect(in PartyEffect e)
    {
    }

    public void Taken(in TakenHit e)
    {
    }

    public void Tick(in TickEvent e, ReadOnlySpan<TickShare> shares)
    {
    }

    // ---------------------------------------------------------------------------------------------
    // Each frame (from the plugin's update).

    /// <summary>Listens while wanted; reads the party four times a second and your own cooldowns each frame.</summary>
    public void Update()
    {
        if (disposed) return;
        try
        {
            if (!Wanted || !Services.ClientState.IsLoggedIn || Services.Objects.LocalPlayer is not { } me)
            {
                Detach();
                return;
            }
            if (!listening)
            {
                recorder.Combat.Add(this);
                listening = true;
            }
            var now = recorder.Now;
            if (now >= nextParty || party.Count == 0)
            {
                nextParty = now + 0.25;
                ReadParty(me.EntityId, me.ClassJob.RowId, me.Level);
            }
            ReadOwn();
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    private void ReadParty(uint meId, uint myClassJob, int myLevel)
    {
        partiers.Clear();
        levels.Clear();
        foreach (var m in Services.Party)
        {
            if (m.EntityId is 0 or InvalidId) continue;
            var (job, role) = data.Job(m.ClassJob.RowId);
            partiers.Add(new Partier(m.EntityId, job, role));
            levels[m.EntityId] = m.Level;
        }
        if (partiers.Count == 0)
        {
            var (job, role) = data.Job(myClassJob);
            partiers.Add(new Partier(meId, job, role));
        }
        // Yours from your character (the level you're synced to).
        levels[meId] = myLevel;
        party.Clear();
        foreach (var (who, slot) in Roles.Assign(partiers))
            party.Add(new CooldownMember((uint)who.Key, slot, who.Job, who.Role, who.Key == meId, levels.TryGetValue((uint)who.Key, out var lv) ? lv : 0));
    }

    /// <summary>Your own tracked cooldowns, from the game's timers (and the recast your role's actions have for you, for the others').</summary>
    private void ReadOwn()
    {
        own.Clear();
        roleRecast.Clear();
        if (!Ready) return;
        var am = ActionManager.Instance();
        if (am == null) return;
        string? myJob = null;
        foreach (var m in party)
            if (m.IsYou)
                myJob = m.Job;
        if (myJob == null) return;
        foreach (var spec in CooldownCatalog.All)
        {
            if (!spec.For(myJob) || Action(spec.Key) is not { } a) continue;
            // The cooldown's own row, not what it turns into for a moment (Technical Step is Technical Finish while dancing;
            // a row replaced at a lower level, as Dokumori by Mug, shares its recast group) [verify].
            var id = a.Id;
            var maxCharges = Math.Max(1, (int)ActionManager.GetMaxCharges(id, 0));
            var total = am->GetRecastTime(ActionType.Action, id);
            // A charge's recast as the game works it out for you (traits at this level, in ms) [verify]; else the
            // timer's total over the charges (the total is for every charge at the top level), else the sheet's.
            var adjusted = ActionManager.GetAdjustedRecastTime(ActionType.Action, id, true) / 1000.0;
            var per = adjusted > 0 ? (float)adjusted : total > 0 ? total / maxCharges : (float)a.Recast;
            if (adjusted > 0 && lookup.Result.Rows.TryGetValue(spec.Key, out var r) && r.Role) roleRecast[spec.Key] = adjusted;
            if (!am->IsRecastTimerActive(ActionType.Action, id) || !(total > 0))
            {
                own[spec.Key] = new OwnCooldown(0, 0, maxCharges, maxCharges, per);
                continue;
            }
            var elapsed = am->GetRecastTimeElapsed(ActionType.Action, id);
            // One charge: on its recast, none (whatever the game's count says); more: the game's count.
            var charges = maxCharges <= 1 ? 0 : Math.Clamp((int)am->GetCurrentCharges(id), 0, maxCharges);
            if (maxCharges <= 1)
            {
                // One charge: the timer itself, to the hundredth.
                var left = Math.Max(0, total - elapsed);
                own[spec.Key] = new OwnCooldown(left, left, 0, 1, total);
                continue;
            }
            var next = charges >= maxCharges ? 0 : Math.Max(0, per - elapsed % per);
            own[spec.Key] = new OwnCooldown(charges > 0 ? 0 : next, next, charges, maxCharges, per);
        }
    }

    private void OnTerritory(uint _) => Book.Clear();

    private void OnRecommenced(Dalamud.Game.DutyState.IDutyStateEventArgs _) => Book.Clear();

    private void Detach()
    {
        if (!listening) return;
        listening = false;
        recorder.Combat.Remove(this);
    }

    private void Failed(Exception ex)
    {
        if (DateTime.UtcNow - failedAt < TimeSpan.FromSeconds(10)) return;
        failedAt = DateTime.UtcNow;
        Services.Log.Error(ex, "Raid Planner's cooldown tracker couldn't read this");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            Services.ClientState.TerritoryChanged -= OnTerritory;
            Services.DutyState.DutyRecommenced -= OnRecommenced;
        }
        finally
        {
            Detach();
        }
    }
}
