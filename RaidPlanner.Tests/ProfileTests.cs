using System.Text.RegularExpressions;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// Settings profiles (0.9.0): which kind of duty a duty is, the settings read and written by path, the layer over the
/// configuration (in effect, captured, put back, recovered after a crash), the switching as you go in and out, and the
/// words the cards and the notice say.
/// </summary>
public class ProfileTests
{
    /// <summary>The configuration's profile-able settings as it has them (Configuration.cs isn't built here: its fields are checked against its source below).</summary>
    private sealed class FakeConfig : IProfileHost
    {
        public List<SettingsProfile> Profiles { get; set; } = new();
        public string? ProfileActive { get; set; }
        public Dictionary<string, string> ProfileBackup { get; set; } = new();
        public bool ShowMeter { get; set; } = true;
        public bool MeterCompact { get; set; }
        public MeterThemeId MeterTheme { get; set; } = MeterThemeId.ReadyCheck;
        public bool MeterHideOutOfCombat { get; set; }
        public bool MeterOpenInDuties { get; set; } = true;
        public OverlaySettings CalloutOverlay { get; set; } = new() { Show = true, Everywhere = true, Background = 0.45f };
        public int CalloutCount { get; set; } = 3;
        public bool ShowPlanInDuties { get; set; } = true;
        public OverlaySettings Cooldowns { get; set; } = new() { Show = false, Background = 0.6f };
        public OverlaySettings ReadyCheck { get; set; } = new() { Show = true, Everywhere = true, Background = 0.3f };
        public OverlaySettings DeathRecap { get; set; } = new() { Show = true, Everywhere = true, Background = 0.3f };
        public bool DeathRecapEveryone { get; set; }
        public int WindowSize { get; set; }
        public float MeterAnchorX { get; set; }
        public float MeterAnchorY { get; set; }
        public bool MeterAnchored { get; set; }
        public MeterGrows MeterAnchorFor { get; set; } = MeterGrows.Down;
        public float MeterHeight { get; set; }
        public float PlanPlaceX { get; set; }
        public float PlanPlaceY { get; set; }
        public float PlanPlaceW { get; set; }
        public float PlanPlaceH { get; set; }
        public bool PlanPlaced { get; set; }

        /// <summary>Not profile-able: always your usual settings'.</summary>
        public int MeterKeep { get; set; } = 20;
    }

    private static ProfileItem Item(string id) => ProfileItems.ById(id)!;

    private static SettingsProfile Ultimate(FakeConfig c)
    {
        var p = new SettingsProfile { Name = "Ultimate", UsedIn = { DutyKind.Ultimate } };
        c.Profiles.Add(p);
        return p;
    }

    // ---------------------------------------------------------------------------------------------
    // Duty kinds.

    [Theory]
    [InlineData(28u, false, false, "The Omega Protocol (Ultimate)", DutyKind.Ultimate)]
    [InlineData(28u, true, false, "Futures Rewritten (Ultimate)", DutyKind.Ultimate)]
    [InlineData(5u, true, false, "AAC Light-heavyweight M4 (Savage)", DutyKind.Savage)]
    [InlineData(5u, false, false, "AAC Light-heavyweight M4 (Savage)", DutyKind.Savage)]
    [InlineData(5u, true, false, "Some raid", DutyKind.Savage)]
    [InlineData(5u, false, false, "AAC Light-heavyweight M4", DutyKind.NormalRaid)]
    [InlineData(5u, false, true, "San d'Oria: The Second Walk", DutyKind.Alliance)]
    [InlineData(5u, true, true, "The Cloud of Darkness (Chaotic)", DutyKind.Savage)]
    [InlineData(37u, true, true, "The Cloud of Darkness (Chaotic)", DutyKind.Savage)]
    [InlineData(4u, true, false, "Worqor Lar Dor (Extreme)", DutyKind.Extreme)]
    [InlineData(4u, false, false, "Worqor Lar Dor (Extreme)", DutyKind.Extreme)]
    [InlineData(4u, true, false, "The Byakko Unreal", DutyKind.Extreme)]
    [InlineData(4u, false, false, "Containment Bay Z1T9 (Unreal)", DutyKind.Extreme)]
    [InlineData(4u, false, false, "Worqor Lar Dor", DutyKind.NormalRaid)]
    [InlineData(30u, false, false, "Another Aloalo Island", DutyKind.Criterion)]
    [InlineData(30u, true, false, "Another Aloalo Island (Savage)", DutyKind.Criterion)]
    [InlineData(30u, false, false, "The Sil'dihn Subterrane", DutyKind.Dungeon)]
    [InlineData(2u, false, false, "Alexandria", DutyKind.Dungeon)]
    [InlineData(3u, false, false, "Basic Training: Enemy Parties", DutyKind.Dungeon)]
    [InlineData(21u, false, false, "The Palace of the Dead (Floors 1-10)", DutyKind.Dungeon)]
    [InlineData(26u, false, false, "The Forbidden Land, Eureka Anemos", DutyKind.OpenWorld)]
    [InlineData(29u, false, false, "The Bozjan Southern Front", DutyKind.OpenWorld)]
    [InlineData(29u, true, true, "Delubrum Reginae (Savage)", DutyKind.Savage)]
    [InlineData(38u, false, false, "The Occult Crescent", DutyKind.OpenWorld)]
    [InlineData(6u, false, false, "The Palaistra", DutyKind.Other)]
    [InlineData(7u, false, false, "A quest battle", DutyKind.Other)]
    [InlineData(9u, false, false, "The Lost Canals of Uznair", DutyKind.Other)]
    public void A_duty_is_the_kind_its_content_type_flags_and_name_say(uint type, bool highEnd, bool alliance, string name, DutyKind kind) =>
        Assert.Equal(kind, DutyKinds.Of(new DutyFacts(type, highEnd, alliance, name)));

