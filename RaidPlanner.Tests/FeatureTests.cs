using System.Net;
using System.Text.Json.Nodes;
using RaidPlanner.Core;
using Xunit;

namespace RaidPlanner.Tests;

public class NewRecordingTests
{
    private static RecordingBuilder New() => new(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", _ => null,
        id => id == 3000 ? new StatusInfo("Doom", 215503) : null);

    /// <summary>One frame of one party member's statuses: (status, source actor, keep).</summary>
    private static void Frame(RecordingBuilder b, double t, int on, params (uint Id, int Source, bool Keep)[] now)
    {
        b.BeginStatuses();
        b.StatusesOf(on);
        foreach (var (id, src, keep) in now)
            if (!b.StatusKnown(on, id)) b.StatusGained(t, on, id, src, 8, keep);
        b.EndStatuses(t);
    }

    [Fact]
    public void An_enemys_status_is_recorded_as_it_comes_and_goes()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1) });
        Frame(b, 1, 1, (3000, 101, true));
        Frame(b, 1.5, 1, (3000, 101, true));
        Frame(b, 2, 1);
        var events = b.Finish(20, Outcome.Wipe).Events.OfType<StatusEvent>().ToList();
        Assert.Equal(2, events.Count);
        Assert.True(events[0].Gain);
        Assert.Equal(101, events[0].Source);
        Assert.Equal(8f, events[0].Duration);
        Assert.False(events[1].Gain);
        Assert.Equal(2.0, events[1].T, 2);
    }

    [Fact]
    public void The_partys_own_buffs_arent_recorded_and_arent_looked_at_again()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1) });
        Frame(b, 1, 1, (1000, 0, false));
        b.BeginStatuses();
        b.StatusesOf(1);
        Assert.True(b.StatusKnown(1, 1000));
        b.EndStatuses(2);
        Frame(b, 3, 1);
        var r = b.Finish(20, Outcome.Wipe);
        Assert.Empty(r.Events.OfType<StatusEvent>());
        Assert.Empty(r.Statuses);
    }

    [Fact]
    public void A_member_not_read_this_frame_keeps_their_statuses()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1), new Partier(12, "WAR", 1) });
        Frame(b, 1, 2, (3000, 0, true));
        // Only member 1 read: member 2's status isn't gone.
        Frame(b, 2, 1);
        Assert.Single(b.Finish(20, Outcome.Wipe).Events.OfType<StatusEvent>());
    }

    [Fact]
    public void Statuses_are_named_in_the_file_and_read_back()
    {
        var r = RecordingTests.Sample();
        Assert.Equal("Doom", r.Statuses[1769].Name);
        Assert.False(r.Statuses.ContainsKey(1000));
        var json = r.ToJson();
        Assert.Contains("\"1769\":{\"name\":\"Doom\",\"icon\":215502}", json);
        Assert.Contains("\"k\":\"status\"", json);
        var back = Recording.Parse(json);
        Assert.Equal("Doom", back.Statuses[1769].Name);
        Assert.Equal(r.Events.OfType<StatusEvent>().Count(), back.Events.OfType<StatusEvent>().Count());
        Assert.Equal(json, back.ToJson());
    }

    [Fact]
    public void A_death_names_the_last_enemy_action_that_hit_within_3_seconds()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1), new Partier(12, "WAR", 1) });
        b.Alive(0, 1, false);
        b.Alive(0, 2, false);
        b.Hit(10, 101, 600, 0, 0, 0, 0, 0, new[] { 1 });
        b.Hit(2, 101, 700, 0, 0, 0, 0, 0, new[] { 2 });
        b.Alive(11, 1, true);
        b.Alive(11, 2, true);
        var deaths = b.Finish(20, Outcome.Wipe).Events.OfType<DeathEvent>().ToList();
        Assert.Equal(2, deaths.Count);
        Assert.Equal(600u, deaths[0].By);
        Assert.Equal(101, deaths[0].From);
        // Hit 9 seconds before: not what killed them.
        Assert.Equal(0u, deaths[1].By);
        Assert.Equal(0, deaths[1].From);
    }

    [Fact]
    public void Down_already_isnt_a_death_but_dying_again_after_a_raise_is()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1) });
        b.Alive(0, 1, true);
        b.Alive(1, 1, true);
        b.Alive(5, 1, false);
        b.Alive(9, 1, true);
        var deaths = b.Finish(20, Outcome.Wipe).Events.OfType<DeathEvent>().ToList();
        Assert.Single(deaths);
        Assert.Equal(9.0, deaths[0].T, 2);
    }

    private static void Targets(RecordingBuilder b, double t, params (int Actor, bool On)[] now)
    {
        foreach (var (a, on) in now) b.Targetable(t, a, on);
        b.EndTargets(t);
    }

    [Fact]
    public void Targetability_is_recorded_only_for_enemies_that_have_been_targetable()
    {
        var b = New();
        Targets(b, 0, (101, true), (102, false));
        Targets(b, 1, (101, true), (102, false));
        Targets(b, 2, (101, false), (102, false));
        Targets(b, 3, (101, true), (102, false));
        var events = b.Finish(20, Outcome.Wipe).Events.OfType<TargetEvent>().ToList();
        // The first time it's targetable is recorded too (the site sees a boss swap by it).
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(101, e.Actor));
        Assert.True(events[0].On);
        Assert.Equal(0, events[0].T);
        Assert.False(events[1].On);
        Assert.True(events[2].On);
        // Gone a second only: not a phase.
        Assert.Equal(1, b.Phase);
    }

    [Fact]
    public void Phases_count_every_enemy_gone_a_while_and_one_back_a_new_boss_too()
    {
        var b = New();
        Targets(b, 0, (101, true), (102, true));
        // One of two goes: not a phase.
        Targets(b, 1, (101, false), (102, true));
        Targets(b, 20, (101, true), (102, true));
        Assert.Equal(1, b.Phase);
        // Both go (one leaves the game altogether), then a new boss comes 8 s later: phase 2.
        Targets(b, 30, (101, false));
        Targets(b, 35, (101, false));
        Targets(b, 38, (103, true));
        Assert.Equal(2, b.Phase);
        // It jumps away for 5 s: not a phase.
        Targets(b, 40, (103, false));
        Targets(b, 45, (103, true));
        Assert.Equal(2, b.Phase);
        // It goes for 10 s and comes back: phase 3.
        Targets(b, 50, (103, false));
        Targets(b, 60, (103, true));
        Assert.Equal(3, b.Phase);
        var events = b.Finish(70, Outcome.Wipe).Events.OfType<TargetEvent>().ToList();
        // 102 leaving the game is it going untargetable; 103 coming the first time is recorded.
        Assert.Contains(events, e => e.Actor == 102 && !e.On && e.T == 30);
        Assert.Contains(events, e => e.Actor == 103 && e.On && e.T == 38);
        Assert.Contains(events, e => e.Actor == 101 && e.On && e.T == 0);
    }

    [Fact]
    public void The_sample_has_its_new_events()
    {
        var r = RecordingTests.SampleBuilder(true, out var phase);
        Assert.Equal(2, phase);
        // Vulnerability on two, Doom on two and off again (the party's own buff isn't kept).
        Assert.Equal(6, r.Events.OfType<StatusEvent>().Count());
        var deaths = r.Events.OfType<DeathEvent>().ToList();
        Assert.Equal(2, deaths.Count);
        Assert.Equal(4, deaths[0].Who);
        Assert.Equal(700u, deaths[0].By);
        Assert.Equal(8, deaths[1].Who);
        Assert.Equal(100u, deaths[1].By);
        // The boss targetable at the pull, gone at 41, back at 50 (9 s: a phase).
        Assert.Equal(3, r.Events.OfType<TargetEvent>().Count());
        // The timeline only reads casts and hits (the second raidwide is its own line).
        Assert.Equal(7, r.MechanicCount);
    }
}

