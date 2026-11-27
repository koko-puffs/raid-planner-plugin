using RaidPlanner.UI;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>0.8.0: the main parts' switches and the setup guide's state.</summary>
public class SetupTests
{
    [Fact]
    public void QuickStartsAreWhatTheGuideSays()
    {
        Assert.Equal(new FeatureSet(true, true, true, true, false, true), Features.Preset(FeaturePreset.Recommended));
        Assert.Equal(6, Features.Preset(FeaturePreset.Everything).Count);
        var rec = Features.Preset(FeaturePreset.RecordingOnly);
        Assert.True(rec.Record && rec.Upload);
        Assert.False(rec.Meter || rec.Plan || rec.Cooldowns || rec.Progress);
    }

    [Fact]
    public void SwitchesNameTheirQuickStartOrNone()
    {
        var set = Features.Preset(FeaturePreset.Recommended);
        Assert.Equal(FeaturePreset.Recommended, Features.PresetOf(set));
        Assert.Equal(FeaturePreset.Everything, Features.PresetOf(set.With(Feature.Cooldowns, true)));
        Assert.Null(Features.PresetOf(set.With(Feature.Meter, false)));
        Assert.True(set[Feature.Plan]);
        Assert.False(set.With(Feature.Plan, false)[Feature.Plan]);
    }

    [Fact]
    public void OnlyUploadsAndProgressNeedAnAccount()
    {
        foreach (var f in Features.All)
            Assert.Equal(f is Feature.Upload or Feature.Progress, Features.NeedsAccount(f));
        Assert.Equal(6, Features.All.Count);
    }

    [Fact]
    public void SettingsSavedBeforeTheGuideNeverSeeIt()
    {
        Assert.Equal(SetupState.Done, SetupState.Migrate(SetupState.Unknown));
        Assert.Equal(SetupState.Pending, SetupState.Migrate(SetupState.Pending));
        Assert.True(SetupState.Due(SetupState.Pending));
        Assert.False(SetupState.Due(SetupState.Done));
    }
}

/// <summary>0.8.0's review: what Reset this page leaves, and the guide's words.</summary>
public class SetupReviewTests
{
    [Fact]
    public void ResettingRecordingLeavesUploadsAsTheyAre()
    {
        // On would upload pulls, which are public on the site: a reset never turns it back on.
        Assert.DoesNotContain(SettingId.UploadLogs, SettingsIndex.Resets(SettingsTab.Recording));
        Assert.Contains(SettingId.Keep, SettingsIndex.Resets(SettingsTab.Recording));
    }

    [Fact]
    public void TheCooldownsNoteSaysWhereItShows()
    {
        var facts = new SetupFacts(true, RecordLevels.Hard, 20, false, "Ready Check", false, CooldownsEverywhere: true);
        Assert.EndsWith("everywhere", SetupGuide.DoneNote(Feature.Cooldowns, true, facts));
        Assert.EndsWith("in duties", SetupGuide.DoneNote(Feature.Cooldowns, true, facts with { CooldownsEverywhere = false }));
    }
}
