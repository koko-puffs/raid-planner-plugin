using System.Numerics;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>0.8.0's Arrange windows (UI/Model/ArrangeWindows.cs): who takes part, the places kept for Cancel, Done's lock, the samples it started, where things go.</summary>
public class ArrangeWindowsTests
{
    private static OverlaySettings Placed() => new()
    {
        Show = true,
        Locked = true,
        ClickThrough = true,
        Grows = MeterGrows.Up,
        Background = 0.45f,
        Width = 320,
        Anchored = true,
        AnchorX = 400,
        AnchorY = 900,
        AnchorFor = MeterGrows.Up,
        Compact = true,
    };

    [Fact]
    public void Only_the_windows_turned_on_take_part_in_order()
    {
        Assert.Equal(new[] { ArrangePart.Meter, ArrangePart.Plan, ArrangePart.Callouts, ArrangePart.Cooldowns }, ArrangeParts.Taking(true, true, true, true));
        Assert.Equal(new[] { ArrangePart.Callouts }, ArrangeParts.Taking(false, false, true, false));
        Assert.Equal(new[] { ArrangePart.Meter, ArrangePart.Cooldowns }, ArrangeParts.Taking(true, false, false, true));
        Assert.Empty(ArrangeParts.Taking(false, false, false, false));
    }

    [Fact]
    public void Each_has_its_label_and_only_the_plan_window_has_no_options()
    {
        Assert.Equal("Meter", ArrangeParts.Label(ArrangePart.Meter));
        Assert.Equal("Plan", ArrangeParts.Label(ArrangePart.Plan));
        Assert.Equal("Callouts", ArrangeParts.Label(ArrangePart.Callouts));
        Assert.Equal("Party cooldowns", ArrangeParts.Label(ArrangePart.Cooldowns));
        Assert.False(ArrangeParts.HasOptions(ArrangePart.Plan));
        Assert.True(ArrangeParts.HasOptions(ArrangePart.Meter));
        Assert.True(ArrangeParts.HasOptions(ArrangePart.Callouts));
        Assert.True(ArrangeParts.HasOptions(ArrangePart.Cooldowns));
    }

    [Fact]
    public void Cancel_puts_an_overlays_place_size_lock_and_options_back()
    {
        var s = Placed();
        var was = WindowPlace.Of(s);
        // Moved, sized, its options changed while arranging; then locked by hand.
        s.AnchorX = 10;
        s.AnchorY = 20;
        s.AnchorFor = MeterGrows.Down;
        s.Grows = MeterGrows.Down;
        s.Width = 500;
        s.Background = 1;
        s.ClickThrough = false;
        s.Locked = false;
        s.Anchored = true;
        was.PutBack(s);
        Assert.Equal(WindowPlace.Of(Placed()), WindowPlace.Of(s));
        // What isn't the mode's stays as it is.
        Assert.True(s.Show);
        Assert.True(s.Compact);
    }

    [Fact]
    public void Cancel_puts_back_a_window_never_placed_as_never_placed()
    {
        var s = new OverlaySettings { Show = true };
        var was = WindowPlace.Of(s);
        s.Anchored = true;
        s.AnchorX = 300;
        s.Width = 280;
        was.PutBack(s);
        Assert.False(s.Anchored);
        Assert.Equal(0, s.Width);
        Assert.Equal(0, s.AnchorX);
    }

    [Fact]
    public void The_plan_windows_place_is_known_once_drawn()
    {
        var none = WindowPlace.OfPlan(true, null, null);
        Assert.False(none.Anchored);
        Assert.True(none.Locked);
        var drawn = WindowPlace.OfPlan(false, new Vector2(60, 70), new Vector2(340, 520));
        Assert.True(drawn.Anchored);
        Assert.Equal((60f, 70f, 340f, 520f), (drawn.AnchorX, drawn.AnchorY, drawn.Width, drawn.Height));
    }