public class PullSummaryTests
{
    [Fact]
    public void Sums_up_a_pull()
    {
        var r = RecordingTests.SampleBuilder(true, out var phase);
        var s = PullSummary.Build(r, phase);
        Assert.Equal(99999u, s["content"]!.GetValue<uint>());
        Assert.Equal(1u, s["territory"]!.GetValue<uint>());
        Assert.Equal("Test Arena (Savage)", s["duty"]!.GetValue<string>());
        Assert.Equal(3, s["pull"]!.GetValue<int>());
        Assert.Equal("2026-11-02T20:00:00Z", s["recordedAt"]!.GetValue<string>());
        Assert.Equal("wipe", s["outcome"]!.GetValue<string>());
        Assert.Equal(61.0, s["reached"]!.GetValue<double>(), 2);
        Assert.Equal(61.0, s["duration"]!.GetValue<double>(), 2);
        Assert.Equal(2, s["phase"]!.GetValue<int>());
        var deaths = s["deaths"]!.AsArray();
        Assert.Equal(2, deaths.Count);
        Assert.Equal("H2", deaths[0]!["slot"]!.GetValue<string>());
        Assert.Equal("SGE", deaths[0]!["job"]!.GetValue<string>());
        Assert.Equal("Exaflare", deaths[0]!["by"]!.GetValue<string>());
        Assert.Equal(45.5, deaths[0]!["t"]!.GetValue<double>(), 2);
        Assert.Equal("Raidwide Roar", deaths[1]!["by"]!.GetValue<string>());
        // No one's name: only spots and jobs (the stats name actions and statuses only).
        Assert.DoesNotContain("\"name\"", s.ToJsonString());
    }

    [Fact]
    public void Deaths_are_capped_and_a_death_to_nothing_has_no_by()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", _ => null);
        b.SetParty(new[] { new Partier(11, "PLD", 1) });
        for (var i = 0; i < 120; i++)
        {
            b.Alive(i, 1, false);
            b.Alive(i + 0.5, 1, true);
        }
        var s = PullSummary.Build(b.Finish(200, Outcome.Wipe), 0);
        Assert.Equal(PullSummary.MaxDeaths, s["deaths"]!.AsArray().Count);
        Assert.Null(s["deaths"]![0]!["by"]);
        Assert.Equal(1, s["phase"]!.GetValue<int>());
    }
}

