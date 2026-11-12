using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>Settings' tabs: how pulls are recorded, the plan window, the meter, and the site.</summary>
public enum SettingsTab
{
    Recording,
    Plans,
    Meter,

    /// <summary>The party cooldown tracker (0.4.0).</summary>
    Cooldowns,
    Account,
}

/// <summary>
/// Settings (the mock-ups' "Settings" and the meter's "Settings"): the square back button, the
/// title as a small callout, the site's tabs with the ember cast bar under the one you're on, and a
/// slab of rows, each a setting's name over one muted line with its control at the right (switches
/// on in ember, steppers and segments 40 or 32 tall, dense buttons). A row that can't be used dims
/// its words too, and says why ("Link your account first: Account"). The version and What's new at
/// the foot.
/// </summary>
public sealed partial class MainWindow
{
    private const string ReleasesUrl = "https://github.com/koko-puffs/raid-planner-plugin/releases";

    private static readonly TabSpec[] SettingsTabs = { new("Recording"), new("Plan window"), new("Meter"), new("Cooldowns"), new("Account") };

    private static readonly string[] GrowsItems = { "Down", "Up" };

    private static readonly string[] GaugeItems = { "Head", "Foot", "Off" };

    private static readonly string[] WhereItems = { "In duties", "Everywhere" };

    /// <summary>The cooldown tracker's sample: on now, and starting or stopping it (set by the plugin).</summary>
    public Func<bool>? CooldownsSampleOn { get; set; }

    public Action? ToggleCooldownsSample { get; set; }

    /// <summary>The callouts' sample: on now, and starting or stopping it (set by the plugin).</summary>
    public Func<bool>? CalloutsSampleOn { get; set; }

    public Action? ToggleCalloutsSample { get; set; }

    /// <summary>Plays one of the game's sound effects (&lt;se.N&gt;), to hear the callouts' (set by the plugin).</summary>
    public Action<int>? PlaySound { get; set; }

    /// <summary>How Send my party to my roster went, and when (shown for a while under the slab).</summary>
    private (string Text, bool Ok, DateTime At)? rosterNote;

    private SettingsTab settingsTab;

    /// <summary>The meter's background was dragged and not saved yet.</summary>
    private bool backgroundDirty;

    /// <summary>Where Settings was opened from: its back button goes there.</summary>
    private Screen settingsFrom = Screen.Home;

    private void OpenSettings(SettingsTab tab)
    {
        settingsTab = tab;
        if (screen != Screen.Settings) settingsFrom = screen;
        screen = Screen.Settings;
    }

    /// <summary>Opens Settings on the meter's tab (the meter's Settings, /raidplan meter settings).</summary>
    public void ShowMeterSettings() => ShowSettingsOn(SettingsTab.Meter);

    /// <summary>Opens Settings on a tab (/raidplan cooldowns settings, /raidplan callouts settings).</summary>
    public void ShowSettingsOn(SettingsTab tab)
    {
        Dismiss();
        settingsFrom = Screen.Home;
        screen = Screen.Settings;
        settingsTab = tab;
        IsOpen = true;
    }

    private void DrawSettings()
    {
        Frame.BeginHeader();
        if (ScreenHeader(settingsFrom == Screen.Pull ? "Back to the pull" : "Back", null, "Settings"))
            screen = settingsFrom == Screen.Pull && openFile != null && store.Find(openFile) != null ? Screen.Pull : Screen.Home;
        Kit.Gap(10);
        var at = Kit.At;
        // The five tabs at their size where they fit, else dense (a narrow window).
        var tabSize = Kit.TabsWidth(SettingsTabs, TabSize.Md) <= Kit.Width ? TabSize.Md : TabSize.Sm;
        settingsTab = (SettingsTab)Kit.Tabs("##settings-tab", at, SettingsTabs, (int)settingsTab, tabSize, true, Kit.Width);
        Kit.Reserve(at, Kit.Width, S(tabSize == TabSize.Md ? H : HSm));
        Frame.EndHeader();

        Frame.BeginBody(settingsTab switch
        {
            SettingsTab.Account => "##settings-account",
            SettingsTab.Plans => "##settings-plans",
            SettingsTab.Meter => "##settings-meter",
            SettingsTab.Cooldowns => "##settings-cooldowns",
            _ => "##settings-recording",
        }, null);
        switch (settingsTab)
        {
            case SettingsTab.Account:
                DrawAccountTab();
                break;
            case SettingsTab.Cooldowns:
                DrawCooldownsTab();
                break;
            case SettingsTab.Plans:
                DrawPlansTab();
                break;
            case SettingsTab.Meter:
                DrawMeterTab();
                break;
            default:
                DrawRecordingTab();
                break;
        }

        Frame.BeginBottom("settings-bottom");
        // "0.3.0 · What's new": the version in mono, the link underlined.
        var p = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var version = VersionText;
        var vw = Txt.Width(version, Face.Mono, 13);
        var dot = " · ";
        var dw = Txt.Width(dot, Face.Sans, 13);
        var lw = Kit.LinkWidth("What's new");
        var x = p.X + (w - vw - dw - lw) / 2;
        var lineH = S(19.5f);
        Txt.Line(dl, new Vector2(x, p.Y), version, Face.Mono, 13, Palette.Muted, 19.5f);
        Txt.Line(dl, new Vector2(x + vw, p.Y), dot, Face.Sans, 13, Palette.Muted, 19.5f);
        if (Kit.Link("##news", new Vector2(x + vw + dw, p.Y + (lineH - Txt.Natural(Face.SansMedium, 13)) / 2), "What's new")) Dalamud.Utility.Util.OpenLink(ReleasesUrl);
        Kit.Reserve(p, w, lineH);
        Frame.EndBottom();
        Frame.EndBody();
    }

