using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using Xunit;

namespace RaidPlanner.Tests;

public class EffectDecodingTests
{
    private static RawEffect E(byte type, byte p0 = 0, byte p1 = 0, byte p2 = 0, byte p3 = 0, byte p4 = 0, ushort value = 0) => new(type, p0, p1, p2, p3, p4, value);

    [Theory]
    [InlineData(3, 0, 0, 0, 0, 0, 1234, EffectKind.Damage, 1234L, false, false)]
    [InlineData(3, 0x20, 0, 0, 0, 0, 1000, EffectKind.Damage, 1000L, true, false)]
    [InlineData(3, 0x40, 0, 0, 0, 0, 1000, EffectKind.Damage, 1000L, false, true)]
    [InlineData(3, 0x60, 0, 0, 0, 0, 1000, EffectKind.Damage, 1000L, true, true)]
    // The large-value bit: the third byte is Param3 (0x0F4240 = 1,000,000).
    [InlineData(3, 0, 0, 0, 0x0F, 0x40, 0x4240, EffectKind.Damage, 1_000_000L, false, false)]
    // Param3 without the bit isn't part of the amount.
    [InlineData(3, 0, 0, 0, 0x0F, 0, 100, EffectKind.Damage, 100L, false, false)]
    [InlineData(5, 0, 0, 0, 0, 0, 800, EffectKind.Damage, 800L, false, false)]
    [InlineData(6, 0, 0, 0, 0, 0, 700, EffectKind.Damage, 700L, false, false)]
    // A heal's crit is Param1's; Param0's 0x20 on a heal is nothing.
    [InlineData(4, 0, 0x20, 0, 0, 0, 5000, EffectKind.Heal, 5000L, true, false)]
    [InlineData(4, 0x20, 0, 0, 0, 0, 5000, EffectKind.Heal, 5000L, false, false)]
    [InlineData(1, 0, 0, 0, 0, 0, 0, EffectKind.Miss, 0L, false, false)]
    [InlineData(2, 0, 0, 0, 0, 0, 0, EffectKind.Resisted, 0L, false, false)]
    [InlineData(7, 0, 0, 0, 0, 0, 82, EffectKind.Invulnerable, 0L, false, false)]
    // Damage with the immune bit: nothing taken.
    [InlineData(3, 0, 0, 0, 0, 0x10, 0, EffectKind.Invulnerable, 0L, false, false)]
    [InlineData(10, 0, 0, 0, 0, 0, 500, EffectKind.Mp, 500L, false, false)]
    [InlineData(11, 0, 0, 0, 0, 0, 500, EffectKind.Mp, 500L, false, false)]
    [InlineData(50, 0, 0, 0, 0, 0, 1, EffectKind.ReviveLb, 0L, false, false)]
    [InlineData(51, 0, 0, 0, 0, 0, 0, EffectKind.InstantDeath, 0L, false, false)]
    [InlineData(24, 0, 0, 0, 0, 0, 1, EffectKind.Threat, 0L, false, false)]
    [InlineData(0, 0, 0, 0, 0, 0, 0, EffectKind.None, 0L, false, false)]
    public void Decodes(byte type, byte p0, byte p1, byte p2, byte p3, byte p4, ushort value, EffectKind kind, long amount, bool crit, bool dh)
    {
        var d = Effects.Decode(E(type, p0, p1, p2, p3, p4, value));
        Assert.Equal(kind, d.Kind);
        Assert.Equal(amount, d.Amount);
        Assert.Equal(crit, d.Crit);
        Assert.Equal(dh, d.DirectHit);
    }

    [Fact]
    public void Blocks_parries_statuses_and_directions()
    {
        Assert.True(Effects.Decode(E(5, value: 10)).Blocked);
        Assert.True(Effects.Decode(E(6, value: 10)).Parried);
        var on = Effects.Decode(E(14, p2: 3, value: 1871));
        Assert.Equal(EffectKind.StatusOn, on.Kind);
        Assert.Equal(1871u, on.Status);
        Assert.Equal(3, on.Param);
        Assert.False(on.ToSource);
        // Type 15 is a status on the caster.
        Assert.True(Effects.Decode(E(15, value: 786)).ToSource);
        Assert.Equal(EffectKind.StatusOff, Effects.Decode(E(17, value: 1871)).Kind);
        var reflected = Effects.Decode(E(3, p4: 0xA0, value: 300));
        Assert.True(reflected.FromTarget);
        Assert.True(reflected.ToSource);
        Assert.Equal(7u, Effects.Decode(E(7, value: 7)).Status);
    }

    [Fact]
    public void What_a_target_took_from_its_effects()
    {
        var (dmg, fx) = Effects.Taken(new[] { E(3, 0x60, value: 1000), E(14, value: 1138), E(3, value: 500) });
        Assert.Equal(1500, dmg);
        Assert.Equal(Effects.FxCrit | Effects.FxDirectHit, fx);
        Assert.Equal((0L, Effects.FxDodged), Effects.Taken(new[] { E(1) }));
        Assert.Equal((0L, Effects.FxResisted), Effects.Taken(new[] { E(2) }));
        Assert.Equal((0L, Effects.FxInvulnerable), Effects.Taken(new[] { E(7, value: 82) }));
        Assert.Equal((0L, Effects.FxInstantDeath), Effects.Taken(new[] { E(51) }));
        Assert.Equal((200L, Effects.FxBlocked), Effects.Taken(new[] { E(5, value: 200) }));
        Assert.Equal((200L, Effects.FxParried), Effects.Taken(new[] { E(6, value: 200) }));
        // Reflected back at the attacker, or lifesteal to the caster: not the target's.
        Assert.Equal((0L, 0), Effects.Taken(new[] { E(3, p4: 0xA0, value: 300), E(4, p4: 0x80, value: 900) }));
    }
}

/// <summary>A listener that keeps everything it hears.</summary>
internal sealed class Heard : ICombatSink
{
    public readonly List<(double T, uint Member, uint Caster, uint Action)> Used = new();
    public readonly List<PartyEffect> Effects = new();
    public readonly List<TakenHit> Taken = new();
    public readonly List<(TickEvent Tick, TickShare[] Shares)> Ticks = new();

    void ICombatSink.Used(double t, uint member, uint caster, uint action) => Used.Add((t, member, caster, action));

    void ICombatSink.Effect(in PartyEffect e) => Effects.Add(e);

