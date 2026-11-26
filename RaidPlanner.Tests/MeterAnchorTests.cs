using System.Numerics;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// The meter's place over many frames (UI/Model/MeterAnchor.cs): placed from its anchor before ImGui's Begin, the anchor
/// tracked inside the window, as MeterWindow runs them. A locked meter goes back exactly where it was left.
/// </summary>
public class MeterAnchorTests
{
    private static readonly Vector2 Min = Vector2.Zero;
    private static readonly Vector2 Max = new(2560, 1440);

    /// <summary>The title bar's height unlocked (screen px), and the arranging head's (Arrange windows).</summary>
    private const float Title = 22;

    private const float Head = 26;

    /// <summary>
    /// One frame of the meter's window: placed from its anchor (or, held or not placed, ImGui leaves it where it was, or
    /// where your drag took it), then tracked inside the window. Returns the anchor after it and where the window was.
    /// </summary>
    private static (MeterAnchor Anchor, Vector2 Pos) Frame(MeterAnchor a, MeterGrows grows, Vector2 was, float content, float chrome,
        Vector2? dragTo = null, Vector2? screenMax = null)
    {
        var size = new Vector2(330, content + chrome);
        var held = dragTo != null;
        var pos = a.Place(grows, size, chrome, Min, screenMax ?? Max, false, held) ?? dragTo ?? was;
        var (after, _) = a.Track(grows, pos, size, chrome, held && pos != was);
        return (after, pos);
    }

    /// <summary>Where its content's top is for a window at <paramref name="pos"/> under this chrome.</summary>
    private static float ContentTop(Vector2 pos, float chrome) => pos.Y + chrome;

    [Fact]
    public void Locking_it_keeps_what_it_shows_where_you_put_it()
    {
        // Unlocked, growing down: you drag it by its title bar so its head is at 600 (the title bar over it, 578 to 600).
        var a = new MeterAnchor(new Vector2(100, 300), true, MeterGrows.Down, MeterAnchor.Current);
        (a, var pos) = Frame(a, MeterGrows.Down, new Vector2(100, 278), 300, Title, dragTo: new Vector2(100, 578));
        Assert.Equal(600, ContentTop(pos, Title));
        // Let go: placed from the anchor, as it was.
        (a, pos) = Frame(a, MeterGrows.Down, pos, 300, Title);
        Assert.Equal(600, ContentTop(pos, Title));
        // Locked: no title bar. Its head stays at 600 (it was the title bar's top, 578: lifted by the title bar's height).
        (a, pos) = Frame(a, MeterGrows.Down, pos, 300, 0);
        Assert.Equal(new Vector2(100, 600), pos);
        Assert.Equal(new Vector2(100, 600), a.At);
        // Unlocked again: the title bar goes over it, the head stays.
        (_, pos) = Frame(a, MeterGrows.Down, pos, 300, Title);
        Assert.Equal(new Vector2(100, 578), pos);
        Assert.Equal(600, ContentTop(pos, Title));
    }

    [Fact]
    public void Arrange_windows_done_keeps_it_where_you_left_it()
    {
        // Arranging: the arranging head over it, dragged so the meter's own head is at 500.
        var a = new MeterAnchor(new Vector2(60, 60), true, MeterGrows.Down, MeterAnchor.Current);
        (a, var pos) = Frame(a, MeterGrows.Down, new Vector2(60, 60), 350, Head, dragTo: new Vector2(900, 474));
        (a, pos) = Frame(a, MeterGrows.Down, pos, 350, Head);
        Assert.Equal(500, ContentTop(pos, Head));
        // Done locks it: the arranging head goes, the meter stays (it went up by the head's 26 before).
        for (var i = 0; i < 3; i++) (a, pos) = Frame(a, MeterGrows.Down, pos, 350, 0);
        Assert.Equal(new Vector2(900, 500), pos);
    }

    [Fact]
    public void Growing_up_its_foot_is_its_contents_whatever_its_chrome()
    {
        var a = new MeterAnchor(new Vector2(100, 1300), true, MeterGrows.Up, MeterAnchor.Current);
        foreach (var chrome in new[] { 0f, Title, Head })
        {
            var (after, pos) = Frame(a, MeterGrows.Up, Vector2.Zero, 350, chrome);
            Assert.Equal(1300, pos.Y + 350 + chrome);
            Assert.Equal(a, after);
        }
    }

    [Fact]
    public void Taller_at_login_near_the_foot_it_is_lifted_onto_the_screen_but_its_anchor_stays()
    {
        // Locked, growing down, its head at 1300: folded (36) when you left; waiting with the party (350) as you come back.
        var a = new MeterAnchor(new Vector2(100, 1300), true, MeterGrows.Down, MeterAnchor.Current);
        var (after, pos) = Frame(a, MeterGrows.Down, Vector2.Zero, 36, 0);
        Assert.Equal(1300, pos.Y);
        for (var i = 0; i < 100; i++)
        {
            (after, pos) = Frame(after, MeterGrows.Down, pos, 350, 0);
            Assert.Equal(1440 - 350, pos.Y);
        }
        Assert.Equal(a, after);
        // Short again: exactly where you left it.
        (_, pos) = Frame(after, MeterGrows.Down, pos, 36, 0);
        Assert.Equal(1300, pos.Y);
    }

