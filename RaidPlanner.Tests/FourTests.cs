using System.Text.Json.Nodes;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>0.4.0's effects: a status resisted isn't a full resist, knockbacks, absorbed, the bonus %, interrupts, provokes.</summary>
public class FourEffectTests
{
    private static RawEffect E(byte type, byte p0 = 0, byte p1 = 0, byte p2 = 0, byte p3 = 0, byte p4 = 0, ushort value = 0) => new(type, p0, p1, p2, p3, p4, value);

    [Fact]
    public void A_status_resisted_is_its_own_kind_and_the_hit_still_landed()
    {
        var d = Effects.Decode(E(55, value: 2));
        Assert.Equal(EffectKind.StatusResisted, d.Kind);
        Assert.Equal(2u, d.Status);
        Assert.Equal(EffectKind.Resisted, Effects.Decode(E(2)).Kind);
        // A hit that did damage and had its stun resisted: landed (16 not set), 1024 set.
        var (damage, fx) = Effects.Taken(new[] { E(3, value: 5000), E(55, value: 2) });
        Assert.Equal(5000, damage);
        Assert.Equal(Effects.FxStatusResisted, fx);
        Assert.Equal(0, fx & Effects.FxResisted);
        Assert.Equal(Effects.FxResisted, Effects.Taken(new[] { E(2) }).Fx);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(36)]
    public void Knockbacks_and_draw_ins_are_flagged(byte type)
    {
        Assert.Equal(EffectKind.Knockback, Effects.Decode(E(type, value: 7)).Kind);
        Assert.Equal(Effects.FxKnockback, Effects.Taken(new[] { E(3, value: 100), E(type, value: 7) }).Fx);
    }

    [Fact]
    public void A_damage_effects_bonus_and_absorbed()
    {
        var d = Effects.Decode(E(3, p2: 61, value: 9129));
        Assert.Equal((sbyte)61, d.Bonus);
        Assert.False(d.Absorbed);
        // Signed: a parried hit's reduction.
        Assert.Equal((sbyte)-20, Effects.Decode(E(6, p2: unchecked((byte)-20), value: 100)).Bonus);
        var absorbed = Effects.Decode(E(3, p4: 0x04, value: 100));
        Assert.True(absorbed.Absorbed);
        Assert.Equal(Effects.FxAbsorbed, Effects.Taken(new[] { E(3, p4: 0x04, value: 100) }).Fx);
        // An immune hit isn't absorbed.
        Assert.Equal(EffectKind.Invulnerable, Effects.Decode(E(3, p4: 0x14)).Kind);
        Assert.Equal(EffectKind.Interrupt, Effects.Decode(E(76)).Kind);
        Assert.Equal(EffectKind.Threat, Effects.Decode(E(24, value: 1)).Kind);
    }
}

/// <summary>0.4.0's parser: every party action and item used, with what it did.</summary>
public class FourParserTests
{
    private static RawEffect E(byte type, byte p0 = 0, byte p1 = 0, byte p2 = 0, byte p3 = 0, byte p4 = 0, ushort value = 0) => new(type, p0, p1, p2, p3, p4, value);

    private sealed class Uses : ICombatSink
    {
        public readonly List<PartyUse> Seen = new();
        public readonly List<PartyEffect> Effects = new();

        public void Used(double t, uint member, uint caster, uint action)
        {
        }

        public void Effect(in PartyEffect e) => Effects.Add(e);

        public void Taken(in TakenHit e)
        {
        }

        public void Tick(in TickEvent e, ReadOnlySpan<TickShare> shares)
        {
        }

        public void Use(in PartyUse u) => Seen.Add(u);
    }

    private static (CombatParser P, Uses U) Parser()
    {
        // 7: an auto-attack; 9: a limit break; 3569: a ground action.
        var p = new CombatParser(id => id == 7 ? 1 : id == 9 ? 9 : 3, isArea: id => id == 3569);
        p.SetParty(new uint[] { 1, 2 });
        var u = new Uses();
        p.Add(u);
        return (p, u);
    }

    private static EffectBatch Batch(double t, uint caster, uint action, byte type = 1, uint owner = 0, uint seq = 0, params (uint Target, RawEffect[] Effects)[] targets)
    {
        var b = new EffectBatch();
        b.Begin(t, caster, owner, action, type);
        b.Sequence = seq;
        foreach (var (target, effects) in targets)
        {
            var k = b.Add(target);
            effects.CopyTo(b.EffectsOf(k));
            b.Hostile[k] = target > 100;
        }
        return b;
    }

