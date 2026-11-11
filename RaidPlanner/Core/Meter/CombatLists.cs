using System;
using System.Collections.Generic;

namespace RaidPlanner.Core.Meter;

/// <summary>
/// What the meter and the summary need to know about particular actions and statuses, kept in one
/// place (the site keeps its own in <c>model/debuffs.ts</c> and <c>model/combatLists.ts</c>). Statuses
/// go by their English names on the game's Status sheet: the game side (<c>Game/GameData</c>) reads
/// the sheet in English once, whatever the client's language, and finds every row with each name,
/// so the ids are the game's own, every row that shares a name included, and a new patch's are
/// caught without a plugin release.
/// </summary>
public static class CombatLists
{
    /// <summary>The ActionCategory rows of limit breaks: 9 (PvE), 15 (PvP).</summary>
    public static bool IsLimitBreak(int category) => category is 9 or 15;

    /// <summary>
    /// Barriers: statuses that soak damage. Never healing (as in ACT): the meter only notes one was
    /// put on, as a shield (<c>fx</c> kind 3) rather than a status (kind 4).
    /// </summary>
    public static readonly IReadOnlyList<string> ShieldNames = new[]
    {
        "Galvanize", "Catalyze", "Seraphic Veil", "Eukrasian Diagnosis", "Eukrasian Prognosis", "Differential Diagnosis",
        "Haima", "Haimatinon", "Panhaima", "Panhaimatinon", "Holosakos", "Divine Benison", "Divine Caress", "Intersection",
        "Divine Veil", "Shake It Off", "The Blackest Night", "Improvised Finish", "Radiant Aegis", "Stoneskin",
    };

    /// <summary>
    /// The jobs' damage over time on enemies. A tick the game sent without saying whose is shared
    /// out between the party's on the target (and marked estimated); it's never handed to one player
    /// on a guess.
    /// </summary>
    public static readonly IReadOnlyList<string> DotNames = new[]
    {
        "Circle of Scorn", "Goring Blade", "Sonic Break", "Bow Shock", "Dia", "Aero II", "Aero", "Biolysis", "Bio II", "Bio",
        "Baneful Impaction", "Combust III", "Combust II", "Combust", "Eukrasian Dosis III", "Eukrasian Dosis II",
        "Eukrasian Dosis", "Eukrasian Dyskrasia", "Chaotic Spring", "Chaos Thrust", "Demolish", "Higanbana", "Caustic Bite",
        "Stormbite", "Venomous Bite", "Windbite", "Bioblaster", "Flamethrower", "High Thunder", "High Thunder II",
        "Thunder III", "Thunder IV", "Thunder II", "Thunder", "Salted Earth", "Doton", "Slipstream",
    };

    /// <summary>A status that's no mechanic-failure debuff.</summary>
    public const int NotAFail = 0;

    /// <summary>Damage Down: what a failed mechanic often leaves.</summary>
    public const int FailDamageDown = 1;

    /// <summary>Vulnerability Up: the other one.</summary>
    public const int FailVulnerability = 2;

    /// <summary>
    /// Whether a status is a damage down or a vulnerability up, by its English name: exactly "Damage
    /// Down" or "Vulnerability Up" (the Status sheet's 18 and 22 rows of those names in November 2026),
    /// as the site's <c>model/debuffs.ts</c> lists them by id. "Physical Vulnerability Up" and "Magic
    /// Vulnerability Up" aren't failures: tankbusters put them on the tanks on purpose (to make them swap).
    /// </summary>
    public static int FailKind(string? englishName)
    {
        if (string.IsNullOrWhiteSpace(englishName)) return NotAFail;
        var name = englishName.Trim();
        if (name.Equals("Damage Down", StringComparison.OrdinalIgnoreCase)) return FailDamageDown;
        if (name.Equals("Vulnerability Up", StringComparison.OrdinalIgnoreCase)) return FailVulnerability;
        return NotAFail;
    }
}
