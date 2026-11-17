using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// 0.7.7's lighter recording: a nameless enemy's name read at most once a second, the statuses of someone who left ended, and a
/// frame keeping the recorder's dictionaries when nothing in them is left out.
/// </summary>
public class LighterRecorderTests
{
    private static RecordingBuilder Builder(bool party = true) =>
        new(new DutyInfo("Test", 1, 1), 1, DateTimeOffset.UnixEpoch, party, "0.7.7", _ => new ActionInfo("Hit", 0, 0, 0, string.Empty, 0));

    [Fact]
    public void A_nameless_enemys_name_is_asked_for_at_most_once_a_second()
    {
        var b = Builder();
        Assert.True(b.NeedsName(900, 0));
        b.Actor(900, string.Empty, 1, 1);
        Assert.True(b.NeedsName(900, 0.0));
        Assert.False(b.NeedsName(900, 0.5));
        Assert.False(b.NeedsName(900, 0.99));
        Assert.True(b.NeedsName(900, 1.0));
        Assert.False(b.NeedsName(900, 1.5));
        // Once it has one, never again.
        b.Actor(900, "Omega", 1, 1);
        Assert.False(b.NeedsName(900, 9));
        // The other overload, as before.
        Assert.False(b.NeedsName(900));
    }

    [Fact]
    public void Statuses_of_someone_not_read_for_five_seconds_end_when_they_were_last_read()
    {
        var b = Builder();
        var boss = b.Actor(900, "Boss", 5, 1);
        var add = b.Actor(901, "Add", 2, 2);
        for (var t = 0.0; t <= 12; t += 0.5)
        {
            b.BeginStatuses();
            b.StatusesOf(boss);
            if (!b.StatusKnown(t, boss, 50, 0, 0, 60f)) b.StatusGained(t, boss, 50, 0, StatusFrom.Enemy, boss, 0, 60f);
            // The add, with the party's DoT on it, leaves at 3 s.
            if (t <= 3)
            {
                b.StatusesOf(add);
                if (!b.StatusKnown(t, add, 51, 0, 0, 60f)) b.StatusGained(t, add, 51, 0, StatusFrom.Enemy, add, 0, 60f);
                if (!b.StatusKnown(t, add, 1871, 13, 0, 30f)) b.StatusGained(t, add, 1871, 13, StatusFrom.Party, 3, 0, 30f, 9003);
            }
            b.EndStatuses(t);
        }
        var r = b.Finish(12, Outcome.Wipe);
        var events = r.Events.OfType<StatusEvent>().ToList();
        // The boss, read every frame, keeps its status.
        Assert.Single(events, e => e.On == boss);
        // The add's: gone as of 3 s (its last read), found at 8.
        var gone = Assert.Single(events, e => e.On == add && !e.Gain);
        Assert.Equal(3, gone.T);
        var rows = r.Combat!.Statuses.Where(s => s.On == add).ToList();
        Assert.Equal(2, rows.Count);
        Assert.False(rows[1].Gain);
        Assert.Equal(3, rows[1].T);
    }

    [Fact]
    public void A_frame_keeps_the_dictionaries_it_is_given_when_nothing_is_left_out()
    {
        var b = Builder();
        var boss = b.Actor(900, "Boss", 5, 1);
        var at = new Dictionary<int, (float, float, float)> { [1] = (1, 2, 0), [boss] = (100, 100, 0) };
        var hp = new Dictionary<int, long> { [1] = 5000, [boss] = 900_000 };
        b.Positions(0, at, hp);
        // Without the party's places: a copy with the enemies only, as before.
        var c = Builder(party: false);
        var boss2 = c.Actor(900, "Boss", 5, 1);
        c.Positions(0, new Dictionary<int, (float, float, float)> { [1] = (1, 2, 0), [boss2] = (100, 100, 0) },
            new Dictionary<int, long> { [1] = 5000, [boss2] = -3 });
        b.SeenTargetable(boss, 900_000);
        c.SeenTargetable(boss2, 900_000);
        var f = Assert.Single(b.Finish(1, Outcome.Wipe).Frames);
        Assert.Same(at, f.At);
        Assert.Same(hp, f.Hp);
        var g = Assert.Single(c.Finish(1, Outcome.Wipe).Frames);
        Assert.Equal(new[] { boss2 }, g.At.Keys);
        Assert.Equal(0, g.Hp![boss2]);
    }
}
