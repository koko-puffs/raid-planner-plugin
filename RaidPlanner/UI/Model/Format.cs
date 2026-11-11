using System;
using System.Collections.Generic;
using System.Globalization;

namespace RaidPlanner.UI.Model;

/// <summary>
/// The windows' numbers and times, said the way the site says them (its components/when.ts and
/// Ready Check's mono counts), worked out away from ImGui so they're tested. English whatever the
/// PC's language, as the site is: "27,840", "1.28M", "16.9", "2:31", "2:45.2", "8 minutes ago".
/// </summary>
public static class Format
{
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    /// <summary>The true minus sign, as the site's recap writes a hit (−38,620).</summary>
    public const char Minus = '−';

    /// <summary>A fight's clock: "0:07", "6:42", "1:02:03" (negative or not a number: "0:00").</summary>
    public static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var s = (long)Math.Floor(seconds);
        var h = s / 3600;
        var m = s / 60 % 60;
        var sec = s % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
    }

    /// <summary>A written time read back: "1:24" (84), "1:02:03", "45"; null for anything else.</summary>
    public static double? ParseClock(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split(':');
        if (parts.Length > 3) return null;
        double total = 0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, En, out var v) || v < 0) return null;
            total = total * 60 + v;
        }
        return total;
    }

    /// <summary>A moment to a tenth, as the recap times a hit: "2:45.2" (rounded down, so 2:45.29 is 2:45.2).</summary>
    public static string ClockTenths(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var tenths = (long)Math.Floor(seconds * 10 + 1e-6);
        return $"{Clock(tenths / 10)}.{tenths % 10}";
    }

    /// <summary>A whole number with thousands marks: "27,840", "5,317,440".</summary>
    public static string Thousands(long n) => n.ToString("N0", En);

    /// <summary>
    /// A number for a narrow column: whole up to 999,999 ("27,840"), then three figures with a
    /// letter ("1.28M", "12.8M", "128M", "1.28B"). Rounded down, so a number never reads bigger
    /// than it is.
    /// </summary>
    public static string Compact(long n)
    {
        if (n < 0) return Minus + Compact(-n);
        if (n < 1_000_000) return Thousands(n);
        var (div, unit) = n < 1_000_000_000 ? (1_000_000d, 'M') : (1_000_000_000d, 'B');
        var v = n / div;
        var text = v >= 100 ? Floor(v, 0) : v >= 10 ? Floor(v, 1) : Floor(v, 2);
        return text + unit;
    }

    private static string Floor(double v, int decimals)
    {
        var k = Math.Pow(10, decimals);
        var f = Math.Floor(v * k + 1e-9) / k;
        return f.ToString(decimals == 0 ? "0" : decimals == 1 ? "0.0" : "0.00", En);
    }

    /// <summary>A share (0–1) as the meter's % column writes it: "16.9" (one decimal, no sign).</summary>
    public static string Share(double fraction) => Pct(fraction, 1);

    /// <summary>A rate (0–1) as a whole percentage with its sign: "31%".</summary>
    public static string PercentWhole(double fraction) => Pct(fraction, 0) + "%";

    private static string Pct(double fraction, int decimals)
    {
        if (!double.IsFinite(fraction)) fraction = 0;
        var v = Math.Round(Math.Clamp(fraction, -10, 10) * 100, decimals, MidpointRounding.AwayFromZero);
        return v.ToString(decimals == 0 ? "0" : "0.0", En);
    }

    /// <summary>A change with its sign: "+3.4%", "−1.2%".</summary>
    public static string SignedPercent(double fraction)
    {
        if (!double.IsFinite(fraction)) fraction = 0;
        var v = Math.Round(fraction * 100, 1, MidpointRounding.AwayFromZero);
        var text = Math.Abs(v).ToString("0.0", En) + "%";
        return v < 0 ? Minus + text : "+" + text;
    }

    /// <summary>An amount with its sign, as a recap line: "−38,620" for a hit, "+6,120" for a heal.</summary>
    public static string Signed(long n) => n < 0 ? Minus + Thousands(-n) : "+" + Thousands(n);

    /// <summary>A size in megabytes to a tenth, at least 0.1: "0.6 MB".</summary>
    public static string Megabytes(long bytes)
    {
        var mb = Math.Max(0.1, bytes / 1024d / 1024d);
        return mb.ToString("0.0", En) + " MB";
    }

    /// <summary>"in 16 s", or "in 1:04" past a minute.</summary>
    public static string InSeconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var s = (int)Math.Ceiling(seconds - 1e-6);
        return s < 60 ? $"in {s} s" : $"in {Clock(s)}";
    }

    /// <summary>
    /// When something happened, as the site's when.ts says it: "just now", "5 minutes ago", "3 hours
    /// ago" (the same day), "yesterday", "4 days ago", then "12 Oct" this year and "12 Oct 2025" before.
    /// </summary>
    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var s = Math.Max(0, (long)Math.Round((now - at).TotalSeconds));
        if (s < 60) return "just now";
        var m = s / 60;
        if (m < 60) return m == 1 ? "1 minute ago" : $"{m} minutes ago";
        var days = (now.ToLocalTime().Date - at.ToLocalTime().Date).Days;
        if (days <= 0)
        {
            var h = m / 60;
            return h == 1 ? "1 hour ago" : $"{h} hours ago";
        }
        if (days == 1) return "yesterday";
        if (days < 7) return $"{days} days ago";
        var local = at.ToLocalTime();
        return local.Year == now.ToLocalTime().Year ? local.ToString("d MMM", En) : local.ToString("d MMM yyyy", En);
    }

    /// <summary>
    /// A day and time for an eyebrow: "tonight 23:42" (today from 17:00), "today 14:02", "yesterday
    /// 23:42", "Mon 21:04" this week, "2 Nov 21:04" this year, "2 Nov 2025" before.
    /// </summary>
    public static string Day(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var day = local.Date;
        var time = local.ToString("HH:mm", En);
        if (day == today) return local.Hour >= 17 ? $"tonight {time}" : $"today {time}";
        if (day == today.AddDays(-1)) return $"yesterday {time}";
        if (day > today.AddDays(-7) && day < today) return $"{local.ToString("ddd", En)} {time}";
        if (day.Year == today.Year) return $"{local.ToString("d MMM", En)} {time}";
        return local.ToString("d MMM yyyy", En);
    }

    /// <summary>"Sent just now.", "Sent 8 minutes ago.", "Sent yesterday at 23:52.", "Sent 12 Oct."</summary>
    public static string SentLine(DateTimeOffset at, DateTimeOffset now)
    {
        var ago = Ago(at, now);
        if (ago == "yesterday") ago = $"yesterday at {at.ToLocalTime().ToString("HH:mm", En)}";
        return $"Sent {ago}.";
    }

    /// <summary>
    /// The aside on a list of pulls: "Tonight · 10" when they're all from this evening (from 17:00),
    /// "Today · 10" when all from today, else "10 pulls".
    /// </summary>
    public static string PullsAside(IReadOnlyList<DateTimeOffset> times, DateTimeOffset now)
    {
        var n = times.Count;
        if (n == 0) return "No pulls";
        var today = now.ToLocalTime().Date;
        var allToday = true;
        var allEvening = true;
        foreach (var t in times)
        {
            var l = t.ToLocalTime();
            if (l.Date != today) allToday = allEvening = false;
            else if (l.Hour < 17) allEvening = false;
        }
        if (allEvening) return $"Tonight · {n}";
        if (allToday) return $"Today · {n}";
        return n == 1 ? "1 pull" : $"{Thousands(n)} pulls";
    }

    /// <summary>A step's number for an eyebrow, two figures at least: "04", "13", "120".</summary>
    public static string StepNumber(int oneBased) => Math.Max(0, oneBased).ToString("00", En);

    /// <summary>Upper case for a callout, an eyebrow or a tag (CSS text-transform), except on a Japanese client (which draws them in Dalamud's font, as written).</summary>
    public static string Caps(string text, bool japanese) => japanese ? text : text.ToUpperInvariant();

    /// <summary>
    /// Text cut to fit a width with an ellipsis, the most letters that fit (by halving), as
    /// <c>text-overflow: ellipsis</c>; <paramref name="measure"/> says how wide a string is.
    /// </summary>
    public static string Fit(string text, Func<string, float> measure, float width)
    {
        if (measure(text) <= width) return text;
        const string dots = "…";
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (measure(text[..mid].TrimEnd() + dots) <= width) lo = mid;
            else hi = mid - 1;
        }
        return lo == 0 ? dots : text[..lo].TrimEnd() + dots;
    }

    /// <summary>
    /// Words wrapped to a width, at most <paramref name="maxLines"/> lines: the last takes the rest,
    /// and any line still too wide (one long word, or that last) is cut with "…".
    /// </summary>
    public static List<string> Wrap(string text, Func<string, float> measure, float width, int maxLines = int.MaxValue)
    {
        var lines = new List<string>();
        var cur = string.Empty;
        foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = cur.Length == 0 ? w : cur + " " + w;
            if (cur.Length > 0 && measure(candidate) > width)
            {
                lines.Add(cur);
                cur = w;
            }
            else cur = candidate;
        }
        lines.Add(cur);
        if (maxLines >= 1 && lines.Count > maxLines)
        {
            var rest = string.Join(' ', lines.GetRange(maxLines - 1, lines.Count - maxLines + 1));
            lines.RemoveRange(maxLines - 1, lines.Count - maxLines + 1);
            lines.Add(rest);
        }
        for (var i = 0; i < lines.Count; i++) lines[i] = Fit(lines[i], measure, width);
        return lines;
    }

    /// <summary>
    /// An error said as an empty page says it: its first sentence as the small callout, the rest as
    /// the line under it ("Couldn't reach the site." / "Check your connection."). One long sentence is
    /// the line, under "Something went wrong".
    /// </summary>
    public static (string Title, string Line) Headline(string why)
    {
        var t = why.Trim();
        var i = t.IndexOf(". ", StringComparison.Ordinal);
        if (i > 0 && i <= 60) return (t[..i], t[(i + 2)..].Trim());
        if (t.Length <= 60 && t.IndexOf('.') is var dot && (dot < 0 || dot == t.Length - 1)) return (t.TrimEnd('.'), "Try again in a moment.");
        return ("Something went wrong", t);
    }

    /// <summary>A spot's role from its label: MT/OT tanks, H1/H2 healers, M1/M2/R1/R2/D1… dps; null for anything else.</summary>
    public static string? RoleOfSlot(string? slot)
    {
        if (string.IsNullOrEmpty(slot)) return null;
        var s = slot.Trim().ToUpperInvariant();
        if (s is "MT" or "OT" || (s.Length >= 2 && s[0] == 'T' && char.IsDigit(s[1]))) return "tank";
        if (s.Length >= 2 && s[0] == 'H' && char.IsDigit(s[1])) return "healer";
        if (s.Length >= 2 && (s[0] is 'M' or 'R' or 'D') && char.IsDigit(s[1])) return "dps";
        return null;
    }
}
