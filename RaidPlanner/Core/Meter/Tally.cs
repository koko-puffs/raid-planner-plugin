using System;
using System.Collections.Generic;

namespace RaidPlanner.Core.Meter;

/// <summary>
/// One line of a breakdown by ability: an action, a DoT/HoT's ticks (<see cref="Tick"/>; named by the
/// action that put it on, or by its status when that's not known: <see cref="IsStatus"/>), a pet's.
/// </summary>
public readonly record struct AbilityKey(uint Id, bool Tick, bool Pet, bool IsStatus);

/// <summary>An ability's totals: the amount (healing: what it restored), overheal, hits, crits, direct hits, the biggest.</summary>
public sealed class AbilityTotal
{
    public long Amount;
    public long Over;
    public int Hits;
    public int Crits;
    public int DirectHits;
    public long Max;

    /// <summary>Some of it came from ticks the game sent for several players, shared out.</summary>
    public bool Estimated;
}

/// <summary>
/// A party member's totals for one encounter (or pull): damage, with hits, crits and direct hits
/// (of direct hits only: the game's tick doesn't say whether it crit), the biggest hit, the first
/// and last damage (their active time); healing that landed, overheal and heal crits; damage taken;
/// shields put on; and each by ability.
/// </summary>
public sealed class MemberTotals
{
    public long Damage;
    public int Hits;
    public int Crits;
    public int DirectHits;
    public long MaxHit;
    public AbilityKey MaxHitBy;
    public double FirstHit = double.NaN;
    public double LastHit = double.NaN;

    /// <summary>Healing that landed (the heals less their overheal).</summary>
    public long Healing;

    public long Overheal;
    public int Heals;
    public int HealCrits;
    public long MaxHeal;
    public long Taken;
    public long MaxTaken;
    public int Shields;

    public readonly Dictionary<AbilityKey, AbilityTotal> DamageBy = new();
    public readonly Dictionary<AbilityKey, AbilityTotal> HealingBy = new();
    public readonly Dictionary<AbilityKey, AbilityTotal> TakenBy = new();

    /// <summary>Seconds from their first damage to their last (0 without any).</summary>
    public double Active => double.IsNaN(FirstHit) ? 0 : LastHit - FirstHit;

    /// <summary>Crits and direct hits as shares of their direct hits (0 without any).</summary>
    public double CritRate => Hits == 0 ? 0 : (double)Crits / Hits;

    public double DirectHitRate => Hits == 0 ? 0 : (double)DirectHits / Hits;

    public double HealCritRate => Heals == 0 ? 0 : (double)HealCrits / Heals;

    internal static AbilityTotal Line(Dictionary<AbilityKey, AbilityTotal> by, AbilityKey key)
    {
        if (!by.TryGetValue(key, out var line)) by[key] = line = new AbilityTotal();
        return line;
    }
}

/// <summary>
/// Totals for an encounter, by party member (any number the caller uses: the meter's, the
/// recording's), the limit break on its own (never anyone's damage), and the party's (the limit
/// break included, so the shares add up to 100). What the meter shows and what the summary's
/// <c>stats</c> are made of: the same sums either way.
/// </summary>
public sealed class Tally
{
    private readonly Dictionary<int, MemberTotals> members = new();

    public MemberTotals LimitBreak { get; } = new();

    /// <summary>Damage done by the party, the limit break included.</summary>
    public long PartyDamage { get; private set; }

    public long PartyHealing { get; private set; }

    public long PartyOverheal { get; private set; }

    public long PartyTaken { get; private set; }

    public bool LimitBreakUsed => LimitBreak.Hits > 0 || LimitBreak.Damage > 0;

    public MemberTotals Of(int member)
    {
        if (!members.TryGetValue(member, out var m)) members[member] = m = new MemberTotals();
        return m;
    }

    public bool Has(int member) => members.ContainsKey(member);

    public IReadOnlyDictionary<int, MemberTotals> Members => members;

    private static AbilityKey Key(uint action, uint status, bool tick, bool pet) =>
        tick && action == 0 ? new AbilityKey(status, true, pet, true) : new AbilityKey(action, tick, pet, false);

    /// <summary>Damage done: a direct hit, or (<paramref name="tick"/>) a DoT's tick, named by its action or else its status.</summary>
    public void Damage(int member, double t, uint action, long amount, FxFlags flags, bool tick = false, uint status = 0, bool estimated = false)
    {
        if (amount < 0) return;
        var m = (flags & FxFlags.LimitBreak) != 0 ? LimitBreak : Of(member);
        var key = Key(action, status, tick, (flags & FxFlags.Pet) != 0);
        var line = MemberTotals.Line(m.DamageBy, key);
        m.Damage += amount;
        line.Amount += amount;
        line.Hits++;
        if (estimated) line.Estimated = true;
        if (!tick)
        {
            m.Hits++;
            if ((flags & FxFlags.Crit) != 0)
            {
                m.Crits++;
                line.Crits++;
            }
            if ((flags & FxFlags.DirectHit) != 0)
            {
                m.DirectHits++;
                line.DirectHits++;
            }
        }
        if (amount > line.Max) line.Max = amount;
        if (amount > m.MaxHit)
        {
            m.MaxHit = amount;
            m.MaxHitBy = key;
        }
        if (double.IsNaN(m.FirstHit) || t < m.FirstHit) m.FirstHit = t;
        if (double.IsNaN(m.LastHit) || t > m.LastHit) m.LastHit = t;
        PartyDamage += amount;
    }

    /// <summary>A heal (or a HoT's tick): it counts for what it restored, <paramref name="amount"/> less <paramref name="over"/>.</summary>
    public void Heal(int member, uint action, long amount, long over, FxFlags flags, bool tick = false, uint status = 0, bool estimated = false)
    {
        if (amount < 0) return;
        over = Math.Clamp(over, 0, amount);
        var m = (flags & FxFlags.LimitBreak) != 0 ? LimitBreak : Of(member);
        var line = MemberTotals.Line(m.HealingBy, Key(action, status, tick, (flags & FxFlags.Pet) != 0));
        var landed = amount - over;
        m.Healing += landed;
        m.Overheal += over;
        line.Amount += landed;
        line.Over += over;
        line.Hits++;
        if (estimated) line.Estimated = true;
        if (!tick)
        {
            m.Heals++;
            if ((flags & FxFlags.Crit) != 0)
            {
                m.HealCrits++;
                line.Crits++;
            }
        }
        if (amount > line.Max) line.Max = amount;
        if (amount > m.MaxHeal) m.MaxHeal = amount;
        PartyHealing += landed;
        PartyOverheal += over;
    }

    /// <summary>Damage taken from an enemy's action, or (<paramref name="tick"/>) a bleed or DoT, by what it was.</summary>
    public void Taken(int member, uint what, bool tick, long amount)
    {
        if (amount < 0) return;
        var m = Of(member);
        var line = MemberTotals.Line(m.TakenBy, new AbilityKey(what, tick, false, tick));
        m.Taken += amount;
        line.Amount += amount;
        line.Hits++;
        if (amount > line.Max) line.Max = amount;
        if (amount > m.MaxTaken) m.MaxTaken = amount;
        PartyTaken += amount;
    }

    /// <summary>A barrier put on someone: counted, never healing.</summary>
    public void Shield(int member) => Of(member).Shields++;
}
