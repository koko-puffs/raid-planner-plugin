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
