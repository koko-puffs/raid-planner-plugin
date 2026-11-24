using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// The meter for healers and tanks (0.9.0): what barriers absorbed (<see cref="ShieldBook"/>, credited to whoever's barrier was
/// on), the healers' casts on and off the GCD, self-healing, the big hits on each member with what was up for them
/// (<see cref="GuardRules"/>), and how long a tank's own mitigation was up while they held the boss.
/// </summary>
public class ProtectionTests
{
    private const uint Tank = 1, Healer = 2, Dps = 3, Boss = 900;

    // Statuses and actions by name, as the game side's English sheets would have them.
    private const uint Galvanize = 297, Rampart = 74, Reprisal = 1193, Hallowed = 82, Regen = 158;
    private const uint Adloquium = 185, Succor = 186, Lustrate = 189, Buster = 700, Raidwide = 701, Auto = 702, Clemency = 3541;

    private static readonly Dictionary<uint, string> Statuses = new()
    {
        [Galvanize] = "Galvanize", [Rampart] = "Rampart", [Reprisal] = "Reprisal", [Hallowed] = "Hallowed Ground", [Regen] = "Regen",
    };

    private static MeterSession New(out CombatParser parser)
    {
        var kinds = CombatKinds.FromNames(_ => null, id => Statuses.GetValueOrDefault(id), a => a is Adloquium or Succor or Clemency);
        var s = new MeterSession(a => $"A{a}", st => Statuses.GetValueOrDefault(st, $"S{st}"), kinds: kinds) { EndAfter = 4 };
        parser = CombatParserTests.Parser(out _);
        parser.Add(s);
        s.Place(1, 77, "Test Arena");
        return s;
    }

    private static PartyState[] Party(long tank = 100_000, long healer = 100_000, long dps = 100_000) => new[]
    {
        new PartyState(Tank, "Tank", "PLD", 1, tank, 100_000, true, false),
        new PartyState(Healer, "Healer", "SCH", 4, healer, 100_000, true, true),
        new PartyState(Dps, "Dps", "BLM", 3, dps, 100_000, true, false),
    };

    private static RawEffect Dmg(long amount) =>
        CombatParserTests.E(3, value: (ushort)(amount & 0xFFFF), p3: (byte)(amount >> 16), p4: (byte)(amount > 0xFFFF ? 0x40 : 0));

    /// <summary>An enemy action on members, each with their HP and barrier (% of 100,000) as read.</summary>
    private static void Hit(CombatParser p, double t, uint action, params (uint On, long Amount, long Hp, int Shield)[] on)
    {
        var b = new EffectBatch();
        b.Begin(t, Boss, 0, action, 1);
        foreach (var (target, amount, hp, shield) in on)
        {
            var k = b.Add(target);
            b.EffectsOf(k)[0] = Dmg(amount);
            b.Hp[k] = hp;
            b.MaxHp[k] = 100_000;
            b.Shield[k] = shield;
        }
        p.Action(b);
    }

    private static void Cast(CombatParser p, double t, uint by, uint action, params (uint On, RawEffect[] Effects, long Hp)[] on) =>
        p.Action(CombatParserTests.Batch(t, by, action, 0, on.Select(x => (x.On, x.Effects, x.Hp, 100_000L, false)).ToArray()));

    private static RawEffect HealOf(long amount) => CombatParserTests.E(4, value: (ushort)amount);

    private static RawEffect StatusOf(uint status) => CombatParserTests.E(14, value: (ushort)status);

