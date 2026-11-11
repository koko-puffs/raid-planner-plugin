using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>A row of a pull's list: a mechanic (from the timeline) or a death.</summary>
public sealed record PullRow(double T, string Name, int Count, IReadOnlyList<MechanicKind> Kinds, string? DeathSlot = null, string? DeathJob = null)
{
    public bool Death => DeathSlot != null;
}

/// <summary>
/// What a pull's screen shows of its recording, worked out once on a worker: its mechanics in
/// order with its deaths among them (not the wipe's), where its phases began (the recorder's rule:
/// every enemy that had been targetable gone 8 seconds or more, then one back), and its counts.
/// </summary>
public sealed record PullView(IReadOnlyList<PullRow> Rows, IReadOnlyList<double> Phases, IReadOnlyList<double> Deaths, int Waymarks, int Tethers, int WipeDeaths)
{
    /// <summary>The seconds at the end of a wiped pull whose deaths are the wipe, not anyone's (the spec's rule).</summary>
    public const double WipeWindow = 6;

    /// <summary>Seconds every enemy must be gone for the next one back to start a phase (RecordingBuilder.PhaseGap).</summary>
    public const double PhaseGap = 8;

    public static PullView Build(Recording r)
    {
        var rows = Timeline.Build(r).Select(e => new PullRow(e.T, e.Name, e.Count, e.Kinds)).ToList();
        var slots = r.Party.ToDictionary(p => p.Id);
        var deaths = new List<double>();
        var wipe = 0;
        foreach (var d in r.Events.OfType<DeathEvent>())
        {
            if (r.Outcome == Outcome.Wipe && d.T >= r.Duration - WipeWindow)
            {
                wipe++;
                continue;
            }
            slots.TryGetValue(d.Who, out var who);
            var by = d.By == 0 ? null : d.ByStatus
                ? r.Statuses.TryGetValue(d.By, out var st) ? st.Name : null
                : r.Actions.TryGetValue(d.By, out var a) ? a.Name : null;
            var slot = who?.Slot ?? "?";
            rows.Add(new PullRow(d.T, by is { Length: > 0 } ? $"{slot} died · {by}" : $"{slot} died", 1, Array.Empty<MechanicKind>(), slot, who?.Job));
            deaths.Add(d.T);
        }
        rows.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.Death.CompareTo(b.Death));
        var waymarks = r.Events.OfType<WaymarksEvent>().LastOrDefault()?.Marks.Count ?? 0;
        var tethers = r.Events.OfType<TetherEvent>().Count(t => t.On);
        return new PullView(rows, PhaseStarts(r.Events.OfType<TargetEvent>()), deaths, waymarks, tethers, wipe);
    }

    /// <summary>
    /// Where phases began (the first at 0): each time every enemy that had been targetable was gone
    /// for <see cref="PhaseGap"/> seconds or more, and then one became targetable (again, or a new one).
    /// </summary>
    public static List<double> PhaseStarts(IEnumerable<TargetEvent> events)
    {
        var starts = new List<double> { 0 };
        var up = new HashSet<int>();
        var everUp = false;
        double? allGoneAt = null;
        foreach (var e in events.OrderBy(e => e.T))
        {
            if (e.On)
            {
                if (up.Count == 0 && everUp && allGoneAt is { } gone && e.T - gone >= PhaseGap) starts.Add(e.T);
                up.Add(e.Actor);
                everUp = true;
                allGoneAt = null;
            }
            else if (up.Remove(e.Actor) && up.Count == 0)
            {
                allGoneAt = e.T;
            }
        }
        return starts;
    }
}
