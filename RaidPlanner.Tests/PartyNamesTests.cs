using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// What the windows call a player (Core/PartyNames.cs): their name, else their job, "WAR 2" for the second of a job shown by
/// job; never a spot (MT, H1…), the plugin's guess the game never says.
/// </summary>
public class PartyNamesTests
{
    private static readonly string[] Spots = { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" };

    [Fact]
    public void A_player_is_their_name_when_there_is_one()
    {
        var party = new List<(string?, string?)> { ("Velka Moor", "PLD"), (" Koko Nyan ", "WHM") };
        Assert.Equal(new[] { "Velka Moor", "Koko Nyan" }, PartyNames.Labels(party));
    }

    [Fact]
    public void Without_a_name_a_player_is_their_job()
    {
        var party = new List<(string?, string?)> { (null, "PLD"), ("", "whm"), ("  ", "SCH"), (null, null) };
        Assert.Equal(new[] { "PLD", "WHM", "SCH", "Player" }, PartyNames.Labels(party));
    }

    [Fact]
    public void Two_of_a_job_without_names_are_told_apart_by_the_party_lists_order()
    {
        var party = new List<(string?, string?)> { (null, "WAR"), (null, "WHM"), (null, "WAR"), (null, "DRG"), (null, "WAR") };
        Assert.Equal(new[] { "WAR", "WHM", "WAR 2", "DRG", "WAR 3" }, PartyNames.Labels(party));
        Assert.Equal("WAR 2", PartyNames.Label(party, 2));
        Assert.Equal("Player", PartyNames.Label(party, 9));
    }

    [Fact]
    public void Only_those_shown_by_job_are_numbered()
    {
        // The named WAR is their name; the one without is the only WAR shown by job, so just "WAR".
        var party = new List<(string?, string?)> { ("Brann", "WAR"), (null, "WAR") };
        Assert.Equal(new[] { "Brann", "WAR" }, PartyNames.Labels(party));
        // Jobs only (Settings → Damage meter): everyone by job, numbered.
        Assert.Equal(new[] { "WAR", "WAR 2" }, PartyNames.Labels(party, jobsOnly: true));
    }

    [Fact]
    public void Who_is_the_name_unless_jobs_only_or_there_is_none()
    {
        Assert.Equal("Brann", PartyNames.Who("Brann", "WAR 2", false));
        Assert.Equal("WAR 2", PartyNames.Who("Brann", "WAR 2", true));
        Assert.Equal("WAR 2", PartyNames.Who(null, "WAR 2", false));
        Assert.Equal("Player", PartyNames.Who("", "", false));
        Assert.Equal("DRG", PartyNames.Numbered("DRG", 1));
        Assert.Equal("DRG 2", PartyNames.Numbered("DRG", 2));
    }

    [Fact]
    public void The_meter_numbers_two_of_a_job_and_never_shows_a_spot()
    {
        var s = new MeterSession(a => $"A{a}", st => $"S{st}") { EndAfter = 4 };
        var parser = CombatParserTests.Parser(out _);
        parser.Add(s);
        parser.SetParty(new uint[] { 1, 2, 3, 4 });
        s.Place(1, 77, "Test Arena");
        PartyState P(uint id, string name, string job, byte role, bool you = false) => new(id, name, job, role, 100_000, 100_000, true, you);
        var party = new[] { P(1, "Brann", "WAR", 1), P(2, "Sylvie", "WHM", 4), P(3, "", "WAR", 1), P(4, "Rook", "DRG", 2, you: true) };
        s.Frame(0, party, true);
        parser.Action(CombatParserTests.Batch(0.5, 4, 100, 0, (900, new[] { CombatParserTests.E(3, value: 5000) }, -1, -1, true)));
        s.Frame(2, party, true);
        var e = s.Current!;
        var rows = e.Rows.Where(r => !r.IsLimitBreak).ToList();
        Assert.Equal("WAR", rows.Single(r => r.PartyId == 1).Tag);
        Assert.Equal("WAR 2", rows.Single(r => r.PartyId == 3).Tag);
        // Names shown: theirs, and the second WAR (no name) by job; Jobs only: every one by job.
        Assert.Equal(new[] { "Brann", "Sylvie", "WAR 2", "Rook" }, rows.OrderBy(r => r.PartyId).Select(r => MeterText.Called(r, false)));
        Assert.Equal(new[] { "WAR", "WHM", "WAR 2", "DRG" }, rows.OrderBy(r => r.PartyId).Select(r => MeterText.Called(r, true)));
        foreach (var r in rows)
        {
            Assert.DoesNotContain(MeterText.Called(r, true), Spots);
            Assert.DoesNotContain(MeterText.Who(r, true), Spots);
        }
        // The copied text with Jobs only: jobs, no spots.
        var text = MeterText.Parse(e, true);
        Assert.DoesNotContain("MT", text);
        Assert.Contains("\nDRG ", text);
    }

    [Fact]
    public void A_ready_check_card_and_the_tracker_number_two_of_a_job()
    {
        var card = ReadyCheckView.Card(new[]
        {
            new ReadyMember("", "WAR", "tank", FoodState.Fed, 1800), new ReadyMember("Brann", "WAR", "tank", FoodState.Fed, 1800),
        }, new ReadyYou(2, 1, null), null);
        Assert.Equal(new[] { "WAR", "Brann" }, card.Rows.Select(r => r.Called(false)));
        Assert.Equal(new[] { "WAR", "WAR 2" }, card.Rows.Select(r => r.Called(true)));
        var m = new CooldownMember(2, "OT", "WAR", 1, false, 0, "", "WAR 2");
        Assert.Equal("WAR 2", m.Called(false));
        Assert.Equal("Brann", (m with { Name = "Brann" }).Called(false));
    }
}
