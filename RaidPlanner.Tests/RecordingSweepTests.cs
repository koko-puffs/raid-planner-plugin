using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>0.9.0's sweep of the recording: what the plugin works out from a pull agrees with the site.</summary>
public class RecordingSweepTests
{
    /// <summary>
    /// A boss the party fights, and a mob far off it never touches (someone else's, in a field zone) going out of sight and
    /// back: the site's <c>phaseStarts</c> leaves the mob out (<c>fightOf(rec).strays</c>), so its coming back isn't a phase.
    /// </summary>
    private static Recording FarMob()
    {
        var r = new Recording { PartyPositions = true, Duration = 50 };
        r.Party.Add(new PartyMember(1, "MT", "PLD", "tank"));
        r.Actors.Add(new Actor(101, "Boss", ActorKind.Boss, 5, 1, 1_000_000));
        r.Actors.Add(new Actor(102, "Far mob", ActorKind.Add, 1, 2, 10_000));
        r.Events.Add(new TargetEvent(0, 101, true));
        r.Events.Add(new TargetEvent(1, 102, true));
        r.Events.Add(new HitEvent(2, 101, 9, 5, 0, 0, 0, 0, new[] { 1 }));
        r.Events.Add(new TargetEvent(10, 101, false));
        r.Events.Add(new TargetEvent(11, 102, false));
        r.Events.Add(new TargetEvent(25, 102, true));
        r.Events.Add(new TargetEvent(40, 101, true));
        for (var t = 0; t <= 40; t++)
        {
            var at = new Dictionary<int, (float, float, float)> { [1] = (0, 0, 0), [101] = (5, 0, 0) };
            if (t is < 11 or >= 25) at[102] = (200, 0, 0);
            r.Frames.Add(new Frame(t, at));
        }
        return r;
    }

    [Fact]
    public void A_far_mob_coming_back_is_not_a_phase()
    {
        var r = FarMob();
        Assert.Equal(new[] { 102 }, PullStats.Strays(r).ToArray());
        Assert.Equal(new[] { 0.0, 40 }, PullStats.PhaseStarts(r));
    }

    [Fact]
    public void A_mob_near_the_fight_still_counts()
    {
        var r = FarMob();
        for (var i = 0; i < r.Frames.Count; i++)
        {
            var f = r.Frames[i];
            if (!f.At.ContainsKey(102)) continue;
            var at = f.At.ToDictionary(kv => kv.Key, kv => kv.Value);
            at[102] = (20, 0, 0);
            r.Frames[i] = f with { At = at };
        }
        Assert.Empty(PullStats.Strays(r));
        Assert.Equal(new[] { 0.0, 25 }, PullStats.PhaseStarts(r));
    }
}
