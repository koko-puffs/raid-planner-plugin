using System.Net;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using Xunit;

namespace RaidPlanner.Tests;

public class WordsTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(7.9, "0:07")]
    [InlineData(402, "6:42")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "0:00")]
    public void Clock(double seconds, string expected) => Assert.Equal(expected, Words.Clock(seconds));

    [Fact]
    public void When_says_today_and_yesterday()
    {
        var now = new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero).ToLocalTime();
        var earlier = now.AddHours(-1);
        Assert.StartsWith("today ", Words.When(earlier, now));
        Assert.StartsWith("yesterday ", Words.When(now.AddDays(-1), now));
        Assert.DoesNotContain("today", Words.When(now.AddDays(-30), now));
    }

    [Fact]
    public void Pull_line_and_name()
    {
        Assert.Equal("Pull 3 · 6:42 · Wiped", Words.PullLine(3, 402, Outcome.Wipe));
        Assert.Equal("The Omega Protocol · pull 1", Words.PullName("the Omega Protocol", 1));
        Assert.Equal("Unknown duty · pull 2", Words.PullName("  ", 2));
        Assert.True(Words.PullName(new string('x', 300), 4).Length <= 120);
    }

    [Fact]
    public void File_stem_leaves_out_what_windows_refuses()
    {
        var stem = Words.FileStem("A:B/C*D?", 2, new DateTimeOffset(2026, 11, 2, 21, 4, 0, TimeSpan.Zero));
        Assert.DoesNotContain(':', stem.Substring(16));
        Assert.DoesNotContain('/', stem);
        Assert.DoesNotContain('*', stem);
        Assert.EndsWith("pull 2", stem);
    }

    [Fact]
    public void Counts()
    {
        Assert.Equal("1 mechanic", Words.Count(1, "mechanic"));
        Assert.Equal("38 mechanics", Words.Count(38, "mechanic"));
        Assert.Equal("2 waymarks", Words.Count(2, "waymark"));
    }
}

public class RolesTests
{
    private static Partier P(ulong key, byte role, string job = "JOB") => new(key, job, role);

    [Fact]
    public void Full_party_gets_the_usual_spots_in_list_order()
    {
        var party = new[] { P(1, 3), P(2, 1), P(3, 4), P(4, 2), P(5, 1), P(6, 4), P(7, 2), P(8, 3) };
        var slots = Roles.Assign(party).Select(x => x.Slot).ToArray();
        Assert.Equal(new[] { "R1", "MT", "H1", "M1", "OT", "H2", "M2", "R2" }, slots);
    }

    [Fact]
    public void A_third_melee_takes_a_ranged_spot()
    {
        var party = new[] { P(1, 2), P(2, 2), P(3, 2), P(4, 1) };
        Assert.Equal(new[] { "M1", "M2", "R1", "MT" }, Roles.Assign(party).Select(x => x.Slot).ToArray());
    }

    [Fact]
    public void Past_eight_are_numbered()
    {
        var party = Enumerable.Range(1, 10).Select(i => P((ulong)i, 2)).ToArray();
        var slots = Roles.Assign(party).Select(x => x.Slot).ToList();
        Assert.Equal(10, slots.Distinct().Count());
        Assert.Contains("P9", slots);
        Assert.Contains("P10", slots);
    }

    [Fact]
    public void Role_words()
    {
        Assert.Equal("tank", Roles.RoleWord(1));
        Assert.Equal("healer", Roles.RoleWord(4));
        Assert.Equal("dps", Roles.RoleWord(2));
        Assert.Equal("dps", Roles.RoleWord(3));
    }
}