    [Fact]
    public void Every_party_action_is_a_use_with_its_flags_autos_left_out()
    {
        var (p, u) = Parser();
        p.Action(Batch(1, 1, 7538, targets: (900, new[] { E(3, value: 10), E(76) })));
        p.Action(Batch(2, 1, 7540, targets: (900, new[] { E(55, value: 2) })));
        p.Action(Batch(3, 2, 7533, targets: (900, new[] { E(24, value: 1) })));
        p.Action(Batch(4, 2, 100, targets: new[] { (900u, new[] { E(1) }), (901u, new[] { E(7) }) }));
        p.Action(Batch(5, 2, 101));
        p.Action(Batch(6, 1, 7, targets: (900, new[] { E(3, value: 10) })));
        p.Action(Batch(7, 1, 9, targets: (2, new[] { E(50, value: 1) })));
        p.Action(Batch(8, 50, 25774, owner: 1, targets: (900, new[] { E(3, value: 10) })));
        Assert.Equal(new[] { 7538u, 7540u, 7533u, 100u, 101u, 9u, 25774u }, u.Seen.Select(x => x.Action));
        Assert.Equal(UseFlags.Interrupted, u.Seen[0].Flags);
        Assert.Equal(UseFlags.StatusResisted, u.Seen[1].Flags);
        Assert.Equal(UseFlags.Threat, u.Seen[2].Flags);
        Assert.Equal(UseFlags.Missed, u.Seen[3].Flags);
        Assert.Equal(2, u.Seen[3].Targets);
        Assert.Equal(900u, u.Seen[3].Target);
        Assert.Equal(UseFlags.NoTarget, u.Seen[4].Flags);
        Assert.Equal(UseFlags.LimitBreak | UseFlags.Revive, u.Seen[5].Flags);
        Assert.Equal(UseFlags.Pet, u.Seen[6].Flags);
        Assert.Equal(1u, u.Seen[6].Member);
        Assert.Equal(50u, u.Seen[6].Caster);
        // The use comes after the action's effects (an encounter its first hit starts has begun).
        Assert.NotEmpty(u.Effects);
    }

    [Fact]
    public void An_item_is_only_a_use_and_a_message_twice_is_one_use()
    {
        var (p, u) = Parser();
        p.Action(Batch(1, 1, 1_039_727, type: 2, targets: (1, new[] { E(14, value: 49) })));
        Assert.Single(u.Seen);
        Assert.Equal(UseFlags.Item, u.Seen[0].Flags);
        Assert.Equal(2, u.Seen[0].ActionType);
        Assert.Empty(u.Effects);
        // An enemy's item isn't read.
        p.Action(Batch(1, 900, 5, type: 2, targets: (1, new[] { E(3, value: 10) })));
        Assert.Single(u.Seen);
        p.Action(Batch(2, 1, 9001, seq: 77, targets: (900, new[] { E(3, value: 10) })));
        p.Action(Batch(2.1, 1, 9001, seq: 77, targets: (901, new[] { E(3, value: 10) })));
        Assert.Equal(2, u.Seen.Count);
        // Its effects are still both.
        Assert.Equal(2, u.Effects.Count);
    }

    [Fact]
    public void A_ground_action_says_where_and_a_hits_bonus_goes_on()
    {
        var (p, u) = Parser();
        var b = Batch(1, 1, 3569);
        b.HasGround = true;
        b.GroundX = 100.5f;
        b.GroundZ = 99.25f;
        p.Action(b);
        Assert.True(u.Seen[0].Ground);
        Assert.Equal(100.5f, u.Seen[0].X);
        var other = Batch(2, 1, 9001, targets: (900, new[] { E(3, p2: 61, value: 10) }));
        other.HasGround = true;
        p.Action(other);
        Assert.False(u.Seen[1].Ground);
        Assert.Equal((sbyte)61, u.Effects.Single().Bonus);
    }
}

/// <summary>0.4.0's game-free helpers: who interrupted, the moments before a pull, the countdown.</summary>
public class FourWatchTests
{
    [Fact]
    public void The_interrupt_effect_is_credited_before_a_named_interrupt_and_only_in_the_second_before()
    {
        var w = new CastWatch();
        w.Used(9.0, 900, 1, 7538, false, true);
        w.Used(9.5, 900, 2, 7551, false, true);
        Assert.Equal((2u, 7551u), w.Credit(10, 900));
        w.Used(9.2, 900, 3, 7540, true, false);
        Assert.Equal((3u, 7540u), w.Credit(10, 900));
        Assert.Null(w.Credit(10, 901));
        Assert.Null(w.Credit(11, 900));
        // Neither flagged nor an interrupt: not kept.
        w.Used(11.9, 900, 4, 9001, false, false);
        Assert.Null(w.Credit(12, 900));
    }

    [Fact]
    public void Before_a_pull_the_last_30_seconds_are_kept_with_times_from_its_start()
    {
        var pre = new PrePull();
        pre.Use(new PartyUse(60, 1, 1, 9001, 1, 0, 0, UseFlags.None, false, 0, 0));
        pre.Use(new PartyUse(98, 1, 1, 1_039_727, 2, 1, 1, UseFlags.Item, false, 0, 0));
        pre.Use(new PartyUse(98.5, 2, 2, 9005, 1, 0, 0, UseFlags.None, false, 0, 0));
        var rows = pre.Before(100);
        Assert.Equal(new[] { -2.0, -1.5 }, rows.Select(r => r.T));
        pre.Clear();
        Assert.Empty(pre.Before(100));
    }

