using System.Numerics;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>The party cooldown tracker's logic (UI/Model/Cooldowns.cs): the catalogue, the book that learns recasts, the rows.</summary>
public class CooldownTests
{
    [Fact]
    public void The_catalogue_has_one_entry_a_name_and_only_real_jobs()
    {
        var jobs = new HashSet<string> { "PLD", "WAR", "DRK", "GNB", "WHM", "SCH", "AST", "SGE", "MNK", "DRG", "NIN", "SAM", "RPR", "VPR", "BRD", "MCH", "DNC", "BLM", "SMN", "RDM", "PCT" };
        var names = CooldownCatalog.All.SelectMany(s => s.Names).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(CooldownCatalog.All.Count, CooldownCatalog.All.Select(s => s.Key).Distinct().Count());
        foreach (var s in CooldownCatalog.All)
        {
            Assert.Equal(s.Key, s.Names[0]);
            Assert.NotEmpty(s.Jobs);
            Assert.All(s.Jobs, j => Assert.Contains(j, jobs));
            Assert.Same(s, CooldownCatalog.Find(s.Key.ToLowerInvariant()));
        }
        // Every kind has some, in the tracker's order (the catalogue is by kind).
        Assert.Equal(CooldownCatalog.Kinds, CooldownCatalog.All.Select(s => s.Kind).Distinct());
        Assert.True(CooldownCatalog.Find("Reprisal")!.For("gnb"));
        Assert.False(CooldownCatalog.Find("Reprisal")!.For("WHM"));
        Assert.Contains("Mug", CooldownCatalog.Find("Dokumori")!.Names);
    }

    [Fact]
    public void What_is_tracked_is_its_default_unless_picked()
    {
        var picks = new Dictionary<string, bool> { ["Reprisal"] = false, ["Provoke"] = true };
        Assert.False(CooldownCatalog.IsOn(CooldownCatalog.Find("Reprisal")!, picks));
        Assert.True(CooldownCatalog.IsOn(CooldownCatalog.Find("Provoke")!, picks));
        Assert.True(CooldownCatalog.IsOn(CooldownCatalog.Find("Addle")!, picks));
        Assert.False(CooldownCatalog.IsOn(CooldownCatalog.Find("Shirk")!, null));
        var (on, of) = CooldownCatalog.Count(CooldownKind.Tank, picks);
        Assert.Equal((1, 6), (on, of));
        Assert.Equal(12, CooldownCatalog.Count(CooldownKind.RaidBuff, null).On);
    }

    [Fact]
    public void Unused_is_ready_and_a_use_counts_down_by_the_recast()
    {
        var book = new CooldownBook();
        var s = book.State(7, "Reprisal", 10, 60, 1);
        Assert.Equal(0, s.ReadyIn);
        Assert.False(s.Seen);
        Assert.Equal(1, s.Charges);
        book.Use(7, "Reprisal", 100, 60, 1);
        s = book.State(7, "Reprisal", 110, 60, 1);
        Assert.Equal(50, s.ReadyIn, 6);
        Assert.Equal(0, s.Charges);
        Assert.True(s.Seen);
        Assert.False(s.Learned);
        Assert.Equal(0, book.State(7, "Reprisal", 160, 60, 1).ReadyIn);
        Assert.Equal(1, book.State(7, "Reprisal", 160, 60, 1).Charges);
        // Someone else's is their own.
        Assert.Equal(0, book.State(8, "Reprisal", 110, 60, 1).ReadyIn);
    }

    [Fact]
    public void Two_uses_closer_than_the_recast_teach_the_real_one()
    {
        // Troubadour: the sheet says 120, the trait makes it 90.
        var book = new CooldownBook();
        book.Use(3, "Troubadour", 10, 120, 1);
        Assert.Equal(30, book.State(3, "Troubadour", 100, 120, 1).ReadyIn, 6);
        book.Use(3, "Troubadour", 100, 120, 1);
        var s = book.State(3, "Troubadour", 110, 120, 1);
        Assert.True(s.Learned);
        Assert.Equal(90, s.Recast, 6);
        Assert.Equal(80, s.ReadyIn, 6);
        // Used again a second early (within the slack): nothing more is learned.
        book.Use(3, "Troubadour", 189.5, 120, 1);
        Assert.Equal(90, book.State(3, "Troubadour", 190, 120, 1).Recast, 6);
    }

