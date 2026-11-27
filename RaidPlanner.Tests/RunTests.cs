using System.Text.Json.Nodes;
using RaidPlanner.Core;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// The dungeons round: each pull's visit to the duty (<c>run</c>) and its main enemy (<c>boss</c>), in the file and in the
/// summary the site's progress tracker gets, so the site can show a dungeon or an alliance raid run by run, boss by boss.
/// </summary>
public class RunTests
{
    private static RecordingBuilder New() => new(new DutyInfo("The Vanguard", 1198, 1000), 4, new DateTimeOffset(2026, 11, 27, 20, 0, 0, TimeSpan.Zero),
        false, "0", _ => null);

    [Fact]
    public void The_sample_carries_its_run_and_its_boss_as_the_site_reads_them()
    {
        var o = JsonNode.Parse(RecordingTests.Sample().ToJson())!.AsObject();
        Assert.Equal("3f9c2a7d1e4b", o["run"]!["id"]!.GetValue<string>());
        Assert.Equal("2026-11-02T19:41:12.305Z", o["run"]!["startedAt"]!.GetValue<string>());
        Assert.Equal(1001u, o["boss"]!["dataId"]!.GetValue<uint>());
        Assert.Equal(2001u, o["boss"]!["nameId"]!.GetValue<uint>());
        Assert.Equal("The Boss", o["boss"]!["name"]!.GetValue<string>());
        // Alone (its helper never counts): its max HP, and 0 for the next.
        Assert.Equal(1_000_000u, o["boss"]!["hp"]!.GetValue<uint>());
        Assert.Equal(0u, o["boss"]!["next"]!.GetValue<uint>());
        var back = Recording.Parse(o.ToJsonString());
        Assert.Equal(new RunInfo("3f9c2a7d1e4b", new DateTimeOffset(2026, 11, 2, 19, 41, 12, 305, TimeSpan.Zero)), back.Run);
        Assert.Equal(new BossInfo(1001, 2001, "The Boss", 1_000_000, 0), back.Boss);
    }

    [Fact]
    public void The_boss_is_the_targetable_enemy_with_the_most_max_hp_never_a_helper()
    {
        var b = New();
        var pack = b.Actor(1, "Sentry R8", 1, 501, 601);
        var big = b.Actor(2, "Protector", 3, 502);
        var helper = b.Actor(3, "", 0.5f, 503, 603);
        b.SeenTargetable(pack, 200_000);
        b.SeenTargetable(big, 900_000);
        b.SeenMaxHp(helper, 5_000_000);
        // Its BNpcName only seen on a later frame.
        b.Actor(2, "Protector", 3, 502, 602);
        var r = b.Finish(60, Outcome.Ended);
        // The next is the pack's, not the helper's.
        Assert.Equal(new BossInfo(502, 602, "Protector", 900_000, 200_000), r.Boss);
    }

    [Fact]
    public void A_tie_goes_to_the_first_seen()
    {
        var b = New();
        var first = b.Actor(1, "Sentry G7", 1, 501, 601);
        var second = b.Actor(2, "Sentry S8", 1, 504, 604);
        b.SeenTargetable(first, 200_000);
        b.SeenTargetable(second, 200_000);
        var boss = b.Finish(30, Outcome.Ended).Boss!;
        Assert.Equal(501u, boss.DataId);
        // The next is as big as it.
        Assert.Equal((200_000u, 200_000u), (boss.Hp, boss.Next));
    }

    [Fact]
    public void A_pull_with_no_enemy_has_no_boss_and_neither_key_is_written_without_one()
    {
        var b = New();
        b.Actor(3, "", 0.5f, 503);
        var r = b.Finish(30, Outcome.Ended);
        Assert.Null(r.Boss);
        Assert.Null(r.Run);
        var o = JsonNode.Parse(r.ToJson())!.AsObject();
        Assert.False(o.ContainsKey("boss"));
        Assert.False(o.ContainsKey("run"));
        var s = PullSummary.Build(r, 1);
        Assert.False(s.ContainsKey("boss"));
        Assert.False(s.ContainsKey("run"));
    }