public class RecordingTests
{
    private static readonly Dictionary<uint, ActionInfo> Data = new()
    {
        [100] = new ActionInfo("Raidwide Roar", 2, 60, 0, "", 5),
        [200] = new ActionInfo("Cleaving Arc", 3, 40, 0, "gl_fan120_1bf", 4.7f),
        [300] = new ActionInfo("Pairs", 2, 6, 0, "", 0),
        [400] = new ActionInfo("Spread Out", 2, 5, 0, "", 0),
        [500] = new ActionInfo("attack", 1, 0, 0, "", 0),
        [600] = new ActionInfo("Crushing Blow", 1, 0, 0, "", 5),
        [700] = new ActionInfo("Exaflare", 2, 6, 0, "general_1bf", 0),
        // The party's own (since 0.3.0, with their category and recast): a weaponskill, a heal, a DoT,
        // a pet's, a cast spell, a buff, and a limit break.
        [9001] = new ActionInfo("Fast Blade", 1, 3, 0, "", 0, 3, 2.5f),
        [9002] = new ActionInfo("Cure II", 1, 30, 0, "", 2, 2, 2.5f),
        [9003] = new ActionInfo("Dia", 1, 25, 0, "", 0, 2, 2.5f),
        [9004] = new ActionInfo("Phantom Kamaitachi", 2, 3, 0, "", 0, 3, 0),
        [9005] = new ActionInfo("Fire IV", 2, 25, 5, "", 2.8f, 2, 2.5f),
        [9006] = new ActionInfo("Battle Litany", 2, 0, 30, "", 0, 4, 120),
        [9007] = new ActionInfo("Meteor", 2, 25, 8, "", 4.5f, 9, 0),
    };

    public static readonly Dictionary<uint, StatusInfo> StatusData = new()
    {
        [1138] = new StatusInfo("Physical Vulnerability Up", 215532),
        [1769] = new StatusInfo("Doom", 215502),
        [1000] = new StatusInfo("Party Buff", 210001),
        [1871] = new StatusInfo("Dia", 212675),
        [786] = new StatusInfo("Battle Litany", 212578),
    };

    /// <summary>
    /// A small made-up pull: a raidwide, a cone, a pair stack, spreads, autos, a buster and an exaflare
    /// chain; the cone's vulnerability on the two it hit, Doom on two, H2 dying to the exaflare, the
    /// boss going away and coming back (a second phase, with a second raidwide) and R2 dying to it.
    /// Since 0.3.0 also HP (the party's max, the boss's, everyone's in the frames, each hit's damage and
    /// HP before) and the party's own fight (the combat block: hits, a crit, a heal with overheal, a
    /// pet's hit, a DoT's ticks credited and one that named no one, a bleed, a cast finished and one
    /// cancelled, a buff and the DoT as statuses, a limit break). The site's test fixture is this,
    /// written by the plugin's own code (<see cref="FixtureTests"/>).
    /// </summary>
    public static Recording Sample(bool partyPositions = true) => SampleBuilder(partyPositions, out _);