    [Fact]
    public void Outside_a_duty_is_the_open_world_and_a_duty_without_a_finder_entry_is_other()
    {
        Assert.Equal(DutyKind.OpenWorld, DutyKinds.Of(null));
        Assert.Equal(DutyKind.Other, DutyKinds.Of(new DutyFacts(0, false, false, string.Empty)));
        // A field operation's open zone by its intended use, even with no ContentType.
        Assert.Equal(DutyKind.OpenWorld, DutyKinds.Of(new DutyFacts(0, false, false, string.Empty, 48)));
    }

    [Fact]
    public void Kinds_have_names_lines_and_read_as_a_list()
    {
        Assert.Equal(DutyKinds.All.Count, DutyKinds.All.Select(DutyKinds.Name).Distinct().Count());
        Assert.All(DutyKinds.All, k => Assert.False(string.IsNullOrWhiteSpace(DutyKinds.Line(k))));
        Assert.Equal("Ultimate, Savage and Extreme", DutyKinds.Words(new[] { DutyKind.Extreme, DutyKind.Ultimate, DutyKind.Savage }));
        Assert.Equal("Dungeons", DutyKinds.Words(new[] { DutyKind.Dungeon }));
        Assert.Equal(string.Empty, DutyKinds.Words(Array.Empty<DutyKind>()));
    }

    // ---------------------------------------------------------------------------------------------
    // Settings by path.

