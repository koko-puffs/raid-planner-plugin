using System;
using System.Collections.Generic;
using System.Globalization;

namespace RaidPlanner.UI.Model;

/// <summary>
/// The compact meter's numbers (since 0.7.0): three figures in a fixed column ("27.8k", "9.05k",
/// "165k", "1.23M"), so the last digits don't dance at every snapshot; whole numbers ("27,840") as a setting.
/// </summary>
public static class MeterNumber
{
    /// <summary>A number in three figures, rounded: under 1,000 whole; then k, M, B, each with as many decimals as keep three figures.</summary>
    public static string Short(double v)
    {
        if (!double.IsFinite(v)) return "0";
        var neg = v < 0;
        v = Math.Abs(v);
        string s;
        if (Math.Round(v) < 1000) s = Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
        else
        {
            var (div, unit) = v < 999_500 ? (1e3, "k") : v < 999_500_000 ? (1e6, "M") : (1e9, "B");
            var x = v / div;
            // Two decimals under 10, one under 100, none from there; a rounding that reaches the next (9.996 → 10.00) takes its fewer.
            var dec = x < 9.995 ? 2 : x < 99.95 ? 1 : 0;
            s = Math.Round(x, dec, MidpointRounding.AwayFromZero).ToString(dec switch { 2 => "0.00", 1 => "0.0", _ => "0" }, CultureInfo.InvariantCulture) + unit;
        }
        return neg ? "−" + s : s;
    }

    /// <summary>The meter's number as the setting has it: three figures, or whole with thousands' commas.</summary>
    public static string Text(double v, bool whole) => whole ? Format.Thousands((long)Math.Round(v)) : Short(v);
}

/// <summary>One of the compact meter's rows: which of the ranked rows (its index there), its rank (1 the top), and whether it's you pinned at the foot.</summary>
public readonly record struct PlateSlot(int Index, int Rank, bool Pinned);

/// <summary>
/// Which rows the compact meter shows: at most eight (or as many as its height has room for), the top
/// ones; when you're ranked below the last shown, the last row is you, pinned, with your real rank.
/// </summary>
public static class PlateRows
{
    /// <summary>The most rows the meter shows: the top eight (no alliance view).</summary>
    public const int Max = 8;

    /// <summary>
    /// Fills <paramref name="into"/> with the rows to show of <paramref name="count"/> ranked ones (biggest first), you at
    /// <paramref name="you"/> (−1: not in them), with room for <paramref name="room"/> rows (at most <see cref="Max"/>).
    /// </summary>
    public static void Select(int count, int you, int room, List<PlateSlot> into)
    {
        into.Clear();
        var n = Math.Min(Math.Min(room, Max), count);
        if (n <= 0) return;
        var pin = you >= n;
        var top = pin ? n - 1 : n;
        for (var i = 0; i < top; i++) into.Add(new PlateSlot(i, i + 1, false));
        if (pin) into.Add(new PlateSlot(you, you + 1, true));
    }
}

/// <summary>
/// Ranks that swap only once a lead has held (a second): two players a few hundred apart don't trade
/// places at every snapshot. Keeps the order shown; each update, a row moves above one shown above it
/// only when it has been ahead of it for <see cref="Hold"/> seconds without a break. A new row goes in
/// at its place at once; <see cref="Reset"/> (a new fight, a tab) shows the true order.
/// </summary>
public sealed class RankHold
{
    /// <summary>How long a lead holds before the rows swap (seconds).</summary>
    public double Hold { get; init; } = 1.0;

    private readonly List<int> shown = new();
    private readonly Dictionary<int, double> value = new();
    private readonly Dictionary<(int Ahead, int Behind), double> leads = new();
    private readonly List<(int, int)> stale = new();
    private readonly HashSet<int> seen = new();

    /// <summary>The ids in the order shown, after the last update.</summary>
    public IReadOnlyList<int> Order => shown;

    public void Reset()
    {
        shown.Clear();
        leads.Clear();
        value.Clear();
    }