    [Fact]
    public void The_same_use_seen_twice_is_one()
    {
        var book = new CooldownBook();
        book.Use(4, "Fey Illumination", 50, 120, 1);
        book.Use(4, "Fey Illumination", 50.4, 120, 1);
        var s = book.State(4, "Fey Illumination", 60, 120, 1);
        Assert.False(s.Learned);
        Assert.Equal(110, s.ReadyIn, 6);
    }

    [Fact]
    public void Charges_come_back_one_at_a_time()
    {
        var book = new CooldownBook();
        book.Use(2, "Oblation", 0, 60, 2);
        var s = book.State(2, "Oblation", 10, 60, 2);
        Assert.Equal((1, 0.0), (s.Charges, s.ReadyIn));
        Assert.Equal(50, s.NextIn, 6);
        book.Use(2, "Oblation", 20, 60, 2);
        s = book.State(2, "Oblation", 30, 60, 2);
        Assert.Equal(0, s.Charges);
        Assert.Equal(30, s.ReadyIn, 6);
        Assert.False(s.Learned);
        s = book.State(2, "Oblation", 70, 60, 2);
        Assert.Equal(1, s.Charges);
        Assert.Equal(50, s.NextIn, 6);
        Assert.Equal(2, book.State(2, "Oblation", 125, 60, 2).Charges);
        book.Clear();
        Assert.Equal(0, book.Count);
    }

    private static CooldownAction? Act(string key) => new(key, 1, key, 0, CooldownCatalog.Find(key)!.Kind == CooldownKind.RaidBuff ? 120 : 90, 1);

    private static readonly CooldownMember[] Party =
    {
        new(1, "MT", "PLD", 1, true), new(2, "OT", "WAR", 1, false), new(3, "M1", "DRG", 2, false), new(4, "R1", "BRD", 3, false),
    };

    [Fact]
    public void Rows_are_each_members_by_kind_then_spot_yours_exact()
    {
        var book = new CooldownBook();
        book.Use(2, "Reprisal", 0, 90, 1);
        OwnCooldown? Own(string key) => key == "Reprisal" ? new OwnCooldown(12, 12, 0, 1, 60) : key == "Divine Veil" ? new OwnCooldown(0, 0, 1, 1, 90) : null;
        var lines = CooldownView.Lines(Party, Act, null, book, Own, 30);
        // Raid buffs first (the DRG's, the BRD's two), then mitigation (Reprisal MT then OT, Feint, Troubadour, Divine Veil, Passage, Shake It Off…).
        Assert.Equal(CooldownKind.RaidBuff, lines[0].Spec.Kind);
        Assert.Equal(new[] { "Battle Litany", "Battle Voice", "Radiant Finale" }, lines.TakeWhile(l => l.Spec.Kind == CooldownKind.RaidBuff).Select(l => l.Spec.Key));
        var reprisal = lines.Where(l => l.Spec.Key == "Reprisal").ToList();
        Assert.Equal(new[] { "MT", "OT" }, reprisal.Select(l => l.Who.Slot));
        Assert.True(reprisal[0].Exact);
        Assert.Equal(12, reprisal[0].ReadyIn);
        Assert.Equal(60, reprisal[0].Recast);
        Assert.False(reprisal[1].Exact);
        Assert.Equal(60, reprisal[1].ReadyIn, 6);
        Assert.Equal(1 - 60 / 90.0, reprisal[1].Fill, 3);
        Assert.Contains(lines, l => l.Spec.Key == "Divine Veil" && l.Exact && l.Ready);
        Assert.DoesNotContain(lines, l => l.Spec.Key == "Swiftcast");
        // Without yours, or only what's recharging.
        Assert.DoesNotContain(CooldownView.Lines(Party, Act, null, book, Own, 30, you: false), l => l.Who.IsYou);
        var busy = CooldownView.Lines(Party, Act, null, book, Own, 30, hideReady: true);
        Assert.All(busy, l => Assert.False(l.Ready));
        Assert.Equal(2, busy.Count);
        // Picked off, gone; an action the game doesn't have, no rows.
        Assert.DoesNotContain(CooldownView.Lines(Party, Act, new Dictionary<string, bool> { ["Feint"] = false }, book, Own, 30), l => l.Spec.Key == "Feint");
        Assert.Empty(CooldownView.Lines(Party, _ => null, null, book, Own, 30));
    }