    [Fact]
    public void A_barrier_soaks_what_it_holds_and_hits_before_the_game_shows_it_share_it()
    {
        var b = new ShieldBook();
        // 10% of 100,000: 10,000 soaked of a 40,000 hit.
        Assert.Equal(10_000, b.Absorb(Tank, 1, 10, 100_000, 40_000));
        // Read the same a moment later (the game hasn't shown the first yet): nothing left of it.
        Assert.Equal(0, b.Absorb(Tank, 1.2, 10, 100_000, 5_000));
        // A small hit is soaked whole, and the next on the same reading takes the rest.
        Assert.Equal(3_000, b.Absorb(Healer, 2, 8, 100_000, 3_000));
        Assert.Equal(5_000, b.Absorb(Healer, 2.1, 8, 100_000, 9_000));
        // A new reading (the game showed it, or a new barrier) starts again; so does the same one read long after.
        Assert.Equal(4_000, b.Absorb(Healer, 2.5, 4, 100_000, 9_000));
        Assert.Equal(4_000, b.Absorb(Healer, 9, 4, 100_000, 9_000));
        // No barrier, or nothing known: nothing.
        Assert.Equal(0, b.Absorb(Dps, 3, 0, 100_000, 9_000));
        Assert.Equal(0, b.Absorb(Dps, 3, -1, 100_000, 9_000));
        Assert.Equal(0, b.Absorb(Dps, 3, 20, -1, 9_000));
    }

    [Fact]
    public void Big_hits_are_told_apart_and_judged_by_what_was_up()
    {
        Assert.Equal(BigHitKind.Buster, GuardRules.Classify(1, 8, 0.45, 0.45));
        Assert.Equal(BigHitKind.SharedBuster, GuardRules.Classify(2, 8, 0.35, 0.3));
        Assert.Null(GuardRules.Classify(1, 8, 0.08, 0.08));
        Assert.Equal(BigHitKind.Raidwide, GuardRules.Classify(8, 8, 0.5, 0.4));
        Assert.Equal(BigHitKind.Raidwide, GuardRules.Classify(5, 8, 0.3, 0.2));
        // Half the party, or small hits on everyone, aren't raid-wides.
        Assert.Null(GuardRules.Classify(4, 8, 0.5, 0.4));
        Assert.Null(GuardRules.Classify(8, 8, 0.1, 0.05));
        Assert.Equal(MitigationVerdict.Died, GuardRules.Verdict(3, false, true));
        Assert.Equal(MitigationVerdict.Mitigated, GuardRules.Verdict(2, false, false));
        Assert.Equal(MitigationVerdict.Mitigated, GuardRules.Verdict(1, true, false));
        Assert.Equal(MitigationVerdict.Light, GuardRules.Verdict(1, false, false));
        Assert.Equal(MitigationVerdict.Unmitigated, GuardRules.Verdict(0, false, false));
        var shares = new long[3];
        GuardRules.Split(1001, new uint[] { 5, 6, 7 }, shares);
        Assert.Equal(new long[] { 335, 333, 333 }, shares);
        Assert.Equal(MitigationKind.Shield, CombatLists.MitigationOf("Galvanize"));
        Assert.Equal(MitigationKind.Shield, CombatLists.MitigationOf("Shade Shift"));
        Assert.Equal(MitigationKind.Enemy, CombatLists.MitigationOf("Dismantled"));
        Assert.Equal(MitigationKind.Invulnerable, CombatLists.MitigationOf("Living Dead"));
        Assert.Equal(MitigationKind.Mitigation, CombatLists.MitigationOf("Great Nebula"));
        Assert.Equal(MitigationKind.None, CombatLists.MitigationOf("Regen"));
    }

