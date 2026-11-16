using System.Numerics;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>0.7.0's compact meter (round four's Plate) and its themes: the numbers, the rows shown, the ranks' hold, the sizes, the tokens, the job icons, the settings moved.</summary>
public class SevenTests
{
    [Theory]
    [InlineData(27840, "27.8k")]
    [InlineData(26910, "26.9k")]
    [InlineData(23480, "23.5k")]
    [InlineData(12460, "12.5k")]
    [InlineData(9050, "9.05k")]
    [InlineData(7000, "7.00k")]
    [InlineData(165000, "165k")]
    [InlineData(999, "999")]
    [InlineData(0, "0")]
    [InlineData(999.6, "1.00k")]
    [InlineData(9996, "10.0k")]
    [InlineData(99960, "100k")]
    [InlineData(999_600, "1.00M")]
    [InlineData(5_317_440, "5.32M")]
    [InlineData(13_103_000, "13.1M")]
    [InlineData(131_000_000, "131M")]
    [InlineData(1_234_567_890, "1.23B")]
    public void Numbers_are_three_figures(double v, string text) => Assert.Equal(text, MeterNumber.Short(v));

    [Fact]
    public void Numbers_whole_as_a_setting_and_never_more_than_three_figures()
    {
        Assert.Equal("27,840", MeterNumber.Text(27840, whole: true));
        Assert.Equal("27.8k", MeterNumber.Text(27840, whole: false));
        Assert.Equal("0", MeterNumber.Short(double.NaN));
        Assert.Equal("−1.20k", MeterNumber.Short(-1200));
        // Every number from 1 to 10 billion (on a log sweep) in three figures: at most five characters, the column fixed.
        for (var v = 1.0; v < 1e10; v *= 1.013)
        {
            var s = MeterNumber.Short(v);
            Assert.True(s.Length <= 5, $"{v} → {s}");
            if (v >= 99.5) Assert.Equal(3, s.Count(char.IsDigit));
        }
    }

    [Fact]
    public void The_top_eight_and_you_pinned_when_below_them()
    {
        var into = new List<PlateSlot>();
        // Eight, you sixth: all eight, nothing pinned.
        PlateRows.Select(8, 5, 8, into);
        Assert.Equal(Enumerable.Range(0, 8), into.Select(s => s.Index));
        Assert.DoesNotContain(into, s => s.Pinned);
        // Cut to five rows, you sixth: the top four, then you pinned with your real rank.
        PlateRows.Select(8, 5, 5, into);
        Assert.Equal(new[] { 0, 1, 2, 3, 5 }, into.Select(s => s.Index));
        Assert.Equal(new[] { 1, 2, 3, 4, 6 }, into.Select(s => s.Rank));
        Assert.True(into[^1].Pinned);
        // Twelve (more than a party), you tenth: never more than eight rows; the eighth is you, #10.
        PlateRows.Select(12, 9, 20, into);
        Assert.Equal(8, into.Count);
        Assert.Equal(new PlateSlot(9, 10, true), into[^1]);
        Assert.Equal(7, into.Count(s => !s.Pinned));
        // You eighth of twelve: in the top eight, nothing pinned.
        PlateRows.Select(12, 7, 8, into);
        Assert.Equal(Enumerable.Range(0, 8), into.Select(s => s.Index));
        // Not in the party (a sample, a kept fight of another's): the top ones.
        PlateRows.Select(4, -1, 8, into);
        Assert.Equal(4, into.Count);
        PlateRows.Select(4, 2, 0, into);
        Assert.Empty(into);
        // One row of room and you below it: you alone, with your rank.
        PlateRows.Select(8, 6, 1, into);
        Assert.Equal(new[] { new PlateSlot(6, 7, true) }, into);
    }

