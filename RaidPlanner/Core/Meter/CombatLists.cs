using System;
using System.Collections.Generic;
using System.Linq;

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

    // ---------------------------------------------------------------------------------------------
    // Since 0.4.0: what the recording's new tables and events, the summary and the meter pick out.
    // Like the lists above they go by English names; the game side (GameData.Kinds) finds every
    // row with each name on the Action or Status sheet, read in English.

    /// <summary>The cooldown group of the GCD (the Action sheet's CooldownGroup 58: Raise, Chaos Thrust, Gibbet…).</summary>
    public const int GcdGroup = 58;

    /// <summary>
    /// Is an action a weaponskill or spell on the GCD: its cooldown group, or its additional one, is the
    /// GCD's (Gnashing Fang, Drill, Technical Step and the mudras share the GCD through the second).
    /// </summary>
    public static bool IsGcd(int cooldownGroup, int cooldownGroup2) => cooldownGroup == GcdGroup || cooldownGroup2 == GcdGroup;

    /// <summary>The ActionCategory of auto-attacks (1): already in <c>fx</c>, never a <c>use</c> row.</summary>
    public static bool IsAutoAttack(int category) => category == 1;

    /// <summary>A high-quality item's id as the game sends it: the Item sheet's row plus this.</summary>
    public const uint HqOffset = 1_000_000;

    /// <summary>The party's interrupts (and stuns that interrupt): who stopped an enemy's cast, when the game didn't say.</summary>
    public static readonly IReadOnlyList<string> InterruptNames = new[] { "Interject", "Head Graze", "Low Blow", "Leg Sweep" };

    /// <summary>The healers' (and Red Mage's) raises: who raised a dead member, when no Raise status says.</summary>
    public static readonly IReadOnlyList<string> RaiseActionNames = new[] { "Raise", "Resurrection", "Ascend", "Verraise", "Egeiro" };

    /// <summary>The status a raise puts on the dead member until they accept it (148, 1140).</summary>
    public static readonly IReadOnlyList<string> RaiseStatusNames = new[] { "Raise" };

    /// <summary>The statuses that stop knockbacks and draw-ins.</summary>
    public static readonly IReadOnlyList<string> KnockbackImmunityNames = new[] { "Arm's Length", "Surecast" };

    /// <summary>Control on enemies: stuns, sleeps, binds, heavies, silences.</summary>
    public static readonly IReadOnlyList<string> ControlNames = new[] { "Stun", "Sleep", "Bind", "Heavy", "Silence" };

    /// <summary>What a raised member carries for a while: Weakness, then (raised again with it) Brink of Death.</summary>
    public static readonly IReadOnlyList<string> WeaknessNames = new[] { "Weakness", "Brink of Death" };

    /// <summary>The tanks' Provoke (top of the enemy's list) and everyone's Shirk (enmity handed to another).</summary>
    public static readonly IReadOnlyList<string> ProvokeNames = new[] { "Provoke" };

    public static readonly IReadOnlyList<string> ShirkNames = new[] { "Shirk" };
}

/// <summary>
/// Which actions and statuses are which, by id, for the recording, the summary and the meter (each
/// question false by default: tests set only what they need). The game side makes one from the
/// English sheets (<c>GameData.Kinds</c>, the names in <see cref="CombatLists"/>); <see cref="FromNames"/>
/// makes one from any names, as the tests do.
/// </summary>
public sealed class CombatKinds
{
    public static readonly CombatKinds None = new();

    /// <summary>Interject, Head Graze, Low Blow, Leg Sweep.</summary>
    public Func<uint, bool> Interrupt { get; init; } = _ => false;

    /// <summary>Raise, Resurrection, Ascend, Verraise, Egeiro.</summary>
    public Func<uint, bool> RaiseAction { get; init; } = _ => false;

    /// <summary>The Raise status (on a dead member until they accept).</summary>
    public Func<uint, bool> RaiseStatus { get; init; } = _ => false;

    /// <summary>Weakness (1) or Brink of Death (2), 0 for any other status.</summary>
    public Func<uint, int> Weakness { get; init; } = _ => 0;

    /// <summary>Stun, Sleep, Bind, Heavy, Silence.</summary>
    public Func<uint, bool> Control { get; init; } = _ => false;

    /// <summary>Arm's Length, Surecast.</summary>
    public Func<uint, bool> KnockbackImmunity { get; init; } = _ => false;

    public Func<uint, bool> Provoke { get; init; } = _ => false;

    public Func<uint, bool> Shirk { get; init; } = _ => false;

    /// <summary>Is an action on the GCD (<see cref="CombatLists.IsGcd"/>, from the Action sheet's cooldown groups).</summary>
    public Func<uint, bool> Gcd { get; init; } = _ => false;

    /// <summary>The kinds by English names: an action's (<paramref name="actionName"/>) and a status's, null for none.</summary>
    public static CombatKinds FromNames(Func<uint, string?> actionName, Func<uint, string?> statusName, Func<uint, bool>? gcd = null)
    {
        bool In(IReadOnlyList<string> names, string? name) =>
            name != null && names.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
        return new CombatKinds
        {
            Interrupt = id => In(CombatLists.InterruptNames, actionName(id)),
            RaiseAction = id => In(CombatLists.RaiseActionNames, actionName(id)),
            RaiseStatus = id => In(CombatLists.RaiseStatusNames, statusName(id)),
            Weakness = id => statusName(id)?.Trim() switch
            {
                { } n when n.Equals("Weakness", StringComparison.OrdinalIgnoreCase) => 1,
                { } n when n.Equals("Brink of Death", StringComparison.OrdinalIgnoreCase) => 2,
                _ => 0,
            },
            Control = id => In(CombatLists.ControlNames, statusName(id)),
            KnockbackImmunity = id => In(CombatLists.KnockbackImmunityNames, statusName(id)),
            Provoke = id => In(CombatLists.ProvokeNames, actionName(id)),
            Shirk = id => In(CombatLists.ShirkNames, actionName(id)),
            Gcd = gcd ?? (_ => false),
        };
    }
}
