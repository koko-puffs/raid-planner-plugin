using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.UI.Model;

/// <summary>One of the picker's rows: an encounter, whether it's the one shown, live, or the best.</summary>
public sealed record PickerRow(MeterEncounter Fight, bool Shown, bool Live, bool Best);

/// <summary>
/// What the meter's windows show of an encounter, worked out away from ImGui so it's tested: the
/// rows in order for a tab (biggest first, the limit break apart), each row's number and its second
/// column, the bars' lengths (to the top player's), the party's line, the picker's order, pace's bar.
/// </summary>
public static class MeterView
{
    /// <summary>The party's rows for a tab, biggest first (the limit break left out: it has its own row).</summary>
    public static List<MeterRow> Ranked(IReadOnlyList<MeterRow> rows, MeterTab tab, MeterRank rank)
    {
        var list = rows.Where(r => !r.IsLimitBreak).ToList();
        list.Sort((a, b) =>
        {
            var c = Value(b, tab, rank).CompareTo(Value(a, tab, rank));
            return c != 0 ? c : string.CompareOrdinal(a.Slot, b.Slot);
        });
        return list;
    }

    /// <summary>The limit break's row, when it was used.</summary>
    public static MeterRow? LimitBreak(IReadOnlyList<MeterRow> rows) => rows.FirstOrDefault(r => r.IsLimitBreak);

    /// <summary>A row's number on a tab: ENCDPS or DPS, HPS, damage taken.</summary>
    public static double Value(MeterRow r, MeterTab tab, MeterRank rank) => tab switch
    {
        MeterTab.Healing => r.Hps,
        MeterTab.Taken => r.Taken,
        _ => rank == MeterRank.Dps && !r.IsLimitBreak ? r.Dps : r.EncDps,
    };

    /// <summary>A row's number as the meter writes it: "27,840"; a big total in a narrow column "13.1M".</summary>
    public static string ValueText(MeterRow r, MeterTab tab, MeterRank rank, bool narrow = false)
    {
        var v = (long)Math.Round(Value(r, tab, rank));
        return narrow && tab == MeterTab.Taken ? Format.Compact(v) : Format.Thousands(v);
    }

    /// <summary>The second column: the share of the party's damage ("16.9"), overheal ("31%"), the share of what the party took.</summary>
    public static string Second(MeterRow r, MeterTab tab) => tab switch
    {
        MeterTab.Healing => Format.PercentWhole(r.OverhealRate),
        MeterTab.Taken => Format.Share(r.TakenShare),
        _ => Format.Share(r.DamageShare),
    };

    /// <summary>The two columns' heads.</summary>
    public static (string Value, string Second) Heads(MeterTab tab, MeterRank rank) => tab switch
    {
        MeterTab.Healing => ("HPS", "Over"),
        MeterTab.Taken => ("Taken", "%"),
        _ => (rank == MeterRank.Dps ? "DPS" : "ENCDPS", "%"),
    };

    /// <summary>A row's bar: its number against the top player's (0–1).</summary>
    public static float Bar(MeterRow r, double top, MeterTab tab, MeterRank rank) =>
        top <= 0 ? 0 : (float)Math.Clamp(Value(r, tab, rank) / top, 0, 1);

    /// <summary>The party's line at the foot: "Party · 165,000" (and a unit after it: "HPS").</summary>
    public static (string Value, string? Unit) Party(MeterEncounter e, MeterTab tab, MeterRank rank) => tab switch
    {
        MeterTab.Healing => (Format.Thousands((long)Math.Round(e.Party.Hps)), "HPS"),
        MeterTab.Taken => (Format.Thousands(e.Party.Taken), null),
        _ => (Format.Thousands((long)Math.Round(rank == MeterRank.Dps ? e.Party.Dps : e.Party.EncDps)), null),
    };

    /// <summary>The deaths that count (the wipe's left out), in the order they came.</summary>
    public static List<DeathEntry> Deaths(MeterEncounter e) => e.Deaths.Where(d => !d.PartOfWipe).OrderBy(d => d.At).ToList();

    /// <summary>Where in the fight: "P2 · 0:41 in" (the phase and how far into it), or null with no phases.</summary>
    public static string? Where(MeterEncounter e) =>
        e.Phase is { Length: > 0 } p ? $"{p} · {Format.Clock(Math.Max(0, e.Duration - e.PhaseAt))} in" : null;

    /// <summary>
    /// The picker's rows: the shown encounter's duty's fights, newest first (at most
    /// <paramref name="max"/>), the best (the one that got furthest) starred and added at the end if
    /// it's older; then the other duties' fights, newest first.
    /// </summary>
    public static (List<PickerRow> Here, List<PickerRow> Others) Picker(IReadOnlyList<MeterEncounter> kept, MeterEncounter? shown, MeterEncounter? best, int max = 8)
    {
        var content = shown?.Content ?? kept.FirstOrDefault()?.Content ?? 0;
        var here = new List<PickerRow>();
        var others = new List<PickerRow>();
        var bestId = best?.Id;
        foreach (var f in kept.OrderByDescending(k => k.Started))
        {
            var row = new PickerRow(f, f.Id == shown?.Id, f.Live, f.Id == bestId);
            if (f.Content == content)
            {
                if (here.Count < max) here.Add(row);
            }
            else if (others.Count < max) others.Add(row);
        }
        if (best != null && best.Content == content && here.All(r => r.Fight.Id != best.Id))
            here.Add(new PickerRow(best, best.Id == shown?.Id, best.Live, true));
        return (here, others);
    }

    /// <summary>Pace's bar: this pull's fill and the other's tick, the bigger at the full width ("on pull 4 +3.4%").</summary>
    public static (float Fill, float Mark) PaceBar(PaceLine pace)
    {
        var a = Math.Max(0, pace.Damage);
        var b = Math.Max(0, pace.AgainstDamage);
        var top = Math.Max(a, b);
        if (top <= 0) return (0, 0);
        return ((float)(a / (double)top), (float)(b / (double)top));
    }

    /// <summary>A recap's HP bar: the HP before against the most the recap saw (the model doesn't carry max HP).</summary>
    public static long RecapMax(IReadOnlyList<RecapLine> recap)
    {
        long max = 0;
        foreach (var l in recap)
        {
            max = Math.Max(max, l.HpBefore);
            if (l.Amount > 0) max = Math.Max(max, l.HpBefore + l.Amount);
        }
        return max;
    }

    /// <summary>An outcome's tag on the meter: Wipe, Clear, or none for a fight that just ended.</summary>
    public static string? OutcomeWord(Outcome? o) => o switch
    {
        Outcome.Wipe => "Wipe",
        Outcome.Clear => "Clear",
        _ => null,
    };
}
