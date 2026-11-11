using System.Text.Json.Nodes;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>The recording's round-three additions (the site's contract §2): written, read back, and old files still read.</summary>
public class FormatAdditionTests
{
    private static RecordingBuilder New(bool positions = true) => new(new DutyInfo("X", 1, 1), 1, new DateTimeOffset(2026, 11, 2, 20, 0, 0, TimeSpan.Zero), positions, "0.3.0",
        id => id == 100 ? new ActionInfo("Big Hit", 2, 10, 0, "", 0) : null,
        id => new StatusInfo(id == 3000 ? "Doom" : $"S{id}", 1));

    [Fact]
    public void The_sample_goes_through_its_file_unchanged_with_its_new_keys()
    {
        var r = RecordingTests.Sample();
        var json = r.ToJson();
        var back = Recording.Parse(json);
        Assert.Equal(json, back.ToJson());
        foreach (var key in new[] { "\"maxHp\":100000", "\"dmg\":[", "\"fx\":[", "\"hp\":[", "\"combat\":{\"v\":1", "\"cat\":9", "\"recast\":2.5", "\"n\":1" })
            Assert.Contains(key, json);
        Assert.Equal(100_000, back.Party[0].MaxHp);
        Assert.Equal(1_000_000, back.Actors.Single(a => a.Name == "The Boss").MaxHp);
        Assert.Equal(9, back.Actions[9007].Category);
        Assert.Equal(2.5f, back.Actions[9001].Recast);
        Assert.NotNull(back.Combat);
        Assert.Equal(r.Combat!.Fx.Count, back.Combat!.Fx.Count);
        Assert.Equal(r.Combat.Ticks.Count, back.Combat.Ticks.Count);
        Assert.Equal(r.Combat.Casts.Count, back.Combat.Casts.Count);
        Assert.Equal(r.Combat.Statuses.Count, back.Combat.Statuses.Count);
        var hit = back.Events.OfType<HitEvent>().First(h => h.Action == 200);
        Assert.Equal(new long[] { 45_000, 60_000 }, hit.Damage);
        Assert.Equal(new long[] { 100_000, 85_000 }, hit.Hp);
        Assert.Equal(400_000, back.Frames.Last(f => f.Hp != null && f.Hp.ContainsKey(101)).Hp![101]);
    }

