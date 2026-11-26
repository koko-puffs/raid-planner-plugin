using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>0.8.0: Home's latest pull in a sentence, the pulls by night, the setup guide's rules, the plan's next step.</summary>
public class HomeTests
{
    private static PullRow Mech(double t, string name) => new(t, name, 1, Array.Empty<MechanicKind>());

    private static PullRow Death(double t, string who, string? by) => new(t, by != null ? $"{who} died to {by}" : $"{who} died", 1, Array.Empty<MechanicKind>(), "M1", "SAM", who, by);

    [Fact]
    public void A_wipe_says_where_it_ended_and_who_died_first()
    {
        var rows = new List<PullRow> { Mech(300, "Wave Repeater"), Death(350, "Velka Moor", "Latent Defect"), Mech(370, "Hello World 2"), Mech(395, "Too Late") };
        var view = new PullView(rows, new double[] { 0, 120, 260 }, new double[] { 350, 360, 365 }, 0, 0, 5) { WipeAt = 380 };
        var r = new Recording { Duration = 391, Outcome = Outcome.Wipe };
        for (var i = 0; i < 8; i++) r.Party.Add(new PartyMember(i, "MT", "PLD", "tank"));
        var g = PullGist.Of(r, view);
        Assert.Equal(3, g.Phase);
        Assert.Equal("P3", g.PhaseLabel);
        Assert.Equal("Hello World 2", g.EndedAt);
        Assert.Equal(8, g.Party);
        var said = PullGist.Sentence(Outcome.Wipe, 391, g);
        Assert.Equal("Wiped in P3, at Hello World 2. Three deaths, the first Velka Moor to Latent Defect.", PullGist.Plain(said));
        Assert.Contains(said, s => s.Strong && s.Text == "Hello World 2");
        Assert.Equal("P3", PullGist.Where(g));
    }

    [Fact]
    public void The_sentence_says_what_it_knows()
    {
        // Not read yet: how it ended and its length.
        Assert.Equal("Wiped after 6:31.", PullGist.Plain(PullGist.Sentence(Outcome.Wipe, 391, null)));
        Assert.Equal("Cleared in 9:41.", PullGist.Plain(PullGist.Sentence(Outcome.Clear, 581, null)));
        // One phase, one death, no killer known; then a clear with none.
        var one = new PullGist(1, 1, "Ruinous Blast", 1, "M1", null, 8);
        Assert.Equal("Ended at Ruinous Blast. One death: M1.", PullGist.Plain(PullGist.Sentence(Outcome.Ended, 80, one)));
        Assert.Equal("at Ruinous Blast", PullGist.Where(one));
        Assert.Equal("Cleared in 9:41. No deaths.", PullGist.Plain(PullGist.Sentence(Outcome.Clear, 581, new PullGist(5, 5, "Enrage", 0, null, null, 8))));
        Assert.Equal("Wiped in P2, after 3:00. No deaths before the wipe.", PullGist.Plain(PullGist.Sentence(Outcome.Wipe, 180, new PullGist(2, 3, null, 0, null, null, 8))));
        Assert.Equal("14", PullGist.Number(14));
    }

    [Fact]
    public void A_pull_is_measured_against_the_one_before()
    {
        var g = new PullGist(3, 3, "Hello World 2", 3, "Velka Moor", "Latent Defect", 8);
        Assert.EndsWith(" 2 seconds further than pull 11.", PullGist.Plain(PullGist.Sentence(Outcome.Wipe, 391, g, (11, 389))));
        Assert.EndsWith(" 1:12 short of pull 11.", PullGist.Plain(PullGist.Sentence(Outcome.Wipe, 300, g, (11, 372))));
        Assert.EndsWith(" As far as pull 4.", PullGist.Plain(PullGist.Sentence(Outcome.Wipe, 300, g, (4, 300.2))));
        // A clear isn't "further".
        Assert.Equal("Cleared in 5:00. Three deaths, the first Velka Moor to Latent Defect.", PullGist.Plain(PullGist.Sentence(Outcome.Clear, 300, g, (11, 372))));
    }