    [Fact]
    public void A_smaller_screen_for_a_while_at_login_doesnt_move_its_anchor()
    {
        var small = new Vector2(1920, 1080);
        foreach (var grows in new[] { MeterGrows.Up, MeterGrows.Down })
        {
            var a = new MeterAnchor(new Vector2(2000, grows == MeterGrows.Up ? 1400 : 1200), true, grows, MeterAnchor.Current);
            var after = a;
            Vector2 pos = default;
            // The game's window not at its size yet (or a smaller resolution for a while): kept on that screen.
            for (var i = 0; i < 5; i++)
            {
                (after, pos) = Frame(after, grows, pos, 200, 0, screenMax: small);
                Assert.True(pos.X + 330 <= small.X && pos.Y + 200 <= small.Y);
            }
            Assert.Equal(a, after);
            // Its size again: back exactly.
            (after, pos) = Frame(after, grows, pos, 200, 0);
            Assert.Equal(new Vector2(2000, 1200), pos);
            Assert.Equal(a, after);
        }
    }

    [Fact]
    public void Its_size_moving_with_the_scale_keeps_its_edge()
    {
        var up = new MeterAnchor(new Vector2(100, 1300), true, MeterGrows.Up, MeterAnchor.Current);
        var down = new MeterAnchor(new Vector2(100, 200), true, MeterGrows.Down, MeterAnchor.Current);
        foreach (var scale in new[] { 1f, 1.25f, 1.5f })
        {
            var (ua, upos) = Frame(up, MeterGrows.Up, Vector2.Zero, 300 * scale, 0);
            Assert.Equal(1300, upos.Y + 300 * scale, 3);
            Assert.Equal(up, ua);
            var (da, dpos) = Frame(down, MeterGrows.Down, Vector2.Zero, 300 * scale, Title * scale);
            Assert.Equal(200, ContentTop(dpos, Title * scale), 3);
            Assert.Equal(down, da);
        }
    }

    [Fact]
    public void Not_placed_when_held_never_anchored_or_for_the_other_grows()
    {
        var size = new Vector2(330, 300);
        var a = new MeterAnchor(new Vector2(100, 200), true, MeterGrows.Down, MeterAnchor.Current);
        Assert.NotNull(a.Place(MeterGrows.Down, size, 0, Min, Max, false, false));
        Assert.Null(a.Place(MeterGrows.Down, size, 0, Min, Max, false, true));
        Assert.Null(a.Place(MeterGrows.Up, size, 0, Min, Max, false, false));
        Assert.Null((a with { Anchored = false }).Place(MeterGrows.Down, size, 0, Min, Max, false, false));
        Assert.Null(a.Place(MeterGrows.Down, new Vector2(330, float.NaN), 0, Min, Max, false, false));
    }

    [Fact]
    public void Read_the_first_time_and_turned_as_grows_changes_from_where_it_is()
    {
        var never = new MeterAnchor(default, false, MeterGrows.Down, 0);
        // ImGui's first place (60, 60), the title bar over it: its content's corner.
        var (first, change) = never.Track(MeterGrows.Down, new Vector2(60, 60), new Vector2(330, 322), Title, false);
        Assert.Equal(AnchorChange.First, change);
        Assert.Equal(new MeterAnchor(new Vector2(60, 60 + Title), true, MeterGrows.Down, MeterAnchor.Current), first);
        // Grows changed to up: its foot where it is, so it doesn't move.
        var (turned, change2) = first.Track(MeterGrows.Up, new Vector2(60, 60), new Vector2(330, 322), Title, false);
        Assert.Equal(AnchorChange.Turned, change2);
        Assert.Equal(new Vector2(60, 382), turned.At);
        Assert.Equal(MeterGrows.Up, turned.For);
        // Locked and still: nothing, however it's been placed.
        var (same, none) = turned.Track(MeterGrows.Up, new Vector2(5, 5), new Vector2(330, 999), 0, false);
        Assert.Equal(AnchorChange.None, none);
        Assert.Equal(turned, same);
    }

    [Fact]
    public void An_anchor_saved_before_is_read_once_so_it_stays_where_it_is()
    {
        // Saved as the window's top-left: unlocked, the title bar's top at 578, its head under it at 600.
        var old = new MeterAnchor(new Vector2(100, 578), true, MeterGrows.Down, 0);
        var now = old.Migrated(Title);
        Assert.Equal(new MeterAnchor(new Vector2(100, 600), true, MeterGrows.Down, MeterAnchor.Current), now);
        var (_, pos) = Frame(now, MeterGrows.Down, Vector2.Zero, 300, Title);
        Assert.Equal(new Vector2(100, 578), pos);
        // Locked, it's shown at its anchor already: as it is.
        Assert.Equal(old with { Version = MeterAnchor.Current }, old.Migrated(0));
        // Growing up, or never placed: as it was.
        var up = new MeterAnchor(new Vector2(100, 1300), true, MeterGrows.Up, 0);
        Assert.Equal(up with { Version = MeterAnchor.Current }, up.Migrated(Title));
        var never = new MeterAnchor(default, false, MeterGrows.Down, 0);
        Assert.Equal(never with { Version = MeterAnchor.Current }, never.Migrated(Title));
        // Once only.
        Assert.Equal(now, now.Migrated(Title));
    }

    [Fact]
    public void Reset_places_puts_the_window_at_imguis_first_place_its_chrome_and_all()
    {
        var size = new Vector2(330, 400 + Head);
        var at = ArrangeLayout.MeterFirst(Min, size, MeterGrows.Down, Head);
        Assert.Equal(new Vector2(60, 60), MeterLayout.Place(at, size, MeterGrows.Down, Min, Max, false, Head));
        var up = ArrangeLayout.MeterFirst(Min, size, MeterGrows.Up, Head);
        Assert.Equal(new Vector2(60, 60), MeterLayout.Place(up, size, MeterGrows.Up, Min, Max, false, Head));
    }
}
