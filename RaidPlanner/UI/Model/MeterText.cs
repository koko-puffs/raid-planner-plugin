using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.UI.Model;

/// <summary>
/// The meter's 0.4.0 words, worked out away from ImGui so they're tested: Copy as text (a mini-parse
/// in ACT's spirit), the Events tab's lines, a death's raise, the limit break gauge.
/// </summary>
public static class MeterText
{
    /// <summary>
    /// The encounter as text for the clipboard, one line each: the duty, which pull, its length and how
    /// it ended (and where), the party's ENCDPS; then each player, biggest first: who (their name and job,
    /// or their spot and job with Jobs only), ENCDPS, their share and their deaths (the wipe's not
    /// counted); the limit break last when it was used.
    /// <code>
    /// Example Arena · Pull 12 · 3:31 · Wipe in P2 · Party 165,000 ENCDPS
    /// Rook (DRG) 27,840 · 16.9% · 0 deaths
    /// </code>
    /// </summary>
    public static string Parse(MeterEncounter e, bool jobsOnly)
    {
        var sb = new StringBuilder();
        sb.Append(e.Duty.Length > 0 ? e.Duty : "A fight");
        sb.Append(" · Pull ").Append(e.Pull.ToString(CultureInfo.InvariantCulture));
        sb.Append(" · ").Append(Format.Clock(e.Duration));
        var how = e.Live ? "So far" : MeterView.OutcomeWord(e.Ended);
        if (how != null)
        {
            sb.Append(" · ").Append(how);
            if (e.Phase is { Length: > 0 } p && e.Ended != Outcome.Clear) sb.Append(" in ").Append(p);
        }
        sb.Append(" · Party ").Append(Format.Thousands((long)Math.Round(e.Party.EncDps))).Append(" ENCDPS");
        foreach (var r in MeterView.Ranked(e.Rows, MeterTab.Damage, MeterRank.EncDps))
        {
            sb.Append('\n');
            sb.Append(Who(r, jobsOnly)).Append(' ');
            sb.Append(Format.Thousands((long)Math.Round(r.EncDps))).Append(" · ");
            sb.Append(Format.Share(r.DamageShare)).Append("% · ");
            sb.Append(Words.Count(r.Deaths, "death"));
        }
        if (MeterView.LimitBreak(e.Rows) is { } lb)
            sb.Append('\n').Append("Limit break ").Append(Format.Thousands((long)Math.Round(lb.EncDps))).Append(" · ").Append(Format.Share(lb.DamageShare)).Append('%');
        return sb.ToString();
    }

    /// <summary>A player in the copied text: "Rook (DRG)", or with Jobs only "M1 DRG".</summary>
    public static string Who(MeterRow r, bool jobsOnly)
    {
        if (jobsOnly || r.Name.Length == 0) return r.Job.Length > 0 ? $"{r.Slot} {r.Job}".Trim() : r.Slot;
        return r.Job.Length > 0 ? $"{r.Name} ({r.Job})" : r.Name;
    }

    /// <summary>An event's kind in a word, for its tooltip and the opened-up window's column.</summary>
    public static string KindWord(MeterEventKind kind) => kind switch
    {
        MeterEventKind.Death => "Death",
        MeterEventKind.Raise => "Raise",
        MeterEventKind.Interrupt => "Interrupt",
        MeterEventKind.CastWentOff => "Went off",
        MeterEventKind.Stun => "Stun",
        MeterEventKind.Resisted => "Resisted",
        MeterEventKind.Potion => "Potion",
        MeterEventKind.LimitBreak => "Limit break",
        MeterEventKind.TankSwap => "Tank swap",
        MeterEventKind.AggroLost => "Aggro",
        _ => "Event",
    };

    /// <summary>What an event's mark looks like: bad (red), good (green), ember (the limit break, potions) or quiet.</summary>
    public static EventTone Tone(MeterEventKind kind) => kind switch
    {
        MeterEventKind.Death or MeterEventKind.CastWentOff or MeterEventKind.AggroLost => EventTone.Bad,
        MeterEventKind.Raise or MeterEventKind.Interrupt => EventTone.Good,
        MeterEventKind.LimitBreak or MeterEventKind.Potion => EventTone.Ember,
        _ => EventTone.Quiet,
    };

    /// <summary>The events to show, newest first (the small meter's feed), at most <paramref name="max"/>.</summary>
    public static List<MeterEvent> Newest(IReadOnlyList<MeterEvent> events, int max)
    {
        var list = new List<MeterEvent>(Math.Min(Math.Max(0, max), events.Count));
        for (var i = events.Count - 1; i >= 0 && list.Count < max; i--) list.Add(events[i]);
        return list;
    }