    [Fact]
    public void The_countdown_says_how_early_or_late_the_pull_was()
    {
        var cd = new CountdownWatch();
        cd.Frame(10, false, 0);
        for (var t = 10.0; t < 24.95; t += 0.1) cd.Frame(t, true, (float)(15 - (t - 10)));
        cd.Frame(25.1, false, 0);
        var at = cd.At(25.42);
        Assert.NotNull(at);
        Assert.Equal(15, at!.Value.Length);
        Assert.Equal(0.42, at.Value.End, 2);
        // Too long after: not this pull's.
        Assert.Null(cd.At(60));
        // Cancelled well before zero: none.
        var cancelled = new CountdownWatch();
        cancelled.Frame(0, true, 10);
        cancelled.Frame(3, false, 7);
        Assert.Null(cancelled.At(10));
        // Combat before zero (pulled early): negative.
        var early = new CountdownWatch();
        early.Frame(0, true, 5);
        early.Frame(4, true, 1);
        Assert.Equal(-1.0, early.At(4)!.Value.End, 2);
    }
}

/// <summary>0.4.0's recording: the new tables, events and columns, written, read back, and old files as they were.</summary>
public class FourFormatTests
{
    [Fact]
    public void The_sample_carries_every_new_table_event_and_column()
    {
        var r = RecordingTests.Sample();
        var json = r.ToJson();
        foreach (var key in new[]
                 {
                     "\"use\":{\"t\":[", "\"area\":{\"t\":[", "\"hate\":{\"t\":[", "\"res\":{\"t\":[", "\"lb\":{\"t\":[", "\"pre\":{\"from\":30,", "\"cd\":{\"len\":15,\"end\":0.42}",
                     "\"items\":{\"1039727\":{\"name\":\"Grade 2 Gemdraught of Intelligence\",\"icon\":22457,\"hq\":true}}", "\"k\":\"cancel\"", "\"k\":\"raise\"",
                     "\"k\":\"aggro\"", "\"k\":\"signs\"", "\"int\":true", "\"sh\":[12,12,", "\"px\":[101,", "\"pz\":[105,", "\"why\":[", "\"cdg\":58", "\"charges\":2",
                     "\"rc\":60", "\"area\":true", "\"self\":true", "\"dispel\":true", "\"cat\":2",
                 })
            Assert.Contains(key, json);
        var back = Recording.Parse(json);
        Assert.Equal(json, back.ToJson());
        var c = back.Combat!;
        Assert.True(c.Extended);
        Assert.Equal(r.Combat!.Uses.Count, c.Uses.Count);
        Assert.Equal(new[] { -2.1, -1.5 }, c.Pre.Select(p => p.T));
        Assert.Equal((15.0, 0.42), c.Countdown);
        Assert.True(c.Items[1_039_727].Hq);
        Assert.Equal(25, c.Fx.First(f => Math.Abs(f.T - 14.5) < 0.001).Bonus);
        Assert.Equal(1, c.Casts.Single(x => Math.Abs(x.T - 30) < 0.001).Why);
        Assert.True(c.Casts.Single(x => Math.Abs(x.T - 30) < 0.001).Cancelled);
        Assert.Equal(new AreaRow(25, 3, 3569, 100.5f, 101.25f), c.Areas.Single());
        Assert.Equal(new[] { 10_000, 20_000, 0, 4_400 }, c.Gauge.Select(g => g.Value));
        // The interrupt, credited to the OT's Interject.
        var cancel = back.Events.OfType<CancelEvent>().Single();
        Assert.Equal((101, 800u, true, 2, 7538u), (cancel.Source, cancel.Action, cancel.Interrupted, cancel.By, cancel.With));
        Assert.True(back.Events.OfType<CastEvent>().Single(x => x.Action == 800).Interruptible);
        // H2 raised by H1 (the Raise status from them at 56.5), up at 57.5.
        var raise = back.Events.OfType<RaiseEvent>().Single();
        Assert.Equal((57.5, 4, 3, (double?)56.5, false), (raise.T, raise.Who, raise.By, raise.At, raise.Lb));
        // The boss's target on change only.
        Assert.Equal(new[] { 1, 2, 7, 2, 1 }, back.Events.OfType<AggroEvent>().Select(a => a.On));
        Assert.Equal(new[] { 1, 2 }, back.Events.OfType<SignsEvent>().Select(s => s.Marks.Count));
        var hit = back.Events.OfType<HitEvent>().Single(h => h.Action == 600);
        Assert.Equal(new[] { 20 }, hit.Shield);
        Assert.Equal(new[] { 101.25f }, hit.Px);
        Assert.Equal(Effects.FxAbsorbed, hit.Fx![0]);
        Assert.True(back.Party.Single(p => p.Slot == "R2").Self);
        Assert.Equal(60f, back.Actions[16151].AdjustedRecast);
        Assert.Equal(2, back.Actions[16151].Charges);
        // Not asked for others' actions, nor GCDs.
        Assert.Equal(0f, back.Actions[7538].AdjustedRecast);
        Assert.Equal(0f, back.Actions[9005].AdjustedRecast);
        // As the Status sheet has them: Dia can be cleansed, Weakness can't.
        Assert.True(back.Statuses[1871].Dispel);
        Assert.False(back.Statuses[43].Dispel);
    }

