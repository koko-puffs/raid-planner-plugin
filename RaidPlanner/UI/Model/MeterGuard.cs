using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.UI.Model;

/// <summary>How a stat card's number is coloured: as the rest, a warning (much overheal), bad (a buster met with nothing).</summary>
public enum CardTone
{
    Plain,
    Warn,
    Bad,
}

/// <summary>A stat card of the opened-up meter's Healing and Mitigation tabs: its label (capitals as drawn), its number, the line under it, its tone, what it says when pointed at.</summary>
public sealed record StatCard(string Label, string Value, string Sub, CardTone Tone = CardTone.Plain, string? Tip = null);

/// <summary>A big hit's line on the Mitigation tab, in words: its time, the enemy's action, what it was, the game's amount, the HP it took after barriers (null: none soaked any), the mitigations up, the tag.</summary>
public sealed record HitLine(string Time, string Ability, string Kind, string Amount, string? After, IReadOnlyList<string> Up, string Verdict, MitigationVerdict Tag, string Tip);

/// <summary>
/// What the opened-up meter's Healing and Mitigation tabs say (since 0.9.0), worked out away from ImGui so it's tested: the
/// healer's cards and their abilities' cells, a member's mitigation cards and their big hits' lines.
/// </summary>
public static class MeterGuard
{
    /// <summary>Overheal from this share up is coloured as a warning.</summary>
    public const double MuchOverheal = 0.3;

    /// <summary>The Healing tab's cards for a member: healing that landed (and a second), overheal (and the ability that wasted most), shields absorbed (an estimate), heals cast on and off the GCD.</summary>
    public static StatCard[] HealerCards(MeterRow r, IReadOnlyList<AbilityRow> abilities, MeterProtection? p)
    {
        var wasted = abilities.Where(a => a.Over > 0).OrderByDescending(a => a.Over).FirstOrDefault();
        var over = r.Overheal <= 0 ? "Nothing wasted"
            : wasted != null ? $"{MeterNumber.Short(r.Overheal)} wasted, most from {wasted.Name}" : $"{MeterNumber.Short(r.Overheal)} wasted";
        var absorbed = p?.Absorbed ?? r.Absorbed;
        var shieldSub = absorbed <= 0 ? "None of theirs soaked a hit"
            : p is { AbsorbedBy.Count: > 0 } ? $"Estimate · most from {p.AbsorbedBy[0].Name}" : "Estimate";
        return new[]
        {
            new StatCard("Healing that landed", MeterNumber.Short(r.Healing), $"{Format.Thousands((long)Math.Round(r.Hps))} a second"),
            new StatCard("Overheal", Format.PercentWhole(r.OverhealRate), over, r.OverhealRate >= MuchOverheal ? CardTone.Warn : CardTone.Plain),
            new StatCard("Shields absorbed", absorbed > 0 ? MeterNumber.Short(absorbed) : "–", shieldSub, CardTone.Plain,
                "What the barriers they put on soaked of enemies' hits: an estimate, from the barrier the game shows on each member before each hit "
                + "(as a whole % of their HP), credited to whoever's barrier it was (shared when there were several). Never counted as healing."),
            new StatCard("Heals cast", p == null ? "–" : $"{p.GcdHeals.ToString(CultureInfo.InvariantCulture)} GCD",
                p == null ? string.Empty : $"{p.OgcdHeals.ToString(CultureInfo.InvariantCulture)} off the GCD", CardTone.Plain,
                "Each use of a heal or a barrier once, however many it landed on (a pet's left out)."),
        };
    }

    /// <summary>The Healing tab's ability columns' heads.</summary>
    public static readonly string[] HealerHeads = { "Landed", "Share", "Over", "Casts", "Biggest" };