    /// <summary>How many of each kind ("2 deaths · 1 raise · 3 interrupts"), for the Events tab's foot; the most said first.</summary>
    public static string Counts(IReadOnlyList<MeterEvent> events, int most = 3)
    {
        if (events.Count == 0) return "No events yet";
        var counts = new Dictionary<MeterEventKind, int>();
        foreach (var e in events) counts[e.Kind] = counts.TryGetValue(e.Kind, out var n) ? n + 1 : 1;
        var parts = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => (int)kv.Key).Take(most).Select(kv => Plural(kv.Key, kv.Value));
        return string.Join(" · ", parts);
    }

    private static string Plural(MeterEventKind kind, int n) => kind switch
    {
        MeterEventKind.Death => Words.Count(n, "death"),
        MeterEventKind.Raise => Words.Count(n, "raise"),
        MeterEventKind.Interrupt => Words.Count(n, "interrupt"),
        MeterEventKind.CastWentOff => n.ToString(CultureInfo.InvariantCulture) + " went off",
        MeterEventKind.Stun => Words.Count(n, "stun"),
        MeterEventKind.Resisted => n.ToString(CultureInfo.InvariantCulture) + " resisted",
        MeterEventKind.Potion => Words.Count(n, "potion"),
        MeterEventKind.LimitBreak => Words.Count(n, "limit break"),
        MeterEventKind.TankSwap => Words.Count(n, "tank swap"),
        _ => n.ToString(CultureInfo.InvariantCulture) + " aggro",
    };

    /// <summary>
    /// What happened after a death, for its row and recap: "Raised at 3:02 by H1 · 0:11 dead",
    /// "Raised at 3:02 · 0:11 dead" (who isn't known), "Not raised" (null for one in the wipe). With
    /// Weakness on as they died, "with Weakness" first.
    /// </summary>
    public static string? AfterDeath(DeathEntry d)
    {
        if (d.PartOfWipe) return null;
        var parts = new List<string>(3);
        if (d.HadWeakness) parts.Add("Died with Weakness");
        if (d.RaisedAt is { } at)
        {
            var raised = "Raised at " + Format.Clock(at);
            if (!string.IsNullOrEmpty(d.RaisedBy)) raised += " by " + d.RaisedBy;
            parts.Add(raised);
            parts.Add(Format.Clock(Math.Max(0, at - d.At)) + " dead");
        }
        else parts.Add("Not raised");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// A death's line under its name on the small meter: what killed them and how big, then (since
    /// 0.4.0) whether they had Weakness on and when they were raised and by whom: "Exaflare · 104,880 ·
    /// Weakness · raised by H1 after 0:11".
    /// </summary>
    public static string DeathLine(DeathEntry d)
    {
        var line = d.KilledBy + " · " + Format.Thousands(d.Amount);
        if (d.HadWeakness) line += " · Weakness";
        if (d.RaisedAt is { } at)
        {
            line += string.IsNullOrEmpty(d.RaisedBy) ? " · raised" : " · raised by " + d.RaisedBy;
            line += " after " + Format.Clock(Math.Max(0, at - d.At));
        }
        return line;
    }

    /// <summary>Seconds dead in a cell: "–" for none, "0:42".</summary>
    public static string Dead(double seconds) => seconds > 0.05 ? Format.Clock(seconds) : "–";

    /// <summary>A count in a cell: "–" for none.</summary>
    public static string Count(int n) => n > 0 ? n.ToString(CultureInfo.InvariantCulture) : "–";

    /// <summary>The limit break gauge as the meter draws it: each bar's fill (0–1), the bars the party has.</summary>
    public static float[] Bars(LimitBreakGauge? g)
    {
        if (g == null || g.MaxBars <= 0) return Array.Empty<float>();
        var n = Math.Clamp(g.MaxBars, 1, 5);
        var bars = new float[n];
        var full = Math.Clamp(g.Bars, 0, n);
        for (var i = 0; i < full; i++) bars[i] = 1;
        if (full < n && g.BarUnits > 0) bars[full] = Math.Clamp(g.Units / (float)g.BarUnits, 0, 1);
        return bars;
    }

    /// <summary>The gauge's words ("LB 2/3", its tooltip).</summary>
    public static string GaugeTip(LimitBreakGauge g)
    {
        var pct = g.BarUnits > 0 && g.Bars < g.MaxBars ? $", the next {Format.PercentWhole(Math.Clamp(g.Units / (double)g.BarUnits, 0, 1))} full" : string.Empty;
        return $"Limit break: {g.Bars} of {g.MaxBars} bars{pct}.";
    }
}

/// <summary>Where the meter shows the limit break gauge (Settings → Meter).</summary>
public enum GaugePlace
{
    /// <summary>In its head, after the clock (as far as it fits before the buttons).</summary>
    Head,

    /// <summary>In its foot, before what it says on the right.</summary>
    Foot,

    Off,
}

/// <summary>An event's mark's colour on the Events tab.</summary>
public enum EventTone
{
    Quiet,
    Good,
    Bad,
    Ember,
}