    void ICombatSink.Taken(in TakenHit e) => Taken.Add(e);

    void ICombatSink.Tick(in TickEvent e, ReadOnlySpan<TickShare> shares) => Ticks.Add((e, shares.ToArray()));
}

public class CombatParserTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;

    public CombatParserTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

    // The party: a tank (1), a healer (2), a dps (3); enemies 900+, a pet 50 of the dps, an outsider 77.
    private const uint Tank = 1, Healer = 2, Dps = 3, Boss = 900, Pet = 50, Outsider = 77;

    private static readonly HashSet<uint> Shields = new() { 297 };
    private static readonly HashSet<uint> Dots = new() { 1871, 1200 };

    internal static CombatParser Parser(out Heard heard, Func<uint, int>? category = null)
    {
        var p = new CombatParser(category ?? (a => a == 4238 ? 9 : a == 29000 ? 15 : a == 7 ? 8 : 3), Shields.Contains, Dots.Contains);
        heard = new Heard();
        p.Add(heard);
        p.SetParty(new[] { Tank, Healer, Dps });
        return p;
    }

    internal static RawEffect E(byte type, byte p0 = 0, byte p1 = 0, byte p2 = 0, byte p3 = 0, byte p4 = 0, ushort value = 0) => new(type, p0, p1, p2, p3, p4, value);

    /// <summary>One action, its targets each with their effects (and HP / max HP, hostile).</summary>
    internal static EffectBatch Batch(double t, uint caster, uint action, uint owner = 0, params (uint Target, RawEffect[] Effects, long Hp, long Max, bool Hostile)[] targets)
    {
        var b = new EffectBatch();
        b.Begin(t, caster, owner, action, 1);
        foreach (var (target, effects, hp, max, hostile) in targets)
        {
            var k = b.Add(target);
            effects.CopyTo(b.EffectsOf(k));
            b.Hp[k] = hp;
            b.MaxHp[k] = max;
            b.Hostile[k] = hostile;
        }
        return b;
    }

    [Fact]
    public void The_partys_damage_is_its_own_and_others_are_not_read()
    {
        var p = Parser(out var heard);
        p.Action(Batch(1, Dps, 100, 0, (Boss, new[] { E(3, 0x20, value: 5000) }, -1, -1, true)));
        var e = Assert.Single(heard.Effects);
        Assert.Equal(FxKind.Damage, e.Kind);
        Assert.Equal(Dps, e.Member);
        Assert.Equal(Boss, e.Recipient);
        Assert.Equal(5000, e.Amount);
        Assert.Equal(FxFlags.Crit, e.Flags);
        Assert.Equal((1.0, Dps, Dps, 100u), heard.Used.Single());
        // An outsider hitting the boss, or the boss hitting an outsider: none of the party's business.
        Assert.False(p.Concerns(Outsider, 0, new[] { Boss }));
        Assert.False(p.Concerns(Boss, 0, new[] { Outsider }));
        Assert.True(p.Concerns(Boss, 0, new[] { Outsider, Healer }));
        p.Action(Batch(2, Outsider, 100, 0, (Boss, new[] { E(3, value: 5000) }, -1, -1, true)));
        p.Action(Batch(2, Boss, 100, 0, (Outsider, new[] { E(3, value: 5000) }, -1, -1, false)));
        Assert.Single(heard.Effects);
        Assert.Empty(heard.Taken);
    }

    [Fact]
    public void A_pet_is_its_owners()
    {
        var p = Parser(out var heard);
        p.Action(Batch(1, Pet, 200, Dps, (Boss, new[] { E(3, value: 900) }, -1, -1, true)));
        var e = Assert.Single(heard.Effects);
        Assert.Equal(Dps, e.Member);
        Assert.Equal(Pet, e.Caster);
        Assert.Equal(FxFlags.Pet, e.Flags);
        Assert.Equal((1.0, Dps, Pet, 200u), heard.Used.Single());
        // Someone else's pet isn't the party's.
        p.Action(Batch(2, 51, 200, Outsider, (Boss, new[] { E(3, value: 900) }, -1, -1, true)));
        Assert.Single(heard.Effects);
    }

    [Theory]
    [InlineData(4238u, true)]
    [InlineData(29000u, true)]
    [InlineData(7u, false)]
    public void Limit_breaks_are_flagged_by_their_category(uint action, bool lb)
    {
        var p = Parser(out var heard);
        p.Action(Batch(1, Dps, action, 0, (Boss, new[] { E(3, p3: 0x40, p4: 0x40, value: 0) }, -1, -1, true)));
        var e = Assert.Single(heard.Effects);
        Assert.Equal(lb, (e.Flags & FxFlags.LimitBreak) != 0);
        // 0x40 << 16: four million, a limit break's size.
        Assert.Equal(0x40L << 16, e.Amount);
    }

    [Fact]
    public void A_hit_too_big_to_be_real_is_dropped_and_counted_but_never_a_limit_breaks()
    {
        var p = Parser(out var heard);
        p.Action(Batch(1, Dps, 100, 0, (Boss, new[] { E(3, p3: 0x60, p4: 0x40) }, -1, -1, true)));
        Assert.Empty(heard.Effects);
        Assert.Equal(1, p.Anomalies);
        p.Action(Batch(2, Dps, 4238, 0, (Boss, new[] { E(3, p3: 0x60, p4: 0x40) }, -1, -1, true)));
        Assert.Single(heard.Effects);
        Assert.Equal(1, p.Anomalies);
    }

    [Fact]
    public void Reflected_damage_is_the_tanks()
    {
        var p = Parser(out var heard);
        // The boss hits the tank, who reflects 300 back (from the target, to the source).
        p.Action(Batch(1, Boss, 600, 0, (Tank, new[] { E(3, value: 20_000), E(3, p4: 0xA0, value: 300) }, 90_000, 100_000, false)));
        var hit = Assert.Single(heard.Taken);
        Assert.Equal(Tank, hit.Target);
        Assert.Equal(20_000, hit.Amount);
        Assert.Equal(90_000, hit.HpBefore);
        var back = Assert.Single(heard.Effects);
        Assert.Equal(Tank, back.Member);
        Assert.Equal(Boss, back.Recipient);
        Assert.Equal(300, back.Amount);
        Assert.Equal(FxFlags.Reflected, back.Flags);
        // The boss didn't use anything of the party's.
        Assert.Empty(heard.Used);
    }

    [Fact]
    public void Lifesteal_heals_the_one_who_drew_it()
    {
        var p = Parser(out var heard);
        var b = Batch(1, Dps, 100, 0, (Boss, new[] { E(3, value: 5000), E(4, p4: 0x80, value: 1200) }, -1, -1, true));
        b.CasterHp = 79_000;
        b.CasterMaxHp = 80_000;
        p.Action(b);
        Assert.Equal(2, heard.Effects.Count);
        var heal = heard.Effects[1];
        Assert.Equal(FxKind.Heal, heal.Kind);
        Assert.Equal(Dps, heal.Recipient);
        Assert.True(heal.RecipientParty);
        Assert.Equal(FxFlags.Self, heal.Flags);
        Assert.Equal(79_000, heal.HpBefore);
        // 1,000 was missing: 200 over.
        Assert.Equal(200, heal.Overheal);
        Assert.True(CombatParser.WantsCaster(b.EffectsOf(0)));
    }

    [Fact]
    public void Heals_count_what_was_missing_and_mp_is_never_healing()
    {
        var p = Parser(out var heard);
        p.Action(Batch(1, Healer, 135, 0, (Tank, new[] { E(4, p1: 0x20, value: 10_000), E(11, value: 500) }, 95_000, 100_000, false)));
        var heal = Assert.Single(heard.Effects);
        Assert.Equal(FxFlags.Crit, heal.Flags);
        Assert.Equal(5_000, heal.Overheal);
        Assert.Equal(0, PartyEffect.Over(10_000, -1, 100_000));
        Assert.Equal(0, PartyEffect.Over(10_000, 50_000, 100_000));
        Assert.Equal(10_000, PartyEffect.Over(10_000, 100_000, 100_000));
        Assert.True(p.WantsTarget(Tank, Array.Empty<RawEffect>()));
        Assert.True(p.WantsTarget(Outsider, new[] { E(4, value: 1) }));
        Assert.False(p.WantsTarget(Boss, new[] { E(3, value: 1) }));
    }

    [Fact]
    public void A_heal_on_an_enemy_isnt_the_partys_but_isnt_nonsense_a_heal_too_big_is()
    {
        var p = Parser(out var heard);
        // An enemy healed by the party (one that absorbs the element it was hit with): left out, the
        // meter not stopped for it (three in a row would have turned it off for the session).
        p.Action(Batch(1, Healer, 135, 0, (Boss, new[] { E(4, value: 10_000) }, 500, 1000, true)));
        for (var i = 0; i < 5; i++) p.Action(Batch(2 + i, Dps, 142, 0, (Boss, new[] { E(4, value: 30_000) }, 500, 1000, true)));
        Assert.Empty(heard.Effects);
        Assert.Equal(0, p.Anomalies);
        // A heal over five million on anyone is (the game moved something); a limit break's isn't checked.
        p.Action(Batch(9, Healer, 135, 0, (Tank, new[] { E(4, p3: 0x60, p4: 0x40, value: 1) }, 500, 1000, false)));
        Assert.Empty(heard.Effects);
        Assert.Equal(1, p.Anomalies);
    }

    [Fact]
    public void Shields_are_shields_and_a_status_alone_is_its_own_row()
    {
        var p = Parser(out var heard);
        p.Action(Batch(1, Healer, 185, 0, (Tank, new[] { E(4, value: 3000), E(14, value: 297) }, 90_000, 100_000, false)));
        Assert.Equal(new[] { FxKind.Heal, FxKind.Shield }, heard.Effects.Select(e => e.Kind));
        Assert.Equal(297u, heard.Effects[1].Status);
        Assert.Equal(0, heard.Effects[1].Amount);
        heard.Effects.Clear();
        // A DoT put on with damage: the damage only (the status is the statuses' business).
        p.Action(Batch(2, Healer, 16532, 0, (Boss, new[] { E(3, value: 4000), E(14, value: 1871) }, -1, -1, true)));
        Assert.Equal(FxKind.Damage, Assert.Single(heard.Effects).Kind);
        heard.Effects.Clear();
        // A buff alone: a status row.
        p.Action(Batch(3, Dps, 3557, 0, (Tank, new[] { E(14, value: 786) }, -1, -1, false)));
        var st = Assert.Single(heard.Effects);
        Assert.Equal(FxKind.Status, st.Kind);
        Assert.Equal(786u, st.Status);
        Assert.Equal(16532u, p.ActionFor(Boss, 1871, Healer));
        // A buff on the party is remembered too (the combat block's statuses name its action).
        Assert.Equal(3557u, p.ActionFor(Tank, 786, Dps));
    }

    [Fact]
    public void Damage_between_party_members_isnt_kept()
    {
        var p = Parser(out var heard);
        p.Action(Batch(1, Dps, 100, 0, (Tank, new[] { E(3, value: 100) }, 100, 100, false)));
        Assert.Empty(heard.Effects);
        Assert.Empty(heard.Taken);
    }

    [Fact]
    public void An_instant_death_is_taken_even_with_no_amount()
    {
        var p = Parser(out var heard);
        p.Action(Batch(1, Boss, 700, 0, (Healer, new[] { E(51) }, 80_000, 80_000, false), (Tank, new[] { E(1) }, 100_000, 100_000, false)));
        var hit = Assert.Single(heard.Taken);
        Assert.Equal(Healer, hit.Target);
        Assert.Equal(Effects.FxInstantDeath, hit.Fx);
    }

    [Fact]
    public void A_tick_goes_to_whom_the_game_names_a_pet_to_its_owner()
    {
        var p = Parser(out var heard);
        p.Action(Batch(1, Healer, 16532, 0, (Boss, new[] { E(14, value: 1871) }, -1, -1, true)));
        p.Tick(3, Boss, 0, false, 2500, Healer, 0, -1, -1);
        var (tick, shares) = heard.Ticks.Single();
        Assert.True(tick.SourceParty);
        var share = Assert.Single(shares);
        Assert.Equal(Healer, share.Member);
        Assert.Equal(2500, share.Amount);
        Assert.False(share.Estimated);
        // Named after the DoT the healer put on it.
        Assert.Equal(1871u, share.Status);
        Assert.Equal(16532u, share.Action);
        p.Tick(4, Boss, 1200, false, 900, Pet, Dps, -1, -1);
        Assert.Equal(Dps, heard.Ticks[1].Shares.Single().Member);
    }

    /// <summary>A frame's reading of statuses, as the game side does it: each target's list read, with the party's statuses on it (target, status, member).</summary>
    internal static void Statuses(CombatParser p, double t, params (uint On, uint Status, uint Member)[] on)
    {
        p.BeginStatuses();
        foreach (var target in on.Select(x => x.On).Append(Boss).Distinct()) p.StatusesOf(target);
        foreach (var (target, status, member) in on) p.StatusOn(t, target, status, member);
        p.EndStatuses(t);
    }

    [Fact]
    public void A_tick_that_names_no_one_is_shared_between_the_partys_dots()
    {
        var p = Parser(out var heard);
        p.Action(Batch(1, Healer, 16532, 0, (Boss, new[] { E(14, value: 1871) }, -1, -1, true)));
        p.Action(Batch(1.5, Dps, 3560, 0, (Boss, new[] { E(14, value: 1200) }, -1, -1, true)));
        // A buff the tank put on the boss that isn't a DoT (Reprisal-like): not a sharer.
        p.Action(Batch(1.6, Tank, 7535, 0, (Boss, new[] { E(14, value: 1193) }, -1, -1, true)));
        // The game's lists, read the next frame: the DoTs (in the dps's order first, as a list may have them).
        Statuses(p, 1.7, (Boss, 1200, Dps), (Boss, 1871, Healer), (Boss, 1193, Tank));
        p.Tick(3, Boss, 0, false, 1001, 0, 0, -1, -1);
        var shares = heard.Ticks.Single().Shares;
        Assert.Equal(2, shares.Length);
        Assert.All(shares, s => Assert.True(s.Estimated));
        // In the party list's order, a whole share each and the remainder the first's.
        Assert.Equal(new[] { Healer, Dps }, shares.Select(s => s.Member));
        Assert.Equal(new long[] { 501, 500 }, shares.Select(s => s.Amount));
        // Named after the DoT each put on, and the action that did.
        Assert.Equal((1871u, 16532u), (shares[0].Status, shares[0].Action));
        Assert.Equal((1200u, 3560u), (shares[1].Status, shares[1].Action));
        // A tick naming its status: only those with that one on.
        p.Tick(3.5, Boss, 1200, false, 900, 0, 0, -1, -1);
        Assert.Equal(new[] { (Dps, 900L) }, heard.Ticks[1].Shares.Select(x => (x.Member, x.Amount)));
        // Another player's DoT (a source outside the party): never handed to the party.
        p.Tick(4, Boss, 0, false, 500, Outsider, 0, -1, -1);
        Assert.Empty(heard.Ticks[2].Shares);
        // A HoT's tick that names no one is nobody's.
        p.Tick(4.5, Tank, 0, true, 3000, 0, 0, 50_000, 100_000);
        Assert.Empty(heard.Ticks[3].Shares);
    }

    [Fact]
    public void Who_shares_a_tick_is_what_the_games_lists_say_not_when_a_dot_was_put_on()
    {
        var p = Parser(out var heard);
        // The healer's Dia put on long ago and still on (refreshed in the lists): still theirs to share.
        p.Action(Batch(1, Healer, 16532, 0, (Boss, new[] { E(14, value: 1871) }, -1, -1, true)));
        Statuses(p, 2, (Boss, 1871, Healer));
        Statuses(p, 90, (Boss, 1871, Healer));
        p.Tick(90.5, Boss, 0, false, 700, 0, 0, -1, -1);
        Assert.Equal(new[] { Healer }, heard.Ticks[0].Shares.Select(s => s.Member));
        // Gone from the boss's list (it ran out): nobody's, though it was put on a moment ago.
        p.Action(Batch(91, Dps, 3560, 0, (Boss, new[] { E(14, value: 1200) }, -1, -1, true)));
        Statuses(p, 92);
        p.Tick(92.5, Boss, 0, false, 700, 0, 0, -1, -1);
        Assert.Empty(heard.Ticks[1].Shares);
        // An enemy whose list wasn't read (out of sight) keeps its DoTs; one not read for a minute is forgotten.
        const uint Add = 901;
        p.BeginStatuses();
        p.StatusesOf(Add);
        p.StatusOn(93, Add, 1200, Dps);
        p.EndStatuses(93);
        Statuses(p, 120);
        p.Tick(120.5, Add, 0, false, 600, 0, 0, -1, -1);
        Assert.Equal(new[] { (Dps, 600L) }, heard.Ticks[2].Shares.Select(x => (x.Member, x.Amount)));
        Statuses(p, 160);
        p.Tick(160.5, Add, 0, false, 600, 0, 0, -1, -1);
        Assert.Empty(heard.Ticks[3].Shares);
        // A status that isn't one of the jobs' DoTs, or a DoT on a party member, isn't kept.
        Statuses(p, 161, (Boss, 1193, Tank), (Tank, 1871, Healer));
        p.Tick(161.5, Boss, 0, false, 600, 0, 0, -1, -1);
        Assert.Empty(heard.Ticks[4].Shares);
        Assert.False(p.DotOn(Tank, Healer));
    }

    [Fact]
    public void Bleeds_on_the_party_and_hots()
    {
        var p = Parser(out var heard);
        p.Tick(1, Tank, 0, false, 3000, Boss, 0, 50_000, 100_000);
        var (bleed, none) = heard.Ticks[0];
        Assert.True(bleed.TargetParty);
        Assert.False(bleed.SourceParty);
        Assert.Equal(Boss, bleed.Source);
        Assert.Empty(none);
        p.Tick(2, Tank, 0, true, 3000, Healer, 0, 99_000, 100_000);
        var (hot, shares) = heard.Ticks[1];
        Assert.Equal(2000, hot.Overheal);
        Assert.Equal(Healer, shares.Single().Member);
        // Nothing at all: no tick.
        p.Tick(3, Tank, 0, true, 0, Healer, 0, 99_000, 100_000);
        Assert.Equal(2, heard.Ticks.Count);
    }

    [Fact]
    public void Nothing_is_read_with_no_one_listening_and_an_item_isnt_an_action()
    {
        var p = new CombatParser();
        p.SetParty(new[] { Dps });
        Assert.False(p.Concerns(Dps, 0, Array.Empty<uint>()));
        var heard = new Heard();
        p.Add(heard);
        var b = Batch(1, Dps, 100, 0, (Boss, new[] { E(3, value: 10) }, -1, -1, true));
        b.ActionType = 2;
        p.Action(b);
        Assert.Empty(heard.Effects);
        p.Remove(heard);
        Assert.False(p.Listening);
    }

    /// <summary>
    /// The hot path: an eight-target action read again and again allocates nothing (measured with the
    /// runtime's own counter), and takes about ten microseconds with a tick.
    /// </summary>
    [Fact]
    public void Reading_an_action_allocates_nothing()
    {
        var p = new CombatParser(_ => 3, _ => false, _ => false);
        p.SetParty(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        p.Add(new Quiet());
        var b = new EffectBatch();
        var concerns = new uint[] { 900 };
        void Fill(double t)
        {
            b.Begin(t, 1, 0, 100, 1);
            for (uint i = 0; i < 8; i++)
            {
                var k = b.Add(900 + i);
                var e = b.EffectsOf(k);
                e[0] = E(3, 0x20, value: 5000);
                e[1] = E(14, value: 1871);
            }
        }
        for (var i = 0; i < 5000; i++)
        {
            Fill(i * 0.001);
            p.Concerns(1, 0, concerns);
            p.Action(b);
        }
        // Measured in up to three rounds, the least kept: the runtime itself can allocate on this thread now and then (a few KB
        // once, as it compiles a method again or replaces a running loop: seen once, 8,160 bytes, on a busy machine), which the
        // parser would do on every message (20,000 runs: hundreds of KB) were it allocating. Each round's time and bytes are said.
        var watch = new System.Diagnostics.Stopwatch();
        const int Runs = 20_000;
        var allocated = long.MaxValue;
        for (var round = 0; round < 3 && allocated != 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            for (var i = 0; i < Runs; i++)
            {
                Fill(5 + round * 100 + i * 0.001);
                p.Concerns(1, 0, concerns);
                p.Action(b);
                p.Tick(5 + round * 100 + i * 0.001, 900, 0, false, 100, 1, 0, -1, -1);
            }
            watch.Stop();
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            allocated = Math.Min(allocated, bytes);
            output.WriteLine($"round {round + 1}: {watch.Elapsed.TotalMilliseconds / Runs * 1000:F2} µs for an eight-target action and a tick; {bytes} bytes allocated in {Runs}");
        }
        Assert.Equal(0, allocated);
        // About 10 µs here; the bound only catches something gone badly wrong (the machine may be busy with other work).
        Assert.True(watch.Elapsed.TotalMilliseconds / Runs < 0.5, $"{watch.Elapsed.TotalMilliseconds / Runs * 1000:F2} µs a message");
    }

    private sealed class Quiet : ICombatSink
    {
        public void Used(double t, uint member, uint caster, uint action)
        {
        }

        public void Effect(in PartyEffect e)
        {
        }

        public void Taken(in TakenHit e)
        {
        }

        public void Tick(in TickEvent e, ReadOnlySpan<TickShare> shares)
        {
        }
    }
}