    public static Recording SampleBuilder(bool partyPositions, out int phase)
    {
        var b = new RecordingBuilder(new DutyInfo("Test Arena (Savage)", 1, 99999), 3, new DateTimeOffset(2026, 11, 2, 20, 0, 0, TimeSpan.Zero), partyPositions, "0.1.0",
            id => Data.TryGetValue(id, out var a) ? a : null, id => StatusData.TryGetValue(id, out var st) ? st : null);
        var party = new[] { new Partier(11, "PLD", 1, 100_000), new Partier(12, "WAR", 1, 102_000), new Partier(13, "WHM", 4, 80_000),
            new Partier(14, "SGE", 4, 80_500), new Partier(15, "DRG", 2, 85_000), new Partier(16, "NIN", 2, 84_000), new Partier(17, "BRD", 3, 79_000),
            new Partier(18, "BLM", 3, 78_000) };
        b.SetParty(party);
        var boss = b.Actor(900, "The Boss", 5, 1001);
        var helper = b.Actor(901, "", 0.5f, 1002);
        b.SeenTargetable(boss, 1_000_000);
        b.Waymarks(0, new Dictionary<string, (float, float)> { ["A"] = (100, 85), ["B"] = (115, 100) });
        for (var t = 0.0; t <= 60; t += 0.25)
        {
            var at = new Dictionary<int, (float, float, float)> { [boss] = (100, 100, 0), [helper] = (90, 90, 0) };
            // The boss loses 10,000 a second; the party is at full HP but for H2 and R2 once they're down.
            var hp = new Dictionary<int, long> { [boss] = 1_000_000 - (long)(t * 10_000), [helper] = 1 };
            for (var i = 1; i <= 8; i++)
            {
                at[i] = (100 + i, 105, 3.14f);
                hp[i] = (i == 4 && t >= 45.5) || (i == 8 && t >= 56.2) ? 0 : party[i - 1].MaxHp;
            }
            b.Positions(t, at, hp);
        }
        var all = Enumerable.Range(1, 8).ToList();
        long[] Each(long dmg, int n) => Enumerable.Repeat(dmg, n).ToArray();
        long[] Hp(IEnumerable<int> who) => who.Select(i => party[i - 1].MaxHp).ToArray();
        b.Cast(5, boss, 100, 5, 100, 100, 0, 0, null, null);
        b.Hit(10, boss, 100, 100, 100, 0, 100, 100, all, Each(30_000, 8), new int[8], Hp(all));
        b.Cast(12, boss, 200, 4.7f, 100, 100, 3.14f, 1, null, null);
        b.Hit(16.8, boss, 200, 100, 100, 3.14f, 100, 100, new[] { 1, 5 }, new long[] { 45_000, 60_000 }, new[] { 0, 0 }, new long[] { 100_000, 85_000 });
        b.Hit(20, boss, 500, 100, 100, 0, 101, 105, new[] { 1 }, new long[] { 8_000 }, new[] { 4 }, new long[] { 100_000 });
        b.Hit(21, boss, 500, 100, 100, 0, 101, 105, new[] { 1 }, new long[] { 0 }, new[] { 128 }, new long[] { 92_000 });
        b.Marker(22, 3, 62);
        b.Marker(22, 7, 62);
        b.Hit(27, helper, 300, 90, 90, 0, 103, 105, new[] { 3, 4, 5, 6 }, Each(20_000, 4), new int[4], Hp(new[] { 3, 4, 5, 6 }));
        b.Hit(27, helper, 300, 90, 90, 0, 107, 105, new[] { 7, 8, 1, 2 }, Each(20_000, 4), new int[4], Hp(new[] { 7, 8, 1, 2 }));
        b.Cast(29, boss, 400, 3, 100, 100, 0, 0, null, null);
        foreach (var i in all) b.Hit(32, helper, 400, 90, 90, 0, 100 + i, 105, new[] { i }, new long[] { 15_000 }, new[] { 0 }, new[] { party[i - 1].MaxHp });
        b.Cast(35, boss, 600, 5, 100, 100, 0, 1, null, null);
        b.Hit(40, boss, 600, 100, 100, 0, 101, 105, new[] { 1 }, new long[] { 90_000 }, new[] { 0 }, new long[] { 100_000 });
        for (var k = 0; k < 5; k++)
            b.Hit(45 + k * 1.1, helper, 700, 90, 90, 0, 90 + k * 4, 90, k == 0 ? new[] { 4 } : Array.Empty<int>(),
                k == 0 ? new long[] { 104_880 } : Array.Empty<long>(), k == 0 ? new[] { 0 } : Array.Empty<int>(), k == 0 ? new long[] { 80_500 } : Array.Empty<long>());
        b.Cast(51, boss, 100, 5, 100, 100, 0, 0, null, null);
        b.Hit(56, boss, 100, 100, 100, 0, 100, 100, new[] { 1, 2, 3, 5, 6, 7, 8 }, new long[] { 30_000, 30_000, 30_000, 30_000, 30_000, 30_000, 90_000 },
            new int[7], new long[] { 100_000, 102_000, 80_000, 85_000, 84_000, 79_000, 70_000 });
        // The party's own fight: the PLD's hits (one a crit), the WHM's Cure II on the MT (a little
        // overheal), the NIN's Bunshin (a pet's hit, credited to the NIN), Dia's ticks (one the game
        // didn't say whose), a bleed on the MT, the BLM's Fire IV finished then one cancelled, the
        // DRG's Battle Litany on the party, Dia on the boss, and the BLM's Meteor (a limit break).
        for (var k = 0; k < 20; k++) b.CombatFx(2 + k * 2.5, 9001, 1, boss, 12_000 + k * 100, (int)FxKind.Damage | (k == 3 ? (int)FxFlags.Crit : 0), -1);
        b.CombatFx(17.5, 9002, 3, 1, 25_000, (int)FxKind.Heal, 80_000);
        b.CombatFx(28, 9004, 6, boss, 18_000, (int)FxKind.Damage | (int)FxFlags.Pet, -1);
        b.CombatFx(6, 9003, 3, boss, 4_000, (int)FxKind.Damage, -1);
        b.CombatFx(6, 9003, 3, boss, 0, (int)FxKind.Status, -1);
        for (var k = 1; k <= 9; k++) b.CombatTick(6 + k * 3, boss, k == 5 ? 0 : 3, 0, 2_500, false);
        b.CombatTick(21.5, 1, 101, 0, 3_000, false);
        b.PartyCast(8, 8, 9005, 2.8f);
        b.PartyUsed(10.8, 8, 9005);
        b.CombatFx(10.8, 9005, 8, boss, 55_000, (int)FxKind.Damage | (int)FxFlags.Crit | (int)FxFlags.DirectHit, -1);
        b.PartyCast(30, 8, 9005, 2.8f);
        b.PartyCast(34, 8, 9007, 4.5f);
        b.PartyUsed(38.5, 8, 9007);
        b.CombatFx(38.5, 9007, 8, boss, 2_400_000, (int)FxKind.Damage | (int)FxFlags.LimitBreak, -1);
        b.CombatFx(12, 9006, 5, 1, 0, (int)FxKind.Status, -1);
        b.Tethers(50, new[] { (boss, 2, 84u) });
        b.Tethers(55, Array.Empty<(int, int, uint)>());
        b.MapEffect(44, 0x10, 0x00020001);
        // Every frame (and at the moments things happen): the party's statuses, who's down, the boss's
        // targetability; the vulnerability and Doom with their stacks and time left as they run down.
        var frames = Enumerable.Range(0, 121).Select(i => i * 0.5).Concat(new[] { 6.1, 12.1, 16.9, 23, 31, 41, 45.5, 50, 56.2 }).Distinct().OrderBy(x => x);
        foreach (var t in frames)
        {
            b.BeginStatuses();
            for (var i = 1; i <= 8; i++)
            {
                b.StatusesOf(i);
                if (i == 1 && !b.StatusKnown(1, 1000)) b.StatusGained(t, 1, 1000, 0, 30, keep: false);
                if ((i == 1 || i == 5) && t >= 16.9 && !b.StatusKnown(t, i, 1138, 900, 1, (float)(30 - (t - 16.9))))
                    b.StatusGained(t, i, 1138, 900, StatusFrom.Enemy, boss, 1, 30);
                if ((i == 1 || i == 3) && t >= 23 && t < 31 && !b.StatusKnown(t, i, 1769, 900, 0, (float)(8 - (t - 23))))
                    b.StatusGained(t, i, 1769, 900, StatusFrom.Enemy, boss, 0, 8);
                // The DRG's Battle Litany on everyone (a party buff: a combat row, not an event) for 20 s.
                if (t >= 12.1 && t < 32.1 && !b.StatusKnown(t, i, 786, 15, 0, (float)(20 - (t - 12.1))))
                    b.StatusGained(t, i, 786, 15, StatusFrom.Party, 5, 0, (float)(20 - (t - 12.1)), 9006);
                b.Alive(t, i, (i == 4 && t >= 45.5) || (i == 8 && t >= 56.2));
            }
            // Dia on the boss, read with the boss's statuses, until it runs out at 36.1.
            b.StatusesOf(boss);
            if (t >= 6.1 && t < 36.1 && !b.StatusKnown(t, boss, 1871, 13, 0, (float)(30 - (t - 6.1))))
                b.StatusGained(t, boss, 1871, 13, StatusFrom.Party, 3, 0, (float)(30 - (t - 6.1)), 9003);
            b.EndStatuses(t);
            b.Targetable(t, boss, t < 41 || t >= 50);
            b.EndTargets(t);
        }
        phase = b.Phase;
        return b.Finish(61, Outcome.Wipe);
    }