public class RosterTests
{
    [Fact]
    public void A_party_that_cant_be_read_isnt_sent()
    {
        // Outside a duty, a cross-world party reads as no one (or just you): never sent, so the
        // site's roster isn't replaced with one name.
        Assert.NotNull(PartyRoster.WhyNot(0));
        Assert.Contains("try in the duty", PartyRoster.WhyNot(PartyRoster.Build(new List<(string, byte)> { ("Just Me", 1) }).Count));
        Assert.Null(PartyRoster.WhyNot(PartyRoster.Build(new List<(string, byte)> { ("Tank One", 1), ("Healer One", 4) }).Count));
    }

    [Fact]
    public void Names_go_to_their_spots_only_the_eight()
    {
        var party = new List<(string, byte)>
        {
            ("Ranged One", 3), ("Tank One", 1), ("Healer One", 4), ("Melee One", 2), ("Tank Two", 1), ("Healer Two", 4), ("Melee Two", 2),
            ("Ranged Two", 3), ("Extra Person", 2), ("  ", 2),
        };
        var r = PartyRoster.Build(party);
        Assert.Equal("Tank One", r["MT"]!.GetValue<string>());
        Assert.Equal("Tank Two", r["OT"]!.GetValue<string>());
        Assert.Equal("Ranged One", r["R1"]!.GetValue<string>());
        Assert.Equal(8, r.Count);
        Assert.False(r.ContainsKey("P9"));
    }

    [Fact]
    public void Long_names_are_cut()
    {
        var r = PartyRoster.Build(new List<(string, byte)> { (new string('x', 80), 1) });
        Assert.Equal(PartyRoster.MaxName, r["MT"]!.GetValue<string>().Length);
    }
}

public class SummaryQueueTests
{
    [Fact]
    public void Sends_one_at_a_time_from_the_oldest()
    {
        var q = new SummaryQueue(3);
        q.Add("{}", "a");
        q.Add("{}", "b");
        Assert.Equal("a", q.Next()!.Value.ClientId);
        Assert.True(q.Sending);
        Assert.Null(q.Next());
        // Can't be reached: it waits, and goes again.
        q.Answered(false);
        Assert.Equal("a", q.Next()!.Value.ClientId);
        q.Answered(true);
        Assert.Equal(1, q.Count);
        Assert.Equal("b", q.Next()!.Value.ClientId);
    }

    [Fact]
    public void The_oldest_dropped_while_its_call_is_out_doesnt_take_the_next_one_with_it()
    {
        var q = new SummaryQueue(2);
        q.Add("{}", "a");
        q.Add("{}", "b");
        Assert.Equal("a", q.Next()!.Value.ClientId);
        // Full while "a" is on its way: "a" goes to make room for "c".
        q.Add("{}", "c");
        q.Answered(true);
        // "b" is still waiting (only "a" was answered), then "c".
        Assert.Equal(2, q.Count);
        Assert.Equal("b", q.Next()!.Value.ClientId);
        q.Answered(true);
        Assert.Equal("c", q.Next()!.Value.ClientId);
    }

    [Fact]
    public void Cleared_while_a_call_is_out_its_answer_takes_nothing()
    {
        var q = new SummaryQueue(5);
        q.Add("{}", "a");
        q.Next();
        q.Clear();
        q.Add("{}", "b");
        Assert.Null(q.Next());
        q.Answered(true);
        Assert.Equal(1, q.Count);
        Assert.Equal("b", q.Next()!.Value.ClientId);
    }
}

public class PlanJsonTests
{
    private const string Plan = """
        {
          "id": "p1", "name": "  M4S prog ", "updated_at": "2026-11-01T10:00:00Z",
          "arena": { "shape": "square", "aspect": 1, "outlines": [], "color": "#223344" },
          "phases": [
            { "id": "a", "name": "Start", "time": "0:00", "notes": "", "objects": [
              { "type": "player", "x": 10, "y": -20, "rotation": 0, "size": 28, "color": "#3d6fd9", "label": "MT", "opacity": 1 },
              { "type": "cone", "x": 0, "y": 0, "rotation": 90, "size": 300, "angle": 120, "color": "#ff8a3d", "opacity": 0.3 },
              { "type": "mystery", "x": 1, "y": 2 },
              { "type": "circle", "x": "nope", "y": 0 },
              { "x": 0, "y": 0 }
            ] },
            { "id": "b", "name": "", "time": "1:24", "notes": "- Stack north\nthen out", "objects": "not a list" },
            { "id": "c", "name": "Third", "objects": [] }
          ],
          "game": { "content": 1234, "territory": 99, "duty": "M4S", "cues": [
            { "step": 2, "action": 100, "t": 50 },
            { "step": 1, "action": 200, "t": 20 },
            { "step": 7, "action": 300, "t": 30 },
            { "step": 1, "action": 0, "t": 30 },
            { "step": 1.5, "action": 400, "t": 30 },
            { "step": 1, "action": 500, "t": -3 }
          ] }
        }
        """;