    [Fact]
    public void An_old_file_reads_and_writes_as_it_was()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1) });
        var boss = b.Actor(900, "Boss", 5, 1);
        b.Cast(1, boss, 100, 2, 0, 0, 0, 0, null, null);
        b.Hit(3, boss, 100, 0, 0, 0, 0, 0, new[] { 1 });
        b.Positions(0, new Dictionary<int, (float, float, float)> { [1] = (1, 2, 3), [boss] = (4, 5, 6) });
        var r = b.Finish(20, Outcome.Wipe);
        r.Combat = null;
        var json = r.ToJson();
        foreach (var key in new[] { "maxHp", "combat", "\"dmg\"", "\"field\"", "\"cat\"", "\"recast\"", "byStatus", "\"re\"" })
            Assert.DoesNotContain(key, json);
        var back = Recording.Parse(json);
        Assert.Null(back.Combat);
        Assert.Null(back.Events.OfType<HitEvent>().Single().Damage);
        Assert.All(back.Frames, f => Assert.Null(f.Hp));
        Assert.Equal(json, back.ToJson());
        // Keys it doesn't know (a later plugin's) are passed over.
        var later = JsonNode.Parse(json)!.AsObject();
        later["somethingNew"] = 1;
        later["events"]![0]!["extra"] = "x";
        Assert.Equal(json, Recording.Parse(later.ToJsonString()).ToJson());
    }

    [Fact]
    public void The_combat_block_is_columns_of_time_deltas_in_order()
    {
        var c = new CombatLog();
        c.Fx.Add(new FxRow(0.5, 100, 1, 101, 5000, 1 | 8, -1));
        c.Fx.Add(new FxRow(0.3, 101, 2, 1, 900, 2, 45_000));
        c.Fx.Add(new FxRow(1.234, 102, 1, 101, 7000, 1, -1));
        var r = new Recording { Combat = c };
        var json = r.ToJson();
        Assert.Contains("\"fx\":{\"t\":[30,20,73],\"a\":[101,100,102],\"o\":[2,1,1],\"r\":[1,101,101],\"v\":[900,5000,7000],\"k\":[2,9,1],\"h\":[45000,-1,-1]}", json);
        var back = Recording.Parse(json).Combat!;
        Assert.Equal(new[] { 0.3, 0.5, 1.23 }, back.Fx.Select(f => f.T));
        Assert.Equal(new[] { 101u, 100u, 102u }, back.Fx.Select(f => f.Action));
        Assert.Equal(json, new Recording { Combat = back }.ToJson());
    }

    [Fact]
    public void A_malformed_table_is_left_out_never_thrown_and_a_negative_delta_reads_as_0()
    {
        var json = new Recording { Combat = new CombatLog() }.ToJson();
        var o = JsonNode.Parse(json)!.AsObject();
        o["combat"] = JsonNode.Parse("""
            {"v":1,
             "fx":{"t":[10,-5,"x"],"a":[1,2,3],"o":[1,1,1],"r":[101,101,101],"v":[10,20,30],"k":[1,1,1],"h":[-1,-1,-1]},
             "tick":{"t":[1,2],"on":[101],"src":[1,1],"s":[0,0],"v":[5,5],"heal":[0,0]},
             "cast":"nope",
             "st":{"t":[100],"on":[101],"src":[1],"s":[1871],"a":[16532],"dur":[30],"n":[0],"gain":[1]},
             "thinned":["cast","junk"]}
            """);
        var c = Recording.Parse(o.ToJsonString()).Combat!;
        Assert.Equal(new[] { 0.1, 0.1, 0.1 }, c.Fx.Select(f => f.T));
        Assert.Empty(c.Ticks);
        Assert.Empty(c.Casts);
        Assert.Equal(16532u, c.Statuses.Single().Action);
        Assert.Equal(new[] { "cast" }, c.Thinned);
        Assert.False(c.HasCasts);
        Assert.DoesNotContain("\"cast\":{", new Recording { Combat = c }.ToJson());
        Assert.Contains("\"thinned\":[\"cast\"]", new Recording { Combat = c }.ToJson());
    }

    [Fact]
    public void A_hits_amounts_stay_aligned_with_its_targets()
    {
        var json = new Recording().ToJson();
        var o = JsonNode.Parse(json)!.AsObject();
        o["events"] = JsonNode.Parse("""
            [{"t":1,"k":"hit","src":101,"action":5,"x":0,"z":0,"rot":0,"hx":0,"hz":0,"tgts":[1,0,2],"dmg":[100,200,300],"fx":[1,2,4],"hp":[10,20,-1]},
             {"t":2,"k":"hit","src":101,"action":5,"x":0,"z":0,"rot":0,"hx":0,"hz":0,"tgts":[1,2],"dmg":[100],"fx":"x"}]
            """);
        var hits = Recording.Parse(o.ToJsonString()).Events.OfType<HitEvent>().ToList();
        Assert.Equal(new[] { 1, 2 }, hits[0].Targets);
        Assert.Equal(new long[] { 100, 300 }, hits[0].Damage);
        Assert.Equal(new[] { 1, 4 }, hits[0].Fx);
        Assert.Equal(new long[] { 10, -1 }, hits[0].Hp);
        // Not as long as the targets: left out.
        Assert.Null(hits[1].Damage);
        Assert.Null(hits[1].Fx);
    }

    [Fact]
    public void Statuses_deaths_and_the_duty_carry_their_new_keys()
    {
        var r = new Recording { Duty = new DutyInfo("South Horn", 1252, 0, true) };
        r.Events.Add(new StatusEvent(1, 1, 1138, 101, 30, true, 2, true));
        r.Events.Add(new DeathEvent(2, 1, 2941, 101, true));
        var json = r.ToJson();
        Assert.Contains("\"field\":true", json);
        Assert.Contains("\"gain\":true,\"n\":2,\"re\":true", json);
        Assert.Contains("\"byStatus\":true", json);
        var back = Recording.Parse(json);
        Assert.True(back.Duty.Field);
        var st = back.Events.OfType<StatusEvent>().Single();
        Assert.Equal(2, st.Stacks);
        Assert.True(st.Refresh);
        Assert.True(back.Events.OfType<DeathEvent>().Single().ByStatus);
        Assert.Equal(json, back.ToJson());
    }

    [Fact]
    public void Hp_goes_in_the_frames_enemies_always_the_party_with_their_places()
    {
        foreach (var positions in new[] { true, false })
        {
            var b = New(positions);
            b.SetParty(new[] { new Partier(11, "PLD", 1, 120_000) });
            var boss = b.Actor(900, "Boss", 5, 1);
            var helper = b.Actor(901, "", 1, 2);
            b.SeenTargetable(boss, 5_000_000);
            b.SeenMaxHp(helper, 10);
            b.Cast(1, boss, 100, 2, 0, 0, 0, 0, null, null);
            b.Positions(0, new Dictionary<int, (float, float, float)> { [1] = (1, 2, 3), [boss] = (4, 5, 6), [helper] = (0, 0, 0) },
                new Dictionary<int, long> { [1] = 110_000, [boss] = 4_900_000, [helper] = 10 });
            var r = b.Finish(20, Outcome.Wipe);
            var f = r.Frames.Single();
            Assert.Equal(4_900_000, f.Hp![boss]);
            Assert.Equal(positions, f.Hp.ContainsKey(1));
            // A helper's place and HP aren't kept.
            Assert.False(f.Hp.ContainsKey(helper));
            Assert.Equal(120_000, r.Party.Single().MaxHp);
            Assert.Equal(5_000_000, r.Actors.Single(a => a.Id == boss).MaxHp);
            Assert.Contains(positions ? "\"1\":[1,2,3,110000]" : "\"101\":[4,5,6,4900000]", r.ToJson());
        }
    }
}

