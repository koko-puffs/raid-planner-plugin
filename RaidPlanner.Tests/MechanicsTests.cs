using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// 0.7.4's mechanics sweep: an enemy's facing and place while it's at a mechanic (turn events: a rotation's steps, a helper's
/// heading over its cast), head markers over enemies with whom they point at, and both through the file.
/// </summary>
public class MechanicsTests
{
    private static RecordingBuilder Builder() =>
        new(new DutyInfo("Test", 1, 1), 1, DateTimeOffset.UnixEpoch, true, "0.7.4", _ => new ActionInfo("Spiral", 3, 30, 0, "gl_fan030_1bf", 1));

    private static float Deg(float d) => d * MathF.PI / 180;

    [Fact]
    public void A_boss_turning_through_its_cast_is_recorded_step_by_step()
    {
        var b = Builder();
        var boss = b.Actor(900, "Boss", 5, 1);
        b.Cast(10, boss, 1, 3, 100, 100, 0, 0, null, null);
        // Its first frame at the mechanic is only remembered (the cast has its facing); then each 30° step, clockwise (the game's angle going down).
        b.Facing(10, boss, 100, 100, 0, true);
        b.Facing(11, boss, 100, 100, Deg(-30), true);
        b.Facing(12, boss, 100, 100, Deg(-60), true);
        var turns = b.Finish(20, Outcome.Wipe).Events.OfType<TurnEvent>().ToList();
        Assert.Equal(new[] { 11.0, 12.0 }, turns.Select(x => x.T));
        Assert.All(turns, x => Assert.Equal(boss, x.Actor));
        Assert.Equal(Deg(-60), turns[1].Rotation, 3);
    }

    [Fact]
    public void Small_turns_and_turns_away_from_a_mechanic_arent_kept()
    {
        var b = Builder();
        var boss = b.Actor(900, "Boss", 5, 1);
        // Away from any mechanic: following the tank round isn't recorded.
        b.Facing(1, boss, 100, 100, 0, false);
        b.Facing(2, boss, 100, 100, 1.5f, false);
        b.Cast(5, boss, 1, 2, 100, 100, 0, 0, null, null);
        b.Facing(5, boss, 100, 100, 0, true);
        // A degree isn't a turn; within a tenth of a second of the last isn't looked at.
        b.Facing(5.5, boss, 100, 100, Deg(1), true);
        b.Facing(6, boss, 100, 100, Deg(-10), true);
        b.Facing(6.05, boss, 100, 100, Deg(-40), true);
        // A while after its cast's end (7 + 1.5), no longer at the mechanic.
        b.Facing(9, boss, 100, 100, Deg(-90), false);
        var turns = b.Finish(20, Outcome.Wipe).Events.OfType<TurnEvent>().ToList();
        Assert.Equal(new[] { 6.0 }, turns.Select(x => x.T));
    }

    [Fact]
    public void A_helper_turning_between_its_hits_is_followed_and_its_moves_too()
    {
        var b = Builder();
        var helper = b.Actor(901, "", 0.5f, 2);
        // A chain of hits with no cast: each hit keeps it at the mechanic a moment longer.
        b.Hit(1, helper, 1, 90, 90, 0, 90, 90, Array.Empty<int>());
        b.Facing(1, helper, 90, 90, 0, false);
        b.Hit(2, helper, 1, 90, 90, Deg(45), 90, 90, Array.Empty<int>());
        b.Facing(2, helper, 90, 90, Deg(45), false);
        b.Facing(2.5, helper, 92, 90, Deg(45), false);
        var r = b.Finish(20, Outcome.Wipe);
        var turns = r.Events.OfType<TurnEvent>().ToList();
        Assert.Equal(2, turns.Count);
        Assert.Equal((92f, 90f), (turns[1].X, turns[1].Z));
        // Helpers aren't in the frames: their heading is only in these.
        Assert.Equal(ActorKind.Helper, r.Actors.Single(a => a.Id == helper).Kind);
    }