    [Fact]
    public void Reads_a_plan_leaving_out_what_isnt_right()
    {
        var p = PlanJson.Read(Plan)!;
        Assert.NotNull(p);
        Assert.Equal("M4S prog", p.Name);
        Assert.Equal(3, p.Steps.Count);
        Assert.Equal("square", p.Arena.Shape);
        Assert.Equal(0x223344u, p.Arena.Color);
        Assert.Equal(3, p.Steps[0].Objects.Count);
        Assert.Equal(BoardKind.Player, p.Steps[0].Objects[0].Kind);
        Assert.Equal("MT", p.Steps[0].Objects[0].Label);
        Assert.Equal(BoardKind.Cone, p.Steps[0].Objects[1].Kind);
        Assert.Equal(120f, p.Steps[0].Objects[1].Angle);
        Assert.Equal(BoardKind.Other, p.Steps[0].Objects[2].Kind);
        Assert.Equal("Step 2", p.Steps[1].Name);
        Assert.Equal("1:24", p.Steps[1].Time);
        Assert.Empty(p.Steps[1].Objects);
        Assert.Equal(1234u, p.Game!.Content);
        // Only the two good cues, in time order.
        Assert.Equal(2, p.Game.Cues.Count);
        Assert.Equal(1, p.Game.Cues[0].Step);
        Assert.Equal(2, p.Game.Cues[1].Step);
    }

    [Fact]
    public void Keys_it_doesnt_know_in_the_link_to_the_game_are_passed_over()
    {
        // The site keeps the world centre the plan was made on (game.origin, for its Review), and may
        // add more: the plan still reads, its cues and all.
        const string json = """
            { "id": "p", "name": "x", "phases": [ { "id": "a", "name": "A" }, { "id": "b", "name": "B" } ],
              "game": { "content": 1018, "territory": 1252, "duty": "The Occult Crescent",
                "origin": { "cx": 620.5, "cz": 800 }, "someday": [1, { "x": "y" }],
                "cues": [ { "phase": "b", "step": 1, "action": 7, "t": 12, "extra": true } ] } }
            """;
        var p = PlanJson.Read(json)!;
        Assert.NotNull(p);
        Assert.Equal(1018u, p.Game!.Content);
        Assert.Equal(1252u, p.Game.Territory);
        var cue = Assert.Single(p.Game.Cues);
        Assert.Equal(1, cue.Step);
        Assert.Equal(7u, cue.Action);
    }

    [Fact]
    public void A_cue_follows_its_phase_id_when_steps_moved()
    {
        // The phases were reordered after the plan was made: "c" is now first, and "gone" was deleted.
        const string json = """
            { "id": "p", "name": "x", "phases": [ { "id": "c", "name": "C" }, { "id": "a", "name": "A" }, { "name": "no id" } ],
              "game": { "content": 1, "cues": [
                { "phase": "a", "step": 0, "action": 1, "t": 10 },
                { "phase": "c", "step": 2, "action": 2, "t": 20 },
                { "phase": "gone", "step": 2, "action": 3, "t": 30 },
                { "phase": "gone", "step": 9, "action": 4, "t": 40 },
                { "step": 1, "action": 5, "t": 50 }
              ] } }
            """;
        var p = PlanJson.Read(json)!;
        Assert.Equal("c", p.Steps[0].Id);
        Assert.Null(p.Steps[2].Id);
        var cues = p.Game!.Cues;
        Assert.Equal(3, cues.Count);
        Assert.Equal(1, cues.Single(c => c.Action == 1).Step);
        Assert.Equal(0, cues.Single(c => c.Action == 2).Step);
        // Its phase is gone: the cue goes too, even with a step number in range.
        Assert.DoesNotContain(cues, c => c.Action == 3);
        Assert.DoesNotContain(cues, c => c.Action == 4);
        // No phase at all: its step number.
        Assert.Equal(1, cues.Single(c => c.Action == 5).Step);
    }

    [Fact]
    public void Numbers_that_arent_finite_or_sensible_are_left_out_or_clamped()
    {
        var json = """
            { "id": "p", "phases": [ { "objects": [
              { "type": "circle", "x": 1e308, "y": -1e308, "size": -5, "opacity": 7, "color": "red" },
              { "type": "circle", "x": 0, "y": 0, "size": 1e300, "color": "#abc" }
            ] } ], "arena": { "shape": "hexagon", "aspect": 0, "color": 5 }, "game": { "content": -1, "cues": [] } }
            """;
        var p = PlanJson.Read(json)!;
        var o = p.Steps[0].Objects;
        Assert.Equal(2, o.Count);
        Assert.InRange(o[0].X, -5000f, 5000f);
        Assert.Equal(0f, o[0].Size);
        Assert.Equal(1f, o[0].Opacity);
        Assert.Equal(0xff8a3du, o[0].Color);
        Assert.Equal(0xaabbccu, o[1].Color);
        Assert.Equal(5000f, o[1].Size);
        Assert.Equal("circle", p.Arena.Shape);
        Assert.Equal(0.1f, p.Arena.Aspect);
        Assert.Null(p.Game);
        Assert.Equal("Untitled plan", p.Name);
    }

    [Fact]
    public void Caps_steps_objects_and_cues()
    {
        var objects = string.Join(",", Enumerable.Range(0, PlanJson.MaxObjects + 50).Select(i => $"{{\"type\":\"circle\",\"x\":{i},\"y\":0}}"));
        var phases = string.Join(",", Enumerable.Range(0, PlanJson.MaxSteps + 10).Select(i => i == 0 ? $"{{\"objects\":[{objects}]}}" : "{}"));
        var cues = string.Join(",", Enumerable.Range(0, PlanJson.MaxCues + 100).Select(i => $"{{\"step\":1,\"action\":{i + 1},\"t\":{i}}}"));
        var p = PlanJson.Read($"{{\"id\":\"p\",\"phases\":[{phases}],\"notes\":\"{new string('n', 9000)}\",\"game\":{{\"content\":1,\"cues\":[{cues}]}}}}")!;
        Assert.Equal(PlanJson.MaxSteps, p.Steps.Count);
        Assert.Equal(PlanJson.MaxObjects, p.Steps[0].Objects.Count);
        Assert.Equal(PlanJson.MaxCues, p.Game!.Cues.Count);
    }

