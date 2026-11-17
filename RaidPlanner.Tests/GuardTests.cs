using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// The meter's stop for nonsense counted by stretch (<see cref="AnomalyGuard"/>), and the diagnostics file's room check
/// (<see cref="DiagnosticsLog.Room"/>) the game side asks before it makes a line.
/// </summary>
public class AnomalyGuardTests
{
    private const uint Dps = 3, Boss = 900;

    /// <summary>A hit too big to be real from the party's dps (the parser drops it and counts it).</summary>
    private static void Nonsense(CombatParser p, double t) =>
        p.Action(CombatParserTests.Batch(t, Dps, 100, 0, (Boss, new[] { CombatParserTests.E(3, p3: 0x60, p4: 0x40) }, -1, -1, true)));

    [Fact]
    public void Three_in_one_stretch_stop_the_meter_a_stray_one_each_pull_never_does()
    {
        var p = CombatParserTests.Parser(out _);
        var g = new AnomalyGuard();
        g.Restart(p.Anomalies);
        // One a pull, for ten pulls (each pull's end a new stretch): never stopped, though the parser's count is ten.
        for (var pull = 0; pull < 10; pull++)
        {
            Nonsense(p, pull);
            Assert.False(g.Check(p.Anomalies));
            g.Restart(p.Anomalies);
        }
        Assert.Equal(10, p.Anomalies);
        Assert.Equal(0, g.Stops);
        // Two in a stretch: still on; the third stops it, and it stays stopped until it's lifted.
        Nonsense(p, 20);
        Nonsense(p, 21);
        Assert.False(g.Check(p.Anomalies));
        Nonsense(p, 22);
        Assert.True(g.Check(p.Anomalies));
        Assert.True(g.Stopped);
        Assert.False(g.ForGood);
        Assert.True(g.Check(p.Anomalies));
        Assert.Equal(1, g.Stops);
    }

    [Fact]
    public void Nonsense_while_the_meter_wasnt_listening_doesnt_count()
    {
        var p = CombatParserTests.Parser(out _);
        // The recorder alone heard these (the meter off): the meter starting to listen begins a stretch.
        for (var i = 0; i < 5; i++) Nonsense(p, i);
        var g = new AnomalyGuard();
        g.Restart(p.Anomalies);
        Assert.False(g.Check(p.Anomalies));
    }

    [Fact]
    public void A_stop_lasts_until_the_next_zone_and_the_third_is_for_good()
    {
        var g = new AnomalyGuard();
        var count = 0;
        g.Restart(count);
        for (var stop = 1; stop <= AnomalyGuard.MostStops; stop++)
        {
            count += AnomalyGuard.PerStretch;
            Assert.True(g.Check(count));
            Assert.Equal(stop, g.Stops);
            // An encounter ending doesn't lift it.
            g.Restart(count);
            Assert.True(g.Check(count));
            var lifted = g.NewZone(count);
            if (stop < AnomalyGuard.MostStops)
            {
                Assert.True(lifted);
                Assert.False(g.Stopped);
                Assert.False(g.Check(count));
            }
            else
            {
                // A layout that's really moved: stopped until the plugin is reloaded.
                Assert.False(lifted);
                Assert.True(g.ForGood);
                Assert.True(g.Check(count));
            }
        }
        // A new zone with nothing stopped lifts nothing, and begins a stretch.
        var fresh = new AnomalyGuard();
        fresh.Restart(0);
        Assert.False(fresh.Check(2));
        Assert.False(fresh.NewZone(2));
        Assert.False(fresh.Check(4));
    }

    [Fact]
    public void A_count_that_went_back_is_counted_from_where_it_is()
    {
        var g = new AnomalyGuard();
        g.Restart(10);
        Assert.False(g.Check(0));
        Assert.False(g.Check(2));
        Assert.True(g.Check(3));
    }
}

public sealed class DiagnosticsRoomTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rp-room-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime now = new(2026, 11, 12, 21, 3, 4, 123);

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, true);
        }
        catch (Exception)
        {
            // Left behind in the temp folder.
        }
    }

    [Fact]
    public void Off_there_is_no_room_on_there_is()
    {
        var log = new DiagnosticsLog(folder, () => now);
        Assert.False(log.Room());
        log.On = true;
        Assert.True(log.Room());
        log.On = false;
        Assert.False(log.Room());
        Assert.True(log.WaitForWrites(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void While_the_disk_lags_lines_past_the_cap_are_neither_made_nor_kept_and_the_file_says_how_many()
    {
        using var hold = new ManualResetEventSlim(false);
        var log = new DiagnosticsLog(folder, () => now) { HoldWrites = hold };
        log.On = true;
        var line = new string('a', 200);
        var written = 0;
        // The worker can't write: what's handed to it stays held, until there's no room.
        while (log.Room())
        {
            log.Write("x", $"{line} {written}");
            written++;
            Assert.True(written < 10_000, "never full");
        }
        Assert.True(written > DiagnosticsLog.MostBuffered / 300);
        // Asked again while full: no room (each asked counted as lost), and a line written anyway is dropped too.
        Assert.False(log.Room());
        log.Write("x", $"dropped");
        // The disk catches up: room again, and every line gathered is written, with how many were lost.
        hold.Set();
        Assert.True(log.WaitForWrites(TimeSpan.FromSeconds(5)));
        Assert.True(log.Room());
        log.On = false;
        Assert.True(log.WaitForWrites(TimeSpan.FromSeconds(5)));
        var lines = File.ReadAllLines(log.FileFor(now));
        Assert.Equal(written, lines.Count(l => l.Contains(" | x | ")));
        Assert.DoesNotContain(lines, l => l.EndsWith("dropped"));
        Assert.Contains(lines, l => l.EndsWith("| diagnostics | 3 lines lost (the file couldn't be written fast enough)"));
    }
}
