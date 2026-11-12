using System;
using System.Collections.Generic;
using Dalamud.Game;
using Lumina.Excel.Sheets;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using Action = Lumina.Excel.Sheets.Action;

namespace RaidPlanner.Game;

/// <summary>
/// What the game's own data says about actions, duties, jobs and statuses (each looked up once, kept).
/// On the framework thread, except the status lists (<see cref="FailKinds"/>, <see cref="DotIds"/>,
/// <see cref="ShieldIds"/>), made once and never changed after, which a worker may read.
/// </summary>
public sealed class GameData
{
    private readonly Dictionary<uint, ActionInfo?> actions = new();
    private readonly Dictionary<uint, DutyInfo?> duties = new();
    private readonly Dictionary<uint, (string Job, byte Role)> jobs = new();
    private readonly Dictionary<uint, bool> fights = new();
    private readonly Dictionary<uint, StatusInfo?> statuses = new();

    /// <summary>
    /// Duty kinds (the ContentType sheet's rows) that aren't one fight to plan: dungeons, guildhests,
    /// PvP, quest battles, treasure hunts, deep dungeons, Eureka, Bozja and the Occult Crescent (38).
    /// A trash pack there would otherwise be kept as a pull, and push out the ones you meant to keep.
    /// </summary>
    private static readonly HashSet<uint> NotFights = new() { 2, 3, 6, 7, 9, 21, 26, 29, 38 };

    /// <summary>The Occult Crescent's open zones (South and North Horn: territory intended use 61), never recorded.</summary>
    private const uint OccultUse = 61;

    /// <summary>
    /// An action's name, cast type (its shape), reach, width, floor marker and cast, as the game's
    /// Action sheet has them; null for one the sheet doesn't have.
    /// </summary>
    public ActionInfo? Action(uint id)
    {
        if (actions.TryGetValue(id, out var known)) return known;
        ActionInfo? info = null;
        if (Services.Data.GetExcelSheet<Action>().TryGetRow(id, out var row))
        {
            var omen = row.Omen.ValueNullable is { } o ? o.Path.ExtractText() : string.Empty;
            info = new ActionInfo(row.Name.ExtractText(), row.CastType, row.EffectRange, row.XAxisModifier, omen, row.Cast100ms / 10f,
                (int)row.ActionCategory.RowId, row.Recast100ms / 10f, row.CooldownGroup, row.AdditionalCooldownGroup, row.MaxCharges, 0, row.TargetArea);
        }
        actions[id] = info;
        return info;
    }

    /// <summary>An action's ActionCategory row (9 a limit break, 15 PvP's; 0 unknown): the combat parser's question.</summary>
    public int ActionCategory(uint id) => Action(id)?.Category ?? 0;

    /// <summary>Is an action placed on the ground (the sheet's TargetArea: Sacred Soil, Asylum, Earthly Star, Doton…)? Since 0.4.0.</summary>
    public bool IsArea(uint id) => Action(id)?.Area ?? false;

    /// <summary>Is an action on the GCD (its cooldown group, or its additional one, is 58)? Since 0.4.0.</summary>
    public bool IsGcd(uint id) => Action(id) is { } a && CombatLists.IsGcd(a.CooldownGroup, a.CooldownGroup2);

    private readonly Dictionary<uint, ItemInfo?> items = new();

    /// <summary>
    /// An item by the id the game sent in an action message (a high-quality one's is the Item sheet's row + 1,000,000 [verify]):
    /// its name, icon and quality; null for one the sheet doesn't have. Since 0.4.0.
    /// </summary>
    public ItemInfo? Item(uint id)
    {
        if (items.TryGetValue(id, out var known)) return known;
        var hq = id > CombatLists.HqOffset;
        var row = hq ? id - CombatLists.HqOffset : id;
        ItemInfo? info = null;
        if (Services.Data.GetExcelSheet<Item>().TryGetRow(row, out var it))
            info = new ItemInfo(it.Name.ExtractText(), it.Icon, hq);
        if (items.Count > 512) items.Clear();
        items[id] = info;
        return info;
    }

