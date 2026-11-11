using System.Collections.Generic;

namespace RaidPlanner.Core.Meter;

/// <summary>The meter's four tabs.</summary>
public enum MeterTab
{
    /// <summary>Damage done: ENCDPS (or DPS) and the share of the party's damage.</summary>
    Damage,

    /// <summary>Healing that landed (HPS) and overheal.</summary>
    Healing,

    /// <summary>Damage taken from enemies and the floor, bleeds and DoTs included.</summary>
    Taken,

    /// <summary>Who died, when, and to what.</summary>
    Deaths,
}

/// <summary>What the meter ranks damage by (Settings): damage over the encounter, or over each player's own time.</summary>
public enum MeterRank
{
    /// <summary>Damage over the whole encounter's length, as ACT shows it.</summary>
    EncDps,

    /// <summary>Damage over each player's own active time (first hit to last).</summary>
    Dps,
}

/// <summary>What pace compares the current encounter with (the picker's choice).</summary>
public enum PaceAgainst
{
    /// <summary>The kept encounter of the same duty that got furthest.</summary>
    Best,

    /// <summary>The kept encounter of the same duty just before this one.</summary>
    Last,

    /// <summary>No pace line.</summary>
    Off,
}

/// <summary>
/// One row of the meter: a party member (or the limit break, <see cref="IsLimitBreak"/>), with
/// every number any tab or column shows. Amounts are whole numbers as the game gave them; rates
/// are 0–1; times are seconds since the encounter began. Pets and summons are in their owner's row.
/// Names are the party list's, shown only in the game (never sent anywhere).
/// </summary>
public sealed record MeterRow(
    int PartyId,
    string Slot,
    string Name,
    string Job,
    string Role,
    bool IsYou,
    bool IsLimitBreak,
    long Damage,
    double EncDps,
    double Dps,
    double DamageShare,
    double CritRate,
    double DirectHitRate,
    long MaxHit,
    string MaxHitName,
    long Healing,
    long Overheal,
    double Hps,
    double OverhealRate,
    double HealingShare,
    double HealCritRate,
    long MaxHeal,
    long Taken,
    double TakenPerSecond,
    double TakenShare,
    long MaxTaken,
    int Deaths,
    bool Dead,
    double? DiedAt,
    double ActiveSeconds);

/// <summary>
/// One ability in a player's breakdown (by ability, for one tab): its own row for a DoT/HoT's ticks
/// (<see cref="IsTick"/>; the game's tick doesn't say whether it crit, so those rates are null),
/// pets' actions named with the pet. <see cref="Estimated"/> when the game sent one tick for several
/// players' DoTs and the share was worked out.
/// </summary>
public sealed record AbilityRow(
    uint ActionId,
    string Name,
    bool IsTick,
    bool IsPet,
    long Amount,
    double Share,
    int Hits,
    double? CritRate,
    double? DirectHitRate,
    long Max,
    bool Estimated);

/// <summary>A line of a death's recap: a hit (negative amount), a heal or shield (positive), with the HP before it.</summary>
public sealed record RecapLine(double At, string What, long Amount, long HpBefore, string Note);

/// <summary>
/// A death: who, when, the killing blow, how far over it went, the recap (oldest first), and the
/// dying member's max HP as they died (<see cref="MaxHp"/>, 0 when not known: what the recap's HP
/// bars are drawn against; added in the review round).
/// </summary>
public sealed record DeathEntry(
    int PartyId,
    string Slot,
    string Name,
    double At,
    string KilledBy,
    long Amount,
    long Overkill,
    bool PartOfWipe,
    IReadOnlyList<RecapLine> Recap,
    long MaxHp = 0);

/// <summary>
/// Pace: the party's damage since the current phase began, against the compared encounter's at the
/// same second of the same phase. <see cref="Ahead"/> is the fraction ahead (negative: behind).
/// </summary>
public sealed record PaceLine(int AgainstPull, PaceAgainst Against, string Phase, double PhaseSeconds, long Damage, long AgainstDamage, double Ahead);

/// <summary>
/// One encounter as the meter shows it: live or kept. <see cref="Rows"/> are the party in spot
/// order with the limit break last (only when it was used); <see cref="Party"/> is the total row.
/// </summary>
public sealed record MeterEncounter(
    int Id,
    int Pull,
    string Duty,
    uint Content,
    double Started,
    double Duration,
    bool Live,
    Outcome? Ended,
    double Reached,
    string? Phase,
    double PhaseAt,
    IReadOnlyList<MeterRow> Rows,
    MeterRow Party,
    IReadOnlyList<DeathEntry> Deaths);

/// <summary>
/// Where the meter's numbers come from (Core/Meter's parser, fed by the game's hooks). The UI only
/// reads from this, on the framework thread, once a frame; every call is cheap (snapshots are
/// rebuilt at most a few times a second, not per call).
/// </summary>
public interface IMeterSource
{
    /// <summary>The encounter going on now, or the last one (kept) between encounters; null before any.</summary>
    MeterEncounter? Current { get; }

    /// <summary>Whether an encounter is going on (the first damage between the party and an enemy has landed and it hasn't ended).</summary>
    bool InEncounter { get; }

    /// <summary>In combat but no damage yet (waiting for the first hit).</summary>
    bool Waiting { get; }

    /// <summary>The encounters kept this session, newest first (at most a few dozen), the current one included.</summary>
    IReadOnlyList<MeterEncounter> Kept { get; }

    /// <summary>The kept encounter with this <see cref="MeterEncounter.Id"/>, or null.</summary>
    MeterEncounter? Get(int id);

    /// <summary>One player's breakdown by ability for a tab (Damage, Healing or Taken: for Taken, by what hit them).</summary>
    IReadOnlyList<AbilityRow> Abilities(int encounterId, int partyId, MeterTab tab);

    /// <summary>Pace for an encounter against another kept one of the same duty, or null when it isn't meaningful.</summary>
    PaceLine? Pace(int encounterId, PaceAgainst against);

    /// <summary>The kept encounter of the same duty that got furthest (the picker's star), or null.</summary>
    MeterEncounter? Best(uint content);

    /// <summary>Forget every kept encounter (Settings' "Clear").</summary>
    void Clear();

    /// <summary>
    /// Why the meter has stopped, in words for the Meter tab's warning box, or null while it works:
    /// it turns itself off rather than show nonsense when what it reads makes no sense (a single
    /// hit or heal over five million that isn't a limit break: the game has moved something). Added
    /// by the data engineer (round three), for the mock-up's warning. (A heal landing on an enemy is
    /// only left out: an enemy that absorbs an element, as Eureka's do, is healed by the party's hits.)
    /// </summary>
    string? Problem { get; }
}