/// <summary>The builder's round-three additions: statuses' stacks and refreshes, the enemies' own and the party's, casts, death causes.</summary>
public class BuilderAdditionTests
{
    private static RecordingBuilder New() => new(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0.3.0", _ => null, id => new StatusInfo($"S{id}", 1));

    private static void Read(RecordingBuilder b, double t, int on, params (uint Id, ulong From, StatusFrom Kind, int Source, int Stacks, float Left, uint Action)[] now)
    {
        b.BeginStatuses();
        b.StatusesOf(on);
        foreach (var s in now)
            if (!b.StatusKnown(t, on, s.Id, s.From, s.Stacks, s.Left))
                b.StatusGained(t, on, s.Id, s.From, s.Kind, s.Source, s.Stacks, s.Left, s.Action);
        b.EndStatuses(t);
    }

    [Fact]
    public void Stacks_and_refreshes_are_recorded_a_new_gain_isnt_counted_twice()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1) });
        Read(b, 1, 1, (1138, 900, StatusFrom.Enemy, 101, 1, 10, 0));
        Read(b, 2, 1, (1138, 900, StatusFrom.Enemy, 101, 1, 9, 0));
        // Hit again: two stacks.
        Read(b, 3, 1, (1138, 900, StatusFrom.Enemy, 101, 2, 10, 0));
        Read(b, 3.5, 1, (1138, 900, StatusFrom.Enemy, 101, 2, 9, 0));
        // Put on again at the same stacks (its time back up).
        Read(b, 4, 1, (1138, 900, StatusFrom.Enemy, 101, 2, 10, 0));
        Read(b, 5, 1, (1138, 900, StatusFrom.Enemy, 101, 2, 9, 0));
        Read(b, 6, 1);
        var events = b.Finish(20, Outcome.Wipe).Events.OfType<StatusEvent>().ToList();
        Assert.Equal(new[] { (true, false, 1), (true, true, 2), (true, true, 2), (false, false, 0) }, events.Select(e => (e.Gain, e.Refresh, e.Stacks)));
        Assert.Equal(new[] { 1.0, 3, 4, 6 }, events.Select(e => e.T));
    }

    [Fact]
    public void A_self_cast_status_read_with_no_time_yet_isnt_a_refresh()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1) });
        Read(b, 1, 1, (76, 11, StatusFrom.Party, 1, 0, -5, 7535));
        Read(b, 1.5, 1, (76, 11, StatusFrom.Party, 1, 0, 19, 7535));
        Read(b, 2, 1, (76, 11, StatusFrom.Party, 1, 0, 18.5f, 7535));
        var rows = b.Finish(20, Outcome.Wipe).Combat!.Statuses;
        var row = Assert.Single(rows);
        Assert.True(row.Gain);
        Assert.Equal(7535u, row.Action);
        Assert.Equal(0, row.Duration);
    }

    [Fact]
    public void Enemies_own_statuses_are_events_the_partys_on_them_are_rows_and_others_nothing()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "WHM", 4) });
        var boss = b.Actor(900, "Boss", 5, 1);
        Read(b, 1, boss, (2000, 900, StatusFrom.Enemy, boss, 3, 0, 0), (1871, 11, StatusFrom.Party, 1, 0, 30, 16532), (1200, 77, StatusFrom.Other, 0, 0, 30, 0));
        Read(b, 2, boss, (2000, 900, StatusFrom.Enemy, boss, 3, 0, 0), (1200, 77, StatusFrom.Other, 0, 0, 30, 0));
        var r = b.Finish(20, Outcome.Wipe);
        var events = r.Events.OfType<StatusEvent>().ToList();
        var buff = Assert.Single(events);
        Assert.Equal(boss, buff.On);
        Assert.Equal(boss, buff.Source);
        Assert.Equal(3, buff.Stacks);
        var rows = r.Combat!.Statuses;
        Assert.Equal(2, rows.Count);
        Assert.Equal((1.0, boss, 1, 1871u, 16532u, true), (rows[0].T, rows[0].On, rows[0].Source, rows[0].Status, rows[0].Action, rows[0].Gain));
        Assert.False(rows[1].Gain);
        Assert.Equal(2.0, rows[1].T);
        Assert.True(r.Statuses.ContainsKey(1871));
        Assert.False(r.Statuses.ContainsKey(1200));
    }

    [Fact]
    public void A_cast_is_cancelled_unless_its_action_goes_off()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "BLM", 3) });
        b.PartyCast(1, 1, 3577, 2.8f);
        b.PartyUsed(3.8, 1, 3577);
        b.PartyCast(5, 1, 3577, 2.8f);
        // Moved, then cast something else.
        b.PartyCast(7, 1, 152, 2);
        // Another of the same action going off isn't this one finishing.
        b.PartyUsed(8, 1, 3577);
        b.PartyCast(15, 1, 3577, 2.8f);
        b.PartyUsed(17.8, 1, 3577);
        // Still casting as the pull ends: not cancelled.
        b.PartyCast(19.5, 1, 3577, 2.8f);
        var casts = b.Finish(20, Outcome.Wipe).Combat!.Casts;
        Assert.Equal(new[] { false, true, true, false, false }, casts.Select(c => c.Cancelled));
        Assert.Equal(new[] { 1.0, 5, 7, 15, 19.5 }, casts.Select(c => c.T));
        // An instant isn't a cast.
        var b2 = New();
        b2.PartyCast(1, 1, 3577, 0);
        Assert.Empty(b2.Finish(5, Outcome.Ended).Combat!.Casts);
    }

    [Fact]
    public void Deaths_come_from_a_lethal_tick_or_an_instant_death_not_a_dodge()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1), new Partier(12, "WAR", 1), new Partier(13, "WHM", 4) });
        foreach (var who in new[] { 1, 2, 3 }) b.Alive(0, who, false);
        // A bleed (its status known) after the last hit.
        b.Hit(9, 101, 600, 0, 0, 0, 0, 0, new[] { 1 }, new long[] { 5000 }, new[] { 0 }, new long[] { 50_000 });
        b.CombatTick(10, 1, 101, 2941, 4000, false);
        // An instant death with no amount.
        b.Hit(10, 101, 700, 0, 0, 0, 0, 0, new[] { 2 }, new long[] { 0 }, new[] { Effects.FxInstantDeath }, new long[] { 90_000 });
        // A hit that was dodged isn't what killed them; the one before is.
        b.Hit(9.5, 101, 800, 0, 0, 0, 0, 0, new[] { 3 }, new long[] { 70_000 }, new[] { 0 }, new long[] { 70_000 });
        b.Hit(10, 101, 900, 0, 0, 0, 0, 0, new[] { 3 }, new long[] { 0 }, new[] { Effects.FxDodged }, new long[] { 0 });
        foreach (var who in new[] { 1, 2, 3 }) b.Alive(10.5, who, true);
        var r = b.Finish(20, Outcome.Wipe);
        var deaths = r.Events.OfType<DeathEvent>().ToDictionary(d => d.Who);
        Assert.Equal((2941u, 101, true), (deaths[1].By, deaths[1].From, deaths[1].ByStatus));
        Assert.Equal((700u, false), (deaths[2].By, deaths[2].ByStatus));
        Assert.Equal(800u, deaths[3].By);
        Assert.True(r.Statuses.ContainsKey(2941));
        // The summary names the status.
        Assert.Contains(PullSummary.Build(r, 1)["deaths"]!.AsArray(), d => d!["by"]?.GetValue<string>() == "S2941");
    }

    [Fact]
    public void Phase_starts_are_kept()
    {
        var r = RecordingTests.SampleBuilder(true, out var phase);
        Assert.Equal(2, phase);
        Assert.Equal(new[] { 0.0, 50 }, PullStats.PhaseStarts(r));
    }
}