    [Fact]
    public void An_older_combat_block_is_written_back_without_the_new_tables()
    {
        var old = """
            {"v":1,"fx":{"t":[10],"a":[1],"o":[1],"r":[101],"v":[10],"k":[1],"h":[-1]},
             "cast":{"t":[5],"src":[1],"a":[2],"dur":[2.5],"x":[1]},
             "st":{"t":[],"on":[],"src":[],"s":[],"a":[],"dur":[],"n":[],"gain":[]}}
            """;
        var c = CombatLog.Read(JsonNode.Parse(old)!.AsObject());
        Assert.False(c.Extended);
        Assert.False(c.HasUses);
        Assert.Equal(0, c.Casts.Single().Why);
        Assert.Equal(0, c.Fx.Single().Bonus);
        var json = new Recording { Combat = c }.ToJson();
        foreach (var key in new[] { "\"use\"", "\"pre\"", "\"items\"", "\"b\":", "\"why\"" }) Assert.DoesNotContain(key, json);
    }

    [Fact]
    public void Optional_columns_not_as_long_read_as_their_default_and_bad_tables_are_left_out()
    {
        var json = """
            {"v":1,"fx":{"t":[10,5],"a":[1,1],"o":[1,1],"r":[101,101],"v":[10,10],"k":[1,1],"h":[-1,-1],"b":[30]},
             "cast":{"t":[5],"src":[1],"a":[2],"dur":[2.5],"x":[1],"why":[2]},
             "use":{"t":[1,2],"src":[1],"a":[1,2],"tgt":[0,0],"n":[1,1],"f":[0,0]},
             "hate":{"t":[100],"on":[101],"who":[1],"pct":[250]},
             "pre":{"from":30,"t":[2800],"src":[1],"a":[9],"tgt":[0],"f":[2]},
             "thinned":["res","junk"]}
            """;
        var c = CombatLog.Read(JsonNode.Parse(json)!.AsObject());
        Assert.All(c.Fx, f => Assert.Equal(0, f.Bonus));
        Assert.Equal(2, c.Casts.Single().Why);
        Assert.Empty(c.Uses);
        Assert.Equal(100, c.Hate.Single().Pct);
        Assert.Equal(-2.0, c.Pre.Single().T);
        Assert.Equal(new[] { "res" }, c.Thinned);
        Assert.True(c.Extended);
    }
}

/// <summary>0.4.0's builder: cancels, raises, aggro, the enmity list, MP, the gauge, the signs.</summary>
public class FourBuilderTests
{
    private static RecordingBuilder New(CombatKinds? kinds = null) => new(new DutyInfo("X", 1, 1), 1, new DateTimeOffset(2026, 11, 12, 20, 0, 0, TimeSpan.Zero), true,
        "0.4.0", id => new ActionInfo($"A{id}", 1, 0, 0, "", 0), id => new StatusInfo($"S{id}", 1), kinds);

    private static readonly CombatKinds Kinds = new()
    {
        Interrupt = id => id == 7538,
        RaiseAction = id => id == 125,
        RaiseStatus = id => id == 148,
    };

    [Fact]
    public void A_cast_stopped_before_its_end_is_a_cancel_and_one_at_its_end_isnt()
    {
        var b = New(Kinds);
        b.SetParty(new[] { new Partier(11, "WAR", 1), new Partier(12, "BLM", 3) });
        var boss = b.Actor(900, "Boss", 5, 1);
        b.Cast(10, boss, 800, 4, 0, 0, 0, 0, null, null, true);
        // Too late: the game's word on a cast that went off.
        b.Cancel(13.8, boss, 800, false);
        Assert.Empty(b.Finish(20, Outcome.Ended).Events.OfType<CancelEvent>());
        b.Cast(20, boss, 801, 4, 0, 0, 0, 0, null, null, true);
        b.PartyUse(21.5, 1, 7538, boss, 1, 0);
        b.Cancel(22, boss, 801, true);
        // Another action than the one held: nothing.
        b.Cast(30, boss, 802, 4, 0, 0, 0, 0, null, null);
        b.Cancel(31, boss, 899, true);
        // A party member's cast: its why.
        b.PartyCast(40, 2, 9005, 2.8f);
        b.Cancel(41, 2, 9005, true);
        var r = b.Finish(60, Outcome.Ended);
        var c = r.Events.OfType<CancelEvent>().Single();
        Assert.Equal((801u, true, 1, 7538u), (c.Action, c.Interrupted, c.By, c.With));
        Assert.Equal((true, 2), (r.Combat!.Casts.Single().Cancelled, r.Combat.Casts.Single().Why));
    }

