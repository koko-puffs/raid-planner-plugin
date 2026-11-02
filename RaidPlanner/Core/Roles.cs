using System.Collections.Generic;
using System.Linq;

namespace RaidPlanner.Core;

/// <summary>A player as the game lists them in the party: a key to find them again, their job, and the job's role.</summary>
/// <param name="Role">The game's ClassJob role: 1 tank, 2 melee, 3 ranged (physical or magic), 4 healer.</param>
public sealed record Partier(ulong Key, string Job, byte Role);

/// <summary>
/// The plan's spots for a party: tanks MT then OT, healers H1 then H2, melee M1 then M2, ranged R1
/// then R2, in the party list's order; a fourth melee takes a free ranged spot (and the other way
/// round), and anyone past the eight is P9, P10… So a light party is MT, H1 and two of the DPS
/// spots, as the planner's own plans have it.
/// </summary>
public static class Roles
{
    public static string RoleWord(byte role) => role switch
    {
        1 => "tank",
        4 => "healer",
        _ => "dps",
    };

    public static List<(Partier Who, string Slot)> Assign(IReadOnlyList<Partier> party)
    {
        var free = new List<string> { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" };
        var result = new (Partier, string)?[party.Count];

        string? Take(params string[] wanted)
        {
            foreach (var w in wanted)
                if (free.Remove(w)) return w;
            return null;
        }

        // First pass: everyone to their own role's spots.
        for (var i = 0; i < party.Count; i++)
        {
            var slot = party[i].Role switch
            {
                1 => Take("MT", "OT"),
                4 => Take("H1", "H2"),
                2 => Take("M1", "M2"),
                3 => Take("R1", "R2"),
                _ => null,
            };
            if (slot != null) result[i] = (party[i], slot);
        }
        // Then whoever's left: DPS into the other DPS spots, anyone into what's free, then P9 up.
        var extra = 9;
        for (var i = 0; i < party.Count; i++)
        {
            if (result[i] != null) continue;
            var slot = party[i].Role switch
            {
                2 => Take("R1", "R2"),
                3 => Take("M1", "M2"),
                _ => null,
            } ?? (free.Count > 0 ? Take(free[0]) : null) ?? $"P{extra++}";
            result[i] = (party[i], slot);
        }
        return result.Select(r => r!.Value).ToList();
    }
}