    [Fact]
    public void A_boss_following_its_tank_between_auto_attacks_isnt_at_a_mechanic()
    {
        // Action 7 an auto-attack (ActionCategory 1), 8 a mechanic's hit with no cast.
        var b = new RecordingBuilder(new DutyInfo("Test", 1, 1), 1, DateTimeOffset.UnixEpoch, true, "0.7.4",
            id => new ActionInfo(id == 7 ? "attack" : "Slash", 1, 0, 0, string.Empty, 0, id == 7 ? 1 : 2));
        var boss = b.Actor(900, "Boss", 5, 1);
        b.Facing(0, boss, 100, 100, 0, false);
        // Its tank moves: the boss swings and turns after them, a while, which is no mechanic (the frames have it).
        for (var i = 1; i <= 20; i++)
        {
            if (i % 5 == 1) b.Hit(i * 0.5, boss, 7, 100, 100, 0, 100, 105, new[] { 1 });
            b.Facing(i * 0.5, boss, 100, 100, Deg(i * 5), false);
        }
        // A hit that is a mechanic still keeps it there a moment: its turn after it is kept.
        b.Hit(11, boss, 8, 100, 100, 0, 100, 105, new[] { 1 });
        b.Facing(11, boss, 100, 100, 0, false);
        b.Facing(11.5, boss, 100, 100, Deg(30), false);
        var r = b.Finish(20, Outcome.Wipe);
        Assert.Equal(new[] { 11.5 }, r.Events.OfType<TurnEvent>().Select(x => x.T));
        // The auto-attacks are still hits in the file.
        Assert.Equal(4, r.Events.OfType<HitEvent>().Count(h => h.Action == 7));
    }

    [Fact]
    public void A_cast_stopped_ends_its_mechanic_soon_after()
    {
        var b = Builder();
        var boss = b.Actor(900, "Boss", 5, 1);
        b.Cast(0, boss, 1, 10, 100, 100, 0, 0, null, null);
        b.Facing(0, boss, 100, 100, 0, true);
        b.Cancel(2, boss, 1, true);
        b.Facing(3, boss, 100, 100, Deg(20), false);
        b.Facing(5, boss, 100, 100, Deg(90), false);
        Assert.Equal(new[] { 3.0 }, b.Finish(20, Outcome.Wipe).Events.OfType<TurnEvent>().Select(x => x.T));
    }

    [Fact]
    public void Turns_have_their_own_cap_and_never_crowd_out_the_rest()
    {
        var b = Builder();
        var boss = b.Actor(900, "Boss", 5, 1);
        var t = 0.0;
        b.Cast(0, boss, 1, 100_000, 100, 100, 0, 0, null, null);
        b.Facing(0, boss, 100, 100, 0, true);
        for (var i = 1; i <= RecordingBuilder.MaxTurns + 50; i++)
        {
            t = i * 0.2;
            b.Facing(t, boss, 100, 100, i % 2 == 0 ? 0 : 1, true);
        }
        Assert.Equal(RecordingBuilder.MaxTurns, b.Turns);
        // Every other event still has its whole room.
        for (var i = 0; i < RecordingBuilder.MaxEvents + 10; i++) b.Marker(t, boss, 1);
        var r = b.Finish(t + 1, Outcome.Wipe);
        Assert.Equal(RecordingBuilder.MaxEvents, r.Events.Count(e => e is not TurnEvent));
    }