/// <summary>Fitting a pull to the site's limit: frames first, then the combat block's casts, then its ticks, never its effects.</summary>
public class FitTests
{
    private static Recording Big(int fx, int casts, int ticks)
    {
        var r = RecordingTests.Sample();
        var c = new CombatLog();
        for (var i = 0; i < fx; i++) c.Fx.Add(new FxRow(i * 0.01, 9001, 1 + i % 8, 101, 12_000 + i % 1000, 1, -1));
        for (var i = 0; i < casts; i++) c.Casts.Add(new CastRow(i * 0.5, 1 + i % 8, 9005, 2.8f, i % 3 == 0));
        for (var i = 0; i < ticks; i++) c.Ticks.Add(new TickRow(i * 0.01, 101, 1 + i % 8, 1871, 2500, false));
        return r.With(r.Frames, r.PartyPositions, c);
    }

    [Fact]
    public void The_casts_go_after_every_frame_and_the_file_says_so()
    {
        var r = Big(30_000, 200_000, 1000);
        var json = r.ToJson();
        Assert.True(SiteLink.ServerSize(json) > SiteLink.MaxBytes);
        var fit = SiteLink.Fit(json);
        Assert.NotNull(fit);
        Assert.True(fit!.Value.Thinned);
        Assert.True(SiteLink.ServerSize(fit.Value.Json) <= SiteLink.MaxBytes);
        var back = Recording.Parse(fit.Value.Json);
        Assert.Empty(back.Frames);
        Assert.Equal(new[] { "cast" }, back.Combat!.Thinned);
        Assert.Empty(back.Combat.Casts);
        Assert.Equal(30_000, back.Combat.Fx.Count);
        Assert.Equal(1000, back.Combat.Ticks.Count);
        Assert.Equal(r.Events.Count, back.Events.Count);
    }