    [Fact]
    public void A_death_says_who_by_name_when_the_recording_has_it()
    {
        var r = new Recording { Duration = 100, Outcome = Outcome.Clear };
        r.Party.Add(new PartyMember(1, "MT", "PLD", "tank", Name: "Velka Moor"));
        r.Party.Add(new PartyMember(2, "H2", "SCH", "healer"));
        r.Actions[9] = new ActionInfo("Latent Defect", 0, 0, 0, string.Empty, 0);
        r.Events.Add(new DeathEvent(40, 1, 9, 0));
        r.Events.Add(new DeathEvent(50, 2, 0, 0));
        var view = PullView.Build(r);
        var deaths = view.Rows.Where(x => x.Death).ToList();
        Assert.Equal("Velka Moor died to Latent Defect", deaths[0].Name);
        Assert.Equal(("Velka Moor", "Latent Defect"), (deaths[0].DeathWho, deaths[0].DeathBy));
        Assert.Equal("H2 died", deaths[1].Name);
        Assert.Null(view.WipeAt);
        r.Outcome = Outcome.Wipe;
        // The wipe is the deaths in the 6 s before the last one: here only the last.
        Assert.Equal(50, PullView.Build(r).WipeAt);
    }

    [Fact]
    public void Pulls_group_by_night_past_midnight()
    {
        var now = new DateTime(2026, 11, 20, 0, 30, 0);
        var times = new List<DateTime>
        {
            new(2026, 11, 20, 0, 10, 0), // after midnight: still tonight
            new(2026, 11, 19, 22, 0, 0),
            new(2026, 11, 18, 21, 0, 0), // last night
            new(2026, 11, 17, 14, 0, 0), // an afternoon
            new(2026, 11, 2, 21, 0, 0),
        };
        var nights = PullNights.Group(times, now);
        Assert.Equal(new[] { "Tonight", "Last night", "Tuesday", "2 Nov" }, nights.Select(n => n.Label));
        Assert.Equal(new[] { 0, 1 }, nights[0].Items);
        Assert.Equal("Tonight · 2 pulls", nights[0].Heading);
        Assert.Equal("Last night · 1 pull", nights[1].Heading);
        Assert.Equal("Today", PullNights.Label(new DateTime(2026, 11, 20), false, new DateTime(2026, 11, 20, 15, 0, 0)));
        Assert.Equal("Monday night", PullNights.Label(new DateTime(2026, 11, 16), true, new DateTime(2026, 11, 20, 15, 0, 0)));
        Assert.Equal("2 Nov 2025", PullNights.Label(new DateTime(2025, 11, 2), true, now));
    }

    [Fact]
    public void The_best_pull_is_a_clear_else_the_longest_of_its_duty()
    {
        var pulls = new List<(string, double, Outcome)>
        {
            ("TOP", 300, Outcome.Wipe), ("TOP", 462, Outcome.Wipe), ("TOP", 130, Outcome.Wipe),
            ("FRU", 600, Outcome.Clear), ("FRU", 700, Outcome.Wipe), ("FRU", 590, Outcome.Clear),
            ("UWU", 900, Outcome.Wipe),
        };
        Assert.Equal(new HashSet<int> { 1, 5 }, PullNights.Best(pulls));
    }

    [Fact]
    public void Pulls_are_numbered_by_night_and_duty_as_the_site_says_them()
    {
        // 0.9.1: "Pull 17" is the pull's place in its night of its fight (the site's words), not since the zone was entered.
        var pulls = new List<(DateTime, string)>
        {
            (new DateTime(2026, 11, 25, 0, 40, 0), "TOP"), // after midnight: still Monday's night
            (new DateTime(2026, 11, 24, 23, 10, 0), "top"),
            (new DateTime(2026, 11, 24, 22, 0, 0), "FRU"),
            (new DateTime(2026, 11, 24, 21, 0, 0), "TOP"),
            (new DateTime(2026, 11, 23, 21, 0, 0), "TOP"), // the night before
        };
        Assert.Equal(new[] { 3, 2, 1, 1, 1 }, PullNights.Numbers(pulls));
        Assert.Equal(4, PullNights.Next(pulls, "TOP", new DateTime(2026, 11, 25, 1, 0, 0)));
        Assert.Equal(1, PullNights.Next(pulls, "TOP", new DateTime(2026, 11, 25, 20, 0, 0)));
        Assert.Equal(2, PullNights.Next(pulls, "FRU", new DateTime(2026, 11, 24, 23, 0, 0)));
    }

