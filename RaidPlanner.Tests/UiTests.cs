using System.Numerics;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>The windows' words and numbers (UI/Model/Format.cs), said as the site says them.</summary>
public class UiFormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(7.9, "0:07")]
    [InlineData(151, "2:31")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-4, "0:00")]
    [InlineData(double.NaN, "0:00")]
    public void Clock(double s, string text) => Assert.Equal(text, Format.Clock(s));

    [Theory]
    [InlineData(165.2, "2:45.2")]
    [InlineData(165.29, "2:45.2")]
    [InlineData(171.3, "2:51.3")]
    [InlineData(0, "0:00.0")]
    public void Clock_to_a_tenth(double s, string text) => Assert.Equal(text, Format.ClockTenths(s));

    [Theory]
    [InlineData(27840, "27,840")]
    [InlineData(5317440, "5,317,440")]
    [InlineData(0, "0")]
    public void Thousands(long n, string text) => Assert.Equal(text, Format.Thousands(n));

    [Theory]
    [InlineData(9050, "9,050")]
    [InlineData(999_999, "999,999")]
    [InlineData(1_000_000, "1.00M")]
    [InlineData(1_289_999, "1.28M")]
    [InlineData(13_103_000, "13.1M")]
    [InlineData(128_400_000, "128M")]
    [InlineData(1_280_000_000, "1.28B")]
    [InlineData(-38_620, "−38,620")]
    public void Compact_numbers_never_read_bigger(long n, string text) => Assert.Equal(text, Format.Compact(n));

    [Fact]
    public void Shares_and_rates()
    {
        Assert.Equal("16.9", Format.Share(0.169));
        Assert.Equal("100.0", Format.Share(1));
        Assert.Equal("0.0", Format.Share(double.NaN));
        Assert.Equal("31%", Format.PercentWhole(0.31));
        Assert.Equal("82%", Format.PercentWhole(0.8249));
        Assert.Equal("+3.4%", Format.SignedPercent(0.034));
        Assert.Equal("−1.2%", Format.SignedPercent(-0.012));
        Assert.Equal("−38,620", Format.Signed(-38620));
        Assert.Equal("+6,120", Format.Signed(6120));
        Assert.Equal("0.6 MB", Format.Megabytes(600 * 1024));
        Assert.Equal("0.1 MB", Format.Megabytes(10));
        Assert.Equal("in 16 s", Format.InSeconds(15.2));
        Assert.Equal("in 1:04", Format.InSeconds(64));
    }

    [Fact]
    public void Ago_as_the_site_says_it()
    {
        var now = new DateTimeOffset(2026, 11, 11, 23, 41, 0, TimeSpan.Zero).ToLocalTime();
        Assert.Equal("just now", Format.Ago(now.AddSeconds(-20), now));
        Assert.Equal("1 minute ago", Format.Ago(now.AddMinutes(-1), now));
        Assert.Equal("8 minutes ago", Format.Ago(now.AddMinutes(-8), now));
        Assert.Equal("yesterday", Format.Ago(now.AddDays(-1), now));
        Assert.Equal("4 days ago", Format.Ago(now.AddDays(-4), now));
        Assert.Equal(now.AddDays(-30).ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture), Format.Ago(now.AddDays(-30), now));
    }

    [Fact]
    public void Days_for_eyebrows()
    {
        var now = new DateTime(2026, 11, 11, 23, 45, 0, DateTimeKind.Local);
        var nowOffset = new DateTimeOffset(now);
        Assert.Equal("tonight 23:42", Format.Day(new DateTimeOffset(now.AddMinutes(-3)), nowOffset));
        Assert.Equal("today 14:02", Format.Day(new DateTimeOffset(new DateTime(2026, 11, 11, 14, 2, 0, DateTimeKind.Local)), nowOffset));
        Assert.Equal("yesterday 23:52", Format.Day(new DateTimeOffset(new DateTime(2026, 11, 10, 23, 52, 0, DateTimeKind.Local)), nowOffset));
        Assert.Equal("Sent yesterday at 23:52.", Format.SentLine(new DateTimeOffset(new DateTime(2026, 11, 10, 23, 52, 0, DateTimeKind.Local)), nowOffset));
        Assert.Equal("Sent 8 minutes ago.", Format.SentLine(nowOffset.AddMinutes(-8), nowOffset));
    }

    [Fact]
    public void Pulls_aside()
    {
        var now = new DateTimeOffset(new DateTime(2026, 11, 11, 23, 45, 0, DateTimeKind.Local));
        Assert.Equal("Tonight · 2", Format.PullsAside(new[] { now.AddMinutes(-10), now.AddMinutes(-30) }, now));
        Assert.Equal("Today · 2", Format.PullsAside(new[] { now.AddMinutes(-10), now.AddHours(-10) }, now));
        Assert.Equal("2 pulls", Format.PullsAside(new[] { now.AddMinutes(-10), now.AddDays(-2) }, now));
        Assert.Equal("1 pull", Format.PullsAside(new[] { now.AddDays(-3) }, now));
    }

    [Fact]
    public void Steps_spots_caps_and_clocks_read_back()
    {
        Assert.Equal("04", Format.StepNumber(4));
        Assert.Equal("13", Format.StepNumber(13));
        Assert.Equal("120", Format.StepNumber(120));
        Assert.Equal("tank", Format.RoleOfSlot("MT"));
        Assert.Equal("tank", Format.RoleOfSlot("ot"));
        Assert.Equal("healer", Format.RoleOfSlot("H2"));
        Assert.Equal("dps", Format.RoleOfSlot("R1"));
        Assert.Equal("dps", Format.RoleOfSlot("D3"));
        Assert.Null(Format.RoleOfSlot("P9"));
        Assert.Equal("EXAFLARES", Format.Caps("Exaflares", false));
        Assert.Equal("定位置で散開", Format.Caps("定位置で散開", true));
        Assert.Equal("Exaflares", Format.Caps("Exaflares", true));
        Assert.Equal(84, Format.ParseClock("1:24"));
        Assert.Equal(3723, Format.ParseClock("1:02:03"));
        Assert.Null(Format.ParseClock("soon"));
        Assert.Null(Format.ParseClock(""));
    }

    /// <summary>A measure for the tests: every letter 10 wide.</summary>
    private static float Ten(string s) => s.Length * 10;

    [Fact]
    public void Truncation_keeps_the_most_letters_that_fit()
    {
        Assert.Equal("Example", Format.Fit("Example", Ten, 70));
        Assert.Equal("Exam…", Format.Fit("Example", Ten, 50));
        Assert.Equal("…", Format.Fit("Example", Ten, 5));
        // A space before the dots is left off.
        Assert.Equal("Pull…", Format.Fit("Pull 12 tonight", Ten, 60));
    }

    [Fact]
    public void Wrapping_to_lines_the_last_cut()
    {
        var lines = Format.Wrap("Middle, step through the first", Ten, 160);
        Assert.Equal(new[] { "Middle, step", "through the", "first" }, lines);
        var two = Format.Wrap("Middle, step through the first", Ten, 160, 2);
        Assert.Equal(2, two.Count);
        Assert.Equal("through the fir…", two[1]);
        Assert.Single(Format.Wrap("", Ten, 100));
    }

    [Fact]
    public void A_word_wider_than_a_line_breaks_where_it_fills_it()
    {
        // Japanese has no spaces: a step's callout is one "word", and still wraps (it was cut on its first line).
        var ja = "中央で待機して最初の波を抜ける";
        Assert.Equal(new[] { "中央で待機して", "最初の波を抜け", "る" }, Format.Wrap(ja, Ten, 70));
        // At most two lines: the rest goes on the last, joined again without a space, and is cut.
        Assert.Equal(new[] { "中央で待機して", "最初の波を抜…" }, Format.Wrap(ja, Ten, 70, 2));
        // A long word among others: broken, and the words after it go on as usual.
        Assert.Equal(new[] { "Go", "abcdefghij", "klm now" }, Format.Wrap("Go abcdefghijklm now", Ten, 100));
        // A pair of surrogates isn't split.
        Assert.All(Format.Wrap("𠀋𠀋𠀋𠀋", Ten, 30), l => Assert.False(l.Length > 0 && char.IsLowSurrogate(l[0])));
    }

    [Fact]
    public void Errors_said_as_a_headline_and_a_line()
    {
        Assert.Equal(("Couldn't reach the site", "Check your connection."), Format.Headline("Couldn't reach the site. Check your connection."));
        Assert.Equal(("The site didn't answer in time", "Try again in a moment."), Format.Headline("The site didn't answer in time."));
        var (title, line) = Format.Headline(new string('x', 90) + ".");
        Assert.Equal("Something went wrong", title);
        Assert.StartsWith("xxx", line);
    }
}

