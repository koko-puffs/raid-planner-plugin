using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>A step of the pull on the dock (a plan made from it would have it): its number, name, start and end in the pull's seconds.</summary>
public sealed record ReplayStep(int Index, string Name, double Start, double End);

/// <summary>A phase's name on the dock's top row, from its start.</summary>
public sealed record ReplayPhase(string Name, double Start);

/// <summary>A bar on a lane (px from the lane's left): one status, or several at once that found no row (<see cref="Items"/>).</summary>
public sealed class LaneBar<T>
{
    public required string Key { get; init; }
    public float Left { get; init; }
    public float Right { get; set; }
    public int Row { get; init; }
    public List<T> Items { get; } = new();
}

/// <summary>
/// The sizes and the steps of Replay's dock (the site's replayLanes.ts and the plan made from a pull, model/recording.ts's
/// <c>draftSteps</c> and model/timeline.ts's <c>timelineOf</c>): the phases' row, the steps as tall blocks, the bosses'
/// buffs, the party's debuffs and the ruler, each lane there only when the pull has something for it, so the dock is as
/// tall as what's in it. Sizes in CSS px. Pure.
/// </summary>
public static class ReplayLanes
{
    /// <summary>The frame's border and padding (top and bottom together), the gap between lanes, each lane's height, a status row's and the gap between rows.</summary>
    public const float Frame = 2 + 8 + 8;
    public const float Gap = 4;
    public const float Phases = 20;
    public const float Steps = 56;
    public const float Row = 22;
    public const float RowGap = 4;
    public const float Ruler = 16;

    /// <summary>The slab's padding round the frame (the site's 0.75rem), the same on every side.</summary>
    public const float SlabPad = 12;

    /// <summary>The rows a lane has at most: the bosses' buffs, the party's debuffs.</summary>
    public const int BossRows = 2;
    public const int PartyRows = 3;

    /// <summary>A bar's icon with the space after it (px), and the icons a crowded bar shows before "+n".</summary>
    public const float IconStep = 22;
    public const int ShownIcons = 4;

    /// <summary>How far two bars on a row may meet (px).</summary>
    private const float BarTouch = 2;

    /// <summary>A lane of <paramref name="rows"/> status rows (0: no lane).</summary>
    public static float StatusLanePx(int rows) => rows > 0 ? rows * Row + (rows - 1) * RowGap : 0;

    /// <summary>The frame's height (px), with the bosses' and the party's rows it has (0: the lane's left out).</summary>
    public static float LanesPx(int bossRows, int partyRows)
    {
        var boss = StatusLanePx(bossRows);
        var party = StatusLanePx(partyRows);
        var count = 3 + (boss > 0 ? 1 : 0) + (party > 0 ? 1 : 0);
        return Frame + Phases + Steps + boss + party + Ruler + (count - 1) * Gap;
    }

    /// <summary>The dock's height (px): the frame and the slab's padding over and under it.</summary>
    public static float DockPx(int bossRows, int partyRows) => LanesPx(bossRows, partyRows) + 2 * SlabPad;

    /// <summary>
    /// The ruler's times: every 30 s (a minute on a long fight, two on a very long one), further apart where the lane is
    /// narrow so no two labels touch (44 px at least between them). <paramref name="width"/> 0: not measured yet.
    /// </summary>
    public static List<double> RulerTimes(double total, float width)
    {
        double every = total > 600 ? 120 : total > 300 ? 60 : 30;
        if (width > 0)
            while (every < 600 && every / total * width < 44)
                every *= 2;
        var list = new List<double>();
        for (double t = 0; t <= total; t += every) list.Add(t);
        return list;
    }

    /// <summary>How wide a bar of <paramref name="n"/> icons is at least (px): its icons, at most four, and a "+n".</summary>
    public static float BarMinPx(int n, float step = IconStep) => (Math.Min(n, ShownIcons) + (n > ShownIcons ? 1 : 0)) * step;

    /// <summary>
    /// A lane's bars in rows, where they're drawn (px; the site's <c>packLane</c>): each on the first row clear of the
    /// last bar there, drawn at least its icons wide; else on a new row while there are fewer than
    /// <paramref name="rows"/>; else it joins the bar on the row free soonest, which then shows both icons.
    /// </summary>
    public static (List<LaneBar<T>> Bars, int Rows) Pack<T>(IEnumerable<(float From, float To, string Key, T Item)> items, int rows, float step = IconStep)
    {
        var bars = new List<LaneBar<T>>();
        var last = new List<LaneBar<T>>();
        float Reach(LaneBar<T> b) => MathF.Max(b.Right, b.Left + BarMinPx(b.Items.Count, step));
        foreach (var it in items.OrderBy(x => x.From))
        {
            var row = last.FindIndex(b => Reach(b) - BarTouch <= it.From);
            if (row < 0 && last.Count < rows) row = last.Count;
            if (row >= 0)
            {
                var bar = new LaneBar<T> { Key = it.Key, Left = it.From, Right = MathF.Max(it.From, it.To), Row = row };
                bar.Items.Add(it.Item);
                bars.Add(bar);
                if (row == last.Count) last.Add(bar);
                else last[row] = bar;
                continue;
            }
            var soonest = 0;
            for (var i = 1; i < last.Count; i++)
                if (Reach(last[i]) < Reach(last[soonest])) soonest = i;
            var joined = last[soonest];
            joined.Items.Add(it.Item);
            joined.Right = MathF.Max(joined.Right, it.To);
        }
        return (bars, last.Count);
    }

