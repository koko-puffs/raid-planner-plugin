using System.Text.Json.Nodes;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// A whole 0.3 pull for checking that the site reads the plugin's numbers as the plugin means them:
/// its recording and its summary, both written by the plugin's own code, are the site's fixtures
/// (<c>kokos-tools/src/features/raid-planner/model/__fixtures__/align.raidrec.json</c> and
/// <c>align.summary.json</c>; with <c>RP_ALIGN_OUT</c> set to that folder, this writes them), and the
/// site's <c>alignment.spec.ts</c> checks that its numbers for the recording (Review) and for the
/// summary (Progress) are the plugin's.
/// </summary>
public class AlignmentTests
{
    // The party in the party list's order (not the spots'), so a slot mask's bits (the summary's
    // `slots` order) aren't the spots' order: BLM R1, WHM H1, PLD MT, DRG M1, SCH H2, BRD R2, WAR OT, NIN M2.
    public const int Blm = 1, Whm = 2, Pld = 3, Drg = 4, Sch = 5, Brd = 6, War = 7, Nin = 8;

    private static readonly Dictionary<uint, ActionInfo> Actions = new()
    {
        // The enemies'.
        [100] = new ActionInfo("Raidwide Roar", 2, 60, 0, "", 5),
        [300] = new ActionInfo("Pillar Stack", 2, 6, 0, "", 0),
        [400] = new ActionInfo("Scatter", 2, 5, 0, "", 0),
        [500] = new ActionInfo("attack", 1, 0, 0, "", 0),
        [501] = new ActionInfo("Shot", 1, 25, 0, "", 0),
        [600] = new ActionInfo("Crushing Blow", 1, 0, 0, "", 5),
        [700] = new ActionInfo("Exaflare", 2, 6, 0, "general_1bf", 0),
        [800] = new ActionInfo("Spark", 1, 0, 0, "", 0),
        [1000] = new ActionInfo("Doom's End", 1, 0, 0, "", 0),
        [1100] = new ActionInfo("Ultimate End", 2, 60, 0, "", 8),
        // The party's (category, base recast).
        [9] = new ActionInfo("Fast Blade", 1, 3, 0, "", 0, 3, 2.5f),
        [31] = new ActionInfo("Heavy Swing", 1, 3, 0, "", 0, 3, 2.5f),
        [75] = new ActionInfo("True Thrust", 1, 3, 0, "", 0, 3, 2.5f),
        [2240] = new ActionInfo("Spinning Edge", 1, 3, 0, "", 0, 3, 2.5f),
        [25774] = new ActionInfo("Phantom Kamaitachi", 2, 3, 0, "", 0, 3, 0),
        [16495] = new ActionInfo("Burst Shot", 1, 25, 0, "", 0, 3, 2.5f),
        [7406] = new ActionInfo("Caustic Bite", 1, 25, 0, "", 0, 3, 2.5f),
        [7407] = new ActionInfo("Stormbite", 1, 25, 0, "", 0, 3, 2.5f),
        [25865] = new ActionInfo("Broil IV", 1, 25, 0, "", 1.5f, 2, 2.5f),
        [16540] = new ActionInfo("Biolysis", 1, 25, 0, "", 0, 2, 2.5f),
        [185] = new ActionInfo("Adloquium", 1, 30, 0, "", 2, 2, 2.5f),
        [190] = new ActionInfo("Physick", 1, 30, 0, "", 1.5f, 2, 2.5f),
        [25859] = new ActionInfo("Glare III", 1, 25, 0, "", 1.5f, 2, 2.5f),
        [16532] = new ActionInfo("Dia", 1, 25, 0, "", 0, 2, 2.5f),
        [135] = new ActionInfo("Cure II", 1, 30, 0, "", 2, 2, 2.5f),
        [137] = new ActionInfo("Regen", 1, 30, 0, "", 0, 2, 2.5f),
        [3577] = new ActionInfo("Fire IV", 2, 25, 5, "", 2.8f, 2, 2.5f),
        [36986] = new ActionInfo("High Thunder", 2, 25, 5, "", 0, 2, 2.5f),
        [3557] = new ActionInfo("Battle Litany", 2, 0, 30, "", 0, 4, 120),
        [205] = new ActionInfo("Meteor", 2, 25, 8, "", 4.5f, 9, 0),
    };