public class EaseTests
{
    [Fact]
    public void Curves_start_and_end_where_they_should()
    {
        Assert.Equal(0, Ease.Snap(0));
        Assert.Equal(1, Ease.Snap(1));
        Assert.Equal(1, Ease.Out(2));
        // Out and snap are fast early; in is slow early.
        Assert.True(Ease.Out(0.25f) > 0.6f);
        Assert.True(Ease.Snap(0.5f) > 0.8f);
        Assert.True(Ease.In(0.25f) < 0.15f);
        // A linear bezier is linear.
        Assert.Equal(0.3f, Ease.Bezier(0.3f, 0.3f, 0.7f, 0.7f, 0.3f), 3);
    }
}

public class PolygonTests
{
    private const string BossShield =
        "M-46 -50 C-47 -36 -38 -27 -24 -27 L0 -31 L24 -27 C38 -27 47 -36 46 -50 C55 -36 53 -18 42 -10 L40 6 C38 27 20 40 0 50 C-20 40 -38 27 -40 6 L-42 -10 C-53 -18 -55 -36 -46 -50 Z";

    private const string Mouth = "M-24 10 L-15 27 L-8 17 L0 29 L8 17 L15 27 L24 10 L15 17 L8 11 L0 20 L-8 11 L-15 17 Z";

    [Theory]
    [InlineData(BossShield)]
    [InlineData(Mouth)]
    public void Triangles_cover_a_concave_shape_exactly(string d)
    {
        var outline = Polygon.Outlines(d)[0];
        var tris = Polygon.Triangulate(outline);
        Assert.Equal((outline.Length - 2) * 3, tris.Length);
        Assert.Equal(MathF.Abs(Polygon.Area(outline)), Polygon.TrianglesArea(outline, tris), 1);
    }

    [Fact]
    public void Either_winding_works()
    {
        var square = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) };
        Assert.Equal(100, Polygon.TrianglesArea(square, Polygon.Triangulate(square)), 3);
        var back = square.Reverse().ToArray();
        Assert.Equal(100, Polygon.TrianglesArea(back, Polygon.Triangulate(back)), 3);
        Assert.Empty(Polygon.Triangulate(new[] { Vector2.Zero, Vector2.One }));
    }

    [Fact]
    public void A_face_is_three_outlines()
    {
        var face = Polygon.Outlines("M-27 -15 L-6 -5 L-8 4 L-26 -3 Z M27 -15 L6 -5 L8 4 L26 -3 Z " + Mouth);
        Assert.Equal(3, face.Count);
        Assert.Equal(4, face[0].Length);
    }
}

public class StatesTests
{
    [Fact]
    public void The_plan_window_says_the_one_thing_that_matters()
    {
        Assert.Equal(EmptyKind.NotLinked, EmptyState.Of(false, true, true, "x", 3, true).Kind);
        Assert.Equal(EmptyKind.Reading, EmptyState.Of(true, true, false, null, 3, true).Kind);
        Assert.Equal("Reading your plans", EmptyState.Of(true, false, true, null, 0, false).Title);
        var wrong = EmptyState.Of(true, false, false, "Couldn't reach the site. Check your connection.", 3, true);
        Assert.Equal(EmptyKind.Wrong, wrong.Kind);
        Assert.Equal("Couldn't reach the site", wrong.Title);
        Assert.Equal("Try again", wrong.Button);
        Assert.Equal(EmptyKind.NoPlans, EmptyState.Of(true, false, false, null, 0, true).Kind);
        Assert.Equal(EmptyKind.NoPlanHere, EmptyState.Of(true, false, false, null, 4, true).Kind);
        Assert.Equal(EmptyKind.Pick, EmptyState.Of(true, false, false, null, 4, false).Kind);
    }

    [Fact]
    public void A_step_fills_towards_the_next()
    {
        var cues = new[] { new PlanCue(0, 1, 10), new PlanCue(1, 2, 40), new PlanCue(1, 3, 50), new PlanCue(3, 4, 80) };
        Assert.Equal((40, 80), StepProgress.Window(cues, 1));
        // A step with no cue of its own starts at the last cue before it.
        Assert.Equal((50, 80), StepProgress.Window(cues, 2));
        Assert.Equal((80, null), StepProgress.Window(cues, 3));
        Assert.Equal((null, null), StepProgress.Window(Array.Empty<PlanCue>(), 0));
        var (f, next) = StepProgress.Of(40, 80, 50);
        Assert.Equal(0.25f, f, 3);
        Assert.Equal(30, next);
        Assert.Equal((0f, (double?)null), StepProgress.Of(40, 80, null));
        Assert.Equal((1f, (double?)null), StepProgress.Of(80, null, 90));
        Assert.Equal(1f, StepProgress.Of(40, 80, 200).Fraction);
    }

