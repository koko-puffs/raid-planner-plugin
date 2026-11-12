using System;
using System.Collections.Generic;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>A step on the on-screen callouts: its number (from 0), its big words, its name under them (when they aren't just the name), when it goes off and how soon.</summary>
/// <param name="At">When its first mechanic goes off, in the plan's own time (its cues' times).</param>
/// <param name="In">Seconds until then (negative once it has).</param>
public sealed record CalloutLine(int Step, string Big, string? Name, double At, double In)
{
    /// <summary>It's going off now (its time has come): shown as "Now" for <see cref="CalloutTimeline.NowFor"/>.</summary>
    public bool Now => In <= 0;
}

/// <summary>
/// The on-screen callouts' timeline, worked out away from ImGui so it's tested: when each step of a
/// plan goes off (its first cue's time: the plan's own clock, as the cue follower moves it,
/// <c>PlanService.PlanClock</c>), and the next few steps from now, each with its countdown.
/// </summary>
public static class CalloutTimeline
{
    /// <summary>A step stays on as "Now" this long after its time.</summary>
    public const double NowFor = 2.0;

    /// <summary>When each step's first mechanic goes off (its earliest cue), NaN for a step with no cue.</summary>
    public static double[] StepTimes(IReadOnlyList<PlanCue>? cues, int steps)
    {
        var times = new double[Math.Max(0, steps)];
        Array.Fill(times, double.NaN);
        if (cues == null) return times;
        foreach (var c in cues)
        {
            if (c.Step < 0 || c.Step >= times.Length || !double.IsFinite(c.T)) continue;
            if (double.IsNaN(times[c.Step]) || c.T < times[c.Step]) times[c.Step] = c.T;
        }
        return times;
    }

    /// <summary>A step's big words and the name shown under them (null when the big words are the name).</summary>
    public static (string Big, string? Name) Words(PlanStep step)
    {
        var name = PlanJson.WithoutPhase(step.Name);
        var big = step.Big;
        return (big, string.Equals(big.Trim(), name, StringComparison.OrdinalIgnoreCase) ? null : name);
    }

    /// <summary>
    /// The next <paramref name="count"/> steps at the plan's time <paramref name="clock"/>: those going
    /// off within <paramref name="ahead"/> seconds, and one that went off in the last
    /// <see cref="NowFor"/> ("Now"), soonest first. Steps with no cue have no time and aren't shown.
    /// </summary>
    public static List<CalloutLine> Upcoming(Plan plan, double[] times, double clock, double ahead, int count)
    {
        var list = new List<CalloutLine>();
        if (count <= 0 || !double.IsFinite(clock)) return list;
        var n = Math.Min(times.Length, plan.Steps.Count);
        var order = new List<int>();
        for (var s = 0; s < n; s++)
        {
            var at = times[s];
            if (double.IsNaN(at)) continue;
            var d = at - clock;
            if (d < -NowFor || d > ahead) continue;
            order.Add(s);
        }
        order.Sort((a, b) =>
        {
            var c = times[a].CompareTo(times[b]);
            return c != 0 ? c : a.CompareTo(b);
        });
        foreach (var s in order)
        {
            if (list.Count >= count) break;
            var (big, name) = Words(plan.Steps[s]);
            list.Add(new CalloutLine(s, big, name, times[s], times[s] - clock));
        }
        return list;
    }

    /// <summary>The countdown as the callouts write it: "Now", "8" (seconds, rounded up), "1:05".</summary>
    public static string Countdown(double seconds)
    {
        if (seconds <= 0) return "Now";
        var s = (int)Math.Ceiling(seconds - 1e-9);
        return s < 60 ? s.ToString(System.Globalization.CultureInfo.InvariantCulture) : Format.Clock(s);
    }
}

/// <summary>
/// When the callouts' sound plays: once a step, a pull, <c>lead</c> seconds before it goes off. A
/// step the clock passes without its sound (a match moved the plan on, the sound was off, it was
/// already past when the plan engaged) is marked heard, never played late. Reset for a new pull or another plan.
/// </summary>
public sealed class CalloutSounds
{
    private bool[] heard = Array.Empty<bool>();
    private string? of;

    public void Reset()
    {
        Array.Clear(heard);
        of = null;
    }

    /// <summary>
    /// The step whose sound is due now (marked heard), or null; call every frame while the plan's clock
    /// runs. The same plan read again (it changed on the site) keeps what's been heard; another starts afresh.
    /// </summary>
    public int? Due(Plan plan, double[] times, double clock, double lead)
    {
        if (of != plan.Id)
        {
            of = plan.Id;
            heard = new bool[times.Length];
        }
        else if (heard.Length != times.Length) Array.Resize(ref heard, times.Length);
        if (!double.IsFinite(clock)) return null;
        int? due = null;
        var dueAt = double.PositiveInfinity;
        for (var s = 0; s < times.Length; s++)
        {
            if (heard[s] || double.IsNaN(times[s])) continue;
            var d = times[s] - clock;
            if (d <= 0)
            {
                heard[s] = true;
                continue;
            }
            if (d > lead || times[s] >= dueAt) continue;
            due = s;
            dueAt = times[s];
        }
        if (due is { } step) heard[step] = true;
        return due;
    }
}

/// <summary>
/// A sample of the on-screen callouts (Settings → Plan window, `/raidplan callouts preview`), to
/// place and size them without a pull: made-up steps a few seconds apart, counting down and looping.
/// </summary>
public static class CalloutSample
{
    private static readonly (string Big, string? Name, double At)[] Steps =
    {
        ("Out, then in", "Thunderous Breath", 6), ("Spread for the tethers", null, 14), ("Stack in the middle", "Wild Charge", 21),
        ("Tank swap", null, 27), ("Exaflares: middle, step through the first", "Exaflares", 36), ("Raidwide: mitigate", "Starfall", 44),
    };

    /// <summary>How long the sample runs before it starts again (seconds).</summary>
    public const double Loop = 50;

    /// <summary>The sample's next steps at second <paramref name="t"/> of any steady clock.</summary>
    public static List<CalloutLine> At(double t, double ahead, int count)
    {
        var clock = (t % Loop + Loop) % Loop;
        var list = new List<CalloutLine>();
        for (var i = 0; i < Steps.Length && list.Count < count; i++)
        {
            var d = Steps[i].At - clock;
            if (d < -CalloutTimeline.NowFor || d > ahead) continue;
            list.Add(new CalloutLine(i, Steps[i].Big, Steps[i].Name, Steps[i].At, d));
        }
        return list;
    }
}