    [Fact]
    public void Ranks_swap_only_once_a_lead_has_held_a_second()
    {
        var hold = new RankHold();
        int[] ids = { 1, 2, 3 };
        // The first snapshot is the true order.
        Assert.Equal(new[] { 1, 2, 3 }, hold.Update(ids, new double[] { 300, 200, 100 }, 0));
        // 2 passes 1: not yet.
        Assert.Equal(new[] { 1, 2, 3 }, hold.Update(new[] { 2, 1, 3 }, new double[] { 310, 305, 100 }, 0.25));
        Assert.Equal(new[] { 1, 2, 3 }, hold.Update(new[] { 2, 1, 3 }, new double[] { 320, 310, 100 }, 0.75));
        // Held a second: they swap.
        Assert.Equal(new[] { 2, 1, 3 }, hold.Update(new[] { 2, 1, 3 }, new double[] { 330, 315, 100 }, 1.25));
        // Back and forth a few hundred apart, four times a second: no flicker.
        for (var i = 0; i < 8; i++)
        {
            var t = 1.5 + i * 0.25;
            var flip = i % 2 == 0;
            var order = hold.Update(flip ? new[] { 1, 2, 3 } : new[] { 2, 1, 3 }, flip ? new double[] { 340, 339, 100 } : new double[] { 341, 340, 100 }, t);
            Assert.Equal(new[] { 2, 1, 3 }, order);
        }
        // 3 races past both and holds: it moves past both at once.
        hold.Update(new[] { 3, 2, 1 }, new double[] { 500, 341, 340 }, 4);
        Assert.Equal(new[] { 3, 2, 1 }, hold.Update(new[] { 3, 2, 1 }, new double[] { 510, 342, 341 }, 5.1));
        // A member joining goes in at their place at once; one leaving is dropped.
        Assert.Equal(new[] { 3, 9, 2 }, hold.Update(new[] { 3, 9, 2 }, new double[] { 510, 400, 342 }, 5.2));
        // A new fight (reset): the true order.
        hold.Reset();
        Assert.Equal(new[] { 1, 2 }, hold.Update(new[] { 1, 2 }, new double[] { 2, 1 }, 6));
    }

    [Fact]
    public void Ranks_held_allocate_nothing_once_warm()
    {
        var hold = new RankHold();
        int[] a = { 1, 2, 3, 4, 5, 6, 7, 8 };
        int[] b = { 2, 1, 3, 4, 5, 6, 8, 7 };
        double[] va = { 8, 7, 6, 5, 4, 3, 2, 1 };
        double[] vb = { 8.5, 8, 6, 5, 4, 3, 2.5, 2 };
        for (var i = 0; i < 20; i++) hold.Update(i % 2 == 0 ? a : b, i % 2 == 0 ? va : vb, i * 0.25);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 20; i < 220; i++) hold.Update(i % 2 == 0 ? a : b, i % 2 == 0 ? va : vb, i * 0.25);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Its_heights_are_the_mock_ups()
    {
        var t = MeterThemes.ReadyCheck;
        // Eight and the limit break: 238 (today's compact was 272); a light party with no limit break yet: 133.
        Assert.Equal(238, PlateSizes.Needed(t, 8, true));
        Assert.Equal(133, PlateSizes.Needed(t, 4, false));
        // Never more than eight rows.
        Assert.Equal(PlateSizes.Needed(t, 8, true), PlateSizes.Needed(t, 24, true));
        Assert.Equal(PlateSizes.Needed(t, 8, true) + PlateSizes.PinRule, PlateSizes.Needed(t, 8, true, pinned: true));
        Assert.Equal(8, PlateSizes.RowsIn(t, 238, true));
        // The last row needs no gap after it, and a pixel of slack (as the normal meter has).
        Assert.Equal(8, PlateSizes.RowsIn(t, 236, true));
        Assert.Equal(7, PlateSizes.RowsIn(t, 235, true));
        Assert.Equal(1, PlateSizes.RowsIn(t, 10, true));
        foreach (var theme in MeterThemes.All)
        {
            for (var n = 1; n <= 8; n++) Assert.Equal(n, PlateSizes.RowsIn(theme, PlateSizes.Needed(theme, n, true), true));
            // The one line is shorter than a head and a row.
            Assert.True(theme.Win.CollapsedH < theme.Win.HeadH + theme.Row.H + 8);
        }
    }