    [Theory]
    [InlineData("The site doesn't know this token. Link again in Settings with a new one.", true, SendFix.LinkAgain)]
    [InlineData("That isn't a Raid Planner token. Link again in Settings.", true, SendFix.LinkAgain)]
    [InlineData("The site turned the plugin away. It may need updating.", false, SendFix.Update)]
    [InlineData("The site can't take pulls from the plugin right now. The plugin may need updating.", false, SendFix.Update)]
    [InlineData("The site is busy. Try again in a minute.", false, SendFix.TryAgain)]
    [InlineData("The site had a problem (502). Try again in a minute.", false, SendFix.TryAgain)]
    [InlineData("Couldn't reach the site. Check your connection and try again.", false, SendFix.TryAgain)]
    [InlineData("The site didn't answer in time. Look in the Raid Planner before sending it again.", false, SendFix.Look)]
    [InlineData("The site answered, but not with the pull. Look in the Raid Planner before sending it again.", false, SendFix.Look)]
    [InlineData("This pull is too big to send. Save it as a file instead, and import it on the site.", false, SendFix.SaveFile)]
    [InlineData("The pull couldn't be written down to send.", false, SendFix.SaveFile)]
    [InlineData("The pull's file couldn't be read.", false, SendFix.SaveFile)]
    [InlineData("Something went wrong sending it (see /xllog).", false, SendFix.TryAgain)]
    [InlineData("Stopped.", false, SendFix.Stopped)]
    public void Every_failure_has_its_one_thing_to_do(string why, bool token, SendFix fix) =>
        Assert.Equal(fix, SendFailure.Of(new SendResult(null, why, token)).Fix);

    [Fact]
    public void Failures_are_said_plainly()
    {
        Assert.Equal("Couldn't reach the site. Check your connection, then try again.",
            SendFailure.Of(new SendResult(null, "Couldn't reach the site. Check your connection and try again.")).Text);
        Assert.Equal("This pull's file couldn't be read.", SendFailure.Of(new SendResult(null, "The pull's file couldn't be read.")).Text);
        // The site's own words for players are said as it says them.
        Assert.Equal("Couldn't send it. Your inbox is full.", SendFailure.Of(new SendResult(null, "Your inbox is full.")).Text);
        Assert.Equal("Something went wrong sending it. The details are in /xllog.", SendFailure.Of(new SendResult(null, "The site said no (418).")).Text);
    }
}

public class MeterViewTests
{
    private static MeterRow Row(int id, string slot, double enc, double dps, double hps, long taken, bool you = false, bool lb = false, bool dead = false) =>
        new(id, slot, slot + " name", "JOB", Format.RoleOfSlot(slot) ?? "dps", you, lb, (long)(enc * 100), enc, dps, enc / 100000, 0.3, 0.4, 1000, "Hit", 0, 0, hps, 0.25, 0.1, 0.2,
            100, taken, taken / 100.0, 0.1, 500, dead ? 1 : 0, dead, dead ? 50 : null, 100);

    private static MeterEncounter Fight(int id, int pull, uint content, double started, double duration, bool live = false, Outcome? ended = Outcome.Wipe) =>
        new(id, pull, "Arena " + content, content, started, duration, live, live ? null : ended, duration, "P2", duration - 41,
            new[] { Row(1, "MT", 15210, 15210, 2310, 3_912_400, you: true), Row(2, "M1", 27840, 28046, 410, 1_086_900), Row(3, "H2", 9050, 10289, 4120, 1_324_700, dead: true),
                Row(-1, "LB", 7000, 0, 0, 0, lb: true) },
            Row(0, "", 165000, 165000, 19410, 13_103_000), Array.Empty<DeathEntry>());

    [Fact]
    public void Rows_go_biggest_first_by_the_tab_and_the_number()
    {
        var e = Fight(1, 12, 1, 0, 191, true);
        Assert.Equal(new[] { "M1", "MT", "H2" }, MeterView.Ranked(e.Rows, MeterTab.Damage, MeterRank.EncDps).Select(r => r.Slot));
        Assert.Equal(new[] { "H2", "MT", "M1" }, MeterView.Ranked(e.Rows, MeterTab.Healing, MeterRank.EncDps).Select(r => r.Slot));
        Assert.Equal(new[] { "MT", "H2", "M1" }, MeterView.Ranked(e.Rows, MeterTab.Taken, MeterRank.EncDps).Select(r => r.Slot));
        Assert.DoesNotContain(MeterView.Ranked(e.Rows, MeterTab.Damage, MeterRank.Dps), r => r.IsLimitBreak);
        Assert.NotNull(MeterView.LimitBreak(e.Rows));
    }

    [Fact]
    public void Numbers_said_as_the_meter_says_them()
    {
        var e = Fight(1, 12, 1, 0, 191, true);
        var m1 = e.Rows[1];
        Assert.Equal("27,840", MeterView.ValueText(m1, MeterTab.Damage, MeterRank.EncDps));
        Assert.Equal("28,046", MeterView.ValueText(m1, MeterTab.Damage, MeterRank.Dps));
        Assert.Equal("3,912,400", MeterView.ValueText(e.Rows[0], MeterTab.Taken, MeterRank.EncDps));
        // The small meter's 66px column takes a total whole up to ten million (the mock-up's "3,912,400"), then shortened.
        Assert.Equal("3,912,400", MeterView.ValueText(e.Rows[0], MeterTab.Taken, MeterRank.EncDps, narrow: true));
        Assert.Equal("13.1M", MeterView.ValueText(Row(9, "OT", 0, 0, 0, 13_103_000), MeterTab.Taken, MeterRank.EncDps, narrow: true));
        Assert.Equal("13,103,000", MeterView.ValueText(Row(9, "OT", 0, 0, 0, 13_103_000), MeterTab.Taken, MeterRank.EncDps));
        Assert.Equal("27.8", MeterView.Second(m1, MeterTab.Damage));
        Assert.Equal("25%", MeterView.Second(m1, MeterTab.Healing));
        Assert.Equal(("ENCDPS", "%"), MeterView.Heads(MeterTab.Damage, MeterRank.EncDps));
        Assert.Equal(("HPS", "Over"), MeterView.Heads(MeterTab.Healing, MeterRank.EncDps));
        Assert.Equal(("165,000", (string?)null), MeterView.Party(e, MeterTab.Damage, MeterRank.EncDps));
        Assert.Equal(("19,410", "HPS"), MeterView.Party(e, MeterTab.Healing, MeterRank.EncDps));
        Assert.Equal(1f, MeterView.Bar(m1, 27840, MeterTab.Damage, MeterRank.EncDps));
        Assert.Equal(0.5f, MeterView.Bar(m1, 55680, MeterTab.Damage, MeterRank.EncDps), 3);
        Assert.Equal("P2 · 0:41 in", MeterView.Where(e));
        Assert.Equal("Wipe", MeterView.OutcomeWord(Outcome.Wipe));
        Assert.Null(MeterView.OutcomeWord(null));
    }