    [Fact]
    public void The_account_step_waits_only_for_what_needs_an_account()
    {
        var rec = Features.Preset(FeaturePreset.Recommended);
        Assert.False(SetupGuide.CanGoOn(SetupGuide.Account, rec, false));
        Assert.True(SetupGuide.CanGoOn(SetupGuide.Account, rec, true));
        Assert.True(SetupGuide.CanGoOn(SetupGuide.Look, rec, false));
        var none = rec.With(Feature.Upload, false).With(Feature.Progress, false);
        Assert.True(SetupGuide.CanGoOn(SetupGuide.Account, none, false));
        Assert.Equal("Two of your picks need a Raid Codex account: Upload my logs and Track my progress. Linking takes a token from the site, once.",
            PullGist.Plain(SetupGuide.AccountLead(rec)));
        Assert.StartsWith("One of your picks needs a Raid Codex account: Upload my logs.", PullGist.Plain(SetupGuide.AccountLead(rec.With(Feature.Progress, false))));
        Assert.StartsWith("None of your picks", PullGist.Plain(SetupGuide.AccountLead(none)));
        Assert.Equal("5 of 6 on", SetupGuide.CountLine(rec));
        Assert.Equal(5, SetupGuide.Steps.Count);
    }

    [Fact]
    public void The_last_step_says_each_part_as_set()
    {
        var facts = new SetupFacts(true, false, 20, false, "Ready Check", false);
        Assert.Equal("Raids and trials · keeps the last 20", SetupGuide.DoneNote(Feature.Record, true, facts));
        Assert.Equal("Linked · public, as on FF Logs", SetupGuide.DoneNote(Feature.Upload, true, facts));
        Assert.Equal("Detailed · Ready Check · in duties", SetupGuide.DoneNote(Feature.Meter, true, facts));
        Assert.Equal("Compact · Readout · everywhere", SetupGuide.DoneNote(Feature.Meter, true, facts with { Compact = true, Theme = "Readout", MeterEverywhere = true }));
        Assert.Equal("Turn it on any time in Settings › Features", SetupGuide.DoneNote(Feature.Cooldowns, false, facts));
        Assert.StartsWith("Waits for your account", SetupGuide.DoneNote(Feature.Progress, true, facts with { Linked = false }));
        Assert.Equal("Plan: TOP v3", SetupGuide.ChipName(Feature.Plan, "TOP v3"));
        Assert.Equal("Plan", SetupGuide.ChipName(Feature.Plan));
    }

    [Fact]
    public void The_recording_card_knows_the_plans_next_step()
    {
        var arena = new PlanArena("circle", 1, Array.Empty<float[]>(), 0);
        PlanStep Step(string name, string time) => new(name, time, string.Empty, Array.Empty<BoardObj>(), null);
        var steps = new List<PlanStep> { Step("Pull", "0:00"), Step("Program Loop", "2:10"), Step("Pantokrator", "2:49"), Step("Enrage", "9:41") };
        var written = new Plan("p", "TOP v3", "1", arena, steps, null);
        Assert.Equal(581, PlanProgress.Length(written));
        Assert.Equal(("Pantokrator", (double?)18), PlanProgress.Next(written, 1, 151));
        Assert.Null(PlanProgress.Next(written, 3, 600));
        // Past its time: no "in".
        Assert.Null(PlanProgress.Next(written, 1, 200)!.Value.In);
        // Cues win over the written times.
        var cued = written with { Game = new PlanGame(1, 1, "TOP", new[] { new PlanCue(1, 1, 128), new PlanCue(2, 2, 170), new PlanCue(3, 3, 590) }) };
        Assert.Equal(590, PlanProgress.Length(cued));
        Assert.Equal(19, PlanProgress.Next(cued, 1, 151)!.Value.In);
    }

    [Fact]
    public void The_sent_line_says_since_when()
    {
        var now = new DateTimeOffset(new DateTime(2026, 11, 19, 23, 55, 0, DateTimeKind.Local));
        Assert.Equal("On your Raid Codex since 23:49", PullGist.SinceLine(now.AddMinutes(-6), now));
        Assert.Equal("On your Raid Codex since yesterday 23:49", PullGist.SinceLine(now.AddDays(-1).AddMinutes(-6), now));
    }
}
