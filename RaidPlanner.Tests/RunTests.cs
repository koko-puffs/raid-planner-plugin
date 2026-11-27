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

    /// <summary>The party fought it: its eye on one of the party (the MT, 1).</summary>
    private static void Fight(RecordingBuilder b, params int[] enemies)
    {
        foreach (var e in enemies) b.Aggro(1, e, 1);
    }

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
        // The helper's hits count it as fought, and still it's never the boss.
        Fight(b, pack, big);
        b.Hit(2, helper, 600, 0, 0, 0, 0, 0, new[] { 1 }, new long[] { 5_000 });
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
        Fight(b, second, first);
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
        Fight(b, boss);
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
        Fight(b, boss);
        var r = b.Finish(60, Outcome.Ended);
        Assert.NotNull(r.Boss);
        var copy = r.WithFrames(r.Frames, false);
        Assert.Equal(run, copy.Run);
        Assert.Equal(r.Boss, copy.Boss);
    }

    // Only an enemy the party fought is the boss (the site's foughtIds): in a duty the recorder sees every enemy around.

    private static Dictionary<int, (float, float, float)> At(params int[] ids) => ids.ToDictionary(i => i, _ => (0f, 0f, 0f));

    [Fact]
    public void A_pack_beside_an_idle_boss_has_the_packs_biggest_for_its_boss()
    {
        // The Great Gubal Library: a trash pack at the door of a boss's room, the boss standing idle in it.
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1, 100_000), new Partier(12, "WHM", 4, 80_000) });
        var idle = b.Actor(10, "Demon Tome", 6, 3923, 3925);
        var bigOne = b.Actor(11, "Bibliovore", 2, 3930, 3931);
        var small = b.Actor(12, "Page 64", 1, 3932, 3933);
        var smaller = b.Actor(13, "Page 64", 1, 3932, 3933);
        b.SeenTargetable(idle, 5_000_000);
        b.SeenTargetable(bigOne, 300_000);
        b.SeenTargetable(small, 150_000);
        b.SeenTargetable(smaller, 120_000);
        for (var t = 0.0; t < 20; t += 0.5)
        {
            // The boss targetable all along, its eye on no one, its HP full; the pack's falling.
            b.Aggro(t, idle, 0);
            b.Aggro(t, bigOne, 1);
            b.Positions(t, At(idle, bigOne, small, smaller), new Dictionary<int, long>
            {
                [idle] = 5_000_000, [bigOne] = 300_000 - (long)(t * 10_000), [small] = 150_000, [smaller] = 120_000 - (long)(t * 5_000),
            });
        }
        // The small one hit the WHM; the party's damage on the boss's room is none.
        b.Hit(3, small, 500, 0, 0, 0, 0, 0, new[] { 2 }, new long[] { 3_000 });
        var r = b.Finish(20, Outcome.Ended);
        Assert.Equal(new BossInfo(3930, 3931, "Bibliovore", 300_000, 150_000), r.Boss);
        Assert.False(b.Engaged(idle));
        var s = PullSummary.Build(r, 1);
        Assert.Equal(3930u, s["boss"]!["dataId"]!.GetValue<uint>());
        Assert.Equal(150_000u, s["boss"]!["next"]!.GetValue<uint>());
    }

    [Fact]
    public void A_boss_pull_has_its_boss()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1, 100_000), new Partier(12, "WHM", 4, 80_000) });
        var boss = b.Actor(10, "Demon Tome", 6, 3923, 3925);
        var helper = b.Actor(11, "", 0.5f, 3924);
        b.SeenTargetable(boss, 5_000_000);
        b.Cast(2, boss, 100, 4, 0, 0, 0, 1, null, null);
        b.Hit(8, helper, 101, 0, 0, 0, 0, 0, new[] { 1, 2 }, new long[] { 20_000, 20_000 });
        b.CombatFx(9, 9, 1, boss, 12_000, 1, -1);
        var r = b.Finish(60, Outcome.Clear);
        Assert.Equal(new BossInfo(3923, 3925, "Demon Tome", 5_000_000, 0), r.Boss);
    }

    [Fact]
    public void In_a_fight_with_many_adds_the_boss_still_wins()
    {
        // The adds do most of the hitting; the boss only casts its raidwides (at no one) and takes the party's damage.
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1, 100_000), new Partier(12, "WHM", 4, 80_000) });
        var boss = b.Actor(10, "Liquid Flame", 6, 4000, 4001);
        b.SeenTargetable(boss, 2_000_000);
        b.Cast(1, boss, 100, 5, 0, 0, 0, 0, null, null);
        for (var i = 0; i < 8; i++)
        {
            var add = b.Actor(20 + (ulong)i, "Liquid Hand", 1, 4002, 4003);
            b.SeenTargetable(add, i == 0 ? 600_000u : 250_000u);
            b.Hit(2 + i, add, 500, 0, 0, 0, 0, 0, new[] { 1 }, new long[] { 4_000 });
            b.Aggro(2 + i, add, 2);
        }
        // A bigger enemy elsewhere never fought.
        b.SeenTargetable(b.Actor(99, "Elsewhere", 6, 4100, 4101), 9_000_000);
        b.CombatFx(4, 9, 1, boss, 12_000, 1 | 0x10, -1);
        var r = b.Finish(90, Outcome.Wipe);
        Assert.Equal(new BossInfo(4000, 4001, "Liquid Flame", 2_000_000, 600_000), r.Boss);
    }

    [Fact]
    public void A_pull_where_no_enemy_was_fought_has_no_boss()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1, 100_000), new Partier(12, "WHM", 4, 80_000) });
        var idle = b.Actor(10, "Demon Tome", 6, 3923, 3925);
        var other = b.Actor(11, "Bibliovore", 2, 3930, 3931);
        b.SeenTargetable(idle, 5_000_000);
        b.SeenTargetable(other, 300_000);
        for (var t = 0.0; t < 12; t += 0.5)
        {
            b.Aggro(t, idle, 0);
            b.Positions(t, At(idle, other, 1), new Dictionary<int, long> { [idle] = 5_000_000, [other] = 300_000, [1] = 90_000 - (long)t });
        }
        // What isn't fighting it: a cast at no one, the party's heal on itself, the party's status on itself, a tether between enemies.
        b.Cast(1, other, 100, 3, 0, 0, 0, 0, null, null);
        b.CombatFx(2, 120, 2, 1, 5_000, 2, 80_000);
        b.CombatFx(3, 7531, 1, 1, 0, 4, -1);
        b.Tethers(4, new[] { (idle, other, 1u) });
        var r = b.Finish(12, Outcome.Ended);
        Assert.Null(r.Boss);
        Assert.False(JsonNode.Parse(r.ToJson())!.AsObject().ContainsKey("boss"));
        Assert.False(PullSummary.Build(r, 1).ContainsKey("boss"));
    }

    [Fact]
    public void Each_way_of_fighting_an_enemy_counts_it()
    {
        RecordingBuilder Fresh(out int enemy)
        {
            var b = New();
            b.SetParty(new[] { new Partier(11, "PLD", 1, 100_000), new Partier(12, "WHM", 4, 80_000) });
            enemy = b.Actor(10, "Mob", 2, 700, 701);
            b.SeenTargetable(enemy, 300_000);
            return b;
        }
        var ways = new List<Action<RecordingBuilder, int>>
        {
            (b, e) => b.Hit(1, e, 500, 0, 0, 0, 0, 0, new[] { 2 }),
            (b, e) => b.Cast(1, e, 100, 3, 0, 0, 0, 2, null, null),
            (b, e) => b.StatusGained(1, 2, 2088, 10, StatusFrom.Enemy, e, 0, 10),
            (b, e) => b.Tethers(1, new[] { (e, 1, 1u) }),
            (b, e) => b.Tethers(1, new[] { (1, e, 1u) }),
            (b, e) => b.Aggro(1, e, 2),
            (b, e) => b.CombatFx(1, 9, 1, e, 8_000, 1, -1),
            (b, e) =>
            {
                b.Positions(0, At(e), new Dictionary<int, long> { [e] = 300_000 });
                b.Positions(0.5, At(e), new Dictionary<int, long> { [e] = 299_000 });
            },
            (b, e) =>
            {
                // Killed someone with its bleed (and did nothing else).
                b.Alive(0, 2, false);
                b.CombatTick(1, 2, e, 2088, 90_000, false, 80_000);
                b.Alive(1.2, 2, true);
            },
        };
        foreach (var way in ways)
        {
            var b = Fresh(out var e);
            Assert.False(b.Engaged(e));
            way(b, e);
            Assert.True(b.Engaged(e));
            Assert.Equal(700u, b.Finish(20, Outcome.Ended).Boss!.DataId);
        }
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
