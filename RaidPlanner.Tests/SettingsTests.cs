using RaidPlanner.UI;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>Settings' pages as data (0.8.0): every row has a home, the more rows' words, the search, what a page resets, the Features cards' tags.</summary>
public class SettingsIndexTests
{
    [Fact]
    public void Every_row_has_exactly_one_home_page()
    {
        foreach (var id in Enum.GetValues<SettingId>())
        {
            var homes = SettingsIndex.All.Where(e => e.Id == id && !e.Echo).ToList();
            Assert.True(homes.Count == 1, $"{id} has {homes.Count} homes");
        }
    }

    [Fact]
    public void A_copy_is_on_another_page_than_its_home_and_never_twice_on_one()
    {
        foreach (var e in SettingsIndex.All.Where(e => e.Echo))
            Assert.NotEqual(e.Page, SettingsIndex.All.Single(h => h.Id == e.Id && !h.Echo).Page);
        foreach (var page in SettingsIndex.Pages)
        {
            var ids = SettingsIndex.On(page).Select(e => e.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
    }

    [Fact]
    public void Pages_start_with_features_and_each_has_its_own_title_and_line()
    {
        Assert.Equal(SettingsTab.Features, SettingsIndex.Pages[0]);
        Assert.Equal(8, SettingsIndex.Pages.Count);
        Assert.Equal(SettingsIndex.Pages.Count, SettingsIndex.Pages.Select(SettingsIndex.Title).Distinct().Count());
        Assert.All(SettingsIndex.Pages, p => Assert.False(string.IsNullOrWhiteSpace(SettingsIndex.Lead(p))));
        Assert.Equal("Damage meter", SettingsIndex.Title(SettingsTab.Meter));
        Assert.Equal(SettingsTab.Windows, SettingsIndex.LeadLink(SettingsTab.Meter));
        Assert.Null(SettingsIndex.LeadLink(SettingsTab.Recording));
    }

    [Fact]
    public void Every_row_has_a_name_and_a_line_and_every_folded_one_its_words()
    {
        foreach (var e in SettingsIndex.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.Line));
            if (e.More) Assert.False(string.IsNullOrWhiteSpace(e.Short), $"{e.Id} on {e.Page} has no words for its more row");
        }
    }

    [Fact]
    public void The_more_row_names_what_it_folds()
    {
        Assert.Equal("3 more: smoother replays, the server info bar, recordings folder", SettingsIndex.MoreWords(SettingsTab.Recording));
        Assert.StartsWith("12 more: grows up or down, head, order, limit break gauge, background, when a fight ends, kept fights, pace against",
            SettingsIndex.MoreWords(SettingsTab.Meter));
        Assert.Equal(string.Empty, SettingsIndex.MoreWords(SettingsTab.Windows));
        Assert.Equal(string.Empty, SettingsIndex.Words(Array.Empty<string>()));
        Assert.Empty(SettingsIndex.More(SettingsTab.Features));
    }

    [Fact]
    public void Sections_keep_their_rows_in_order_and_leave_out_the_folded_ones()
    {
        var meter = SettingsIndex.Sections(SettingsTab.Meter);
        Assert.Equal(new[] { "When it shows", "How it looks", "The numbers" }, meter.Select(s => s.Title));
        Assert.Equal(new[] { SettingId.ShowMeter, SettingId.MeterWhere, SettingId.MeterHide }, meter[0].Rows.Select(r => r.Id));
        Assert.DoesNotContain(meter.SelectMany(s => s.Rows), r => r.More);
        var windows = SettingsIndex.Sections(SettingsTab.Windows).Select(s => s.Title).ToList();
        Assert.Equal(new[] { "Arrange", "Damage meter", "Plan window", "Callouts", "Party cooldowns" }, windows);
        Assert.NotNull(SettingsIndex.Section(SettingsTab.Plans, "On-screen callouts"));
    }

    [Fact]
    public void Lock_and_click_through_moved_to_windows()
    {
        Assert.Equal(SettingsTab.Windows, Home(SettingId.MeterLock).Page);
        Assert.Equal(SettingsTab.Windows, Home(SettingId.MeterClickThrough).Page);
        Assert.Equal(SettingsTab.Windows, Home(SettingId.PlanLock).Page);
        Assert.Equal(SettingsTab.Windows, Home(SettingId.CalloutLock).Page);
        Assert.Equal(SettingsTab.Windows, Home(SettingId.CdLock).Page);
        Assert.DoesNotContain(SettingsIndex.On(SettingsTab.Meter), e => e.Id is SettingId.MeterLock or SettingId.MeterClickThrough);
    }

    private static SettingEntry Home(SettingId id) => SettingsIndex.All.Single(e => e.Id == id && !e.Echo);

    [Fact]
    public void A_search_finds_rows_by_name_or_line_and_never_a_copy()
    {
        Assert.Empty(SettingsIndex.Find(""));
        Assert.Empty(SettingsIndex.Find("   "));
        Assert.Equal(new[] { SettingId.MeterRank }, SettingsIndex.Find("encdps").Select(e => e.Id));
        var backgrounds = SettingsIndex.Find("Background");
        Assert.Equal(new[] { SettingId.MeterBackground, SettingId.CalloutBackground, SettingId.CdBackground }, backgrounds.Select(e => e.Id));
        Assert.All(backgrounds, e => Assert.Equal(SettingsTab.Windows, e.Page));
        Assert.All(SettingsIndex.Find("e"), e => Assert.False(e.Echo));
        // Every word, anywhere in the name or the line.
        Assert.Contains(SettingsIndex.Find("upload older"), e => e.Id == SettingId.UploadOlder);
        Assert.DoesNotContain(SettingsIndex.Find("upload older"), e => e.Id == SettingId.UploadLogs);
        Assert.Empty(SettingsIndex.Find("upload zebra"));
    }