    [Fact]
    public void The_picker_lists_this_duty_newest_first_and_the_best_starred()
    {
        var best = Fight(4, 4, 1, 100, 255);
        var kept = new[] { Fight(12, 12, 1, 900, 191, true), Fight(11, 11, 1, 800, 252), Fight(9, 9, 2, 700, 100), Fight(10, 10, 1, 750, 178), best };
        var (here, others) = MeterView.Picker(kept, kept[0], best, 3);
        Assert.Equal(new[] { 12, 11, 10, 4 }, here.Select(r => r.Fight.Pull));
        Assert.True(here[0].Shown);
        Assert.True(here[0].Live);
        Assert.True(here[^1].Best);
        Assert.Equal(new[] { 9 }, others.Select(r => r.Fight.Pull));
        // The best already in the list isn't added twice.
        var (again, _) = MeterView.Picker(kept, kept[0], kept[1], 8);
        Assert.Equal(4, again.Count);
        Assert.True(again[1].Best);
    }

    [Fact]
    public void The_small_meters_rows_are_worked_out_once_with_their_words_and_bars()
    {
        var e = Fight(1, 12, 1, 0, 191, true);
        var (rows, lb) = MeterView.Lines(e, MeterTab.Damage, MeterRank.EncDps);
        Assert.Equal(new[] { "M1", "MT", "H2" }, rows.Select(l => l.Row.Slot));
        Assert.Equal(new[] { "27,840", "15,210", "9,050" }, rows.Select(l => l.Value));
        Assert.Equal("27.8", rows[0].Second);
        Assert.Equal(1f, rows[0].Bar);
        Assert.Equal(15210f / 27840f, rows[1].Bar, 4);
        // Ids are unique (each row's hit-test), the limit break's apart from any member's.
        Assert.Equal(rows.Count + 1, rows.Select(l => l.Id).Append(lb!.Id).Distinct().Count());
        Assert.True(lb.Row.IsLimitBreak);
        Assert.Equal(-1, lb.Row.PartyId);
        Assert.Equal("7,000", lb.Value);
        Assert.Equal(7000f / 27840f, lb.Bar, 4);
        // The limit break has a row only on the Damage tab; the others rank by their own number.
        var (heal, none) = MeterView.Lines(e, MeterTab.Healing, MeterRank.EncDps);
        Assert.Null(none);
        Assert.Equal(new[] { "H2", "MT", "M1" }, heal.Select(l => l.Row.Slot));
        Assert.Equal("25%", heal[0].Second);
    }

    [Fact]
    public void A_stopped_meter_says_why_without_saying_it_stopped_twice()
    {
        Assert.Equal("What it reads from the game made no sense, so the game has probably moved something.",
            MeterView.StoppedLine("The meter has stopped: what it reads from the game made no sense, so the game has probably moved something."));
        Assert.Equal("Something else.", MeterView.StoppedLine("Something else."));
        Assert.Equal("An update to the plugin should bring it back.", MeterView.StoppedLine("The meter has stopped."));
    }

    [Fact]
    public void Pace_bar_puts_the_bigger_at_the_full_width()
    {
        Assert.Equal((1f, 0.5f), MeterView.PaceBar(new PaceLine(4, PaceAgainst.Best, "P2", 41, 200, 100, 1)));
        Assert.Equal((0.5f, 1f), MeterView.PaceBar(new PaceLine(4, PaceAgainst.Best, "P2", 41, 100, 200, -0.5)));
        Assert.Equal((0f, 0f), MeterView.PaceBar(new PaceLine(4, PaceAgainst.Best, "P2", 41, 0, 0, 0)));
    }

    [Fact]
    public void Recap_bar_runs_to_the_most_hp_seen()
    {
        var recap = new[] { new RecapLine(161, "Full HP", 0, 97210, ""), new RecapLine(166, "Embrace", 6120, 58590, ""), new RecapLine(171, "Exaflare", -104880, 86110, "") };
        Assert.Equal(97210, MeterView.RecapMax(recap));
        Assert.Equal(64710, MeterView.RecapMax(new[] { new RecapLine(1, "Cure", 6120, 58590, "") }));
    }
}

/// <summary>The small meter's sizes, its bands, its anchor and its menus (UI/Model/MeterLayout.cs).</summary>
public class MeterLayoutTests
{
    [Fact]
    public void The_normal_meter_is_the_mock_ups()
    {
        var z = MeterSizes.Normal;
        Assert.False(z.IsCompact);
        // 36 head + 27 tabs + 26 pace + 20 heads + 8 × 25 rows + 27 limit break + 28 foot.
        Assert.Equal(364, z.Needed(8, true));
        Assert.Equal(338, z.Needed(8, false));
        Assert.Equal(8, z.RowsIn(364, true));
        Assert.Equal(7, z.RowsIn(363 - 1, true));
        Assert.Equal(0, z.RowsIn(10, true));
    }

    [Fact]
    public void Compact_fits_more_rows_in_the_same_room_and_stays_readable()
    {
        var n = MeterSizes.Normal;
        var c = MeterSizes.Compact;
        Assert.True(c.IsCompact);
        Assert.Equal(0, c.Tabs);
        // Its own head, pace, heads, rows, limit break and foot are all smaller.
        Assert.True(c.Head < n.Head && c.Pace < n.Pace && c.Cols < n.Cols && c.Row < n.Row && c.LimitBreak < n.LimitBreak && c.Foot < n.Foot);
        // The eight and the limit break in 271px, not 364.
        Assert.Equal(271, c.Needed(8, true));
        // Where the normal meter shows 8, compact shows 12.
        Assert.Equal(12, c.RowsIn(n.Needed(8, true), true));
        // A row is one line of its name's words (Geist's line is 1.3 of its size) with room round it, and the spot fits in it.
        Assert.True(c.Row >= c.NamePx * 1.3f + 2);
        Assert.True(c.Spot <= c.Row - 2);
        Assert.True(c.ValuePx >= 12 && c.SecondPx >= 10 && c.JobPx >= 9);
        // "3,912,400" in mono 600 at 12px (0.6em a figure) fits its column.
        Assert.True(9 * 0.6f * c.ValuePx <= c.ValueCol);
        Assert.True(c.DeathRow >= c.Row);
        Assert.Same(c, MeterSizes.Of(true));
        Assert.Same(n, MeterSizes.Of(false));
    }