    /// <summary>The statuses, by the Status sheet's ids and English names (the site's lists go by these ids).</summary>
    private static readonly Dictionary<uint, StatusInfo> Statuses = new()
    {
        [2912] = new StatusInfo("Vulnerability Up", 215532),
        [2911] = new StatusInfo("Damage Down", 215501),
        [1138] = new StatusInfo("Physical Vulnerability Up", 215532),
        [2088] = new StatusInfo("Bleeding", 215503),
        [1871] = new StatusInfo("Dia", 212675),
        [1200] = new StatusInfo("Caustic Bite", 212616),
        [1201] = new StatusInfo("Stormbite", 212617),
        [1895] = new StatusInfo("Biolysis", 212836),
        [3871] = new StatusInfo("High Thunder", 212681),
        [786] = new StatusInfo("Battle Litany", 212578),
        [158] = new StatusInfo("Regen", 212626),
        [297] = new StatusInfo("Galvanize", 212801),
    };

    /// <summary>A status of the jobs' DoTs, as the game side finds them (by their English names: <see cref="CombatLists.DotNames"/>).</summary>
    public static bool IsDot(uint id) => Statuses.TryGetValue(id, out var s) && CombatLists.DotNames.Contains(s.Name);

    private static readonly long[] MaxHp = { 0, 78_000, 80_000, 100_000, 85_000, 80_500, 79_000, 102_000, 84_000 };

    /// <summary>When each died (none survived the enrage): BRD to a bleed, WHM to an instant death, NIN to an exaflare in the wipe, the rest to the enrage.</summary>
    private static readonly Dictionary<int, double> DiedAt = new()
    {
        [Brd] = 51.2, [Whm] = 60.2, [Nin] = 80.2, [Blm] = 84.2, [Pld] = 84.3, [Drg] = 84.4, [Sch] = 84.5, [War] = 84.6,
    };

    /// <summary>A status on someone for a while, declared every frame it's on (its stacks may change: a refresh).</summary>
    private sealed record On(int Who, uint Id, ulong From, StatusFrom Kind, int Source, double Start, double End, uint Action = 0, int Stacks = 0,
        double? StacksUpAt = null);