    [Fact]
    public void Finish_sorts_the_enemies_and_drops_the_helpers_places()
    {
        var r = Sample();
        Assert.Equal(ActorKind.Boss, r.Actors.Single(a => a.Name == "The Boss").Kind);
        Assert.Equal(ActorKind.Helper, r.Actors.Single(a => a.Id == 102).Kind);
        Assert.All(r.Frames, f => Assert.False(f.At.ContainsKey(102)));
        Assert.Equal(8, r.Party.Count);
        Assert.Equal("MT", r.Party[0].Slot);
        Assert.Equal("OT", r.Party[1].Slot);
    }

    [Fact]
    public void Frames_are_kept_twice_a_second()
    {
        var r = Sample();
        Assert.InRange(r.Frames.Count, 119, 122);
        Assert.True(r.Frames[0].At.ContainsKey(1));
    }

    [Fact]
    public void Without_party_positions_only_enemies_are_kept()
    {
        var r = Sample(partyPositions: false);
        Assert.All(r.Frames, f => Assert.All(f.At.Keys, k => Assert.True(k > 100)));
        Assert.False(r.PartyPositions);
    }

    [Fact]
    public void Tethers_come_and_go()
    {
        var r = Sample();
        var tethers = r.Events.OfType<TetherEvent>().ToList();
        Assert.Equal(2, tethers.Count);
        Assert.True(tethers[0].On);
        Assert.False(tethers[1].On);
    }

