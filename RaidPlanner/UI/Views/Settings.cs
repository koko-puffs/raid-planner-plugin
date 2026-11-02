using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>Settings' tabs: how pulls are recorded, your plans in the game, and the site.</summary>
public enum SettingsTab
{
    Recording,
    Plans,
    Account,
}

/// <summary>
/// Settings, laid out as Housing Studio's plugin lays its own: the back arrow at the top left, the
/// title, the tabs under it, and a card of rows, each a setting's name, one line under it, and its
/// control on the right (the longer words are its tooltip). Recording holds how pulls are recorded
/// and what's sent of them; Plans, your plans in the game; Account links the plugin to the site.
/// The version and What's new at the foot.
/// </summary>
public sealed partial class MainWindow
{
    private const string ReleasesUrl = "https://github.com/koko-puffs/raid-planner-plugin/releases";

    private static readonly TabSpec[] SettingsTabs = { new("Recording"), new("Plans"), new("Account") };

    /// <summary>How Send my party to my roster went, and when (shown for a while under the card).</summary>
    private (string Text, bool Ok, DateTime At)? rosterNote;

    private SettingsTab settingsTab;

    /// <summary>Where Settings was opened from: its back arrow goes there.</summary>
    private Screen settingsFrom = Screen.Home;

    private void OpenSettings(SettingsTab tab)
    {
        settingsTab = tab;
        if (screen != Screen.Settings) settingsFrom = screen;
        screen = Screen.Settings;
    }

    private void DrawSettings()
    {
        Frame.BeginHeader(20);
        if (ScreenHeader(settingsFrom == Screen.Pull ? "Back to the pull" : "Back", null, "Settings"))
            screen = settingsFrom == Screen.Pull && openFile != null && store.Find(openFile) != null ? Screen.Pull : Screen.Home;
        Kit.Gap(16);
        settingsTab = (SettingsTab)Kit.Tabs("##settings-tab", SettingsTabs, (int)settingsTab);
        Frame.EndHeader();

        Frame.BeginBody(settingsTab switch
        {
            SettingsTab.Account => "##settings-account",
            SettingsTab.Plans => "##settings-plans",
            _ => "##settings-recording",
        }, null, 18, 22);
        switch (settingsTab)
        {
            case SettingsTab.Account:
                DrawAccountTab();
                break;
            case SettingsTab.Plans:
                DrawPlansTab();
                break;
            default:
                DrawRecordingTab();
                break;
        }

        Frame.BeginBottom("settings-bottom");
        var runs = new[]
        {
            new Run(VersionLine, Face.Sans, 13, Palette.Faint),
            new Run("What's new", Face.SansMedium, 13, Palette.Light, Link: "news"),
        };
        if (Txt.Para(runs, 19.5f, center: true) == "news") Dalamud.Utility.Util.OpenLink(ReleasesUrl);
        Frame.EndBottom();
        Frame.EndBody();
    }