    /// <summary>"0.3.0", read once (it's a reflection call).</summary>
    private static readonly string VersionText = typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    private void DrawRecordingTab()
    {
        Kit.BeginPanel("##set-recording", 0, 0, 0);
        var i = 0;
        if (SwitchRow(i++, "##auto", "Record fights by themselves", "Trials, raids and ultimates, from the pull until the fight ends", config.AutoRecord,
                "Each pull is recorded on its own: the bosses' casts and where they land, stacks, spreads, tankbusters, tethers, head markers and waymarks. Off: nothing is recorded."))
        {
            config.AutoRecord = !config.AutoRecord;
            config.Save();
        }
        if (config.AutoRecord && SwitchRow(i++, "##everywhere", "In every duty", "Dungeons and field operations too", config.RecordAllDuties,
                "Off, only fights are recorded (trials, raids, ultimates, criterion dungeons), so a run of trash packs never pushes out the pulls you want. On, every duty's pulls are, but never the Occult Crescent's.",
                sub: true))
        {
            config.RecordAllDuties = !config.RecordAllDuties;
            config.Save();
        }
        if (SwitchRow(i++, "##party", "Record where the party stands", "Their places come along into the plan. Names are never kept.", config.RecordParty,
                "On, the plan shows where everyone stood at each mechanic (you can still swap to standard spots on the site). Off, only the enemies and their attacks are kept, for when you'd rather work out your own strats. Names are never kept, only jobs."))
        {
            config.RecordParty = !config.RecordParty;
            config.Save();
        }
        if (SwitchRow(i++, "##smooth", "Smoother replays", "Places four times a second, not two: bigger files", config.SmoothReplays,
                "Where everyone stands is kept four times a second instead of two, so the site's replay of the pull moves more smoothly. Files grow by about 30 KB a minute of an eight-player pull; a pull too big to send is thinned back to two first.",
                sub: true, off: !config.RecordParty))
        {
            config.SmoothReplays = !config.SmoothReplays;
            config.Save();
        }
        var delta = StepperRow(i++, "##keep", "Keep the last", "Past that, the oldest sent ones go first", config.Keep.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.Keep > 5, config.Keep < 100,
            "Pulls are kept in the plugin's folder until there are more than this; then, as each new one is kept, the oldest you've sent goes first (the site keeps your newest 30 sends), and only then the oldest you haven't. Lowering it deletes nothing now.");
        if (delta != 0)
        {
            config.Keep = Math.Clamp(config.Keep + delta * 5, 5, 100);
            config.Save();
            // Nothing is deleted on a click: the next pull kept lets the extra ones go.
            store.Keep = config.Keep;
        }
        if (SwitchRow(i++, "##send-each", "Send each pull", "Each kept pull goes to your Raid Planner by itself", config.SendEachPull,
                "Each pull worth keeping is sent to your Raid Planner as soon as it's kept, so it's waiting under From the game. Off, you send the ones you want.",
                off: !Linked, linkFirst: !Linked))
        {
            config.SendEachPull = !config.SendEachPull;
            config.Save();
        }
        if (SwitchRow(i++, "##progress", "Track my progress", "How far each pull got, for Progress on the site", config.TrackProgress,
                "After each kept pull, a few numbers go to your account: the duty, how long it lasted, the furthest phase, the boss's HP left, who died to what and what hit the party (by spot and job, never names). The site charts your progress from them.",
                off: !Linked, linkFirst: !Linked))
        {
            config.TrackProgress = !config.TrackProgress;
            config.Save();
        }
        if (SwitchRow(i++, "##bar", "Show it in the server info bar", "“Rec 2:31” while a pull is recorded", config.ShowInBar,
                "The bar at the top right of the screen shows that a pull is being recorded, and for how long. Click it to open the plugin."))
        {
            config.ShowInBar = !config.ShowInBar;
            config.Save();
        }
        if (ButtonRow(i, "##folder", "Recordings folder", "Where the pulls are kept", "Open", Btn.Outline, false, null, store.Folder))
            Dalamud.Utility.Util.OpenLink(store.Folder);
        Kit.EndPanel();

        // When the game hides something: a warning box in amber, saying what and why.
        if (recorder.HookProblem is { } problem)
        {
            Kit.Gap(12);
            Kit.NoteBox(problem, warn: true);
        }
    }