    [Fact]
    public void Round_trips_through_its_file()
    {
        var r = Sample();
        var json = r.ToJson();
        var back = Recording.Parse(json);
        Assert.Equal(r.Duty, back.Duty);
        Assert.Equal(r.Pull, back.Pull);
        Assert.Equal(Outcome.Wipe, back.Outcome);
        Assert.Equal(r.Events.Count, back.Events.Count);
        Assert.Equal(r.Frames.Count, back.Frames.Count);
        Assert.Equal(r.Actions.Count, back.Actions.Count);
        Assert.Equal(r.Party.Select(p => p.Slot), back.Party.Select(p => p.Slot));
        Assert.Equal("gl_fan120_1bf", back.Actions[200].Omen);
        Assert.Equal(json, back.ToJson());
        Assert.Contains("\"format\":\"kokos-raid-recording\"", json);
        Assert.Contains("\"waymarks\"", json);
    }

    [Fact]
    public void Parse_refuses_what_isnt_a_recording()
    {
        Assert.Throws<FormatException>(() => Recording.Parse("{\"format\":\"something\"}"));
        Assert.Throws<FormatException>(() => Recording.Parse("not json"));
        Assert.Throws<FormatException>(() => Recording.Parse("{\"format\":\"kokos-raid-recording\",\"version\":99}"));
    }

