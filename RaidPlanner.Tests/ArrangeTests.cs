using System;
using System.Collections.Generic;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>0.7.4's meter layout: Grows, Head and Order apart (where the head goes, the rows' order, compact's pinned you), the normal meter fitted to what it shows, and an older Grows moved.</summary>
public class ArrangeTests
{
    private static MeterArrange A(MeterGrows g, MeterHead h, MeterOrder o) => new(g, h, o);

    /// <summary>The rows (0 the top player) top to bottom down the window.</summary>
    private static int[] Down(MeterArrange a, int n)
    {
        var r = new int[n];
        for (var d = 0; d < n; d++) r[d] = a.ReadingDown(d, n);
        return r;
    }

    /// <summary>The rows (0 the top player) by their place from the head's side.</summary>
    private static int[] ByPlace(MeterArrange a, int n)
    {
        var r = new int[n];
        for (var k = 0; k < n; k++) r[a.FromHead(k, n)] = k;
        return r;
    }

    [Fact]
    public void Order_reads_down_the_window_whichever_edge_the_head_is_at()
    {
        foreach (var g in new[] { MeterGrows.Down, MeterGrows.Up })
        foreach (var h in new[] { MeterHead.Top, MeterHead.Bottom })
        {
            Assert.Equal(new[] { 0, 1, 2, 3 }, Down(A(g, h, MeterOrder.HighestFirst), 4));
            Assert.Equal(new[] { 3, 2, 1, 0 }, Down(A(g, h, MeterOrder.LowestFirst), 4));
        }
    }

    [Fact]
    public void Each_row_has_its_place_from_the_head()
    {
        // The head on top, highest first: the top player next to the head, the rest away from it.
        var a = A(MeterGrows.Up, MeterHead.Top, MeterOrder.HighestFirst);
        Assert.False(a.HeadAtBottom);
        Assert.True(a.TopNearHead);
        Assert.Equal(new[] { 0, 1, 2 }, ByPlace(a, 3));
        // The head at the bottom, highest first: the top player at the top of the list, so furthest from the head.
        var b = A(MeterGrows.Down, MeterHead.Bottom, MeterOrder.HighestFirst);
        Assert.True(b.HeadAtBottom);
        Assert.False(b.TopNearHead);
        Assert.Equal(new[] { 2, 1, 0 }, ByPlace(b, 3));
        // The head at the bottom, lowest first (an older Grows Up): the top player next to the head.
        Assert.True(A(MeterGrows.Up, MeterHead.Bottom, MeterOrder.LowestFirst).TopNearHead);
        // The head on top, lowest first: the top player at the bottom, away from it.
        Assert.False(A(MeterGrows.Down, MeterHead.Top, MeterOrder.LowestFirst).TopNearHead);
        // Which way it grows doesn't move anything inside it.
        foreach (var h in new[] { MeterHead.Top, MeterHead.Bottom })
        foreach (var o in new[] { MeterOrder.HighestFirst, MeterOrder.LowestFirst })
            Assert.Equal(ByPlace(A(MeterGrows.Down, h, o), 5), ByPlace(A(MeterGrows.Up, h, o), 5));
    }

    [Fact]
    public void Rows_in_the_window_from_the_bands()
    {
        // A meter 100 to 300: a 36 head, 27 tabs, the 28 foot at the other edge, 25px rows, three of them.
        foreach (var h in new[] { MeterHead.Top, MeterHead.Bottom })
        foreach (var o in new[] { MeterOrder.HighestFirst, MeterOrder.LowestFirst })
        {
            var a = A(MeterGrows.Down, h, o);
            var s = new MeterStack(100, 300, a.HeadAtBottom);
            var headY = s.Near(36);
            s.Near(27);
            var footY = s.Far(28);
            Assert.Equal(h == MeterHead.Top ? 100 : 264, headY);
            Assert.Equal(h == MeterHead.Top ? 272 : 100, footY);
            var ys = new float[3];
            for (var k = 0; k < 3; k++) ys[k] = s.Row(a.FromHead(k, 3), 25);
            // The top player's row is the top one with highest first, the bottom one with lowest first.
            var topFirst = ys[0] < ys[1] && ys[1] < ys[2];
            Assert.Equal(o == MeterOrder.HighestFirst, topFirst);
            // The rows sit against the head's bands: under the tabs (163 on), or over them (to 237).
            Assert.Equal(h == MeterHead.Top ? 163 : 162, Math.Min(ys[0], ys[2]));
        }
    }