    private void DrawPlansTab()
    {
        Kit.BeginPanel("##set-plans", 0, 0, 0);
        var i = 0;
        if (SwitchRow(i++, "##plan-show", "Show my plan in duties", "Its window opens in a duty you have a plan for", config.ShowPlanInDuties,
                "In a duty you've made a plan for on the site, its window opens: the step, its board and its notes, moving along with the pull. /raidplan plan shows it anywhere."))
        {
            config.ShowPlanInDuties = !config.ShowPlanInDuties;
            config.Save();
        }
        if (SwitchRow(i++, "##plan-lock", "Lock it where it is", "No moving or resizing it by accident", config.PlanWindowLocked,
                "Locked, the plan window can't be moved or resized (Dalamud's own moving and resizing are off too), and clicking it doesn't bring it to the front."))
        {
            config.PlanWindowLocked = !config.PlanWindowLocked;
            config.Save();
        }
        if (SwitchRow(i++, "##plan-callouts", "Say each step in chat", "Its callout, five seconds before, with a soft sound", config.Callouts,
                "Five seconds before each step's mechanic, its name and its callout (what a raid lead would say) are said in chat, with a soft sound. Once a step, each pull."))
        {
            config.Callouts = !config.Callouts;
            config.Save();
        }
        if (SwitchRow(i++, "##plan-share", "Share my step with the site", "Present on the site can follow along", config.ShareStep,
                "Each time the plan window's step changes, the site hears which plan and step, so Present on kokos-tools.com (on your stream, or a second screen) can follow the fight. Only the plan and the step are sent.",
                off: !Linked, linkFirst: !Linked))
        {
            config.ShareStep = !config.ShareStep;
            config.Save();
        }
        if (ButtonRow(i++, "##plan-window", "Plan window", plans.Plan is { } p ? p.Name : "Your plan, anywhere on screen",
                PlanWindowOpen?.Invoke() == true ? "Hide" : "Show", Btn.Outline))
            TogglePlanWindow?.Invoke();
        if (ButtonRow(i, "##roster", "Send my party to my roster", "Their names, by spot, to your Raid Planner",
                uploads.RosterSending ? "Sending…" : "Send", Btn.Outline, !Linked || uploads.RosterSending, null,
                "Your party's names go to your roster on the site, each in their spot (MT, OT, H1…), so your plans show who stands where. It replaces the roster there. Only when you press this.",
                off: !Linked, linkFirst: !Linked))
        {
            uploads.SendRoster((text, ok) => rosterNote = (text, ok, DateTime.UtcNow));
        }
        Kit.EndPanel();
        Kit.Gap(12);
        if (rosterNote is { } note && DateTime.UtcNow - note.At < TimeSpan.FromMinutes(1))
        {
            if (note.Ok)
            {
                var at = Kit.At;
                Kit.Reserve(at, Kit.Width, SentLine(at, Kit.Width, note.Text));
            }
            else Kit.NoteBox(note.Text);
            Kit.Gap(12);
        }
        Txt.Note("Send my party sends names to your own account only, and only when you press it. Nothing else the plugin sends has names in it.");
        Kit.Gap(18);
        DrawCalloutSettings();
    }

