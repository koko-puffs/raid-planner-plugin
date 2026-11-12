using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>0.5.0: who the party is (the party list, a cross-world party's group, or you), with fakes for the game's reads.</summary>
public class PartyPickTests
{
    private static readonly PartySeat Me = new(0x10000001, 1001, "Me Myself", 19, 100);

    private static PartySeat Seat(uint id, ulong cid, string name, uint job = 24, int group = 0, int index = 0) => new(id, cid, name, job, 100, 73, group, index);

    [Fact]
    public void The_party_list_wins_when_it_has_anyone()
    {
        var list = new[] { Seat(0x10000001, 1001, "Me Myself", 19), Seat(0x10000002, 1002, "Friend", 24) };
        var cross = new CrossWorldRead(true, false, 0, 1, new[] { Seat(0x10000009, 1009, "Someone else") });
        var c = PartyPick.Choose(list, cross, Me);
        Assert.Equal(PartyFrom.List, c.From);
        Assert.Equal(new uint[] { 0x10000001, 0x10000002 }, c.Ids);
    }

    [Fact]
    public void An_empty_party_list_falls_to_the_cross_world_group()
    {
        // The user's case: a cross-world party of two outside a duty, the party list empty.
        var cross = new CrossWorldRead(true, false, 0, 1, new[] { Seat(0x10000002, 1002, "Friend", index: 1), Seat(0x10000001, 1001, "Me Myself", 19, index: 0) });
        var c = PartyPick.Choose(Array.Empty<PartySeat>(), cross, Me);
        Assert.Equal(PartyFrom.CrossWorld, c.From);
        // In the group's order, you first here.
        Assert.Equal(new uint[] { 0x10000001, 0x10000002 }, c.Ids);
        Assert.Equal("Friend", c.Members[1].Name);
    }

    [Fact]
    public void Invalid_ids_in_the_list_are_skipped_and_with_none_left_its_the_cross_world_group()
    {
        var list = new[] { Seat(0, 0, ""), Seat(PartyPick.InvalidId, 0, "") };
        var cross = new CrossWorldRead(true, false, 0, 1, new[] { Seat(0x10000001, 1001, "Me Myself"), Seat(0x10000002, 1002, "Friend", index: 1) });
        Assert.Equal(PartyFrom.CrossWorld, PartyPick.Choose(list, cross, Me).From);
    }

    [Fact]
    public void No_party_at_all_is_just_you()
    {
        var c = PartyPick.Choose(Array.Empty<PartySeat>(), null, Me);
        Assert.Equal(PartyFrom.Alone, c.From);
        Assert.Equal(new uint[] { Me.EntityId }, c.Ids);
        Assert.Empty(PartyPick.Choose(Array.Empty<PartySeat>(), null, null).Members);
        // Not in a cross-world party: you, and no reason to give.
        var c2 = PartyPick.Choose(Array.Empty<PartySeat>(), new CrossWorldRead(false, false, 0, 0, Array.Empty<PartySeat>()), Me);
        Assert.Equal(PartyFrom.Alone, c2.From);
        Assert.Null(c2.Why);
    }

    [Fact]
    public void In_an_alliance_only_your_own_group_counts()
    {
        var members = new List<PartySeat>();
        for (var g = 0; g < 3; g++)
            for (var i = 0; i < 8; i++)
                members.Add(Seat((uint)(0x10000100 + g * 16 + i), (ulong)(5000 + g * 16 + i), $"P{g}-{i}", group: g, index: i));
        // You're in group 1, the fourth listed.
        members[8 + 3] = Me with { Group = 1, Index = 3 };
        var c = PartyPick.Choose(Array.Empty<PartySeat>(), new CrossWorldRead(true, true, 1, 3, members), Me);
        Assert.Equal(PartyFrom.CrossWorld, c.From);
        Assert.Equal(8, c.Members.Count);
        Assert.All(c.Members, m => Assert.Equal(1, m.Group));
        Assert.Contains(c.Members, m => m.EntityId == Me.EntityId);
    }

    [Fact]
    public void A_group_index_that_isnt_yours_is_corrected_by_where_you_are()
    {
        var members = new[]
        {
            Seat(0x10000010, 2010, "A", group: 0), Seat(0x10000011, 2011, "B", group: 0, index: 1),
            Me with { Group = 1, Index = 0 }, Seat(0x10000002, 1002, "Friend", group: 1, index: 1),
        };
        var c = PartyPick.Choose(Array.Empty<PartySeat>(), new CrossWorldRead(true, true, 0, 2, members), Me);
        Assert.Equal(new uint[] { Me.EntityId, 0x10000002 }, c.Ids);
    }

    [Fact]
    public void A_group_youre_not_in_or_with_only_you_or_too_big_isnt_used()
    {
        var notMe = new CrossWorldRead(true, false, 0, 1, new[] { Seat(0x10000002, 1002, "A"), Seat(0x10000003, 1003, "B", index: 1) });
        var c = PartyPick.Choose(Array.Empty<PartySeat>(), notMe, Me);
        Assert.Equal(PartyFrom.Alone, c.From);
        Assert.NotNull(c.Why);
        var onlyMe = new CrossWorldRead(true, false, 0, 1, new[] { Me });
        Assert.Equal(PartyFrom.Alone, PartyPick.Choose(Array.Empty<PartySeat>(), onlyMe, Me).From);
        var nine = Enumerable.Range(0, 9).Select(i => i == 0 ? Me : Seat((uint)(0x10000010 + i), (ulong)(3000 + i), $"N{i}", index: i)).ToArray();
        Assert.Equal(PartyFrom.Alone, PartyPick.Choose(Array.Empty<PartySeat>(), new CrossWorldRead(true, false, 0, 1, nine), Me).From);
        Assert.Equal(PartyFrom.Alone, PartyPick.Choose(Array.Empty<PartySeat>(), new CrossWorldRead(true, false, 0, 7, new[] { Me, Seat(2, 2, "x") }), Me).From);
    }

    [Fact]
    public void You_are_found_by_content_id_and_take_your_own_entity_id_and_job()
    {
        var cross = new CrossWorldRead(true, false, 0, 1, new[] { new PartySeat(0, 1001, "Me Myself", 1, 90, 73, 0, 0), Seat(0x10000002, 1002, "Friend", index: 1) });
        var c = PartyPick.Choose(Array.Empty<PartySeat>(), cross, Me);
        Assert.Equal(PartyFrom.CrossWorld, c.From);
        Assert.Equal(Me.EntityId, c.Members[0].EntityId);
        Assert.Equal(19u, c.Members[0].ClassJob);
    }

    [Fact]
    public void A_member_with_no_entity_id_is_resolved_else_kept_by_name()
    {
        var cross = new CrossWorldRead(true, false, 0, 1, new[] { Me, new PartySeat(0, 1002, "Friend", 24, 100, 73, 0, 1), new PartySeat(PartyPick.InvalidId, 1003, "Far Away", 33, 100, 73, 0, 2) });
        var c = PartyPick.Choose(Array.Empty<PartySeat>(), cross, Me, s => s.ContentId == 1002 ? 0x10000002u : 0u);
        Assert.Equal(3, c.Members.Count);
        Assert.Equal(new uint[] { Me.EntityId, 0x10000002 }, c.Ids);
        Assert.Equal(0u, c.Members[2].EntityId);
        Assert.Equal("Far Away", c.Members[2].Name);
    }

    [Fact]
    public void Each_member_once()
    {
        var cross = new CrossWorldRead(true, false, 0, 1, new[] { Me, Seat(0x10000002, 1002, "Friend", index: 1), Seat(0x10000002, 1002, "Friend", index: 2) });
        Assert.Equal(2, PartyPick.Choose(Array.Empty<PartySeat>(), cross, Me).Members.Count);
    }

    [Fact]
    public void Describes_itself_for_diagnostics()
    {
        var cross = new CrossWorldRead(true, false, 0, 1, new[] { Me, Seat(0x10000002, 1002, "Friend", index: 1) });
        var c = PartyPick.Choose(Array.Empty<PartySeat>(), cross, Me);
        var line = PartyPick.Describe(c, cross, s => s.EntityId == Me.EntityId);
        Assert.StartsWith("cross-world (cross-world: in True alliance False group 0 of 1, 2 members)", line);
        Assert.Contains("[10000001 Me Myself job 19 lv 100 cid near]", line);
        Assert.Contains("[10000002 Friend job 24 lv 100 cid]", line);
    }
}

/// <summary>0.5.0: the recording player's gear in the file (<c>gear</c>), written and read back leniently.</summary>
public class GearTests
{
    [Fact]
    public void The_sample_keeps_its_gear_through_the_file()
    {
        var r = RecordingTests.Sample();
        var json = r.ToJson();
        var back = Recording.Parse(json);
        Assert.NotNull(back.Gear);
        Assert.Equal("BLM", back.Gear!.Job);
        Assert.Equal(730, back.Gear.ItemLevel);
        Assert.Equal(12, back.Gear.Items.Count);
        Assert.Equal("Dark Horse Champion's Rod", back.Gear.Items[0].Name);
        Assert.Equal(4886, back.Gear.Stats["int"]);
        Assert.Equal(12, back.Gear.Stats.Count);
        Assert.Equal(json, back.ToJson());
        // A copy with other frames keeps it.
        Assert.Same(r.Gear, r.WithFrames(r.Frames, true).Gear);
    }

    [Fact]
    public void No_gear_is_none_and_isnt_written()
    {
        var r = RecordingTests.Sample();
        r.Gear = null;
        var json = r.ToJson();
        Assert.DoesNotContain("\"gear\"", json);
        Assert.Null(Recording.Parse(json).Gear);
    }

    [Fact]
    public void Read_leniently()
    {
        var json = RecordingTests.Sample().ToJson();
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node["gear"] = System.Text.Json.Nodes.JsonNode.Parse("""
            { "job": "BLM", "ilvl": "high", "items": [ { "slot": 0, "id": 43107, "hq": true, "name": "Rod" }, { "slot": 0, "id": 1 },
              { "slot": 14, "id": 2 }, { "slot": 1.5, "id": 3 }, { "slot": 2, "id": 0 }, { "slot": 3 }, "nonsense" ],
              "stats": { "int": 4886, "luck": 7, "crit": -1, "dh": 1.5e12, "det": "2106" } }
            """);
        var gear = Recording.Parse(node.ToJsonString()).Gear!;
        Assert.Equal(0, gear.ItemLevel);
        var item = Assert.Single(gear.Items);
        Assert.True(item.Hq);
        Assert.Equal(43107u, item.Id);
        Assert.Equal(new Dictionary<string, int> { ["int"] = 4886 }, gear.Stats);
        node["gear"] = 5;
        Assert.Null(Recording.Parse(node.ToJsonString()).Gear);
    }

    [Fact]
    public void The_average_item_level_as_the_character_window_has_it()
    {
        // A two-handed weapon counts twice; the waist and the soul crystal never count; an empty place is 0.
        var all = Enumerable.Range(0, 14).Where(s => s != 1).Select(s => (s, s == 0 ? 735 : 730)).ToList();
        Assert.Equal(730, Gear.AverageItemLevel(all, true));
        // A sword and shield: each once.
        var pld = Enumerable.Range(0, 13).Where(s => s != 5).Select(s => (s, s == 0 ? 740 : 728)).ToList();
        Assert.Equal((740 + 728 * 11) / 12, Gear.AverageItemLevel(pld, false));
        // A shield beside a two-handed weapon isn't counted.
        Assert.Equal(735 * 2 / 12, Gear.AverageItemLevel(new[] { (0, 735), (1, 999) }, true));
        Assert.Equal(0, Gear.AverageItemLevel(Array.Empty<(int, int)>(), false));
    }
}

/// <summary>0.5.0: a step's planned cooldowns (SPEC5 §3.1), read leniently, placed on the plan's clock, said and marked.</summary>
public class PlanCooldownTests
{
    private const string Json = """
        { "id": "p1", "name": "Plan", "phases": [
            { "id": "s1", "name": "Opener", "cooldowns": [
                { "id": "a", "spot": "OT", "action": 7531, "name": "Rampart", "at": 4 },
                { "id": "b", "spot": "mt", "action": 7535, "name": "Reprisal", "at": -3, "note": "for   Akh  Morn" },
                { "id": "c", "spot": "H1", "action": 16536, "name": "Temperance" },
                { "id": "d", "spot": "X9", "action": 1, "name": "Nobody's" },
                { "id": "e", "spot": "P25", "name": "Too far" },
                { "id": "f", "spot": "M1", "name": "" },
                { "id": "g", "spot": "R1", "action": "7549", "name": "Bad action" },
                { "id": "h", "spot": "R2", "name": "Bad at", "at": "soon" },
                { "id": "i", "spot": "P12", "action": -5, "name": "Alliance Feint", "at": 900.04 },
                "nonsense"
            ] },
            { "id": "s2", "name": "Nothing planned" },
            { "id": "s3", "name": "Not an array", "cooldowns": { "spot": "MT" } }
        ], "game": { "content": 1, "cues": [ { "phase": "s1", "action": 100, "t": 10 }, { "phase": "s2", "action": 101, "t": 40 } ] } }
        """;

    [Fact]
    public void Read_leniently_in_the_order_they_go()
    {
        var plan = PlanJson.Read(Json)!;
        var c = plan.Steps[0].Cooldowns;
        // A known spot and a name each; a value there but not a number skips the entry; the rest clamped as the site does.
        Assert.Equal(new[] { "b", "c", "a", "i" }, c.Select(x => x.Id));
        var reprisal = c[0];
        Assert.Equal("MT", reprisal.Spot);
        Assert.Equal(7535u, reprisal.Action);
        Assert.Equal(-3, reprisal.At);
        Assert.Equal("for Akh Morn", reprisal.Note);
        Assert.Equal("−3 s", reprisal.AtWords);
        Assert.Equal(string.Empty, c[1].AtWords);
        Assert.Null(c[1].Note);
        var alliance = c[3];
        Assert.Equal(0u, alliance.Action);
        Assert.Equal(PlanJson.AtMax, alliance.At);
        Assert.Empty(plan.Steps[1].Cooldowns);
        Assert.Empty(plan.Steps[2].Cooldowns);
        Assert.Equal(new[] { "Reprisal" }, plan.Steps[0].CooldownsFor("MT").Select(x => x.Name));
        Assert.Empty(plan.Steps[0].CooldownsFor(null));
    }

    [Fact]
    public void At_most_24_a_step()
    {
        var many = string.Join(",", Enumerable.Range(0, 40).Select(i => $$"""{ "id": "c{{i}}", "spot": "MT", "name": "Rampart", "at": {{i}} }"""));
        var plan = PlanJson.Read($$"""{ "id": "p", "phases": [ { "name": "S", "cooldowns": [{{many}}] } ] }""")!;
        Assert.Equal(PlanJson.MaxCooldowns, plan.Steps[0].Cooldowns.Count);
    }

    [Fact]
    public void An_older_plan_has_none()
    {
        var plan = PlanJson.Read("""{ "id": "p", "phases": [ { "name": "S" } ] }""")!;
        Assert.Empty(plan.Steps[0].Cooldowns);
        Assert.Empty(new PlannedUses(plan).All);
    }

    [Fact]
    public void At_words_as_the_site_writes_them()
    {
        Assert.Equal("+3 s", PlanJson.AtWords(3));
        Assert.Equal("−2.5 s", PlanJson.AtWords(-2.5));
        Assert.Equal(string.Empty, PlanJson.AtWords(0));
        Assert.Equal(0, PlanJson.SpotRank("MT"));
        Assert.Equal(8 + 12, PlanJson.SpotRank("P12"));
        Assert.Equal(99, PlanJson.SpotRank("X"));
    }

    [Fact]
    public void Placed_on_the_plans_clock()
    {
        var plan = PlanJson.Read(Json)!;
        var uses = new PlannedUses(plan);
        // The step's time is its first cue's (10): Reprisal at 7, Temperance at 10, Rampart at 14; the second step has none.
        Assert.Equal(new[] { 7.0, 10, 14, 610 }, uses.All.Select(u => u.At));
        // The MT's Reprisal from 2 s after to 15 s before.
        bool Any(PlanCooldown _) => true;
        Assert.Equal("b", uses.Next("MT", Any, 0)?.Cooldown.Id);
        Assert.Null(uses.Next("MT", Any, -9));
        Assert.Equal("b", uses.Next("MT", Any, 8.9)?.Cooldown.Id);
        Assert.Null(uses.Next("MT", Any, 9.1));
        Assert.Null(uses.Next("H2", Any, 5));
        Assert.Null(uses.Next("MT", c => c.Name == "Rampart", 5));
        Assert.Null(uses.Next("MT", Any, double.NaN));
        // A step with no cue isn't placed.
        var untimed = PlanJson.Read(Json.Replace("\"cues\"", "\"nocues\""))!;
        Assert.Empty(new PlannedUses(untimed).All);
    }

    [Fact]
    public void Your_line_under_the_callout()
    {
        var plan = PlanJson.Read(Json)!;
        Assert.Equal("You: Reprisal −3 s", PlannedUses.YouLine(plan.Steps[0], "MT"));
        Assert.Equal("You: Temperance", PlannedUses.YouLine(plan.Steps[0], "H1"));
        Assert.Null(PlannedUses.YouLine(plan.Steps[0], "R2"));
        Assert.Null(PlannedUses.YouLine(plan.Steps[1], "MT"));
        // The callouts' first line carries it; the next ones don't.
        var cp = new CalloutPlan(plan);
        var lines = new List<CalloutLine>();
        CalloutTimeline.Upcoming(cp, 5, 60, 3, lines, "MT");
        Assert.Equal("You: Reprisal −3 s", lines[0].You);
        Assert.Null(lines[1].You);
        CalloutTimeline.Upcoming(cp, 5, 60, 3, lines, "OT");
        Assert.Equal("You: Rampart +4 s", lines[0].You);
        CalloutTimeline.Upcoming(cp, 5, 60, 3, lines);
        Assert.Null(lines[0].You);
        // The sample says one too.
        Assert.Contains(Enumerable.Range(0, 50).SelectMany(t => CalloutSample.At(t, 15, 3)), l => l.You != null);
    }

    [Fact]
    public void The_tracker_marks_a_planned_use_coming_even_with_ready_ones_hidden()
    {
        var plan = PlanJson.Read(Json)!;
        var uses = new PlannedUses(plan);
        var party = new[] { new CooldownMember(1, "MT", "WAR", 1, false, 100), new CooldownMember(2, "OT", "PLD", 1, true, 100) };
        CooldownAction? Action(string key) => key == "Reprisal" ? new CooldownAction("Reprisal", 7535, "Reprisal", 0, 60, 1) : null;
        var book = new CooldownBook();
        (PlannedUse, double)? Planned(CooldownMember m, CooldownSpec spec, CooldownAction a, double clock) =>
            uses.Next(m.Slot, c => CooldownView.Matches(c, spec, a), clock) is { } u ? (u, u.At - clock) : null;
        var lines = CooldownView.Lines(party, Action, null, book, _ => null, 0, true, hideReady: true, planned: (m, s, a) => Planned(m, s, a, 1));
        var line = Assert.Single(lines);
        Assert.Equal("MT", line.Who.Slot);
        Assert.Equal(6, line.Planned!.Value.In);
        Assert.Contains("Planned: Reprisal at 0:07, for Akh Morn.", CooldownView.Tip(line));
        Assert.Equal("planned in 6 s", PlannedUses.Soon(line.Planned.Value.In));
        Assert.Equal("planned now", PlannedUses.Soon(-1));
        // Too far off, or nothing planned: ready ones hidden, as ever.
        Assert.Empty(CooldownView.Lines(party, Action, null, book, _ => null, 0, true, hideReady: true, planned: (m, s, a) => Planned(m, s, a, -20)));
        Assert.Empty(CooldownView.Lines(party, Action, null, book, _ => null, 0, true, hideReady: true));
    }

    [Fact]
    public void A_planned_cooldown_matches_by_its_row_or_its_name()
    {
        var spec = CooldownCatalog.Find("Physis II")!;
        var a = new CooldownAction("Physis II", 24302, "Physis II", 0, 60, 1);
        Assert.True(CooldownView.Matches(new PlanCooldown("x", "H2", 24302, "Physis II", 0, null), spec, a));
        Assert.True(CooldownView.Matches(new PlanCooldown("x", "H2", 24288, "Physis", 0, null), spec, a, id => id == 24288 ? "Physis II" : null));
        Assert.True(CooldownView.Matches(new PlanCooldown("x", "H2", 0, "physis", 0, null), spec, a));
        Assert.False(CooldownView.Matches(new PlanCooldown("x", "H2", 24300, "Kerachole", 0, null), spec, a, _ => "Kerachole"));
    }

    [Fact]
    public void The_trackers_sample_shows_a_planned_row()
    {
        Assert.Contains(Enumerable.Range(0, 30).SelectMany(t => CooldownSample.At(t, null)), l => l.Planned != null);
    }
}

/// <summary>0.5.0: the summary carries the recording player's average item level (<c>x.ilvl</c>) when the gear was read.</summary>
public class GearSummaryTests
{
    [Fact]
    public void The_summary_says_the_item_level_only_when_the_gear_was_read()
    {
        var r = RecordingTests.SampleBuilder(true, out var phase);
        var x = PullSummary.Build(r, phase, isDot: id => id == 1871)["stats"]!["x"]!.AsObject();
        Assert.Equal(730, x["ilvl"]!.GetValue<int>());
        r.Gear = null;
        Assert.False(PullSummary.Build(r, phase, isDot: id => id == 1871)["stats"]!["x"]!.AsObject().ContainsKey("ilvl"));
        r.Gear = RecordingTests.SampleGear with { ItemLevel = 0 };
        Assert.False(PullSummary.Build(r, phase, isDot: id => id == 1871)["stats"]!["x"]!.AsObject().ContainsKey("ilvl"));
    }
}