    [Fact]
    public void Isnt_a_plan()
    {
        Assert.Null(PlanJson.Read(null));
        Assert.Null(PlanJson.Read("not json"));
        Assert.Null(PlanJson.Read("[]"));
        Assert.Null(PlanJson.Read("{\"id\":\"p\",\"phases\":[]}"));
        Assert.Null(PlanJson.Read("{\"phases\":[{}]}"));
        Assert.Null(PlanJson.Read(new string('[', 5000)));
    }

    [Fact]
    public void Reads_the_list_of_plans()
    {
        var list = PlanJson.List("""
            [ { "id": "a", "name": "A", "updated_at": "2026-11-01T10:00:00Z", "content": 1234, "duty": "M4S" },
              { "id": "b", "name": null, "updated_at": null, "content": null, "duty": null },
              { "name": "no id" }, 5, "x",
              { "id": "c", "name": "C", "content": 1.5 } ]
            """);
        Assert.Equal(3, list.Count);
        Assert.Equal(1234u, list[0].Content);
        Assert.Equal("2026-11-01T10:00:00Z", list[0].Stamp);
        Assert.NotNull(list[0].UpdatedAt);
        Assert.Equal("Untitled plan", list[1].Name);
        Assert.Null(list[1].Content);
        Assert.Null(list[2].Content);
        Assert.Empty(PlanJson.List("{}"));
        Assert.Empty(PlanJson.List(null));
        var many = "[" + string.Join(",", Enumerable.Range(0, 300).Select(i => $"{{\"id\":\"{i}\"}}")) + "]";
        Assert.Equal(PlanJson.MaxPlans, PlanJson.List(many).Count);
    }

    [Fact]
    public void A_steps_arena_carries_on_until_another()
    {
        var p = PlanJson.Read("""
            { "id": "p", "arena": { "shape": "circle" }, "phases": [ {}, { "arena": { "shape": "square" } }, {} ] }
            """)!;
        Assert.Equal("circle", p.ArenaAt(0).Shape);
        Assert.Equal("square", p.ArenaAt(1).Shape);
        Assert.Equal("square", p.ArenaAt(2).Shape);
    }

    [Fact]
    public void First_line_of_notes()
    {
        Assert.Equal("Stack north", PlanJson.FirstLine("\n  - Stack north\nthen out"));
        Assert.Equal(string.Empty, PlanJson.FirstLine("  \n "));
        Assert.True(PlanJson.FirstLine(new string('x', 500)).Length <= 201);
    }
}

public class CueFollowerTests
{
    // Steps: 0 the pull, 1 at 20 s (action 10), 2 at 40 s (action 20), 3 at 60 s (action 10 again).
    private static CueFollower Make() => new(new[] { new PlanCue(1, 10, 20), new PlanCue(2, 20, 40), new PlanCue(3, 10, 60) }, 4);

    [Fact]
    public void A_pull_starts_at_the_first_step()
    {
        var f = Make();
        f.Set(3);
        f.Start();
        Assert.Equal(0, f.Step);
        Assert.True(f.Following);
    }

    [Fact]
    public void Moves_ahead_by_the_clock_a_couple_of_seconds_early_once_a_cue_has_matched()
    {
        var f = Make();
        f.Start();
        // Nothing matched yet (a trash pull, say): the clock doesn't move it, and nothing is said.
        Assert.False(f.Tick(30));
        Assert.Equal(0, f.Step);
        Assert.Null(f.DueCallout(18));
        Assert.True(f.Hit(10, 20));
        Assert.Equal(1, f.Step);
        Assert.False(f.Tick(37.9));
        Assert.True(f.Tick(38.1));
        Assert.Equal(2, f.Step);
        Assert.True(f.Tick(58.5));
        Assert.Equal(3, f.Step);
    }

    [Fact]
    public void A_cast_moves_to_its_step_and_the_clock_follows_it()
    {
        var f = Make();
        f.Start();
        // The pull runs 10 s late: the step-1 cast begins at 26 s, going off at 30 (the plan said 20).
        Assert.True(f.Cast(10, 26, 4));
        Assert.Equal(1, f.Step);
        // So step 2 comes at 50 (less the lead), not 40.
        Assert.False(f.Tick(45));
        Assert.True(f.Tick(48.1));
        Assert.Equal(2, f.Step);
    }

    [Fact]
    public void A_repeated_action_matches_the_occurrence_nearest_its_time()
    {
        var f = Make();
        f.Start();
        f.Hit(10, 20);
        f.Tick(50);
        Assert.Equal(2, f.Step);
        // Action 10 at 60: the second occurrence (step 3), not the first.
        Assert.True(f.Cast(10, 57, 3));
        Assert.Equal(3, f.Step);
    }