    [Fact]
    public void Bands_go_from_the_head_down_or_from_the_foot_up()
    {
        // Growing down: the head on top, then what's next to it; the foot at the bottom; rows from the head's side.
        var down = new MeterStack(100, 500, false);
        Assert.Equal(100, down.Near(36));
        Assert.Equal(136, down.Near(27));
        Assert.Equal(472, down.Far(28));
        Assert.Equal(163, down.NearEdge);
        Assert.Equal(472, down.FarEdge);
        Assert.Equal(309, down.Room);
        Assert.Equal(163, down.Row(0, 25));
        Assert.Equal(188, down.Row(1, 25));
        Assert.Equal(12, down.Fits(25));
        // The tabs' line on their far side (their foot), the foot's line on its head side (its top).
        Assert.Equal(162, down.Away(136, 27));
        Assert.Equal(472, down.Toward(472, 28));

        // Growing up: the same bands mirrored, the head at the foot, the party's line at the top.
        var up = new MeterStack(100, 500, true);
        Assert.True(up.Up);
        Assert.Equal(464, up.Near(36));
        Assert.Equal(437, up.Near(27));
        Assert.Equal(100, up.Far(28));
        Assert.Equal(437, up.NearEdge);
        Assert.Equal(128, up.FarEdge);
        Assert.Equal(309, up.Room);
        // The top player nearest the head: the first row just over the tabs, the next over it.
        Assert.Equal(412, up.Row(0, 25));
        Assert.Equal(387, up.Row(1, 25));
        Assert.Equal(12, up.Fits(25));
        Assert.Equal(437, up.Away(437, 27));
        Assert.Equal(127, up.Toward(100, 28));
    }

    [Fact]
    public void A_bit_of_slack_lets_the_last_row_in_as_it_always_did()
    {
        var s = new MeterStack(0, 99, false);
        Assert.Equal(4, s.Fits(25));
        Assert.Equal(3, new MeterStack(0, 98, false).Fits(25));
        Assert.Equal(0, new MeterStack(0, 10, false).Fits(0));
    }

    [Fact]
    public void The_anchor_is_the_corner_that_stays_put()
    {
        var pos = new Vector2(1500, 300);
        var size = new Vector2(330, 382);
        Assert.Equal(pos, MeterLayout.Corner(pos, size, MeterGrows.Down));
        Assert.Equal(new Vector2(1500, 682), MeterLayout.Corner(pos, size, MeterGrows.Up));
    }

    [Fact]
    public void Down_keeps_the_top_and_up_keeps_the_foot_as_the_height_changes()
    {
        var min = Vector2.Zero;
        var max = new Vector2(2560, 1440);
        // Folded (36) and open (364): growing down the top stays at 300; growing up the foot stays at 1200.
        Assert.Equal(new Vector2(1500, 300), MeterLayout.Place(new Vector2(1500, 300), new Vector2(330, 36), MeterGrows.Down, min, max));
        Assert.Equal(new Vector2(1500, 300), MeterLayout.Place(new Vector2(1500, 300), new Vector2(330, 364), MeterGrows.Down, min, max));
        Assert.Equal(new Vector2(1500, 1164), MeterLayout.Place(new Vector2(1500, 1200), new Vector2(330, 36), MeterGrows.Up, min, max));
        Assert.Equal(new Vector2(1500, 836), MeterLayout.Place(new Vector2(1500, 1200), new Vector2(330, 364), MeterGrows.Up, min, max));
    }

    [Fact]
    public void It_stays_on_the_screen_without_its_anchor_moving()
    {
        var min = Vector2.Zero;
        var max = new Vector2(1920, 1080);
        // Down near the bottom: opened up it would run off, so it's lifted just enough.
        Assert.Equal(new Vector2(1500, 716), MeterLayout.Place(new Vector2(1500, 1000), new Vector2(330, 364), MeterGrows.Down, min, max));
        // Up near the top: pushed down just enough.
        Assert.Equal(new Vector2(1500, 0), MeterLayout.Place(new Vector2(1500, 100), new Vector2(330, 364), MeterGrows.Up, min, max));
        // Off the right edge: back in.
        Assert.Equal(new Vector2(1590, 300), MeterLayout.Place(new Vector2(1800, 300), new Vector2(330, 364), MeterGrows.Down, min, max));
        // Taller than the screen: its head's edge on it first (the top growing down, the foot growing up).
        Assert.Equal(0, MeterLayout.Place(new Vector2(10, 500), new Vector2(330, 1400), MeterGrows.Down, min, max).Y);
        Assert.Equal(1080 - 1400, MeterLayout.Place(new Vector2(10, 500), new Vector2(330, 1400), MeterGrows.Up, min, max).Y);
        // An anchor off the game's screen (saved at a bigger resolution) is brought back onto it.
        Assert.Equal(new Vector2(1590, 716), MeterLayout.Place(new Vector2(2100, 1300), new Vector2(330, 364), MeterGrows.Down, min, max));
        Assert.Equal(new Vector2(1590, 716), MeterLayout.Place(new Vector2(2100, 1300), new Vector2(330, 364), MeterGrows.Up, min, max));
        // With multi-monitor windows on, it's on another monitor: left alone.
        Assert.Equal(new Vector2(2100, 900), MeterLayout.Place(new Vector2(2100, 900), new Vector2(330, 364), MeterGrows.Down, min, max, true));
        Assert.Equal(new Vector2(-400, 836), MeterLayout.Place(new Vector2(-400, 1200), new Vector2(330, 364), MeterGrows.Up, min, max, true));
        // On the game's screen, kept on it either way.
        Assert.Equal(new Vector2(1500, 716), MeterLayout.Place(new Vector2(1500, 1000), new Vector2(330, 364), MeterGrows.Down, min, max, true));
        // The main viewport needn't start at 0 (a work area under a top bar).
        Assert.Equal(new Vector2(100, 40), MeterLayout.Place(new Vector2(100, 40), new Vector2(330, 36), MeterGrows.Down, new Vector2(0, 40), max));
        Assert.Equal(new Vector2(100, 40), MeterLayout.Place(new Vector2(100, 60), new Vector2(330, 364), MeterGrows.Up, new Vector2(0, 40), max));
    }

