using System;
using System.Collections.Generic;
using Dalamud.Game;
using Lumina.Excel.Sheets;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.Game;

/// <summary>What an inventory item is to the ready check (since 0.9.0): a meal, a tincture, or neither.</summary>
public enum PrepItem
{
    None,
    Meal,
    Tincture,
}

/// <summary>
/// What the game's own data says for the ready check and the death recap (since 0.9.0), each looked up once and kept: which
/// statuses are the death recap's mitigations (<see cref="Mitigations"/>, by their English names on the Status sheet, as the
/// meter's other lists are), which is Well Fed, and which items are meals and tinctures. The status lists are made once on a
/// worker as the plugin loads and only read after; the items are looked up on the framework thread.
/// </summary>
public sealed class PrepData
{
    /// <summary>The Status sheet's Well Fed (food) and Medicated (a tincture) rows, as long known [verify: the ids].</summary>
    public const uint WellFedId = 48, MedicatedId = 49;

    /// <summary>The ItemUICategory sheet's Medicine and Meal rows [verify: the rows].</summary>
    private const uint MedicineCategory = 44, MealCategory = 46;

    private readonly Lazy<(Dictionary<uint, int> Mitigation, HashSet<uint> WellFed)> lists = new(Read);
    private readonly Dictionary<uint, PrepItem> items = new();

    public PrepData()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                _ = lists.Value;
            }
            catch (Exception)
            {
                // Read says itself what went wrong (and never throws).
            }
        });
    }

    /// <summary>A status's place in <see cref="Mitigations.All"/>, or −1 when it isn't one of them.</summary>
    public int MitigationIndex(uint status) => status != 0 && lists.Value.Mitigation.TryGetValue(status, out var i) ? i : -1;

    /// <summary>Is a status Well Fed (the food's)?</summary>
    public bool IsWellFed(uint status) => status == WellFedId || lists.Value.WellFed.Contains(status);

    /// <summary>
    /// What an item is (by its Item sheet row, its quality taken off): a meal (the Meal category), a tincture (a Medicine whose
    /// use gives Medicated: not a potion or an elixir) [verify: the item action's first value is the status it gives], or neither.
    /// </summary>
    public PrepItem Item(uint row)
    {
        if (row == 0) return PrepItem.None;
        if (items.TryGetValue(row, out var known)) return known;
        var kind = PrepItem.None;
        try
        {
            if (Services.Data.GetExcelSheet<Item>().TryGetRow(row, out var it))
            {
                var category = it.ItemUICategory.RowId;
                var gives = it.ItemAction.ValueNullable is { } action && action.Data.Count > 0 ? action.Data[0] : (ushort)0;
                if (category == MealCategory || (gives == WellFedId && category != MedicineCategory)) kind = PrepItem.Meal;
                else if (category == MedicineCategory && gives == MedicatedId) kind = PrepItem.Tincture;
            }
        }
        catch (Exception)
        {
            // An item the sheet can't give is neither.
        }
        if (items.Count > 1024) items.Clear();
        items[row] = kind;
        return kind;
    }

    private static (Dictionary<uint, int>, HashSet<uint>) Read()
    {
        var mitigation = new Dictionary<uint, int>();
        var wellFed = new HashSet<uint>();
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, index) in Mitigations.Names()) names.TryAdd(name, index);
        try
        {
            foreach (var row in Services.Data.GetExcelSheet<Status>(ClientLanguage.English))
            {
                var name = row.Name.ExtractText().Trim();
                if (name.Length == 0) continue;
                // Every row with the name: only the party's (and the bosses') statuses are asked about.
                if (names.TryGetValue(name, out var i)) mitigation[row.RowId] = i;
                if (name.Equals("Well Fed", StringComparison.OrdinalIgnoreCase)) wellFed.Add(row.RowId);
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Raid Codex couldn't read the game's statuses for the death recap");
        }
        return (mitigation, wellFed);
    }
}
