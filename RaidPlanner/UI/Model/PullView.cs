using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>A row of a pull's list: a mechanic (from the timeline) or a death.</summary>
/// <remarks>
/// A death also says who (their name when the recording has it, else their job, "WAR 2" for the second of it unnamed: never their
/// spot, the plugin's guess the game never says; <see cref="PartyNames"/>), their role (for its colour) and what killed them, when known.
/// </remarks>
public sealed record PullRow(double T, string Name, int Count, IReadOnlyList<MechanicKind> Kinds, string? DeathRole = null, string? DeathJob = null,
    string? DeathWho = null, string? DeathBy = null)
{
    public bool Death => DeathRole != null;
}

/// <summary>
/// What a pull's screen shows of its recording, worked out once on a worker: its mechanics in
/// order with its deaths among them (not the wipe's: <see cref="PullStats.WipeDeaths"/>, the one rule
/// the summary, the meter and the site keep too), where its phases began (the recorder's rule: every
/// enemy that had been targetable gone 8 seconds or more, then one back), and its counts.
/// </summary>
public sealed record PullView(IReadOnlyList<PullRow> Rows, IReadOnlyList<double> Phases, IReadOnlyList<double> Deaths, int Waymarks, int Tethers, int WipeDeaths)
{
    /// <summary>When the wipe began (its first death), for a wiped pull whose wipe was seen; else null.</summary>
    public double? WipeAt { get; init; }

    /// <summary>Seconds every enemy must be gone for the next one back to start a phase (RecordingBuilder.PhaseGap).</summary>
    public const double PhaseGap = 8;

    public static PullView Build(Recording r)
    {
        var rows = Timeline.Build(r).Select(e => new PullRow(e.T, e.Name, e.Count, e.Kinds)).ToList();
        var slots = r.Party.ToDictionary(p => p.Id);
        // What each is called: their name, else their job (numbered from the second of it in the party list's order).
        var labels = PartyNames.Labels(r.Party.Select(p => (PartyMember.Clean(p.Name), (string?)p.Job)).ToList());
        var called = new Dictionary<int, string>(r.Party.Count);
        for (var i = 0; i < r.Party.Count; i++) called[r.Party[i].Id] = labels[i];
        var deaths = new List<double>();
        var wipe = 0;
        // The wipe: a wiped pull's deaths within six seconds of its last death (counted back from that
        // death, not from the recording's end, which runs on past the wipe while the duty calls it).
        var wipeDeaths = PullStats.WipeDeaths(r);
        foreach (var d in r.Events.OfType<DeathEvent>())
        {
            if (wipeDeaths.Contains(d))
            {
                wipe++;
                continue;
            }
            slots.TryGetValue(d.Who, out var who);
            var by = d.By == 0 ? null : d.ByStatus
                ? r.Statuses.TryGetValue(d.By, out var st) ? st.Name : null
                : r.Actions.TryGetValue(d.By, out var a) ? a.Name : null;
            var name = called.TryGetValue(d.Who, out var c) ? c : "Someone";
            var killer = by is { Length: > 0 } ? by.Trim() : null;
            rows.Add(new PullRow(d.T, killer != null ? $"{name} died to {killer}" : $"{name} died", 1, Array.Empty<MechanicKind>(), who?.Role ?? "dps", who?.Job, name, killer));
            deaths.Add(d.T);
        }
        rows.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.Death.CompareTo(b.Death));
        var waymarks = r.Events.OfType<WaymarksEvent>().LastOrDefault()?.Marks.Count ?? 0;
        var tethers = r.Events.OfType<TetherEvent>().Count(t => t.On);
        double? wipeAt = wipeDeaths.Count > 0 ? wipeDeaths.Min(d => d.T) : null;
        return new PullView(rows, PullStats.PhaseStarts(r), deaths, waymarks, tethers, wipe) { WipeAt = wipeAt };
    }

    /// <summary>
    /// (A pull's page uses <see cref="PullStats.PhaseStarts"/> since 0.9.0, as the site and the replay do: far mobs left out.)
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