    /// <summary>
    /// The order to show for <paramref name="ids"/> (in their true order, biggest first) with their
    /// <paramref name="values"/>, at <paramref name="now"/> seconds. Allocates nothing once warm.
    /// </summary>
    public IReadOnlyList<int> Update(IReadOnlyList<int> ids, IReadOnlyList<double> values, double now)
    {
        value.Clear();
        seen.Clear();
        for (var i = 0; i < ids.Count; i++)
        {
            value[ids[i]] = values[i];
            seen.Add(ids[i]);
        }
        // Rows gone are dropped; new ones go in at their true place.
        for (var i = shown.Count - 1; i >= 0; i--)
            if (!seen.Contains(shown[i]))
                shown.RemoveAt(i);
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            if (shown.Contains(id)) continue;
            var at = 0;
            while (at < shown.Count && value[shown[at]] >= values[i]) at++;
            shown.Insert(at, id);
        }
        // Every pair shown in the wrong order: when its lead began (kept while it lasts).
        stale.Clear();
        foreach (var k in leads.Keys)
            if (!value.TryGetValue(k.Ahead, out var a) || !value.TryGetValue(k.Behind, out var b) || a <= b || Index(k.Ahead) < Index(k.Behind))
                stale.Add(k);
        foreach (var k in stale) leads.Remove(k);
        for (var i = 0; i < shown.Count; i++)
            for (var j = i + 1; j < shown.Count; j++)
                if (value[shown[j]] > value[shown[i]])
                    leads.TryAdd((shown[j], shown[i]), now);
        // Swap neighbours whose lead has held, until none does (a row past several that it has led for long enough moves past them all).
        for (var pass = 0; pass < shown.Count; pass++)
        {
            var moved = false;
            for (var i = 1; i < shown.Count; i++)
            {
                var up = shown[i];
                var down = shown[i - 1];
                if (!leads.TryGetValue((up, down), out var since) || now - since < Hold) continue;
                shown[i - 1] = up;
                shown[i] = down;
                leads.Remove((up, down));
                moved = true;
            }
            if (!moved) break;
        }
        return shown;
    }

    private int Index(int id) => shown.IndexOf(id);
}

/// <summary>The compact meter's heights, from its theme's tokens (CSS px): what the window is sized to.</summary>
public static class PlateSizes
{
    /// <summary>The limit break's row: a hairline 2 off the rows (1 for Readout), 3 to its row, its row 4 shorter than a player's (16 at the least).</summary>
    public static float LimitBreak(MeterTheme t) => (t.Id == MeterThemeId.Readout ? 1 : 2) + 3 + LimitBreakRow(t);

    public static float LimitBreakRow(MeterTheme t) => MathF.Max(16, t.Row.H - 4);

    /// <summary>The foot: a pixel, then an 18px line.</summary>
    public const float Foot = 19;

    /// <summary>The dashed rule before you pinned at the foot.</summary>
    public const float PinRule = 4;

    /// <summary>A row and the gap after it.</summary>
    public static float RowStep(MeterTheme t) => t.Row.H + t.Row.Gap;

    /// <summary>
    /// How tall the meter is for <paramref name="rows"/> rows (at most eight), with the limit break's
    /// row when it was used and the dashed rule when you're pinned: the head, the gap to the rows, the
    /// rows, the limit break, the foot, the padding. Ready Check with eight and the limit break: 238.
    /// </summary>
    public static float Needed(MeterTheme t, int rows, bool limitBreak, bool pinned = false)
    {
        rows = Math.Clamp(rows, 0, PlateRows.Max);
        return t.Win.HeadH + t.Win.HeadGap + rows * RowStep(t) + (pinned ? PinRule : 0) + (limitBreak ? LimitBreak(t) : 0) + Foot + t.Win.FootPad;
    }

    /// <summary>How many rows fit in a meter this tall (CSS px), the rest as <see cref="Needed"/> has it (at least one, at most eight).</summary>
    public static int RowsIn(MeterTheme t, float height, bool limitBreak)
    {
        var room = height - Needed(t, 0, limitBreak) + t.Row.Gap;
        var n = (int)MathF.Floor((room + 1) / RowStep(t));
        return Math.Clamp(n, 1, PlateRows.Max);
    }
}