    [Fact]
    public void The_summary_carries_the_run_and_the_boss_as_the_file_does()
    {
        var b = New();
        b.SetRun(new RunInfo("a1b2c3d4e5f6", new DateTimeOffset(2026, 11, 27, 19, 52, 3, 81, TimeSpan.Zero)));
        var boss = b.Actor(2, "Protector", 3, 502, 602);
        b.SeenTargetable(boss, 900_000);
        var s = PullSummary.Build(b.Finish(60, Outcome.Ended), 1);
        Assert.Equal("a1b2c3d4e5f6", s["run"]!["id"]!.GetValue<string>());
        Assert.Equal("2026-11-27T19:52:03.081Z", s["run"]!["startedAt"]!.GetValue<string>());
        Assert.Equal(502u, s["boss"]!["dataId"]!.GetValue<uint>());
        Assert.Equal(602u, s["boss"]!["nameId"]!.GetValue<uint>());
        Assert.Equal("Protector", s["boss"]!["name"]!.GetValue<string>());
        Assert.Equal(900_000u, s["boss"]!["hp"]!.GetValue<uint>());
        Assert.Equal(0u, s["boss"]!["next"]!.GetValue<uint>());
        // Still one pull of the duty, numbered in it.
        Assert.Equal(4, s["pull"]!.GetValue<int>());
    }

    [Fact]
    public void Every_pull_of_a_visit_shares_its_run_and_a_copy_keeps_both()
    {
        var run = RunInfo.Begin(new DateTimeOffset(2026, 11, 27, 19, 52, 3, TimeSpan.Zero));
        Assert.Matches("^[0-9a-f]{12}$", run.Id);
        Assert.NotEqual(run.Id, RunInfo.NewId());
        var b = New();
        b.SetRun(run);
        var boss = b.Actor(2, "Protector", 3, 502, 602);
        b.SeenTargetable(boss, 900_000);
        var r = b.Finish(60, Outcome.Ended);
        var copy = r.WithFrames(r.Frames, false);
        Assert.Equal(run, copy.Run);
        Assert.Equal(r.Boss, copy.Boss);
    }

    [Fact]
    public void Run_and_boss_are_read_back_leniently()
    {
        var json = RecordingTests.Sample().ToJson();
        JsonObject With(string key, JsonNode? value)
        {
            var o = JsonNode.Parse(json)!.AsObject();
            o[key] = value;
            return o;
        }
        Assert.Null(Recording.Parse(With("run", new JsonObject { ["startedAt"] = "2026-11-02T19:41:12.305Z" }).ToJsonString()).Run);
        Assert.Null(Recording.Parse(With("run", new JsonObject { ["id"] = "x", ["startedAt"] = "not a time" }).ToJsonString()).Run);
        Assert.Null(Recording.Parse(With("run", "3f9c2a7d1e4b").ToJsonString()).Run);
        var longId = new string('a', 100);
        Assert.Equal(RunInfo.MaxId, Recording.Parse(With("run", new JsonObject { ["id"] = longId, ["startedAt"] = "2026-11-02T19:41:12Z" }).ToJsonString()).Run!.Id.Length);
        Assert.Null(Recording.Parse(With("boss", new JsonObject { ["name"] = "The Boss" }).ToJsonString()).Boss);
        Assert.Equal(new BossInfo(1001, 0, ""), Recording.Parse(With("boss", new JsonObject { ["dataId"] = 1001 }).ToJsonString()).Boss);
        // Its HP and the next's: whole numbers in range, else 0; no next without an hp.
        BossInfo? Boss(JsonNode? hp, JsonNode? next) =>
            Recording.Parse(With("boss", new JsonObject { ["dataId"] = 1001, ["hp"] = hp, ["next"] = next }).ToJsonString()).Boss;
        Assert.Equal((3_000_000u, 400_000u), (Boss(3_000_000, 400_000)!.Hp, Boss(3_000_000, 400_000)!.Next));
        Assert.Equal((0u, 0u), (Boss(-5, 400_000)!.Hp, Boss(-5, 400_000)!.Next));
        Assert.Equal((0u, 0u), (Boss(1.5, 400_000)!.Hp, Boss(1.5, 400_000)!.Next));
        Assert.Equal((0u, 0u), (Boss("3000000", null)!.Hp, Boss("3000000", null)!.Next));
        Assert.Equal((4_294_967_295u, 0u), (Boss(4_294_967_295L, 4_294_967_296L)!.Hp, Boss(4_294_967_295L, 4_294_967_296L)!.Next));
        // Without an hp, neither is written back.
        var noHp = JsonNode.Parse(Recording.Parse(With("boss", new JsonObject { ["dataId"] = 1001 }).ToJsonString()).ToJson())!["boss"]!.AsObject();
        Assert.False(noHp.ContainsKey("hp"));
        Assert.False(noHp.ContainsKey("next"));
        // An older file, with neither: read as none, written back without them.
        var old = JsonNode.Parse(json)!.AsObject();
        old.Remove("run");
        old.Remove("boss");
        var r = Recording.Parse(old.ToJsonString());
        Assert.Null(r.Run);
        Assert.Null(r.Boss);
        Assert.DoesNotContain("\"run\"", r.ToJson());
    }
}