    [Fact]
    public void The_six_themes_are_a_table_in_their_order()
    {
        Assert.Equal(6, MeterThemes.All.Count);
        for (var i = 0; i < MeterThemes.All.Count; i++)
        {
            Assert.Equal((MeterThemeId)i, MeterThemes.All[i].Id);
            Assert.Same(MeterThemes.All[i], MeterThemes.Of((MeterThemeId)i));
        }
        Assert.Same(MeterThemes.ReadyCheck, MeterThemes.Of((MeterThemeId)99));
        Assert.Equal(new[] { "Ready Check", "Mochi", "Native", "Readout", "Neon", "Grimoire" }, MeterThemes.All.Select(t => t.Name));
        // Their bars, marks for you and the dead, as the mock-ups have them.
        Assert.Equal(new[] { BarStyle.Fill, BarStyle.Pill, BarStyle.Hp, BarStyle.Segments, BarStyle.Glow, BarStyle.Underline }, MeterThemes.All.Select(t => t.Bar.Style));
        Assert.Equal(new[] { YouStyle.Notch, YouStyle.Heart, YouStyle.Gold, YouStyle.Invert, YouStyle.White, YouStyle.Ribbon }, MeterThemes.All.Select(t => t.YouStyle));
        Assert.Equal(new[] { DeathStyle.Skull, DeathStyle.Broken, DeathStyle.Cross, DeathStyle.Tag, DeathStyle.Glitch, DeathStyle.Dagger }, MeterThemes.All.Select(t => t.DeathStyle));
        // Mochi wider and its rows taller than the mock-ups' (256, 22): its words a step up, to read in the game.
        Assert.Equal(new[] { 248f, 268, 262, 248, 256, 268 }, MeterThemes.All.Select(t => t.Win.Width));
        Assert.Equal(new[] { 20f, 24, 22, 17, 20, 21 }, MeterThemes.All.Select(t => t.Row.H));
        // Ready Check's tokens, the default.
        var r = MeterThemes.ReadyCheck;
        Assert.Equal(Rgb.Hex(0x7c85de), r.Roles.Tank);
        Assert.Equal(Rgb.Hex(0x47e19e), r.Roles.Healer);
        Assert.Equal(Rgb.Hex(0xe8402f), r.Roles.Dps);
        Assert.Equal(Rgb.Hex(0xff8a3d), r.You);
        Assert.Equal(0.92f, r.Row.PlateAlpha);
        Assert.Equal(0.3f, r.Bar.Alpha);
        Assert.Equal(Face.SansMedium, r.Font.Name.Face);
        Assert.Equal(Face.MonoSemibold, r.Font.Num.Face);
        // The faces each theme ships or borrows: Mochi Zen Maru Gothic for its words, a step up from the mock-ups, and Geist
        // SemiBold for its numbers and clock; Grimoire Fraunces (digits in cells); Native the game's, outlined.
        var mochi = MeterThemes.Mochi.Font;
        Assert.All(new[] { mochi.Name, mochi.Label }, f => Assert.Equal(Face.Round, f.Face));
        Assert.All(new[] { mochi.Num, mochi.Small, mochi.Head }, f => Assert.Equal(Face.SansSemibold, f.Face));
        Assert.Equal((14, 12, 13, 11, 14), (mochi.Name.Px, mochi.Label.Px, mochi.Num.Px, mochi.Small.Px, mochi.Head.Px));
        Assert.All(MeterThemes.Grimoire.Font.All(), f => Assert.True(f.Face is Face.Serif or Face.SerifSemibold));
        Assert.True(MeterThemes.Grimoire.Font.Num.Cells > 0 && MeterThemes.Mochi.Font.Num.Cells > 0);
        Assert.All(MeterThemes.Native.Font.All(), f => Assert.True(f.Face is Face.GameAxis or Face.GameTitle));
        // Its numbers in Axis (MiedingerMid lost its decimal points at these sizes); Jupiter only for the head's word.
        Assert.Equal(Face.GameAxis, MeterThemes.Native.Font.Num.Face);
        Assert.Equal(Face.GameAxis, MeterThemes.Native.Font.Small.Face);
        Assert.Equal(Face.GameTitle, MeterThemes.Native.Font.Head.Face);
        Assert.True(MeterThemes.Native.Font.Name.Outline && MeterThemes.Native.Font.Num.Outline);
        Assert.All(MeterThemes.Readout.Font.All(), f => Assert.True(f.Face is Face.Mono or Face.MonoSemibold));
        Assert.True(MeterThemes.Neon.Font.Head.Caps && MeterThemes.Neon.Font.Head.Face == Face.Callout);
        Assert.Contains((Face.RoundBlack, 14), MeterThemes.FacesOf(MeterThemes.Mochi));
        Assert.Contains((Face.SansSemibold, 12), MeterThemes.FacesOf(MeterThemes.Mochi));
        Assert.Contains((Face.SerifSemibold, 19), MeterThemes.FacesOf(MeterThemes.Grimoire));
        // Whole numbers widen the column by 10.
        Assert.Equal(48, r.ValueWidth(true));
        Assert.Equal(38, r.ValueWidth(false));
    }

