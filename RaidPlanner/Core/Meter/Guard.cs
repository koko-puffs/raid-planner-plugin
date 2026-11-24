using System;
using System.Collections.Generic;

namespace RaidPlanner.Core.Meter;

/// <summary>
/// What barriers absorbed, worked out from what the meter already reads (since 0.9.0; nothing new is recorded). The game
/// doesn't say how much of a hit a barrier soaked: the hit's amount is the whole hit, and the target's barrier is a % of their
/// max HP (<c>ShieldValue</c>, read with their HP as the hit's message arrives: <see cref="TakenHit.Shield"/>). The barrier
/// then is the one before the hit (the game takes a hit off a moment after its message, as it does the HP), so each hit's
/// absorbed is the barrier before it, as far as the hit goes: min(hit, barrier). Hits that come before the game has shown the
/// last one's effect (the same % read again within <see cref="Settle"/> seconds) share that barrier: what the earlier ones
/// took is taken off it first. An estimate (the % is whole, so to about 1% of max HP); a hit's own "partly absorbed" flag
/// (<see cref="Effects.FxAbsorbed"/>, unverified in the game) isn't needed for it.
/// </summary>
public sealed class ShieldBook
{
    /// <summary>How long a barrier read the same is taken to be the same reading (the game shows a hit's effect within about a second).</summary>
    public const double Settle = 1.5;

    private readonly Dictionary<uint, (int Pct, double At, long Took)> seen = new();

    /// <summary>What a hit's barrier absorbed (0 none, or not known): the barrier before it (% of max HP) less what earlier hits on the same reading took.</summary>
    public long Absorb(uint target, double t, int pct, long maxHp, long amount)
    {
        if (pct <= 0 || maxHp <= 0)
        {
            seen.Remove(target);
            return 0;
        }
        if (amount <= 0) return 0;
        var pool = Math.Min(100, pct) * maxHp / 100;
        long took = 0;
        var at = t;
        if (seen.TryGetValue(target, out var s) && s.Pct == pct && t - s.At <= Settle && t >= s.At)
        {
            took = s.Took;
            at = s.At;
        }
        var absorbed = Math.Clamp(pool - took, 0, amount);
        seen[target] = (pct, at, took + absorbed);
        if (seen.Count > 64) seen.Clear();
        return absorbed;
    }

    public void Clear() => seen.Clear();
}

/// <summary>A status someone carries that the Mitigation tab reads: which, who put it on (a party member's entity id, a pet's owner; 0 no one in the party), what it does.</summary>
public readonly record struct Worn(uint Status, uint Source, MitigationKind Kind);

/// <summary>The Mitigation tab's rules, apart so they're tested: what a big hit is, and how it was met. Since 0.9.0.</summary>
public static class GuardRules
{
    /// <summary>A hit on one or two that took at least this share of the target's max HP (the whole hit, a barrier's part included) is a buster.</summary>
    public const double BusterShare = 0.3;

    /// <summary>A hit on most of the party is a raid-wide when it took at least this share of their max HP on average.</summary>
    public const double RaidwideShare = 0.12;

    /// <summary>Hits of one enemy action this close together (seconds) are one hit (a raid-wide sent target by target).</summary>
    public const double Together = 0.75;

    /// <summary>
    /// What a hit is: a buster on one (<paramref name="targets"/> 1) or two, at least <see cref="BusterShare"/> of their max HP
    /// (<paramref name="biggestShare"/>: the biggest hit's); a raid-wide on most of the party (at least three, more than half of
    /// <paramref name="party"/>), <see cref="RaidwideShare"/> of their max HP on average; else null (an auto-attack, a puddle).
    /// </summary>
    public static BigHitKind? Classify(int targets, int party, double biggestShare, double meanShare)
    {
        if (targets <= 0) return null;
        if (targets <= 2 && biggestShare >= BusterShare && (targets == 1 || targets < party)) return targets == 1 ? BigHitKind.Buster : BigHitKind.SharedBuster;
        if (targets >= 3 && targets * 2 > party && meanShare >= RaidwideShare) return BigHitKind.Raidwide;
        return null;
    }

    /// <summary>
    /// How a big hit was met: died within <see cref="MeterSession.DiedAfterHit"/> s; an invulnerability or two mitigations up
    /// (on them, on the enemy, a barrier that soaked some of it) is mitigated; one is light; none is unmitigated.
    /// </summary>
    public static MitigationVerdict Verdict(int up, bool invulnerable, bool died) =>
        died ? MitigationVerdict.Died : invulnerable || up >= 2 ? MitigationVerdict.Mitigated : up == 1 ? MitigationVerdict.Light : MitigationVerdict.Unmitigated;

    /// <summary>
    /// What a barrier absorbed shared between those whose barriers were on (<paramref name="sources"/>, distinct, in the order
    /// given): a whole share each, the remainder to the first. Nothing when none are known (the barrier was no one's in the party).
    /// </summary>
    public static void Split(long absorbed, ReadOnlySpan<uint> sources, Span<long> shares)
    {
        if (sources.Length == 0) return;
        var each = absorbed / sources.Length;
        for (var i = 0; i < sources.Length; i++) shares[i] = i == 0 ? absorbed - each * (sources.Length - 1) : each;
    }
}