    [Fact]
    public void A_real_cast_can_move_back_when_the_clock_ran_ahead()
    {
        var f = new CueFollower(new[] { new PlanCue(1, 10, 20), new PlanCue(2, 20, 30), new PlanCue(3, 30, 36) }, 4);
        f.Start();
        f.Hit(10, 20);
        f.Tick(34.5);
        Assert.Equal(3, f.Step);
        // The step-2 mechanic is only now going off (the pull is behind the plan).
        Assert.True(f.Hit(20, 35));
        Assert.Equal(2, f.Step);
        // And the clock takes it on from there.
        Assert.False(f.Tick(38.5));
        Assert.True(f.Tick(39.1));
        Assert.Equal(3, f.Step);
    }

    // The review's case: B (1) at 30 s and A (3) at 35 s in step 1, but only B is step 1's cue;
    // step 2 starts with C (2) at 55 s, and step 3 with A again at 80 s.
    private static CueFollower Repeated() => new(new[] { new PlanCue(1, 1, 30), new PlanCue(2, 2, 55), new PlanCue(3, 3, 80) }, 4);

    [Fact]
    public void An_action_used_again_in_an_earlier_step_doesnt_jump_ahead()
    {
        var f = Repeated();
        f.Start();
        Assert.True(f.Hit(1, 30));
        Assert.Equal(1, f.Step);
        // A in step 1: 45 s before step 3's A, so it's not that.
        Assert.False(f.Hit(3, 35));
        Assert.Equal(1, f.Step);
        Assert.False(f.Tick(40));
        Assert.Equal(1, f.Step);
        // The normal flow goes on: C's cast, then the clock, then A's real cast.
        Assert.True(f.Cast(2, 52, 3));
        Assert.Equal(2, f.Step);
        Assert.True(f.Cast(3, 76, 4));
        Assert.Equal(3, f.Step);
    }

    [Fact]
    public void A_match_wont_pass_a_cue_that_isnt_due_yet()
    {
        // Step 2 at 41 s, step 3's A at 45 s; A also goes off at 35 s, in step 1.
        var f = new CueFollower(new[] { new PlanCue(1, 1, 30), new PlanCue(2, 2, 41), new PlanCue(3, 3, 45) }, 4);
        f.Start();
        f.Hit(1, 30);
        // Within the window of step 3's A, but step 2 is still 6 s away: not taken.
        Assert.False(f.Hit(3, 35));
        Assert.Equal(1, f.Step);
        // The clock catches up to step 2, and then A is taken.
        Assert.True(f.Tick(39.5));
        Assert.Equal(2, f.Step);
        Assert.True(f.Hit(3, 44));
        Assert.Equal(3, f.Step);
    }

    [Fact]
    public void The_normal_flow_follows_each_cue_on_time()
    {
        var f = Repeated();
        f.Start();
        Assert.False(f.Tick(27.5));
        Assert.True(f.Cast(1, 26, 4));
        Assert.Equal(1, f.Step);
        // Its own hits don't match again, a few seconds on.
        Assert.False(f.Hit(1, 36));
        Assert.True(f.Tick(53.1));
        Assert.Equal(2, f.Step);
        Assert.True(f.Hit(3, 81));
        Assert.Equal(3, f.Step);
    }

    [Fact]
    public void A_casts_own_hits_dont_match_again_and_unknown_actions_do_nothing()
    {
        var f = Make();
        f.Start();
        f.Cast(10, 16, 4);
        Assert.Equal(1, f.Step);
        Assert.False(f.Hit(10, 20.2));
        Assert.Equal(1, f.Step);
        Assert.False(f.Cast(999, 21, 3));
        Assert.Equal(1, f.Step);
    }

    [Fact]
    public void A_cast_far_from_any_cue_is_not_taken()
    {
        var f = new CueFollower(new[] { new PlanCue(1, 10, 300) }, 2);
        f.Start();
        Assert.False(f.Cast(10, 5, 3));
        Assert.Equal(0, f.Step);
    }

    [Fact]
    public void Nothing_moves_when_not_following()
    {
        var f = Make();
        Assert.False(f.Tick(100));
        Assert.False(f.Cast(10, 16, 4));
        Assert.Null(f.DueCallout(19));
        f.Start();
        f.Stop();
        Assert.False(f.Tick(100));
        Assert.Equal(0, f.Step);
    }

    [Fact]
    public void Callouts_come_a_few_seconds_early_once_each()
    {
        var f = Make();
        f.Start();
        // None before the first match; that one is said as it's matched.
        Assert.Null(f.DueCallout(15.1));
        f.Hit(10, 20);
        Assert.Equal(1, f.DueCallout(20));
        Assert.Null(f.DueCallout(21));
        Assert.Null(f.DueCallout(34.9));
        Assert.Equal(2, f.DueCallout(35.1));
        Assert.Null(f.DueCallout(36));
        // Called once a pull; a new pull calls them again.
        f.Start();
        Assert.Null(f.DueCallout(15.5));
        f.Hit(10, 20);
        Assert.Equal(1, f.DueCallout(20));
    }