    [Fact]
    public void A_plate_less_theme_keeps_its_floor_over_a_bright_arena()
    {
        // Mochi 90%, Readout 82%, Grimoire 92%: the Background slider can't take them lower.
        Assert.Equal(new[] { 0f, 0.9f, 0, 0.82f, 0, 0.92f }, MeterThemes.All.Select(t => t.Win.Floor));
        Assert.Equal(0.82f, MeterThemes.Readout.Alpha(0.45f));
        Assert.Equal(0.45f, MeterThemes.ReadyCheck.Alpha(0.45f));
        Assert.Equal(0.2f, MeterThemes.ReadyCheck.Alpha(0.05f));
        // Every theme's names read over its rows, over a black arena and a white one, at the slider's lowest (4.5:1, WCAG AA).
        foreach (var t in MeterThemes.All)
            foreach (var behind in new[] { Vector4.One, new Vector4(0, 0, 0, 1) })
            {
                var ground = Rgb.Over(t.Win.GradBottom ?? t.Win.Bg, t.Alpha(0.2f), behind);
                var plate = t.Row.PlateAlpha > 0 ? Rgb.Over(t.Row.Plate, t.Row.PlateAlpha, ground) : ground;
                Assert.True(ColorMath.Contrast(t.Row.Text, plate) >= 4.5f, $"{t.Name} over {behind}: {ColorMath.Contrast(t.Row.Text, plate)}");
                Assert.True(ColorMath.Contrast(t.Row.Num, plate) >= 4.5f, t.Name);
            }
    }

    [Fact]
    public void The_roles_are_told_apart_with_colour_blindness()
    {
        // The mock-ups' figures (CIE76 ΔE, Machado 2009): Ready Check's set at least 40 apart under all three; the game's red and green 14 apart for a deutan.
        for (var k = 0; k < 3; k++) Assert.True(ColorMath.RolesApart(MeterThemes.ReadyCheck.Roles, k) >= 40, $"kind {k}");
        Assert.InRange(ColorMath.RolesApart(MeterThemes.GameRoles, 1), 13, 15);
        Assert.InRange(ColorMath.RolesApart(MeterThemes.Mochi.Roles, 1), 47, 49);
        Assert.InRange(ColorMath.RolesApart(MeterThemes.Native.Roles, 1), 30, 32);
        Assert.InRange(ColorMath.RolesApart(MeterThemes.Readout.Roles, 1), 27, 29);
        Assert.InRange(ColorMath.RolesApart(MeterThemes.Neon.Roles, 1), 38, 40);
        Assert.InRange(ColorMath.RolesApart(MeterThemes.Grimoire.Roles, 1), 58, 60);
        // Every theme's set is further apart for a deutan than the game's.
        foreach (var t in MeterThemes.All) Assert.True(ColorMath.RolesApart(t.Roles, 1) > ColorMath.RolesApart(MeterThemes.GameRoles, 1) * 1.9f, t.Name);
        // The setting: the theme's, or the game's own.
        Assert.Equal(MeterThemes.Neon.Roles.Tank, MeterThemes.Neon.RoleColor("tank", MeterRoleColours.Theme));
        Assert.Equal(MeterThemes.GameRoles.Healer, MeterThemes.Neon.RoleColor("healer", MeterRoleColours.Game));
        Assert.Equal(MeterThemes.Neon.Roles.Dps, MeterThemes.Neon.RoleColor("dps", MeterRoleColours.Theme));
    }