    [Fact]
    public void A_raise_is_credited_by_its_status_else_the_limit_break_else_the_raise_used()
    {
        var b = New(Kinds);
        b.SetParty(new[] { new Partier(11, "WHM", 4), new Partier(12, "SCH", 4), new Partier(13, "BLM", 3), new Partier(14, "PLD", 1) });
        // Their party ids by spot: PLD MT 1? The roles assign MT, H1, H2, R1.
        var ids = b.Finish(0, Outcome.Ended).Party.ToDictionary(p => p.Job, p => p.Id);
        b = New(Kinds);
        b.SetParty(new[] { new Partier(11, "WHM", 4), new Partier(12, "SCH", 4), new Partier(13, "BLM", 3), new Partier(14, "PLD", 1) });
        int whm = ids["WHM"], sch = ids["SCH"], blm = ids["BLM"], pld = ids["PLD"];
        foreach (var who in ids.Values) b.Alive(0, who, false);
        // BLM: the Raise status from the SCH (rule 1), though the WHM's raise came after.
        b.Alive(10, blm, true);
        b.StatusGained(12, blm, 148, 12, StatusFrom.Party, sch, 0, 60);
        b.PartyUse(13, whm, 125, blm, 1, 0);
        b.Alive(15, blm, false);
        // PLD: the WHM's raise only (rule 3).
        b.Alive(20, pld, true);
        b.PartyUse(22, whm, 125, pld, 1, 0);
        b.Alive(25, pld, false);
        // Both down; the SCH's limit break 3 (rule 2).
        b.Alive(30, pld, true);
        b.Alive(30, blm, true);
        b.PartyUse(32, sch, 9, sch, 4, (int)(UseFlags.LimitBreak | UseFlags.Revive));
        b.Alive(33, pld, false);
        b.Alive(33, blm, false);
        // Up by itself (no raise seen): no one.
        b.Alive(40, pld, true);
        b.Alive(45, pld, false);
        // A raise used before they died isn't theirs.
        b.PartyUse(49, whm, 125, blm, 1, 0);
        b.Alive(50, blm, true);
        b.Alive(55, blm, false);
        var raises = b.Finish(60, Outcome.Ended).Events.OfType<RaiseEvent>().Select(x => (x.T, x.Who, x.By, x.At, x.Lb)).ToList();
        Assert.Equal(new (double, int, int, double?, bool)[]
        {
            (15, blm, sch, 12, false), (25, pld, whm, 22, false), (33, pld, sch, 32, true), (33, blm, sch, 32, true), (45, pld, 0, null, false),
            (55, blm, 0, null, false),
        }, raises);
    }

    [Fact]
    public void Aggro_hate_mp_the_gauge_and_signs_are_kept_on_change()
    {
        var b = New();
        b.SetParty(new[] { new Partier(11, "PLD", 1), new Partier(12, "WAR", 1), new Partier(13, "BLM", 3) });
        var boss = b.Actor(900, "Boss", 5, 1);
        b.Aggro(0, boss, 1);
        b.Aggro(0.1, boss, 1);
        b.Aggro(5, boss, 2);
        b.Hate(1, boss, new[] { (1, 100), (2, 50) });
        b.Hate(2, boss, new[] { (1, 100), (2, 53) });
        b.Hate(3, boss, new[] { (1, 100), (2, 56) });
        b.Hate(4, boss, new[] { (2, 100), (1, 98) });
        b.Hate(5, boss, new[] { (2, 100) });
        b.Resources(0, 3, 10_000, 0);
        // Within half a second of the last: not kept; then back within 500 of it: nothing to keep.
        b.Resources(0.2, 3, 5_000, 0);
        b.Resources(1, 3, 9_800, 0);
        b.Resources(2, 3, 9_800, 10);
        b.Resources(2.4, 3, 9_800, 0);
        b.Resources(3, 3, 9_800, 0);
        b.LimitGauge(0, 0, 0);
        b.LimitGauge(1, 0, 30_000);
        b.LimitGauge(2, 0, 30_000);
        b.LimitGauge(3, 220, 30_000);
        b.Signs(0, new Dictionary<int, int>());
        b.Signs(1, new Dictionary<int, int> { [0] = boss });
        b.Signs(2, new Dictionary<int, int> { [0] = boss });
        b.Signs(3, new Dictionary<int, int>());
        var r = b.Finish(10, Outcome.Ended);
        Assert.Equal(new[] { (0.0, 1), (5.0, 2) }, r.Events.OfType<AggroEvent>().Select(a => (a.T, a.On)));
        Assert.Equal(new[] { (1.0, 1, 100), (1.0, 2, 50), (3.0, 2, 56), (4.0, 2, 100), (4.0, 1, 98), (5.0, 1, -1) },
            r.Combat!.Hate.Select(h => (h.T, h.Who, h.Pct)));
        Assert.Equal(new[] { (0.0, 10_000, 0), (2.0, 9_800, 10), (3.0, 9_800, 0) }, r.Combat.Res.Select(x => (x.T, x.Mp, x.Shield)));
        Assert.Equal(new[] { (1.0, 0), (3.0, 220) }, r.Combat.Gauge.Select(g => (g.T, g.Value)));
        Assert.Equal(new[] { 1, 0 }, r.Events.OfType<SignsEvent>().Select(s => s.Marks.Count));
    }

