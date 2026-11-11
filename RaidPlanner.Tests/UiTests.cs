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