    [Fact]
    public void Decorations_off_keeps_the_theme_without_its_flourishes()
    {
        foreach (var t in MeterThemes.All) Assert.Equal(Ornaments.None, t.Drawn(false));
        Assert.True((MeterThemes.Mochi.Drawn(true) & Ornaments.Sparkles) != 0);
        Assert.True((MeterThemes.Readout.Drawn(true) & Ornaments.Scanlines) != 0);
        Assert.True((MeterThemes.Neon.Drawn(true) & Ornaments.Horizon) != 0);
        Assert.Equal(YouStyle.Ribbon, MeterThemes.Grimoire.YouDrawn(true));
        Assert.Equal(YouStyle.Gold, MeterThemes.Grimoire.YouDrawn(false));
        Assert.Equal(YouStyle.Gold, MeterThemes.Mochi.YouDrawn(false));
        Assert.Equal(YouStyle.Notch, MeterThemes.ReadyCheck.YouDrawn(false));
        Assert.Equal(DeathStyle.Glitch, MeterThemes.Neon.DeathDrawn(true));
        Assert.Equal(DeathStyle.Cross, MeterThemes.Neon.DeathDrawn(false));
        Assert.Equal(DeathStyle.Dagger, MeterThemes.Grimoire.DeathDrawn(false));
    }

    [Theory]
    [InlineData("PLD", 19u, 62119u)]
    [InlineData("DRG", 22u, 62122u)]
    [InlineData("whm", 24u, 62124u)]
    [InlineData("SGE", 40u, 62140u)]
    [InlineData("PCT", 42u, 62142u)]
    [InlineData("GLA", 1u, 62101u)]
    [InlineData("XYZ", 0u, 0u)]
    [InlineData("", 0u, 0u)]
    [InlineData(null, 0u, 0u)]
    public void Job_icons_by_job(string? job, uint row, uint icon)
    {
        Assert.Equal(row, JobIcons.ClassJob(job));
        Assert.Equal(icon, JobIcons.Icon(job));
    }

    [Fact]
    public void A_configuration_from_before_lands_on_compact()
    {
        // 0.6 and before (no MeterLook): compact, whatever it had, the old compact's height let go.
        Assert.Equal((1, true, 0f), MeterLook.Migrate(0, false, 180));
        Assert.Equal((1, true, 0f), MeterLook.Migrate(0, true, 271));
        // Once moved, it's yours: normal stays normal.
        Assert.Equal((1, false, 180f), MeterLook.Migrate(1, false, 180));
        Assert.Equal((2, false, 0f), MeterLook.Migrate(2, false, 0));
    }

    [Fact]
    public void Its_small_words()
    {
        Assert.Equal(("P2 · 0:41", "P2", "P2+0:41", "Phase 2"), PlateWords.Phase("P2", 41));
        Assert.Equal(("Adds · 0:05", "Adds", "Adds+0:05", "Adds"), PlateWords.Phase("Adds", 5.2));
        Assert.Null(PlateWords.Phase(null, 10));
        Assert.Null(PlateWords.Phase("", 10));
        Assert.Equal("DPS", PlateWords.TabWord(MeterTab.Damage));
        Assert.Equal("HPS", PlateWords.TabWord(MeterTab.Healing));
        Assert.Equal("Taken", PlateWords.TabWord(MeterTab.Taken));
        Assert.Equal("0:20", PlateWords.DeadFor(171, 191));
        Assert.Equal("0:00", PlateWords.DeadFor(191, 180));
    }

    [Fact]
    public void Compact_is_never_resizable()
    {
        // Compact's width is its theme's and its height its rows': unlocked or locked, it can't be sized by hand.
        Assert.False(MeterLayout.Resizable(compact: true, locked: false));
        Assert.False(MeterLayout.Resizable(compact: true, locked: true));
        // The normal meter can, unlocked.
        Assert.True(MeterLayout.Resizable(compact: false, locked: false));
        Assert.False(MeterLayout.Resizable(compact: false, locked: true));
    }