    /// <summary>An ability's cells on the Healing tab: what it landed, its share, its overheal, its casts (a HoT's ticks), its biggest heal.</summary>
    public static string[] HealerCells(AbilityRow a) => new[]
    {
        MeterNumber.Short(a.Amount), Format.PercentWhole(a.Share), Format.PercentWhole(a.OverRate),
        a.Casts > 0 ? Format.Thousands(a.Casts) : "–", Format.Thousands(a.Max),
    };

    /// <summary>
    /// The Mitigation tab's cards for a member: damage taken (and their share), their own mitigation's time up while they held a
    /// boss, the busters on them (and how many met nothing), self-healing (and with what).
    /// </summary>
    public static StatCard[] GuardCards(MeterRow r, MeterProtection? p)
    {
        var uptime = p?.Uptime;
        var held = p == null || p.HeldSeconds < 1 ? "Never held a boss" : $"Of the {Format.Clock(p.HeldSeconds)} they held a boss";
        var busters = p?.Busters ?? 0;
        var bare = p?.BustersBare ?? 0;
        var bustersSub = busters == 0 ? "None on them" : bare == 0 ? "Something up for each" : bare == 1 ? "1 with nothing up" : $"{bare} with nothing up";
        var self = p?.SelfHealing ?? 0;
        var selfSub = p is { SelfHealingBy.Count: > 0 } ? string.Join(", ", p.SelfHealingBy.Take(2).Select(x => x.Name)) : "None";
        return new[]
        {
            new StatCard("Damage taken", MeterNumber.Short(r.Taken), $"{Format.PercentWhole(r.TakenShare)} of the party's"),
            new StatCard("Mitigation up", uptime is { } u ? Format.PercentWhole(u) : "–", held, CardTone.Plain,
                "The share of the time a boss was on them (not a cast's target) that one of their own was up: a mitigation, barrier or "
                + "invulnerability they put on themselves, or their Reprisal (Addle, Feint, Dismantle) on that boss."),
            new StatCard("Tank busters", busters.ToString(CultureInfo.InvariantCulture), bustersSub, bare > 0 ? CardTone.Bad : CardTone.Plain,
                $"A buster is an enemy's hit on one or two of {Format.PercentWhole(GuardRules.BusterShare)} of their HP or more (a barrier's part included)."),
            new StatCard("Self-healing", self > 0 ? MeterNumber.Short(self) : "–", selfSub, CardTone.Plain,
                "What their heals on themselves put back (the overheal left out): their own heals, lifesteal, their HoTs on themselves."),
        };
    }

    /// <summary>A big hit as its line says it.</summary>
    public static HitLine Line(BigHit h)
    {
        var kind = h.Kind switch
        {
            BigHitKind.Buster => "Buster",
            BigHitKind.SharedBuster => "Shared buster",
            _ => $"Raid-wide · {h.Targets.ToString(CultureInfo.InvariantCulture)} hit",
        };
        var after = h.Absorbed > 0 ? Format.Thousands(Math.Max(0, h.Amount - h.Absorbed)) : null;
        var up = h.Up.Select(u => u.Own || u.By.Length == 0 ? u.Name : $"{u.Name} ({u.By})").ToList();
        var tip = h.Absorbed > 0
            ? $"{Format.Thousands(h.Amount)} hit them; barriers soaked about {Format.Thousands(h.Absorbed)} of it (an estimate), so it took about {after} HP."
            : $"{Format.Thousands(h.Amount)} hit them; no barrier soaked any of it.";
        return new HitLine(Format.Clock(Math.Max(0, h.At)), h.Ability, kind, Format.Thousands(h.Amount), after, up, Verdict(h.Verdict), h.Verdict, tip);
    }

    /// <summary>A verdict's tag word.</summary>
    public static string Verdict(MitigationVerdict v) => v switch
    {
        MitigationVerdict.Mitigated => "Mitigated",
        MitigationVerdict.Light => "Light",
        MitigationVerdict.Unmitigated => "Unmitigated",
        _ => "Died",
    };
}
