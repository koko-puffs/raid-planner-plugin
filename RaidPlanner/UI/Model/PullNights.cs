using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>A night (or a day) of pulls: what it's called ("Tonight", "Last night", "Friday night", "12 Oct") and its pulls' places in the list.</summary>
public sealed record PullNight(string Label, DateTime Key, IReadOnlyList<int> Items)
{
    /// <summary>"Tonight · 12 pulls".</summary>
    public string Heading => $"{Label} · {Words.Count(Items.Count, "pull")}";
}

/// <summary>
/// The kept pulls by night (0.8.0: Home's Tonight, the Pulls tab): a raid night runs past midnight, so a pull before
/// 6:00 is the evening before's. A night is "Tonight" (or "Today" when none of it was in the evening), then "Last
/// night" ("Yesterday"), a weekday this week ("Friday night"), then a date. In local time throughout.
/// </summary>
public static class PullNights
{
    /// <summary>A night's pulls before this hour are the evening before's.</summary>
    public const int Morning = 6;

    /// <summary>From this hour a pull is in the evening (as <see cref="Format.Day"/>'s "tonight").</summary>
    public const int Evening = 17;

    public static DateTime KeyOf(DateTime local) => local.AddHours(-Morning).Date;

    private static bool InEvening(DateTime local) => local.Hour >= Evening || local.Hour < Morning;

    /// <summary>What a night is called, seen from <paramref name="nowLocal"/>.</summary>
    public static string Label(DateTime key, bool evening, DateTime nowLocal)
    {
        var today = KeyOf(nowLocal);
        var days = (today - key).Days;
        if (days <= 0) return evening ? "Tonight" : "Today";
        if (days == 1) return evening ? "Last night" : "Yesterday";
        if (days < 7) return key.ToString("dddd", CultureInfo.InvariantCulture) + (evening ? " night" : string.Empty);
        return key.Year == today.Year ? key.ToString("d MMM", CultureInfo.InvariantCulture) : key.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>The pulls (newest first, in local time) by night, the newest night first; each night's pulls in the list's order.</summary>
    public static List<PullNight> Group(IReadOnlyList<DateTime> local, DateTime nowLocal)
    {
        var nights = new List<(DateTime Key, List<int> Items, bool Evening)>();
        for (var i = 0; i < local.Count; i++)
        {
            var key = KeyOf(local[i]);
            var at = nights.FindIndex(n => n.Key == key);
            if (at < 0)
            {
                nights.Add((key, new List<int>(), false));
                at = nights.Count - 1;
            }
            var n = nights[at];
            n.Items.Add(i);
            nights[at] = (n.Key, n.Items, n.Evening || InEvening(local[i]));
        }
        return nights.OrderByDescending(n => n.Key).Select(n => new PullNight(Label(n.Key, n.Evening, nowLocal), n.Key, n.Items)).ToList();
    }

    /// <summary>
    /// The pulls' numbers as the site says them (since 0.9.1, its words: "Pull 17" by night): each pull's place among its
    /// night's pulls of its duty, the first 1. <paramref name="pulls"/> in local time, in any order (the list's, newest first).
    /// </summary>
    public static int[] Numbers(IReadOnlyList<(DateTime Local, string Duty)> pulls)
    {
        var order = Enumerable.Range(0, pulls.Count).OrderBy(i => pulls[i].Local).ToList();
        var counts = new Dictionary<(DateTime, string), int>();
        var numbers = new int[pulls.Count];
        foreach (var i in order)
        {
            var key = (KeyOf(pulls[i].Local), pulls[i].Duty.ToUpperInvariant());
            counts.TryGetValue(key, out var n);
            numbers[i] = counts[key] = n + 1;
        }
        return numbers;
    }

    /// <summary>The number a pull of <paramref name="duty"/> starting at <paramref name="nowLocal"/> has: one more than its night's kept pulls of it before then.</summary>
    public static int Next(IReadOnlyList<(DateTime Local, string Duty)> pulls, string duty, DateTime nowLocal)
    {
        var key = KeyOf(nowLocal);
        var n = 1;
        foreach (var p in pulls)
            if (KeyOf(p.Local) == key && p.Local <= nowLocal && string.Equals(p.Duty, duty, StringComparison.OrdinalIgnoreCase))
                n++;
        return n;
    }

    /// <summary>
    /// Each duty's best pull in a list (a night's): its fastest clear, else the one that lasted longest; only for a duty
    /// pulled more than once. Returns their places in the list.
    /// </summary>
    public static HashSet<int> Best(IReadOnlyList<(string Duty, double Duration, Outcome Outcome)> pulls)
    {
        var best = new HashSet<int>();
        foreach (var g in Enumerable.Range(0, pulls.Count).GroupBy(i => pulls[i].Duty, StringComparer.OrdinalIgnoreCase))
        {
            var items = g.ToList();
            if (items.Count < 2) continue;
            var clears = items.Where(i => pulls[i].Outcome == Outcome.Clear).ToList();
            var pick = clears.Count > 0
                ? clears.OrderBy(i => pulls[i].Duration).First()
                : items.OrderByDescending(i => pulls[i].Duration).First();
            best.Add(pick);
        }
        return best;
    }
}
