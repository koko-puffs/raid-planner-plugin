using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPlanner.Core;

/// <summary>What a mechanic looks like, for its small icon in the window's timeline.</summary>
public enum MechanicKind
{
    Circle,
    Donut,
    Cone,
    Line,
    Cross,
    Stack,
    Spread,
    Tankbuster,
    Raidwide,
    Other,
}

/// <summary>One line of a pull's timeline: when, what it's called, how many went off, and what they look like.</summary>
public sealed record TimelineEntry(double T, string Name, int Count, IReadOnlyList<MechanicKind> Kinds);

/// <summary>
/// A pull's mechanics in order, as the window lists them before it's sent: each enemy cast, and
/// each hit that came without a cast (most mechanics resolve as a hit from the game's invisible
/// helpers). The same action going off again within a moment is one line with its count. The
/// site makes the plan's steps itself, from the whole recording; this only shows what's in it.
/// </summary>
public static class Timeline
{
    /// <summary>Seconds within which the same action going off again is the same line.</summary>
    public const double Together = 1.5;

    /// <summary>A hit within this of a cast of the same action ending is that cast going off.</summary>
    public const double CastSlack = 1.25;

    public static List<TimelineEntry> Build(Recording r)
    {
        var partyIds = new HashSet<int>(r.Party.Select(p => p.Id));
        var tanks = new HashSet<int>(r.Party.Where(p => p.Role == "tank").Select(p => p.Id));
        var partySize = Math.Max(1, r.Party.Count);
        var casts = r.Events.OfType<CastEvent>().Where(c => !partyIds.Contains(c.Source)).ToList();
        // Each action's hits and casts' ends, in time order, so finding the ones near a moment is a
        // look-up, not a pass over the whole pull (a long pull has thousands of each).
        var hitsOf = r.Events.OfType<HitEvent>().Where(h => !partyIds.Contains(h.Source))
            .GroupBy(h => h.Action).ToDictionary(g => g.Key, g => g.OrderBy(h => h.T).ToList());
        var castEndsOf = casts.GroupBy(c => c.Action)
            .ToDictionary(g => g.Key, g => g.Select(c => c.T + c.Duration).OrderBy(t => t).ToList());

        // Moments: (time it goes off, action, kind).
        var moments = new List<(double T, uint Action, MechanicKind Kind)>();
        foreach (var c in casts)
        {
            if (!r.Actions.TryGetValue(c.Action, out var info) || !Named(info)) continue;
            var end = c.T + c.Duration;
            var landed = hitsOf.TryGetValue(c.Action, out var own) ? Between(own, end - CastSlack, end + CastSlack) : new List<HitEvent>();
            moments.Add((end, c.Action, KindOf(info, c.Target, landed, tanks, partySize)));
        }
        foreach (var (action, group) in hitsOf)
        {
            if (!r.Actions.TryGetValue(action, out var info) || !Named(info)) continue;
            castEndsOf.TryGetValue(action, out var ends);
            foreach (var h in group)
            {
                // A cast's own hit is already its moment.
                if (ends != null && AnyBetween(ends, h.T - CastSlack, h.T + CastSlack)) continue;
                // A single-target hit with no cast and no shape: an auto-attack, or near enough.
                if (info.CastType <= 1 && h.Targets.Count <= 1 && !(h.Targets.Count == 1 && tanks.Contains(h.Targets[0]) && info.Cast > 0)) continue;
                var together = Between(group, h.T - 0.3, h.T + 0.3);
                moments.Add((h.T, action, KindOf(info, 0, together, tanks, partySize)));
            }
        }

        moments.Sort((a, b) => a.T.CompareTo(b.T));
        var entries = new List<TimelineEntry>();
        // Each action's line still open, and when it last went off: a chain (exaflares, a run of
        // puddles) stays one line while each goes off within a moment of the one before.
        var open = new Dictionary<uint, (int Index, double Last)>();
        foreach (var (t, action, kind) in moments)
        {
            var name = r.Actions[action].Name;
            if (open.TryGetValue(action, out var o) && t - o.Last <= Together)
            {
                var e = entries[o.Index];
                var kinds = e.Kinds.Contains(kind) ? e.Kinds : e.Kinds.Append(kind).ToList();
                entries[o.Index] = e with { Count = e.Count + 1, Kinds = kinds };
                open[action] = (o.Index, t);
                continue;
            }
            open[action] = (entries.Count, t);
            entries.Add(new TimelineEntry(t, name, 1, new[] { kind }));
        }
        return entries;
    }

    /// <summary>The hits (in time order) from <paramref name="lo"/> to <paramref name="hi"/>, both included.</summary>
    private static List<HitEvent> Between(List<HitEvent> sorted, double lo, double hi)
    {
        var list = new List<HitEvent>();
        for (var i = First(sorted.Count, i => sorted[i].T >= lo); i < sorted.Count && sorted[i].T <= hi; i++) list.Add(sorted[i]);
        return list;
    }

    private static bool AnyBetween(List<double> sorted, double lo, double hi)
    {
        var i = First(sorted.Count, i => sorted[i] >= lo);
        return i < sorted.Count && sorted[i] <= hi;
    }

    /// <summary>The first index from 0 to <paramref name="count"/> where <paramref name="at"/> holds (it holds for all after).</summary>
    private static int First(int count, Func<int, bool> at)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (at(mid)) hi = mid;
            else lo = mid + 1;
        }
        return lo;
    }

    private static bool Named(ActionInfo info) =>
        !string.IsNullOrWhiteSpace(info.Name) && !string.Equals(info.Name.Trim(), "attack", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What a mechanic looks like, from its cast type (the game's shape), who it was aimed at and
    /// who it hit: a raidwide hits (nearly) everyone with no shape to dodge; a circle on players
    /// that hits several at once is a stack, one each a spread; a single target on a tank a buster.
    /// </summary>
    public static MechanicKind KindOf(ActionInfo info, int castTarget, IReadOnlyList<HitEvent> landed, ISet<int> tanks, int partySize)
    {
        var most = landed.Count == 0 ? 0 : landed.Max(h => h.Targets.Count);
        var everyone = partySize >= 4 ? most >= partySize - 1 : most >= partySize;
        switch (info.CastType)
        {
            case 1:
                if (everyone && partySize > 1) return MechanicKind.Raidwide;
                var first = castTarget != 0 ? castTarget : landed.SelectMany(h => h.Targets).FirstOrDefault();
                return first != 0 && tanks.Contains(first) ? MechanicKind.Tankbuster : MechanicKind.Other;
            case 2:
            case 5:
            case 6:
            case 7:
                if (everyone && partySize > 1 && (info.Range >= 30 || info.Range == 0)) return MechanicKind.Raidwide;
                if (info.CastType == 2 && (castTarget != 0 || landed.Count > 0))
                {
                    // Aimed at players: several hit by one means stand together.
                    if (most >= 2 && landed.Count <= 2) return MechanicKind.Stack;
                    if (landed.Count >= 2 || (castTarget != 0 && most <= 1)) return MechanicKind.Spread;
                }
                return MechanicKind.Circle;
            case 3:
            case 13:
                return MechanicKind.Cone;
            case 4:
            case 8:
            case 12:
                return MechanicKind.Line;
            case 10:
                return MechanicKind.Donut;
            case 11:
                return MechanicKind.Cross;
            default:
                return everyone && partySize > 1 ? MechanicKind.Raidwide : MechanicKind.Other;
        }
    }
}