    [Theory]
    [InlineData(0, "Ready")]
    [InlineData(-3, "Ready")]
    [InlineData(0.2, "1")]
    [InlineData(41.01, "42")]
    [InlineData(59.5, "1:00")]
    [InlineData(65, "1:05")]
    public void The_time_to_ready_reads_as_a_count(double readyIn, string text) => Assert.Equal(text, CooldownView.Time(readyIn));

    [Fact]
    public void Charges_and_how_its_known_are_said()
    {
        var spec = CooldownCatalog.Find("Reprisal")!;
        var a = new CooldownAction("Reprisal", 7535, "Reprisal", 0, 60, 2);
        var line = new CooldownLine(Party[1], spec, a, 0, 20, 1, 2, 60, false, false, true);
        Assert.Equal("1/2", CooldownView.Charges(line));
        Assert.Null(CooldownView.Charges(line with { MaxCharges = 1 }));
        Assert.Contains("estimate", CooldownView.Tip(line));
        Assert.StartsWith("Reprisal · OT · WAR.", CooldownView.Tip(line));
        Assert.Contains("learned", CooldownView.Tip(line with { Learned = true }));
        Assert.Contains("game's own timer", CooldownView.Tip(line with { Exact = true, Who = Party[0] }));
        Assert.Contains("taken to be ready", CooldownView.Tip(line with { Seen = false }));
    }

    [Fact]
    public void The_sample_recharges_and_comes_back()
    {
        var a = CooldownSample.At(0, null);
        var b = CooldownSample.At(30, null);
        Assert.NotEmpty(a);
        Assert.Equal(a.Count, b.Count);
        Assert.Contains(a, l => l.Ready);
        Assert.Contains(a, l => !l.Ready);
        Assert.NotEqual(a.Select(l => l.ReadyIn), b.Select(l => l.ReadyIn));
        // In kind order, then spot.
        Assert.Equal(a.Select(l => (int)l.Spec.Kind).OrderBy(k => k), a.Select(l => (int)l.Spec.Kind));
        Assert.All(CooldownSample.At(5, null, hideReady: true), l => Assert.False(l.Ready));
        Assert.DoesNotContain(CooldownSample.At(5, null, you: false), l => l.Who.IsYou);
    }
}

/// <summary>The on-screen callouts' timeline (UI/Model/CalloutTimeline.cs).</summary>
public class CalloutTests
{
    private static Plan Make(params (string Name, string? Callout)[] steps) =>
        new("p1", "Plan", "s", PlanArena.Default,
            steps.Select(s => new PlanStep(s.Name, string.Empty, string.Empty, Array.Empty<BoardObj>(), null) { Callout = s.Callout }).ToList(),
            new PlanGame(1, 1, "Arena", new[] { new PlanCue(0, 10, 12), new PlanCue(1, 11, 30), new PlanCue(1, 12, 26), new PlanCue(3, 13, 41) }));

    private static readonly Plan P = Make(("P1 · Opener", null), ("Exaflares", "Middle, step through"), ("No cue", null), ("P2 · Tower soaks", "Towers"));