    [Fact]
    public void A_fights_height_is_what_the_party_needs_at_most_what_you_gave_it()
    {
        // Never sized: what the eight need, the title bar's 26 on top.
        Assert.Equal(390, MeterLayout.FightHeight(364, 26, 0, 60, 1400));
        // Sized to 300: no taller, the rows past it cut.
        Assert.Equal(300, MeterLayout.FightHeight(364, 26, 300, 60, 1400));
        // Sized taller than four need: as tall as they need, so a bigger party grows it up to 500.
        Assert.Equal(26 + MeterSizes.Normal.Needed(4, true), MeterLayout.FightHeight(MeterSizes.Normal.Needed(4, true), 26, 500, 60, 1400));
        Assert.Equal(390, MeterLayout.FightHeight(364, 26, 500, 60, 1400));
        // Within the window's limits.
        Assert.Equal(60, MeterLayout.FightHeight(10, 0, 0, 60, 1400));
        Assert.Equal(1400, MeterLayout.FightHeight(5000, 0, 0, 60, 1400));
    }

    [Fact]
    public void Menus_open_away_from_the_anchored_edge_unless_there_is_no_room()
    {
        // The head on top: down; at the foot: up.
        Assert.False(MeterLayout.MenuUp(false, 300, 800, 400));
        Assert.True(MeterLayout.MenuUp(true, 800, 300, 400));
        // No room that way and more the other: flipped.
        Assert.True(MeterLayout.MenuUp(false, 800, 100, 400));
        Assert.False(MeterLayout.MenuUp(true, 100, 800, 400));
        // No room either way: kept on its side when the other has no more.
        Assert.True(MeterLayout.MenuUp(true, 200, 150, 400));
    }

    [Fact]
    public void A_menu_sits_by_its_button_inside_the_meter_and_the_screen()
    {
        // The picker (300) on a 330 meter at x 1000: 16 in (the mock-up's 14, as far right as it goes).
        Assert.Equal(1016, MeterLayout.MenuLeft(1227, 1000, 1330, 300, 14, 0, 1920));
        // Compact's tab menu (200) by its button, kept 14 inside the meter's right.
        Assert.Equal(1116, MeterLayout.MenuLeft(1221, 1000, 1330, 200, 14, 0, 1920));
        Assert.Equal(1080, MeterLayout.MenuLeft(1080, 1000, 1330, 200, 14, 0, 1920));
        // Wider than the meter: 14 in from its left.
        Assert.Equal(1014, MeterLayout.MenuLeft(1100, 1000, 1260, 300, 14, 0, 1920));
        // On the screen whatever the meter does.
        Assert.Equal(0, MeterLayout.MenuLeft(-50, -100, 160, 300, 14, 0, 1920));
        Assert.Equal(1620, MeterLayout.MenuLeft(1800, 1700, 2030, 300, 14, 0, 1920));
    }

    [Fact]
    public void The_tab_menu_lists_the_five_tabs_in_the_tab_rows_order()
    {
        Assert.Equal(new[] { MeterTab.Damage, MeterTab.Healing, MeterTab.Taken, MeterTab.Deaths, MeterTab.Events }, MeterLayout.TabOrder);
        Assert.Equal(Enum.GetValues<MeterTab>(), MeterLayout.TabOrder);
        Assert.Equal(new[] { "Damage", "Healing", "Taken", "Deaths", "Events" }, MeterLayout.TabOrder.Select(MeterLayout.TabWord));
        Assert.Equal("Damage", MeterLayout.TabWord((MeterTab)9));
        // A tab a newer version saved is Damage here; the ones this version has stay.
        Assert.Equal(MeterTab.Damage, MeterLayout.Known((MeterTab)9));
        Assert.Equal(MeterTab.Events, MeterLayout.Known(MeterTab.Events));
    }
}

/// <summary>The meter's sample fight (UI/Model/MeterPreview.cs): it looks like a pull and moves like one.</summary>
public class MeterSampleTests
{
    private sealed class Clock
    {
        public double Now = 1000;
    }

    private static (MeterPreview Sample, Clock Clock) Make()
    {
        var c = new Clock();
        return (new MeterPreview(() => c.Now), c);
    }

