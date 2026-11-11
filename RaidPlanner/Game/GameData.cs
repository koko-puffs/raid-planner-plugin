using System.Collections.Generic;
using Lumina.Excel.Sheets;
using RaidPlanner.Core;

namespace RaidPlanner.Game;

/// <summary>What the game's own data says about actions, duties and jobs (each looked up once, kept).</summary>
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
            info = new ActionInfo(row.Name.ExtractText(), row.CastType, row.EffectRange, row.XAxisModifier, omen, row.Cast100ms / 10f);
        }
        actions[id] = info;
        return info;
    }

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