/// <summary>
/// The game's job icons by job: the ClassJob row for an English abbreviation (what the meter's rows
/// carry), and its framed icon (<c>62100 + the row</c>, drawn with Dalamud's texture provider). 0 for
/// one that isn't known: the meter draws the role's mark in a socket instead.
/// </summary>
public static class JobIcons
{
    private static readonly string[] ByRow =
    {
        "ADV", "GLA", "PGL", "MRD", "LNC", "ARC", "CNJ", "THM", "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL", "MIN", "BTN", "FSH",
        "PLD", "MNK", "WAR", "DRG", "BRD", "WHM", "BLM", "ACN", "SMN", "SCH", "ROG", "NIN", "MCH", "DRK", "AST", "SAM", "RDM", "BLU", "GNB",
        "DNC", "RPR", "SGE", "VPR", "PCT",
    };

    /// <summary>The ClassJob row of an English abbreviation ("DRG" 22), 0 for none.</summary>
    public static uint ClassJob(string? abbreviation)
    {
        if (string.IsNullOrEmpty(abbreviation)) return 0;
        for (var i = 1; i < ByRow.Length; i++)
            if (string.Equals(ByRow[i], abbreviation, StringComparison.OrdinalIgnoreCase))
                return (uint)i;
        return 0;
    }

    /// <summary>The job's framed icon (62100 + its ClassJob row: DRG 62122), 0 for none.</summary>
    public static uint Icon(string? abbreviation) => ClassJob(abbreviation) is var row and > 0 ? 62100 + row : 0;
}

/// <summary>
/// The meter's look across versions (the configuration's <c>MeterLook</c>): 0 before 0.7.0, 1 since. 0.7.0's compact
/// meter (round four's Plate) is the default, and replaces the old compact: a configuration from before it lands on
/// compact (whichever it had), with the old compact's height let go (its rows and head were other sizes; and since
/// 0.7.0 the compact meter isn't sized by hand at all: <c>Configuration.Migrate</c> clears any size given it).
/// </summary>
public static class MeterLook
{
    public const int Current = 1;

    public static (int Version, bool Compact, float CompactHeight) Migrate(int version, bool compact, float compactHeight) =>
        version >= Current ? (version, compact, compactHeight) : (Current, true, 0);
}

/// <summary>The compact meter's small words, as each theme writes them (worked out once a snapshot).</summary>
public static class PlateWords
{
    /// <summary>The head's menu button: the tab's short word ("DPS", "HPS", "Taken", "Deaths", "Events").</summary>
    public static string TabWord(Core.Meter.MeterTab tab) => tab switch
    {
        Core.Meter.MeterTab.Healing => "HPS",
        Core.Meter.MeterTab.Taken => "Taken",
        Core.Meter.MeterTab.Deaths => "Deaths",
        Core.Meter.MeterTab.Events => "Events",
        _ => "DPS",
    };

    /// <summary>
    /// Where in the fight, four ways: the chip ("P2 · 0:41"), short ("P2"), Readout's ("P2+0:41") and Native's
    /// ("Phase 2"); all null with no phases.
    /// </summary>
    public static (string Chip, string Short, string Term, string Long)? Phase(string? phase, double into)
    {
        if (string.IsNullOrEmpty(phase)) return null;
        var clock = Format.Clock(Math.Max(0, into));
        var longWord = phase.Length > 1 && (phase[0] == 'P' || phase[0] == 'p') && int.TryParse(phase.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? "Phase " + n.ToString(CultureInfo.InvariantCulture)
            : phase;
        return (phase + " · " + clock, phase, phase + "+" + clock, longWord);
    }

    /// <summary>How long a member has been down ("0:20"): from their death to the fight's clock, or to when they were raised.</summary>
    public static string DeadFor(double diedAt, double now) => Format.Clock(Math.Max(0, now - diedAt));
}