    /// <summary>How long a step after the last is drawn when nothing says (s), at least and at most.</summary>
    private const double LastMin = 8, LastMax = 30, DefaultSpan = 12;

    /// <summary>A step never spans more than this (s); mechanics within <see cref="Together"/> of each other share one.</summary>
    public const double Span = 8;

    public const double Together = 1.5;

    /// <summary>The most steps a pull gets: past it, mechanics are put together more.</summary>
    public const int MostSteps = 150;

    /// <summary>
    /// The steps a plan made from the pull would have (the site's <c>draftSteps</c>, on the plugin's timeline of the pull):
    /// "Pull" at 0, then a step each time something goes off, mechanics within 1.5 s of each other sharing one, a step
    /// never spanning more than 8 s nor two phases; each named by its mechanics ("A + B + 2 more"), "P2 · " in front in a
    /// later phase. Each ends where the next starts; the last runs on for the steps' usual length (8 to 30 s).
    /// </summary>
    public static List<ReplayStep> StepsOf(IReadOnlyList<TimelineEntry> entries, IReadOnlyList<double> phaseStarts)
    {
        var drafts = new List<(double T, string Name)>();
        var together = Together;
        for (var tries = 0; tries < 12; tries++)
        {
            var span = tries < 4 ? Span : double.PositiveInfinity;
            drafts.Clear();
            var cur = new List<TimelineEntry>();
            var phase = 1;
            var curPhase = 1;
            void Flush()
            {
                if (cur.Count == 0) return;
                var names = cur.Select(e => e.Name).Distinct().ToList();
                var name = names.Count <= 2 ? string.Join(" + ", names) : $"{names[0]} + {names[1]} + {names.Count - 2} more";
                drafts.Add((cur[0].T, curPhase > 1 ? $"P{curPhase} · {name}" : name));
                cur.Clear();
            }
            foreach (var m in entries.OrderBy(e => e.T))
            {
                while (phase < phaseStarts.Count && phaseStarts[phase] <= m.T) phase++;
                if (cur.Count > 0 && (m.T - cur[^1].T > together || m.T - cur[0].T > span || phase != curPhase)) Flush();
                curPhase = phase;
                cur.Add(m);
            }
            Flush();
            if (drafts.Count < MostSteps) break;
            together += 1.5;
        }
        if (drafts.Count > MostSteps) drafts.RemoveRange(MostSteps, drafts.Count - MostSteps);
        drafts.Insert(0, (0, "Pull"));
        var spans = new List<double>();
        for (var i = 1; i < drafts.Count; i++)
            if (drafts[i].T - drafts[i - 1].T > 0) spans.Add(drafts[i].T - drafts[i - 1].T);
        var usual = spans.Count > 0 ? spans.Average() : DefaultSpan;
        var lastName = drafts[^1].Name;
        var lastStart = drafts[^1].T;
        var lastSpan = lastName.Contains("enrage", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(4, Math.Round(lastStart / 50))
            : Math.Min(LastMax, Math.Max(LastMin, Math.Round(usual)));
        var steps = new List<ReplayStep>();
        for (var i = 0; i < drafts.Count; i++)
            steps.Add(new ReplayStep(i, drafts[i].Name, drafts[i].T, i + 1 < drafts.Count ? drafts[i + 1].T : drafts[i].T + lastSpan));
        return steps;
    }

    /// <summary>
    /// The phases the steps name (the site's <c>timelineOf</c>): each "P2 · " a phase from its first step; a later one
    /// named means the steps before it were the first (P1 at 0). None in a pull of one phase.
    /// </summary>
    public static List<ReplayPhase> PhasesOf(IReadOnlyList<ReplayStep> steps)
    {
        var phases = new List<ReplayPhase>();
        foreach (var s in steps)
        {
            var dot = s.Name.IndexOf(" · ", StringComparison.Ordinal);
            if (dot < 2 || s.Name[0] != 'P' || !s.Name[1..dot].All(char.IsDigit)) continue;
            var name = s.Name[..dot];
            if (phases.Count == 0 || phases[^1].Name != name) phases.Add(new ReplayPhase(name, s.Start));
        }
        if (phases.Count > 0 && phases[0].Start > 0 && phases[0].Name != "P1") phases.Insert(0, new ReplayPhase("P1", 0));
        return phases;
    }

    /// <summary>A moment counts toward a step this long before the step's moment (a hit lands as its cast ends).</summary>
    public const double StepEarly = 0.5;

    /// <summary>The step a moment falls in (the site's <c>stepAt</c>): the last whose start is at or before it (and half a second).</summary>
    public static int StepAt(IReadOnlyList<ReplayStep> steps, double t)
    {
        var step = 0;
        for (var i = 0; i < steps.Count; i++)
            if (steps[i].Start <= t + StepEarly) step = i;
        return step;
    }
}