    [Fact]
    public void It_starts_mid_pull_with_the_eight_and_the_limit_break()
    {
        var (s, _) = Make();
        var e = s.Current!;
        Assert.True(s.InEncounter);
        Assert.False(s.Waiting);
        Assert.Null(s.Problem);
        Assert.True(e.Live);
        Assert.Null(e.Ended);
        Assert.Equal(MeterPreview.Begins, e.Duration);
        Assert.Equal("2:15", Format.Clock(e.Duration));
        Assert.Equal(8, e.Rows.Count(r => !r.IsLimitBreak));
        Assert.Single(e.Rows, r => r.IsLimitBreak);
        Assert.Equal(new[] { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" }, e.Rows.Where(r => !r.IsLimitBreak).Select(r => r.Slot));
        Assert.Single(e.Rows, r => r.IsYou);
        Assert.Equal("P1", e.Phase);
        Assert.Empty(e.Deaths);
    }

    [Fact]
    public void Its_numbers_add_up()
    {
        foreach (var t in new[] { 30.0, 135, 160, 171.3, 191, 205, 211 })
        {
            var e = MeterPreview.PullAt(t);
            var players = e.Rows.Where(r => !r.IsLimitBreak).ToList();
            // Each row's whole number is rounded on its own: the party's is within a few of their sum.
            Assert.InRange(e.Party.Damage - e.Rows.Sum(r => r.Damage), -9, 9);
            Assert.Equal(1.0, e.Rows.Sum(r => r.DamageShare), 6);
            Assert.Equal(1.0, players.Sum(r => r.HealingShare), 6);
            Assert.Equal(1.0, players.Sum(r => r.TakenShare), 6);
            Assert.Equal(e.Party.EncDps, e.Party.Damage / Math.Max(1, t), 0);
            foreach (var r in players) Assert.True(r.Dps >= r.EncDps - 0.001);
        }
        // At 3:11 the rows are the mock-ups': Rook's 27,840 ENCDPS, Odette's 9,050 (she died at 2:51), the party's own.
        var at = MeterPreview.PullAt(191);
        Assert.Equal(27840, at.Rows.Single(r => r.Slot == "M1").EncDps, 0);
        Assert.Equal(9050, at.Rows.Single(r => r.Slot == "H2").EncDps, 0);
        Assert.InRange(at.Rows.Single(r => r.Slot == "MT").Taken, 3912398, 3912402);
        // Before 0:58 there's no limit break yet.
        Assert.DoesNotContain(MeterPreview.PullAt(40).Rows, r => r.IsLimitBreak);
    }

    [Fact]
    public void Its_numbers_move_and_never_go_back()
    {
        var (s, c) = Make();
        var first = s.Current!;
        // Four snapshots a second: the same one within a quarter, a new one after.
        c.Now += 0.1;
        Assert.Same(first, s.Current);
        c.Now += 0.2;
        var next = s.Current!;
        Assert.NotSame(first, next);
        Assert.Equal(MeterPreview.Begins + 0.25, next.Duration);
        Assert.Same(next, s.Kept[0]);
        Assert.NotEqual(first.Rows[4].EncDps, next.Rows[4].EncDps);
        // Damage, healing and damage taken only ever grow; ENCDPS rises and falls.
        var last = MeterPreview.PullAt(0.25);
        var rises = 0;
        var falls = 0;
        for (var t = 0.5; t <= MeterPreview.Ends; t += 0.25)
        {
            var now = MeterPreview.PullAt(t);
            for (var i = 0; i < now.Rows.Count && i < last.Rows.Count; i++)
            {
                Assert.True(now.Rows[i].Damage >= last.Rows[i].Damage);
                Assert.True(now.Rows[i].Healing >= last.Rows[i].Healing);
                Assert.True(now.Rows[i].Taken >= last.Rows[i].Taken);
                if (now.Rows[i].EncDps > last.Rows[i].EncDps) rises++;
                else if (now.Rows[i].EncDps < last.Rows[i].EncDps) falls++;
            }
            last = now;
        }
        Assert.True(rises > 100 && falls > 100);
    }

    [Fact]
    public void Odette_dies_then_Fenn_then_the_wipe()
    {
        var before = MeterPreview.PullAt(171);
        Assert.Empty(before.Deaths);
        var after = MeterPreview.PullAt(172);
        var odette = after.Rows.Single(r => r.Slot == "H2");
        Assert.True(odette.Dead);
        Assert.Equal(1, odette.Deaths);
        Assert.Equal("Exaflare", Assert.Single(MeterView.Deaths(after)).KilledBy);
        Assert.Equal(1, after.Party.Deaths);
        // Dead, she does nothing more: her damage stays where it was.
        Assert.Equal(odette.Damage, MeterPreview.PullAt(190).Rows.Single(r => r.Slot == "H2").Damage);
        // Fenn at 3:22, then everyone in the wipe at 3:29–3:31, which counts against no one.
        var end = MeterPreview.PullAt(MeterPreview.Ends);
        Assert.False(end.Live);
        Assert.Equal(Outcome.Wipe, end.Ended);
        Assert.All(end.Rows.Where(r => !r.IsLimitBreak), r => Assert.True(r.Dead));
        Assert.Equal(new[] { "H2", "R1" }, MeterView.Deaths(end).Select(d => d.Slot));
        Assert.Equal(8, end.Deaths.Count);
        Assert.Equal(2, end.Party.Deaths);
        Assert.True(MeterView.KillingLine(MeterView.Deaths(end)[1]) >= 0);
    }

    [Fact]
    public void It_paces_against_pull_4_from_ten_seconds_into_each_phase()
    {
        var (s, c) = Make();
        var p1 = s.Pace(12, PaceAgainst.Best)!;
        Assert.Equal(4, p1.AgainstPull);
        Assert.Equal("P1", p1.Phase);
        Assert.InRange(p1.Ahead, -0.05, 0.1);
        Assert.Equal(11, s.Pace(12, PaceAgainst.Last)!.AgainstPull);
        Assert.Null(s.Pace(12, PaceAgainst.Off));
        Assert.Null(s.Pace(11, PaceAgainst.Best));
        // P2 begins at 2:30: no pace for its first ten seconds.
        c.Now += MeterPreview.PhaseTwo - MeterPreview.Begins + 5;
        Assert.Equal("P2", s.Current!.Phase);
        Assert.Null(s.Pace(12, PaceAgainst.Best));
        c.Now += 6;
        var p2 = s.Pace(12, PaceAgainst.Best)!;
        Assert.Equal("P2", p2.Phase);
        Assert.Equal(11, p2.PhaseSeconds);
        Assert.True(p2.Damage > 0 && p2.AgainstDamage > 0);
        Assert.Equal("P2 · 0:11 in", MeterView.Where(s.Current!));
    }

    [Fact]
    public void It_stays_on_the_wipe_then_plays_again()
    {
        var (s, c) = Make();
        c.Now += MeterPreview.Ends - MeterPreview.Begins + 1;
        Assert.False(s.InEncounter);
        Assert.Equal(Outcome.Wipe, s.Current!.Ended);
        Assert.Equal(MeterPreview.Ends, s.Current!.Duration);
        Assert.Null(s.Pace(12, PaceAgainst.Best));
        // A second into the wipe, then the 8 it stays there: a second into playing again.
        c.Now += MeterPreview.Holds;
        Assert.True(s.InEncounter);
        Assert.Equal(MeterPreview.Begins + 1, s.Current!.Duration);
        Assert.True(s.Current!.Live);
    }

    [Fact]
    public void Its_earlier_pulls_are_kept_for_the_picker()
    {
        var (s, _) = Make();
        Assert.Equal(new[] { 12, 11, 10, 9, 4 }, s.Kept.Select(k => k.Pull));
        Assert.Equal(4, s.Best(1)!.Pull);
        Assert.Equal(11, s.Get(11)!.Pull);
        Assert.Same(s.Current, s.Get(12));
        Assert.Null(s.Get(99));
        var (here, _) = MeterView.Picker(s.Kept, s.Current, s.Best(1));
        Assert.Equal(new[] { 12, 11, 10, 9, 4 }, here.Select(r => r.Fight.Pull));
        Assert.True(here[0].Live);
        Assert.True(here[^1].Best);
    }

    [Fact]
    public void A_players_abilities_follow_their_damage_and_are_worked_out_once()
    {
        var (s, _) = Make();
        var rook = s.Current!.Rows.Single(r => r.Slot == "M1");
        var a = s.Abilities(12, rook.PartyId, MeterTab.Damage);
        Assert.Same(a, s.Abilities(12, rook.PartyId, MeterTab.Damage));
        Assert.Equal(12, a.Count);
        Assert.Equal((long)Math.Round(rook.Damage * 0.125), a[0].Amount);
        Assert.Contains(a, x => x.IsTick && x.CritRate == null);
        Assert.Empty(s.Abilities(12, rook.PartyId, MeterTab.Healing));
    }
}

public class PullViewTests
{
    [Fact]
    public void Phases_begin_when_every_enemy_was_gone_long_enough()
    {
        var events = new[]
        {
            new TargetEvent(0, 101, true), new TargetEvent(60, 101, false), new TargetEvent(64, 101, true),
            new TargetEvent(150, 101, false), new TargetEvent(160, 102, true),
        };
        Assert.Equal(new[] { 0d, 160 }, PullView.PhaseStarts(events));
        Assert.Equal(new[] { 0d }, PullView.PhaseStarts(Array.Empty<TargetEvent>()));
    }

    [Fact]
    public void The_recording_box_counts_each_tether_put_on()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", _ => null);
        Assert.Equal(0, b.TethersSeen);
        b.Tethers(10, new[] { (101, 2, 84u) });
        b.Tethers(11, new[] { (101, 2, 84u) });
        Assert.Equal(1, b.TethersSeen);
        b.Tethers(12, new[] { (101, 2, 84u), (101, 3, 84u) });
        b.Tethers(13, Array.Empty<(int, int, uint)>());
        b.Tethers(14, new[] { (101, 2, 84u) });
        Assert.Equal(3, b.TethersSeen);
        // As a pull's screen counts them from the file: each one put on.
        Assert.Equal(3, PullView.Build(b.Finish(20, Outcome.Wipe)).Tethers);
    }

    [Fact]
    public void The_wipe_counts_back_from_the_last_death_not_the_recordings_end()
    {
        // The recording runs on 12 s past the last death while the duty calls the wipe: the deaths in
        // the 6 s before the last one are still the wipe (as the summary, the meter and the site say).
        var r = new Recording { Duration = 212, Outcome = Outcome.Wipe };
        r.Party.Add(new PartyMember(1, "MT", "PLD", "tank"));
        r.Party.Add(new PartyMember(2, "H2", "SCH", "healer"));
        r.Party.Add(new PartyMember(3, "R1", "BRD", "dps"));
        r.Events.Add(new DeathEvent(171.3, 2, 0, 0));
        r.Events.Add(new DeathEvent(195, 3, 0, 0));
        r.Events.Add(new DeathEvent(200, 1, 0, 0));
        var view = PullView.Build(r);
        Assert.Equal(new[] { 171.3 }, view.Deaths);
        Assert.Equal(2, view.WipeDeaths);
        Assert.Equal(new[] { "H2" }, view.Rows.Where(x => x.Death).Select(x => x.DeathSlot));

        // A pull that didn't end in a wipe has no wipe: every death is someone's.
        r.Outcome = Outcome.Clear;
        Assert.Equal(3, PullView.Build(r).Deaths.Count);
    }
}

public class PlanCalloutTests
{
    private static PlanStep Step(string name, string? callout = null) =>
        new(name, "2:44", "Two platforms.", Array.Empty<BoardObj>(), null) { Callout = callout };

