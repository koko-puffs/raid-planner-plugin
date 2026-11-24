using System;
using System.Collections.Generic;

namespace RaidPlanner.Core.Meter;

/// <summary>
/// What a mitigation is (<see cref="MitigationSpec.Kind"/>): the death recap's "Up at the hit" sorts and words them by it; the
/// meter's Mitigation tab reads the same catalogue as a <see cref="MitigationKind"/> (<see cref="CombatLists.MitigationOf"/>).
/// </summary>
public enum MitigationRole
{
    /// <summary>A tank's own: Rampart, Guardian, Holmgang, Shadowed Vigil, Great Nebula…</summary>
    Personal,

    /// <summary>On the party from a party member: Troubadour, Kerachole, Temperance, Dark Missionary…</summary>
    Party,

    /// <summary>A barrier (it soaks damage, it doesn't lower it): Divine Benison, Galvanize, The Blackest Night…</summary>
    Shield,

    /// <summary>On the enemy (it lowers the damage it deals): Reprisal, Addle, Feint, Dismantle.</summary>
    OnEnemy,
}

/// <summary>
/// A mitigation the death recap knows (since 0.9.0): its key (the name it's shown by), what it is, its English names on the
/// game's Status sheet (each row with one of them counts: Sentinel and Guardian are one), the jobs that have it, and whether
/// a tank dying without it is worth saying (<see cref="Notable"/>: their big personal cooldowns and invulnerability).
/// </summary>
public sealed record MitigationSpec(string Key, MitigationRole Kind, IReadOnlyList<string> Names, IReadOnlyList<string> Jobs, bool Notable = false)
{
    public bool For(string? job)
    {
        if (string.IsNullOrEmpty(job)) return false;
        foreach (var j in Jobs)
            if (string.Equals(j, job, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

/// <summary>
/// The mitigations the death recap looks for (since 0.9.0), by their English Status names, as the meter's other lists are
/// (<see cref="CombatLists"/>): the game side finds their rows on the Status sheet read in English and reads which are on each
/// party member (and, on the bosses, the party's Reprisal, Addle, Feint and Dismantle) every frame of an encounter. A set of
/// them is a bit mask (<see cref="All"/>'s order: at most 64), so a frame's reading makes nothing. Level 100 jobs.
/// The Status sheet's names are the game's own: each checked against the English sheet (xivapi's ffxiv-datamining Status.csv,
/// November 2026) in 0.9.0's review, where only The Blackest Night's was wrong (its status is "Blackest Night").
/// </summary>
public static class Mitigations
{
    private static readonly string[] Tanks = { "PLD", "WAR", "DRK", "GNB" };

    private static MitigationSpec P(string key, bool notable, string job, params string[] names) =>
        new(key, MitigationRole.Personal, names.Length > 0 ? names : new[] { key }, new[] { job }, notable);

    private static MitigationSpec Party(string key, string job, params string[] names) => new(key, MitigationRole.Party, names.Length > 0 ? names : new[] { key }, new[] { job });

    private static MitigationSpec Shield(string key, string job, params string[] names) => new(key, MitigationRole.Shield, names.Length > 0 ? names : new[] { key }, new[] { job });

    /// <summary>Every mitigation, in the order a recap lists them (a mask's bits are these indices).</summary>
    public static readonly IReadOnlyList<MitigationSpec> All = new[]
    {
        // A tank's own, the notable ones first (a tank dying without them is said).
        new MitigationSpec("Rampart", MitigationRole.Personal, new[] { "Rampart" }, Tanks, true),
        P("Guardian", true, "PLD", "Guardian", "Sentinel"),
        P("Hallowed Ground", true, "PLD"),
        P("Damnation", true, "WAR", "Damnation", "Vengeance"),
        P("Holmgang", true, "WAR"),
        P("Shadowed Vigil", true, "DRK", "Shadowed Vigil", "Shadow Wall"),
        P("Living Dead", true, "DRK", "Living Dead", "Walking Dead"),
        P("Great Nebula", true, "GNB", "Great Nebula", "Nebula"),
        P("Superbolide", true, "GNB"),
        P("Bulwark", false, "PLD"),
        P("Holy Sheltron", false, "PLD", "Holy Sheltron", "Sheltron", "Knight's Resolve", "Intervention"),
        P("Thrill of Battle", false, "WAR"),
        P("Bloodwhetting", false, "WAR", "Bloodwhetting", "Raw Intuition", "Stem the Flow", "Nascent Glint"),
        P("Dark Mind", false, "DRK"),
        P("Oblation", false, "DRK"),
        P("Camouflage", false, "GNB"),
        P("Heart of Corundum", false, "GNB", "Heart of Corundum", "Clarity of Corundum", "Heart of Stone"),

        // Others' own.
        P("Third Eye", false, "SAM", "Third Eye", "Tengentsu"),
        P("Riddle of Earth", false, "MNK"),

        // The party's, from its members.
        Party("Passage of Arms", "PLD", "Passage of Arms", "Arms Up"),
        Party("Dark Missionary", "DRK"),
        Party("Heart of Light", "GNB"),
        Party("Troubadour", "BRD"),
        Party("Nature's Minne", "BRD"),
        Party("Tactician", "MCH"),
        Party("Shield Samba", "DNC"),
        Party("Magick Barrier", "RDM"),
        Party("Temperance", "WHM"),
        Party("Aquaveil", "WHM"),
        Party("Sacred Soil", "SCH"),
        Party("Fey Illumination", "SCH", "Fey Illumination", "Seraphic Illumination"),
        Party("Expedient", "SCH", "Desperate Measures"),
        Party("Collective Unconscious", "AST"),
        Party("Exaltation", "AST"),
        Party("Neutral Sect", "AST"),
        Party("The Bole", "AST"),
        Party("Sun Sign", "AST"),
        Party("Kerachole", "SGE"),
        Party("Holos", "SGE"),
        Party("Taurochole", "SGE"),

        // Barriers.
        Shield("Divine Veil", "PLD"),
        Shield("Shake It Off", "WAR"),
        // The status is "Blackest Night" (the action is The Blackest Night): until 0.9.0's review only the action's name, found nowhere.
        Shield("The Blackest Night", "DRK", "Blackest Night"),
        Shield("Divine Benison", "WHM", "Divine Benison", "Divine Caress"),
        Shield("Galvanize", "SCH", "Galvanize", "Catalyze", "Seraphic Veil"),
        Shield("Intersection", "AST"),
        Shield("Eukrasian Diagnosis", "SGE", "Eukrasian Diagnosis", "Differential Diagnosis"),
        Shield("Eukrasian Prognosis", "SGE"),
        Shield("Haima", "SGE", "Haima", "Haimatinon"),
        Shield("Panhaima", "SGE", "Panhaima", "Panhaimatinon", "Holosakos"),
        Shield("Radiant Aegis", "SMN"),
        Shield("Improvised Finish", "DNC"),
        Shield("Tempera Coat", "PCT", "Tempera Coat", "Tempera Grassa"),
        Shield("Stem the Tide", "WAR"),
        Shield("Shade Shift", "NIN"),
        Shield("Arcane Crest", "RPR", "Arcane Crest", "Crest of Time Borrowed"),
        Shield("Manaward", "BLM"),

        // On the enemy.
        new MitigationSpec("Reprisal", MitigationRole.OnEnemy, new[] { "Reprisal" }, Tanks),
        new MitigationSpec("Addle", MitigationRole.OnEnemy, new[] { "Addle" }, new[] { "BLM", "SMN", "RDM", "PCT" }),
        new MitigationSpec("Feint", MitigationRole.OnEnemy, new[] { "Feint" }, new[] { "MNK", "DRG", "NIN", "SAM", "RPR", "VPR" }),
        new MitigationSpec("Dismantle", MitigationRole.OnEnemy, new[] { "Dismantled", "Dismantle" }, new[] { "MCH" }),
    };

    /// <summary>Each English status name and the mitigation it's (its index in <see cref="All"/>).</summary>
    public static IEnumerable<(string Name, int Index)> Names()
    {
        for (var i = 0; i < All.Count; i++)
            foreach (var n in All[i].Names)
                yield return (n, i);
    }

    /// <summary>A mask with this mitigation's bit.</summary>
    public static ulong Bit(int index) => index is >= 0 and < 64 ? 1UL << index : 0;

    /// <summary>The bits of the mitigations that are on the enemy (Reprisal, Addle, Feint, Dismantle).</summary>
    public static readonly ulong OnEnemy = MaskOf(MitigationRole.OnEnemy);

    private static ulong MaskOf(MitigationRole kind)
    {
        ulong mask = 0;
        for (var i = 0; i < All.Count; i++)
            if (All[i].Kind == kind)
                mask |= Bit(i);
        return mask;
    }

    /// <summary>A mask with the mitigations of these keys (unknown keys left out).</summary>
    public static ulong Mask(params string[] keys)
    {
        ulong mask = 0;
        foreach (var k in keys)
            for (var i = 0; i < All.Count; i++)
                if (string.Equals(All[i].Key, k, StringComparison.OrdinalIgnoreCase))
                    mask |= Bit(i);
        return mask;
    }

    /// <summary>The mitigations in a mask, in <see cref="All"/>'s order.</summary>
    public static List<MitigationSpec> In(ulong mask)
    {
        var list = new List<MitigationSpec>();
        for (var i = 0; i < All.Count && i < 64; i++)
            if ((mask & Bit(i)) != 0)
                list.Add(All[i]);
        return list;
    }

    /// <summary>Whether a mask has anything that lowers damage (a barrier only soaks it: it doesn't count).</summary>
    public static bool AnyMitigation(ulong mask)
    {
        foreach (var m in In(mask))
            if (m.Kind != MitigationRole.Shield)
                return true;
        return false;
    }

    /// <summary>A tank's notable personal mitigations (<see cref="MitigationSpec.Notable"/>) not in the mask, for their job: what a tank's death recap says was missing.</summary>
    public static List<string> MissingFor(string? job, ulong mask)
    {
        var list = new List<string>();
        for (var i = 0; i < All.Count; i++)
        {
            var m = All[i];
            if (m.Notable && m.Kind == MitigationRole.Personal && m.For(job) && (mask & Bit(i)) == 0) list.Add(m.Key);
        }
        return list;
    }
}

/// <summary>What the killing blow was, as far as the meter can tell (since 0.9.0; <see cref="DeathEntry.Hit"/>).</summary>
public enum DeathHit
{
    /// <summary>Not told: anything else (an auto-attack, a spread, a puddle, an instant death).</summary>
    Unknown,

    /// <summary>A tank buster: it hit a tank (or both) and no one else, for a large share of their HP.</summary>
    TankBuster,

    /// <summary>A raid-wide: it hit most of the party at once.</summary>
    RaidWide,
}

/// <summary>How a killing blow is told (since 0.9.0), from how many it hit at once: worked out away from the session so it's tested.</summary>
public static class DeathHits
{
    /// <summary>Hits of the same action within this of the killing blow (seconds) are the same cast landing.</summary>
    public const double Together = 1.0;

    /// <summary>
    /// The kind: raid-wide when it hit at least half the party (three at least, in a party of four or more); a tank buster when
    /// it hit only tanks (one or two), one of them the one who died, for 40% of their max HP or more (when it's known).
    /// </summary>
    public static DeathHit Kind(int hit, int tanksHit, int partySize, bool tank, long amount, long maxHp)
    {
        if (partySize >= 4 && hit >= Math.Max(3, (partySize + 1) / 2)) return DeathHit.RaidWide;
        if (tank && hit is >= 1 and <= 2 && tanksHit == hit && (maxHp <= 0 || amount * 10 >= maxHp * 4)) return DeathHit.TankBuster;
        return DeathHit.Unknown;
    }
}
