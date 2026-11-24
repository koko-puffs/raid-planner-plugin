using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>0.9.0's ready check card and death recap: the mitigations' catalogue, a death's kind and mitigation in the meter, the recap's card and queue, the ready check's card and when it shows.</summary>
public class PrepTests
{
    // ---------------------------------------------------------------------------------------------
    // The mitigations' catalogue.

    [Fact]
    public void The_mitigations_fit_a_mask_and_each_key_is_its_own()
    {
        Assert.True(Mitigations.All.Count <= 64);
        Assert.Equal(Mitigations.All.Count, Mitigations.All.Select(m => m.Key).Distinct().Count());
        Assert.All(Mitigations.All, m => Assert.NotEmpty(m.Names));
        // A name is one mitigation's only.
        var names = Mitigations.Names().ToList();
        Assert.Equal(names.Count, names.Select(n => n.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Mitigations.Mask("Reprisal", "Addle", "Feint", "Dismantle"), Mitigations.OnEnemy);
    }

    [Fact]
    public void A_tank_without_their_big_cooldowns_is_told_which_and_a_barrier_isnt_mitigation()
    {
        Assert.Equal(new[] { "Rampart", "Guardian", "Hallowed Ground" }, Mitigations.MissingFor("PLD", 0));
        Assert.Equal(new[] { "Guardian", "Hallowed Ground" }, Mitigations.MissingFor("PLD", Mitigations.Mask("Rampart", "Reprisal")));
        Assert.Equal(new[] { "Rampart", "Great Nebula", "Superbolide" }, Mitigations.MissingFor("GNB", Mitigations.Mask("Heart of Corundum")));
        Assert.Empty(Mitigations.MissingFor("WHM", 0));
        Assert.False(Mitigations.AnyMitigation(Mitigations.Mask("Divine Benison", "Galvanize")));
        Assert.True(Mitigations.AnyMitigation(Mitigations.Mask("Divine Benison", "Kerachole")));
        Assert.Equal(new[] { "Rampart", "Divine Benison" }, Mitigations.In(Mitigations.Mask("Divine Benison", "Rampart")).Select(m => m.Key));
        Assert.Equal(0UL, Mitigations.Bit(-1));
    }

    [Fact]
    public void A_killing_blow_is_a_buster_or_a_raid_wide_by_how_many_it_hit()
    {
        Assert.Equal(DeathHit.TankBuster, DeathHits.Kind(1, 1, 8, true, 182_400, 152_000));
        Assert.Equal(DeathHit.TankBuster, DeathHits.Kind(2, 2, 8, true, 90_000, 150_000));
        // Not a tank's, or too small a share of their HP, or someone else hit too.
        Assert.Equal(DeathHit.Unknown, DeathHits.Kind(1, 0, 8, false, 182_400, 120_000));
        Assert.Equal(DeathHit.Unknown, DeathHits.Kind(1, 1, 8, true, 30_000, 150_000));
        Assert.Equal(DeathHit.Unknown, DeathHits.Kind(2, 1, 8, true, 182_400, 150_000));
        Assert.Equal(DeathHit.RaidWide, DeathHits.Kind(8, 2, 8, false, 100_000, 120_000));
        Assert.Equal(DeathHit.RaidWide, DeathHits.Kind(4, 0, 8, false, 100_000, 120_000));
        Assert.Equal(DeathHit.Unknown, DeathHits.Kind(3, 0, 8, false, 100_000, 120_000));
        Assert.Equal(DeathHit.RaidWide, DeathHits.Kind(3, 1, 4, false, 100_000, 120_000));
        Assert.Equal(DeathHit.Unknown, DeathHits.Kind(2, 0, 2, false, 100_000, 120_000));
    }

    // ---------------------------------------------------------------------------------------------
    // A death in the meter: its kind, what was up, its phase.

    private const uint Mt = 1, Ot = 2, Whm = 3, Blm = 4, Boss = 900;

    private static PartyState[] Party(long mtHp = 100_000, long blmHp = 70_000, ulong mtMits = 0, bool read = true) => new[]
    {
        new PartyState(Mt, "Tank A", "PLD", 1, mtHp, 100_000, true, false, 0, mtMits, read),
        new PartyState(Ot, "Tank B", "WAR", 1, 100_000, 100_000, true, false, 0, 0, read),
        new PartyState(Whm, "Healer", "WHM", 4, 80_000, 80_000, true, false, 0, 0, read),
        new PartyState(Blm, "Caster", "BLM", 3, blmHp, 70_000, true, true, 0, 0, read),
    };

    private static MeterSession Session()
    {
        var s = new MeterSession(id => $"A{id}", id => $"S{id}");
        s.Place(1, 1, "Arena");
        s.Frame(0, Party(), true);
        s.Taken(new TakenHit(1, Boss, 7, Mt, 5_000, 0, 100_000, 100_000));
        s.Frame(1, Party(), true);
        return s;
    }

    [Fact]
    public void A_tank_killed_by_a_buster_keeps_what_was_up_on_them_and_on_the_boss()
    {
        var s = Session();
        // Rampart on them as last read alive; Reprisal on the boss.
        s.Frame(2, Party(mtMits: Mitigations.Mask("Rampart", "Divine Benison")), true);
        s.EnemyMitigation(Mitigations.Mask("Reprisal") | Mitigations.Mask("Kerachole"));
        s.Taken(new TakenHit(3, Boss, 500, Mt, 140_000, 0, 95_000, 100_000));
        // The frame they're seen dead: what's read then (nothing) isn't what was up.
        s.Frame(3.1, Party(mtHp: 0), true);
        var d = Assert.Single(s.Current!.Deaths);
        Assert.Equal(DeathHit.TankBuster, d.Hit);
        Assert.True(d.MitigationKnown);
        Assert.Equal(Mitigations.Mask("Rampart", "Divine Benison", "Reprisal"), d.Mitigation);
        Assert.Null(d.Phase);
        var card = DeathRecapView.Card(s.Current!, 0)!;
        Assert.Equal(new[] { "Rampart", "Reprisal", "Divine Benison" }, card.Up);
        Assert.Equal(new[] { "Guardian", "Hallowed Ground" }, card.Missing);
        Assert.False(card.NoMitigation);
        Assert.Equal("PLD", card.Job);
    }

    [Fact]
    public void A_raid_wide_that_kills_one_is_told_from_the_others_it_hit()
    {
        var s = Session();
        foreach (var who in new[] { Mt, Ot, Whm })
            s.Taken(new TakenHit(5, Boss, 600, who, 40_000, 0, 80_000, 100_000));
        s.Taken(new TakenHit(5.05, Boss, 600, Blm, 60_000, 0, 50_000, 70_000));
        s.Frame(5.2, Party(blmHp: 0), true);
        var d = Assert.Single(s.Current!.Deaths);
        Assert.Equal(DeathHit.RaidWide, d.Hit);
        Assert.Equal("BLM", DeathRecapView.Card(s.Current!, 0)!.Job);
        Assert.True(DeathRecapView.Card(s.Current!, 0)!.IsYou);
    }

    [Fact]
    public void Statuses_not_read_say_nothing_about_mitigation()
    {
        var s = Session();
        s.Frame(2, Party(read: false), true);
        s.Taken(new TakenHit(3, Boss, 500, Mt, 140_000, 0, 95_000, 100_000));
        s.Frame(3.1, Party(mtHp: 0, read: false), true);
        var card = DeathRecapView.Card(s.Current!, 0)!;
        Assert.False(card.MitigationKnown);
        Assert.False(card.NoMitigation);
        Assert.Empty(card.Missing);
    }

    // ---------------------------------------------------------------------------------------------
    // The recap's card.

    private static DeathEntry Death(ulong mits = 0, bool known = true, DeathHit hit = DeathHit.TankBuster, string? phase = "P3") =>
        new(1, "MT", "Velka Moor", 252, "Latent Defect", 182_400, 41_200, false, new[]
        {
            new RecapLine(240.0, "Auto-attack", -9_000, 150_000, string.Empty),
            new RecapLine(246.2, "Auto-attack", -11_200, 141_000, string.Empty),
            new RecapLine(247.9, "Tetragrammaton", 41_230, 99_970, "Koko Nyan"),
            new RecapLine(250.8, "Divine Benison", 0, 122_300, "Shield from Koko Nyan"),
            new RecapLine(252.0, "Latent Defect", -182_400, 141_200, string.Empty),
        }, 152_000, null, null, true, mits, known, hit, phase);

    [Fact]
    public void A_card_has_the_last_six_seconds_with_the_hp_after_each_and_the_killing_blow()
    {
        var c = DeathRecapView.Card(3, 0, Death(Mitigations.Mask("Divine Benison")), "PLD", "tank", true);
        Assert.Equal(4, c.Rows.Count);
        Assert.Equal(-5.8, c.Rows[0].Offset, 6);
        Assert.Equal(129_800, c.Rows[0].HpAfter);
        Assert.Equal((RecapKind.Heal, "Koko Nyan", 141_200L), (c.Rows[1].Kind, c.Rows[1].Who, c.Rows[1].HpAfter));
        Assert.Equal((RecapKind.Shield, "Koko Nyan"), (c.Rows[2].Kind, c.Rows[2].Who));
        Assert.True(c.Rows[3].Killing);
        Assert.Equal(0, c.Rows[3].HpAfter);
        Assert.Equal("4:12 · P3", DeathRecapView.When(c));
        Assert.Equal("4:12", DeathRecapView.When(DeathRecapView.Card(3, 0, Death(phase: null), "PLD", "tank", true)));
        // A barrier isn't mitigation: nothing that lowers damage was up.
        Assert.True(c.NoMitigation);
        Assert.Equal((" (41,200 more than they had)", ", a tank buster, with no mitigation up."), DeathRecapView.KilledWords(c));
        Assert.Equal("No Rampart, Guardian or Hallowed Ground", DeathRecapView.MissingLine(c.Missing));
        Assert.True(c.Weakness);
        Assert.Equal("Latent Defect (buster)", DeathRecapView.RowWords(c));
        Assert.Equal("Velka Moor", c.Who(false));
        Assert.Equal("MT", c.Who(true));
    }

    [Fact]
    public void A_lines_hp_after_is_never_past_their_max_and_unknown_when_the_hp_before_was()
    {
        // A heal's amount has its overheal in it (the meter's recap keeps the whole heal): 150,000 of 152,000 healed for 41,230.
        var d = new DeathEntry(1, "MT", "Velka Moor", 252, "Latent Defect", 182_400, 41_200, false, new[]
        {
            new RecapLine(247.9, "Tetragrammaton", 41_230, 150_000, "Koko Nyan"),
            new RecapLine(249.0, "Auto-attack", -9_000, -1, string.Empty),
            new RecapLine(252.0, "Latent Defect", -182_400, 141_200, string.Empty),
        }, 152_000);
        var c = DeathRecapView.Card(3, 0, d, "PLD", "tank", true);
        Assert.Equal(152_000, c.Rows[0].HpAfter);
        Assert.Equal(-1, c.Rows[1].HpAfter);
        Assert.Equal(0, c.Rows[2].HpAfter);
    }

    [Fact]
    public void The_cards_words()
    {
        Assert.Equal("−5.8 s", DeathRecapView.Offset(-5.8));
        Assert.Equal("0.0 s", DeathRecapView.Offset(0));
        Assert.Equal("−1.2 s", DeathRecapView.Offset(-1.2));
        Assert.Null(DeathRecapView.MissingLine(Array.Empty<string>()));
        Assert.Equal("No Rampart", DeathRecapView.MissingLine(new[] { "Rampart" }));
        Assert.Equal("No Rampart or Holmgang", DeathRecapView.MissingLine(new[] { "Rampart", "Holmgang" }));
        var c = DeathRecapView.Card(3, 0, Death(Mitigations.Mask("Rampart"), hit: DeathHit.Unknown), "PLD", "tank", true);
        Assert.Equal((" (41,200 more than they had)", "."), DeathRecapView.KilledWords(c));
        Assert.Equal("Latent Defect", DeathRecapView.RowWords(c));
        Assert.Equal("Goes in 8 s", DeathRecapView.GoesIn(7.2));
        Assert.Equal("+41,230", DeathRecapView.Amount(c.Rows[1]));
        Assert.Equal("shield", DeathRecapView.Amount(c.Rows[2]));
        // Not a tank: nothing said missing.
        Assert.Empty(DeathRecapView.Card(3, 0, Death(), "WHM", "healer", false).Missing);
    }

    [Fact]
    public void The_sample_is_a_wipe_of_three_newest_first()
    {
        var cards = DeathRecapSample.Cards();
        Assert.Equal(3, cards.Count);
        Assert.True(cards[0].At > cards[1].At && cards[1].At > cards[2].At);
        Assert.All(cards, c => Assert.True(c.FightId < 1));
        Assert.Equal(DeathHit.TankBuster, cards[2].Hit);
    }

    // ---------------------------------------------------------------------------------------------
    // The recap's queue.

    private static MeterRow Row(int id, string slot, string job, string role, bool you) =>
        new(id, slot, slot, job, role, you, false, 0, 0, 0, 0, 0, 0, 0, string.Empty, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, null, 0);

    private static MeterEncounter Fight(int id, params DeathEntry[] deaths) =>
        new(id, 1, "Arena", 1, 0, 300, true, null, 300, "P1", 0,
            new[] { Row(1, "MT", "PLD", "tank", false), Row(4, "R1", "BLM", "dps", true) }, Row(0, "", "", "", false), deaths);

    private static DeathEntry DeathOf(int partyId, double at) =>
        new(partyId, partyId == 1 ? "MT" : "R1", partyId == 1 ? "Tank A" : "Caster", at, "Boom", 100_000, 10_000, false, Array.Empty<RecapLine>());

    [Fact]
    public void The_queue_shows_only_new_deaths_yours_unless_everyones_and_lets_them_go_after_eight_seconds()
    {
        var q = new DeathRecapQueue();
        // The first look: what's there isn't new.
        q.Feed(Fight(1, DeathOf(1, 10)), false);
        Assert.Empty(q.Cards);
        // Someone else's death: not mine.
        q.Feed(Fight(1, DeathOf(1, 10), DeathOf(1, 20)), false);
        Assert.Empty(q.Cards);
        q.Feed(Fight(1, DeathOf(1, 10), DeathOf(1, 20), DeathOf(4, 30)), false);
        var c = Assert.Single(q.Cards);
        Assert.Equal((1, 2, true), (c.FightId, c.Index, c.IsYou));
        Assert.Same(c, q.Full);
        q.Tick(100, false);
        q.Tick(105, false);
        Assert.Single(q.Cards);
        // Held (the mouse over it): the time stands still.
        q.Tick(110, true);
        Assert.Equal(3, q.Left, 6);
        q.Tick(113.5, false);
        Assert.Empty(q.Cards);
    }

    [Fact]
    public void A_wipe_stacks_newest_first_and_a_row_opens_into_its_card()
    {
        var q = new DeathRecapQueue();
        q.Feed(Fight(1), true);
        q.Feed(Fight(2, DeathOf(1, 10), DeathOf(4, 11)), true);
        Assert.Equal(new[] { 1, 0 }, q.Cards.Select(x => x.Index));
        Assert.Null(q.Full);
        q.Toggle(q.Cards[1]);
        Assert.Same(q.Cards[1], q.Full);
        q.Toggle(q.Cards[1]);
        Assert.Null(q.Full);
        // Pinned, it stays; unpinned, it goes a few seconds later.
        q.TogglePin();
        q.Tick(0, false);
        q.Tick(60, false);
        Assert.Equal(2, q.Cards.Count);
        q.TogglePin();
        q.Tick(61, false);
        Assert.Equal(2, q.Cards.Count);
        q.Tick(70, false);
        Assert.Empty(q.Cards);
        // An older encounter shown again isn't new.
        q.Feed(Fight(1, DeathOf(4, 5)), true);
        Assert.Empty(q.Cards);
        // Looked at afresh: the deaths there aren't new.
        q.Restart();
        q.Feed(Fight(3, DeathOf(4, 5)), true);
        Assert.Empty(q.Cards);
    }

    // ---------------------------------------------------------------------------------------------
    // The ready check.

    private static ReadyMember Fed(string name, double left) => new(name, "WAR", "tank", FoodState.Fed, left);

    [Fact]
    public void The_ready_check_card_says_whos_fed_and_who_runs_out_mid_pull()
    {
        var c = ReadyCheckView.Card(ReadyCheckSample.Members, ReadyCheckSample.You, ReadyCheckSample.Average);
        Assert.Equal("7 of 8 fed · one runs out mid-pull", c.Headline);
        Assert.Equal("Your average pull here is 6:40.", c.Average);
        Assert.False(c.Compact);
        Assert.Equal("Gear 18%: repair soon", c.Gear);
        Assert.Equal(("52:10", FoodTone.Good), (c.Rows[0].Text, c.Rows[0].Tone));
        Assert.Equal(("6:05", FoodTone.Short), (c.Rows[3].Text, c.Rows[3].Tone));
        Assert.Equal(("No food", FoodTone.Bad), (c.Rows[4].Text, c.Rows[4].Tone));
        // Without an average pull nothing is amber: only the time.
        var none = ReadyCheckView.Card(ReadyCheckSample.Members, ReadyCheckSample.You, null);
        Assert.Equal(FoodTone.Good, none.Rows[3].Tone);
        Assert.Equal("7 of 8 fed", none.Headline);
        Assert.Null(none.Average);
        // One out of sight.
        var unseen = ReadyCheckView.Card(new[] { Fed("A", 2000), new ReadyMember("B", "WHM", "healer", FoodState.Unseen, 0) }, new ReadyYou(3, 1, 80), 400);
        Assert.Equal("1 of 2 fed · one out of sight", unseen.Headline);
        Assert.Equal(("can't see", FoodTone.Unknown), (unseen.Rows[1].Text, unseen.Rows[1].Tone));
        Assert.False(unseen.Compact);
        var two = ReadyCheckView.Card(new[] { Fed("A", 100), Fed("B", 200) }, new ReadyYou(3, 1, 80), 400);
        Assert.Equal("Everyone's fed · two run out mid-pull", two.Headline);
    }

    [Fact]
    public void Everyone_fed_for_longer_than_a_pull_with_gear_fine_is_one_line()
    {
        var c = ReadyCheckView.Card(new[] { Fed("A", 1650.4), Fed("B", 3000) }, new ReadyYou(14, 22, 64), 400);
        Assert.True(c.Compact);
        Assert.Equal("Everyone's fed", c.CompactTitle);
        Assert.Equal("The shortest has 27 minutes left · you have 14 tinctures", c.CompactLine);
        Assert.Null(c.Gear);
        // Worn gear keeps the whole card.
        Assert.False(ReadyCheckView.Card(new[] { Fed("A", 1650) }, new ReadyYou(1, 0, 19), 400).Compact);
        Assert.Equal("The shortest has 1 minute left · you have 1 tincture", ReadyCheckView.Card(new[] { Fed("A", 61) }, new ReadyYou(1, 0, null), null).CompactLine);
        Assert.Equal("The shortest has less than a minute left · you have no tinctures", ReadyCheckView.Card(new[] { Fed("A", 30) }, new ReadyYou(0, 0, null), null).CompactLine);
    }

    [Fact]
    public void Gear_is_warned_about_under_twenty_percent()
    {
        Assert.Null(ReadyCheckView.GearWarning(null));
        Assert.Null(ReadyCheckView.GearWarning(20));
        Assert.Equal("Gear 19%: repair soon", ReadyCheckView.GearWarning(19));
        Assert.Equal("Gear 0%: broken, repair it", ReadyCheckView.GearWarning(0));
        Assert.Equal("Tinctures ×14", ReadyCheckView.Times("Tinctures", 14));
    }

    [Fact]
    public void The_average_pull_is_of_this_dutys_kept_pulls()
    {
        var at = DateTimeOffset.UnixEpoch;
        var kept = new[]
        {
            new RecordingInfo("a", "Arena", 1, at, 300, Outcome.Wipe, 0, false),
            new RecordingInfo("b", "Arena", 2, at, 500, Outcome.Wipe, 0, false),
            new RecordingInfo("c", "Elsewhere", 1, at, 900, Outcome.Clear, 0, false),
        };
        Assert.Equal(400, ReadyCheckView.AveragePull(kept, "Arena"));
        Assert.Null(ReadyCheckView.AveragePull(kept, "Nowhere"));
        Assert.Null(ReadyCheckView.AveragePull(kept, null));
    }

    [Fact]
    public void The_card_shows_from_the_check_until_the_pull_or_thirty_seconds_after()
    {
        var w = new ReadyCheckWatch();
        Assert.False(w.Frame(0, false, false));
        Assert.False(w.Showing);
        Assert.True(w.Frame(1, true, false));
        Assert.True(w.Showing);
        Assert.Null(w.Left);
        w.Frame(10, false, false);
        Assert.True(w.Showing);
        Assert.Equal(30, w.Left!.Value, 6);
        w.Frame(39, false, false);
        Assert.True(w.Showing);
        w.Frame(40.1, false, false);
        Assert.False(w.Showing);
        // The pull starts: it goes at once.
        w.Frame(50, true, false);
        w.Frame(52, false, false);
        w.Frame(53, false, true);
        Assert.False(w.Showing);
        // Pinned, it stays through the pull; unpinned, it goes a few seconds later.
        w.Frame(60, true, false);
        w.TogglePin(61);
        w.Frame(62, false, true);
        w.Frame(200, false, false);
        Assert.True(w.Showing);
        w.TogglePin(200);
        w.Frame(203, false, false);
        Assert.True(w.Showing);
        w.Frame(206, false, false);
        Assert.False(w.Showing);
        // A ready check that starts in combat isn't shown; one that stuck is over after a minute.
        w.Frame(300, false, true);
        w.Frame(301, true, true);
        Assert.False(w.Showing);
        w.Frame(400, false, false);
        w.Frame(401, true, false);
        w.Frame(462, true, false);
        Assert.False(w.Checking);
        Assert.Equal(30, w.Left!.Value, 6);
    }

    [Fact]
    public void Arrange_windows_has_them_when_theyre_on()
    {
        Assert.Equal(new[] { ArrangePart.Meter, ArrangePart.ReadyCheck, ArrangePart.DeathRecap }, ArrangeParts.Taking(true, false, false, false, true, true));
        Assert.Equal("Ready check", ArrangeParts.Label(ArrangePart.ReadyCheck));
        Assert.Equal("Death recap", ArrangeParts.Label(ArrangePart.DeathRecap));
        Assert.True(ArrangeParts.HasOptions(ArrangePart.DeathRecap));
    }
}