    [Fact]
    public void The_big_words_are_the_callout_else_the_name()
    {
        Assert.Equal("Middle, step through the first", Step("P2 · Exaflares", "Middle, step through the first").Big);
        Assert.Equal("Exaflares", Step("P2 · Exaflares").Big);
        Assert.Equal("Exaflares", Step("Exaflares", "  ").Big);
    }

    [Fact]
    public void Chat_says_the_name_then_the_callout()
    {
        Assert.Equal("Exaflares: middle, step through the first.", PlanJson.ChatLine(Step("P2 · Exaflares", "Middle, step through the first")));
        Assert.Equal("Spread: MT soaks!", PlanJson.ChatLine(Step("Spread", "MT soaks!")));
        Assert.Equal("Exaflares", PlanJson.ChatLine(Step("Exaflares")));
        Assert.Equal("Out", PlanJson.ChatLine(Step("Out", "out")));
    }

    [Fact]
    public void Phases_come_from_the_steps_names()
    {
        Assert.Equal("P2", PlanJson.PhaseOf("P2 · Exaflares"));
        Assert.Equal("P3", PlanJson.PhaseOf("Phase 3: Adds"));
        Assert.Null(PlanJson.PhaseOf("Exaflares"));
        Assert.Equal("Exaflares", PlanJson.WithoutPhase("P2 · Exaflares"));
        var steps = new[] { Step("Opener"), Step("Raidwide"), Step("P2 · Exaflares"), Step("Towers"), Step("P3 — Adds") };
        Assert.Equal(new[] { "P1", "P1", "P2", "P2", "P3" }, PlanJson.Phases(steps));
        Assert.All(PlanJson.Phases(new[] { Step("A"), Step("B") }), p => Assert.Null(p));
    }

    [Fact]
    public void Reads_a_steps_callout_and_the_boards_new_kinds()
    {
        const string json = """
            {
              "id": "p", "name": "Plan",
              "arena": { "shape": "circle", "yalms": 25, "edge": "death", "grid": "radial" },
              "phases": [ { "id": "a", "name": "P2 · Exaflares", "callout": "  Middle, step through the first ", "notes": "", "objects": [
                { "type": "moving", "x": 0, "y": 0, "size": 40, "reach": 200, "spacing": 50, "order": 2 },
                { "type": "gaze", "x": 10, "y": 10 },
                { "type": "knockback", "x": 10, "y": 10 },
                { "type": "tower", "x": 10, "y": 10, "count": 2 },
                { "type": "pen", "x": 0, "y": 0, "points": [0, 0, 10, 10, 20, 0] }
              ] } ]
            }
            """;
        var plan = PlanJson.Read(json)!;
        var s = plan.Steps[0];
        Assert.Equal("Middle, step through the first", s.Callout);
        Assert.Equal(25, plan.Arena.Yalms);
        Assert.Equal("death", plan.Arena.Edge);
        Assert.Equal("radial", plan.Arena.Grid);
        Assert.Equal(new[] { BoardKind.Moving, BoardKind.Gaze, BoardKind.Knockback, BoardKind.Tower, BoardKind.Pen }, s.Objects.Select(o => o.Kind));
        Assert.Equal(2, s.Objects[0].Order);
        Assert.Equal(200, s.Objects[0].Reach);
        Assert.Equal(50, s.Objects[0].Spacing);
        Assert.Equal(6, s.Objects[4].Points!.Length);
        Assert.Equal(2, s.Objects[3].Count);
    }
}