    /// <summary>
    /// Plan window → On-screen callouts (0.4.0): show them, how far ahead and how many, the sound (on,
    /// which of the game's, how early, and a button to hear it), the window's lock, click-through, Grows,
    /// compact and background, and a sample to place it.
    /// </summary>
    private void DrawCalloutSettings()
    {
        var o = config.CalloutOverlay;
        Kit.Section("##sec-callouts", "On-screen callouts", "Your plan's next steps, over the fight");
        Kit.Gap(10);
        Kit.BeginPanel("##set-callouts", 0, 0, 0);
        var i = 0;
        if (SwitchRow(i++, "##co-show", "Show the callouts", "While your plan follows a pull. /raidplan callouts too", o.Show,
                "While your plan follows a pull, its next steps' callouts sit over the fight, the next one big, each counting down to when it goes off (the plan's own times, kept to the fight by its cues).",
                pad: 10))
        {
            o.Show = !o.Show;
            config.Save();
        }
        var ahead = StepperRow(i++, "##co-ahead", "How far ahead", "Steps going off within this", $"{config.CalloutAhead} s", config.CalloutAhead > 5, config.CalloutAhead < 60, pad: 10);
        if (ahead != 0)
        {
            config.CalloutAhead = Math.Clamp(config.CalloutAhead + ahead * 5, 5, 60);
            config.Save();
        }
        var count = StepperRow(i++, "##co-count", "How many", "Steps shown at once", config.CalloutCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.CalloutCount > 1, config.CalloutCount < 5, pad: 10);
        if (count != 0)
        {
            config.CalloutCount = Math.Clamp(config.CalloutCount + count, 1, 5);
            config.Save();
        }
        if (SwitchRow(i++, "##co-sound", "A sound before each step", "One of the game's own, as <se.N> in a macro", config.CalloutSound,
                "A few seconds before each step goes off, the game plays one of its own sound effects (the ones a macro's <se.1> to <se.16> play), once a step, each pull. It follows your game's sound settings.",
                pad: 10))
        {
            config.CalloutSound = !config.CalloutSound;
            config.Save();
        }
        var which = StepperRow(i++, "##co-which", "Which sound", "The game's numbering", $"<se.{config.CalloutSoundId}>", config.CalloutSoundId > 1, config.CalloutSoundId < 16, pad: 10);
        if (which != 0)
        {
            config.CalloutSoundId = Math.Clamp(config.CalloutSoundId + which, 1, 16);
            config.Save();
            PlaySound?.Invoke(config.CalloutSoundId);
        }
        var lead = StepperRow(i++, "##co-lead", "How early", "Before the step goes off", $"{config.CalloutSoundLead} s", config.CalloutSoundLead > 1, config.CalloutSoundLead < 10, pad: 10);
        if (lead != 0)
        {
            config.CalloutSoundLead = Math.Clamp(config.CalloutSoundLead + lead, 1, 10);
            config.Save();
        }
        if (ButtonRow(i++, "##co-hear", "Hear it", "Plays it now", "Play", Btn.Outline, PlaySound == null, null, null, icon: Icon.Volume, pad: 10))
            PlaySound?.Invoke(config.CalloutSoundId);
        OverlayRows(o, "co", ref i, "Locked, the callouts have no title bar and can't be moved or sized.");
        var sampleOn = CalloutsSampleOn?.Invoke() == true;
        if (ButtonRow(i, "##co-sample", "See them", sampleOn ? "Made-up steps under a Preview tag" : "Made-up steps counting down, to place them",
                sampleOn ? "Stop the sample" : "Show a sample", Btn.Outline, ToggleCalloutsSample == null, null,
                "A few made-up steps counting down and starting again, as the callouts show your plan's: to place and size them without a pull.", pad: 10))
            ToggleCalloutsSample?.Invoke();
        Kit.EndPanel();
        Kit.Gap(12);
        Txt.Note("The callouts follow the plan window's own clock: a plan made from a pull on the site carries when each step's mechanic went off.");
    }

    /// <summary>An overlay's own rows: Lock it where it is, Click-through when locked, Grows, Compact, Background.</summary>
    private void OverlayRows(OverlaySettings o, string id, ref int i, string lockTip)
    {
        if (SwitchRow(i++, $"##{id}-lock", "Lock it where it is", null, o.Locked, lockTip, pad: 10))
        {
            o.Locked = !o.Locked;
            config.Save();
        }
        if (SwitchRow(i++, $"##{id}-through", "Click-through when locked", "The mouse goes through to the game", o.ClickThrough,
                "It doesn't take the mouse at all while it's locked: its command with unlock, or this, gives it back.", sub: true, pad: 10, off: !o.Locked))
        {
            o.ClickThrough = !o.ClickThrough;
            config.Save();
        }
        var grows = SegmentRow(i++, $"##{id}-grows", "Grows", "Which edge stays put as it grows", GrowsItems, (int)o.Grows,
            tip: "Down: its top edge stays where you put it and it grows downwards. Up: its foot stays, and it grows upwards.");
        if (grows != (int)o.Grows)
        {
            o.Grows = (MeterGrows)grows;
            config.Save();
        }
        if (SwitchRow(i++, $"##{id}-compact", "Compact", "Smaller rows and words", o.Compact, null, pad: 10))
        {
            o.Compact = !o.Compact;
            config.Save();
        }
        var bg = Math.Clamp(SliderRow(i++, $"##{id}-bg", "Background", "Its words stay solid", Math.Clamp(o.Background, 0, 1)), 0, 1);
        if (MathF.Abs(bg - o.Background) > 0.001f)
        {
            o.Background = bg;
            backgroundDirty = true;
        }
        if (backgroundDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            backgroundDirty = false;
            config.Save();
        }
    }