    [Fact]
    public void Four_frames_a_second_when_asked()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0.4.0", _ => null)
        {
            FrameStep = RecordingBuilder.SmoothFrameEvery,
            FrameCap = RecordingBuilder.SmoothMaxFrames,
        };
        var boss = b.Actor(900, "Boss", 5, 1);
        b.SeenTargetable(boss, 1);
        for (var t = 0.0; t < 10; t += 1.0 / 60) b.Positions(t, new Dictionary<int, (float, float, float)> { [boss] = (0, 0, 0) });
        Assert.InRange(b.Finish(10, Outcome.Ended).Frames.Count, 39, 41);
    }
}

/// <summary>0.4.0's send: what's dropped first when a pull is too big.</summary>
public class FourFitTests
{
    [Fact]
    public void Four_frames_a_second_go_to_two_first_then_the_enmity_and_mp_before_any_more_frames()
    {
        var r = RecordingTests.Sample();
        var frames = new List<Frame>();
        var c = r.Combat!;
        // A long pull at four frames a second, with a big enmity list.
        var party = Enumerable.Range(1, 8).ToDictionary(i => i, i => (100f + i, 105f, 3.14f));
        for (var i = 0; i < 9600; i++) frames.Add(new Frame(i * 0.25, new Dictionary<int, (float, float, float)>(party) { [101] = (100, 100, 0) }));
        var big = r.With(frames, true, c.Without());
        for (var i = 0; i < 150_000; i++) big.Combat!.Hate.Add(new HateRow(i * 0.016, 101, 1 + i % 8, i % 100));
        for (var i = 0; i < 150_000; i++) big.Combat!.Res.Add(new ResRow(i * 0.016, 1 + i % 8, 10_000 - i % 5000, i % 50));
        var json = big.ToJson();
        Assert.True(SiteLink.ServerSize(json) > SiteLink.MaxBytes);
        var fit = SiteLink.Fit(json)!.Value;
        var back = Recording.Parse(fit.Json);
        Assert.True(fit.Thinned);
        // Two frames a second (by time), and the enmity list, MP and barriers gone, said so.
        Assert.InRange(back.Frames.Count, 4790, 4810);
        Assert.Equal(0.5, back.Frames[1].T - back.Frames[0].T, 3);
        Assert.Contains("hate", back.Combat!.Thinned);
        Assert.Empty(back.Combat.Hate);
        Assert.NotEmpty(back.Combat.Uses);
        Assert.NotEmpty(back.Combat.Pre);
        Assert.NotEmpty(back.Combat.Gauge);
    }

    [Fact]
    public void Frames_are_thinned_by_time_not_by_count()
    {
        var frames = new[] { 0.0, 0.5, 1.0, 1.5, 2.0, 2.4, 2.6, 3.1 }.Select(t => new Frame(t, new Dictionary<int, (float, float, float)>())).ToList();
        Assert.Equal(new[] { 0.0, 1.0, 2.0, 3.1 }, SiteLink.ByTime(frames, 1).Select(f => f.T));
        Assert.Equal(new[] { 0.0, 2.0 }, SiteLink.ByTime(frames, 2).Select(f => f.T));
    }
}