    [Fact]
    public void A_session_keeps_each_place_picks_the_first_and_ends_with_the_samples_it_started()
    {
        var a = new ArrangeSession();
        Assert.False(a.On);
        Assert.False(a.Takes(ArrangePart.Meter));
        var meter = new WindowPlace(false, true, MeterGrows.Up, 0.88f, 330, 300, true, 50, 800, MeterGrows.Up);
        var callouts = WindowPlace.Of(Placed());
        // Given out of order: kept in the mode's order.
        Assert.True(a.Start([(ArrangePart.Callouts, callouts), (ArrangePart.Meter, meter)]));
        Assert.True(a.On);
        Assert.Equal(new[] { ArrangePart.Meter, ArrangePart.Callouts }, a.Parts);
        Assert.Equal(ArrangePart.Meter, a.Picked);
        Assert.Equal(meter, a.Before(ArrangePart.Meter));
        Assert.Equal(callouts, a.Before(ArrangePart.Callouts));
        Assert.Null(a.Before(ArrangePart.Cooldowns));
        Assert.True(a.Takes(ArrangePart.Callouts));
        Assert.False(a.Takes(ArrangePart.Plan));

        // On already: a second start changes nothing.
        Assert.False(a.Start([(ArrangePart.Cooldowns, callouts)]));
        Assert.Equal(new[] { ArrangePart.Meter, ArrangePart.Callouts }, a.Parts);

        // Picking: one taking part, not one that isn't.
        a.Pick(ArrangePart.Callouts);
        Assert.Equal(ArrangePart.Callouts, a.Picked);
        a.Pick(ArrangePart.Plan);
        Assert.Equal(ArrangePart.Callouts, a.Picked);

        a.StartedSample(ArrangePart.Callouts);
        a.StartedSample(ArrangePart.Plan);
        Assert.True(a.StartedSampleOf(ArrangePart.Callouts));
        Assert.False(a.StartedSampleOf(ArrangePart.Meter));
        Assert.False(a.StartedSampleOf(ArrangePart.Plan));

        Assert.Equal(new[] { ArrangePart.Callouts }, a.End());
        Assert.False(a.On);
        Assert.Null(a.Picked);
        Assert.Empty(a.Parts);
        Assert.Null(a.Before(ArrangePart.Meter));
        Assert.False(a.StartedSampleOf(ArrangePart.Callouts));
        Assert.Empty(a.End());
    }

    [Fact]
    public void A_session_with_nothing_on_has_nothing_picked()
    {
        var a = new ArrangeSession();
        Assert.True(a.Start([]));
        Assert.True(a.On);
        Assert.Null(a.Picked);
        Assert.Empty(a.Parts);
    }

    [Fact]
    public void A_place_learnt_later_replaces_the_one_unknown_at_the_start()
    {
        var a = new ArrangeSession();
        a.Start([(ArrangePart.Plan, WindowPlace.OfPlan(true, null, null))]);
        var drawn = WindowPlace.OfPlan(true, new Vector2(60, 60), new Vector2(340, 520));
        a.Learnt(ArrangePart.Plan, drawn);
        Assert.Equal(drawn, a.Before(ArrangePart.Plan));
        // Not for one that isn't taking part.
        a.Learnt(ArrangePart.Meter, drawn);
        Assert.Null(a.Before(ArrangePart.Meter));
    }

    [Fact]
    public void First_places_toolbar_and_panel_sit_on_the_screen()
    {
        var min = new Vector2(0, 0);
        var max = new Vector2(1920, 1080);
        // The meter at ImGui's first place, its anchor the corner its Grows keeps.
        Assert.Equal(new Vector2(60, 60), ArrangeLayout.MeterFirst(min, new Vector2(330, 400), MeterGrows.Down));
        Assert.Equal(new Vector2(60, 460), ArrangeLayout.MeterFirst(min, new Vector2(330, 400), MeterGrows.Up));
        // The plan window at the right, centred down the screen; kept on a small one.
        Assert.Equal(new Vector2(1920 - 60 - 340, (1080 - 520) / 2f), ArrangeLayout.PlanFirst(min, max, new Vector2(340, 520)));
        var small = ArrangeLayout.PlanFirst(min, new Vector2(300, 400), new Vector2(340, 520));
        Assert.Equal(new Vector2(0, 0), small);
        // The toolbar centred, its foot 28 above the bottom; the panel 24 in from the top-right corner.
        Assert.Equal(new Vector2((1920 - 800) / 2f, 1080 - 28 - 60), ArrangeLayout.Toolbar(min, max, new Vector2(800, 60), 28));
        Assert.Equal(new Vector2(1920 - 24 - 250, 24), ArrangeLayout.Panel(min, max, 250, 24));
        // On a second screen's offset.
        Assert.Equal(new Vector2(100 + 1920 - 24 - 250, 50 + 24), ArrangeLayout.Panel(new Vector2(100, 50), new Vector2(2020, 1130), 250, 24));
    }
}