public class PhaseClockTests
{
    [Fact]
    public void Counts_phases_as_the_recording_does()
    {
        var clock = new PhaseClock();
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", _ => null);
        void Frame(double t, params (int Actor, bool On)[] now)
        {
            foreach (var (a, on) in now)
            {
                clock.Targetable(t, (uint)a, on);
                b.Targetable(t, a, on);
            }
            clock.EndFrame(t);
            b.EndTargets(t);
        }
        Frame(0, (101, true), (102, true));
        Frame(1, (101, false), (102, true));
        Frame(20, (101, true), (102, true));
        Frame(30, (101, false));
        Frame(38, (103, true));
        Frame(40, (103, false));
        Frame(45, (103, true));
        Frame(50, (103, false));
        Frame(60, (103, true));
        Assert.Equal(3, clock.Phase);
        Assert.Equal(b.Phase, clock.Phase);
        Assert.Equal(new[] { 0.0, 38, 60 }, clock.Starts);
        Assert.Equal(b.PhaseStarts, clock.Starts);
        Assert.Equal((1, 38.0), clock.At(50));
        Assert.Equal((0, 0.0), clock.At(10));
    }
}

public class MeterSessionTests
{
    private const uint Tank = 1, Healer = 2, Dps = 3, Boss = 900;