    [Fact]
    public void The_resize_grips_reach_as_ImGui_works_it_out()
    {
        // Dalamud's 17px font, 6.4px corners: the grip is drawn floor(17 × 1.35) = 22 and takes a press 16 in; the edges 4 either side.
        var r = ResizeReach.Of(17, 6.4f, fromEdges: true);
        Assert.Equal(new ResizeReach(4, 16, true), r);
        Assert.Equal(20, r.CornerInset);
        // It grows with the font (Dalamud's scale): 34px, drawn 45, 33 in.
        Assert.Equal(33, ResizeReach.Of(34, 6.4f, true).Corner);
        // Big corners make a bigger grip: floor(30 + 1 + 3.2) = 34, 25 in.
        Assert.Equal(25, ResizeReach.Of(16, 30, true).Corner);
        // A bigger touch padding widens the edges; without resizing from the edges there are none.
        Assert.Equal(6, ResizeReach.Of(17, 0, true, touchPadding: 6).Border);
        Assert.Equal(new ResizeReach(0, 12, false), ResizeReach.Of(13, 0, false));
    }

    [Fact]
    public void A_lock_in_a_corner_is_moved_out_of_the_grip()
    {
        var r = ResizeReach.Of(17, 6.4f, true);
        var wmin = new Vector2(100, 100);
        var wmax = new Vector2(430, 482);
        // The normal meter growing up: its head at the foot, the 24px lock 6 in from the right and the bottom: under the bottom-right grip.
        var min = new Vector2(400, 452);
        var max = new Vector2(424, 476);
        Assert.True(ResizeZones.Hits(wmin, wmax, min, max, r));
        var shift = ResizeZones.ShiftLeft(wmin, wmax, min, max, r);
        Assert.Equal(14, shift);
        var at = new Vector2(shift, 0);
        Assert.False(ResizeZones.Hits(wmin, wmax, min - at, max - at, r));
        // It ends the grip's reach and 4 more in from the side.
        Assert.Equal(wmax.X - r.Corner - ResizeReach.HoverPadding, max.X - shift);
        // Growing down (the head under the title bar, at the top-right): no grip there, clear of the right edge, left where it is.
        Assert.Equal(0, ResizeZones.ShiftLeft(wmin, wmax, new Vector2(400, 132), new Vector2(424, 156), r));
        Assert.False(ResizeZones.Hits(wmin, wmax, new Vector2(400, 132), new Vector2(424, 156), r));
        // On the right edge's band, moved just off it.
        Assert.Equal(2, ResizeZones.ShiftLeft(wmin, wmax, new Vector2(404, 200), new Vector2(428, 224), r));
        // A button in the bottom-left corner (the plan window's Previous) moves right out of that grip.
        Assert.Equal(14, ResizeZones.ShiftRight(wmin, wmax, new Vector2(106, 452), new Vector2(130, 476), r));
        Assert.Equal(0, ResizeZones.ShiftRight(wmin, wmax, new Vector2(106, 200), new Vector2(130, 224), r));
        // Without resizing from the edges only the bottom-right grip is there.
        var one = ResizeReach.Of(17, 6.4f, false);
        Assert.False(ResizeZones.Hits(wmin, wmax, new Vector2(100, 452), new Vector2(124, 482), one));
        Assert.Equal(0, ResizeZones.ShiftRight(wmin, wmax, new Vector2(100, 452), new Vector2(124, 482), one));
        Assert.True(ResizeZones.Hits(wmin, wmax, min, max, one));
        Assert.False(ResizeZones.Hits(wmin, wmax, min, max, ResizeReach.None));
        Assert.True(ResizeZones.Inside(new Vector2(400, 452), min, max));
        Assert.False(ResizeZones.Inside(new Vector2(399, 452), min, max));
    }