    /// <summary>
    /// Cooldowns (0.4.0): the tracker (show it, where, yours too, those ready, the limit break gauge,
    /// its window), a sample, then each kind's cooldowns with a switch each (the kind's head turns them
    /// all on or off).
    /// </summary>
    private void DrawCooldownsTab()
    {
        var o = config.Cooldowns;
        Kit.BeginPanel("##set-cooldowns", 0, 0, 0);
        var i = 0;
        if (SwitchRow(i++, "##cd-show", "Show the tracker", "The party's cooldowns over the fight. /raidplan cooldowns too", o.Show,
                "Small rows of your party's raid buffs, mitigation and the rest you pick below, each with the time until it's ready: yours from the game's own timers, the others' worked out from when they used them.",
                pad: 10))
        {
            o.Show = !o.Show;
            config.Save();
        }
        var where = SegmentRow(i++, "##cd-where", "Where", null, WhereItems, o.Everywhere ? 1 : 0, sub: true, off: !o.Show);
        if (where != (o.Everywhere ? 1 : 0))
        {
            o.Everywhere = where == 1;
            config.Save();
        }
        if (SwitchRow(i++, "##cd-mine", "Yours too", "From the game's own timers, exactly", config.CooldownsMine,
                "Your own cooldowns are read from the game's timers (as your hotbars show them). Off, only the rest of the party's are listed.", pad: 10))
        {
            config.CooldownsMine = !config.CooldownsMine;
            config.Save();
        }
        if (SwitchRow(i++, "##cd-hide", "Hide those ready", "Only what's recharging is listed", config.CooldownsHideReady,
                "Off, every tracked cooldown has its row (\"Ready\" in green when it is). On, a row comes when it's used and goes when it's back.", pad: 10))
        {
            config.CooldownsHideReady = !config.CooldownsHideReady;
            config.Save();
        }
        if (SwitchRow(i++, "##cd-gauge", "Limit break gauge", "Its bars as the last row", config.CooldownsGauge,
                "The party's limit break bars as the tracker's last row, when the party has a gauge (it's read with the meter's numbers).", pad: 10))
        {
            config.CooldownsGauge = !config.CooldownsGauge;
            config.Save();
        }
        OverlayRows(o, "cd", ref i, "Locked, the tracker has no title bar and can't be moved or sized.");
        var sampleOn = CooldownsSampleOn?.Invoke() == true;
        if (ButtonRow(i, "##cd-sample", "See it", sampleOn ? "A made-up party under a Preview tag" : "A made-up party's cooldowns, to place it",
                sampleOn ? "Stop the sample" : "Show a sample", Btn.Outline, ToggleCooldownsSample == null, null,
                "A made-up party's cooldowns recharging and coming back, with the ones you've picked: to place and size the tracker without a party.", pad: 10))
            ToggleCooldownsSample?.Invoke();
        Kit.EndPanel();

        foreach (var kind in CooldownCatalog.Kinds)
        {
            Kit.Gap(18);
            var (on, of) = CooldownCatalog.Count(kind, config.CooldownPicks);
            var all = on == of;
            if (Kit.Section("##cd-kind" + (int)kind, CooldownCatalog.KindWord(kind), $"{on} of {of}", all ? "None" : "All"))
            {
                foreach (var spec in CooldownCatalog.All)
                    if (spec.Kind == kind)
                        config.CooldownPicks[spec.Key] = !all;
                config.Save();
            }
            Kit.Gap(10);
            Kit.BeginPanel("##set-cd" + (int)kind, 0, 0, 0);
            var j = 0;
            foreach (var spec in CooldownCatalog.All)
            {
                if (spec.Kind != kind) continue;
                var picked = CooldownCatalog.IsOn(spec, config.CooldownPicks);
                if (SwitchRow(j++, "##cd-" + spec.Key, spec.Key, string.Join(", ", spec.Jobs), picked, null, pad: 8))
                {
                    config.CooldownPicks[spec.Key] = !picked;
                    config.Save();
                }
            }
            Kit.EndPanel();
        }
        Kit.Gap(12);
        Txt.Note("The others' times are worked out from when they used each one, by its recast (learned from their own uses when two come closer than the game's base). Yours are the game's own. A new zone, or the duty starting again after a wipe, sets them all back to ready.");
    }