/// <summary>0.4.0's summary: each spot's counts, the damage by phase, the extras, and what's trimmed first.</summary>
public class FourSummaryTests
{
    [Fact]
    public void The_samples_counts_phases_and_extras()
    {
        var r = RecordingTests.SampleBuilder(true, out var phase);
        var s = PullSummary.Build(r, phase, isDot: id => id == 1871);
        var stats = s["stats"]!.AsObject();
        var slots = stats["slots"]!.AsArray().Select(x => x!.AsObject()).ToList();
        JsonObject Slot(string spot) => slots.Single(x => x["slot"]!.GetValue<string>() == spot);
        // The BLM recorded it.
        Assert.True(Slot("R2")["self"]!.GetValue<bool>());
        Assert.False(Slot("MT").ContainsKey("self"));
        // GCDs: the MT's 20 Fast Blades; H1's Cure II, Dia and Raise; the BLM's Fire IV (Meteor, a limit break, isn't one).
        Assert.Equal(20, Slot("MT")["gcds"]!.GetValue<int>());
        Assert.Equal(3, Slot("H1")["gcds"]!.GetValue<int>());
        Assert.Equal(1, Slot("R2")["gcds"]!.GetValue<int>());
        // The BLM's potions: in the pull and before it.
        Assert.Equal(2, Slot("R2")["pots"]!.GetValue<int>());
        Assert.Equal(1, Slot("H1")["raises"]!.GetValue<int>());
        Assert.Equal(1, Slot("H2")["raised"]!.GetValue<int>());
        // H2 dead from 45.5 to 57.5; R2's death is the wipe's.
        Assert.Equal(12.0, Slot("H2")["dead"]!.GetValue<double>());
        Assert.False(Slot("R2").ContainsKey("dead"));
        Assert.Equal(1, Slot("OT")["ints"]!.GetValue<int>());
        Assert.False(Slot("M1").ContainsKey("gcds"));
        // The damage by phase: each spot's adds up to its damage; the limit break out.
        var ph = stats["ph"]!.AsArray().Select(x => x!.AsArray().Select(v => v!.GetValue<long>()).ToArray()).ToList();
        Assert.Equal(new long[] { 0, 50 }, ph.Select(x => x[0]));
        for (var i = 0; i < slots.Count; i++) Assert.Equal(slots[i]["dmg"]!.GetValue<long>(), ph.Sum(x => x[i + 1]));
        var x = stats["x"]!.AsObject();
        Assert.Equal(new[] { 1, 1 }, x["int"]!.AsArray().Select(v => v!.GetValue<int>()));
        // MT → OT (their Provoke), the BRD's moment not a tank's, OT → MT after the phase.
        Assert.Equal(2, x["swaps"]!.GetValue<int>());
        Assert.Equal(new long[] { 385, 9007 }, x["lbUses"]!.AsArray().Single()!.AsArray().Select(v => v!.GetValue<long>()));
        Assert.Equal(0.42, x["cd"]!.GetValue<double>());
    }

    [Fact]
    public void Past_6_KB_the_hits_go_then_the_debuffs_then_ph_then_x()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0",
            id => new ActionInfo($"A rather long mechanic name number {id}", 2, 10, 0, "", 0), id => new StatusInfo("Damage Down", 1));
        b.SetParty(Enumerable.Range(1, 8).Select(i => new Partier((ulong)i, "PLD", (byte)(i % 4 + 1))).ToArray());
        var boss = b.Actor(900, "Boss", 5, 1);
        b.SeenTargetable(boss, 1000);
        // Fifty phases, each with damage for all eight: ph alone is near 3 KB.
        for (var p = 0; p < 50; p++)
        {
            b.Targetable(p * 20, boss, true);
            b.EndTargets(p * 20);
            for (var i = 1; i <= 8; i++) b.CombatFx(p * 20 + 1, 9001, i, boss, 1_234_567, 1, -1);
            b.Targetable(p * 20 + 10, boss, false);
            b.EndTargets(p * 20 + 10);
        }
        for (var i = 0; i < 400; i++) b.Hit(i * 0.5, boss, (uint)(1000 + i % 300), 0, 0, 0, 0, 0, new[] { 1 + i % 8, 1 + (i + 3) % 8 }, new long[] { 1, 1 }, new[] { 0, 0 }, new long[] { 1, 1 });
        var r = b.Finish(1000, Outcome.Wipe);
        var stats = PullStats.Stats(r, phaseStarts: b.PhaseStarts);
        Assert.True(PullStats.Size(stats) <= PullStats.MaxBytes, $"{PullStats.Size(stats)} bytes");
        Assert.Equal(50, stats["ph"]!.AsArray().Count);
        // The hits made room for ph.
        Assert.True(stats["hits"]!.AsArray().Count < 400);
    }
}

/// <summary>0.4.0's meter: what the party used, raises, interrupts, tank swaps, events and the gauge.</summary>
public class FourMeterTests
{
    private const uint Mt = 1, Ot = 2, Whm = 3, Blm = 4, Boss = 900;

    private static readonly CombatKinds Kinds = new()
    {
        Interrupt = id => id == 7538,
        RaiseAction = id => id == 125,
        Control = id => id == 2,
        Gcd = id => id is 9001 or 125,
    };

    private static RawEffect E(byte type, ushort value = 0, byte p4 = 0) => new(type, 0, 0, 0, 0, p4, value);

    private static PartyState[] Party(double t, bool whmDead = false, byte weak = 0) => new[]
    {
        new PartyState(Mt, "Tank A", "PLD", 1, 100_000, 100_000, true, false),
        new PartyState(Ot, "Tank B", "WAR", 1, 100_000, 100_000, true, false),
        new PartyState(Whm, "Healer", "WHM", 4, whmDead ? 0 : 80_000, 80_000, true, false, weak),
        new PartyState(Blm, "Caster", "BLM", 3, 70_000, 70_000, true, true),
    };

