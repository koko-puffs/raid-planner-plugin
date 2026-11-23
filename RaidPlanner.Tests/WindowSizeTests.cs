using RaidPlanner.UI;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>How big the windows are (0.8.1, Settings → Windows → Size): fitting the screen, a size picked, /raidcodex size.</summary>
public class WindowSizeTests
{
    [Theory]
    [InlineData(1080, 1)]
    [InlineData(1440, 1)]
    [InlineData(1600, 1.1)]
    [InlineData(2160, 1.5)]
    [InlineData(2880, 2)]
    [InlineData(4320, 2)]
    [InlineData(0, 1)]
    [InlineData(float.NaN, 1)]
    public void Fitting_grows_with_a_screen_taller_than_1440(float height, float factor) =>
        Assert.Equal(factor, WindowSize.Factor(WindowSize.Fit, height, 1), 3);

    [Fact]
    public void Fitting_counts_what_dalamuds_own_scale_already_gives()
    {
        // 4K wants 150% over all: Dalamud at 150% already gives it; at 125% the plugin adds the rest (120%, in 5% steps).
        Assert.Equal(1f, WindowSize.Factor(WindowSize.Fit, 2160, 1.5f), 3);
        Assert.Equal(1.2f, WindowSize.Factor(WindowSize.Fit, 2160, 1.25f), 3);
        // Never smaller than Dalamud's own: at 200% on 1080p it stays 100%.
        Assert.Equal(1f, WindowSize.Factor(WindowSize.Fit, 1080, 2f), 3);
        Assert.Equal(1.5f, WindowSize.Factor(WindowSize.Fit, 2160, 0), 3);
    }

    [Fact]
    public void A_size_picked_is_on_top_of_dalamuds_whatever_the_screen()
    {
        Assert.Equal(1.3f, WindowSize.Factor(130, 2160, 1.5f), 3);
        Assert.Equal(0.8f, WindowSize.Factor(50, 1080, 1), 3);
        Assert.Equal(2f, WindowSize.Factor(500, 1080, 1), 3);
    }

    [Fact]
    public void Stepping_goes_ten_at_a_time_within_80_to_200()
    {
        Assert.Equal(110, WindowSize.Stepped(100, 1, 1));
        Assert.Equal(80, WindowSize.Stepped(80, -1, 0.8f));
        Assert.Equal(200, WindowSize.Stepped(200, 1, 2));
        // Fitting, a step starts from what fitting gives now.
        Assert.Equal(160, WindowSize.Stepped(WindowSize.Fit, 1, 1.5f));
        Assert.Equal(110, WindowSize.Stepped(WindowSize.Fit, -1, 1.2f));
    }

    [Theory]
    [InlineData("fit", WindowSize.Fit)]
    [InlineData("Auto", WindowSize.Fit)]
    [InlineData("150", 150)]
    [InlineData("150%", 150)]
    [InlineData(" 125 % ", 120)]
    [InlineData("40", 80)]
    [InlineData("999", 200)]
    public void The_command_reads_fit_or_a_size(string words, int setting) => Assert.Equal(setting, WindowSize.Parse(words));

    [Theory]
    [InlineData("")]
    [InlineData("big")]
    [InlineData("0")]
    [InlineData("-50")]
    public void The_command_says_no_to_anything_else(string words) => Assert.Null(WindowSize.Parse(words));

    [Fact]
    public void Shown_as_a_percentage() => Assert.Equal("150%", WindowSize.Words(1.5f));

    [Fact]
    public void The_size_rows_are_on_windows_and_reset_with_it()
    {
        var size = SettingsIndex.Section(SettingsTab.Windows, "Size");
        Assert.NotNull(size);
        Assert.Equal(new[] { SettingId.WindowFit, SettingId.WindowScale }, size!.Rows.Select(r => r.Id));
        Assert.Contains(SettingId.WindowFit, SettingsIndex.Resets(SettingsTab.Windows));
        Assert.Contains(SettingId.WindowScale, SettingsIndex.Find("window size").Select(e => e.Id));
    }

    [Fact]
    public void Help_lists_the_size_command() => Assert.Contains(CommandWords.Help, h => h.Words.StartsWith("size", System.StringComparison.Ordinal));
}