    [Fact]
    public void A_steps_time_is_its_earliest_cue()
    {
        var t = CalloutTimeline.StepTimes(P.Game!.Cues, P.Steps.Count);
        Assert.Equal(12, t[0]);
        Assert.Equal(26, t[1]);
        Assert.True(double.IsNaN(t[2]));
        Assert.Equal(41, t[3]);
        Assert.All(CalloutTimeline.StepTimes(null, 2), d => Assert.True(double.IsNaN(d)));
        // A cue for a step that isn't there is left out.
        Assert.Single(CalloutTimeline.StepTimes(new[] { new PlanCue(5, 1, 3), new PlanCue(0, 1, 4) }, 1));
    }

    [Fact]
    public void The_next_steps_within_the_window_soonest_first()
    {
        var times = CalloutTimeline.StepTimes(P.Game!.Cues, P.Steps.Count);
        var next = CalloutTimeline.Upcoming(P, times, 10, 20, 3);
        Assert.Equal(new[] { 0, 1 }, next.Select(l => l.Step));
        Assert.Equal(2, next[0].In, 6);
        Assert.Equal("Opener", next[0].Big);
        Assert.Null(next[0].Name);
        Assert.Equal("Middle, step through", next[1].Big);
        Assert.Equal("Exaflares", next[1].Name);
        // A step that just went off stays as Now for two seconds.
        var now = CalloutTimeline.Upcoming(P, times, 13.5, 20, 3);
        Assert.True(now[0].Now);
        Assert.Equal(new[] { 0, 1 }, now.Select(l => l.Step));
        Assert.Equal(new[] { 1, 3 }, CalloutTimeline.Upcoming(P, times, 14.5, 30, 3).Select(l => l.Step));
        Assert.Single(CalloutTimeline.Upcoming(P, times, 10, 20, 1));
        Assert.Empty(CalloutTimeline.Upcoming(P, times, 50, 20, 3));
        Assert.Empty(CalloutTimeline.Upcoming(P, times, double.NaN, 20, 3));
    }

    [Theory]
    [InlineData(0, "Now")]
    [InlineData(-1, "Now")]
    [InlineData(0.3, "1")]
    [InlineData(7.5, "8")]
    [InlineData(75, "1:15")]
    public void The_countdown_reads_as_seconds(double s, string text) => Assert.Equal(text, CalloutTimeline.Countdown(s));

    [Fact]
    public void Each_steps_sound_plays_once_a_pull_never_late()
    {
        var times = CalloutTimeline.StepTimes(P.Game!.Cues, P.Steps.Count);
        var sounds = new CalloutSounds();
        Assert.Null(sounds.Due(P, times, 5, 3));
        Assert.Equal(0, sounds.Due(P, times, 9.5, 3));
        Assert.Null(sounds.Due(P, times, 9.6, 3));
        // The clock jumped past step 1 (a match moved the plan on): it's never played late.
        Assert.Null(sounds.Due(P, times, 27, 3));
        Assert.Equal(3, sounds.Due(P, times, 38.5, 3));
        Assert.Null(sounds.Due(P, times, 39, 3));
        // The same plan read again keeps what's been heard; a new pull plays them again.
        var again = Make(("P1 · Opener", null), ("Exaflares", "Middle, step through"), ("No cue", null), ("P2 · Tower soaks", "Towers"));
        Assert.Null(sounds.Due(again, times, 39, 3));
        sounds.Reset();
        Assert.Equal(0, sounds.Due(again, times, 10, 3));
    }

    [Fact]
    public void The_sample_counts_down_and_loops()
    {
        var a = CalloutSample.At(0, 15, 3);
        Assert.Equal(2, a.Count);
        Assert.Equal(6, a[0].In, 6);
        Assert.True(CalloutSample.At(6.5, 15, 3)[0].Now);
        Assert.Equal(a.Select(l => l.In), CalloutSample.At(CalloutSample.Loop, 15, 3).Select(l => l.In));
        Assert.Single(CalloutSample.At(0, 15, 1));
    }
}