    [Fact]
    public void Too_short_isnt_worth_keeping()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", id => Data.TryGetValue(id, out var a) ? a : null);
        Assert.False(b.Worthwhile(30));
        b.Cast(1, 101, 100, 1, 0, 0, 0, 0, null, null);
        Assert.False(b.Worthwhile(5));
        Assert.True(b.Worthwhile(30));
    }

    [Fact]
    public void A_pull_of_only_auto_attacks_isnt_kept()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", id => Data.TryGetValue(id, out var a) ? a : null);
        b.SetParty(new[] { new Partier(11, "PLD", 1) });
        var mob = b.Actor(900, "Trash", 1, 1);
        for (var t = 1; t < 15; t += 3) b.Hit(t, mob, 500, 0, 0, 0, 0, 0, new[] { 1 });
        Assert.False(b.Worthwhile(15));
    }

    [Fact]
    public void Numbers_that_arent_numbers_are_written_as_0()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", id => Data.TryGetValue(id, out var a) ? a : null);
        var boss = b.Actor(900, "Boss", float.NaN, 1);
        b.Cast(1, boss, 100, 5, float.PositiveInfinity, float.NaN, float.NegativeInfinity, 0, float.NaN, 3);
        b.Positions(0, new Dictionary<int, (float, float, float)> { [boss] = (float.NaN, 1, 2) });
        var json = b.Finish(double.PositiveInfinity, Outcome.Wipe).ToJson();
        var back = Recording.Parse(json);
        Assert.Equal(0, back.Events.OfType<CastEvent>().Single().X);
    }

    [Fact]
    public void Parse_says_FormatException_for_a_duplicate_key()
    {
        var json = Sample().ToJson();
        var bad = "{\"format\":\"x\"," + json[1..];
        Assert.Throws<FormatException>(() => Recording.Parse(bad));
    }

    [Fact]
    public void A_huge_enemy_is_still_the_boss()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", _ => null);
        var big = b.Actor(900, "Big", 5, 1);
        var bigger = b.Actor(901, "Bigger", 5, 2);
        b.SeenTargetable(big, 2_900_000_000);
        b.SeenTargetable(bigger, 3_000_000_000);
        var r = b.Finish(30, Outcome.Wipe);
        Assert.All(r.Actors, a => Assert.Equal(ActorKind.Boss, a.Kind));
    }

    [Fact]
    public void Waymarks_are_recorded_only_when_they_change()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", _ => null);
        var marks = new Dictionary<string, (float, float)> { ["A"] = (1, 2) };
        b.Waymarks(0, marks);
        b.Waymarks(1, new Dictionary<string, (float, float)>(marks));
        b.Waymarks(2, new Dictionary<string, (float, float)> { ["A"] = (5, 2) });
        b.Cast(3, 101, 5, 1, 0, 0, 0, 0, null, null);
        var r = b.Finish(20, Outcome.Ended);
        Assert.Equal(2, r.Events.OfType<WaymarksEvent>().Count());
    }
}

public class TimelineTests
{
    [Fact]
    public void Reads_the_sample_pull()
    {
        var r = RecordingTests.Sample();
        var lines = Timeline.Build(r);
        var names = lines.Select(l => l.Name).ToList();
        // Autos ("attack") never show; each cast and hit-only mechanic does, in order.
        Assert.DoesNotContain("attack", names);
        Assert.Equal(new[] { "Raidwide Roar", "Cleaving Arc", "Pairs", "Spread Out", "Crushing Blow", "Exaflare", "Raidwide Roar" }, names);
        Assert.Equal(MechanicKind.Raidwide, lines[0].Kinds[0]);
        Assert.Equal(MechanicKind.Cone, lines[1].Kinds[0]);
        Assert.Equal(MechanicKind.Stack, lines[2].Kinds[0]);
        Assert.Equal(2, lines[2].Count);
        Assert.Equal(MechanicKind.Spread, lines[3].Kinds[0]);
        Assert.Equal(MechanicKind.Tankbuster, lines[4].Kinds[0]);
        // The exaflare's hits, each about a second apart, are one line.
        Assert.Equal(5, lines[5].Count);
        Assert.Equal(10, lines[0].T, 1);
    }

    [Fact]
    public void Mechanic_count_matches_the_timeline()
    {
        var r = RecordingTests.Sample();
        Assert.Equal(Timeline.Build(r).Count, r.MechanicCount);
    }
}