    [Fact]
    public void The_columns_are_as_wide_as_their_face_draws_them()
    {
        var t = MeterThemes.Mochi;
        // A narrow face: the mock-ups' widths (CSS px) at the scale.
        float Narrow(string s, ThemeFont f) => s.Length * 4;
        Assert.Equal(t.ValueWidth(false) * 1.5f, PlateColumns.Value(t, false, 1.5f, Narrow));
        Assert.Equal(t.ValueWidth(true), PlateColumns.Value(t, true, 1, Narrow));
        Assert.Equal(t.ShareCol, PlateColumns.Share(t, 1, Narrow));
        // A face wider in the game: the widest sample as measured, in the face the column draws in.
        var asked = new List<(string, ThemeFont)>();
        float Wide(string s, ThemeFont f)
        {
            asked.Add((s, f));
            return s.Length * 11;
        }
        Assert.Equal(5 * 11, PlateColumns.Value(t, false, 1, Wide));
        Assert.All(asked, a => Assert.Equal(t.Font.Num, a.Item2));
        Assert.Contains(("8.88M", t.Font.Num), asked);
        Assert.Equal(7 * 11, PlateColumns.Value(t, true, 1, Wide));
        asked.Clear();
        Assert.Equal(5 * 11, PlateColumns.Share(t, 1, Wide));
        Assert.All(asked, a => Assert.Equal(t.Font.Small, a.Item2));
        Assert.Contains(("100.0", t.Font.Small), asked);
    }

    [Fact]
    public void A_full_bar_ends_before_the_share()
    {
        // A 246px row, 5 in on the right, a 32px share column: the track ends 3 before the share (206).
        Assert.Equal(206, PlateColumns.Track(246, 5, 0, 32, 1));
        // Mochi keeps its heart's place (12) on every row; at 150% the gap is 4.5.
        Assert.Equal(246 - 7 - 12 - 32 - 4.5f, PlateColumns.Track(246, 7, 12, 32, 1.5f));
        // Never below nothing.
        Assert.Equal(0, PlateColumns.Track(20, 5, 0, 32, 1));
        // So 100% of the track, from the row's left, stops short of the share column's left edge.
        var shareLeft = 246 - 5 - 32;
        Assert.True(PlateColumns.Track(246, 5, 0, 32, 1) < shareLeft);
    }

    [Fact]
    public void A_bar_with_a_hard_end_stops_before_the_number_too()
    {
        // Native's HP bar, Mochi's pill, Neon's cap: their track ends before the number column (and its gap) as well.
        Assert.Equal(new[] { false, true, true, false, true, false }, MeterThemes.All.Select(t => t.Bar.BeforeNumber));
        // A 246px row, 5 in, a 32px share, a 46px number 6 before it: 246 − 5 − 32 − 6 − 46 − 3 = 154.
        Assert.Equal(154, PlateColumns.Track(246, 5, 0, 32, 1, numberWidth: 46, columnGap: 6));
        // The bars keep their lengths to each other: only the track is shorter.
        var full = PlateColumns.Track(246, 5, 0, 32, 1, 46, 6);
        Assert.Equal(0.5f * full, 0.5f * PlateColumns.Track(246, 5, 0, 32, 1, 46, 6));
        Assert.True(full < PlateColumns.Track(246, 5, 0, 32, 1));
    }

    [Theory]
    // Geist (and Geist Mono): ascent 1005, descent 295, 'H' 0–710 of 1000: its capitals are centred in its line: no shift.
    [InlineData(1005, 295, 710, 1000, 14 * 1.3f, 0f)]
    // Zen Maru Gothic: ascent 1160, descent 288, 'H' 0–700: 0.086 em low by its line, so up 1.2px at 14px.
    [InlineData(1160, 288, 700, 1000, 14 * 1.448f, -1.204f)]
    // Fraunces at opsz 14: 0.0115 em low, a sixth of a pixel at 13px.
    [InlineData(1956, 510, 1400, 2000, 13 * 1.233f, -0.150f)]
    public void Words_are_centred_by_their_capitals_ink(float ascent, float descent, float capHeight, float em, float fontSize, float shift)
    {
        // ImGui's glyph for 'H' from the line's top (a font sized by its line, ascent to descent).
        var k = fontSize / (ascent + descent);
        var top = (ascent - capHeight) * k;
        var bottom = ascent * k;
        var mid = InkCentre.Mid(top, bottom);
        Assert.Equal(shift, InkCentre.Shift(fontSize, mid), 2);
        // Drawn on a line's middle: the ink's middle is on it.
        Assert.Equal(50, InkCentre.Top(50, mid) + mid, 3);
        _ = em;
    }