    [Fact]
    public void A_callout_well_past_isnt_said_late_but_a_cast_says_it()
    {
        var f = Make();
        f.Start();
        f.Hit(10, 20);
        Assert.Equal(1, f.DueCallout(20));
        // The clock is past step 2's time already (no frame came in between).
        Assert.Null(f.DueCallout(44));
        Assert.Equal(3, f.DueCallout(55.5));
        var g = Make();
        g.Start();
        Assert.True(g.Cast(10, 16, 4));
        Assert.Equal(1, g.DueCallout(16));
    }

    [Fact]
    public void An_exaflare_chain_doesnt_jump_to_its_own_next_step()
    {
        // A hit-only action going off every second from 50 to 66 s, split into steps at 50 and 58.6.
        var f = new CueFollower(new[] { new PlanCue(1, 7, 50), new PlanCue(2, 7, 58.6) }, 3);
        f.Start();
        Assert.True(f.Hit(7, 50));
        Assert.Equal(1, f.Step);
        for (var t = 51; t <= 56; t++)
        {
            Assert.False(f.Hit(7, t));
            Assert.False(f.Tick(t));
            Assert.Equal(1, f.Step);
        }
        // Past the quiet time for casts, the same action still doesn't take its next cue before it's due.
        Assert.False(f.Cast(7, 52, 0));
        Assert.Equal(1, f.Step);
        // The clock moves it on time, and the hits that follow keep it there.
        Assert.True(f.Tick(56.7));
        Assert.Equal(2, f.Step);
        for (var t = 58; t <= 66; t++) f.Hit(7, t);
        Assert.Equal(2, f.Step);
    }

    [Fact]
    public void Helpers_casting_together_are_one_mechanic()
    {
        // Four helpers start the same cast at 16 s, going off at 20; that action's next cue is 6 s on.
        var f = new CueFollower(new[] { new PlanCue(1, 5, 20), new PlanCue(2, 5, 26) }, 3);
        f.Start();
        Assert.True(f.Cast(5, 16, 4));
        for (var i = 0; i < 3; i++) Assert.False(f.Cast(5, 16, 4));
        for (var i = 0; i < 4; i++) Assert.False(f.Hit(5, 20));
        Assert.Equal(1, f.Step);
        // The real next cast moves it.
        Assert.True(f.Cast(5, 23, 3));
        Assert.Equal(2, f.Step);
    }

    [Fact]
    public void The_same_plan_read_again_mid_pull_keeps_what_was_said_and_matched()
    {
        var f = Make();
        f.Start();
        Assert.True(f.Cast(10, 16, 4));
        Assert.Equal(1, f.DueCallout(16));
        // The plan changed on the site (a note edited) and is read again.
        var g = Make();
        g.CarryFrom(f);
        Assert.True(g.Following);
        Assert.Equal(1, g.Step);
        // Step 1 isn't said again, and its own hits don't match again.
        Assert.Null(g.DueCallout(18));
        Assert.False(g.Hit(10, 21));
        // The clock goes on from the cast: step 2 at 40 (less the lead).
        Assert.False(g.Tick(37.5));
        Assert.True(g.Tick(38.1));
        Assert.Equal(2, g.Step);
    }

    [Fact]
    public void A_plan_without_cues_still_goes_back_to_the_start()
    {
        var f = new CueFollower(Array.Empty<PlanCue>(), 5);
        f.Set(4);
        f.Start();
        Assert.Equal(0, f.Step);
        Assert.False(f.Tick(1000));
        Assert.False(f.HasCues);
        f.Set(99);
        Assert.Equal(4, f.Step);
    }
}

public class SiteRpcTests
{
    private const string Token = "rpp_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";

    [Fact]
    public void Reads_answers_and_refusals()
    {
        var ok = SiteRpc.Read(HttpStatusCode.OK, "[1]");
        Assert.True(ok.Ok);
        Assert.Equal("[1]", ok.Body);
        Assert.True(SiteRpc.Read(HttpStatusCode.NoContent, null).Ok);
        var plan = SiteRpc.Read(HttpStatusCode.BadRequest, "{\"message\":\"That plan isn't yours.\",\"hint\":\"plan\"}");
        Assert.Equal("That plan isn't yours.", plan.Why);
        Assert.Equal("plan", plan.Hint);
        var token = SiteRpc.Read(HttpStatusCode.BadRequest, "{\"message\":\"Revoked.\",\"hint\":\"token\"}");
        Assert.True(token.TokenRefused);
        Assert.Contains("Link again", token.Why);
        var raw = SiteRpc.Read(HttpStatusCode.BadRequest, "{\"message\":\"relation public.x does not exist\",\"hint\":\"whatever\"}");
        Assert.DoesNotContain("relation", raw.Why);
        Assert.Null(raw.Hint);
        Assert.True(SiteRpc.Read(HttpStatusCode.ServiceUnavailable, "<html>").Unreachable);
        Assert.False(SiteRpc.Read(HttpStatusCode.NotFound, null).Unreachable);
    }