    [Fact]
    public void Then_the_ticks_but_never_the_effects()
    {
        var both = SiteLink.Fit(Big(1000, 200_000, 200_000).ToJson());
        Assert.NotNull(both);
        var back = Recording.Parse(both!.Value.Json).Combat!;
        Assert.Equal(new[] { "cast", "tick" }, back.Thinned);
        Assert.Equal(1000, back.Fx.Count);
        Assert.Empty(back.Ticks);
        // Effects alone too many: it doesn't go (never sent without them).
        Assert.Null(SiteLink.Fit(Big(200_000, 0, 0).ToJson()));
    }

    [Fact]
    public void A_small_pull_goes_whole()
    {
        var json = RecordingTests.Sample().ToJson();
        var fit = SiteLink.Fit(json)!.Value;
        Assert.False(fit.Thinned);
        Assert.Same(json, fit.Json);
    }
}

/// <summary>The summary's round-three additions (contract §3): the boss's HP left, the phases, the stats.</summary>
public class SummaryAdditionTests
{
    [Fact]
    public void Boss_hp_phases_and_stats_of_the_sample()
    {
        var r = RecordingTests.SampleBuilder(true, out var phase);
        var s = PullSummary.Build(r, phase, isDot: id => id == 1871);
        // The boss lost 10,000 a second for 60 s: 40 % left.
        Assert.Equal(40.0, s["boss_hp"]!.GetValue<double>(), 2);
        Assert.Equal(new[] { 0, 50 }, s["phases"]!.AsArray().Select(x => x!.GetValue<int>()));
        var stats = s["stats"]!.AsObject();
        Assert.Equal(1, stats["v"]!.GetValue<int>());
        var slots = stats["slots"]!.AsArray().Select(x => x!.AsObject()).ToList();
        Assert.Equal(new[] { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" }, slots.Select(x => x["slot"]!.GetValue<string>()));
        var mt = slots[0];
        Assert.Equal(259_000, mt["dmg"]!.GetValue<long>());
        Assert.Equal(0.05, mt["crit"]!.GetValue<double>(), 3);
        Assert.Equal(47.5, mt["active"]!.GetValue<double>(), 1);
        // The hits on the MT (the dodged one 0) and the bleed.
        Assert.Equal(30_000 + 45_000 + 8_000 + 20_000 + 15_000 + 90_000 + 30_000 + 3_000, mt["taken"]!.GetValue<long>());
        Assert.Equal(1, mt["vuln"]!.GetValue<int>());
        Assert.Equal(0, mt["dd"]!.GetValue<int>());
        var whm = slots[2];
        // Cure II: 25,000 with 20,000 missing.
        Assert.Equal(20_000, whm["heal"]!.GetValue<long>());
        Assert.Equal(5_000, whm["over"]!.GetValue<long>());
        // Dia's hit and its ticks, the one that named no one shared to the only Dia on the boss.
        Assert.Equal(4_000 + 9 * 2_500, whm["dmg"]!.GetValue<long>());
        Assert.Equal(18_000, slots[5]["dmg"]!.GetValue<long>());
        var blm = slots[7];
        Assert.Equal(55_000, blm["dmg"]!.GetValue<long>());
        Assert.Equal(1.0, blm["crit"]!.GetValue<double>());
        Assert.Equal(1.0, blm["dh"]!.GetValue<double>());
        Assert.Equal(2_400_000, stats["lb"]!.GetValue<long>());
        // H2 died to the exaflare; R2 in the wipe (the last 6 s), not counted.
        Assert.Equal(1, slots[3]["deaths"]!.GetValue<int>());
        Assert.Equal(0, blm["deaths"]!.GetValue<int>());
        var hits = stats["hits"]!.AsArray().Select(h => h!.AsArray().Select(x => x!.GetValue<long>()).ToArray()).ToList();
        // Autos left out; the buster on the MT kept; each hit its own entry.
        Assert.Equal(15, hits.Count);
        Assert.DoesNotContain(hits, h => h[1] == 500);
        Assert.Equal(new long[] { 100, 100, 255, 8 * 30_000 }, hits[0]);
        Assert.Equal(new long[] { 400, 600, 1, 90_000 }, hits.Single(h => h[1] == 600));
        var debuffs = stats["debuffs"]!.AsArray().Select(h => h!.AsArray().Select(x => x!.GetValue<long>()).ToArray()).ToList();
        Assert.Equal(new long[] { 169, 1138, 1 | 16 }, Assert.Single(debuffs));
        var names = stats["names"]!.AsObject();
        Assert.Equal("Exaflare", names["700"]!.GetValue<string>());
        Assert.Equal("Physical Vulnerability Up", names["1138"]!.GetValue<string>());
        Assert.True(PullStats.Size(stats) <= PullStats.MaxBytes);
    }

    [Fact]
    public void Boss_hp_is_0_on_a_clear_and_null_when_unknown()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", _ => null);
        var boss = b.Actor(900, "Boss", 5, 1);
        b.SeenTargetable(boss, 1000);
        b.Targetable(0, boss, true);
        Assert.Null(PullStats.BossHp(b.Finish(30, Outcome.Wipe)));
        Assert.Equal(0, PullStats.BossHp(b.Finish(30, Outcome.Clear)));
        Assert.Null(PullStats.BossHp(new Recording()));
    }

    [Fact]
    public void The_main_boss_is_the_one_targetable_last()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", _ => null);
        var first = b.Actor(900, "First", 5, 1);
        var second = b.Actor(901, "Second", 5, 2);
        b.SeenTargetable(first, 2000);
        b.SeenTargetable(second, 1000);
        b.Targetable(0, first, true);
        b.Targetable(0, second, false);
        b.EndTargets(0);
        b.Positions(0, new Dictionary<int, (float, float, float)> { [first] = (0, 0, 0) }, new Dictionary<int, long> { [first] = 2000 });
        b.Targetable(10, first, false);
        b.Targetable(10, second, true);
        b.EndTargets(10);
        b.Positions(10, new Dictionary<int, (float, float, float)> { [first] = (0, 0, 0), [second] = (0, 0, 0) },
            new Dictionary<int, long> { [first] = 0, [second] = 250 });
        // Both bosses (the second has half the first's HP); the second was targetable last.
        Assert.Equal(25.0, PullStats.BossHp(b.Finish(20, Outcome.Wipe)));
    }