    private static EffectBatch Batch(double t, uint caster, uint action, byte type, uint target, params RawEffect[] effects)
    {
        var b = new EffectBatch();
        b.Begin(t, caster, 0, action, type);
        if (target != 0)
        {
            var k = b.Add(target);
            effects.CopyTo(b.EffectsOf(k));
            b.Hostile[k] = target == Boss;
        }
        return b;
    }

    [Fact]
    public void A_fights_events_and_counts()
    {
        var p = new CombatParser(id => id == 205 ? 9 : 3);
        p.SetParty(new[] { Mt, Ot, Whm, Blm });
        var s = new MeterSession(id => $"A{id}", id => $"S{id}", id => $"I{id}", Kinds);
        p.Add(s);
        s.Place(1, 1, "Arena");
        s.Frame(0, Party(0), true);
        // An item a moment before the first hit is the encounter's; the first hit starts it.
        p.Action(Batch(0.5, Blm, 1_039_727, 2, Blm));
        p.Action(Batch(1, Mt, 9001, 1, Boss, E(3, 10_000)));
        s.Frame(1, Party(1), true);
        s.Aggro(1, Boss, Mt);
        // The boss's interruptible cast, the OT's Interject (its effect), the game says interrupted.
        s.EnemyCast(2, Boss, 800, 4, true);
        p.Action(Batch(3, Ot, 7538, 1, Boss, E(76)));
        s.Cancelled(3.05, Boss, 800, true);
        // One that went off.
        s.EnemyCast(5, Boss, 801, 2, true);
        s.Frame(7.5, Party(7.5), true);
        // The OT's stun resisted; a stun that landed.
        p.Action(Batch(8, Ot, 7540, 1, Boss, E(55, 2)));
        p.Action(Batch(8.5, Ot, 7863, 1, Boss, E(14, 2)));
        // The tanks swap (held a second); a moment on the BLM (1.5 s), not a cast's target.
        s.Aggro(10, Boss, Ot);
        s.Frame(11.1, Party(11.1), true);
        s.Aggro(12, Boss, Blm);
        s.Frame(12.5, Party(12.5), true);
        s.Aggro(13.5, Boss, Ot);
        s.Frame(14, Party(14), true);
        // The WHM dies, the BLM raises... the MT (a raise only counts on the dead): the OT raises them.
        s.Frame(20, Party(20, whmDead: true), true);
        p.Action(Batch(21, Mt, 125, 1, Whm, E(14, 148)));
        s.Frame(25, Party(25, whmDead: false, weak: 1), true);
        s.Frame(30, Party(30, whmDead: true), true);
        // The limit break.
        p.Action(Batch(31, Blm, 205, 1, Boss, E(3, 50_000)));
        s.Gauge(12_000, 3, 10_000);
        s.Frame(35, Party(35, whmDead: true), true);
        var e = s.Current!;
        MeterRow Row(string slot) => e.Rows.Single(r => r.Slot == slot);
        Assert.Equal(1, Row("R1").Potions);
        // The MT's Fast Blade and their Raise (both GCDs).
        Assert.Equal(2, Row("MT").Casts);
        Assert.Equal(1, Row("OT").Interrupts);
        Assert.Equal(1, Row("MT").RaisesGiven);
        Assert.Equal(1, Row("H1").Raised);
        // Dead 20–25, then again from 30 (to now: 35).
        Assert.Equal(5 + 5, Row("H1").DeadSeconds, 6);
        Assert.Equal(2, e.Party.Casts);
        var deaths = e.Deaths;
        // Encounter time: it began at 1.
        Assert.Equal((24.0, "MT", false), (deaths[0].RaisedAt!.Value, deaths[0].RaisedBy!, deaths[0].HadWeakness));
        Assert.Null(deaths[1].RaisedAt);
        Assert.True(deaths[1].HadWeakness);
        var events = s.Events(e.Id);
        Assert.Equal(new[]
        {
            MeterEventKind.Potion, MeterEventKind.Interrupt, MeterEventKind.CastWentOff, MeterEventKind.Resisted, MeterEventKind.Stun,
            MeterEventKind.TankSwap, MeterEventKind.AggroLost, MeterEventKind.Death, MeterEventKind.Raise, MeterEventKind.Death, MeterEventKind.LimitBreak,
        }, events.Select(x => x.Kind));
        Assert.Equal("Tank B interrupted A800", events[1].Text);
        Assert.Equal("A801 went off", events[2].Text);
        Assert.Equal("Tank B's A7540 was resisted", events[3].Text);
        Assert.Equal("Tank B's S2 landed", events[4].Text);
        Assert.Equal(("OT", "MT"), (events[5].Slot, events[5].OtherSlot));
        Assert.Equal("Caster held a boss for 1.5s", events[6].Text);
        Assert.Equal("Healer was raised by Tank A", events[8].Text);
        Assert.Equal(new LimitBreakGauge(1, 3, 2_000, 10_000), s.LimitBreak);
        s.Gauge(0, 0, 10_000);
        Assert.Null(s.LimitBreak);
        Assert.Empty(s.Events(999));
    }
}