    private static PartyState P(uint id, string job, byte role, long hp, long max = 100_000, bool combat = true, bool you = false) =>
        new(id, $"Name{id}", job, role, hp, max, combat, you);

    private static PartyState[] Party(long tank = 100_000, long healer = 100_000, long dps = 100_000, bool combat = true) => new[]
    {
        P(Tank, "PLD", 1, tank, combat: combat), P(Healer, "WHM", 4, healer, combat: combat), P(Dps, "BLM", 3, dps, combat: combat, you: true),
    };

    private static MeterSession New(out CombatParser parser)
    {
        var s = new MeterSession(a => $"A{a}", st => $"S{st}") { EndAfter = 4 };
        parser = CombatParserTests.Parser(out _);
        parser.Add(s);
        s.Place(1, 77, "Test Arena");
        return s;
    }

    private static void Hit(CombatParser p, double t, uint caster, long amount, uint action = 100, byte p0 = 0, uint owner = 0) =>
        p.Action(CombatParserTests.Batch(t, caster, action, owner, (Boss, new[] { CombatParserTests.E(3, p0, value: (ushort)(amount & 0xFFFF), p3: (byte)(amount >> 16), p4: (byte)(amount > 0xFFFF ? 0x40 : 0)) }, -1, -1, true)));

    private static void Big(CombatParser p, double t, uint caster, long amount, uint action = 100) =>
        p.Action(CombatParserTests.Batch(t, caster, action, 0, (Boss, new[] { CombatParserTests.E(3, value: (ushort)(amount & 0xFFFF), p3: (byte)(amount >> 16), p4: 0x40) }, -1, -1, true)));