    /// <summary>"Raid Planner 0.1.0 · ", read once (it's a reflection call).</summary>
    private static readonly string VersionLine = typeof(MainWindow).Assembly.GetName().Version is { } v
        ? $"Raid Planner {v.Major}.{v.Minor}.{v.Build} · "
        : "Raid Planner · ";

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
                "Off, only fights are recorded (trials, raids, ultimates, criterion dungeons), so a run of trash packs never pushes out the pulls you want. On, every duty's pulls are."))
        {
            config.RecordAllDuties = !config.RecordAllDuties;
            config.Save();
        }
        if (SwitchRow(i++, "##party", "Record where the party stands",
                config.RecordParty ? "Your party's places come along into the plan" : "Just the bosses and mechanics: place the party yourself",
                config.RecordParty,
                "On, the plan shows where everyone stood at each mechanic (you can still swap to standard spots on the site). Off, only the enemies and their attacks are kept, for when you'd rather work out your own strats. Names are never kept, only jobs."))
        {
            config.RecordParty = !config.RecordParty;
            config.Save();
        }
        var keepWords = Words.Count(config.Keep, "pull");
        var delta = StepperRow(i++, "##keep", "Keep the last", "Past that, the oldest sent ones go first", keepWords, config.Keep > 5, config.Keep < 100,
            "Pulls are kept in the plugin's folder until there are more than this; then, as each new one is kept, the oldest you've sent goes first (the site keeps your newest 30 sends), and only then the oldest you haven't. Lowering it deletes nothing now.");
        if (delta != 0)
        {
            config.Keep = Math.Clamp(config.Keep + delta * 5, 5, 100);
            config.Save();
            // Nothing is deleted on a click: the next pull kept lets the extra ones go.
            store.Keep = config.Keep;
        }
        if (SwitchRow(i++, "##send-each", "Send each pull", Linked ? "Each kept pull goes to your Raid Planner by itself" : "Link your account first (Account)",
                config.SendEachPull,
                "Each pull worth keeping is sent to your Raid Planner as soon as it's kept, so it's waiting under From the game. Off, you send the ones you want.",
                !Linked))
        {
            config.SendEachPull = !config.SendEachPull;
            config.Save();
        }
        if (SwitchRow(i++, "##progress", "Track my progress", Linked ? "How far each pull got, for the site's progress tracker" : "Link your account first (Account)",
                config.TrackProgress,
                "After each kept pull, a few numbers go to your account: the duty, how long it lasted, the furthest phase, and who died to what (by spot and job, never names). The site charts your progress from them.",
                !Linked))
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
        if (ButtonRow(i, "##folder", "Recordings folder", "Where the pulls are kept", "Open", Btn.Glass, false, null, store.Folder))
            Dalamud.Utility.Util.OpenLink(store.Folder);
        Kit.EndPanel();

        if (recorder.HookProblem is { } problem)
        {
            Kit.Gap(14);
            Txt.Note(problem, Palette.EmberText);
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
        if (SwitchRow(i++, "##plan-callouts", "Callouts", "Each step's notes in chat, a few seconds before", config.Callouts,
                "A few seconds before each step's mechanic, its name and the first line of its notes are said in chat, with a soft sound. Once a step, each pull."))
        {
            config.Callouts = !config.Callouts;
            config.Save();
        }
        if (SwitchRow(i++, "##plan-share", "Share my step with the site", "Present on the site can follow along", config.ShareStep,
                "Each time the plan window's step changes, the site hears which plan and step, so Present on kokos-tools.com (on your stream, or a second screen) can follow the fight. Only the plan and the step are sent.",
                !Linked))
        {
            config.ShareStep = !config.ShareStep;
            config.Save();
        }
        if (ButtonRow(i++, "##plan-window", "Plan window", plans.Plan is { } p ? p.Name : "Your plan, anywhere on screen",
                PlanWindowOpen?.Invoke() == true ? "Hide" : "Show", Btn.Glass))
            TogglePlanWindow?.Invoke();
        if (ButtonRow(i, "##roster", "Send my party to my roster", "Their names, by spot, to your Raid Planner",
                uploads.RosterSending ? "Sending…" : "Send", Btn.Glass, !Linked || uploads.RosterSending,
                Linked ? null : "Link your account first",
                "Your party's names go to your roster on the site, each in their spot (MT, OT, H1…), so your plans show who stands where. It replaces the roster there. Only when you press this."))
        {
            uploads.SendRoster((text, ok) => rosterNote = (text, ok, DateTime.UtcNow));
        }
        Kit.EndPanel();
        Kit.Gap(14);
        if (rosterNote is { } note && DateTime.UtcNow - note.At < TimeSpan.FromMinutes(1))
        {
            Txt.Note(note.Text, note.Ok ? Palette.Success : Palette.Danger);
            Kit.Gap(8);
        }
        Txt.Note("Send my party sends names to your own account only, and only when you press it. Nothing else the plugin sends has names in it.", Palette.Faint);
        if (!Linked)
        {
            Kit.Gap(8);
            if (Txt.Para(new[] { Run.Note("Your plans come from your Raid Planner account. "), new Run("Link it", Face.SansMedium, 13, Palette.Ink, Link: "link") }) == "link")
                settingsTab = SettingsTab.Account;
        }
    }

    private void DrawAccountTab()
    {
        Kit.BeginPanel("##set-account", 0, 0, 0);
        DrawAccountRow(0);
        if (ButtonRow(1, "##planner", "The Raid Planner", "Make plans from your pulls, draw your strats, present them", "Open", Btn.Glass, false, null,
                "kokos-tools.com/raid-planner"))
            Dalamud.Utility.Util.OpenLink(SiteLink.PlannerUrl);
        Kit.EndPanel();
        Kit.Gap(14);
        Txt.Note("Anyone with the token can send pulls to your account, so keep it to yourself. Revoke it on the site if it gets out.", Palette.Faint);
    }

    // ---------------------------------------------------------------------------------------------
    // Rows.

    private static readonly Run[] SetNote = new Run[1];

    /// <summary>
    /// A setting's row (12px 16px inside, a hairline between): its name (with an icon before it when
    /// given) and one line under it, the control on the right; the longer words are the tooltip over
    /// the words. <paramref name="below"/> puts the control under the words instead, for wide ones.
    /// </summary>
    private static void SetRow(int index, string name, string? note, float controlW, float controlH, Action<Vector2> control, bool below = false,
        string? tip = null, bool disabled = false, Icon? icon = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        if (index > 0) Kit.Dl.AddLine(new Vector2(at.X + 1, at.Y - 0.5f), new Vector2(at.X + w - 1, at.Y - 0.5f), Palette.U32(Palette.Line), 1f);
        var x = at.X + S(16);
        var wordsW = below || controlW <= 0 ? w - S(32) : w - S(32) - controlW - S(14);
        var nameH = Txt.Natural(Face.SansMedium, 14);
        if (note != null) SetNote[0] = new Run(note, Face.Sans, 12, Palette.Muted);
        var noteH = note != null ? Txt.Flow(Vector2.Zero, wordsW, SetNote, 18, out _, draw: false) : 0;
        var wordsH = nameH + (note != null ? S(2) + noteH : 0);
        var h = below ? S(13) * 2 + wordsH + (controlH > 0 ? S(10) + controlH : 0) : S(13) * 2 + MathF.Max(wordsH, controlH);
        var dl = Kit.Dl;
        var top = below ? at.Y + S(13) : at.Y + (h - wordsH) / 2;
        var old = Palette.Opacity;
        if (disabled) Palette.Opacity = old * 0.5f;
        var nx = x;
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(nx, top + (nameH - S(14)) / 2), S(14), Palette.Ember);
            nx += S(14 + 6);
        }
        Txt.Line(dl, new Vector2(nx, top), Txt.Fit(name, Face.SansMedium, 14, x + wordsW - nx), Face.SansMedium, 14, Palette.Ink);
        if (note != null)
        {
            SetNote[0] = new Run(note, Face.Sans, 12, Palette.Muted);
            Txt.Flow(new Vector2(x, top + nameH + S(2)), wordsW, SetNote, 18, out _);
        }
        Palette.Opacity = old;
        if (tip != null && !Kit.Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(x, top), new Vector2(x + wordsW, top + wordsH)))
            Kit.Tooltip(tip);
        if (below) control(new Vector2(x, top + wordsH + S(10)));
        else if (controlW > 0) control(new Vector2(at.X + w - S(16) - controlW, at.Y + (h - controlH) / 2));
        Kit.Reserve(at, w, h);
    }

    /// <summary>A setting with a switch; true when it's flipped.</summary>
    private static bool SwitchRow(int index, string id, string name, string? note, bool on, string? tip = null, bool disabled = false)
    {
        var flipped = false;
        SetRow(index, name, note, S(44), S(24), p => flipped = Kit.Switch(id, p, on, disabled), false, tip, disabled);
        return flipped;
    }

    /// <summary>A setting with a − value + stepper; returns −1, 0 or +1.</summary>
    private static int StepperRow(int index, string id, string name, string? note, string value, bool canDown, bool canUp, string? tip = null)
    {
        var delta = 0;
        SetRow(index, name, note, Kit.StepperWidth(value), S(36), p => delta = Kit.Stepper(id, p, value, canDown, canUp), false, tip);
        return delta;
    }

    /// <summary>A setting with a small button on the right; true when it's pressed.</summary>
    private static bool ButtonRow(int index, string id, string name, string? note, string label, Btn style = Btn.Glass, bool disabled = false,
        string? buttonTip = null, string? tip = null)
    {
        var pressed = false;
        SetRow(index, name, note, Kit.ButtonWidth(label, BtnSize.Tiny, null, style), Kit.ButtonHeight(BtnSize.Tiny),
            p => pressed = Kit.Button(id, p, label, style, BtnSize.Tiny, null, 0, disabled, buttonTip), false, tip);
        return pressed;
    }
}