    [Fact]
    public void The_opened_up_window_takes_the_themes_faces()
    {
        // Ready Check: the window as it was.
        var rc = DetailFonts.For(MeterThemes.ReadyCheck);
        Assert.Same(DetailFonts.ReadyCheck, rc);
        Assert.Equal(new ThemeFont(Face.SansMedium, 14), rc.Name);
        Assert.Equal(new ThemeFont(Face.Mono, 13), rc.Num);
        Assert.Equal(new ThemeFont(Face.MonoSemibold, 13), rc.NumStrong);
        Assert.Equal(new ThemeFont(Face.Mono, 10, Caps: true), rc.Head);
        Assert.Equal(new ThemeFont(Face.MonoSemibold, 18), rc.Clock);
        // Mochi: its words two steps up (Zen Maru reads small), its numbers Geist SemiBold a step up, as its compact faces are.
        var mochi = DetailFonts.For(MeterThemes.Mochi);
        Assert.Equal((Face.Round, 16), (mochi.Name.Face, mochi.Name.Px));
        Assert.Equal((Face.RoundBlack, 17), (mochi.Title.Face, mochi.Title.Px));
        Assert.Equal((Face.SansSemibold, 14), (mochi.Num.Face, mochi.Num.Px));
        Assert.Equal((Face.SansSemibold, 13), (mochi.Small.Face, mochi.Small.Px));
        Assert.True(mochi.Head.Caps && mochi.Head.Cells == 0);
        // Native: outlined Axis, never MiedingerMid; its clock in its numbers' face.
        var native = DetailFonts.For(MeterThemes.Native);
        Assert.All(native.All(), f => Assert.NotEqual(Face.GameNumbers, f.Face));
        Assert.True(native.Num.Outline && native.Name.Outline);
        Assert.Equal(Face.GameAxis, native.Clock.Face);
        // Every theme's faces are loaded with it.
        foreach (var t in MeterThemes.All)
            Assert.All(DetailFonts.For(t).All(), f => Assert.Contains((f.Face, f.Px), DetailFonts.For(t).Faces()));
    }

    [Fact]
    public void A_tables_columns_are_as_wide_as_what_they_show()
    {
        // "1,337,000" (the limit break's biggest hit) in a face 8px a character: wider than its 68px column, so the column widens.
        float Eight(string s, ThemeFont f) => s.Length * 8;
        var into = new float[3];
        var css = new[] { 44f, 68f, 40f };
        var heads = new[] { "DH", "Max hit", "Deaths" };
        var fonts = new[] { new ThemeFont(Face.Mono, 13), new ThemeFont(Face.Mono, 13), new ThemeFont(Face.Mono, 13) };
        var rows = new List<string[]> { new[] { "–", "1,337,000", string.Empty }, new[] { "18.2", "61,400", "1" } };
        DetailColumns.Widths(into, css, 1, heads, new ThemeFont(Face.Mono, 10, Caps: true), fonts, rows, Eight);
        Assert.Equal(new[] { 44f, 72f, 48f }, into);
        // At 150% the CSS widths scale; a column is never narrower than them.
        DetailColumns.Widths(into, css, 1.5f, heads, new ThemeFont(Face.Mono, 10, Caps: true), fonts, rows, (s, f) => 1);
        Assert.Equal(new[] { 66f, 102f, 60f }, into);
    }

    [Fact]
    public void A_dropdowns_chevron_keeps_its_gap()
    {
        // 11.2 in, the words (80), 6, the chevron (15), 11.2.
        Assert.Equal(123.4f, Dropdown.Width(80, 15, 11.2f, 1), 3);
        Assert.Equal(97.2f, Dropdown.ChevronAt(80, 11.2f, 1), 3);
        // The chevron ends where the padding starts; the gap scales.
        Assert.Equal(Dropdown.Width(80, 15, 11.2f, 1) - 11.2f, Dropdown.ChevronAt(80, 11.2f, 1) + 15, 3);
        Assert.Equal(80 + 9f, Dropdown.ChevronAt(80, 0, 1.5f), 3);
    }
}