    /// <summary>An item's name for the meter ("Item 1234" for one the sheet doesn't name; " (HQ)" after a high-quality one's).</summary>
    public string ItemName(uint id) => Item(id) is { Name.Length: > 0 } i ? i.Hq ? $"{i.Name} (HQ)" : i.Name : $"Item {id}";

    /// <summary>
    /// The recording player's own recast for one of their actions (seconds) and its most charges, as the game works them out
    /// (ActionManager: their traits at the synced level [verify: milliseconds; charges at level 0 = the current]); null when
    /// the game has no answer. On the framework thread. Since 0.4.0.
    /// </summary>
    public unsafe (float Recast, int Charges)? OwnRecast(uint id)
    {
        // Only with a character in the world (a pull kept at the title screen, as you log out, asks nothing of the game).
        if (!Services.ClientState.IsLoggedIn || Services.Objects.LocalPlayer == null) return null;
        try
        {
            var ms = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.GetAdjustedRecastTime(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Action, id, true);
            var charges = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.GetMaxCharges(id, 0);
            if (ms <= 0 || ms > 3_600_000) return null;
            return (ms / 1000f, charges);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>An action's name for the meter ("Action 1234" for one the sheet doesn't name).</summary>
    public string ActionName(uint id) => Action(id) is { Name.Length: > 0 } a ? a.Name : $"Action {id}";

    /// <summary>A status's name for the meter ("Status 1234" for one the sheet doesn't name).</summary>
    public string StatusName(uint id) => Status(id) is { Name.Length: > 0 } st ? st.Name : $"Status {id}";

    private readonly Lazy<(Dictionary<uint, int> Fails, HashSet<uint> Dots, HashSet<uint> Shields)> lists = new(ReadLists);

    private readonly Lazy<Named> named = new(ReadNamed);

    /// <summary>The status lists made on a worker as the plugin loads, so the first fight doesn't wait for them.</summary>
    public GameData()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                _ = lists.Value;
                _ = named.Value;
            }
            catch (Exception)
            {
                // ReadLists and ReadNamed say themselves what went wrong (and never throw).
            }
        });
        Kinds = new CombatKinds
        {
            Interrupt = id => named.Value.Interrupts.Contains(id),
            RaiseAction = id => named.Value.Raises.Contains(id),
            RaiseStatus = id => named.Value.RaiseStatuses.Contains(id),
            Weakness = id => named.Value.Weakness.TryGetValue(id, out var w) ? w : 0,
            Control = id => named.Value.Control.Contains(id),
            KnockbackImmunity = id => named.Value.KnockbackImmunity.Contains(id),
            Provoke = id => named.Value.Provoke.Contains(id),
            Shirk = id => named.Value.Shirk.Contains(id),
            Gcd = IsGcd,
        };
    }

    /// <summary>
    /// Which actions and statuses are interrupts, raises, control and the rest (<see cref="CombatKinds"/>), by their English
    /// names (<see cref="CombatLists"/>): every row with each name on the Action and Status sheets read in English. The id sets
    /// are made once on a worker and then only read; <see cref="CombatKinds.Gcd"/> reads the Action sheet (the framework thread).
    /// Since 0.4.0.
    /// </summary>
    public CombatKinds Kinds { get; }

    private sealed record Named(HashSet<uint> Interrupts, HashSet<uint> Raises, HashSet<uint> Provoke, HashSet<uint> Shirk, HashSet<uint> RaiseStatuses,
        Dictionary<uint, int> Weakness, HashSet<uint> Control, HashSet<uint> KnockbackImmunity);

    private static Named ReadNamed()
    {
        var n = new Named(new(), new(), new(), new(), new(), new(), new(), new());
        static HashSet<string> Set(IReadOnlyList<string> names) => new(names, StringComparer.OrdinalIgnoreCase);
        var interrupts = Set(CombatLists.InterruptNames);
        var raises = Set(CombatLists.RaiseActionNames);
        var provoke = Set(CombatLists.ProvokeNames);
        var shirk = Set(CombatLists.ShirkNames);
        var raiseStatus = Set(CombatLists.RaiseStatusNames);
        var control = Set(CombatLists.ControlNames);
        var kb = Set(CombatLists.KnockbackImmunityNames);
        try
        {
            foreach (var row in Services.Data.GetExcelSheet<Action>(ClientLanguage.English))
            {
                var name = row.Name.ExtractText().Trim();
                if (name.Length == 0) continue;
                if (interrupts.Contains(name)) n.Interrupts.Add(row.RowId);
                if (raises.Contains(name)) n.Raises.Add(row.RowId);
                if (provoke.Contains(name)) n.Provoke.Add(row.RowId);
                if (shirk.Contains(name)) n.Shirk.Add(row.RowId);
            }
            foreach (var row in Services.Data.GetExcelSheet<Lumina.Excel.Sheets.Status>(ClientLanguage.English))
            {
                var name = row.Name.ExtractText().Trim();
                if (name.Length == 0) continue;
                if (raiseStatus.Contains(name)) n.RaiseStatuses.Add(row.RowId);
                if (control.Contains(name)) n.Control.Add(row.RowId);
                if (kb.Contains(name)) n.KnockbackImmunity.Add(row.RowId);
                if (name.Equals("Weakness", StringComparison.OrdinalIgnoreCase)) n.Weakness[row.RowId] = 1;
                else if (name.Equals("Brink of Death", StringComparison.OrdinalIgnoreCase)) n.Weakness[row.RowId] = 2;
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Raid Planner couldn't read the game's actions and statuses in English");
        }
        return n;
    }

    /// <summary>
    /// The statuses the meter and the summary pick out, by their English names on the Status sheet
    /// (read in English whatever the client's language; <see cref="CombatLists"/>): every damage down
    /// and vulnerability up (by kind), the jobs' DoTs, the barriers. Made once, then only read.
    /// </summary>
    private static (Dictionary<uint, int>, HashSet<uint>, HashSet<uint>) ReadLists()
    {
        var fails = new Dictionary<uint, int>();
        var dots = new HashSet<uint>();
        var shields = new HashSet<uint>();
        var dotNames = new HashSet<string>(CombatLists.DotNames, StringComparer.OrdinalIgnoreCase);
        var shieldNames = new HashSet<string>(CombatLists.ShieldNames, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var row in Services.Data.GetExcelSheet<Lumina.Excel.Sheets.Status>(ClientLanguage.English))
            {
                var name = row.Name.ExtractText().Trim();
                if (name.Length == 0) continue;
                var fail = CombatLists.FailKind(name);
                if (fail != CombatLists.NotAFail) fails[row.RowId] = fail;
                // Every row with the name: only the party's statuses are ever asked about, so another of the same name does no harm.
                if (dotNames.Contains(name)) dots.Add(row.RowId);
                if (shieldNames.Contains(name)) shields.Add(row.RowId);
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Raid Planner couldn't read the game's statuses in English");
        }
        return (fails, dots, shields);
    }

    /// <summary>Each damage down's and vulnerability up's kind (<see cref="CombatLists.FailDamageDown"/>, <see cref="CombatLists.FailVulnerability"/>), by status id.</summary>
    public IReadOnlyDictionary<uint, int> FailKinds => lists.Value.Fails;

    public IReadOnlySet<uint> DotIds => lists.Value.Dots;

    public IReadOnlySet<uint> ShieldIds => lists.Value.Shields;

    public int FailKind(uint status) => lists.Value.Fails.TryGetValue(status, out var k) ? k : CombatLists.NotAFail;

    public bool IsDot(uint status) => lists.Value.Dots.Contains(status);

    public bool IsShield(uint status) => lists.Value.Shields.Contains(status);

    /// <summary>A status's name and icon, as the game's Status sheet has them; null for one it doesn't have.</summary>
    public StatusInfo? Status(uint id)
    {
        if (statuses.TryGetValue(id, out var known)) return known;
        StatusInfo? info = null;
        if (Services.Data.GetExcelSheet<Lumina.Excel.Sheets.Status>().TryGetRow(id, out var row))
            info = new StatusInfo(row.Name.ExtractText(), row.Icon, row.StatusCategory, row.CanDispel);
        statuses[id] = info;
        return info;
    }

    /// <summary>The duty a zone is (its name and ids), or null for somewhere that isn't one (a city, the open world).</summary>
    public DutyInfo? Duty(uint territory)
    {
        if (duties.TryGetValue(territory, out var known)) return known;
        DutyInfo? info = null;
        if (Services.Data.GetExcelSheet<TerritoryType>().TryGetRow(territory, out var t) && t.ContentFinderCondition.RowId != 0
            && t.ContentFinderCondition.ValueNullable is { } cfc)
        {
            var name = cfc.Name.ExtractText();
            if (name.Length == 0) name = t.PlaceName.ValueNullable is { } place ? place.Name.ExtractText() : string.Empty;
            info = new DutyInfo(Words.DutyName(name), territory, t.ContentFinderCondition.RowId);
        }
        duties[territory] = info;
        return info;
    }

    /// <summary>
    /// Is the zone a fight worth recording by itself: a duty from the Duty Finder that's a trial, a
    /// raid, an ultimate, a criterion dungeon (anything but the kinds in <see cref="NotFights"/>)?
    /// </summary>
    public bool IsFight(uint territory)
    {
        if (fights.TryGetValue(territory, out var known)) return known;
        var fight = Services.Data.GetExcelSheet<TerritoryType>().TryGetRow(territory, out var t)
            && t.ContentFinderCondition.ValueNullable is { } cfc && t.ContentFinderCondition.RowId != 0
            && !NotFights.Contains(cfc.ContentType.RowId);
        fights[territory] = fight;
        return fight;
    }

    /// <summary>
    /// The field operations' open zones, by the TerritoryType sheet's TerritoryIntendedUse: Eureka (41),
    /// the Bozjan Southern Front and Zadnor (48), Occult Crescent's South and North Horn (61). Many
    /// fights go on there at once, its CEs and FATEs among the mobs others are fighting; Delubrum
    /// Reginae (52, 53) and the duties are one fight each.
    /// </summary>
    private static readonly HashSet<uint> FieldUses = new() { 41, 48, 61 };

    private readonly Dictionary<uint, bool> fields = new();

    /// <summary>Is the zone a field operation's open zone (<see cref="FieldUses"/>), where only the party's own fight is recorded?</summary>
    public bool IsFieldZone(uint territory)
    {
        if (fields.TryGetValue(territory, out var known)) return known;
        var field = Services.Data.GetExcelSheet<TerritoryType>().TryGetRow(territory, out var t)
            && FieldUses.Contains(t.TerritoryIntendedUse.RowId);
        fields[territory] = field;
        return field;
    }

    /// <summary>
    /// Is the zone the Occult Crescent (South or North Horn, the Forked Tower and the critical
    /// engagements included, by the territory's intended use)? Nothing there is ever recorded, even with
    /// <i>In every duty</i> on: its fights are a field's, not a planned duty's. The meter still works there.
    /// </summary>
    public bool IsOccultCrescent(uint territory) =>
        Services.Data.GetExcelSheet<TerritoryType>().TryGetRow(territory, out var t)
        && t.TerritoryIntendedUse.RowId == OccultUse;

    /// <summary>A zone's own name, for a fight recorded outside a duty.</summary>
    public string PlaceName(uint territory) =>
        Services.Data.GetExcelSheet<TerritoryType>().TryGetRow(territory, out var t) && t.PlaceName.ValueNullable is { } place
            ? Words.DutyName(place.Name.ExtractText())
            : "Unknown place";

    /// <summary>
    /// A job's short name ("PLD") and role (1 tank, 2 melee, 3 ranged, 4 healer). Always the English
    /// abbreviation, whatever the client's language: it goes into recordings and summaries, and the site
    /// and the meter read jobs by these names.
    /// </summary>
    public (string Job, byte Role) Job(uint classJob)
    {
        if (jobs.TryGetValue(classJob, out var known)) return known;
        var result = Services.Data.GetExcelSheet<ClassJob>(ClientLanguage.English).TryGetRow(classJob, out var row)
            ? (row.Abbreviation.ExtractText(), row.Role)
            : (string.Empty, (byte)0);
        jobs[classJob] = result;
        return result;
    }
}
