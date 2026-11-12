using System;
using System.Collections.Generic;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>A step on the on-screen callouts: its number (from 0), its big words, its name under them (when they aren't just the name), when it goes off and how soon.</summary>
/// <param name="At">When its first mechanic goes off, in the plan's own time (its cues' times).</param>
/// <param name="In">Seconds until then (negative once it has).</param>
public readonly record struct CalloutLine(int Step, string Big, string? Name, double At, double In)
{
    /// <summary>It's going off now (its time has come): shown as "Now" for <see cref="CalloutTimeline.NowFor"/>.</summary>
    public bool Now => In <= 0;
}

/// <summary>
/// A plan as the callouts read it, worked out once a plan (not each frame): when each step goes off
/// (<see cref="CalloutTimeline.StepTimes"/>), its words (<see cref="CalloutTimeline.Words"/>), and the
/// steps with a time in the order they go off.
/// </summary>
public sealed class CalloutPlan
{
    public CalloutPlan(Plan plan, double[]? times = null)
    {
        Plan = plan;
        Times = times ?? CalloutTimeline.StepTimes(plan.Game?.Cues, plan.Steps.Count);
        var n = Math.Min(Times.Length, plan.Steps.Count);
        Words = new (string, string?)[n];
        var order = new List<int>(n);
        for (var s = 0; s < n; s++)
        {
            if (double.IsNaN(Times[s])) continue;
            Words[s] = CalloutTimeline.Words(plan.Steps[s]);
            order.Add(s);
        }
        var t = Times;
        order.Sort((a, b) =>
        {
            var c = t[a].CompareTo(t[b]);
            return c != 0 ? c : a.CompareTo(b);
        });
        Order = order.ToArray();
    }

    public Plan Plan { get; }

    /// <summary>Each step's time (NaN: no cue).</summary>
    public double[] Times { get; }

    /// <summary>Each timed step's big words and name (the others' are left empty).</summary>
    public (string Big, string? Name)[] Words { get; }

    /// <summary>The timed steps, soonest first (ties by their number).</summary>
    public int[] Order { get; }
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
        Upcoming(new CalloutPlan(plan, times), clock, ahead, count, list);
        return list;
    }

    /// <summary>As above, into <paramref name="into"/> (cleared first), from a plan read once: nothing made but the lines, each frame.</summary>
    public static void Upcoming(CalloutPlan plan, double clock, double ahead, int count, List<CalloutLine> into)
    {
        into.Clear();
        if (count <= 0 || !double.IsFinite(clock)) return;
        foreach (var s in plan.Order)
        {
            if (into.Count >= count) break;
            var d = plan.Times[s] - clock;
            if (d < -NowFor || d > ahead) continue;
            var (big, name) = plan.Words[s];
            into.Add(new CalloutLine(s, big, name, plan.Times[s], d));
        }
    }

    /// <summary>The countdown as the callouts write it: "Now", "8" (seconds, rounded up), "1:05".</summary>
    public static string Countdown(double seconds)
    {
        if (seconds <= 0) return "Now";
        // At least 1 while it's still to come (a hair over 0 isn't "0").
        var s = Math.Max(1, (int)Math.Ceiling(seconds - 1e-9));
        return s < 60 ? s.ToString(System.Globalization.CultureInfo.InvariantCulture) : Format.Clock(s);
    }
}

/// <summary>
/// When the callouts' sound plays: once a step, a pull, <c>lead</c> seconds before it goes off. A
/// step the clock passes without its sound (a match moved the plan on, the sound was off, it was
/// already past when the plan engaged) is marked heard, never played late. What's been heard is kept by
/// plan for the pull (switching to another plan and back mid-pull plays nothing twice); reset for a new pull.
/// </summary>
public sealed class CalloutSounds
{
    private readonly Dictionary<string, bool[]> byPlan = new();
    private bool[] heard = Array.Empty<bool>();
    private string? of;

    public void Reset()
    {
        byPlan.Clear();
        heard = Array.Empty<bool>();
        of = null;
    }

    /// <summary>
    /// The step whose sound is due now (marked heard), or null; call every frame while the plan's clock
    /// runs. The same plan read again (it changed on the site) keeps what's been heard, and so does a
    /// plan switched back to in the same pull; one not yet followed this pull starts afresh.
    /// </summary>
    public int? Due(Plan plan, double[] times, double clock, double lead)
    {
        var id = plan.Id ?? string.Empty;
        if (of != id || heard.Length != times.Length)
        {
            if (of != null) byPlan[of] = heard;
            of = id;
            heard = byPlan.TryGetValue(id, out var had) ? had : Array.Empty<bool>();
            if (heard.Length != times.Length) Array.Resize(ref heard, times.Length);
            byPlan[id] = heard;
        }
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