    /// <summary>
    /// The pull: a raidwide, a stack one short, an exaflare (one dodged it, one was invulnerable; the
    /// one it hit twice gets two stacks of Vulnerability Up), a buster on the MT (and its Physical
    /// Vulnerability Up, no failure), autos and a ranged auto, a single-target spark, spreads with one
    /// overlapping (Damage Down), the boss away for 12 s (a second phase), a bleed that kills the BRD,
    /// an instant death on the WHM, then the enrage: a wipe. The party's own fight: everyone's hits
    /// (crits, direct hits), the NIN's pet, the WAR on the add, DoTs (BRD's two, SCH's, WHM's, BLM's)
    /// with ticks credited, ticks that named no one shared out (one naming its status, one on the add
    /// with no DoT on it), heals with overheal, a HoT's ticks (and one that named no one), a shield,
    /// Battle Litany on the party, casts, and the BLM's limit break.
    /// </summary>
    public static Recording Pull()
    {
        var b = new RecordingBuilder(new DutyInfo("Alignment Arena (Savage)", 1, 99998), 12, new DateTimeOffset(2026, 11, 10, 21, 0, 0, TimeSpan.Zero), true,
            "0.3.0", id => Actions.TryGetValue(id, out var a) ? a : null, id => Statuses.TryGetValue(id, out var s) ? s : null);
        var party = new[]
        {
            new Partier(21, "BLM", 3, MaxHp[Blm]), new Partier(22, "WHM", 4, MaxHp[Whm]), new Partier(23, "PLD", 1, MaxHp[Pld]),
            new Partier(24, "DRG", 2, MaxHp[Drg]), new Partier(25, "SCH", 4, MaxHp[Sch]), new Partier(26, "BRD", 3, MaxHp[Brd]),
            new Partier(27, "WAR", 1, MaxHp[War]), new Partier(28, "NIN", 2, MaxHp[Nin]),
        };
        b.SetParty(party);
        var boss = b.Actor(900, "The Twinning", 5, 2001);
        var add = b.Actor(901, "Iron Add", 2, 2002);
        var helper = b.Actor(902, "", 0.5f, 2003);
        b.SeenTargetable(boss, 10_000_000);
        b.SeenTargetable(add, 1_000_000);
        var everyone = Enumerable.Range(1, 8).ToArray();
        long[] Each(long v, int n) => Enumerable.Repeat(v, n).ToArray();
        (float, float) At(int who) => who == Drg || who == Sch ? (104 + (who == Sch ? 0.6f : 0), 110) : (100 + who * 3, 112);

        // The party's HP as the frames read it: full, but for the PLD (who's healed and hit) and the dead.
        long Hp(int who, double t)
        {
            if (DiedAt.TryGetValue(who, out var died) && t >= died) return 0;
            if (who != Pld) return MaxHp[who];
            return t < 8 ? 100_000 : t < 15 ? 70_000 : t < 18 ? 52_000 : t < 27 ? 100_000 : t < 28 ? 10_000 : t < 33 ? 5_000 : 60_000;
        }

        // The enemies' mechanics.
        b.Cast(3, boss, 100, 5, 100, 100, 0, 0, null, null);
        b.Hit(8, boss, 100, 100, 100, 0, 100, 100, everyone, Each(30_000, 8), new int[8], everyone.Select(i => MaxHp[i]).ToArray());
        var stack = new[] { Blm, Whm, Pld, Drg, Sch, Brd, War };
        b.Hit(15, helper, 300, 90, 90, 0, 104, 112, stack, Each(18_000, 7), new int[7], stack.Select(i => Hp(i, 14.9)).ToArray());
        b.Hit(20, helper, 700, 95, 112, 0, 95, 112, new[] { Blm, Pld, War }, new long[] { 50_000, 0, 0 }, new[] { 0, Effects.FxDodged, Effects.FxInvulnerable },
            new long[] { 78_000, 100_000, 102_000 });
        b.Hit(21.1, helper, 700, 99, 112, 0, 99, 112, new[] { Blm }, new long[] { 50_000 }, new[] { 0 }, new long[] { 28_000 });
        b.Cast(22, boss, 600, 5, 100, 100, 0, Pld, null, null);
        b.Hit(27, boss, 600, 100, 100, 0, 109, 112, new[] { Pld }, new long[] { 90_000 }, new[] { Effects.FxCrit }, new long[] { 100_000 });
        b.Hit(28, boss, 500, 100, 100, 0, 109, 112, new[] { Pld }, new long[] { 5_000 }, new[] { 0 }, new long[] { 10_000 });
        b.Hit(29, add, 501, 110, 100, 0, 121, 112, new[] { War }, new long[] { 2_000 }, new[] { 0 }, new long[] { 102_000 });
        b.Hit(32, helper, 800, 90, 90, 0, 106, 112, new[] { Whm }, new long[] { 10_000 }, new[] { 0 }, new long[] { 80_000 });
        foreach (var who in everyone)
        {
            var (x, z) = At(who);
            // The DRG's spread took the SCH, standing in it, too.
            var on = who == Drg ? new[] { Drg, Sch } : new[] { who };
            b.Hit(35, helper, 400, 90, 90, 0, x, z, on, Each(15_000, on.Length), new int[on.Length], on.Select(i => Hp(i, 34.9)).ToArray());
        }
        // The bleed's ticks on the BRD (the boss's), the last killing them.
        foreach (var t in new[] { 45.0, 48.0, 51.0 }) b.CombatTick(t, Brd, boss, 2088, 30_000, false);
        b.Hit(60, helper, 1000, 90, 90, 0, 106, 112, new[] { Whm }, new long[] { 0 }, new[] { Effects.FxInstantDeath }, new long[] { 80_000 });
        b.Hit(80, helper, 700, 124, 112, 0, 124, 112, new[] { Nin }, new long[] { 120_000 }, new[] { 0 }, new long[] { 84_000 });
        b.Cast(76, boss, 1100, 8, 100, 100, 0, 0, null, null);
        var last = new[] { Blm, Pld, Drg, Sch, War };
        b.Hit(84, boss, 1100, 100, 100, 0, 100, 100, last, Each(999_999, 5), new int[5], last.Select(i => Hp(i, 83.9)).ToArray());

        // The party's own fight.
        bool BossUp(double t) => t < 40 || t >= 52;
        for (var k = 0; k < 34; k++)
        {
            var t = 1 + k * 2.5;
            if (!BossUp(t) || t >= 84) continue;
            if (t < DiedAt[Pld]) b.CombatFx(t, 9, Pld, boss, 12_000 + k * 100, (int)FxKind.Damage | (k % 4 == 3 ? (int)FxFlags.Crit : 0) | (k % 5 == 4 ? (int)FxFlags.DirectHit : 0), -1);
            if (t < DiedAt[War]) b.CombatFx(t + 0.2, 31, War, t >= 11 && t < 30 ? add : boss, 10_000, (int)FxKind.Damage | (k % 3 == 2 ? (int)FxFlags.Crit : 0), -1);
            if (t < DiedAt[Drg]) b.CombatFx(t + 0.4, 75, Drg, boss, 14_000, (int)FxKind.Damage | (k % 2 == 1 ? (int)FxFlags.DirectHit : 0), -1);
            if (t < DiedAt[Nin]) b.CombatFx(t + 0.6, 2240, Nin, boss, 13_000, (int)FxKind.Damage, -1);
            if (t < DiedAt[Brd]) b.CombatFx(t + 0.8, 16495, Brd, boss, 11_000, (int)FxKind.Damage | (k % 6 == 0 ? (int)(FxFlags.Crit | FxFlags.DirectHit) : 0), -1);
            if (t < DiedAt[Sch]) b.CombatFx(t + 1.0, 25865, Sch, boss, 9_000, (int)FxKind.Damage, -1);
            if (t < DiedAt[Whm]) b.CombatFx(t + 1.2, 25859, Whm, boss, 9_500, (int)FxKind.Damage, -1);
            if (t < DiedAt[Blm] && t + 1.4 < 37) b.CombatFx(t + 1.4, 3577, Blm, boss, 20_000, (int)FxKind.Damage | (k % 2 == 0 ? (int)FxFlags.Crit : 0), -1);
        }
        // The NIN's pet, credited to the NIN.
        b.CombatFx(28.3, 25774, Nin, boss, 18_000, (int)FxKind.Damage | (int)FxFlags.Pet, -1);
        b.CombatFx(33.3, 25774, Nin, boss, 18_000, (int)FxKind.Damage | (int)FxFlags.Pet | (int)FxFlags.Crit, -1);
        // DoTs going on (their hits, then the statuses below), and their ticks the game credited.
        b.CombatFx(4, 16532, Whm, boss, 10_000, (int)FxKind.Damage, -1);
        b.CombatFx(5, 7406, Brd, boss, 15_000, (int)FxKind.Damage, -1);
        b.CombatFx(6, 7407, Brd, boss, 16_000, (int)FxKind.Damage, -1);
        b.CombatFx(6, 16540, Sch, boss, 0, (int)FxKind.Status, -1);
        b.CombatFx(7, 36986, Blm, boss, 20_000, (int)FxKind.Damage | (int)FxFlags.Crit, -1);
        foreach (var t in new[] { 8.0, 11.0, 14.0, 17.0, 20.0 }) b.CombatTick(t, boss, Brd, 1200, 3_000, false);
        foreach (var t in new[] { 8.5, 11.5, 14.5 }) b.CombatTick(t, boss, Brd, 1201, 3_200, false);
        foreach (var t in new[] { 10.0, 16.0, 22.0 }) b.CombatTick(t, boss, Sch, 1895, 2_800, false);
        foreach (var t in new[] { 7.5, 13.5 }) b.CombatTick(t, boss, Whm, 1871, 2_600, false);
        // The BLM's High Thunder ticks after their last Fire IV (34.9): their active time runs to it.
        b.CombatTick(36.5, boss, Blm, 3871, 4_100, false);
        // Ticks the game named no one for: shared between the party's DoTs on the target then.
        b.CombatTick(9.05, boss, 0, 0, 10_003, false);
        b.CombatTick(12.05, boss, 0, 0, 8_000, false);
        b.CombatTick(13.55, boss, 0, 1201, 3_333, false);
        b.CombatTick(20.55, add, 0, 0, 5_000, false);
        b.CombatTick(37.55, boss, 0, 0, 6_001, false);
        // Heals: the SCH's Physick on the WAR, the WHM's Cure II on the PLD (7,000 of it overheal),
        // Regen's ticks (full, full, then half of it landing), one HoT tick that named no one; a shield.
        b.CombatFx(9, 190, Sch, War, 8_000, (int)FxKind.Heal, 72_000);
        b.CombatFx(18, 135, Whm, Pld, 55_000, (int)FxKind.Heal | (int)FxFlags.Crit, 52_000);
        foreach (var t in new[] { 21.0, 24.0 }) b.CombatTick(t, Pld, Whm, 158, 2_500, true);
        b.CombatTick(28.5, Pld, Whm, 158, 2_500, true);
        b.CombatTick(29.5, Pld, 0, 0, 4_000, true);
        b.CombatFx(26, 185, Sch, Pld, 0, (int)FxKind.Shield, -1);
        b.CombatFx(12, 3557, Drg, Drg, 0, (int)FxKind.Status | (int)FxFlags.Self, -1);
        // The BLM's casts (one cancelled) and the limit break.
        b.PartyCast(30.1, Blm, 3577, 2.8f);
        b.PartyCast(33.5, Blm, 205, 4.5f);
        b.PartyUsed(38, Blm, 205);
        b.CombatFx(38, 205, Blm, boss, 2_400_000, (int)FxKind.Damage | (int)FxFlags.LimitBreak, -1);

        // Statuses: the enemies' on the party (status events) and the party's on anyone (combat rows).
        var statuses = new List<On>
        {
            new(Blm, 2912, 902, StatusFrom.Enemy, helper, 20.3, 50.3, Stacks: 1, StacksUpAt: 21.4),
            new(Sch, 2911, 902, StatusFrom.Enemy, helper, 35.3, 65.3, Stacks: 1),
            new(Pld, 1138, 900, StatusFrom.Enemy, boss, 27.3, 57.3, Stacks: 1),
            new(Brd, 2088, 900, StatusFrom.Enemy, boss, 43.5, 51.3),
            new(boss, 1871, 22, StatusFrom.Party, Whm, 4.1, 34.0, 16532),
            new(boss, 1871, 22, StatusFrom.Party, Whm, 34.0, 64.0, 16532),
            new(boss, 1200, 26, StatusFrom.Party, Brd, 5.1, 50.1, 7406),
            new(boss, 1201, 26, StatusFrom.Party, Brd, 6.1, 51.1, 7407),
            new(boss, 1895, 25, StatusFrom.Party, Sch, 6.1, 36.0, 16540),
            new(boss, 3871, 21, StatusFrom.Party, Blm, 7.1, 37.0, 36986),
            new(Pld, 158, 22, StatusFrom.Party, Whm, 18.5, 36.5, 137),
            new(Pld, 297, 25, StatusFrom.Party, Sch, 26.1, 56.1, 185),
        };
        foreach (var who in everyone) statuses.Add(new On(who, 786, 24, StatusFrom.Party, Drg, 12.1, 32.1, 3557));

        for (var step = 0; step <= 950; step++)
        {
            var t = step / 10.0;
            // Twice a second (the builder keeps only those): everyone's place and HP.
            var at = new Dictionary<int, (float, float, float)> { [boss] = (100, 100, 0), [add] = (110, 100, 0), [helper] = (90, 90, 0) };
            var hp = new Dictionary<int, long> { [boss] = 10_000_000 - (long)(Math.Min(t, 84.6) * 60_000), [add] = t < 10 ? 1_000_000 : Math.Max(0, 1_000_000 - (long)((t - 10) * 50_000)), [helper] = 1 };
            foreach (var who in everyone)
            {
                var (x, z) = At(who);
                at[who] = (x, z, 3.14f);
                hp[who] = Hp(who, t);
            }
            b.Positions(t, at, hp);
            b.BeginStatuses();
            foreach (var who in everyone.Append(boss)) b.StatusesOf(who);
            // The one (or the second) running at a time of each status on each.
            foreach (var s in statuses.Where(s => t >= s.Start && t < s.End).GroupBy(s => (s.Who, s.Id)).Select(g => g.Last()))
            {
                var stacks = s.StacksUpAt is { } up && t >= up ? s.Stacks + 1 : s.Stacks;
                var left = (float)(s.End - t);
                var restarted = statuses.Any(o => o != s && o.Who == s.Who && o.Id == s.Id && o.End == s.Start && Math.Abs(t - s.Start) < 0.05);
                if (restarted || !b.StatusKnown(t, s.Who, s.Id, s.From, stacks, left))
                    b.StatusGained(t, s.Who, s.Id, s.From, s.Kind, s.Source, stacks, left, s.Action);
            }
            b.EndStatuses(t);
            foreach (var who in everyone) b.Alive(t, who, DiedAt[who] <= t);
            b.Targetable(t, boss, t < 40 || t >= 52);
            if (t >= 10) b.Targetable(t, add, t < 30);
            b.EndTargets(t);
        }
        return b.Finish(95, Outcome.Wipe);
    }