    [Fact]
    public void A_search_ignores_case_quotes_and_accents()
    {
        Assert.Contains(SettingsIndex.Find("ACT’s"), e => e.Id == SettingId.MeterAct);
        Assert.Contains(SettingsIndex.Find("acts numbers"), e => e.Id == SettingId.MeterAct);
        Assert.Contains(SettingsIndex.Find("rôle colours"), e => e.Id == SettingId.MeterRoles);
        Assert.True(SettingsIndex.Matches(SettingsIndex.QueryWords("rep war"), "Reprisal", "PLD, WAR, DRK, GNB"));
        Assert.False(SettingsIndex.Matches(SettingsIndex.QueryWords(""), "Reprisal"));
    }

    [Fact]
    public void A_page_resets_its_settings_not_its_buttons_nor_the_windows_show_rows()
    {
        Assert.Empty(SettingsIndex.Resets(SettingsTab.Features));
        Assert.Empty(SettingsIndex.Resets(SettingsTab.Account));
        Assert.False(SettingsIndex.CanReset(SettingsTab.Features));
        var recording = SettingsIndex.Resets(SettingsTab.Recording);
        Assert.Contains(SettingId.AutoRecord, recording);
        Assert.Contains(SettingId.SmoothReplays, recording);
        Assert.DoesNotContain(SettingId.UploadOlder, recording);
        Assert.DoesNotContain(SettingId.RecordingsFolder, recording);
        var windows = SettingsIndex.Resets(SettingsTab.Windows);
        Assert.Contains(SettingId.MeterLock, windows);
        Assert.Contains(SettingId.CdBackground, windows);
        Assert.DoesNotContain(SettingId.ShowMeter, windows);
        Assert.DoesNotContain(SettingId.CdShow, windows);
        Assert.DoesNotContain(SettingId.Arrange, windows);
        // The meter's Grows and Background are on its page too, and its reset puts them back.
        var meter = SettingsIndex.Resets(SettingsTab.Meter);
        Assert.Contains(SettingId.MeterGrows, meter);
        Assert.Contains(SettingId.MeterBackground, meter);
        Assert.Contains(SettingId.CdPicks, SettingsIndex.Resets(SettingsTab.Cooldowns));
        Assert.Equal(new[] { SettingId.Diagnostics, SettingId.MeterAct }, SettingsIndex.Resets(SettingsTab.Advanced));
    }

    [Fact]
    public void Each_feature_card_goes_to_its_page()
    {
        Assert.Equal(SettingsTab.Recording, SettingsIndex.PageOf(Feature.Record));
        Assert.Equal(SettingsTab.Recording, SettingsIndex.PageOf(Feature.Upload));
        Assert.Equal(SettingsTab.Recording, SettingsIndex.PageOf(Feature.Progress));
        Assert.Equal(SettingsTab.Meter, SettingsIndex.PageOf(Feature.Meter));
        Assert.Equal(SettingsTab.Plans, SettingsIndex.PageOf(Feature.Plan));
        Assert.Equal(SettingsTab.Cooldowns, SettingsIndex.PageOf(Feature.Cooldowns));
    }

    private static readonly FeatureFacts Linked = new(true, false, 0, false, false, false, null, false);

    [Fact]
    public void A_feature_card_says_how_its_part_is_doing()
    {
        Assert.Equal(("Ready", StatusKind.Ready), FeatureStatus.Of(Feature.Record, true, Linked));
        Assert.Equal(("Recording", StatusKind.Busy), FeatureStatus.Of(Feature.Record, true, Linked with { Recording = true }));
        Assert.Equal(("Off", StatusKind.Off), FeatureStatus.Of(Feature.Cooldowns, false, Linked));
        Assert.Equal(("3 waiting to upload", StatusKind.Busy), FeatureStatus.Of(Feature.Upload, true, Linked with { Waiting = 3 }));
        Assert.Equal(("Stopped", StatusKind.Problem), FeatureStatus.Of(Feature.Upload, true, Linked with { UploadStopped = true, Waiting = 3 }));
        Assert.Equal(("Needs an account", StatusKind.Problem), FeatureStatus.Of(Feature.Upload, true, Linked with { Linked = false }));
        Assert.Equal(("Needs an account", StatusKind.Problem), FeatureStatus.Of(Feature.Progress, false, Linked with { Linked = false }));
        Assert.Equal(("In duties", StatusKind.Plain), FeatureStatus.Of(Feature.Meter, true, Linked));
        Assert.Equal(("Everywhere", StatusKind.Plain), FeatureStatus.Of(Feature.Meter, true, Linked with { MeterEverywhere = true }));
        Assert.Equal(("Stopped", StatusKind.Problem), FeatureStatus.Of(Feature.Meter, true, Linked with { MeterStopped = true }));
        Assert.Equal(("Plan: TOP v3", StatusKind.Plain), FeatureStatus.Of(Feature.Plan, true, Linked with { PlanName = "TOP v3" }));
        Assert.Equal(("Ready", StatusKind.Ready), FeatureStatus.Of(Feature.Plan, true, Linked with { Linked = false }));
        Assert.Equal(("Everywhere", StatusKind.Plain), FeatureStatus.Of(Feature.Cooldowns, true, Linked with { CooldownsEverywhere = true }));
    }
}
