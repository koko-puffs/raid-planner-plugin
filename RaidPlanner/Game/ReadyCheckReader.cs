using System;
using System.Collections.Generic;
using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;

namespace RaidPlanner.Game;

/// <summary>
/// What the ready check card reads from the game (since 0.9.0), only ever read, never answered and nothing sent:
/// <list type="bullet">
/// <item>Whether a ready check is going on: the game's ready check agent (<c>AgentReadyCheck</c>) keeps an entry for each
/// member with their answer; while one is still awaited (<c>ReadyCheckStatus.AwaitingResponse</c>) the check is on [verify: the
/// entries go back to answered or empty when it ends]. The entries are read as fields, no game function called.</item>
/// <item>Each party member's food (<see cref="PartyMembers"/>, then their character's status list: Well Fed and its time left;
/// a member not near, or in another zone, is "can't see").</item>
/// <item>Yours: the meals and tinctures in your four inventory pages, and your equipped gear's lowest condition, through
/// Dalamud's own reading of the containers (<c>IGameInventory</c>: condition 30,000 is 100%) [verify: the condition's scale].</item>
/// </list>
/// Guarded: a read that throws gives nothing (no card) and is logged now and then. On the framework thread.
/// </summary>
public sealed unsafe class ReadyCheckReader
{
    private const uint InvalidId = 0xE0000000;

    private static readonly GameInventoryType[] Pages =
    {
        GameInventoryType.Inventory1, GameInventoryType.Inventory2, GameInventoryType.Inventory3, GameInventoryType.Inventory4,
    };

    private readonly GameData data;
    private readonly PrepData prep;
    private DateTime failedAt;

    public ReadyCheckReader(GameData data, PrepData prep)
    {
        this.data = data;
        this.prep = prep;
    }

    /// <summary>Whether a ready check is going on: someone's answer is still awaited.</summary>
    public bool Checking()
    {
        try
        {
            if (!Services.ClientState.IsLoggedIn) return false;
            var agent = AgentReadyCheck.Instance();
            if (agent == null) return false;
            foreach (var entry in agent->ReadyCheckEntries)
                if (entry.ContentId != 0 && entry.Status == ReadyCheckStatus.AwaitingResponse)
                    return true;
            return false;
        }
        catch (Exception ex)
        {
            Failed(ex);
            return false;
        }
    }

    /// <summary>The party's food, in the party list's order (you included); empty when it can't be read.</summary>
    public List<ReadyMember> Members()
    {
        var list = new List<ReadyMember>(8);
        try
        {
            var manager = CharacterManager.Instance();
            foreach (var m in PartyMembers.Read())
            {
                var (job, role) = data.Job(m.ClassJob);
                var food = FoodState.Unseen;
                double left = 0;
                if (m.Near && m.EntityId is not (0 or InvalidId) && manager != null)
                {
                    var bc = manager->LookupBattleCharaByEntityId(m.EntityId);
                    if (bc != null)
                    {
                        food = FoodState.None;
                        // The field itself, not the virtual GetStatusManager (never a call through the game's table).
                        var statuses = bc->StatusManager.Status;
                        for (var i = 0; i < statuses.Length; i++)
                        {
                            if (statuses[i].StatusId == 0 || !prep.IsWellFed(statuses[i].StatusId)) continue;
                            food = FoodState.Fed;
                            left = Math.Max(left, statuses[i].RemainingTime);
                        }
                    }
                }
                list.Add(new ReadyMember(m.Name, job, Roles.RoleWord(role), food, left, m.IsYou));
            }
        }
        catch (Exception ex)
        {
            Failed(ex);
            list.Clear();
        }
        return list;
    }

    /// <summary>Your tinctures, meals and gear's lowest condition.</summary>
    public ReadyYou You()
    {
        try
        {
            var tinctures = 0;
            var meals = 0;
            foreach (var page in Pages)
            {
                foreach (var item in Services.Inventory.GetInventoryItems(page))
                {
                    if (item.IsEmpty || item.ItemId == 0) continue;
                    switch (prep.Item(item.BaseItemId))
                    {
                        case PrepItem.Tincture:
                            tinctures += Math.Max(0, item.Quantity);
                            break;
                        case PrepItem.Meal:
                            meals += Math.Max(0, item.Quantity);
                            break;
                    }
                }
            }
            int? lowest = null;
            foreach (var item in Services.Inventory.GetInventoryItems(GameInventoryType.EquippedItems))
            {
                if (item.IsEmpty || item.ItemId == 0) continue;
                // Condition: 30,000 is 100% (a soul crystal has none, and is left out).
                if (item.InventorySlot == 13) continue;
                var percent = (int)Math.Floor(Math.Clamp(item.Condition, 0u, 30_000u) / 300.0);
                lowest = lowest is { } l ? Math.Min(l, percent) : percent;
            }
            return new ReadyYou(tinctures, meals, lowest);
        }
        catch (Exception ex)
        {
            Failed(ex);
            return new ReadyYou(0, 0, null);
        }
    }

    private void Failed(Exception ex)
    {
        if (DateTime.UtcNow - failedAt < TimeSpan.FromSeconds(30)) return;
        failedAt = DateTime.UtcNow;
        Services.Log.Warning(ex, "Raid Codex couldn't read the ready check");
    }
}