/// <summary>The meter's 0.4.0 words (UI/Model/MeterText.cs) and the sample's events and gauge.</summary>
public class MeterTextTests
{
    [Fact]
    public void Copy_as_text_is_a_mini_parse_a_line_each()
    {
        var e = MeterPreview.PullAt(191);
        var lines = MeterText.Parse(e, false).Split('\n');
        Assert.Equal($"Example Arena · Pull 12 · 3:11 · So far in P2 · Party {Format.Thousands((long)Math.Round(e.Party.EncDps))} ENCDPS", lines[0]);
        // Eight players, biggest first, then the limit break.
        Assert.Equal(10, lines.Length);
        Assert.StartsWith("Rook (DRG) 27,840 · ", lines[1]);
        Assert.EndsWith("% · 0 deaths", lines[1]);
        Assert.Contains(lines, l => l.StartsWith("Odette (SCH) 9,050 · ", StringComparison.Ordinal) && l.EndsWith("· 1 death", StringComparison.Ordinal));
        Assert.StartsWith("Limit break ", lines[^1]);
        // Jobs only: the spot and the job, no names.
        var jobs = MeterText.Parse(e, true);
        Assert.DoesNotContain("Rook", jobs);
        Assert.Contains("M1 DRG 27,840", jobs);
        // Over, a wipe in P2.
        Assert.StartsWith("Example Arena · Pull 12 · 3:31 · Wipe in P2 · ", MeterText.Parse(MeterPreview.PullAt(MeterPreview.Ends), false));
    }

    [Fact]
    public void A_deaths_raise_and_weakness_are_said()
    {
        var d = new DeathEntry(8, "H2", "Odette", 171.3, "Exaflare", 104880, 18770, false, Array.Empty<RecapLine>());
        Assert.Equal("Exaflare · 104,880", MeterText.DeathLine(d));
        Assert.Equal("Not raised", MeterText.AfterDeath(d));
        var raised = d with { RaisedAt = 180.4, RaisedBy = "H1", HadWeakness = true };
        Assert.Equal("Exaflare · 104,880 · Weakness · raised by H1 after 0:09", MeterText.DeathLine(raised));
        Assert.Equal("Died with Weakness · Raised at 3:00 by H1 · 0:09 dead", MeterText.AfterDeath(raised));
        Assert.Equal("Raised at 3:00 · 0:09 dead", MeterText.AfterDeath(d with { RaisedAt = 180.4 }));
        Assert.Null(MeterText.AfterDeath(d with { PartOfWipe = true }));
        Assert.Equal("–", MeterText.Dead(0));
        Assert.Equal("0:42", MeterText.Dead(42.3));
        Assert.Equal("–", MeterText.Count(0));
        Assert.Equal("3", MeterText.Count(3));
    }

    [Fact]
    public void Events_read_newest_first_and_are_counted()
    {
        var list = new List<MeterEvent>
        {
            new(1, MeterEventKind.Potion, 1, "M1", "a"), new(2, MeterEventKind.Death, 2, "H2", "b"), new(3, MeterEventKind.Potion, 3, "M2", "c"),
            new(4, MeterEventKind.Raise, 2, "H2", "d"),
        };
        Assert.Equal(new[] { "d", "c" }, MeterText.Newest(list, 2).Select(e => e.Text));
        Assert.Equal("2 potions · 1 death · 1 raise", MeterText.Counts(list));
        Assert.Equal("2 potions", MeterText.Counts(list, 1));
        Assert.Equal("No events yet", MeterText.Counts(Array.Empty<MeterEvent>()));
        Assert.Equal(EventTone.Bad, MeterText.Tone(MeterEventKind.Death));
        Assert.Equal(EventTone.Good, MeterText.Tone(MeterEventKind.Interrupt));
        Assert.Equal(EventTone.Ember, MeterText.Tone(MeterEventKind.LimitBreak));
        Assert.All(Enum.GetValues<MeterEventKind>(), k => Assert.NotEqual("Event", MeterText.KindWord(k)));
    }