    private void DrawMeterTab()
    {
        var meter = Meter?.Invoke();
        Kit.BeginPanel("##set-meter", 0, 0, 0);
        var i = 0;
        if (SwitchRow(i++, "##meter-show", "Show the meter", "Its own window, over the game. /raidplan meter too", config.ShowMeter,
                "Damage, healing, damage taken and deaths for your party, worked out in the game from what it already tells the plugin. Its numbers and names stay in the plugin.", pad: 10))
        {
            config.ShowMeter = !config.ShowMeter;
            config.Save();
        }
        var where = SegmentRow(i++, "##meter-where", "Where", null, new[] { "In duties", "Everywhere" }, config.MeterEverywhere ? 1 : 0, sub: true, off: !config.ShowMeter);
        if (where != (config.MeterEverywhere ? 1 : 0))
        {
            config.MeterEverywhere = where == 1;
            config.Save();
        }
        if (SwitchRow(i++, "##meter-hide", "Hide out of combat", "Folded to one line otherwise", config.MeterHideOutOfCombat, null, sub: true, pad: 10, off: !config.ShowMeter))
        {
            config.MeterHideOutOfCombat = !config.MeterHideOutOfCombat;
            config.Save();
        }
        var rank = SegmentRow(i++, "##meter-rank", "The number", "ENCDPS: over the fight's time. DPS: over each player's own.", new[] { "ENCDPS", "DPS" }, (int)config.MeterRank);
        if (rank != (int)config.MeterRank)
        {
            config.MeterRank = (MeterRank)rank;
            config.Save();
        }
        // The meter always keeps pets in their owners' rows and the limit break in its own, as ACT and FF Logs do.
        SwitchRow(i++, "##meter-pets", "Pets in their owners' rows", "Fairies, demi-summons, the automaton, Living Shadow", true,
            "A pet's or summon's hits and heals count for whoever it belongs to.", pad: 10, off: true, why: "Always, as ACT counts them.");
        SwitchRow(i++, "##meter-lb", "Limit break on its own row", null, true,
            "The limit break isn't anyone's damage: it has its own row, and counts in the party's number.", pad: 10, off: true, why: "Always, as ACT and FF Logs keep it.");
        var names = SegmentRow(i++, "##meter-names", "Names", "Kept in the plugin, never sent", new[] { "Names", "Jobs only" }, config.MeterJobsOnly ? 1 : 0);
        if (names != (config.MeterJobsOnly ? 1 : 0))
        {
            config.MeterJobsOnly = names == 1;
            config.Save();
        }
        if (SwitchRow(i++, "##meter-lock", "Lock it where it is", null, config.MeterLocked,
                "Locked, the meter has no title bar and can't be moved or sized; its tabs and the picker still answer the mouse.", pad: 10))
        {
            config.MeterLocked = !config.MeterLocked;
            config.Save();
        }
        if (SwitchRow(i++, "##meter-through", "Click-through when locked", "The mouse goes through to the game", config.MeterClickThrough,
                "The meter doesn't take the mouse at all while it's locked: /raidplan meter unlock, or this, gives it back.", sub: true, pad: 10, off: !config.MeterLocked))
        {
            config.MeterClickThrough = !config.MeterClickThrough;
            config.Save();
        }
        // Which edge stays put when its height changes by itself (folding, opening, a bigger party, compact).
        var grows = SegmentRow(i++, "##meter-grows", "Grows", "Which edge stays put as it folds and opens", GrowsItems, (int)config.MeterGrows,
            tip: "Down: its top edge stays where you put it, with the clock and its buttons on top, and it grows downwards. Up: its foot stays, with the clock and its buttons at the foot and the rows reading up from them, and it grows upwards (for a meter sitting on your hotbars).");
        if (grows != (int)config.MeterGrows)
        {
            config.MeterGrows = (MeterGrows)grows;
            config.Save();
        }
        var gauge = SegmentRow(i++, "##meter-gauge", "Limit break gauge", "Its bars, while a fight is on", GaugeItems, (int)config.MeterGauge,
            tip: "The party's limit break bars, in the meter's head (after the clock, as room allows) or its foot (before what it says on the right), while a fight is on. Off, nowhere.");
        if (gauge != (int)config.MeterGauge)
        {
            config.MeterGauge = (GaugePlace)gauge;
            config.Save();
        }
        if (SwitchRow(i++, "##meter-compact", "Compact", "Smaller rows, the tabs in a menu by the lock", config.MeterCompact,
                "20px rows with smaller words and the bars behind them, and the four tabs as one menu beside the lock: the party in less room. The meter's own menu (the pull picker) has this switch too. The opened-up window stays as it is.",
                pad: 10))
        {
            config.MeterCompact = !config.MeterCompact;
            config.Save();
        }
        // 20% at the least, as the meter draws it (below that its edge and words would float on nothing).
        var bg = Math.Clamp(SliderRow(i++, "##meter-bg", "Background", "Rows stay solid", Math.Clamp(config.MeterBackground, 0.2f, 1f)), 0.2f, 1f);
        if (MathF.Abs(bg - config.MeterBackground) > 0.001f)
        {
            config.MeterBackground = bg;
            backgroundDirty = true;
        }
        // Saved once the knob is let go, not on every frame it's dragged.
        if (backgroundDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            backgroundDirty = false;
            config.Save();
        }
        // The meter mid-fight with made-up numbers, to place and size it (and see compact and Grows) without a pull.
        var sampleOn = SampleOn?.Invoke() == true;
        var waits = !sampleOn && SampleWaits?.Invoke() == true;
        if (ButtonRow(i++, "##meter-sample", "See it in a fight",
                waits ? "After this fight: a real one ends a sample" : sampleOn ? "Made-up numbers under a Preview tag, until a fight starts" : "A made-up pull, its clock running, under a Preview tag",
                sampleOn ? "Stop the sample" : "Show a sample fight", Btn.Outline, waits || ToggleSample == null, null,
                "Eight players mid-pull, with a limit break, a death, pace and the clock running: the meter as it looks in a fight, to place and size it. Nothing in it is real, it's never mixed with real numbers, and a real fight ends it at once.",
                pad: 10))
            ToggleSample?.Invoke();
        var end = StepperRow(i++, "##meter-end", "A fight ends after", "Nobody in combat for this long", $"{config.MeterEndAfter} s", config.MeterEndAfter > 2, config.MeterEndAfter < 15, pad: 10);
        if (end != 0)
        {
            config.MeterEndAfter = Math.Clamp(config.MeterEndAfter + end, 2, 15);
            config.Save();
        }
        var keep = StepperRow(i++, "##meter-keep", "Keep the last", "Pulls to pick from and pace against", config.MeterKeep.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.MeterKeep > 5, config.MeterKeep < 50, pad: 10);
        if (keep != 0)
        {
            config.MeterKeep = Math.Clamp(config.MeterKeep + keep * 5, 5, 50);
            config.Save();
        }
        var pace = SegmentRow(i++, "##meter-pace", "Pace against", "Damage tab only, from 10 s into a phase", new[] { "Best", "Last", "Off" }, (int)config.MeterPace);
        if (pace != (int)config.MeterPace)
        {
            config.MeterPace = (PaceAgainst)pace;
            config.Save();
        }
        // ACT's numbers (IINACT over OverlayPlugin's local connection) aren't read by this version.
        SwitchRow(i++, "##meter-act", "Use ACT's numbers", "When IINACT is running, show its numbers instead", false, null, pad: 10, off: true, why: "Not in this version yet.");
        if (ButtonRow(i, "##meter-clear", "Kept fights", meter is { } m ? Words.Count(m.Kept.Count, "fight") + " kept for the picker" : "None yet", "Clear", Btn.Danger, meter == null || meter.Kept.Count == 0,
                null, "Forget every fight the meter kept this session (the picker's pulls and pace's best).", pad: 10))
            meter?.Clear();
        Kit.EndPanel();

        // After a patch: it turns itself off rather than lie.
        if (MeterProblem?.Invoke() is { } problem)
        {
            Kit.Gap(12);
            Kit.NoteBox(problem, warn: true);
        }
        Kit.Gap(12);
        Txt.Note("The meter's numbers and names stay in the plugin. The pull you send carries the party's numbers by spot and job, never by name.");
    }

