using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>What the round-four review found in the meter, the recording and the summary, each kept fixed.</summary>
public class ReviewFixTests
{
    private const uint Tank = 1, Healer = 2, Dps = 3, Boss = 900;

    private static PartyState[] Party(long healer = 100_000, long healerMax = 97_210) => new[]
    {
        new PartyState(Tank, "a", "PLD", 1, 100_000, 100_000, true, false),
        new PartyState(Healer, "b", "WHM", 4, healer, healerMax, true, false),
        new PartyState(Dps, "c", "BLM", 3, 100_000, 100_000, true, true),
    };

    private static MeterSession New(out CombatParser parser)
    {
        var s = new MeterSession(a => $"A{a}", st => $"S{st}") { EndAfter = 4 };
        parser = CombatParserTests.Parser(out _);
        parser.Add(s);
        s.Place(1, 77, "Test Arena");
        return s;
    }

    private static void Taken(CombatParser p, double t, uint on, long amount, long hp, uint action = 600, byte type = 3) =>
        p.Action(CombatParserTests.Batch(t, Boss, action, 0,
            (on, new[] { CombatParserTests.E(type, value: (ushort)(amount & 0xFFFF), p3: (byte)(amount >> 16), p4: (byte)(amount > 0xFFFF ? 0x40 : 0)) }, hp, 100_000, false)));