    /// <summary>The summary the plugin sends of it: the game side's lists (fail kinds by name, DoTs by name).</summary>
    public static JsonObject Summary(Recording r) => PullSummary.Build(r, 2, isDot: IsDot);

    [Fact]
    public void The_pull_is_what_it_says()
    {
        var r = Pull();
        Assert.Equal(new[] { "R1", "H1", "MT", "M1", "H2", "R2", "OT", "M2" }, r.Party.Select(p => p.Slot));
        Assert.Equal(new[] { 0.0, 52.0 }, PullStats.PhaseStarts(r));
        // The BRD to the bleed (a status), the WHM to the instant death, the rest to hits.
        var deaths = r.Events.OfType<DeathEvent>().ToDictionary(d => d.Who);
        Assert.True(deaths[Brd].ByStatus);
        Assert.Equal(2088u, deaths[Brd].By);
        Assert.Equal(1000u, deaths[Whm].By);
        Assert.Equal(1100u, deaths[War].By);
        // The DoT and the HoT going on, refreshed, and off.
        var dia = r.Combat!.Statuses.Where(s => s.Status == 1871).ToList();
        Assert.Equal(new[] { true, true, false }, dia.Select(s => s.Gain));
        Assert.Contains(r.Events.OfType<StatusEvent>(), s => s.Id == 2912 && s.Refresh && s.Stacks == 2);
    }