    [Fact]
    public void Stats_stay_under_6_KB_the_latest_hits_going_first()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0",
            id => new ActionInfo($"A rather long mechanic name number {id}", 2, 10, 0, "", 0), id => new StatusInfo("Damage Down", 1));
        b.SetParty(Enumerable.Range(1, 8).Select(i => new Partier((ulong)i, "PLD", (byte)(i % 4 + 1))).ToArray());
        for (var i = 0; i < 1000; i++) b.Hit(i * 0.5, 101, (uint)(1000 + i % 300), 0, 0, 0, 0, 0, new[] { 1 + i % 8, 1 + (i + 3) % 8 }, new long[] { 123_456, 654_321 }, new[] { 0, 0 }, new long[] { 1, 1 });
        b.BeginStatuses();
        b.StatusesOf(1);
        b.StatusGained(5, 1, 2911, 900, StatusFrom.Enemy, 101, 1, 30);
        b.EndStatuses(5);
        var r = b.Finish(600, Outcome.Wipe);
        var stats = PullStats.Stats(r);
        Assert.True(PullStats.Size(stats) <= PullStats.MaxBytes, $"{PullStats.Size(stats)} bytes");
        var hits = stats["hits"]!.AsArray();
        Assert.InRange(hits.Count, 1, PullStats.MaxHits - 1);
        // The earliest are kept.
        Assert.Equal(0, hits[0]![0]!.GetValue<long>());
        Assert.Single(stats["debuffs"]!.AsArray());
        Assert.Equal(1, stats["slots"]![0]!["dd"]!.GetValue<int>());
        // Every name the hits use is there.
        var names = stats["names"]!.AsObject();
        Assert.All(hits, h => Assert.True(names.ContainsKey(h![1]!.GetValue<long>().ToString())));
    }

    [Fact]
    public void The_meter_and_the_summary_add_up_the_same()
    {
        // The same effects through the parser into a recording and into the meter.
        var parser = CombatParserTests.Parser(out _);
        var session = new MeterSession();
        parser.Add(session);
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", _ => null);
        b.SetParty(new[] { new Partier(1, "PLD", 1, 100_000), new Partier(2, "WHM", 4, 100_000), new Partier(3, "BLM", 3, 100_000) });
        parser.Add(new ToRecording(b));
        var party = new[] { new PartyState(1, "a", "PLD", 1, 100_000, 100_000, true, false), new PartyState(2, "b", "WHM", 4, 100_000, 100_000, true, false),
            new PartyState(3, "c", "BLM", 3, 100_000, 100_000, true, true) };
        session.Frame(0, party, true);
        var rng = new Random(7);
        for (var i = 0; i < 300; i++)
        {
            var who = (uint)(1 + i % 3);
            parser.Action(CombatParserTests.Batch(i * 0.1, who, 100 + who, 0, (900, new[] { CombatParserTests.E(3, (byte)(rng.Next(4) * 0x20), value: (ushort)rng.Next(1000, 60000)) }, -1, -1, true)));
            if (i % 10 == 0) parser.Action(CombatParserTests.Batch(i * 0.1, 2, 135, 0, (1, new[] { CombatParserTests.E(4, value: 8000) }, rng.Next(80_000, 100_000), 100_000, false)));
        }
        session.Frame(30, party, true);
        var meter = session.Current!;
        var stats = PullStats.Stats(b.Finish(30, Outcome.Ended));
        var slots = stats["slots"]!.AsArray();
        for (var i = 0; i < 3; i++)
        {
            var row = meter.Rows.Single(r => r.Slot == slots[i]!["slot"]!.GetValue<string>());
            Assert.Equal(row.Damage, slots[i]!["dmg"]!.GetValue<long>());
            Assert.Equal(row.Healing, slots[i]!["heal"]!.GetValue<long>());
            Assert.Equal(row.Overheal, slots[i]!["over"]!.GetValue<long>());
            Assert.Equal(Math.Round(row.CritRate, 3), slots[i]!["crit"]!.GetValue<double>());
        }
    }

    /// <summary>What the game side's recording sink does, by the recording's numbers (entity ids 1–3 are party ids 1–3 here; the boss 900 is 101).</summary>
    private sealed class ToRecording : ICombatSink
    {
        private readonly RecordingBuilder b;

        public ToRecording(RecordingBuilder b) => this.b = b;

        public void Used(double t, uint member, uint caster, uint action) => b.PartyUsed(t, (int)member, action);

        public void Effect(in PartyEffect e) =>
            b.CombatFx(e.T, e.Action, (int)e.Member, e.Recipient == 900 ? 101 : (int)e.Recipient, e.Amount, (int)e.Kind | (int)e.Flags, e.Kind == FxKind.Heal ? e.HpBefore : -1);

        public void Taken(in TakenHit e)
        {
        }

        public void Tick(in TickEvent e, ReadOnlySpan<TickShare> shares) => b.CombatTick(e.T, e.Target == 900 ? 101 : (int)e.Target, e.SourceParty ? (int)e.Source : 0, e.Status, e.Amount, e.Heal);
    }
}

/// <summary>
/// The site's test fixture (<c>kokos-tools/src/features/raid-planner/model/__fixtures__/sample.raidrec.json</c>)
/// is the sample, written by this code: with <c>RP_FIXTURE_OUT</c> set to its path, this writes it.
/// </summary>
public class FixtureTests
{
    [Fact]
    public void Writes_the_sites_fixture_when_asked()
    {
        var json = RecordingTests.Sample().ToJson();
        Assert.Equal(json, Recording.Parse(json).ToJson());
        var path = Environment.GetEnvironmentVariable("RP_FIXTURE_OUT");
        if (string.IsNullOrWhiteSpace(path)) return;
        File.WriteAllText(path, json);
    }
}