    [Fact]
    public async Task A_call_without_a_token_never_leaves()
    {
        using var http = new HttpClient();
        var r = await SiteRpc.CallAsync(http, "rp_plugin_plans", SiteRpc.Args("nope"), TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.True(r.TokenRefused);
    }

    [Fact]
    public async Task Only_the_plugins_own_functions_are_called()
    {
        using var http = new HttpClient();
        var r = await SiteRpc.CallAsync(http, "delete_everything", SiteRpc.Args(Token), TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.False(r.Unreachable);
    }

    [Fact]
    public async Task A_closed_session_calls_nothing()
    {
        var token = Token;
        var site = new SiteSession(() => token);
        Assert.True(site.Linked);
        site.Dispose();
        var r = await site.Call("rp_plugin_plans");
        Assert.False(r.Ok);
        Assert.Equal("Stopped.", r.Why);
    }

    [Fact]
    public void The_token_goes_in_the_body()
    {
        var args = SiteRpc.Args(Token);
        args["p_step"] = 3;
        var json = args.ToJsonString();
        Assert.Contains("\"p_token\":\"rpp_", json);
        Assert.Contains("\"p_step\":3", json);
    }
}

public class PacingTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Backoff_doubles_up_to_its_most_and_resets()
    {
        var b = new Backoff(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        Assert.True(b.Ready(T0));
        b.Failed(T0);
        Assert.False(b.Ready(T0.AddSeconds(4)));
        Assert.True(b.Ready(T0.AddSeconds(5)));
        b.Failed(T0);
        Assert.Equal(T0.AddSeconds(10), b.Until);
        b.Failed(T0);
        b.Failed(T0);
        Assert.Equal(T0.AddSeconds(15), b.Until);
        b.Succeeded();
        Assert.True(b.Ready(T0));
    }

    [Fact]
    public void A_clock_set_back_doesnt_hold_calls_up()
    {
        var b = new Backoff(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        b.Failed(T0);
        Assert.False(b.Ready(T0.AddSeconds(1)));
        // Set back an hour: ready, not in an hour.
        Assert.True(b.Ready(T0.AddHours(-1)));
        var live = new LiveShare();
        live.Changed(S(1), T0);
        Assert.Null(live.Due(T0.AddMilliseconds(100)));
        Assert.Equal(S(1), live.Due(T0.AddHours(-1))!.Value.What);
    }

    private static LiveStep S(int step) => new("p", step, "id" + step);

    [Fact]
    public void A_step_is_shared_once_it_settles_one_call_at_a_time()
    {
        var live = new LiveShare();
        live.Changed(S(1), T0);
        live.Changed(S(2), T0.AddMilliseconds(200));
        Assert.Null(live.Due(T0.AddMilliseconds(500)));
        var due = live.Due(T0.AddMilliseconds(900));
        Assert.Equal(S(2), due!.Value.What);
        Assert.Equal("id2", due.Value.What.Phase);
        // On its way: nothing else goes meanwhile.
        live.Changed(S(3), T0.AddSeconds(1));
        Assert.Null(live.Due(T0.AddSeconds(3)));
        live.Done(due.Value, true, T0.AddSeconds(3));
        Assert.Equal(S(3), live.Due(T0.AddSeconds(3))!.Value.What);
    }

    [Fact]
    public void The_last_step_always_goes_even_after_a_failure()
    {
        var live = new LiveShare();
        live.Changed(S(1), T0);
        var first = live.Due(T0.AddSeconds(1))!.Value;
        // Two more changes while it's on its way; the first call fails.
        live.Changed(S(2), T0.AddSeconds(1.1));
        live.Changed(S(3), T0.AddSeconds(1.2));
        live.Done(first, false, T0.AddSeconds(2));
        Assert.Null(live.Due(T0.AddSeconds(3)));
        // Once the wait's over, the last one goes, not the one that failed.
        var next = live.Due(T0.AddSeconds(7))!.Value;
        Assert.Equal(S(3), next.What);
        live.Done(next, true, T0.AddSeconds(7));
        Assert.Null(live.Due(T0.AddSeconds(9)));
    }

    [Fact]
    public void The_same_step_isnt_sent_twice_and_failures_wait()
    {
        var live = new LiveShare();
        live.Changed(S(1), T0);
        var due = live.Due(T0.AddSeconds(1))!.Value;
        live.Done(due, true, T0.AddSeconds(1));
        live.Changed(S(1), T0.AddSeconds(2));
        Assert.Null(live.Due(T0.AddSeconds(5)));
        live.Changed(S(2), T0.AddSeconds(6));
        due = live.Due(T0.AddSeconds(7))!.Value;
        live.Done(due, false, T0.AddSeconds(7));
        Assert.Null(live.Due(T0.AddSeconds(8)));
        Assert.Equal(S(2), live.Due(T0.AddSeconds(13))!.Value.What);
    }

    [Fact]
    public void A_reset_during_a_call_doesnt_stop_sharing_and_its_late_answer_is_ignored()
    {
        var live = new LiveShare();
        live.Changed(S(1), T0);
        var old = live.Due(T0.AddSeconds(1))!.Value;
        // Linked again while it's on its way.
        live.Reset();
        live.Changed(S(2), T0.AddSeconds(2));
        var next = live.Due(T0.AddSeconds(3))!.Value;
        Assert.Equal(S(2), next.What);
        // The old call's answer comes now: ignored, so the new one is still on its way.
        live.Done(old, true, T0.AddSeconds(3));
        live.Changed(S(4), T0.AddSeconds(3));
        Assert.Null(live.Due(T0.AddSeconds(4)));
        live.Done(next, true, T0.AddSeconds(4));
        Assert.Equal(S(4), live.Due(T0.AddSeconds(4))!.Value.What);
    }
}