    [Fact]
    public void The_healers_barrier_is_credited_with_what_it_soaked_and_healing_stays_what_the_heals_put_back()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        // The encounter begins; Adloquium on the tank: a heal and Galvanize.
        p.Action(CombatParserTests.Batch(9, Dps, 100, 0, (Boss, new[] { Dmg(5000) }, -1, -1, true)));
        Cast(p, 10, Healer, Adloquium, (Tank, new[] { HealOf(5000), StatusOf(Galvanize) }, 50_000));
        s.Frame(10, Party(tank: 55_000), true);
        // The game side reads the tank's statuses: Galvanize from the healer, Rampart from the tank.
        s.Wearing(Tank);
        s.Wears(Tank, Galvanize, Healer);
        s.Wears(Tank, Rampart, Tank);
        s.Wears(Tank, Regen, Healer);
        // A buster: 40,000 on a 10% barrier (10,000 soaked); a hit before the game shows it, on the same reading: none.
        Hit(p, 12, Buster, (Tank, 40_000, 55_000, 10));
        Hit(p, 12.5, Auto, (Tank, 5_000, 55_000, 10));
        s.Frame(13, Party(tank: 10_000), true);
        var e = s.Current!;
        var healer = e.Rows.Single(r => r.Slot == "H1");
        Assert.Equal(10_000, healer.Absorbed);
        Assert.Equal(5_000, healer.Healing);
        Assert.Equal(10_000, e.Party.Absorbed);
        Assert.Equal(0, e.Rows.Single(r => r.Slot == "MT").Absorbed);
        Assert.Equal(10_000 / e.Duration, healer.AbsorbedPerSecond, 3);
        var prot = s.Protection(e.Id, healer.PartyId)!;
        Assert.Equal(10_000, prot.Absorbed);
        Assert.Equal(("Galvanize", 10_000L), prot.AbsorbedBy.Single());
        Assert.Equal(1, prot.GcdHeals);
        Assert.Equal(0, prot.OgcdHeals);
    }

    [Fact]
    public void Barriers_on_one_not_read_are_the_ones_put_on_lately_shared_between_their_healers()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        p.Action(CombatParserTests.Batch(9, Dps, 100, 0, (Boss, new[] { Dmg(5000) }, -1, -1, true)));
        // Two barriers on the dps, from the healer and (a barrier of the tank's) the tank; the dps's statuses are never read.
        Cast(p, 10, Healer, Adloquium, (Dps, new[] { HealOf(1000), StatusOf(Galvanize) }, 90_000));
        Cast(p, 10.5, Tank, 7385, (Dps, new[] { StatusOf(Galvanize) }, 91_000));
        s.Frame(11, Party(), true);
        Hit(p, 12, Raidwide, (Dps, 30_000, 91_000, 12));
        var e = s.Current!;
        Assert.Equal(6_000, e.Rows.Single(r => r.Slot == "H1").Absorbed);
        Assert.Equal(6_000, e.Rows.Single(r => r.Slot == "MT").Absorbed);
        // Long after, they're no one's.
        Hit(p, 60, Raidwide, (Dps, 30_000, 91_000, 12));
        s.Frame(61, Party(), true);
        Assert.Equal(12_000, s.Current!.Party.Absorbed);
    }

    /// <summary>
    /// 0.9.0's review: the members' status lists are read only during an encounter, so the one kept between encounters is the
    /// last encounter's last frame. An enemy's hit that starts the next one (before its first frame reads them afresh) was
    /// credited to the barriers on that old list, gone long since.
    /// </summary>
    [Fact]
    public void A_status_list_from_the_last_encounter_isnt_taken_for_what_they_carry_now()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        p.Action(CombatParserTests.Batch(9, Dps, 100, 0, (Boss, new[] { Dmg(5000) }, -1, -1, true)));
        s.Wearing(Tank);
        s.Wears(Tank, Galvanize, Healer);
        s.Frame(10, Party(), true);
        for (var t = 11.0; t <= 16.0 + 1e-9; t += 1) s.Frame(t, Party(), false);
        Assert.False(s.InEncounter);
        // A minute later the boss's hit begins the next one, on a barrier the healer's Galvanize from then isn't.
        Hit(p, 70, Buster, (Tank, 40_000, 100_000, 10));
        s.Frame(70.1, Party(tank: 60_000), true);
        Assert.True(s.InEncounter);
        var e = s.Current!;
        Assert.Equal(0, e.Rows.Single(r => r.Slot == "H1").Absorbed);
        // Read afresh in the encounter, a barrier of the healer's is the healer's again.
        s.Wearing(Tank);
        s.Wears(Tank, Galvanize, Healer);
        Hit(p, 72, Buster, (Tank, 40_000, 60_000, 10));
        s.Frame(72.1, Party(tank: 20_000), true);
        Assert.Equal(10_000, s.Current!.Rows.Single(r => r.Slot == "H1").Absorbed);
    }

    [Fact]
    public void A_heal_cast_counts_once_however_many_it_lands_on_and_off_the_gcd_is_told()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        p.Action(CombatParserTests.Batch(9, Dps, 100, 0, (Boss, new[] { Dmg(5000) }, -1, -1, true)));
        Cast(p, 10, Healer, Succor, (Tank, new[] { HealOf(3000) }, 60_000), (Healer, new[] { HealOf(3000) }, 99_000), (Dps, new[] { HealOf(3000) }, 60_000));
        Cast(p, 13, Healer, Succor, (Tank, new[] { HealOf(3000) }, 60_000));
        Cast(p, 14, Healer, Lustrate, (Tank, new[] { HealOf(8000) }, 95_000));
        // The tank heals himself (Clemency, a GCD): self-healing, less its overheal.
        Cast(p, 15, Tank, Clemency, (Tank, new[] { HealOf(9000) }, 95_000));
        s.Frame(16, Party(), true);
        var e = s.Current!;
        var healer = e.Rows.Single(r => r.Slot == "H1");
        var rows = s.Abilities(e.Id, healer.PartyId, MeterTab.Healing);
        var succor = rows.Single(r => r.ActionId == Succor);
        Assert.Equal(2, succor.Casts);
        Assert.Equal(4, succor.Hits);
        // 2,000 of the 3,000 on the healer was overheal.
        Assert.Equal(2_000, succor.Over);
        var lustrate = rows.Single(r => r.ActionId == Lustrate);
        Assert.Equal(1, lustrate.Casts);
        Assert.Equal(3_000, lustrate.Over);
        Assert.Equal(3_000.0 / 8_000, lustrate.OverRate, 6);
        var prot = s.Protection(e.Id, healer.PartyId)!;
        Assert.Equal(2, prot.GcdHeals);
        Assert.Equal(1, prot.OgcdHeals);
        Assert.Equal(1_000, prot.SelfHealing);
        var tank = s.Protection(e.Id, e.Rows.Single(r => r.Slot == "MT").PartyId)!;
        Assert.Equal(5_000, tank.SelfHealing);
        Assert.Equal(("A3541", 5_000L), tank.SelfHealingBy.Single());
        // The damage tab's rows have no casts.
        Assert.All(s.Abilities(e.Id, e.Rows.Single(r => r.Slot == "R1").PartyId, MeterTab.Damage), r => Assert.Equal(0, r.Casts));
    }

    [Fact]
    public void The_big_hits_on_a_member_say_what_was_up_and_how_it_went()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        p.Action(CombatParserTests.Batch(9, Dps, 100, 0, (Boss, new[] { Dmg(5000) }, -1, -1, true)));
        s.Frame(10, Party(), true);
        s.Wearing(Tank);
        s.Wears(Tank, Rampart, Tank);
        s.Wearing(Dps);
        s.EnemyWearing(Boss);
        s.EnemyWears(Boss, Reprisal, Tank);
        // A buster on the tank (Rampart, Reprisal: mitigated), an auto-attack (not big), a raid-wide (the dps had only Reprisal).
        Hit(p, 10.5, Buster, (Tank, 45_000, 100_000, 0));
        Hit(p, 11, Auto, (Tank, 4_000, 55_000, 0));
        // The boss's statuses are read four times a second: Reprisal still on.
        s.Frame(12, Party(), true);
        s.EnemyWearing(Boss);
        s.EnemyWears(Boss, Reprisal, Tank);
        Hit(p, 12, Raidwide, (Tank, 20_000, 51_000, 0), (Healer, 22_000, 100_000, 0));
        Hit(p, 12.1, Raidwide, (Dps, 21_000, 100_000, 0));
        s.Frame(13, Party(), true);
        // Reprisal gone; a buster on the dps with nothing up: it kills them.
        s.EnemyWearing(Boss);
        s.Frame(18, Party(), true);
        s.EnemyWearing(Boss);
        Hit(p, 18.5, Buster, (Dps, 70_000, 60_000, 0));
        s.Frame(19, Party(dps: 0), true);
        var e = s.Current!;
        var tank = s.Protection(e.Id, e.Rows.Single(r => r.Slot == "MT").PartyId)!;
        Assert.Equal(2, tank.Hits.Count);
        var buster = tank.Hits[0];
        Assert.Equal(BigHitKind.Buster, buster.Kind);
        Assert.Equal(1.5, buster.At, 6);
        Assert.Equal(45_000, buster.Amount);
        Assert.Equal(new[] { "Rampart", "Reprisal" }, buster.Up.Select(u => u.Name));
        Assert.True(buster.Up.All(u => u.Own));
        Assert.Equal(MitigationVerdict.Mitigated, buster.Verdict);
        Assert.Equal(BigHitKind.Raidwide, tank.Hits[1].Kind);
        Assert.Equal(3, tank.Hits[1].Targets);
        Assert.Equal((1, 0), (tank.Busters, tank.BustersBare));
        var dps = s.Protection(e.Id, e.Rows.Single(r => r.Slot == "R1").PartyId)!;
        Assert.Equal(2, dps.Hits.Count);
        Assert.Equal(MitigationVerdict.Light, dps.Hits[0].Verdict);
        Assert.Equal("MT", dps.Hits[0].Up.Single().By);
        Assert.False(dps.Hits[0].Up.Single().Own);
        Assert.Equal(MitigationVerdict.Died, dps.Hits[1].Verdict);
        Assert.Empty(dps.Hits[1].Up);
        Assert.Equal((1, 1), (dps.Busters, dps.BustersBare));
    }

    [Fact]
    public void The_healing_rows_bars_are_healing_then_shields_against_the_most_of_both()
    {
        var e = MeterPreview.PullAt(191);
        var (rows, _) = RaidPlanner.UI.Model.MeterView.Lines(e, MeterTab.Healing, MeterRank.EncDps);
        // Still ranked by HPS; the scholar's barriers striped after her bar, the dps have none.
        Assert.Equal(rows.Select(r => r.Row.Hps).OrderByDescending(x => x), rows.Select(r => r.Row.Hps));
        var sch = rows.Single(r => r.Row.Slot == "H2");
        Assert.True(sch.Striped > 0);
        Assert.Equal(0, rows.Single(r => r.Row.Slot == "M1").Striped);
        var top = rows.Max(r => r.Bar + r.Striped);
        Assert.Equal(1, top, 4);
        Assert.All(rows, r => Assert.InRange(r.Bar + r.Striped, 0, 1.0001f));
        Assert.Equal(e.Rows.Where(r => !r.IsLimitBreak).Sum(r => r.Absorbed), e.Party.Absorbed);
        Assert.EndsWith("% over", RaidPlanner.UI.Model.MeterView.HealFoot(e));
        Assert.Contains("estimate", RaidPlanner.UI.Model.MeterView.HealTip(sch.Row));
        // Other tabs' lines have no stripes.
        Assert.All(RaidPlanner.UI.Model.MeterView.Lines(e, MeterTab.Damage, MeterRank.EncDps).Rows, r => Assert.Equal(0, r.Striped));
    }

    [Fact]
    public void The_cards_and_hit_lines_say_what_the_numbers_are()
    {
        var preview = new MeterPreview(() => 0);
        var e = preview.Current!;
        var mt = e.Rows.Single(r => r.Slot == "MT");
        var prot = preview.Protection(e.Id, mt.PartyId)!;
        var cards = RaidPlanner.UI.Model.MeterGuard.GuardCards(mt, prot);
        Assert.Equal(new[] { "Damage taken", "Mitigation up", "Tank busters", "Self-healing" }, cards.Select(c => c.Label));
        Assert.EndsWith("of the party's", cards[0].Sub);
        var line = RaidPlanner.UI.Model.MeterGuard.Line(prot.Hits[0]);
        Assert.Equal("0:41", line.Time);
        Assert.StartsWith("Raid-wide", line.Kind);
        Assert.Equal("96,300", line.Amount);
        Assert.Equal("61,600", line.After);
        Assert.Equal("Mitigated", line.Verdict);
        var bare = new BigHit(10, 1, "Smash", BigHitKind.Buster, 1, 50_000, 0, Array.Empty<MitigationUp>(), MitigationVerdict.Unmitigated);
        Assert.Null(RaidPlanner.UI.Model.MeterGuard.Line(bare).After);
        var sch = e.Rows.Single(r => r.Slot == "H2");
        var heal = RaidPlanner.UI.Model.MeterGuard.HealerCards(sch, preview.Abilities(e.Id, sch.PartyId, MeterTab.Healing), preview.Protection(e.Id, sch.PartyId));
        Assert.Equal(new[] { "Healing that landed", "Overheal", "Shields absorbed", "Heals cast" }, heal.Select(c => c.Label));
        Assert.Contains("Medica III", heal[1].Sub);
        Assert.StartsWith("Estimate", heal[2].Sub);
        var cells = RaidPlanner.UI.Model.MeterGuard.HealerCells(new AbilityRow(1, "Cure", false, false, 6000, 0.5, 4, 0.2, null, 3000, false, 2000, 3));
        Assert.Equal(new[] { "6.00k", "50%", "25%", "3", "3,000" }, cells);
    }

    [Fact]
    public void Own_mitigation_is_timed_while_they_hold_the_boss()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        p.Action(CombatParserTests.Batch(9, Dps, 100, 0, (Boss, new[] { Dmg(5000) }, -1, -1, true)));
        s.Aggro(9, Boss, Tank);
        s.Wearing(Tank);
        s.Wears(Tank, Rampart, Tank);
        // Two seconds with Rampart up, two without (a healer's barrier isn't theirs), then the boss on the healer.
        for (var t = 9.0; t <= 11.0 + 1e-9; t += 0.25) s.Frame(t, Party(), true);
        s.Wearing(Tank);
        s.Wears(Tank, Galvanize, Healer);
        for (var t = 11.25; t <= 13.0 + 1e-9; t += 0.25) s.Frame(t, Party(), true);
        s.Aggro(13, Boss, Healer);
        for (var t = 13.25; t <= 15.0 + 1e-9; t += 0.25) s.Frame(t, Party(), true);
        var e = s.Current!;
        var tank = s.Protection(e.Id, e.Rows.Single(r => r.Slot == "MT").PartyId)!;
        Assert.Equal(4, tank.HeldSeconds, 6);
        Assert.Equal(2, tank.CoveredSeconds, 6);
        Assert.Equal(0.5, tank.Uptime!.Value, 6);
        var dps = s.Protection(e.Id, e.Rows.Single(r => r.Slot == "R1").PartyId)!;
        Assert.Null(dps.Uptime);
    }

    /// <summary>
    /// 0.9.0's review: the barriers go by the Status sheet's names (xivapi's English Status.csv): The Blackest Night's status
    /// is "Blackest Night" (1178), which until then wasn't a barrier anywhere (the action's name, no status's, was listed), so
    /// a DRK's barrier was never credited, never "up", and wasn't in the death recap; and Guardian's Will (3830), Brutal
    /// Shell (1898) and The Spire (3892), each "a … barrier is nullifying damage", weren't listed at all.
    /// </summary>
    [Fact]
    public void The_barriers_go_by_the_Status_sheets_names()
    {
        foreach (var name in new[] { "Blackest Night", "Guardian's Will", "Brutal Shell", "The Spire" })
        {
            Assert.Contains(name, CombatLists.ShieldNames);
            Assert.Equal(MitigationKind.Shield, CombatLists.MitigationOf(name));
        }
        var tbn = Mitigations.Names().Single(n => n.Name == "Blackest Night").Index;
        Assert.Equal("The Blackest Night", Mitigations.All[tbn].Key);
        Assert.Equal(MitigationRole.Shield, Mitigations.All[tbn].Kind);
        // Guardian's own mitigation stays mitigation; its barrier is the barrier.
        Assert.Equal(MitigationKind.Mitigation, CombatLists.MitigationOf("Guardian"));
    }
}
