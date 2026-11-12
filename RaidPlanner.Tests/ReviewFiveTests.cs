using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// Round five's review of 0.4.0's data side: what the game side really hands over (a stun on an enemy whose hostility was
/// never read), raises credited as the recording credits them, the potions before a pull, the head signs compared as they're
/// kept, and the diagnostics file.
/// </summary>
public class ReviewFiveMeterTests
{
    private const uint Mt = 1, Ot = 2, Whm = 3, Sch = 4, Boss = 900, Add = 901;

    private static readonly CombatKinds Kinds = new()
    {
        Interrupt = id => id is 7538 or 7540 or 7863,
        RaiseAction = id => id is 125 or 173,
        RaiseStatus = id => id == 148,
        Control = id => id == 2,
        Gcd = id => id is 9001 or 125 or 173,
    };

    private static RawEffect E(byte type, ushort value = 0) => new(type, 0, 0, 0, 0, 0, value);

    private static PartyState[] Party(bool whmDead = false) => new[]
    {
        new PartyState(Mt, "Tank A", "PLD", 1, 100_000, 100_000, true, false),
        new PartyState(Ot, "Tank B", "WAR", 1, 100_000, 100_000, true, false),
        new PartyState(Whm, "Healer A", "WHM", 4, whmDead ? 0 : 80_000, 80_000, true, false),
        new PartyState(Sch, "Healer B", "SCH", 4, 80_000, 80_000, true, true),
    };

    /// <summary>One target, as the game side reads it: hostile only where it reads HP (a party member, a heal's target), so never for an enemy.</summary>
    private static EffectBatch Batch(double t, uint caster, uint action, byte type, uint target, params RawEffect[] effects)
    {
        var b = new EffectBatch();
        b.Begin(t, caster, 0, action, type);
        if (target != 0)
        {
            var k = b.Add(target);
            effects.CopyTo(b.EffectsOf(k));
        }
        return b;
    }

    private static (CombatParser P, MeterSession S) Meter()
    {
        var p = new CombatParser(_ => 3);
        p.SetParty(new[] { Mt, Ot, Whm, Sch });
        var s = new MeterSession(id => $"A{id}", id => $"S{id}", id => $"I{id}", Kinds);
        p.Add(s);
        s.Place(1, 1, "Arena");
        s.Frame(0, Party(), true);
        return (p, s);
    }

    [Fact]
    public void A_stun_on_an_enemy_lands_though_the_game_side_never_read_it_as_hostile()
    {
        var (p, s) = Meter();
        p.Action(Batch(1, Mt, 9001, 1, Boss, E(3, 10_000)));
        // Leg Sweep's stun on the add: a status alone (the game side reads no HP for it, so not whether it's hostile).
        p.Action(Batch(2, Ot, 7863, 1, Add, E(14, 2)));
        var events = s.Events(s.Current!.Id);
        Assert.Equal(new[] { MeterEventKind.Stun }, events.Select(e => e.Kind));
        Assert.Equal("Tank B's S2 landed", events[0].Text);
        // A stun status on a party member (an enemy's, read wrongly) is never the party's stun.
        p.Action(Batch(3, Ot, 7863, 1, Mt, E(14, 2)));
        Assert.Single(s.Events(s.Current!.Id));
    }

    [Fact]
    public void A_stun_is_its_status_in_the_actions_message_only_as_the_site_counts_it()
    {
        var (p, s) = Meter();
        p.Action(Batch(1, Mt, 9001, 1, Boss, E(3, 10_000)));
        // Low Blow with no status in its message, one resisted, one missed: no stun landed (the site's rule: the status
        // effect of the action's own message, pullEvents.ts, or the status lists).
        p.Action(Batch(2, Ot, 7540, 1, Add));
        p.Action(Batch(4, Ot, 7540, 1, Boss, E(55, 2)));
        p.Action(Batch(6, Ot, 7540, 1, Add, E(1)));
        p.Action(Batch(8, Mt, 7863, 1, Add, E(14, 2)));
        var events = s.Events(s.Current!.Id);
        Assert.Equal(new[] { MeterEventKind.Resisted, MeterEventKind.Stun }, events.Select(e => e.Kind));
        Assert.Equal("Tank A's S2 landed", events[1].Text);
        Assert.Equal(2u, events[1].Action);
    }