    [Fact]
    public void Fields_are_read_and_written_by_path_as_invariant_text()
    {
        var c = new FakeConfig();
        Assert.Equal("true", ProfileFields.Get(c, "ShowMeter"));
        Assert.Equal("3", ProfileFields.Get(c, "CalloutCount"));
        Assert.Equal("ReadyCheck", ProfileFields.Get(c, "MeterTheme"));
        Assert.Equal("true", ProfileFields.Get(c, "CalloutOverlay.Show"));
        Assert.True(ProfileFields.Set(c, "MeterTheme", "Neon"));
        Assert.Equal(MeterThemeId.Neon, c.MeterTheme);
        Assert.True(ProfileFields.Set(c, "Cooldowns.AnchorX", "1234.5"));
        Assert.Equal(1234.5f, c.Cooldowns.AnchorX);
        Assert.True(ProfileFields.Set(c, "MeterAnchorY", 0.1f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(0.1f, c.MeterAnchorY);
        Assert.Equal(c.MeterAnchorY.ToString("R", System.Globalization.CultureInfo.InvariantCulture), ProfileFields.Get(c, "MeterAnchorY"));
    }

    [Fact]
    public void A_path_that_isnt_a_setting_or_a_value_of_the_wrong_kind_changes_nothing()
    {
        var c = new FakeConfig();
        Assert.Null(ProfileFields.Get(c, "NoSuchSetting"));
        Assert.False(ProfileFields.Set(c, "NoSuchSetting", "true"));
        Assert.False(ProfileFields.Set(c, "CalloutOverlay.Nope", "true"));
        Assert.False(ProfileFields.Set(c, "CalloutCount", "three"));
        Assert.False(ProfileFields.Set(c, "MeterTheme", "Sparkly"));
        Assert.False(ProfileFields.Set(c, "MeterTheme", "99"));
        Assert.False(ProfileFields.Set(c, "MeterAnchorX", "NaN"));
        Assert.Equal(3, c.CalloutCount);
        Assert.Equal(MeterThemeId.ReadyCheck, c.MeterTheme);
    }

    [Fact]
    public void Every_profile_row_is_a_setting_of_the_configuration()
    {
        var c = new FakeConfig();
        foreach (var item in ProfileItems.All)
        {
            Assert.NotEmpty(item.Fields);
            foreach (var f in item.Fields)
            {
                Assert.True(ProfileFields.Exists(c, f), $"{item.Id}: {f}");
                // What it reads it can write back.
                Assert.True(ProfileFields.Set(c, f, ProfileFields.Get(c, f)!), $"{item.Id}: {f}");
            }
        }
        Assert.Equal(ProfileItems.All.Count, ProfileItems.All.Select(i => i.Id).Distinct().Count());
        // A field is one row's.
        var fields = ProfileItems.All.SelectMany(i => i.Fields).ToList();
        Assert.Equal(fields.Count, fields.Distinct().Count());
    }

    [Fact]
    public void Every_profile_field_is_a_property_of_the_real_configuration_of_the_same_type()
    {
        // Configuration.cs needs Dalamud, so it isn't built here: its properties are read from its source.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RaidPlanner", "Configuration.cs"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "RaidPlanner", "Configuration.cs"));
        var props = Regex.Matches(source, @"public\s+([\w.<>?,\s]+?)\s+(\w+)\s*\{\s*get;\s*set;\s*\}")
            .ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value.Trim());
        foreach (var f in ProfileItems.All.SelectMany(i => i.Fields))
        {
            var top = f.Split('.')[0];
            Assert.True(props.ContainsKey(top), $"Configuration has no {top}");
            var fakeType = typeof(FakeConfig).GetProperty(top)!.PropertyType;
            var realType = props[top];
            Assert.True(realType == fakeType.Name || realType.EndsWith("." + fakeType.Name) || Short(fakeType) == realType, $"{top}: {realType} here, {fakeType.Name} in the test");
        }
        // The profiles themselves, and the backup that's kept with the settings.
        Assert.Contains("Profiles", props.Keys);
        Assert.Contains("ProfileActive", props.Keys);
        Assert.Contains("ProfileBackup", props.Keys);
        Assert.Contains("IProfileHost", source);
        Assert.Contains("ProfileLayer.Capture(this, this)", source);
    }

    private static string Short(Type t) => t == typeof(bool) ? "bool" : t == typeof(int) ? "int" : t == typeof(float) ? "float" : t.Name;

    // ---------------------------------------------------------------------------------------------
    // The layer.

    [Fact]
    public void A_new_profile_changes_nothing_and_a_row_turned_on_starts_at_the_usual_value()
    {
        var c = new FakeConfig { MeterCompact = false };
        var p = Ultimate(c);
        Assert.Empty(ProfileItems.Chips(p));
        ProfileLayer.Activate(c, c, p.Id);
        Assert.Empty(c.ProfileBackup);
        Assert.False(c.MeterCompact);
        ProfileLayer.Deactivate(c, c);

        ProfileLayer.Override(c, c, p, Item("meter-size"));
        Assert.Equal("false", p.Values["MeterCompact"]);
        Assert.True(ProfileItems.Changes(p, Item("meter-size")));
        Assert.Equal(new[] { "Meter Detailed" }, ProfileItems.Chips(p));
    }

    [Fact]
    public void In_effect_its_values_are_live_and_yours_come_back_as_it_goes()
    {
        var c = new FakeConfig { MeterCompact = true, CalloutCount = 2 };
        var p = Ultimate(c);
        ProfileLayer.Override(c, c, p, Item("meter-size"));
        ProfileLayer.SetValue(c, c, p, "MeterCompact", "false");
        ProfileLayer.Override(c, c, p, Item("callout-count"));
        ProfileLayer.SetValue(c, c, p, "CalloutCount", "5");
        // Not in effect: setting the profile's values changes nothing live.
        Assert.True(c.MeterCompact);
        Assert.Equal(2, c.CalloutCount);

        Assert.True(ProfileLayer.Activate(c, c, p.Id));
        Assert.False(c.MeterCompact);
        Assert.Equal(5, c.CalloutCount);
        Assert.Equal("true", c.ProfileBackup["MeterCompact"]);
        Assert.Equal("2", ProfileLayer.Usual(c, c, "CalloutCount"));
        Assert.True(ProfileLayer.Overridden(c, "CalloutCount"));
        Assert.False(ProfileLayer.Overridden(c, "ShowMeter"));

        Assert.True(ProfileLayer.Deactivate(c, c));
        Assert.True(c.MeterCompact);
        Assert.Equal(2, c.CalloutCount);
        Assert.Null(c.ProfileActive);
        Assert.Empty(c.ProfileBackup);
        Assert.False(ProfileLayer.Deactivate(c, c));
    }

    [Fact]
    public void Changes_made_while_in_effect_go_to_the_profile_for_its_settings_and_to_yours_for_the_rest()
    {
        var c = new FakeConfig { MeterCompact = true };
        var p = Ultimate(c);
        ProfileLayer.Override(c, c, p, Item("meter-size"));
        ProfileLayer.SetValue(c, c, p, "MeterCompact", "false");
        ProfileLayer.Activate(c, c, p.Id);
        // From Settings or the meter's menu: written on the live configuration.
        c.MeterCompact = true;
        c.MeterKeep = 30;
        Assert.Equal("true", ProfileLayer.Value(c, c, p, "MeterCompact"));
        ProfileLayer.Capture(c, c);
        Assert.Equal("true", p.Values["MeterCompact"]);
        ProfileLayer.Deactivate(c, c);
        Assert.True(c.MeterCompact);
        Assert.Equal(30, c.MeterKeep);

        // And without a save between: going out captures first.
        ProfileLayer.SetValue(c, c, p, "MeterCompact", "false");
        c.MeterCompact = true;
        ProfileLayer.Activate(c, c, p.Id);
        Assert.False(c.MeterCompact);
        c.MeterCompact = true;
        ProfileLayer.Deactivate(c, c);
        Assert.Equal("true", p.Values["MeterCompact"]);
    }

    [Fact]
    public void Window_places_moved_while_in_effect_are_the_profiles_own()
    {
        var c = new FakeConfig { MeterAnchorX = 100, MeterAnchorY = 200, MeterAnchored = true, PlanPlaceX = 10, PlanPlaced = true };
        c.CalloutOverlay.AnchorX = 500;
        c.CalloutOverlay.Width = 320;
        var p = Ultimate(c);
        ProfileLayer.Override(c, c, p, Item(ProfileItems.PlacesId));
        Assert.Equal("100", p.Values["MeterAnchorX"]);
        ProfileLayer.Activate(c, c, p.Id);
        // Arrange windows (or a drag) moves them.
        c.MeterAnchorX = 900;
        c.CalloutOverlay.AnchorX = 40;
        c.CalloutOverlay.Width = 400;
        c.PlanPlaceX = 700;
        ProfileLayer.Deactivate(c, c);
        Assert.Equal(100, c.MeterAnchorX);
        Assert.Equal(500, c.CalloutOverlay.AnchorX);
        Assert.Equal(320, c.CalloutOverlay.Width);
        Assert.Equal(10, c.PlanPlaceX);
        ProfileLayer.Activate(c, c, p.Id);
        Assert.Equal(900, c.MeterAnchorX);
        Assert.Equal(40, c.CalloutOverlay.AnchorX);
        Assert.Equal(400, c.CalloutOverlay.Width);
        Assert.Equal(700, c.PlanPlaceX);
        Assert.Contains("Own window places", ProfileItems.Chips(p));
    }

    [Fact]
    public void A_row_with_some_of_its_fields_gets_the_rest_from_your_settings_as_it_comes_into_effect()
    {
        // As a profile saved before a window was added to Window places.
        var c = new FakeConfig { MeterAnchorX = 100 };
        c.Cooldowns.AnchorX = 77;
        var p = Ultimate(c);
        p.Values["MeterAnchorX"] = "300";
        ProfileLayer.Activate(c, c, p.Id);
        Assert.Equal(300, c.MeterAnchorX);
        Assert.Equal("77", p.Values["Cooldowns.AnchorX"]);
        c.Cooldowns.AnchorX = 5;
        ProfileLayer.Deactivate(c, c);
        Assert.Equal(77, c.Cooldowns.AnchorX);
        Assert.Equal("5", p.Values["Cooldowns.AnchorX"]);
    }

    [Fact]
    public void A_profile_left_in_effect_by_a_crash_gives_your_settings_back_as_the_plugin_loads()
    {
        var c = new FakeConfig { ShowMeter = true, CalloutCount = 3 };
        var p = Ultimate(c);
        ProfileLayer.Override(c, c, p, Item("meter"));
        ProfileLayer.SetValue(c, c, p, "ShowMeter", "false");
        ProfileLayer.Activate(c, c, p.Id);
        c.ShowMeter = true;
        // As saved (Configuration.Save captures first), then read again as the game starts.
        ProfileLayer.Capture(c, c);
        var saved = new FakeConfig
        {
            Profiles = c.Profiles.Select(x => new SettingsProfile { Id = x.Id, Name = x.Name, UsedIn = x.UsedIn.ToList(), Values = new(x.Values) }).ToList(),
            ProfileActive = c.ProfileActive,
            ProfileBackup = new(c.ProfileBackup),
            ShowMeter = c.ShowMeter,
            CalloutCount = c.CalloutCount,
        };
        Assert.True(ProfileLayer.Recover(saved, saved));
        Assert.True(saved.ShowMeter);
        Assert.Null(saved.ProfileActive);
        Assert.Empty(saved.ProfileBackup);
        Assert.Equal("true", saved.Profiles[0].Values["ShowMeter"]);
        Assert.False(ProfileLayer.Recover(saved, saved));

        // A backup whose profile was deleted meanwhile still gives your values back.
        var orphan = new FakeConfig { ShowMeter = false, ProfileActive = "gone", ProfileBackup = { ["ShowMeter"] = "true" } };
        Assert.True(ProfileLayer.Recover(orphan, orphan));
        Assert.True(orphan.ShowMeter);
        Assert.Null(orphan.ProfileActive);
    }

    [Fact]
    public void Turning_a_row_on_or_off_while_in_effect_keeps_your_value_safe()
    {
        var c = new FakeConfig { CalloutCount = 3 };
        var p = Ultimate(c);
        ProfileLayer.Activate(c, c, p.Id);
        ProfileLayer.Override(c, c, p, Item("callout-count"));
        Assert.Equal(3, c.CalloutCount);
        ProfileLayer.SetValue(c, c, p, "CalloutCount", "1");
        Assert.Equal(1, c.CalloutCount);
        Assert.Equal("3", c.ProfileBackup["CalloutCount"]);
        ProfileLayer.Follow(c, c, p, Item("callout-count"));
        Assert.Equal(3, c.CalloutCount);
        Assert.False(p.Values.ContainsKey("CalloutCount"));
        Assert.Empty(c.ProfileBackup);
    }

    [Fact]
    public void Another_profile_turned_on_while_one_is_in_effect_starts_at_your_value_not_the_live_one()
    {
        var c = new FakeConfig { CalloutCount = 3 };
        var a = Ultimate(c);
        ProfileLayer.Override(c, c, a, Item("callout-count"));
        ProfileLayer.SetValue(c, c, a, "CalloutCount", "5");
        ProfileLayer.Activate(c, c, a.Id);
        var b = new SettingsProfile { Name = "Savage", UsedIn = { DutyKind.Savage } };
        c.Profiles.Add(b);
        ProfileLayer.Override(c, c, b, Item("callout-count"));
        Assert.Equal("3", b.Values["CalloutCount"]);
        // Switching straight from one to the other: your value in between, never the first's.
        ProfileLayer.SetValue(c, c, b, "CalloutCount", "2");
        ProfileLayer.Activate(c, c, b.Id);
        Assert.Equal(2, c.CalloutCount);
        Assert.Equal("3", c.ProfileBackup["CalloutCount"]);
        ProfileLayer.Deactivate(c, c);
        Assert.Equal(3, c.CalloutCount);
        Assert.Equal("5", a.Values["CalloutCount"]);
    }

    [Fact]
    public void Deleting_the_profile_in_effect_puts_your_settings_back()
    {
        var c = new FakeConfig { ShowPlanInDuties = true };
        var p = Ultimate(c);
        ProfileLayer.Override(c, c, p, Item("plan"));
        ProfileLayer.SetValue(c, c, p, "ShowPlanInDuties", "false");
        ProfileLayer.Activate(c, c, p.Id);
        Assert.False(c.ShowPlanInDuties);
        ProfileLayer.Delete(c, c, p);
        Assert.True(c.ShowPlanInDuties);
        Assert.Empty(c.Profiles);
        Assert.Null(c.ProfileActive);
    }

    [Fact]
    public void Settings_rows_say_when_the_profile_in_effect_sets_them()
    {
        var c = new FakeConfig();
        var p = Ultimate(c);
        ProfileLayer.Override(c, c, p, Item("meter-size"));
        ProfileLayer.Override(c, c, p, Item("size"));
        Assert.Null(ProfileLayer.Sets(c, SettingId.MeterSize));
        ProfileLayer.Activate(c, c, p.Id);
        Assert.Same(p, ProfileLayer.Sets(c, SettingId.MeterSize));
        Assert.Same(p, ProfileLayer.Sets(c, SettingId.WindowFit));
        Assert.Same(p, ProfileLayer.Sets(c, SettingId.WindowScale));
        Assert.Null(ProfileLayer.Sets(c, SettingId.MeterTheme));
        Assert.Null(ProfileLayer.Sets(c, SettingId.MeterKeep));
        Assert.Equal("The Ultimate profile sets this here; changes go to it.", ProfileItems.SetsNote(p));
        // Every row a profile shadows is a real Settings row.
        foreach (var row in ProfileItems.All.SelectMany(i => i.Rows)) Assert.Contains(SettingsIndex.All, e => e.Id == row);
    }

    // ---------------------------------------------------------------------------------------------
    // Switching.

    [Fact]
    public void Going_in_puts_the_kinds_profile_in_effect_and_leaving_goes_back()
    {
        var c = new FakeConfig { MeterCompact = true };
        var p = Ultimate(c);
        ProfileLayer.Override(c, c, p, Item("meter-size"));
        ProfileLayer.SetValue(c, c, p, "MeterCompact", "false");
        var s = new ProfileSwitcher(c, c);
        Assert.Null(s.Update(132, DutyKind.OpenWorld, false));
        var going = s.Update(1238, DutyKind.Ultimate, false);
        Assert.NotNull(going);
        Assert.Same(p, going!.Entered);
        Assert.False(going.Places);
        Assert.False(c.MeterCompact);
        Assert.Null(s.Update(1238, DutyKind.Ultimate, false));
        var leaving = s.Update(132, DutyKind.OpenWorld, false);
        Assert.Null(leaving!.Entered);
        Assert.Same(p, leaving.Left);
        Assert.True(c.MeterCompact);
    }

    [Fact]
    public void Undo_keeps_your_settings_until_you_leave()
    {
        var c = new FakeConfig { MeterCompact = true };
        var p = Ultimate(c);
        ProfileLayer.Override(c, c, p, Item("meter-size"));
        ProfileLayer.SetValue(c, c, p, "MeterCompact", "false");
        var s = new ProfileSwitcher(c, c);
        s.Update(1238, DutyKind.Ultimate, false);
        Assert.NotNull(s.Undo());
        Assert.True(c.MeterCompact);
        Assert.True(s.Held);
        Assert.Null(s.Update(1238, DutyKind.Ultimate, false));
        Assert.True(c.MeterCompact);
        s.Update(132, DutyKind.OpenWorld, false);
        Assert.False(s.Held);
        s.Update(1238, DutyKind.Ultimate, false);
        Assert.False(c.MeterCompact);
    }

    [Fact]
    public void Nothing_switches_while_waiting_and_the_first_profile_used_in_a_kind_wins()
    {
        var c = new FakeConfig();
        var first = new SettingsProfile { Name = "First", UsedIn = { DutyKind.Savage } };
        var second = new SettingsProfile { Name = "Second", UsedIn = { DutyKind.Savage, DutyKind.Extreme } };
        c.Profiles.Add(first);
        c.Profiles.Add(second);
        var s = new ProfileSwitcher(c, c);
        Assert.Null(s.Update(1, DutyKind.Savage, true));
        Assert.Null(c.ProfileActive);
        Assert.Same(first, s.Update(1, DutyKind.Savage, false)!.Entered);
        Assert.Same(second, s.Update(2, DutyKind.Extreme, false)!.Entered);
        Assert.Equal(DutyKind.Extreme, s.Kind);
        Assert.Same(second, ProfileItems.ClaimedBy(c.Profiles, DutyKind.Extreme, first));
        Assert.Null(ProfileItems.ClaimedBy(c.Profiles, DutyKind.Dungeon));
    }

    [Fact]
    public void A_switch_says_when_windows_move()
    {
        var c = new FakeConfig();
        var p = Ultimate(c);
        ProfileLayer.Override(c, c, p, Item(ProfileItems.PlacesId));
        var s = new ProfileSwitcher(c, c);
        Assert.True(s.Update(1, DutyKind.Ultimate, false)!.Places);
        Assert.True(s.Update(2, DutyKind.OpenWorld, false)!.Places);
    }

    [Fact]
    public void A_stale_backup_is_cleaned_up_by_the_next_switch()
    {
        var c = new FakeConfig { ShowMeter = false, ProfileActive = "gone", ProfileBackup = { ["ShowMeter"] = "true" } };
        var s = new ProfileSwitcher(c, c);
        Assert.NotNull(s.Update(1, DutyKind.OpenWorld, false));
        Assert.True(c.ShowMeter);
        Assert.Null(c.ProfileActive);
    }

    // ---------------------------------------------------------------------------------------------
    // Words.

    [Fact]
    public void Cards_and_the_notice_say_what_a_profile_changes()
    {
        var c = new FakeConfig();
        var p = Ultimate(c);
        foreach (var id in new[] { "meter-size", "callout-count", "callout-small", "plan", "cooldowns", ProfileItems.PlacesId, "size", "meter-theme" })
            ProfileLayer.Override(c, c, p, Item(id));
        ProfileLayer.SetValue(c, c, p, "MeterCompact", "false");
        ProfileLayer.SetValue(c, c, p, "MeterTheme", "Neon");
        ProfileLayer.SetValue(c, c, p, "WindowSize", "120");
        Assert.Equal(new[]
        {
            "Meter Detailed", "Theme Neon", "Callouts 3 at once", "Callouts big", "Plan window on", "Party cooldowns off", "Own window places", "Windows at 120%",
        }, ProfileItems.Chips(p));
        Assert.Equal("Ultimate profile: meter Detailed, theme Neon, callouts 3 at once and 5 more", ProfileItems.Summary(p));
        Assert.Equal("Raid profile", ProfileItems.Title(new SettingsProfile { Name = "Raid profile" }));
        Assert.Equal("Farm profile", ProfileItems.Summary(new SettingsProfile { Name = "Farm" }));
        Assert.Equal("Windows fit the screen", ProfileItems.Chip(Item("size"), "0"));
    }

    [Fact]
    public void Steps_go_round_and_new_names_dont_clash()
    {
        Assert.Equal(80, ProfileItems.StepSize(WindowSize.Fit, 1));
        Assert.Equal(200, ProfileItems.StepSize(WindowSize.Fit, -1));
        Assert.Equal(WindowSize.Fit, ProfileItems.StepSize(200, 1));
        Assert.Equal(130, ProfileItems.StepSize(120, 1));
        var theme = Item("meter-theme");
        Assert.Equal("Mochi", ProfileItems.StepChoice(theme, "ReadyCheck", 1));
        Assert.Equal("Grimoire", ProfileItems.StepChoice(theme, "ReadyCheck", -1));
        var profiles = new List<SettingsProfile>();
        Assert.Equal("New profile", ProfileItems.NewName(profiles));
        profiles.Add(new SettingsProfile { Name = "New profile" });
        profiles.Add(new SettingsProfile { Name = "new profile 2" });
        Assert.Equal("New profile 3", ProfileItems.NewName(profiles));
    }

    [Fact]
    public void Profiles_has_its_own_settings_page_found_by_a_search()
    {
        Assert.Contains(SettingsTab.Profiles, SettingsIndex.Pages);
        Assert.Equal("Profiles", SettingsIndex.Title(SettingsTab.Profiles));
        Assert.Contains(SettingsIndex.Find("profiles"), e => e.Id == SettingId.Profiles && e.Page == SettingsTab.Profiles);
        Assert.False(SettingsIndex.CanReset(SettingsTab.Profiles));
    }
}