    [Fact]
    public void Compact_keeps_you_pinned_at_the_end_away_from_the_top_player()
    {
        // Eight shown, you ranked ninth: the last slot is you.
        var slots = new List<PlateSlot>();
        PlateRows.Select(12, 8, 8, slots);
        Assert.True(slots[^1].Pinned);
        var n = slots.Count;
        // Highest first under the head (or lowest first over it at the bottom): you furthest from the head, the rule before you.
        var near = A(MeterGrows.Down, MeterHead.Top, MeterOrder.HighestFirst);
        Assert.Equal(7, near.FromHead(n - 1, n));
        Assert.Equal(7, near.PinBoundary(n));
        Assert.Equal(7, A(MeterGrows.Up, MeterHead.Bottom, MeterOrder.LowestFirst).PinBoundary(n));
        // The top player furthest from the head: you next to it, the rule after you, the rest past it.
        var far = A(MeterGrows.Up, MeterHead.Top, MeterOrder.LowestFirst);
        Assert.Equal(0, far.FromHead(n - 1, n));
        Assert.Equal(1, far.PinBoundary(n));
        // Reading down: the pinned you is the bottom row with highest first and the top row with lowest first, whatever the head.
        foreach (var h in new[] { MeterHead.Top, MeterHead.Bottom })
        {
            Assert.Equal(n - 1, A(MeterGrows.Down, h, MeterOrder.HighestFirst).ReadingDown(n - 1, n));
            Assert.Equal(n - 1, A(MeterGrows.Down, h, MeterOrder.LowestFirst).ReadingDown(0, n));
        }
        // Only you shown: the rule before you either way.
        Assert.Equal(0, far.PinBoundary(1));
        Assert.Equal(0, near.PinBoundary(1));
    }

    [Fact]
    public void The_normal_meter_is_as_tall_as_what_it_shows()
    {
        var z = MeterSizes.Normal;
        // One player, no pace yet, no limit break: the head, the tabs, the space under them, the row and the foot. No empty band (it was 189).
        Assert.Equal(36 + 27 + 6 + 25 + 28, MeterFit.Fight(z, 1, false, false));
        // Pace's row and the limit break's only when they show.
        Assert.Equal(122 + 26, MeterFit.Fight(z, 1, true, false));
        Assert.Equal(122 + 27, MeterFit.Fight(z, 1, false, true));
        // Eight with both: 350 (the mock-up's 364 less the columns' heads, gone in 0.8.2).
        Assert.Equal(350, MeterFit.Fight(z, 8, true, true));
        // Waiting: the party with dashes, no pace or limit break.
        Assert.Equal(36 + 27 + 6 + 8 * 25 + 28, MeterFit.Waiting(z, 8));
        // A row comes, the meter grows by it; one goes, it shrinks.
        Assert.Equal(z.Row, MeterFit.Fight(z, 5, false, true) - MeterFit.Fight(z, 4, false, true));
        // The rows it fits again are what it shows.
        for (var n = 0; n <= 8; n++) Assert.Equal(n, z.RowsIn(MeterFit.Fight(z, n, false, false), false, false));
        // A theme's taller rows.
        var mochi = MeterSizes.For(MeterThemes.Of(MeterThemeId.Mochi));
        Assert.Equal(36 + 27 + 6 + 3 * 29 + 28, MeterFit.Fight(mochi, 3, false, false));
    }

    [Fact]
    public void Deaths_and_events_fit_their_lines_up_to_the_partys_room()
    {
        var z = MeterSizes.Normal;
        // Kept fight, two deaths of 44: the head, the tabs, the two and the foot.
        Assert.Equal(36 + 27 + 2 * 44 + 28, MeterFit.Deaths(z, 2, 44, false, 8));
        // None: the foot and "Nobody died".
        Assert.Equal(36 + 27 + 28 + MeterFit.Message, MeterFit.Deaths(z, 0, 44, false, 8));
        // Live: the alive line in the foot's place.
        Assert.Equal(36 + 27 + 44 + MeterFit.Alive, MeterFit.Deaths(z, 1, 44, true, 8));
        // Many: no taller than the party's fight (eight and the limit break, 324).
        Assert.Equal(MeterFit.ListCap(z, 8), MeterFit.Deaths(z, 20, 44, false, 8));
        Assert.Equal(324, MeterFit.ListCap(z, 8));
        // Events: a 23px row each, the foot; none, its line; many, at most the party's.
        Assert.Equal(36 + 27 + 3 * 23 + 28, MeterFit.Events(z, 3, 8));
        Assert.Equal(36 + 27 + 28 + MeterFit.Message, MeterFit.Events(z, 0, 8));
        Assert.Equal(324, MeterFit.Events(z, 64, 8));
        // A solo meter's list still has room for its empty line.
        Assert.Equal(36 + 27 + 28 + MeterFit.Message, MeterFit.Events(z, 0, 1));
    }

    [Fact]
    public void Your_height_is_the_most_it_grows_to()
    {
        var z = MeterSizes.Normal;
        // Sized to 250 (the title bar's 26 in it): one player is 122 + 26, under it; eight are cut to 250.
        Assert.Equal(122 + 26, MeterLayout.FightHeight(MeterFit.Fight(z, 1, false, false), 26, 250, 60, 1400));
        Assert.Equal(250, MeterLayout.FightHeight(MeterFit.Fight(z, 8, true, true), 26, 250, 60, 1400));
        // Never sized: what it shows.
        Assert.Equal(122 + 26, MeterLayout.FightHeight(MeterFit.Fight(z, 1, false, false), 26, 0, 60, 1400));
    }

