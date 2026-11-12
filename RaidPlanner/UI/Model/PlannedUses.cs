using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>A cooldown the plan has someone use, placed on the plan's own clock: its step, the plan's entry, and when (the step's time + its <c>at</c>).</summary>
public readonly record struct PlannedUse(int Step, PlanCooldown Cooldown, double At);

/// <summary>
/// A plan's cooldown assignments (since 0.5.0, SPEC5 §3.1) as the overlays read them, worked out once a plan: each placed on the plan's
/// own clock (its step's time, as the callouts time a step: its earliest cue, <see cref="CalloutTimeline.StepTimes"/>; plus its
/// <c>at</c>), soonest first. A step with no cue has no time, so its assignments aren't placed (the plan window still lists them).
/// Game-free, so it's tested.
/// </summary>
public sealed class PlannedUses
{
    /// <summary>A planned use stays "now" this long after its time (as a callout does).</summary>
    public const double NowFor = CalloutTimeline.NowFor;

    /// <summary>How far ahead the tracker marks a planned use (seconds).</summary>
    public const double Ahead = 15;

    public PlannedUses(Plan plan, double[]? times = null)
    {
        Plan = plan;
        var t = times ?? CalloutTimeline.StepTimes(plan.Game?.Cues, plan.Steps.Count);
        var all = new List<PlannedUse>();
        for (var s = 0; s < plan.Steps.Count && s < t.Length; s++)
        {
            if (!double.IsFinite(t[s])) continue;
            foreach (var c in plan.Steps[s].Cooldowns)
                all.Add(new PlannedUse(s, c, t[s] + c.At));
        }
        All = all.OrderBy(u => u.At).ThenBy(u => u.Step).ToList();
    }

    public Plan Plan { get; }

    /// <summary>Every placed use, soonest first.</summary>
    public IReadOnlyList<PlannedUse> All { get; }

    /// <summary>
    /// The soonest use by <paramref name="spot"/> that <paramref name="match"/> takes, from <see cref="NowFor"/> ago to
    /// <paramref name="ahead"/> on at the plan's time <paramref name="clock"/>; null when there's none.
    /// </summary>
    public PlannedUse? Next(string spot, Func<PlanCooldown, bool> match, double clock, double ahead = Ahead)
    {
        if (!double.IsFinite(clock)) return null;
        foreach (var u in All)
        {
            var d = u.At - clock;
            if (d < -NowFor) continue;
            if (d > ahead) break;
            if (u.Cooldown.Spot == spot && match(u.Cooldown)) return u;
        }
        return null;
    }

    /// <summary>
    /// Your line under a callout (<c>You: Reprisal</c>; several joined, each with its time off the step's when it has one:
    /// <c>You: Reprisal, Rampart +4 s</c>), or null when the step has nothing for <paramref name="spot"/>.
    /// </summary>
    public static string? YouLine(PlanStep step, string? spot)
    {
        var mine = step.CooldownsFor(spot).ToList();
        if (mine.Count == 0) return null;
        return "You: " + string.Join(", ", mine.Select(c => c.At != 0 ? $"{c.Name} {c.AtWords}" : c.Name));
    }

    /// <summary>The tracker's words for a planned use coming: "planned in 8 s", "planned now".</summary>
    public static string Soon(double inSeconds) =>
        inSeconds <= 0 ? "planned now" : $"planned in {Math.Max(1, (int)Math.Ceiling(inSeconds - 1e-9)).ToString(System.Globalization.CultureInfo.InvariantCulture)} s";

    /// <summary>A planned use's line for a tooltip: "Planned: Reprisal at 3:02, for Akh Morn."</summary>
    public static string Tip(PlannedUse u) =>
        $"Planned: {u.Cooldown.Name} at {Format.Clock(Math.Max(0, u.At))}{(u.Cooldown.Note is { } n ? $", {n}" : string.Empty)}.";
}
