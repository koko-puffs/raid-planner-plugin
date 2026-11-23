using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>A part of a sentence: its words, and whether they're the part to see (a mechanic's name, drawn brighter).</summary>
public readonly record struct Said(string Text, bool Strong = false);

/// <summary>
/// A kept pull in a few facts, read once from its file on a worker (0.8.0: Home's latest pull, the Pulls tab's rows, a
/// pull's own screen): the phase it ended in (of how many the recording saw), the mechanic it ended at, its deaths
/// before the wipe (as the strip shows them) and the first of them, how many of the party it kept. Says the pull in
/// one sentence in plain words (<see cref="Sentence"/>) and where it ended in a word or two (<see cref="Where"/>).
/// </summary>
public sealed record PullGist(int Phase, int Phases, string? EndedAt, int Deaths, string? FirstWho, string? FirstBy, int Party)
{
    /// <summary>"P3", when the recording saw more than one phase.</summary>
    public string? PhaseLabel => Phases > 1 ? $"P{Math.Max(1, Phase)}" : null;

    /// <summary>Its facts from the recording and what the pull's screen reads of it.</summary>
    public static PullGist Of(Recording r, PullView v)
    {
        // Where it ended: the wipe's first death, else the end of the recording.
        var end = v.WipeAt ?? r.Duration;
        var phase = Math.Max(1, v.Phases.Count(p => p <= end + 0.01));
        // The last mechanic that went off by then (a death isn't one).
        string? at = null;
        foreach (var row in v.Rows)
        {
            if (row.T > end + 0.01) break;
            if (!row.Death && !string.IsNullOrWhiteSpace(row.Name)) at = row.Name.Trim();
        }
        var first = v.Rows.FirstOrDefault(x => x.Death);
        return new PullGist(phase, Math.Max(1, v.Phases.Count), at, v.Deaths.Count, first?.DeathWho, first?.DeathBy, r.Party.Count);
    }

    /// <summary>Where a pull ended, for a row: "P3", else "at Hello World", else nothing.</summary>
    public static string? Where(PullGist? g) =>
        g == null ? null : g.PhaseLabel ?? (g.EndedAt is { } at ? $"at {at}" : null);

    private static readonly string[] Numbers = { "No", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten" };

    /// <summary>"Three", or "14" past ten (a sentence's start).</summary>
    public static string Number(int n) => n >= 0 && n < Numbers.Length ? Numbers[n] : n.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The pull in a sentence or two, in plain words: how it ended and where ("Wiped at Hello World 2, P3."), then its
    /// deaths ("Three deaths, the first Velka Moor to Latent Defect."), then, given the pull before it in the same
    /// duty, how far this one got against it ("2 seconds further than pull 11."). Without its facts (not read yet,
    /// or unreadable), what the list knows: how it ended and its length.
    /// </summary>
    public static IReadOnlyList<Said> Sentence(Outcome outcome, double duration, PullGist? g, (int Pull, double Duration)? before = null)
    {
        var said = new List<Said>();
        var length = Words.Clock(duration);
        if (g == null)
        {
            said.Add(new Said(outcome switch
            {
                Outcome.Wipe => $"Wiped after {length}.",
                Outcome.Clear => $"Cleared in {length}.",
                _ => $"Ended after {length}.",
            }));
        }
        else
        {
            var phase = g.PhaseLabel;
            switch (outcome)
            {
                case Outcome.Clear:
                    said.Add(new Said($"Cleared in {length}."));
                    break;
                default:
                    var verb = outcome == Outcome.Wipe ? "Wiped" : "Ended";
                    if (g.EndedAt is { } at)
                    {
                        said.Add(new Said($"{verb} at "));
                        said.Add(new Said(at, true));
                        said.Add(new Said(phase != null ? $", {phase}." : "."));
                    }
                    else said.Add(new Said(phase != null ? $"{verb} in {phase}, after {length}." : $"{verb} after {length}."));
                    break;
            }
            said.Add(new Said(" " + DeathsLine(outcome, g)));
        }
        if (before is { } b && outcome != Outcome.Clear && double.IsFinite(b.Duration) && double.IsFinite(duration))
        {
            var d = duration - b.Duration;
            var secs = (int)Math.Round(Math.Abs(d));
            if (secs >= 1)
            {
                var by = secs < 60 ? (secs == 1 ? "1 second" : $"{secs} seconds") : Words.Clock(secs);
                said.Add(new Said($" {by} {(d > 0 ? "further than" : "short of")} pull {Math.Max(1, b.Pull)}."));
            }
            else said.Add(new Said($" As far as pull {Math.Max(1, b.Pull)}."));
        }
        return said;
    }

    /// <summary>The deaths part: "No deaths before the wipe.", "One death: Velka Moor to Latent Defect.", "Three deaths, the first …".</summary>
    private static string DeathsLine(Outcome outcome, PullGist g)
    {
        if (g.Deaths <= 0) return outcome == Outcome.Wipe ? "No deaths before the wipe." : "No deaths.";
        var who = g.FirstWho;
        var to = g.FirstBy != null ? $" to {g.FirstBy}" : string.Empty;
        if (g.Deaths == 1) return who != null ? $"One death: {who}{to}." : "One death.";
        var n = $"{Number(g.Deaths)} deaths";
        return who != null ? $"{n}, the first {who}{to}." : $"{n}.";
    }

    /// <summary>"On your Raid Codex since 23:49" (today), "… since yesterday 23:52", "… since Mon 21:04", "… since 2 Nov 21:04".</summary>
    public static string SinceLine(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var when = local.Date == now.ToLocalTime().Date ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : Format.Day(at, now);
        return $"On your Raid Codex since {when}";
    }

    /// <summary>The sentence's words, as one string.</summary>
    public static string Plain(IReadOnlyList<Said> said) => string.Concat(said.Select(s => s.Text));
}
