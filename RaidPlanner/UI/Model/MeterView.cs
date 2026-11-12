using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.UI.Model;

/// <summary>One of the picker's rows: an encounter, whether it's the one shown, live, or the best.</summary>
public sealed record PickerRow(MeterEncounter Fight, bool Shown, bool Live, bool Best);

/// <summary>A row of the small meter as it's drawn: the row, its number and second column in words, its bar (0–1), its id.</summary>
public sealed record MeterLine(MeterRow Row, string Value, string Second, float Bar, string Id);

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

    private static readonly string[] SpotOrderOf = { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" };

    /// <summary>The party's rows in spot order, MT to R2 (the rest after them in the order they came), the limit break left out: the Events tab's table.</summary>
    public static List<MeterRow> InSpotOrder(IReadOnlyList<MeterRow> rows) =>
        rows.Where(r => !r.IsLimitBreak).Select((r, i) => (r, i))
            .OrderBy(p => Array.IndexOf(SpotOrderOf, p.r.Slot) is var k and >= 0 ? k : 99).ThenBy(p => p.i).Select(p => p.r).ToList();

    /// <summary>The limit break's row, when it was used.</summary>
    public static MeterRow? LimitBreak(IReadOnlyList<MeterRow> rows) => rows.FirstOrDefault(r => r.IsLimitBreak);

    /// <summary>A row's number on a tab: ENCDPS or DPS, HPS, damage taken.</summary>
    public static double Value(MeterRow r, MeterTab tab, MeterRank rank) => tab switch
    {
        MeterTab.Healing => r.Hps,
        MeterTab.Taken => r.Taken,
        _ => rank == MeterRank.Dps && !r.IsLimitBreak ? r.Dps : r.EncDps,
    };

    /// <summary>
    /// A row's number as the meter writes it: "27,840", "3,912,400" (as the mock-up's Taken tab); a
    /// total of ten million or more in the small meter's 66px column "13.1M" (whole, it would run into the job).
    /// </summary>
    public static string ValueText(MeterRow r, MeterTab tab, MeterRank rank, bool narrow = false)
    {
        var v = (long)Math.Round(Value(r, tab, rank));
        return narrow && Math.Abs(v) >= 10_000_000 ? Format.Compact(v) : Format.Thousands(v);
    }

    /// <summary>
    /// The small meter's rows for a tab as it draws them: biggest first, each with its number and its
    /// second column in words, its bar's length and its hit-test id; the limit break's apart (on the
    /// Damage tab, when it was used). Worked out once a snapshot (a few times a second while a fight is
    /// on), not each frame.
    /// </summary>
    public static (List<MeterLine> Rows, MeterLine? LimitBreak) Lines(MeterEncounter e, MeterTab tab, MeterRank rank)
    {
        var ranked = Ranked(e.Rows, tab, rank);
        var top = ranked.Count > 0 ? Value(ranked[0], tab, rank) : 0;
        var rows = new List<MeterLine>(ranked.Count);
        foreach (var r in ranked)
            rows.Add(new MeterLine(r, ValueText(r, tab, rank, true), Second(r, tab), Bar(r, top, tab, rank), "##mr" + r.PartyId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        MeterLine? lb = null;
        if (tab == MeterTab.Damage && LimitBreak(e.Rows) is { } l)
            lb = new MeterLine(l, Format.Thousands((long)Math.Round(l.EncDps)), Format.Share(l.DamageShare), top > 0 ? (float)Math.Clamp(l.EncDps / top, 0, 1) : 0, "##mr-lb");
        return (rows, lb);
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

    /// <summary>A recap's HP bars are drawn against this: the dying member's max HP (<see cref="DeathEntry.MaxHp"/>), else the most the recap saw.</summary>
    public static long RecapMax(DeathEntry death) => death.MaxHp > 0 ? death.MaxHp : RecapMax(death.Recap);

    /// <summary>
    /// Which line of a recap is the killing blow (on a red wash): the last hit that's the one the death
    /// names (<see cref="DeathEntry.KilledBy"/> and its amount: it needn't be the last line, a bleed's tick
    /// can come after it), else the last line when it's a hit; −1 for none.
    /// </summary>
    public static int KillingLine(DeathEntry death)
    {
        var recap = death.Recap;
        for (var i = recap.Count - 1; i >= 0; i--)
            if (recap[i].Amount <= 0 && -recap[i].Amount == death.Amount && recap[i].What == death.KilledBy)
                return i;
        return recap.Count > 0 && recap[^1].Amount < 0 ? recap.Count - 1 : -1;
    }

    /// <summary>A recap's HP bar without the max HP known: the HP before against the most the recap saw.</summary>
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

    /// <summary>
    /// The line under "The meter has stopped" (<see cref="IMeterSource.Problem"/>): the source's words,
    /// without saying it has stopped again ("What it reads from the game made no sense…").
    /// </summary>
    public static string StoppedLine(string problem)
    {
        const string lead = "The meter has stopped";
        var t = problem.Trim();
        if (t.StartsWith(lead, StringComparison.OrdinalIgnoreCase)) t = t[lead.Length..].TrimStart(':', '.', ',', ' ');
        return t.Length == 0 ? "An update to the plugin should bring it back." : char.ToUpperInvariant(t[0]) + t[1..];
    }

    /// <summary>An outcome's tag on the meter: Wipe, Clear, or none for a fight that just ended.</summary>
    public static string? OutcomeWord(Outcome? o) => o switch
    {
        Outcome.Wipe => "Wipe",
        Outcome.Clear => "Clear",
        _ => null,
    };
}