    [Fact]
    public void Hits_and_statuses_never_crowd_out_the_mechanics_or_the_deaths()
    {
        // Actions 7 and 9 are auto-attacks (category 1): the many; any other hit is a mechanic, with the few's room.
        var b = new RecordingBuilder(new DutyInfo("Test", 1, 1), 1, DateTimeOffset.UnixEpoch, true, "0.7.7",
            a => a is 7 or 9 ? new ActionInfo("Attack", 0, 0, 0, string.Empty, 0, Category: 1) : new ActionInfo("Spiral", 3, 30, 0, "gl_fan030_1bf", 1));
        b.SetParty(new[] { new Partier(11, "PLD", 1), new Partier(12, "WAR", 1) });
        b.Alive(0, 1, false);
        b.Alive(0, 2, false);
        var boss = b.Actor(900, "Boss", 5, 1);
        // The many fill their own share and stop there.
        for (var i = 0; i < RecordingBuilder.MaxMany + 50; i++)
        {
            if (i % 2 == 0) b.Hit(1, boss, 7, 100, 100, 0, 100, 105, new[] { 1 });
            else b.Aggro(1, boss, i % 4 == 1 ? 1 : 2);
        }
        // A cast and a marker: the room left is theirs; a status is one of the many.
        b.Cast(2, boss, 8, 3, 100, 100, 0, 0, null, null);
        b.Marker(2, 1, 50);
        b.StatusGained(2, 2, 900, 5, StatusFrom.Enemy, boss, 0, 10f);
        // A castless mechanic's hit is kept past the many's share.
        b.Hit(2.5, boss, 10, 100, 100, 0, 100, 105, new[] { 1 });
        // An auto-attack not kept still says what killed them.
        b.Hit(3, boss, 9, 100, 100, 0, 100, 105, new[] { 2 });
        b.Alive(4, 2, true);
        // The few fill the rest; deaths and raises have room past them.
        for (var i = 0; i < RecordingBuilder.MaxEvents; i++) b.Marker(6, 1, 51);
        b.Alive(8, 2, false);
        b.Alive(9, 1, true);
        var r = b.Finish(10, Outcome.Wipe);
        Assert.Single(r.Events.OfType<CastEvent>(), c => c.Action == 8);
        Assert.Single(r.Events.OfType<MarkerEvent>(), m => m.Icon == 50);
        Assert.Empty(r.Events.OfType<StatusEvent>());
        Assert.DoesNotContain(r.Events.OfType<HitEvent>(), h => h.Action == 9);
        Assert.Single(r.Events.OfType<HitEvent>(), h => h.Action == 10);
        var deaths = r.Events.OfType<DeathEvent>().ToList();
        Assert.Equal(2, deaths.Count);
        Assert.Equal(9u, deaths[0].By);
        Assert.Single(r.Events.OfType<RaiseEvent>());
        Assert.Equal(RecordingBuilder.MaxEvents + 2, r.Events.Count(e => e is not TurnEvent));
        Assert.Equal(RecordingBuilder.MaxMany, r.Events.Count(e => e is HitEvent { Action: 7 } or AggroEvent));
    }

    [Fact]
    public void A_marker_keeps_whom_it_points_at_but_never_itself()
    {
        var b = Builder();
        b.SetParty(new[] { new Partier(11, "PLD", 1, 100_000) });
        var boss = b.Actor(900, "Boss", 5, 1);
        b.Marker(1, boss, 167);
        b.Marker(2, boss, 168, 1);
        b.Marker(3, 1, 62, 1);
        var markers = b.Finish(20, Outcome.Wipe).Events.OfType<MarkerEvent>().ToList();
        Assert.Equal(new[] { 0, 1, 0 }, markers.Select(m => m.Target));
    }

    [Fact]
    public void Turns_and_markers_go_through_the_file()
    {
        var r = RecordingTests.Sample();
        var json = r.ToJson();
        Assert.Contains("\"k\":\"turn\",\"actor\":101,\"rot\":-0.524,\"x\":100,\"z\":100", json);
        Assert.Contains("\"k\":\"marker\",\"on\":101,\"icon\":167}", json);
        var back = Recording.Parse(json);
        Assert.Equal(r.Events.OfType<TurnEvent>().ToList(), back.Events.OfType<TurnEvent>().ToList());
        Assert.Equal(r.Events.OfType<MarkerEvent>().ToList(), back.Events.OfType<MarkerEvent>().ToList());
        Assert.Equal(json, back.ToJson());
        // A marker that points at someone keeps them.
        var withTarget = Recording.Parse(json.Replace("\"icon\":167}", "\"icon\":167,\"tgt\":1}"));
        Assert.Equal(1, withTarget.Events.OfType<MarkerEvent>().Single(m => m.Icon == 167).Target);
    }
}
