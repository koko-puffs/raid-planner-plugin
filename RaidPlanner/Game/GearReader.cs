using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using RaidPlanner.Core;

namespace RaidPlanner.Game;

/// <summary>
/// The recording player's gear as a pull begins (since 0.5.0; the recording's <c>gear</c>): each item in the game's
/// equipped-items container (Dalamud's <c>IGameInventory</c>, <c>EquippedItems</c>: its place, base row and high-quality flag;
/// its name and item level from the Item sheet) and their attributes then (<c>PlayerState.Attributes</c>, by the game's attribute ids) [verify:
/// the container's places, the attributes' indices]. Guarded: anything it can't read is left out, and a read that throws gives
/// no gear at all (the pull is recorded as ever). On the framework thread.
/// </summary>
public static unsafe class GearReader
{
    /// <summary>The attributes kept, by the site's keys: the game's attribute ids (PlayerAttribute).</summary>
    private static readonly (string Key, PlayerAttribute Attribute)[] Stats =
    {
        ("str", PlayerAttribute.Strength), ("dex", PlayerAttribute.Dexterity), ("vit", PlayerAttribute.Vitality),
        ("int", PlayerAttribute.Intelligence), ("mnd", PlayerAttribute.Mind), ("crit", PlayerAttribute.CriticalHit),
        ("dh", PlayerAttribute.DirectHitRate), ("det", PlayerAttribute.Determination), ("sks", PlayerAttribute.SkillSpeed),
        ("sps", PlayerAttribute.SpellSpeed), ("ten", PlayerAttribute.Tenacity), ("pie", PlayerAttribute.Piety),
    };

    /// <summary>Your gear now, or null when it can't be read (and, with Diagnostics on, what was read).</summary>
    public static Gear? Read(GameData data, DiagnosticsLog? diagnostics)
    {
        try
        {
            if (Services.Objects.LocalPlayer is not { } me) return null;
            // Dalamud's own reading of the equipped-items container (no game function called here).
            var items = new List<GearItem>();
            var levels = new List<(int, int)>();
            var twoHanded = false;
            foreach (var item in Services.Inventory.GetInventoryItems(GameInventoryType.EquippedItems))
            {
                var slot = (int)item.InventorySlot;
                if (item.IsEmpty || item.ItemId == 0 || slot is < 0 or >= Gear.MostItems || items.Exists(x => x.Slot == slot)) continue;
                // The base row (an item's id can carry an offset for its quality), and the flag.
                var row = item.BaseItemId;
                if (data.Gear(row) is not { } info) continue;
                items.Add(new GearItem(slot, row, item.IsHq, info.Name));
                levels.Add((slot, info.ItemLevel));
                if (slot == 0) twoHanded = info.TwoHanded;
            }
            if (items.Count == 0) return null;
            var stats = new Dictionary<string, int>();
            var ps = PlayerState.Instance();
            if (ps != null)
            {
                var attributes = ps->Attributes;
                foreach (var (key, attribute) in Stats)
                {
                    var index = (int)attribute;
                    if (index >= 0 && index < attributes.Length && attributes[index] > 0) stats[key] = attributes[index];
                }
            }
            var gear = new Gear(data.Job(me.ClassJob.RowId).Job, Gear.AverageItemLevel(levels, twoHanded), items, stats);
            if (diagnostics is { On: true } d)
                d.WriteText("gear", $"job {gear.Job} ilvl {gear.ItemLevel} two-handed {twoHanded} items {string.Join(' ', items.ConvertAll(x => $"{x.Slot}:{x.Id}{(x.Hq ? "hq" : "")}"))} stats {string.Join(' ', stats.Keys.Select(k => $"{k}={stats[k]}"))}");
            return gear;
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Raid Planner couldn't read your gear for the recording");
            return null;
        }
    }
}