public class StoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rp-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    [Fact]
    public void Saves_lists_and_reads_back()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        Assert.EndsWith(Words.Extension, info.File);
        Assert.Single(store.All);
        Assert.Equal("Test Arena (Savage)", store.All[0].Duty);
        Assert.Equal(7, store.All[0].Mechanics);
        var back = store.Read(info);
        Assert.Equal(3, back.Pull);

        // A new store reads the index.
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Single(again.All);
        Assert.Equal(info.File, again.All[0].File);
    }

    [Fact]
    public void Keeps_only_the_newest()
    {
        var store = new RecordingStore(folder) { Keep = 2 };
        store.Load();
        store.Save(RecordingTests.Sample());
        store.Save(RecordingTests.Sample());
        store.Save(RecordingTests.Sample());
        Assert.Equal(2, store.All.Count);
        Assert.Equal(2, Directory.GetFiles(folder, "*" + Words.Extension).Length);
        Assert.Equal(2, store.All.Select(i => i.File).Distinct().Count());
    }

    [Fact]
    public void Marks_sent_and_deletes()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        var sent = store.MarkSent(info, "6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", DateTimeOffset.UtcNow);
        Assert.NotNull(sent);
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Equal("6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", again.All[0].SentId);
        again.Delete(again.All[0]);
        Assert.Empty(again.All);
        Assert.Empty(Directory.GetFiles(folder, "*" + Words.Extension));
    }

    [Fact]
    public void Unsent_pulls_outlast_sent_ones()
    {
        var store = new RecordingStore(folder) { Keep = 2 };
        store.Load();
        var first = store.Save(RecordingTests.Sample());
        var sent = store.Save(RecordingTests.Sample());
        store.MarkSent(sent, "6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", DateTimeOffset.UtcNow);
        store.Save(RecordingTests.Sample());
        Assert.Equal(2, store.All.Count);
        Assert.Contains(store.All, i => i.File == first.File);
        Assert.DoesNotContain(store.All, i => i.File == sent.File);
    }

    [Fact]
    public void One_bad_file_doesnt_empty_the_list()
    {
        var store = new RecordingStore(folder);
        store.Load();
        store.Save(RecordingTests.Sample());
        File.Delete(Path.Combine(folder, RecordingStore.IndexFile));
        File.WriteAllText(Path.Combine(folder, "dup" + Words.Extension), "{\"format\":\"a\",\"format\":\"b\"}");
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Single(again.All);
    }

    [Fact]
    public void A_bad_index_entry_costs_only_itself()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        var list = RecordingStore.ReadIndex($"[{{\"file\":5}},{{\"file\":\"{info.File}\",\"pull\":3.5}},{{\"file\":\"{info.File}\",\"pull\":3}}]");
        Assert.Single(list);
    }

    [Fact]
    public void Sent_marks_outlive_a_lost_index()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        store.MarkSent(info, "6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", DateTimeOffset.UtcNow);
        File.Delete(Path.Combine(folder, RecordingStore.IndexFile));
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Equal("6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", again.All[0].SentId);
    }

    [Fact]
    public void Rebuilds_a_lost_index_from_the_files()
    {
        var store = new RecordingStore(folder);
        store.Load();
        store.Save(RecordingTests.Sample());
        File.Delete(Path.Combine(folder, RecordingStore.IndexFile));
        File.WriteAllText(Path.Combine(folder, "junk" + Words.Extension), "nope");
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Single(again.All);
        Assert.True(File.Exists(Path.Combine(folder, RecordingStore.IndexFile)));
    }
}

public class SiteLinkTests
{
    private const string Token = "rpp_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";

    [Fact]
    public void Token_shape()
    {
        Assert.Equal(47, Token.Length);
        Assert.True(SiteLink.LooksRight(Token));
        Assert.False(SiteLink.LooksRight("hsp_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ"));
        Assert.False(SiteLink.LooksRight(Token + "x"));
        Assert.Equal(Token, SiteLink.Clean($"  \"{Token}\"\n"));
        Assert.Equal("rpp_••••••••NOPQ", SiteLink.Masked(Token));
    }

    [Fact]
    public void Reads_an_id_back()
    {
        var r = SiteLink.Read(HttpStatusCode.OK, "\"6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10\"");
        Assert.True(r.Sent);
        Assert.Equal("6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", r.Id);
        Assert.False(SiteLink.Read(HttpStatusCode.OK, "null").Sent);
    }

