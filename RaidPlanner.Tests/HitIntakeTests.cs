using System;
using System.Linq;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// An enemy's hit taken in (<see cref="HitIntake"/>, <c>Game/FightRecorder.OnHit</c>): every party member a message hit is
/// a target with its damage, whatever the message's size, and a hit that hurt the party is kept whoever the game says cast it.
/// Found from a 0.7.4 pull of The Omega Protocol whose Wave Cannons (Omega's own message for them has no targets: the
/// damage comes in its helpers') were hits with no one hit, though the party lost 17–48k at each and M1 died to one.
/// </summary>
public class HitIntakeTests
{
    private const uint WaveCannon = 32534;

    private static readonly ulong[] Party = { 0x10001001, 0x10001002, 0x10001003, 0x10001004, 0x10001005, 0x10001006, 0x10001007, 0x10001008 };

    private static RecordingBuilder Builder()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", _ => null);
        b.SetParty(Party.Select((k, i) => new Partier(k, "WAR", 1, 100_000, "Tester " + i, "Lich", 100)).ToArray());
        return b;
    }

    /// <summary>A damage effect as the game sends it (amounts past 65,535 with the large-value bit and the high part in Param3).</summary>
    private static RawEffect Damage(long amount) =>
        amount > ushort.MaxValue
            ? new RawEffect(Effects.Damage, 0, 0, 0, (byte)(amount >> 16), 0x40, (ushort)(amount & 0xFFFF))
            : new RawEffect(Effects.Damage, 0, 0, 0, 0, 0, (ushort)amount);

    /// <summary>One message: <paramref name="targets"/> each taking <paramref name="amount"/>, its HP 81,729 as it came.</summary>
    private static EffectBatch Message(uint caster, uint action, ulong[] targets, long amount)
    {
        var batch = new EffectBatch();
        batch.Begin(1, caster, 0, action, 1);
        foreach (var t in targets)
        {
            var k = batch.Add((uint)t);
            if (k < 0) break;
            batch.EffectsOf(k)[0] = Damage(amount);
            batch.Hp[k] = 81_729;
        }
        return batch;
    }

    [Fact]
    public void A_helpers_Wave_Cannon_on_the_whole_party_records_all_eight_with_their_damage()
    {
        var b = Builder();
        var into = new HitIntake.Lists();
        HitIntake.Collect(Message(0x40001234, WaveCannon, Party, 43_115), b, into);
        Assert.Equal(Enumerable.Range(1, 8), into.Targets);
        Assert.All(into.Damage, d => Assert.Equal(43_115, d));
        Assert.All(into.Hp, h => Assert.Equal(81_729, h));
        Assert.True(into.Hurt);
        // A helper (sub-kind 11) the frames haven't met: kept.
        Assert.True(HitIntake.TakeUnmet(true, true, HitIntake.Helper, false, false, into, false));
    }

    [Fact]
    public void A_32_target_message_keeps_every_party_member_in_it_and_no_one_else()
    {
        var b = Builder();
        // An alliance-sized raidwide: sixteen others first, then the party, past 65,535 damage each.
        var others = Enumerable.Range(0, 16).Select(i => 0x10002000UL + (ulong)i);
        var batch = Message(0x40001234, WaveCannon, others.Concat(Party).ToArray(), 120_000);
        Assert.Equal(24, batch.Count);
        var into = new HitIntake.Lists();
        HitIntake.Collect(batch, b, into);
        Assert.Equal(Enumerable.Range(1, 8), into.Targets);
        Assert.All(into.Damage, d => Assert.Equal(120_000, d));
    }

    [Fact]
    public void A_hit_that_hurt_the_party_is_kept_when_the_game_has_no_object_for_its_caster()
    {
        var into = new HitIntake.Lists();
        HitIntake.Collect(Message(0x40001234, WaveCannon, Party, 30_000), Builder(), into);
        Assert.True(HitIntake.TakeUnmet(false, false, 0, false, false, into, true));
    }

    [Theory]
    [InlineData(true, (byte)5, true)] // an enemy's summon, owned by the boss
    [InlineData(true, (byte)0, false)] // a battle NPC of a sub-kind the plugin doesn't name
    [InlineData(false, (byte)0, false)] // not a battle NPC at all
    public void A_hit_that_hurt_the_party_is_kept_whatever_kind_the_game_says_its_caster_is(bool battleNpc, byte subKind, bool owned)
    {
        var into = new HitIntake.Lists();
        HitIntake.Collect(Message(0x40001234, WaveCannon, Party.Take(3).ToArray(), 30_000), Builder(), into);
        Assert.True(HitIntake.TakeUnmet(true, battleNpc, subKind, owned, false, into, false));
    }

    [Fact]
    public void A_pets_heal_or_a_casterless_message_that_hurt_no_one_is_not_an_enemys()
    {
        var b = Builder();
        var heal = new EffectBatch();
        heal.Begin(1, 0x40009999, 0x10001003, 9999, 1);
        heal.EffectsOf(heal.Add((uint)Party[0]))[0] = new RawEffect(Effects.Heal, 0, 0, 0, 0, 0, 5_000);
        var into = new HitIntake.Lists();
        HitIntake.Collect(heal, b, into);
        Assert.Single(into.Targets);
        Assert.False(into.Hurt);
        Assert.False(HitIntake.TakeUnmet(true, true, HitIntake.Combatant, true, true, into, false));
        Assert.False(HitIntake.TakeUnmet(false, false, 0, false, true, into, false));
        // A field zone: an enemy's hit on none of the party, away from you, is someone else's fight.
        HitIntake.Collect(Message(0x40001234, 1, Array.Empty<ulong>(), 0), b, into);
        Assert.False(HitIntake.TakeUnmet(true, true, HitIntake.Combatant, false, false, into, true));
        Assert.True(HitIntake.TakeUnmet(true, true, HitIntake.Combatant, false, true, into, true));
    }

    [Fact]
    public void The_hit_names_its_targets_and_a_death_to_it_has_its_cause()
    {
        var b = Builder();
        var src = b.Actor(0x40001234, string.Empty, 0.5f, 9020);
        var into = new HitIntake.Lists();
        HitIntake.Collect(Message(0x40001234, WaveCannon, Party, 90_000), b, into);
        b.Hit(1, src, WaveCannon, 100, 100, 0, 0, 0, into.Targets, into.Damage, into.Fx, into.Hp, into.Shield, into.X, into.Z);
        var hit = b.Finish(2, Outcome.Wipe).Events.OfType<HitEvent>().Single();
        Assert.Equal(8, hit.Targets.Count);
        Assert.All(hit.Damage!, d => Assert.Equal(90_000, d));
    }
}
