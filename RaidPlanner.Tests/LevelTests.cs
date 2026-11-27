using System.Text.Json.Nodes;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// The dungeons round: each pull says how hard its duty is (<c>level</c>: 1 hard content, 2 normal raids and trials, 3 the
/// rest), in the file and the summary, from the classification Settings' Which duties uses (<see cref="RecordLevels.For"/>),
/// so the site can keep hard content's replays longer than the rest.
/// </summary>
public class LevelTests
{
    private static RecordingBuilder New() => new(new DutyInfo("The Vanguard", 1198, 1000), 4, new DateTimeOffset(2026, 11, 27, 20, 0, 0, TimeSpan.Zero),
        false, "0", _ => null);

    [Fact]
    public void The_sample_is_hard_content_in_the_file_and_the_summary()
    {
        var r = RecordingTests.Sample();
        var o = JsonNode.Parse(r.ToJson())!.AsObject();
        Assert.Equal(1, o["level"]!.GetValue<int>());
        Assert.Equal(1, Recording.Parse(o.ToJsonString()).Level);
        Assert.Equal(1, PullSummary.Build(r, 1)["level"]!.GetValue<int>());
    }

    [Fact]
    public void A_dungeons_pull_is_level_three_and_a_copy_keeps_it()
    {
        var b = New();
        b.SetLevel(RecordLevels.For(new DutyFacts(2, false, false, "The Vanguard")));
        var r = b.Finish(60, Outcome.Ended);
        Assert.Equal(3, r.Level);
        Assert.Equal(3, PullSummary.Build(r, 1)["level"]!.GetValue<int>());
        Assert.Equal(3, r.WithFrames(r.Frames, false).Level);
    }

    [Theory]
    // As RecordLevelTests: a Savage, a normal raid, an Extreme and its normal, an ultimate, Criterion, the Chaotic alliance raid,
    // a normal alliance raid, a dungeon, PvP.
    [InlineData(5u, true, false, "AAC Heavyweight M4 (Savage)", 0u, 1)]
    [InlineData(5u, false, false, "AAC Heavyweight M4", 0u, 2)]
    [InlineData(4u, true, false, "Recollection (Extreme)", 0u, 1)]
    [InlineData(4u, false, false, "Recollection", 0u, 2)]
    [InlineData(28u, true, false, "Futures Rewritten (Ultimate)", 0u, 1)]
    [InlineData(30u, true, false, "Another Merchant's Tale", 0u, 1)]
    [InlineData(37u, true, true, "The Cloud of Darkness (Chaotic)", 0u, 1)]
    [InlineData(5u, false, true, "Jeuno: The First Walk", 0u, 3)]
    [InlineData(2u, false, false, "Alexandria", 0u, 3)]
    [InlineData(6u, false, false, "The Palaistra", 0u, 3)]
    // The field operations' zones: Eureka Anemos, the Bozjan Southern Front; Delubrum Reginae (Savage) in its own zone.
    [InlineData(26u, false, false, "The Forbidden Land, Eureka Anemos", 41u, 3)]
    [InlineData(29u, false, false, "The Bozjan Southern Front", 48u, 3)]
    [InlineData(29u, true, true, "Delubrum Reginae (Savage)", 53u, 1)]
    // A zone in a duty that isn't from the Duty Finder (a quest's): every other duty.
    [InlineData(0u, false, false, "", 0u, 3)]
    public void A_duty_s_level_is_the_one_it_is_first_recorded_at(uint type, bool highEnd, bool alliance, string name, uint use, int level)
    {
        var facts = new DutyFacts(type, highEnd, alliance, name, use);
        Assert.Equal(level, RecordLevels.For(facts));
        Assert.Equal(RecordLevels.Of(DutyKinds.Of(facts)), RecordLevels.For(facts));
    }

    [Fact]
    public void A_duty_that_can_t_be_told_has_no_level_and_none_is_written()
    {
        Assert.Null(RecordLevels.For(null));
        var b = New();
        b.SetLevel(RecordLevels.For(null));
        var r = b.Finish(30, Outcome.Ended);
        Assert.Null(r.Level);
        Assert.False(JsonNode.Parse(r.ToJson())!.AsObject().ContainsKey("level"));
        Assert.False(PullSummary.Build(r, 1).ContainsKey("level"));
        // Nor one outside 1–3.
        b = New();
        b.SetLevel(4);
        Assert.Null(b.Finish(30, Outcome.Ended).Level);
    }

    [Fact]
    public void The_level_is_read_back_leniently()
    {
        var json = RecordingTests.Sample().ToJson();
        int? Read(JsonNode? value)
        {
            var o = JsonNode.Parse(json)!.AsObject();
            o["level"] = value;
            return Recording.Parse(o.ToJsonString()).Level;
        }
        Assert.Equal(2, Read(2));
        Assert.Null(Read(0));
        Assert.Null(Read(4));
        Assert.Null(Read(1.5));
        Assert.Null(Read("1"));
        // An older file, without it: none, written back without it.
        var old = JsonNode.Parse(json)!.AsObject();
        old.Remove("level");
        var r = Recording.Parse(old.ToJsonString());
        Assert.Null(r.Level);
        Assert.DoesNotContain("\"level\"", r.ToJson());
    }
}
