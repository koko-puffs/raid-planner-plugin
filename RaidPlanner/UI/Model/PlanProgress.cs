using System;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>
/// Where a pull being recorded is in its plan (0.8.0: Home's ember card): the plan's length (its last cue's time,
/// else its last step's written time) and the step after the one it's on, with how long until it.
/// </summary>
public static class PlanProgress
{
    /// <summary>A plan's length: its last cue's time, else its last step's written time; 0 when it has neither.</summary>
    public static double Length(Plan plan)
    {
        if (plan.Game?.Cues is { Count: > 0 } cues) return cues.Max(c => c.T);
        for (var i = plan.Steps.Count - 1; i >= 0; i--)
            if (Format.ParseClock(plan.Steps[i].Time) is { } t) return t;
        return 0;
    }

    /// <summary>When a step begins: its first cue's time, else its written time; null when it has neither.</summary>
    public static double? StartOf(Plan plan, int step)
    {
        if (step < 0 || step >= plan.Steps.Count) return null;
        if (plan.Game?.Cues is { Count: > 0 } cues)
        {
            var cue = cues.Where(c => c.Step == step).Select(c => (double?)c.T).Min();
            if (cue != null) return cue;
        }
        return Format.ParseClock(plan.Steps[step].Time);
    }

    /// <summary>The step after <paramref name="step"/> (its big words) and the seconds until it at <paramref name="clock"/>, when it's known and ahead; null after the last step.</summary>
    public static (string Name, double? In)? Next(Plan plan, int step, double clock)
    {
        var next = step + 1;
        if (next < 1 || next >= plan.Steps.Count) return null;
        var at = StartOf(plan, next);
        double? left = at is { } t && t - clock >= 0 ? t - clock : null;
        return (plan.Steps[next].Big, left);
    }
}
