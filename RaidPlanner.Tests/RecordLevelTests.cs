using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// Settings → Recording → Which duties (the dungeons round): three levels, each duty's from the game's data (its
/// ContentType row, HighEndDuty flag, alliance and English name, as <c>GameData.Facts</c> reads them). The duties are real
/// (their ContentFinderCondition ids in the comments, as the site's <c>duties.json</c> has them).
/// </summary>
public class RecordLevelTests
{
    [Theory]
    // AAC Heavyweight M4 (Savage), M12S (1075) and its normal (1074).
    [InlineData(5u, true, false, "AAC Heavyweight M4 (Savage)", 1)]
    [InlineData(5u, false, false, "AAC Heavyweight M4", 2)]
    // Recollection (Extreme) (1031) and its normal (1030); an Extreme named only "The Minstrel's Ballad" (1062).
    [InlineData(4u, true, false, "Recollection (Extreme)", 1)]
    [InlineData(4u, false, false, "Recollection", 2)]
    [InlineData(4u, false, false, "the Minstrel's Ballad: Necron's Embrace", 1)]
    // An Unreal trial.
    [InlineData(4u, true, false, "Containment Bay Z1T9 (Unreal)", 1)]
    // Ultimates: Futures Rewritten (1006), Dancing Mad (1094).
    [InlineData(28u, true, false, "Futures Rewritten (Ultimate)", 1)]
    [InlineData(28u, true, false, "Dancing Mad (Ultimate)", 1)]
    // Criterion: Another Merchant's Tale (1079), Another Aloalo Island (Savage) (980); a Variant dungeon (868) is a dungeon.
    [InlineData(30u, true, false, "Another Merchant's Tale", 1)]
    [InlineData(30u, true, false, "Another Aloalo Island (Savage)", 1)]
    [InlineData(30u, false, false, "The Sil'dihn Subterrane", 3)]
    // The Chaotic alliance raid (1010); a normal alliance raid, Jeuno: The First Walk (1015).
    [InlineData(37u, true, true, "The Cloud of Darkness (Chaotic)", 1)]
    [InlineData(5u, false, true, "Jeuno: The First Walk", 3)]
    // Dungeons: Alexandria (827), Yuweyawata Field Station (1008); a deep dungeon; a guildhest.
    [InlineData(2u, false, false, "Alexandria", 3)]
    [InlineData(2u, false, false, "Yuweyawata Field Station", 3)]
    [InlineData(21u, false, false, "The Palace of the Dead (Floors 1-10)", 3)]
    [InlineData(3u, false, false, "Basic Training: Enemy Parties", 3)]
    // Delubrum Reginae (Savage) (761) is hard content; Delubrum Reginae (760) an alliance raid.
    [InlineData(29u, true, true, "Delubrum Reginae (Savage)", 1)]
    [InlineData(29u, false, true, "Delubrum Reginae", 3)]
    // The rest of the duties: PvP, a treasure dungeon.
    [InlineData(6u, false, false, "The Palaistra", 3)]
    [InlineData(9u, false, false, "The Lost Canals of Uznair", 3)]
    public void A_duty_is_recorded_from_the_level_its_data_says(uint type, bool highEnd, bool alliance, string name, int level)
    {
        var kind = DutyKinds.Of(new DutyFacts(type, highEnd, alliance, name));
        Assert.Equal(level, RecordLevels.Of(kind));
        for (var at = 1; at <= 3; at++) Assert.Equal(at >= level, RecordLevels.Records(at, kind));
    }

    [Fact]
    public void The_field_operations_open_zones_are_only_recorded_with_every_duty()
    {
        // Eureka Anemos, the Bozjan Southern Front (as with In every duty before); the Occult Crescent never (the recorder's own rule).
        Assert.Equal(3, RecordLevels.Of(DutyKinds.Of(new DutyFacts(26, false, false, "The Forbidden Land, Eureka Anemos", 41))));
        Assert.Equal(3, RecordLevels.Of(DutyKinds.Of(new DutyFacts(29, false, false, "The Bozjan Southern Front", 48))));
        Assert.Equal(3, RecordLevels.Of(DutyKinds.Of(new DutyFacts(0, false, false, string.Empty))));
    }

    [Fact]
    public void The_old_switch_becomes_a_level_once()
    {
        // In every duty on: all three; off (or a fresh install's settings): hard content. The old switch is cleared.
        Assert.Equal((3, false), RecordLevels.Migrate(0, true));
        Assert.Equal((1, false), RecordLevels.Migrate(0, false));
        // Once set, it stays as chosen.
        Assert.Equal((2, false), RecordLevels.Migrate(2, false));
        Assert.Equal((1, false), RecordLevels.Migrate(1, false));
    }

    [Fact]
    public void A_level_not_known_is_hard_content_and_each_has_its_words()
    {
        Assert.Equal(1, RecordLevels.Clamp(0));
        Assert.Equal(1, RecordLevels.Clamp(7));
        Assert.False(RecordLevels.Records(0, DutyKind.NormalRaid));
        Assert.Equal(new[] { "Hard content", "+ Normal", "+ Dungeons" }, RecordLevels.Labels);
        Assert.Equal(3, RecordLevels.Hints.Count);
        Assert.Equal("+ Normal", RecordLevels.Label(2));
    }
}
