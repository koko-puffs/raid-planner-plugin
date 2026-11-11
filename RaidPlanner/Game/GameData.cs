using System;
using System.Collections.Generic;
using Dalamud.Game;
using Lumina.Excel.Sheets;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;

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
    /// PvP, quest battles, treasure hunts, deep dungeons, Eureka and Bozja. A trash pack there would
    /// otherwise be kept as a pull, and push out the ones you meant to keep.
    /// </summary>
    private static readonly HashSet<uint> NotFights = new() { 2, 3, 6, 7, 9, 21, 26, 29 };

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
                (int)row.ActionCategory.RowId, row.Recast100ms / 10f);
        }
        actions[id] = info;
        return info;
    }

    /// <summary>An action's ActionCategory row (9 a limit break, 15 PvP's; 0 unknown): the combat parser's question.</summary>
    public int ActionCategory(uint id) => Action(id)?.Category ?? 0;

    /// <summary>An action's name for the meter ("Action 1234" for one the sheet doesn't name).</summary>
    public string ActionName(uint id) => Action(id) is { Name.Length: > 0 } a ? a.Name : $"Action {id}";

    /// <summary>A status's name for the meter ("Status 1234" for one the sheet doesn't name).</summary>
    public string StatusName(uint id) => Status(id) is { Name.Length: > 0 } st ? st.Name : $"Status {id}";

    private readonly Lazy<(Dictionary<uint, int> Fails, HashSet<uint> Dots, HashSet<uint> Shields)> lists = new(ReadLists);

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
            info = new StatusInfo(row.Name.ExtractText(), row.Icon);
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

    /// <summary>A zone's own name, for a fight recorded outside a duty.</summary>
    public string PlaceName(uint territory) =>
        Services.Data.GetExcelSheet<TerritoryType>().TryGetRow(territory, out var t) && t.PlaceName.ValueNullable is { } place
            ? Words.DutyName(place.Name.ExtractText())
            : "Unknown place";

    /// <summary>A job's short name ("PLD") and role (1 tank, 2 melee, 3 ranged, 4 healer).</summary>
    public (string Job, byte Role) Job(uint classJob)
    {
        if (jobs.TryGetValue(classJob, out var known)) return known;
        var result = Services.Data.GetExcelSheet<ClassJob>().TryGetRow(classJob, out var row)
            ? (row.Abbreviation.ExtractText(), row.Role)
            : (string.Empty, (byte)0);
        jobs[classJob] = result;
        return result;
    }
}
