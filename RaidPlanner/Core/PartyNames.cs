using System;
using System.Collections.Generic;

namespace RaidPlanner.Core;

/// <summary>
/// What a window calls a player: their name (the game's), else their job. The game has no MT, OT, H1…:
/// those spots are the plugin's guess from jobs and the party list's order, so (since the release after 0.11.1) no window
/// shows one for a player (they stay inside: the recording's <c>slot</c>, the summary, keys). Two of the
/// same job shown by job are told apart by the party list's order: "WAR", then "WAR 2", "WAR 3"…, as the
/// site's logs have it. A plan's own spots (the plan window, "You:" in the callouts) are the plan's, not this.
/// </summary>
public static class PartyNames
{
    /// <summary>What a player with no job known is called ("Player", "Player 2").</summary>
    public const string NoJob = "Player";

    /// <summary>
    /// The party's labels, in the order given (the party list's): each one's name when it has one and names
    /// are shown (<paramref name="jobsOnly"/> off), else their job, numbered from the second of that job shown
    /// by job ("WAR", "WAR 2").
    /// </summary>
    public static string[] Labels(IReadOnlyList<(string? Name, string? Job)> party, bool jobsOnly = false)
    {
        var labels = new string[party.Count];
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < party.Count; i++)
        {
            var (name, job) = party[i];
            if (!jobsOnly && !string.IsNullOrWhiteSpace(name))
            {
                labels[i] = name.Trim();
                continue;
            }
            var j = string.IsNullOrWhiteSpace(job) ? NoJob : job.Trim().ToUpperInvariant();
            var n = seen.TryGetValue(j, out var k) ? k + 1 : 1;
            seen[j] = n;
            labels[i] = Numbered(j, n);
        }
        return labels;
    }

    /// <summary>One player's label among the party (<see cref="Labels"/>); <paramref name="index"/> is theirs in it. Out of range: <see cref="NoJob"/>.</summary>
    public static string Label(IReadOnlyList<(string? Name, string? Job)> party, int index, bool jobsOnly = false) =>
        index >= 0 && index < party.Count ? Labels(party, jobsOnly)[index] : NoJob;

    /// <summary>A job shown with its number among those of it shown by job: the first bare ("WAR"), then "WAR 2".</summary>
    public static string Numbered(string job, int nth) =>
        nth <= 1 ? job : $"{job} {nth.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>
    /// A player when the job's number is already worked out (<paramref name="byJob"/>: "WAR 2", the meter's
    /// <c>Tag</c>): their name unless <paramref name="jobsOnly"/> or there's none, else that.
    /// </summary>
    public static string Who(string? name, string byJob, bool jobsOnly) =>
        !jobsOnly && !string.IsNullOrWhiteSpace(name) ? name.Trim() : byJob.Length > 0 ? byJob : NoJob;
}