    private static void Heal(CombatParser p, double t, uint healer, uint on, long amount, long hp, long max = 100_000) =>
        p.Action(CombatParserTests.Batch(t, healer, 135, 0, (on, new[] { CombatParserTests.E(4, value: (ushort)amount) }, hp, max, false)));

    private static void Taken(CombatParser p, double t, uint on, long amount, long hp, uint action = 600) =>
        p.Action(CombatParserTests.Batch(t, Boss, action, 0, (on, new[] { CombatParserTests.E(3, value: (ushort)(amount & 0xFFFF), p3: (byte)(amount >> 16), p4: (byte)(amount > 0xFFFF ? 0x40 : 0)) }, hp, 100_000, false)));

    [Fact]
    public void Waits_in_combat_then_starts_on_the_first_damage_with_the_second_before()
    {
        var s = New(out var p);
        s.Frame(0, Party(combat: false), false);
        Assert.Null(s.Current);
        Assert.False(s.Waiting);
        s.Frame(1, Party(), true);
        Assert.True(s.Waiting);
        Assert.False(s.InEncounter);
        // A heal before the pull (folded in), and one long before (not).
        Heal(p, 0.5, Healer, Tank, 1000, 50_000);
        Heal(p, 9.5, Healer, Tank, 2000, 50_000);
        s.Frame(10, Party(), true);
        Assert.False(s.InEncounter);
        Hit(p, 10, Dps, 5000);
        Assert.True(s.InEncounter);
        Assert.False(s.Waiting);
        s.Frame(12, Party(), true);
        var e = s.Current!;
        Assert.True(e.Live);
        Assert.Equal(2, e.Duration, 3);
        Assert.Equal(1, e.Pull);
        Assert.Equal("Test Arena", e.Duty);
        Assert.Equal(77u, e.Content);
        Assert.Equal(2000, e.Rows.Single(r => r.Slot == "H1").Healing);
        // A DoT's tick alone never starts one.
        var t = New(out var q);
        t.Frame(0, Party(), true);
        q.Tick(1, Boss, 1871, false, 500, Dps, 0, -1, -1);
        Assert.False(t.InEncounter);
        // An enemy's hit on the party does.
        Taken(q, 2, Tank, 3000, 100_000);
        Assert.True(t.InEncounter);
    }

