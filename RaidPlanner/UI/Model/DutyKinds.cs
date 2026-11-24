using System;
using System.Collections.Generic;

namespace RaidPlanner.UI.Model;

/// <summary>
/// The kinds of duty a settings profile (0.9.0, <see cref="SettingsProfile"/>) can be used in, as Settings → Profiles
/// lists them. Saved by name in the settings, so new kinds go at the end.
/// </summary>
public enum DutyKind
{
    Ultimate,

    /// <summary>Savage raids (and the Chaotic alliance raid, Delubrum Reginae's Savage).</summary>
    Savage,

    /// <summary>Extreme and Unreal trials.</summary>
    Extreme,

    /// <summary>Criterion dungeons, normal and Savage (Variant dungeons are <see cref="Dungeon"/>s).</summary>
    Criterion,

    /// <summary>The 24-player alliance raids (not Chaotic).</summary>
    Alliance,

    /// <summary>Normal raids and normal trials.</summary>
    NormalRaid,

    /// <summary>Dungeons, Variant dungeons, deep dungeons and guildhests.</summary>
    Dungeon,

    /// <summary>No duty: the open world, the cities, and the field operations' zones (Eureka, Bozja, the Occult Crescent).</summary>
    OpenWorld,

    /// <summary>Every other duty: PvP, quest battles, treasure dungeons, the Gold Saucer…</summary>
    Other,
}

/// <summary>
/// What the game's data says about the duty you're in (read by <c>GameData.Facts</c>): its ContentFinderCondition's
/// ContentType row, its HighEndDuty flag, whether it's an alliance's (the alliance roulette's, or three parties), its name
/// in English, and the territory's intended use. Pure, so the mapping (<see cref="DutyKinds.Of"/>) is tested.
/// </summary>
public readonly record struct DutyFacts(uint ContentType, bool HighEnd, bool Alliance, string Name, uint IntendedUse = 0);

/// <summary>The duty kinds' names, and which kind a duty is (<see cref="Of"/>).</summary>
public static class DutyKinds
{
    /// <summary>Every kind, in the order the profile's Used in lists them.</summary>
    public static readonly IReadOnlyList<DutyKind> All = (DutyKind[])Enum.GetValues(typeof(DutyKind));

    // The ContentType sheet's rows read here (the same numbers GameData's NotFights uses).
    public const uint Dungeons = 2, Guildhests = 3, Trials = 4, Raids = 5, DeepDungeons = 21, Eureka = 26, UltimateRaids = 28, Bozja = 29;

    /// <summary>Variant and Criterion dungeons ("V&amp;C Dungeon Finder").</summary>
    public const uint VariantCriterion = 30;

    /// <summary>The Chaotic alliance raid (37, "Chaotic Alliance Raid" on the sheet), and the Occult Crescent (38).</summary>
    public const uint Chaotic = 37, OccultCrescent = 38;

    /// <summary>The field operations' open zones, by TerritoryIntendedUse (GameData's FieldUses): Eureka, Bozja and Zadnor, the Occult Crescent.</summary>
    private static readonly HashSet<uint> FieldUses = new() { 41, 48, 61 };

    /// <summary>The chip's name: "Ultimate", "Alliance raids", "Open world"…</summary>
    public static string Name(DutyKind kind) => kind switch
    {
        DutyKind.Ultimate => "Ultimate",
        DutyKind.Savage => "Savage",
        DutyKind.Extreme => "Extreme",
        DutyKind.Criterion => "Criterion",
        DutyKind.Alliance => "Alliance raids",
        DutyKind.NormalRaid => "Normal raids",
        DutyKind.Dungeon => "Dungeons",
        DutyKind.OpenWorld => "Open world",
        _ => "Other duties",
    };

    /// <summary>What a kind takes in, for its chip's tooltip.</summary>
    public static string Line(DutyKind kind) => kind switch
    {
        DutyKind.Ultimate => "Every ultimate raid.",
        DutyKind.Savage => "Savage raids, and the Chaotic alliance raid.",
        DutyKind.Extreme => "Extreme and Unreal trials.",
        DutyKind.Criterion => "Criterion dungeons, normal and Savage.",
        DutyKind.Alliance => "The 24-player alliance raids.",
        DutyKind.NormalRaid => "Normal raids and normal trials.",
        DutyKind.Dungeon => "Dungeons, Variant dungeons, deep dungeons and guildhests.",
        DutyKind.OpenWorld => "Outside duties: the open world, the cities, and field operations (Eureka, Bozja, the Occult Crescent).",
        _ => "Every other duty: PvP, quest battles, treasure dungeons, the Gold Saucer.",
    };

    /// <summary>
    /// Which kind a duty is, from the game's data; null facts: not in a duty (the open world). The ContentType row first
    /// (ultimates, criterion, trials, raids, dungeons), then within trials and raids the HighEndDuty flag and the English
    /// name's "(Savage)", "(Extreme)", "(Unreal)", "(Chaotic)" (either is enough; the Extreme trials named "The Minstrel's
    /// Ballad: …" say neither, nor are they flagged high-end), and the alliance roulette or three parties for an alliance
    /// raid. The field operations' open zones are the open world (the Baldesion Arsenal, Castrum Lacus Litore, Dalriada and
    /// the Forked Tower are inside them: no zone of their own), Delubrum Reginae in its own zone (intended use 52) an
    /// alliance raid, its Savage (53) Savage.
    /// </summary>
    public static DutyKind Of(DutyFacts? facts)
    {
        if (facts is not { } d) return DutyKind.OpenWorld;
        var name = d.Name ?? string.Empty;
        bool Named(string tag) => name.Contains(tag, StringComparison.OrdinalIgnoreCase);
        switch (d.ContentType)
        {
            case UltimateRaids:
                return DutyKind.Ultimate;
            case VariantCriterion:
                return d.HighEnd || name.StartsWith("Another ", StringComparison.OrdinalIgnoreCase) || Named("(Savage)") ? DutyKind.Criterion : DutyKind.Dungeon;
            case Trials:
                return d.HighEnd || Named("(Extreme)") || Named("(Unreal)") || Named("Minstrel's Ballad") ? DutyKind.Extreme : DutyKind.NormalRaid;
            case Raids:
                if (Named("(Savage)") || Named("(Chaotic)") || d.HighEnd) return DutyKind.Savage;
                return d.Alliance ? DutyKind.Alliance : DutyKind.NormalRaid;
            case Chaotic:
                return DutyKind.Savage;
            case Dungeons:
            case Guildhests:
            case DeepDungeons:
                return DutyKind.Dungeon;
            case Eureka:
            case Bozja:
            case OccultCrescent:
                // The open zones themselves are the open world (and the raids inside them); a field operation's raid in its
                // own zone (Delubrum Reginae) is an alliance raid, its Savage one Savage.
                if (Named("(Savage)")) return DutyKind.Savage;
                return FieldUses.Contains(d.IntendedUse) ? DutyKind.OpenWorld : DutyKind.Alliance;
        }
        if (FieldUses.Contains(d.IntendedUse)) return DutyKind.OpenWorld;
        return DutyKind.Other;
    }

    /// <summary>"Ultimate, Savage and Extreme" (empty for none), in <see cref="All"/>'s order.</summary>
    public static string Words(IEnumerable<DutyKind> kinds)
    {
        var names = new List<string>();
        foreach (var k in All)
            foreach (var picked in kinds)
                if (picked == k)
                {
                    names.Add(Name(k));
                    break;
                }
        return names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[^1],
        };
    }
}