    private void DrawAccountTab()
    {
        Kit.BeginPanel("##set-account", 0, 0, 0);
        DrawAccountRow(0);
        if (ButtonRow(1, "##planner", "The Raid Planner", "Make plans from your pulls, draw your strats, present them", "Open", Btn.Outline, false, null,
                "kokos-tools.com/raid-planner", icon: Icon.ExternalLink))
            Dalamud.Utility.Util.OpenLink(SiteLink.PlannerUrl);
        Kit.EndPanel();
        Kit.Gap(12);
        Txt.Note("Anyone with the token can send pulls to your account, so keep it to yourself. Revoke it on the site if it gets out.");
    }

    // ---------------------------------------------------------------------------------------------
    // Rows.

    private static readonly Run[] SetNote = new Run[1];

    /// <summary>
    /// A setting's row (`.set`: 11px 16px inside, a hairline between, 34 at the left for a sub-row): its
    /// name (550 14px white) and one muted line under it (12px), the control at the right, 14px from
    /// the words. A row that's off dims its words and control to 45%, and with <paramref name="linkFirst"/>
    /// says "Link your account first: Account" (the link goes there), or <paramref name="why"/>. The
    /// longer words are the tooltip over the words.
    /// </summary>
    private void SetRow(int index, string name, string? note, float controlW, float controlH, Action<Vector2> control, string? tip = null, bool off = false,
        bool sub = false, bool linkFirst = false, string? why = null, float pad = 11)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        if (index > 0) Kit.Rule(dl, at.X, at.X + w, at.Y - 0.5f);
        var x = at.X + S(sub ? 34 : 16);
        var wordsW = at.X + w - S(16) - (controlW > 0 ? controlW + S(14) : 0) - x;
        var nameH = S(14 * 1.3f);
        var noteH = 0f;
        if (note != null)
        {
            SetNote[0] = new Run(note, Face.Sans, 12, Palette.Muted);
            noteH = S(2) + Txt.Flow(Vector2.Zero, wordsW, SetNote, 17.4f, out _, draw: false);
        }
        var whyText = linkFirst ? "Link your account first: " : why;
        var whyH = whyText != null ? S(2) + S(17.4f) : 0;
        var wordsH = nameH + noteH + whyH;
        var h = S(pad) * 2 + MathF.Max(wordsH, controlH);
        var top = at.Y + (h - wordsH) / 2;
        var old = Palette.Opacity;
        if (off) Palette.Opacity = old * 0.45f;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(name, Face.SansMedium, 14, wordsW), Face.SansMedium, 14, Palette.White, 14 * 1.3f);
        if (note != null)
        {
            SetNote[0] = new Run(note, Face.Sans, 12, Palette.Muted);
            Txt.Flow(new Vector2(x, top + nameH + S(2)), wordsW, SetNote, 17.4f, out _);
        }
        Palette.Opacity = old;
        if (whyText != null)
        {
            // The reason stays bright (`.set .why`), its link underlined.
            var wy = top + nameH + noteH + S(2);
            var ww = Txt.Line(dl, new Vector2(x, wy), whyText, Face.Sans, 12, Palette.Text, 17.4f);
            if (linkFirst && Kit.Link($"##why{name}", new Vector2(x + ww, wy + (S(17.4f) - Txt.Natural(Face.SansMedium, 12)) / 2), "Account", null, 12))
                settingsTab = SettingsTab.Account;
        }
        if (tip != null && Kit.Pointed(new Vector2(x, top), new Vector2(x + wordsW, top + nameH + noteH))) Kit.Tooltip(tip);
        if (controlW > 0)
        {
            if (off) Palette.Opacity = old * 0.45f;
            control(new Vector2(at.X + w - S(16) - controlW, at.Y + (h - controlH) / 2));
            Palette.Opacity = old;
        }
        Kit.Reserve(at, w, h);
    }

    /// <summary>A setting with a switch; true when it's flipped (never while it's off).</summary>
    private bool SwitchRow(int index, string id, string name, string? note, bool on, string? tip = null, bool off = false, bool sub = false, bool linkFirst = false,
        string? why = null, float pad = 11)
    {
        var flipped = false;
        var size = Kit.SwitchSize;
        SetRow(index, name, note, size.X, size.Y, p => flipped = Kit.Switch(id, p, on, off), tip, off, sub, linkFirst, why, pad);
        return flipped && !off;
    }

    /// <summary>A setting with a − value + stepper (40 tall, the value in mono); returns −1, 0 or +1.</summary>
    private int StepperRow(int index, string id, string name, string? note, string value, bool canDown, bool canUp, string? tip = null, float pad = 11)
    {
        var delta = 0;
        SetRow(index, name, note, Kit.StepperWidth(value), S(H), p => delta = Kit.Stepper(id, p, value, canDown, canUp), tip, pad: pad);
        return delta;
    }

    /// <summary>A setting with a dense segmented control; returns the item picked.</summary>
    private int SegmentRow(int index, string id, string name, string? note, string[] items, int selected, bool sub = false, bool off = false, string? tip = null)
    {
        var picked = selected;
        SetRow(index, name, note, Kit.SegmentedWidth(items), S(HSm), p => picked = Kit.Segmented(id, p, items, selected, true, off), tip, off, sub, pad: 10);
        return off ? selected : picked;
    }

    /// <summary>A setting with a slider and its value in mono (150 wide together); returns the value.</summary>
    private float SliderRow(int index, string id, string name, string? note, float value)
    {
        var result = value;
        var text = Format.PercentWhole(value);
        var vw = Txt.Width("100%", Face.Mono, 13);
        SetRow(index, name, note, S(150), S(20), p =>
        {
            var sw = S(150) - vw - S(12);
            result = Kit.Slider(id, p, sw, value);
            Txt.LineRight(Kit.Dl, p.X + S(150), p.Y, Format.PercentWhole(result), Face.Mono, 13, Palette.White, 20);
        }, pad: 10);
        _ = text;
        return result;
    }

    /// <summary>A setting with a dense button on the right; true when it's pressed.</summary>
    private bool ButtonRow(int index, string id, string name, string? note, string label, Btn style = Btn.Outline, bool disabled = false,
        string? buttonTip = null, string? tip = null, bool off = false, bool linkFirst = false, Icon? icon = null, float pad = 11)
    {
        var pressed = false;
        SetRow(index, name, note, Kit.ButtonWidth(label, BtnSize.Sm, icon, style), S(HSm),
            p => pressed = Kit.Button(id, p, label, style, BtnSize.Sm, icon, 0, disabled, buttonTip), tip, off, false, linkFirst, null, pad);
        return pressed;
    }
}