    [Fact]
    public void Encdps_dps_shares_and_the_party_add_up()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        Hit(p, 0, Dps, 10_000);
        Hit(p, 5, Dps, 10_000, 100, 0x20);
        Hit(p, 2, Tank, 4_000);
        Hit(p, 6, Tank, 6_000, 101, 0x40);
        Big(p, 8, Dps, 0x40_0000, 4238);
        s.Frame(10, Party(), true);
        var e = s.Current!;
        Assert.Equal(new[] { "MT", "H1", "R1", "LB" }, e.Rows.Select(r => r.Slot));
        var dps = e.Rows.Single(r => r.Slot == "R1");
        Assert.Equal(20_000, dps.Damage);
        Assert.Equal(2_000, dps.EncDps, 6);
        // Their own time: 0 to 5 s.
        Assert.Equal(4_000, dps.Dps, 6);
        Assert.Equal(5, dps.ActiveSeconds, 6);
        Assert.Equal(0.5, dps.CritRate, 6);
        Assert.Equal(10_000, dps.MaxHit);
        Assert.True(dps.IsYou);
        var tank = e.Rows.Single(r => r.Slot == "MT");
        Assert.Equal(0.5, tank.DirectHitRate, 6);
        Assert.Equal("A101", tank.MaxHitName);
        var lb = e.Rows.Last();
        Assert.True(lb.IsLimitBreak);
        Assert.Equal(0x40_0000, lb.Damage);
        // The limit break is no one's damage.
        Assert.Equal(20_000, dps.Damage);
        Assert.Equal(e.Rows.Sum(r => r.Damage), e.Party.Damage);
        Assert.Equal(e.Rows.Sum(r => r.EncDps), e.Party.EncDps, 3);
        Assert.Equal(1, e.Rows.Sum(r => r.DamageShare), 9);
        Assert.Equal(e.Party.Damage / 10.0, e.Party.EncDps, 6);
        Assert.Equal(0, e.Rows.Single(r => r.Slot == "H1").Damage);
    }

    [Fact]
    public void Healing_is_what_landed()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        Hit(p, 0, Dps, 1000);
        Heal(p, 1, Healer, Tank, 10_000, 95_000);
        Heal(p, 2, Healer, Dps, 10_000, 50_000);
        p.Tick(3, Tank, 158, true, 2000, Healer, 0, 99_000, 100_000);
        s.Frame(10, Party(), true);
        var h = s.Current!.Rows.Single(r => r.Slot == "H1");
        Assert.Equal(5_000 + 10_000 + 1_000, h.Healing);
        Assert.Equal(5_000 + 1_000, h.Overheal);
        Assert.Equal(1_600, h.Hps, 6);
        Assert.Equal(6_000.0 / 22_000, h.OverhealRate, 6);
        Assert.Equal(1, h.HealingShare, 6);
        Assert.Equal(10_000, h.MaxHeal);
        var abilities = s.Abilities(s.Current!.Id, h.PartyId, MeterTab.Healing);
        Assert.Equal(2, abilities.Count);
        var tick = abilities.Single(a => a.IsTick);
        Assert.Null(tick.CritRate);
        Assert.Equal(1_000, tick.Amount);
    }

    [Fact]
    public void Taken_by_what_hit_them_bleeds_included()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        Taken(p, 0, Tank, 30_000, 100_000);
        Taken(p, 1, Tank, 20_000, 70_000, 601);
        p.Tick(2, Tank, 0, false, 3_000, Boss, 0, 50_000, 100_000);
        s.Frame(4, Party(), true);
        var tank = s.Current!.Rows.Single(r => r.Slot == "MT");
        Assert.Equal(53_000, tank.Taken);
        Assert.Equal(30_000, tank.MaxTaken);
        Assert.Equal(1, tank.TakenShare, 6);
        var by = s.Abilities(s.Current!.Id, tank.PartyId, MeterTab.Taken);
        Assert.Equal(new[] { "A600", "A601", "Damage over time" }, by.Select(a => a.Name));
        Assert.Null(by[0].CritRate);
    }

    [Fact]
    public void A_death_keeps_its_recap_the_killing_blow_and_the_overkill()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        Taken(p, 0, Healer, 38_620, 97_210, 700);
        Heal(p, 1.2, Tank, Healer, 6_120, 58_590);
        Heal(p, 2.7, Dps, Healer, 21_400, 64_710);
        Taken(p, 6.1, Healer, 104_880, 86_110, 700);
        s.Frame(6.5, Party(healer: 0), true);
        var e = s.Current!;
        var d = Assert.Single(e.Deaths);
        Assert.Equal("H1", d.Slot);
        Assert.Equal(6.5, d.At, 6);
        Assert.Equal("A700", d.KilledBy);
        Assert.Equal(104_880, d.Amount);
        Assert.Equal(18_770, d.Overkill);
        Assert.Equal(new long[] { -38_620, 6_120, 21_400, -104_880 }, d.Recap.Select(r => r.Amount));
        Assert.Equal(new long[] { 97_210, 58_590, 64_710, 86_110 }, d.Recap.Select(r => r.HpBefore));
        Assert.Equal("Name1", d.Recap[1].Note);
        var row = e.Rows.Single(r => r.Slot == "H1");
        Assert.True(row.Dead);
        Assert.Equal(6.5, row.DiedAt!.Value, 6);
        Assert.Equal(1, row.Deaths);
        // Raised, and HP 0 again: a second death.
        s.Frame(8, Party(healer: 1), true);
        s.Frame(9, Party(healer: 0), true);
        Assert.Equal(2, s.Current!.Deaths.Count);
    }

    [Fact]
    public void Ends_out_of_combat_and_the_duty_says_how()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        Hit(p, 0, Dps, 1000);
        Hit(p, 5, Dps, 1000);
        s.Frame(6, Party(combat: false), false);
        s.Frame(9.9, Party(combat: false), false);
        Assert.True(s.InEncounter);
        s.Frame(10, Party(combat: false), false);
        Assert.False(s.InEncounter);
        var e = s.Current!;
        Assert.False(e.Live);
        Assert.Equal(Outcome.Ended, e.Ended);
        // From the first hit to the last.
        Assert.Equal(5, e.Duration, 6);
        // "Duty complete" a moment later: a clear.
        s.Frame(12, Party(combat: false), false);
        s.Said(Outcome.Clear);
        Assert.Equal(Outcome.Clear, s.Current!.Ended);
        Assert.Equal(e.Id, s.Current!.Id);
    }

    [Fact]
    public void Everyone_down_is_a_wipe_and_its_last_deaths_are_the_wipe()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        Hit(p, 0, Dps, 1000);
        s.Frame(5, Party(dps: 0), true);
        s.Frame(20, Party(dps: 0, tank: 0), true);
        s.Frame(23, Party(dps: 0, tank: 0, healer: 0), true);
        var e = s.Current!;
        Assert.Equal(Outcome.Wipe, e.Ended);
        Assert.False(s.InEncounter);
        Assert.Equal(23, e.Duration, 6);
        Assert.Equal(new[] { false, true, true }, e.Deaths.Select(d => d.PartOfWipe));
        // The wipe's deaths aren't counted against anyone.
        Assert.Equal(1, e.Rows.Single(r => r.Slot == "R1").Deaths);
        Assert.Equal(0, e.Rows.Single(r => r.Slot == "MT").Deaths);
        Assert.True(e.Party.Dead);
    }

    [Fact]
    public void Its_length_is_the_first_damage_to_a_wipes_last_death_else_to_the_last_damage()
    {
        // A wipe the duty called with the dps still up and hitting: it ended at the last death all the same.
        var s = New(out var p);
        s.Frame(0, Party(), true);
        Hit(p, 2, Dps, 1000);
        s.Frame(5, Party(tank: 0), true);
        s.Frame(8, Party(tank: 0, healer: 0), true);
        Hit(p, 9, Dps, 1000);
        s.Frame(9.5, Party(tank: 0, healer: 0), true);
        s.Said(Outcome.Wipe);
        var e = s.Current!;
        Assert.Equal(Outcome.Wipe, e.Ended);
        Assert.Equal(6, e.Duration, 6);
        Assert.Equal(2_000 / 6.0, e.Rows.Single(r => r.Slot == "R1").EncDps, 6);
        // Ended out of combat: at the last damage, a bleed's tick included, a death after it not.
        var t = New(out var q);
        t.Frame(0, Party(), true);
        Hit(q, 1, Dps, 1000);
        q.Tick(4, Tank, 0, false, 500, Boss, 0, 50_000, 100_000);
        t.Frame(4.5, Party(tank: 0), true);
        t.Frame(5, Party(tank: 0, combat: false), false);
        t.Frame(9.5, Party(tank: 0, combat: false), false);
        Assert.Equal(Outcome.Ended, t.Current!.Ended);
        Assert.Equal(3, t.Current!.Duration, 6);
        // Called a wipe a moment later: then at the last death.
        t.Said(Outcome.Wipe);
        Assert.Equal(3.5, t.Current!.Duration, 6);
    }

    [Fact]
    public void Keeps_the_last_ones_newest_first_and_numbers_them_by_the_recorders_pull()
    {
        var s = New(out var p);
        s.Keep = 3;
        for (var i = 0; i < 5; i++)
        {
            var t = i * 100.0;
            s.RecordedPull(i == 4 ? 12 : 0);
            s.Frame(t, Party(), true);
            Hit(p, t, Dps, 1000);
            s.Frame(t + 1, Party(combat: false), false);
            s.Frame(t + 6, Party(combat: false), false);
        }
        Assert.Equal(3, s.Kept.Count);
        Assert.Equal(new[] { 12, 4, 3 }, s.Kept.Select(k => k.Pull));
        Assert.Equal(s.Kept[0].Id, s.Current!.Id);
        Assert.NotNull(s.Get(s.Kept[2].Id));
        Assert.Null(s.Get(1));
        s.Clear();
        Assert.Empty(s.Kept);
        // A new zone counts from 1 again.
        s.Place(2, 78, "Elsewhere");
        s.RecordedPull(0);
        s.Frame(1000, Party(), true);
        Hit(p, 1000, Dps, 1000);
        Assert.Equal(1, s.Current!.Pull);
        Assert.Equal("Elsewhere", s.Current!.Duty);
    }

    /// <summary>An encounter: the boss targetable from the start, gone at <paramref name="p2"/> − 10 and back at <paramref name="p2"/> (P2), the party dealing <paramref name="perSecond"/>.</summary>
    private static void Encounter(MeterSession s, CombatParser p, double start, double p2, double end, long perSecond, Outcome how)
    {
        for (var t = start; t <= end; t += 1)
        {
            s.Frame(t, Party(), true);
            var rel = t - start;
            s.Targetable(Boss, rel < p2 - 10 || rel >= p2);
            s.EndTargets();
            Hit(p, t, Dps, perSecond);
        }
        if (how == Outcome.Clear) s.Said(Outcome.Clear);
        else
        {
            s.Frame(end + 1, Party(combat: false), false);
            s.Frame(end + 10, Party(combat: false), false);
        }
    }

    [Fact]
    public void Pace_compares_the_same_second_of_the_same_phase()
    {
        var s = New(out var p);
        // The best: P2 at 60 s, 1,000 a second, to 120 s.
        Encounter(s, p, 0, 60, 120, 1000, Outcome.Ended);
        // The last: P2 at 50 s, 900 a second, its P2 only 20 s long.
        Encounter(s, p, 1000, 50, 1070, 900, Outcome.Ended);
        // Now: P2 at 55 s, 1,100 a second, 30 s into P2.
        var now = 2000.0;
        for (var t = now; t <= now + 85; t += 1)
        {
            s.Frame(t, Party(), true);
            var rel = t - now;
            s.Targetable(Boss, rel < 45 || rel >= 55);
            s.EndTargets();
            Hit(p, t, Dps, 1100);
        }
        var current = s.Current!;
        Assert.Equal("P2", current.Phase);
        Assert.Equal(55, current.PhaseAt, 6);
        var best = s.Pace(current.Id, PaceAgainst.Best)!;
        Assert.Equal(s.Kept[2].Pull, best.AgainstPull);
        Assert.Equal("P2", best.Phase);
        Assert.Equal(30, best.PhaseSeconds, 6);
        Assert.Equal(30 * 1100, best.Damage);
        Assert.Equal(30 * 1000, best.AgainstDamage);
        Assert.Equal(0.1, best.Ahead, 6);
        // The last one's P2 ended 20 s in: the clocks don't line up.
        Assert.Null(s.Pace(current.Id, PaceAgainst.Last));
        Assert.Null(s.Pace(current.Id, PaceAgainst.Off));
        Assert.Equal(s.Kept[2].Id, s.Best(77)!.Id);
    }

    [Fact]
    public void Pace_waits_ten_seconds_into_a_phase_and_needs_the_phase_and_the_duty()
    {
        var s = New(out var p);
        Encounter(s, p, 0, 60, 120, 1000, Outcome.Ended);
        var now = 1000.0;
        for (var t = now; t <= now + 5; t += 1)
        {
            s.Frame(t, Party(), true);
            Hit(p, t, Dps, 1000);
        }
        Assert.Null(s.Pace(s.Current!.Id, PaceAgainst.Best));
        for (var t = now + 6; t <= now + 20; t += 1)
        {
            s.Frame(t, Party(), true);
            Hit(p, t, Dps, 1000);
        }
        Assert.NotNull(s.Pace(s.Current!.Id, PaceAgainst.Best));
        // Another duty has nothing to compare with.
        s.Stop();
        s.Place(5, 99, "Another");
        Encounter(s, p, 3000, 60, 3100, 1000, Outcome.Ended);
        Assert.Null(s.Pace(s.Kept[0].Id, PaceAgainst.Best));
    }

    [Fact]
    public void The_best_is_a_clear_then_the_furthest_phase()
    {
        var s = New(out var p);
        Encounter(s, p, 0, 60, 100, 1000, Outcome.Ended);
        Encounter(s, p, 1000, 30, 1200, 1000, Outcome.Ended);
        Assert.Equal(s.Kept[0].Id, s.Best(77)!.Id);
        Encounter(s, p, 2000, 500, 2060, 1000, Outcome.Clear);
        Assert.Equal(Outcome.Clear, s.Best(77)!.Ended);
        Assert.Null(s.Best(5));
    }

    [Fact]
    public void Abilities_by_player_with_pets_and_dots()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        Hit(p, 0, Dps, 9_000, 100, 0x20);
        Hit(p, 1, Dps, 3_000, 100);
        Hit(p, 2, 50, 2_000, 200, 0, Dps);
        p.Action(CombatParserTests.Batch(2.5, Dps, 3560, 0, (Boss, new[] { CombatParserTests.E(14, value: 1200) }, -1, -1, true)));
        CombatParserTests.Statuses(p, 2.6, (Boss, 1200, Dps));
        p.Tick(3, Boss, 0, false, 1_000, 0, 0, -1, -1);
        s.Frame(4, Party(), true);
        var e = s.Current!;
        var rows = s.Abilities(e.Id, e.Rows.Single(r => r.Slot == "R1").PartyId, MeterTab.Damage);
        Assert.Equal(new[] { "A100", "A200", "A3560" }, rows.Select(r => r.Name));
        Assert.Equal(12_000, rows[0].Amount);
        Assert.Equal(2, rows[0].Hits);
        Assert.Equal(0.5, rows[0].CritRate);
        Assert.Equal(9_000, rows[0].Max);
        Assert.True(rows[1].IsPet);
        Assert.True(rows[2].IsTick);
        Assert.True(rows[2].Estimated);
        Assert.Null(rows[2].CritRate);
        Assert.Equal(1, rows.Sum(r => r.Share), 9);
        Assert.Same(rows, s.Abilities(e.Id, e.Rows.Single(r => r.Slot == "R1").PartyId, MeterTab.Damage));
        Assert.Empty(s.Abilities(e.Id, 99, MeterTab.Damage));
    }

    [Fact]
    public void A_recorded_pull_that_begins_a_moment_after_the_first_hit_numbers_it()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        Hit(p, 0, Dps, 1000);
        Assert.Equal(1, s.Current!.Pull);
        s.RecordedPull(7);
        s.Frame(0.1, Party(), true);
        Assert.Equal(7, s.Current!.Pull);
        s.Frame(1, Party(combat: false), false);
        s.Frame(6, Party(combat: false), false);
        // The next one, with no recorded pull, counts on from it.
        s.RecordedPull(0);
        s.Frame(10, Party(), true);
        Hit(p, 10, Dps, 1000);
        Assert.Equal(8, s.Current!.Pull);
        // Everyone down after the end: not waiting for a hit.
        s.Frame(11, Party(combat: false), false);
        s.Frame(16, Party(0, 0, 0), true);
        Assert.False(s.Waiting);
    }

    [Fact]
    public void A_member_who_joins_mid_fight_takes_a_free_spot()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        Hit(p, 0, Dps, 1000);
        var four = Party().Append(P(4, "SAM", 2, 100_000)).ToArray();
        p.SetParty(new uint[] { 1, 2, 3, 4 });
        s.Frame(1, four, true);
        Hit(p, 1.5, 4, 1000);
        s.Frame(2, four, true);
        Assert.Contains(s.Current!.Rows, r => r.Slot == "M1" && r.Damage == 1000);
    }
}