    [Fact]
    public void A_stun_that_came_with_damage_is_one_landed_and_still_no_status_row_of_its_own()
    {
        var (p, s) = Meter();
        var rows = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0.4.0", _ => null);
        rows.SetParty(new[] { new Partier(Mt, "PLD", 1), new Partier(Ot, "WAR", 1), new Partier(Whm, "WHM", 4), new Partier(Sch, "SCH", 4) });
        p.Add(new RecordingSink(() => rows, () => 0));
        p.Action(Batch(1, Mt, 9001, 1, Boss, E(3, 10_000)));
        // A Shield Bash: damage and the stun in one message.
        p.Action(Batch(2, Ot, 16, 1, Add, E(3, 5_000), E(14, 2)));
        var events = s.Events(s.Current!.Id);
        Assert.Equal(new[] { MeterEventKind.Stun }, events.Select(e => e.Kind));
        Assert.Equal("Tank B's S2 landed", events[0].Text);
        // The recording keeps its damage, not a status row (as before): the site sees the stun in combat.st.
        var fx = rows.Finish(20, Outcome.Ended).Combat!.Fx;
        Assert.DoesNotContain(fx, f => f.Action == 16 && (f.Kind & 7) == (int)FxKind.Status);
        Assert.Contains(fx, f => f.Action == 16 && (f.Kind & 7) == (int)FxKind.Damage);
    }

    [Fact]
    public void A_raise_is_credited_to_its_status_first_as_the_recording_credits_it()
    {
        var (p, s) = Meter();
        p.Action(Batch(1, Mt, 9001, 1, Boss, E(3, 10_000)));
        s.Frame(5, Party(whmDead: true), true);
        // The SCH's Resurrection lands (its Raise status on the WHM); the MT's Raise comes after, wasted: the WHM gets up on the SCH's.
        p.Action(Batch(8, Sch, 173, 1, Whm, E(14, 148)));
        p.Action(Batch(9, Mt, 125, 1, Whm));
        s.Frame(10, Party(), true);
        var e = s.Current!;
        Assert.Equal(1, e.Rows.Single(r => r.Slot == "H2").RaisesGiven);
        Assert.Equal(0, e.Rows.Single(r => r.Slot == "MT").RaisesGiven);
        Assert.Equal("H2", e.Deaths.Single().RaisedBy);
        Assert.Equal("Healer A was raised by Healer B", s.Events(e.Id).Single(x => x.Kind == MeterEventKind.Raise).Text);
    }

    [Fact]
    public void A_potion_in_the_30_seconds_before_an_encounter_is_its_own_at_its_own_time()
    {
        var (p, s) = Meter();
        // Out of combat: one 40 s before (no pull's), the tincture 2 s before; then the pull.
        p.Action(Batch(60, Sch, 1_039_727, 2, Sch));
        p.Action(Batch(98, Mt, 1_039_726, 2, Mt));
        p.Action(Batch(100, Mt, 9001, 1, Boss, E(3, 10_000)));
        p.Action(Batch(110, Sch, 1_039_727, 2, Sch));
        var e = s.Current!;
        Assert.Equal(1, e.Rows.Single(r => r.Slot == "MT").Potions);
        Assert.Equal(1, e.Rows.Single(r => r.Slot == "H2").Potions);
        var pots = s.Events(e.Id).Where(x => x.Kind == MeterEventKind.Potion).ToList();
        Assert.Equal(new[] { -2.0, 10.0 }, pots.Select(x => x.At));
        Assert.Equal("Tank A used I1039726 before the pull", pots[0].Text);
        Assert.Equal("Healer B used I1039727", pots[1].Text);
    }

    [Fact]
    public void A_long_pulls_feed_keeps_its_newest_events()
    {
        var (p, s) = Meter();
        p.Action(Batch(1, Mt, 9001, 1, Boss, E(3, 10_000)));
        // 250 potions, one a second (each an event): the newest 200 are kept, oldest first, the feed still moving at the end.
        for (var i = 0; i < 250; i++)
        {
            p.Action(Batch(2 + i, Sch, 1_039_727, 2, Sch));
            if (i == 99) Assert.Equal(100, s.Events(s.Current!.Id).Count);
        }
        var events = s.Events(s.Current!.Id);
        Assert.Equal(MeterSession.MostEvents, events.Count);
        Assert.Equal(1 + 50.0, events[0].At);
        Assert.Equal(1 + 249.0, events[^1].At);
        // The counts are every one's (a frame later: the live numbers are worked out again).
        s.Frame(260, Party(), true);
        Assert.Equal(250, s.Current!.Rows.Single(r => r.Slot == "H2").Potions);
    }