    [Fact]
    public void A_death_carries_the_max_hp_its_recap_is_drawn_against()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        Taken(p, 0, Healer, 40_000, 97_210);
        Taken(p, 1, Healer, 70_000, 57_210, 700);
        s.Frame(1.6, Party(healer: 0), true);
        var d = Assert.Single(s.Current!.Deaths);
        Assert.Equal(97_210, d.MaxHp);
    }

    [Fact]
    public void A_bleed_ticking_between_a_lethal_hit_and_the_death_isnt_the_killing_blow()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        // The lethal hit is announced with 50,000 HP left; a bleed ticks before its damage is taken off.
        Taken(p, 0, Healer, 47_210, 97_210);
        Taken(p, 5, Healer, 60_000, 50_000, 700);
        p.Tick(5.3, Healer, 2941, false, 2_000, Boss, 0, 50_000, 97_210);
        s.Frame(5.8, Party(healer: 0), true);
        var d = Assert.Single(s.Current!.Deaths);
        Assert.Equal("A700", d.KilledBy);
        Assert.Equal(60_000, d.Amount);
        Assert.Equal(10_000, d.Overkill);
        // Nothing that could kill on its own: the last hit, as before.
        var s2 = New(out var p2);
        s2.Frame(0, Party(), true);
        Taken(p2, 0, Healer, 30_000, 97_210);
        Taken(p2, 1, Healer, 30_000, 67_210, 701);
        s2.Frame(1.6, Party(healer: 0), true);
        Assert.Equal("A701", Assert.Single(s2.Current!.Deaths).KilledBy);
        // An instant death is the killing blow whatever came after it.
        var s3 = New(out var p3);
        s3.Frame(0, Party(), true);
        Taken(p3, 0, Healer, 1_000, 97_210);
        Taken(p3, 1, Healer, 0, 96_210, 800, Effects.InstantDeath);
        p3.Tick(1.2, Healer, 2941, false, 2_000, Boss, 0, 96_210, 97_210);
        s3.Frame(1.6, Party(healer: 0), true);
        Assert.Equal("A800", Assert.Single(s3.Current!.Deaths).KilledBy);
    }

    [Fact]
    public void A_breakdown_read_during_a_fight_is_read_again_after_its_end()
    {
        var s = New(out var p);
        s.Frame(0, Party(), true);
        p.Action(CombatParserTests.Batch(0, Dps, 100, 0, (Boss, new[] { CombatParserTests.E(3, value: 1000) }, -1, -1, true)));
        var id = s.Current!.Id;
        var member = s.Current!.Rows.Single(r => r.Slot == "R1").PartyId;
        Assert.Equal(1000, s.Abilities(id, member, MeterTab.Damage).Sum(a => a.Amount));
        // A last hit a moment before the end (within the quarter second the breakdown is kept).
        s.Frame(0.1, Party(), true);
        p.Action(CombatParserTests.Batch(0.1, Dps, 100, 0, (Boss, new[] { CombatParserTests.E(3, value: 500) }, -1, -1, true)));
        s.Said(Outcome.Clear);
        Assert.False(s.InEncounter);
        var after = s.Abilities(id, member, MeterTab.Damage);
        Assert.Equal(1500, after.Sum(a => a.Amount));
        // Over, it's worked out once and kept.
        Assert.Same(after, s.Abilities(id, member, MeterTab.Damage));
    }

    [Fact]
    public void A_deaths_cause_is_the_lethal_hit_not_a_bleed_ticking_before_the_hp_goes()
    {
        var b = new RecordingBuilder(new DutyInfo("X", 1, 1), 1, DateTimeOffset.Now, true, "0", _ => null, id => new StatusInfo($"S{id}", 1));
        b.SetParty(new[] { new Partier(11, "PLD", 1), new Partier(12, "WAR", 1), new Partier(13, "WHM", 4) });
        foreach (var who in new[] { 1, 2, 3 }) b.Alive(0, who, false);
        // 1: a hit as big as their HP, then a bleed's small tick before the HP reaches 0.
        b.Hit(9, 101, 600, 0, 0, 0, 0, 0, new[] { 1 }, new long[] { 60_000 }, new[] { 0 }, new long[] { 50_000 });
        b.CombatTick(9.3, 1, 101, 2941, 2_000, false, 50_000);
        // 2: a lethal tick after a small hit is the tick.
        b.Hit(9, 101, 601, 0, 0, 0, 0, 0, new[] { 2 }, new long[] { 5_000 }, new[] { 0 }, new long[] { 50_000 });
        b.CombatTick(9.3, 2, 101, 2941, 6_000, false, 4_000);
        // 3: a lethal-looking hit long before (a shield took it) doesn't hide the hit that killed them.
        b.Hit(5, 101, 602, 0, 0, 0, 0, 0, new[] { 3 }, new long[] { 90_000 }, new[] { 0 }, new long[] { 80_000 });
        b.Hit(9, 101, 603, 0, 0, 0, 0, 0, new[] { 3 }, new long[] { 30_000 }, new[] { 0 }, new long[] { 60_000 });
        foreach (var who in new[] { 1, 2, 3 }) b.Alive(9.8, who, true);
        var deaths = b.Finish(20, Outcome.Wipe).Events.OfType<DeathEvent>().ToDictionary(d => d.Who);
        Assert.Equal((600u, false), (deaths[1].By, deaths[1].ByStatus));
        Assert.Equal((2941u, true), (deaths[2].By, deaths[2].ByStatus));
        Assert.Equal(603u, deaths[3].By);
    }

    [Fact]
    public void The_recaps_bars_run_to_the_max_hp_and_the_killing_line_is_the_blow_named()
    {
        var recap = new List<RecapLine>
        {
            new(161.0, "Exaflare", -38_620, 97_210, ""),
            new(165.2, "Exaflare", -60_000, 50_000, ""),
            new(165.5, "Bleeding", -2_000, 50_000, "Over time"),
        };
        var d = new DeathEntry(8, "H2", "Odette", 166, "Exaflare", 60_000, 10_000, false, recap, 120_000);
        Assert.Equal(120_000, MeterView.RecapMax(d));
        Assert.Equal(97_210, MeterView.RecapMax(d with { MaxHp = 0 }));
        // The blow is the second line, though a tick came after it.
        Assert.Equal(1, MeterView.KillingLine(d));
        Assert.Equal(2, MeterView.KillingLine(d with { KilledBy = "Unknown" }));
        Assert.Equal(-1, MeterView.KillingLine(d with { Recap = Array.Empty<RecapLine>() }));
    }

    [Fact]
    public void The_send_body_carries_the_recording_as_written()
    {
        var json = RecordingTests.Sample().ToJson();
        var body = SiteLink.Body("rpp_" + new string('a', 43), "Pull 3 · エデン", json, "0123456789abcdef0123456789abcdef");
        var o = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
        Assert.Equal("Pull 3 · エデン", o["p_name"]!.GetValue<string>());
        Assert.Equal("0123456789abcdef0123456789abcdef", o["p_client_id"]!.GetValue<string>());
        Assert.Equal(json, o["p_recording"]!.ToJsonString());
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SiteLink.Body("t", "n", "{not json"));
    }

    [Fact]
    public void The_summarys_phases_are_the_builders_own_count_when_given()
    {
        var r = RecordingTests.SampleBuilder(true, out var phase);
        // As counted while it was recorded (a pull past the events' cap can't count them from its events).
        var s = PullSummary.Build(r, phase, phaseStarts: new[] { 0.0, 31.6 });
        Assert.Equal(new[] { 0, 32 }, s["phases"]!.AsArray().Select(x => x!.GetValue<int>()));
        Assert.Equal(new[] { 0, 50 }, PullSummary.Build(r, phase)["phases"]!.AsArray().Select(x => x!.GetValue<int>()));
    }
}