    [Fact]
    public void Reads_refusals_in_words()
    {
        var token = SiteLink.Read(HttpStatusCode.BadRequest, "{\"message\":\"That token was revoked, or never made.\",\"hint\":\"token\"}");
        Assert.True(token.TokenRefused);
        Assert.Contains("Link again", token.Why);
        var rate = SiteLink.Read(HttpStatusCode.BadRequest, "{\"message\":\"That’s 30 sends this hour.\",\"hint\":\"rate\"}");
        Assert.Equal("That’s 30 sends this hour.", rate.Why);
        var db = SiteLink.Read(HttpStatusCode.NotFound, "{\"message\":\"Could not find the function public.rp_plugin_send\"}");
        Assert.DoesNotContain("function", db.Why);
        Assert.Contains("busy", SiteLink.Read((HttpStatusCode)429, null).Why);
    }

    [Fact]
    public void Only_the_sites_own_refusals_are_shown()
    {
        var raw = SiteLink.Read(HttpStatusCode.BadRequest, "{\"message\":\"canceling statement due to statement timeout\"}");
        Assert.DoesNotContain("statement", raw.Why);
        var full = SiteLink.Read(HttpStatusCode.BadRequest, "{\"message\":\"Your Raid Planner is full.\",\"hint\":\"full\"}");
        Assert.Equal("Your Raid Planner is full.", full.Why);
    }

    [Fact]
    public void Size_is_counted_as_the_database_counts_it()
    {
        Assert.Equal("{\"a\": 1, \"b\": [1, 2]}".Length, SiteLink.ServerSize("{\"a\":1,\"b\":[1,2]}"));
        Assert.Equal("{\"a\": \"x:,\\\"y\"}".Length, SiteLink.ServerSize("{\"a\":\"x:,\\\"y\"}"));
    }

    [Fact]
    public void A_pull_too_big_whole_is_sent_thinned()
    {
        var r = RecordingTests.Sample();
        var frame = r.Frames[0];
        var many = Enumerable.Range(0, 24000).Select(i => frame with { T = i * 0.5 }).ToList();
        var big = r.WithFrames(many, true).ToJson();
        Assert.True(SiteLink.ServerSize(big) > SiteLink.MaxBytes);
        var fit = SiteLink.Fit(big);
        Assert.NotNull(fit);
        Assert.True(fit!.Value.Thinned);
        Assert.True(SiteLink.ServerSize(fit.Value.Json) <= SiteLink.MaxBytes);
        Assert.Equal(r.Events.Count, Recording.Parse(fit.Value.Json).Events.Count);
        var small = r.ToJson();
        Assert.False(SiteLink.Fit(small)!.Value.Thinned);
    }

    [Fact]
    public void A_pulls_own_id_is_the_same_each_time()
    {
        var at = new DateTimeOffset(2026, 11, 2, 20, 0, 0, TimeSpan.Zero);
        Assert.Equal(SiteLink.ClientId("a.raidrec.json", at), SiteLink.ClientId("a.raidrec.json", at));
        Assert.NotEqual(SiteLink.ClientId("a.raidrec.json", at), SiteLink.ClientId("b.raidrec.json", at));
        Assert.Equal(32, SiteLink.ClientId("a.raidrec.json", at).Length);
    }

    [Fact]
    public void The_body_carries_the_recording_as_an_object()
    {
        var body = SiteLink.Body(Token, "X · pull 1", "{\"format\":\"kokos-raid-recording\"}");
        Assert.Contains("\"p_recording\":{\"format\"", body);
        Assert.Contains("\"p_token\":\"rpp_", body);
    }

    [Fact]
    public async Task A_bad_token_never_leaves()
    {
        using var http = new HttpClient();
        var r = await SiteLink.SendAsync(http, "nope", "x", "{}");
        Assert.True(r.TokenRefused);
    }
}