    [Fact]
    public void A_potion_in_another_zone_is_no_pulls_here()
    {
        var (p, s) = Meter();
        p.Action(Batch(98, Mt, 1_039_726, 2, Mt));
        s.Place(2, 2, "Elsewhere");
        p.Action(Batch(100, Mt, 9001, 1, Boss, E(3, 10_000)));
        Assert.Equal(0, s.Current!.Rows.Single(r => r.Slot == "MT").Potions);
    }

    [Fact]
    public void An_enemy_cast_with_no_length_the_game_gave_never_breaks_the_events()
    {
        var (p, s) = Meter();
        p.Action(Batch(1, Mt, 9001, 1, Boss, E(3, 10_000)));
        s.EnemyCast(2, Boss, 800, float.NaN, true);
        s.Frame(3, Party(), true);
        // Taken as instant: it went off (said once), never a NaN end that never comes.
        Assert.Equal(new[] { MeterEventKind.CastWentOff }, s.Events(s.Current!.Id).Select(x => x.Kind));
    }
}

public class ReviewFiveRecordingTests
{
    [Fact]
    public void Signs_on_no_one_or_past_the_seventeen_never_make_an_event_each_second()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0.4.0", _ => null);
        var boss = b.Actor(900, "Boss", 5, 1);
        for (var t = 0; t < 5; t++) b.Signs(t, new Dictionary<int, int> { [0] = boss, [3] = 0, [40] = boss });
        b.Signs(5, new Dictionary<int, int> { [0] = boss, [1] = boss });
        b.Signs(6, new Dictionary<int, int>());
        var signs = b.Finish(20, Outcome.Ended).Events.OfType<SignsEvent>().ToList();
        Assert.Equal(new[] { 0.0, 5.0, 6.0 }, signs.Select(x => x.T));
        Assert.Equal(new[] { 0 }, signs[0].Marks.Keys);
        Assert.Empty(signs[2].Marks);
    }
}

public class ReviewFiveSummaryTests
{
    private static RecordingBuilder Builder()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0.4.0", id => new ActionInfo($"A{id}", 1, 0, 0, "", 5));
        b.SetParty(new[] { new Partier(1, "PLD", 1), new Partier(2, "WAR", 1), new Partier(3, "WHM", 4), new Partier(4, "BLM", 3) });
        return b;
    }

    [Fact]
    public void A_boss_looking_back_after_a_buster_on_the_other_tank_is_no_swap_and_spans_end_where_the_pull_was_fought()
    {
        var b = Builder();
        int Id(ulong key) => b.PartyId(key)!.Value;
        var boss = b.Actor(900, "Boss", 5, 1);
        b.SeenTargetable(boss, 1_000_000);
        foreach (var k in new ulong[] { 1, 2, 3, 4 }) b.Alive(0, Id(k), false);
        b.Aggro(0, boss, Id(1));
        // A buster on the OT (10–15): the boss turns to them, and back to the MT 1.2 s after it went off.
        b.Cast(10, boss, 600, 5, 100, 100, 0, Id(2), null, null);
        b.Aggro(10, boss, Id(2));
        b.Aggro(16.2, boss, Id(1));
        // Half a second before the wipe's last death (30) it turns to the OT: the recording runs on to 45, the fight doesn't.
        b.Aggro(29.5, boss, Id(2));
        foreach (var k in new ulong[] { 1, 2, 3, 4 }) b.Alive(30, Id(k), true);
        var r = b.Finish(45, Outcome.Wipe);
        Assert.Equal(30, PullStats.FoughtLength(r));
        Assert.Equal(0, PullStats.TankSwaps(r));
    }

    [Fact]
    public void Time_dead_stops_at_the_fights_end_though_the_raise_came_after()
    {
        var b = Builder();
        var whm = b.PartyId(3)!.Value;
        b.Alive(0, whm, false);
        b.Alive(10, whm, true);
        b.Alive(25, whm, false);
        var r = b.Finish(40, Outcome.Ended);
        Assert.Equal(10, PullStats.Extras(r, fightEnd: 20).Of(whm).Dead, 6);
        // Raised within it: to the raise.
        Assert.Equal(15, PullStats.Extras(r, fightEnd: 30).Of(whm).Dead, 6);
    }
}

public sealed class ReviewFiveDiagnosticsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rp-diag-" + Guid.NewGuid().ToString("N"));
    private DateTime now = new(2026, 11, 12, 21, 3, 4, 123);

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

    private DiagnosticsLog Log(long cap = DiagnosticsLog.DefaultFileCap) =>
        new(folder, () => now) { FileCap = cap, Context = () => (1122u, "Alignment Arena", 3, 12.5) };

    [Fact]
    public void Off_nothing_is_written()
    {
        var log = Log();
        log.Write("hate", $"target {1:X8}");
        log.Flush();
        Assert.True(log.WaitForWrites(TimeSpan.FromSeconds(5)));
        Assert.False(Directory.Exists(folder) && Directory.GetFiles(folder).Length > 0);
    }

    [Fact]
    public void On_each_reading_is_a_line_with_the_time_zone_and_pull_written_at_a_flush_and_when_turned_off()
    {
        var log = Log();
        log.On = true;
        log.Write("lb-gauge", $"units {12_000} bars {3} barUnits {10_000} at {1.5:0.00}");
        Assert.False(File.Exists(log.FileFor(now)));
        log.Flush();
        log.WaitForWrites(TimeSpan.FromSeconds(5));
        var lines = File.ReadAllLines(log.FileFor(now));
        Assert.Equal("21:03:04.123 | zone 1122 Alignment Arena | pull 3 @ 12.50 | diagnostics | on", lines[0]);
        // Invariant numbers whatever the computer's language.
        Assert.Equal("21:03:04.123 | zone 1122 Alignment Arena | pull 3 @ 12.50 | lb-gauge | units 12000 bars 3 barUnits 10000 at 1.50", lines[1]);
        log.Write("hate", $"x");
        log.On = false;
        log.Write("hate", $"not written");
        log.WaitForWrites(TimeSpan.FromSeconds(5));
        lines = File.ReadAllLines(log.FileFor(now));
        Assert.Equal(new[] { "hate", "diagnostics" }, lines.Skip(2).Select(l => l.Split(" | ")[3]));
        Assert.EndsWith("off", lines[^1]);
        Assert.Equal("diagnostics-2026-11-12.log", Path.GetFileName(log.FileFor(now)));
    }

    [Fact]
    public void A_full_file_is_moved_aside_and_only_the_newest_are_kept()
    {
        var log = Log(cap: 400);
        log.On = true;
        for (var i = 0; i < 6; i++)
        {
            log.Write("x", $"{new string('a', 150)} {i}");
            log.Flush();
        }
        log.WaitForWrites(TimeSpan.FromSeconds(5));
        var path = log.FileFor(now);
        Assert.True(new FileInfo(path).Length <= 400);
        Assert.True(File.Exists(Path.Combine(folder, "diagnostics-2026-11-12.1.log")));
        // Other days' files: the newest six stay.
        for (var d = 1; d <= 8; d++)
        {
            now = now.AddDays(1);
            log.Write("x", $"day {d}");
            log.Flush();
            log.WaitForWrites(TimeSpan.FromSeconds(5));
        }
        log.Write("x", $"last");
        log.Flush();
        log.WaitForWrites(TimeSpan.FromSeconds(5));
        Assert.Equal(DiagnosticsLog.KeepFiles, Directory.GetFiles(folder, "diagnostics-*.log").Length);
    }

    [Fact]
    public void Nothing_ever_throws_a_folder_that_cant_be_written_or_a_context_that_fails()
    {
        // A file where the folder should be.
        Directory.CreateDirectory(folder);
        var blocked = Path.Combine(folder, "blocked");
        File.WriteAllText(blocked, "x");
        var log = new DiagnosticsLog(blocked) { Context = () => throw new InvalidOperationException() };
        log.On = true;
        log.Write("x", $"y");
        log.Context = null;
        log.Write("x", $"z");
        log.Flush();
        Assert.True(log.WaitForWrites(TimeSpan.FromSeconds(5)));
        log.Dispose();
        Assert.False(log.On);
    }
}