    [Fact]
    public void Growing_up_or_down_keeps_its_edge_as_the_fitted_height_changes()
    {
        var min = System.Numerics.Vector2.Zero;
        var max = new System.Numerics.Vector2(2560, 1440);
        var z = MeterSizes.Normal;
        var one = MeterFit.Fight(z, 1, false, false);
        var eight = MeterFit.Fight(z, 8, true, true);
        var anchorUp = new System.Numerics.Vector2(100, 1200);
        // Growing up the bottom stays at 1200; the top moves.
        Assert.Equal(1200, MeterLayout.Place(anchorUp, new(330, one), MeterGrows.Up, min, max).Y + one);
        Assert.Equal(1200, MeterLayout.Place(anchorUp, new(330, eight), MeterGrows.Up, min, max).Y + eight);
        // Growing down the top stays.
        var anchorDown = new System.Numerics.Vector2(100, 200);
        Assert.Equal(200, MeterLayout.Place(anchorDown, new(330, one), MeterGrows.Down, min, max).Y);
        Assert.Equal(200, MeterLayout.Place(anchorDown, new(330, eight), MeterGrows.Down, min, max).Y);
    }

    [Fact]
    public void An_older_grows_keeps_its_look_once()
    {
        // Before 0.7.4, growing up put the head at the bottom and read the rows up from it: lowest first, reading down.
        Assert.Equal((1, MeterHead.Bottom, MeterOrder.LowestFirst), MeterArrange.Migrate(0, MeterGrows.Up, MeterHead.Top, MeterOrder.HighestFirst));
        Assert.Equal((1, MeterHead.Top, MeterOrder.HighestFirst), MeterArrange.Migrate(0, MeterGrows.Down, MeterHead.Top, MeterOrder.HighestFirst));
        // Once moved, they're the person's: not moved again.
        Assert.Equal((1, MeterHead.Top, MeterOrder.HighestFirst), MeterArrange.Migrate(1, MeterGrows.Up, MeterHead.Top, MeterOrder.HighestFirst));
        Assert.Equal((1, MeterHead.Bottom, MeterOrder.HighestFirst), MeterArrange.Migrate(1, MeterGrows.Down, MeterHead.Bottom, MeterOrder.HighestFirst));
        // The old Up is the same layout as before: the top player next to the head at the bottom, the foot at the top.
        var was = MeterArrange.Of(MeterGrows.Up);
        Assert.Equal(MeterGrows.Up, was.Grows);
        Assert.True(was.HeadAtBottom);
        Assert.True(was.TopNearHead);
        Assert.Equal(MeterArrange.Default, MeterArrange.Of(MeterGrows.Down));
    }

    [Fact]
    public void Its_height_is_held_while_the_mouse_is_on_it()
    {
        var z = MeterSizes.Normal;
        var damage = MeterFit.Fight(z, 8, false, true);
        var deaths = MeterFit.Deaths(z, 0, 44, false, 8);
        Assert.NotEqual(damage, deaths);
        // The mouse comes on Damage: nothing held yet, so the fitted height (then held).
        var held = MeterLayout.Hold(damage, 0, true);
        Assert.Equal(damage, held);
        // Deaths picked with the mouse still on it: the height stays (Grows Up with the head on top: the tabs don't move).
        Assert.Equal(damage, MeterLayout.Hold(deaths, held, true));
        // A row come mid-fight waits too, and the rows past the held height are cut (as under a height you gave it).
        var nine = MeterFit.Fight(z, 9, false, true);
        Assert.Equal(damage, MeterLayout.Hold(nine, held, true));
        Assert.Equal(8, z.RowsIn(MeterLayout.Hold(nine, held, true), false, true));
        // The mouse off it: it settles to what it shows.
        Assert.Equal(deaths, MeterLayout.Hold(deaths, held, false));
        // Nothing sensible held: fitted.
        Assert.Equal(deaths, MeterLayout.Hold(deaths, float.NaN, true));
        Assert.Equal(deaths, MeterLayout.Hold(deaths, -1, true));
    }
}

public class SmoothReplaysTests
{
    [Fact]
    public void TurnedOnOnceForOlderSettingsAndKeptAfter()
    {
        // Saved before 0.7.5 with it off: turned on once.
        Assert.Equal((1, true), RaidPlanner.Core.SmoothDefault.Migrate(0, false));
        // Turned off after that: it stays off; on stays on.
        Assert.Equal((1, false), RaidPlanner.Core.SmoothDefault.Migrate(1, false));
        Assert.Equal((1, true), RaidPlanner.Core.SmoothDefault.Migrate(1, true));
    }
}