    [Fact]
    public void The_gauge_is_its_bars_full_and_filling()
    {
        Assert.Equal(new[] { 1f, 1f, 0.25f }, MeterText.Bars(new LimitBreakGauge(2, 3, 2500, 10000)));
        Assert.Equal(new[] { 1f, 1f, 1f }, MeterText.Bars(new LimitBreakGauge(3, 3, 0, 10000)));
        Assert.Equal(new[] { 0.5f }, MeterText.Bars(new LimitBreakGauge(0, 1, 5000, 10000)));
        Assert.Empty(MeterText.Bars(null));
        Assert.Empty(MeterText.Bars(new LimitBreakGauge(0, 0, 0, 10000)));
        Assert.Equal("Limit break: 2 of 3 bars, the next 25% full.", MeterText.GaugeTip(new LimitBreakGauge(2, 3, 2500, 10000)));
    }

    [Fact]
    public void The_events_table_is_in_spot_order()
    {
        var e = MeterPreview.PullAt(191);
        var shuffled = e.Rows.Reverse().ToList();
        Assert.Equal(new[] { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" }, MeterView.InSpotOrder(shuffled).Select(r => r.Slot));
    }

    [Fact]
    public void The_sample_has_events_a_gauge_and_the_new_numbers()
    {
        var c = 1000.0;
        var s = new MeterPreview(() => c);
        var e = s.Current!;
        var events = s.Events(e.Id);
        Assert.NotEmpty(events);
        Assert.All(events, ev => Assert.True(ev.At <= e.Duration));
        Assert.Contains(events, ev => ev.Kind == MeterEventKind.LimitBreak);
        Assert.Same(events, s.Events(e.Id));
        Assert.Empty(s.Events(999));
        var g = s.LimitBreak!;
        Assert.Equal(3, g.MaxBars);
        Assert.InRange(g.Bars, 0, 3);
        // The tinctures and the GCDs; the party sums them.
        var at = MeterPreview.PullAt(191);
        Assert.Equal(2, at.Rows.Single(r => r.Slot == "M1").Potions);
        Assert.Equal(1, at.Rows.Single(r => r.Slot == "OT").Interrupts);
        Assert.Equal(at.Rows.Sum(r => r.Casts), at.Party.Casts);
        Assert.Equal(191 - 171.3, at.Rows.Single(r => r.Slot == "H2").DeadSeconds, 3);
        // An earlier pull: Odette raised by H1.
        var eleven = s.Get(11)!;
        var d = Assert.Single(eleven.Deaths);
        Assert.Equal("H1", d.RaisedBy);
        Assert.Contains(s.Events(11), ev => ev.Kind == MeterEventKind.Raise);
        Assert.Equal(1, eleven.Rows.Single(r => r.Slot == "H1").RaisesGiven);
    }
}

/// <summary>Where an overlay goes before it's placed (UI/Model/Overlay.cs).</summary>
public class OverlayLayoutTests
{
    [Fact]
    public void It_starts_centred_where_asked_and_on_the_screen()
    {
        var at = OverlayLayout.FirstAnchor(Vector2.Zero, new Vector2(1920, 1080), 460, 200, 0.5f, 0.2f, MeterGrows.Down);
        Assert.Equal(new Vector2(730, 216), at);
        // Growing up, its foot is the anchor.
        Assert.Equal(new Vector2(730, 416), OverlayLayout.FirstAnchor(Vector2.Zero, new Vector2(1920, 1080), 460, 200, 0.5f, 0.2f, MeterGrows.Up));
        // Kept on the screen at the left.
        Assert.Equal(0, OverlayLayout.FirstAnchor(Vector2.Zero, new Vector2(1920, 1080), 460, 200, 0.05f, 0.2f, MeterGrows.Down).X);
        var s = new OverlaySettings();
        Assert.False(s.Show);
        Assert.Equal(MeterGrows.Down, s.Grows);
    }
}