    [Fact]
    public void Its_summary()
    {
        var r = Pull();
        var s = Summary(r);
        // The boss lost 60,000 a second until the wipe (84.6 s) of 10 M.
        Assert.Equal(49.24, s["boss_hp"]!.GetValue<double>(), 2);
        Assert.Equal(new[] { 0, 52 }, s["phases"]!.AsArray().Select(x => x!.GetValue<int>()));
        var stats = s["stats"]!.AsObject();
        var slots = stats["slots"]!.AsArray().Select(x => x!.AsObject()).ToList();
        int Bit(int who) => 1 << (who - 1);
        var slot = (int who) => slots[who - 1];
        // The exaflare hit the BLM twice (Vulnerability Up, two stacks); the PLD dodged it, the WAR was invulnerable.
        Assert.Equal(2, slot(Blm)["vuln"]!.GetValue<int>());
        Assert.Equal(1, slot(Sch)["dd"]!.GetValue<int>());
        Assert.Equal(0, slot(Pld)["vuln"]!.GetValue<int>());
        var hits = stats["hits"]!.AsArray().Select(h => h!.AsArray().Select(x => x!.GetValue<long>()).ToArray()).ToList();
        Assert.Equal(new long[] { 200, 700, Bit(Blm), 50_000 }, hits.First(h => h[1] == 700));
        // Autos, the ranged auto and the spark aren't mechanics; the buster on the MT is.
        Assert.DoesNotContain(hits, h => h[1] is 500 or 501 or 800 or 1000);
        Assert.Contains(hits, h => h[1] == 600 && h[2] == Bit(Pld));
        // The BRD's and the WHM's deaths count; the NIN's (80.2) and the enrage's (84.2–84.6) are the wipe.
        Assert.Equal(new[] { 0, 1, 0, 0, 0, 1, 0, 0 }, slots.Select(x => x["deaths"]!.GetValue<int>()));
        // Ticks that named no one: 10,003 shared by BLM, WHM, SCH and BRD (the remainder the first's),
        // 8,000 the same, 3,333 named Stormbite (the BRD's), 6,001 by WHM and BRD once SCH's and BLM's
        // were off; none for the add's.
        var sch = 9_000L * Enumerable.Range(0, 34).Count(k => 1 + k * 2.5 < 84 && (1 + k * 2.5 < 40 || 1 + k * 2.5 >= 52)) + 3 * 2_800 + 2_500 + 2_000;
        Assert.Equal(sch, slot(Sch)["dmg"]!.GetValue<long>());
        // Active time runs from the first damage to the last, ticks too: the BLM's last is High Thunder's at 36.5.
        Assert.Equal(36.5 - 2.4, slot(Blm)["active"]!.GetValue<double>(), 1);
        Assert.Equal(2_400_000, stats["lb"]!.GetValue<long>());
        // Cure II: 55,000 with 48,000 missing; Regen: 2,500 twice on a full PLD, then 2,500 landing.
        Assert.Equal(8_000, slot(Sch)["heal"]!.GetValue<long>());
        Assert.Equal(48_000 + 2_500, slot(Whm)["heal"]!.GetValue<long>());
        Assert.Equal(7_000 + 5_000, slot(Whm)["over"]!.GetValue<long>());
    }

    [Fact]
    public void Writes_the_sites_alignment_fixtures_when_asked()
    {
        var r = Pull();
        var json = r.ToJson();
        Assert.Equal(json, Recording.Parse(json).ToJson());
        var summary = Summary(r).ToJsonString();
        var dir = Environment.GetEnvironmentVariable("RP_ALIGN_OUT");
        if (string.IsNullOrWhiteSpace(dir)) return;
        File.WriteAllText(Path.Combine(dir, "align.raidrec.json"), json);
        File.WriteAllText(Path.Combine(dir, "align.summary.json"), summary);
    }
}
