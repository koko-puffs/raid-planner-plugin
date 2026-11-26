using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Settings (0.8.0, under the window's tabs): a sidebar with Find a setting at its top, the pages with their icons,
/// and the version and What's new at its foot; a narrow window has a page picker and the search above the page
/// instead. Each page: its title as a small callout, one line saying what it's for, Reset this page (it asks first),
/// then its sections (an eyebrow over a slab of rows: a setting's name over one muted line, its control at the
/// right) and its rarely changed settings folded under one "N more: …" row. What's on each page, and the search,
/// are <see cref="SettingsIndex"/>'s; a row is drawn by what it is (<see cref="DrawRow"/>), so a search's answer works in place.
/// </summary>
public sealed partial class MainWindow
{
    private const string ReleasesUrl = "https://github.com/koko-puffs/raid-planner-plugin/releases";

    /// <summary>The window's width (CSS px) from which the pages sit in a sidebar beside the page; narrower, a picker above it.</summary>
    private const float SidebarFrom = 700;

    private const float SidebarWidth = 220;

    /// <summary>The cooldown tracker's sample: on now, and starting or stopping it (set by the plugin).</summary>
    public Func<bool>? CooldownsSampleOn { get; set; }

    public Action? ToggleCooldownsSample { get; set; }

    /// <summary>The callouts' sample: on now, and starting or stopping it (set by the plugin).</summary>
    public Func<bool>? CalloutsSampleOn { get; set; }

    public Action? ToggleCalloutsSample { get; set; }

    /// <summary>Plays one of the game's sound effects (&lt;se.N&gt;), to hear the callouts' (set by the plugin).</summary>
    public Action<int>? PlaySound { get; set; }

    /// <summary>How Send my party to my roster went, and when (shown for a while under its section).</summary>
    private (string Text, bool Ok, DateTime At)? rosterNote;

    private SettingsTab settingsTab;

    /// <summary>A background was dragged and not saved yet (saved once the knob is let go).</summary>
    private bool backgroundDirty;

    /// <summary>Where Settings was opened from (the window's tabs keep it; nothing goes back to it since 0.8.0).</summary>
    private Screen settingsFrom = Screen.Home;

    /// <summary>What's typed in Find a setting.</summary>
    private string settingsQuery = string.Empty;

    /// <summary>The pages whose more row is open (closed at first, for the session).</summary>
    private readonly HashSet<SettingsTab> moreOpen = new();

    private void OpenSettings(SettingsTab tab)
    {
        settingsTab = tab;
        settingsQuery = string.Empty;
        if (screen != Screen.Settings) settingsFrom = screen;
        screen = Screen.Settings;
    }

    /// <summary>Opens Settings on the meter's page (the meter's Settings, /raidcodex meter settings).</summary>
    public void ShowMeterSettings() => ShowSettingsOn(SettingsTab.Meter);

    /// <summary>Opens Settings on a page (/raidcodex cooldowns settings, /raidcodex callouts settings).</summary>
    public void ShowSettingsOn(SettingsTab tab)
    {
        Dismiss();
        LeaveSetup();
        settingsFrom = Screen.Home;
        screen = Screen.Settings;
        settingsTab = tab;
        settingsQuery = string.Empty;
        IsOpen = true;
    }

    /// <summary>Goes to a page (a sidebar item, a card's Settings, a search's Go): the search is cleared.</summary>
    private void GoToPage(SettingsTab tab, bool openMore = false)
    {
        settingsTab = tab;
        settingsQuery = string.Empty;
        if (openMore) moreOpen.Add(tab);
    }

    /// <summary>"0.3.0", read once (it's a reflection call).</summary>
    private static readonly string VersionText = typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    private static readonly string SideVersion = "v" + VersionText;

    private void DrawSettings()
    {
        Frame.BeginHeader();
        NavTabs();
        Frame.EndHeader();

        var win = ImGui.GetWindowPos();
        var winW = ImGui.GetWindowWidth();
        var bottom = win.Y + ImGui.GetWindowHeight();
        var top = ImGui.GetCursorScreenPos().Y;
        if (winW >= S(SidebarFrom)) DrawSettingsWide(win, winW, top, bottom);
        else DrawSettingsNarrow(win, winW, top, bottom);

        // A background slider's value is saved once the knob is let go, not on every frame it's dragged.
        if (backgroundDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            backgroundDirty = false;
            config.Save();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The frame: a sidebar beside the page, or a picker above it.

    private void DrawSettingsWide(Vector2 win, float winW, float top, float bottom)
    {
        var sideW = S(SidebarWidth);
        var dl = Kit.Dl;
        var h = MathF.Max(S(40), bottom - top);
        // The sidebar's slab, to the window's foot (its bottom-left corner the window's), a hairline at its right.
        dl.AddRectFilled(new Vector2(win.X + 1, top), new Vector2(win.X + sideW, bottom - 1), Palette.U32(Palette.Slab),
            MathF.Max(0, ImGui.GetStyle().WindowRounding - 1), ImDrawFlags.RoundCornersBottomLeft);
        dl.AddLine(new Vector2(win.X + sideW - 0.5f, top), new Vector2(win.X + sideW - 0.5f, bottom - 1), Palette.U32(Palette.Line), 1f);

        SettingsParts.BeginPane("##set-side", new Vector2(win.X, top), new Vector2(sideW, h), 12, 16, ImGuiWindowFlags.NoScrollbar);
        DrawSidebar();
        SettingsParts.EndPane(16);

        SettingsParts.BeginPane(PaneId(), new Vector2(win.X + sideW, top), new Vector2(winW - sideW, h), 24, 20);
        DrawSettingsContent();
        SettingsParts.EndPane(24);
    }

    private void DrawSettingsNarrow(Vector2 win, float winW, float top, float bottom)
    {
        // The page picker and the search on a row above the page, as the window's header is laid.
        var at = new Vector2(win.X + S(SidePad), top + S(12));
        var w = winW - S(SidePad) * 2;
        Kit.PushBox(at.X, w);
        ImGui.SetCursorScreenPos(at);
        var pickW = MathF.Floor((w - S(8)) * 0.5f);
        DrawPagePicker(at, pickW);
        DrawFindField(new Vector2(at.X + pickW + S(8), at.Y), w - pickW - S(8));
        Kit.Reserve(at, w, S(H));
        Kit.PopBox();

        var y = at.Y + S(H) + S(12);
        Kit.Dl.AddLine(new Vector2(win.X + 1, y - 0.5f), new Vector2(win.X + winW - 1, y - 0.5f), Palette.U32(Palette.Line), 1f);
        SettingsParts.BeginPane(PaneId(), new Vector2(win.X, y), new Vector2(winW, MathF.Max(S(40), bottom - y)), SidePad, 16);
        DrawSettingsContent();
        // The version and What's new at the foot (the sidebar's, when there's one).
        Kit.Gap(24);
        var p = Kit.At;
        var lw = Kit.LinkWidth("What's new");
        var vw = Txt.Width(VersionText, Face.Mono, 13);
        var dot = " · ";
        var dw = Txt.Width(dot, Face.Sans, 13);
        var x = p.X + (Kit.Width - vw - dw - lw) / 2;
        var lineH = S(19.5f);
        Txt.Line(Kit.Dl, new Vector2(x, p.Y), VersionText, Face.Mono, 13, Palette.Muted, 19.5f);
        Txt.Line(Kit.Dl, new Vector2(x + vw, p.Y), dot, Face.Sans, 13, Palette.Muted, 19.5f);
        if (Kit.Link("##news", new Vector2(x + vw + dw, p.Y + (lineH - Txt.Natural(Face.SansMedium, 13)) / 2), "What's new")) Dalamud.Utility.Util.OpenLink(ReleasesUrl);
        Kit.Reserve(p, Kit.Width, lineH);
        SettingsParts.EndPane(18);
    }

    private static readonly Dictionary<SettingsTab, string> PaneIds = SettingsIndex.Pages.ToDictionary(p => p, p => "##set-page-" + p);

    /// <summary>The page's own scrolling part (each page keeps where it was scrolled to); the search's its own.</summary>
    private string PaneId() => settingsQuery.Length > 0 ? "##set-find-pane" : PaneIds[settingsTab];

    /// <summary>The search field, with a quiet × beside it to clear it while something's typed.</summary>
    private void DrawFindField(Vector2 pos, float width)
    {
        var clearW = settingsQuery.Length > 0 ? S(HSm) + S(4) : 0;
        Kit.Field("##set-find", pos, width - clearW, ref settingsQuery, "Find a setting", Icon.Search, 64);
        if (clearW > 0 && Kit.IconButton("##set-find-clear", new Vector2(pos.X + width - S(HSm), pos.Y + (S(H) - S(HSm)) / 2), Icon.X, "Clear the search", Btn.Quiet, BtnSize.Sm))
            settingsQuery = string.Empty;
    }

    /// <summary>The sidebar: Find a setting, the pages (the one you're on lit, its icon ember), the version and What's new at the foot.</summary>
    private void DrawSidebar()
    {
        var w = Kit.Width;
        var at = Kit.At;
        DrawFindField(at, w);
        Kit.Reserve(at, w, S(H));
        Kit.Gap(10);
        var dl = Kit.Dl;
        foreach (var page in SettingsIndex.Pages)
        {
            var p = Kit.At;
            var h = S(38);
            var on = page == settingsTab && settingsQuery.Length == 0;
            var (clicked, hovered, _) = Kit.Hit("##side-" + PaneIds[page], p, new Vector2(w, h));
            if (on || hovered) dl.AddRectFilled(p, p + new Vector2(w, h), Palette.U32(on ? Palette.HoverQuiet : Palette.Hover), S(9));
            var icon = S(16);
            SettingsParts.PageIcon(dl, page, new Vector2(p.X + S(10), p.Y + (h - icon) / 2), icon, on ? Palette.Ember : hovered ? Palette.Muted : Palette.Faint);
            var tx = p.X + S(10) + icon + S(10);
            var face = on ? Face.SansSemibold : Face.SansMedium;
            Txt.Line(dl, new Vector2(tx, p.Y), Txt.Fit(SettingsIndex.Title(page), face, 13, p.X + w - S(8) - tx), face, 13, on || hovered ? Palette.White : Palette.Muted, h / S(1));
            Kit.FocusRing(p, p + new Vector2(w, h), S(9));
            if (clicked) GoToPage(page);
            Kit.Reserve(p, w, h);
            Kit.Gap(2);
        }

        // The foot, at the sidebar's bottom when there's room.
        var footH = S(18);
        var paneBottom = ImGui.GetWindowPos().Y + ImGui.GetWindowHeight() - S(16);
        var y = MathF.Max(Kit.At.Y + S(12), paneBottom - footH);
        var f = new Vector2(Kit.Left, y);
        Txt.Line(dl, new Vector2(f.X + S(6), f.Y), SideVersion, Face.Mono, 11, Palette.Faint, 18);
        var lw = Kit.LinkWidth("What's new", null, 12);
        if (Kit.Link("##news", new Vector2(f.X + w - S(6) - lw, f.Y + (footH - Txt.Natural(Face.SansMedium, 12)) / 2), "What's new", null, 12))
            Dalamud.Utility.Util.OpenLink(ReleasesUrl);
        Kit.Reserve(f, w, footH);
    }

    private const string PagesMenu = "##set-pages";

    /// <summary>A narrow window's page picker: the page's icon and title and a chevron, opening a menu of the pages.</summary>
    private void DrawPagePicker(Vector2 pos, float width)
    {
        var h = S(H);
        var dl = Kit.Dl;
        var open = ImGui.IsPopupOpen(PagesMenu);
        var (clicked, hovered, _) = Kit.Hit("##set-pick", pos, new Vector2(width, h));
        var r = S(Palette.RadiusMd);
        dl.AddRectFilled(pos, pos + new Vector2(width, h), Palette.U32(hovered || open ? Palette.Over(Palette.HoverButton, Palette.Well) : Palette.Well), r);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(width, h) - new Vector2(0.5f), Palette.U32(open ? Palette.Line3 : Palette.Line2), r, ImDrawFlags.None, 1f);
        var searching = settingsQuery.Length > 0;
        var icon = S(16);
        if (searching) Icons.Draw(dl, Icon.Search, new Vector2(pos.X + S(12), pos.Y + (h - icon) / 2), icon, Palette.Muted);
        else SettingsParts.PageIcon(dl, settingsTab, new Vector2(pos.X + S(12), pos.Y + (h - icon) / 2), icon, Palette.Ember);
        var tx = pos.X + S(12) + icon + S(10);
        var chevron = S(14);
        var title = searching ? "Found" : SettingsIndex.Title(settingsTab);
        Txt.Line(dl, new Vector2(tx, pos.Y), Txt.Fit(title, Face.SansMedium, 13, pos.X + width - S(12) - chevron - S(8) - tx), Face.SansMedium, 13, Palette.White, H);
        Icons.Draw(dl, Icon.ChevronDown, new Vector2(pos.X + width - S(12) - chevron, pos.Y + (h - chevron) / 2), chevron, Palette.Muted);
        Kit.FocusRing(pos, pos + new Vector2(width, h), r);
        if (clicked)
        {
            ImGui.SetNextWindowPos(new Vector2(pos.X, pos.Y + h + S(4)));
            ImGui.OpenPopup(PagesMenu);
        }
        ImGui.SetNextWindowSizeConstraints(new Vector2(MathF.Max(S(220), width), 0), new Vector2(S(320), S(520)));
        if (!Kit.BeginMenu(PagesMenu)) return;
        try
        {
            var mw = MathF.Max(S(220), width) - S(11.2f);
            var mdl = ImGui.GetWindowDrawList();
            foreach (var page in SettingsIndex.Pages)
            {
                var (picked, rowHovered, min, max) = Kit.MenuRow("##set-pick-" + PaneIds[page], mw, SettingsIndex.Title(page));
                var (slot, _) = Kit.MenuRowParts(min, max);
                var on = page == settingsTab && !searching;
                SettingsParts.PageIcon(mdl, page, slot + new Vector2((S(20) - S(16)) / 2), S(16), on ? Palette.Ember : rowHovered ? Palette.White : Palette.Muted);
                if (picked)
                {
                    settingsTab = page;
                    settingsQuery = string.Empty;
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        finally
        {
            Kit.EndMenu();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // A page.

    private void DrawSettingsContent()
    {
        if (settingsQuery.Trim().Length > 0)
        {
            DrawFound();
            return;
        }
        var tab = settingsTab;
        // 0.9.0: Profiles draws its own head (New profile at its right) and cards.
        if (tab == SettingsTab.Profiles)
        {
            DrawProfiles();
            return;
        }
        DrawPageHead(tab);
        Kit.Gap(18);

        // When the meter has stopped itself, or the game hides something: said first, in amber.
        if (tab == SettingsTab.Meter && MeterProblem?.Invoke() is { } meterProblem)
        {
            Kit.NoteBox(meterProblem, warn: true);
            Kit.Gap(18);
        }
        if (tab is SettingsTab.Recording or SettingsTab.Advanced && recorder.HookProblem is { } hookProblem)
        {
            Kit.NoteBox(hookProblem, warn: true);
            Kit.Gap(18);
        }

        if (tab == SettingsTab.Features)
        {
            DrawFeatures();
            return;
        }

        foreach (var section in SettingsIndex.Sections(tab))
        {
            switch (tab, section.Title)
            {
                case (SettingsTab.Meter, "How it looks"):
                    DrawLookPanel(section.Title, ProfileNoteFor(SettingId.MeterSize) ?? ProfileNoteFor(SettingId.MeterTheme));
                    break;
                case (SettingsTab.Plans, "On-screen callouts"):
                    DrawCalloutsPanel(section);
                    break;
                case (SettingsTab.Cooldowns, "Cooldowns"):
                    DrawCooldownKinds();
                    continue;
                case (SettingsTab.Windows, "Arrange"):
                    DrawArrangeBox();
                    break;
                default:
                    Kit.BeginPanel("##set-" + tab + section.Title, section.Title, SectionHint(tab, section.Title), 0, 0, 0);
                    for (var i = 0; i < section.Rows.Count; i++) DrawRow(section.Rows[i], i);
                    Kit.EndPanel();
                    break;
            }
            Kit.Gap(18);
            AfterSection(tab, section.Title);
        }

        var more = SettingsIndex.More(tab);
        if (more.Count > 0)
        {
            var open = moreOpen.Contains(tab);
            if (SettingsParts.MoreRow("##more-" + PaneIds[tab], SettingsIndex.MoreWords(tab), open))
            {
                if (open) moreOpen.Remove(tab);
                else moreOpen.Add(tab);
                open = !open;
            }
            if (open)
            {
                Kit.Gap(10);
                Kit.BeginPanel("##set-more-" + tab, 0, 0, 0);
                for (var i = 0; i < more.Count; i++) DrawRow(more[i], i);
                Kit.EndPanel();
            }
            Kit.Gap(18);
        }
        PageNotes(tab);
    }

    private static readonly Dictionary<SettingsTab, Run[]> LeadRuns = SettingsIndex.Pages.ToDictionary(p => p, p => SettingsIndex.LeadLink(p) is { } to
        ? new[] { new Run(SettingsIndex.Lead(p) + " ", Face.Sans, 13, Palette.Muted), Run.LinkRun(SettingsIndex.Title(to), "go") }
        : new[] { new Run(SettingsIndex.Lead(p), Face.Sans, 13, Palette.Muted) });

    /// <summary>A page's head: its title as a small callout, its line under it (a link to Windows where it says so), Reset this page at the right.</summary>
    private void DrawPageHead(SettingsTab tab)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        const string reset = "Reset this page";
        var resetW = SettingsIndex.CanReset(tab) ? Kit.ButtonWidth(reset, BtnSize.Sm, Icon.Refresh, Btn.Quiet) : 0;
        var textW = w - (resetW > 0 ? resetW + S(12) : 0);
        var title = SettingsIndex.Title(tab);
        var th = Kit.Callout(dl, at, title, 22, textW, 2, 1.05f);
        var y = at.Y + th + S(6);
        var lh = Txt.Flow(new Vector2(at.X, y), textW, LeadRuns[tab], 19.5f, out var clicked);
        if (clicked != null && SettingsIndex.LeadLink(tab) is { } to) GoToPage(to);
        if (resetW > 0 && Kit.Button("##set-reset", new Vector2(at.X + w - resetW, at.Y - S(4)), reset, Btn.Quiet, BtnSize.Sm, Icon.Refresh, 0, false,
                "Puts this page's settings back as they were at first. It asks first."))
            Ask($"Reset {title}?", $"Every setting on {title} goes back to how it was when the plugin was new. Where the windows sit stays as it is.",
                "Reset", () => ResetPage(tab), yesIcon: Icon.Refresh);
        Kit.Reserve(at, w, MathF.Max(y + lh - at.Y, S(HSm)));
    }

    /// <summary>The words at the right of a section's eyebrow, where it has any.</summary>
    private string? SectionHint(SettingsTab tab, string section) => (tab, section) switch
    {
        (SettingsTab.Recording, "Uploads") => Linked ? "Linked to your Raid Codex" : "Not linked yet",
        _ => null,
    };

    /// <summary>What a page says under one of its sections (the roster's result under With your static).</summary>
    private void AfterSection(SettingsTab tab, string section)
    {
        if (tab != SettingsTab.Plans || section != "With your static") return;
        if (rosterNote is { } note && DateTime.UtcNow - note.At < TimeSpan.FromMinutes(1))
        {
            if (note.Ok)
            {
                var at = Kit.At;
                Kit.Reserve(at, Kit.Width, SentLine(at, Kit.Width, note.Text));
            }
            else Kit.NoteBox(note.Text);
            Kit.Gap(18);
        }
    }

    /// <summary>The notes at a page's foot (13px muted: they're read).</summary>
    private void PageNotes(SettingsTab tab)
    {
        switch (tab)
        {
            case SettingsTab.Plans:
                Txt.Note("The callouts follow the plan window's own clock: a plan made from a pull on the site carries when each step's mechanic went off. "
                    + "Send my party sends names to your own account only, and only when you press it.");
                break;
            case SettingsTab.Meter:
                Txt.Note("The meter's numbers and names stay in the plugin. Your logs on the site carry the party's numbers with their names (unless you turn Upload my logs off).");
                break;
            case SettingsTab.Cooldowns:
                Txt.Note("The others' times are worked out from when they used each one, by its recast (learned from their own uses when two come closer than the game's base). "
                    + "Yours are the game's own. A new zone, or the duty starting again after a wipe, sets them all back to ready.");
                break;
            case SettingsTab.Account:
                Txt.Note("Whoever has the token can send pulls to your account, so keep it to yourself. Revoke it on the site if it gets out.");
                Kit.Gap(10);
                Txt.Note("What goes to the site: your logs (Upload my logs) carry each pull with your party's names and home worlds, as on FFLogs. Track my progress sends a few "
                    + "numbers (by spot and job, never names). Send my party sends names to your own roster, only when you press it. Share my step sends only the plan and "
                    + "the step. The meter's numbers and names stay in the plugin.");
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Reset this page.

    /// <summary>Puts a page's settings back to a new configuration's (where the windows sit stays).</summary>
    private void ResetPage(SettingsTab tab)
    {
        var d = new Configuration();
        foreach (var id in SettingsIndex.Resets(tab)) Reset(id, d);
        config.Save();
        Notify($"{SettingsIndex.Title(tab)} is as it was at first", ToastKind.Ok, Icon.Refresh);
    }

    private void Reset(SettingId id, Configuration d)
    {
        var c = config;
        switch (id)
        {
            case SettingId.AutoRecord: c.AutoRecord = d.AutoRecord; break;
            case SettingId.RecordAllDuties: c.RecordAllDuties = d.RecordAllDuties; break;
            case SettingId.RecordParty: c.RecordParty = d.RecordParty; break;
            case SettingId.Keep:
                c.Keep = d.Keep;
                store.Keep = c.Keep;
                break;
            case SettingId.UploadLogs:
                if (c.UploadLogs != d.UploadLogs) logs.SetOn(d.UploadLogs);
                break;
            case SettingId.TrackProgress: c.TrackProgress = d.TrackProgress; break;
            case SettingId.SmoothReplays: c.SmoothReplays = d.SmoothReplays; break;
            case SettingId.ShowInBar: c.ShowInBar = d.ShowInBar; break;

            case SettingId.ShowMeter: c.ShowMeter = d.ShowMeter; break;
            case SettingId.MeterWhere: c.MeterEverywhere = d.MeterEverywhere; break;
            case SettingId.MeterHide: c.MeterHideOutOfCombat = d.MeterHideOutOfCombat; break;
            case SettingId.MeterOpenInDuties: c.MeterOpenInDuties = d.MeterOpenInDuties; break;
            case SettingId.MeterSize: c.MeterCompact = d.MeterCompact; break;
            case SettingId.MeterTheme: c.MeterTheme = d.MeterTheme; break;
            case SettingId.MeterRoles: c.MeterRoleColours = d.MeterRoleColours; break;
            case SettingId.MeterRank: c.MeterRank = d.MeterRank; break;
            case SettingId.MeterNames: c.MeterJobsOnly = d.MeterJobsOnly; break;
            case SettingId.MeterGrows: c.MeterGrows = d.MeterGrows; break;
            case SettingId.MeterHead: c.MeterHead = d.MeterHead; break;
            case SettingId.MeterOrder: c.MeterOrder = d.MeterOrder; break;
            case SettingId.MeterGauge: c.MeterGauge = d.MeterGauge; break;
            case SettingId.MeterBackground: c.MeterBackground = d.MeterBackground; break;
            case SettingId.MeterEndAfter: c.MeterEndAfter = d.MeterEndAfter; break;
            case SettingId.MeterKeep: c.MeterKeep = d.MeterKeep; break;
            case SettingId.MeterPace: c.MeterPace = d.MeterPace; break;
            case SettingId.MeterDecorations: c.MeterDecorations = d.MeterDecorations; break;
            case SettingId.MeterWhole: c.MeterWholeNumbers = d.MeterWholeNumbers; break;
            case SettingId.MeterLock: c.MeterLocked = d.MeterLocked; break;
            case SettingId.MeterClickThrough: c.MeterClickThrough = d.MeterClickThrough; break;

            case SettingId.ShowPlan: c.ShowPlanInDuties = d.ShowPlanInDuties; break;
            case SettingId.SayInChat: c.Callouts = d.Callouts; break;
            case SettingId.ShareStep: c.ShareStep = d.ShareStep; break;
            case SettingId.PlanLock: c.PlanWindowLocked = d.PlanWindowLocked; break;

            case SettingId.CalloutShow: c.CalloutOverlay.Show = d.CalloutOverlay.Show; break;
            case SettingId.CalloutAhead: c.CalloutAhead = d.CalloutAhead; break;
            case SettingId.CalloutCount: c.CalloutCount = d.CalloutCount; break;
            case SettingId.CalloutSound: c.CalloutSound = d.CalloutSound; break;
            case SettingId.CalloutWhich: c.CalloutSoundId = d.CalloutSoundId; break;
            case SettingId.CalloutLead: c.CalloutSoundLead = d.CalloutSoundLead; break;
            case SettingId.CalloutLock: c.CalloutOverlay.Locked = d.CalloutOverlay.Locked; break;
            case SettingId.CalloutClickThrough: c.CalloutOverlay.ClickThrough = d.CalloutOverlay.ClickThrough; break;
            case SettingId.CalloutGrows: c.CalloutOverlay.Grows = d.CalloutOverlay.Grows; break;
            case SettingId.CalloutCompact: c.CalloutOverlay.Compact = d.CalloutOverlay.Compact; break;
            case SettingId.CalloutBackground: c.CalloutOverlay.Background = d.CalloutOverlay.Background; break;

            case SettingId.CdShow: c.Cooldowns.Show = d.Cooldowns.Show; break;
            case SettingId.CdWhere: c.Cooldowns.Everywhere = d.Cooldowns.Everywhere; break;
            case SettingId.CdMine: c.CooldownsMine = d.CooldownsMine; break;
            case SettingId.CdHideReady: c.CooldownsHideReady = d.CooldownsHideReady; break;
            case SettingId.CdGauge: c.CooldownsGauge = d.CooldownsGauge; break;
            case SettingId.CdLock: c.Cooldowns.Locked = d.Cooldowns.Locked; break;
            case SettingId.CdClickThrough: c.Cooldowns.ClickThrough = d.Cooldowns.ClickThrough; break;
            case SettingId.CdGrows: c.Cooldowns.Grows = d.Cooldowns.Grows; break;
            case SettingId.CdCompact: c.Cooldowns.Compact = d.Cooldowns.Compact; break;
            case SettingId.CdBackground: c.Cooldowns.Background = d.Cooldowns.Background; break;
            case SettingId.CdPicks: c.CooldownPicks.Clear(); break;

            case SettingId.WindowFit:
            case SettingId.WindowScale:
                c.WindowSize = d.WindowSize;
                break;

            case SettingId.Diagnostics:
                c.Diagnostics = d.Diagnostics;
                recorder.Diagnostics.On = c.Diagnostics;
                break;

            // 0.9.0: the ready check card's and the death recap's (UI/Views/SettingsPrep.cs).
            default:
                ResetPrep(id, d);
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Find a setting.

    /// <summary>A search's answer for one page's section: its rows, and the cooldowns found (by their place in the catalogue).</summary>
    private sealed record FoundGroup(SettingsTab Page, string Section, string Head, List<SettingEntry> Rows, List<int> Cooldowns);

    private string? foundFor;
    private List<FoundGroup> found = new();
    private int foundCount;

    private List<FoundGroup> Found(string query)
    {
        if (query == foundFor) return found;
        foundFor = query;
        found = new List<FoundGroup>();
        foundCount = 0;
        var words = SettingsIndex.QueryWords(query);
        foreach (var e in SettingsIndex.Find(query))
        {
            var g = found.FirstOrDefault(f => f.Page == e.Page && f.Section == e.Section);
            if (g == null)
            {
                var section = e.More ? "More settings" : e.Section;
                found.Add(g = new FoundGroup(e.Page, e.Section, $"{SettingsIndex.Title(e.Page)} · {section}", new List<SettingEntry>(), new List<int>()));
            }
            g.Rows.Add(e);
            foundCount++;
        }
        // Each cooldown is a setting too: found by its name, its jobs or its kind.
        for (var k = 0; k < CooldownCatalog.All.Count; k++)
        {
            var spec = CooldownCatalog.All[k];
            var kind = CooldownCatalog.KindWord(spec.Kind);
            if (!SettingsIndex.Matches(words, spec.Key, SpecWords[k].Jobs, kind, string.Join(" ", spec.Names))) continue;
            var g = found.FirstOrDefault(f => f.Page == SettingsTab.Cooldowns && f.Section == kind);
            if (g == null) found.Add(g = new FoundGroup(SettingsTab.Cooldowns, kind, $"{SettingsIndex.Title(SettingsTab.Cooldowns)} · {kind}", new List<SettingEntry>(), new List<int>()));
            g.Cooldowns.Add(k);
            foundCount++;
        }
        return found;
    }

    /// <summary>The search's answer: the rows found, by page and section, each working in place; Go › opens its page.</summary>
    private void DrawFound()
    {
        var query = settingsQuery.Trim();
        var groups = Found(query);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        const string clear = "Clear";
        var cw = Kit.ButtonWidth(clear, BtnSize.Sm, Icon.X, Btn.Quiet);
        var th = Kit.Callout(dl, at, "Find a setting", 22, w - cw - S(12), 1, 1.05f);
        var line = foundCount == 0
            ? $"Nothing matches “{query}”. Try fewer words, or another way of saying it."
            : $"{Words.Count(foundCount, "setting")} for “{query}”. Each works here; Go opens its page.";
        OneRun[0] = Run.Note(line);
        var lh = Txt.Flow(new Vector2(at.X, at.Y + th + S(6)), w - cw - S(12), OneRunList, 19.5f, out _);
        if (Kit.Button("##set-found-clear", new Vector2(at.X + w - cw, at.Y - S(4)), clear, Btn.Quiet, BtnSize.Sm, Icon.X)) settingsQuery = string.Empty;
        Kit.Reserve(at, w, th + S(6) + lh);
        Kit.Gap(18);

        for (var gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            if (Kit.BeginPanel("##found" + gi, g.Head, null, 0, 0, 0, "Go", Icon.ChevronRight))
            {
                Kit.EndPanel();
                GoToPage(g.Page, g.Rows.Any(r => r.More));
                return;
            }
            var i = 0;
            foreach (var e in g.Rows) DrawRow(e, i++, true);
            foreach (var k in g.Cooldowns) CooldownRow(k, i++);
            Kit.EndPanel();
            Kit.Gap(18);
        }
    }

    private static readonly Run[] OneRun = new Run[1];

    private static IReadOnlyList<Run> OneRunList => OneRun;

    // ---------------------------------------------------------------------------------------------
    // Features: the main parts as cards.

    private static readonly Dictionary<Feature, string> FeatureIds = Features.All.ToDictionary(f => f, f => "##feat-" + f);

    private static readonly Run[] CardRun = new Run[1];

    private void DrawFeatures()
    {
        var set = config.FeaturesNow();
        var facts = new FeatureFacts(Linked, recorder.Current != null, config.UploadLogs && Linked ? logs.WaitingCount : 0, logs.Stopped != null,
            MeterProblem?.Invoke() != null, config.MeterEverywhere, plans.ForThisDuty ? plans.Plan?.Name : null, config.Cooldowns.Everywhere);
        var at = Kit.At;
        var w = Kit.Width;
        var gap = S(10);
        var cols = w >= S(440) ? 2 : 1;
        var cardW = (w - gap * (cols - 1)) / cols;
        var y = at.Y;
        for (var i = 0; i < Features.All.Count; i += cols)
        {
            var rowH = 0f;
            for (var c = 0; c < cols && i + c < Features.All.Count; c++) rowH = MathF.Max(rowH, CardHeight(Features.All[i + c], cardW));
            for (var c = 0; c < cols && i + c < Features.All.Count; c++)
            {
                var f = Features.All[i + c];
                FeatureCard(f, new Vector2(at.X + c * (cardW + gap), y), cardW, rowH, set, facts);
            }
            y += rowH + gap;
        }
        Kit.Reserve(at, w, y - gap - at.Y);
        Kit.Gap(16);
        if (SettingsParts.Box("##set-setup", Icon.Play, "Not sure what you need? The setup guide walks through it again.", "Run the setup guide"))
            Later(ShowSetup);
    }

    private static float CardHeight(Feature f, float w)
    {
        CardRun[0] = new Run(Features.Line(f), Face.Sans, 13, Palette.Muted);
        var lh = Txt.Flow(Vector2.Zero, w - S(28), CardRun, 18.5f, out _, draw: false);
        return S(14) + S(34) + S(10) + lh + S(10) + S(20) + S(14);
    }

    /// <summary>
    /// A main part's card: an ember slab edge while it's on, a well with its icon, its name and its switch, its one
    /// line, its status tag and Settings › (or Link › while it needs an account).
    /// </summary>
    private void FeatureCard(Feature f, Vector2 pos, float w, float h, FeatureSet set, FeatureFacts facts)
    {
        var dl = Kit.Dl;
        var on = set[f];
        var needsLink = Features.NeedsAccount(f) && !Linked;
        var r = S(Palette.RadiusLg);
        // A panel (`.pn`); while it's on, lit (an ember line round it), else quiet.
        Kit.PanelGround(dl, pos, pos + new Vector2(w, h), r);
        Kit.PanelEdge(dl, pos, pos + new Vector2(w, h), r, quiet: !(on && !needsLink));
        if (on && !needsLink) dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.EmberLine), r, ImDrawFlags.None, 1f);
        var x = pos.X + S(14);
        var y = pos.Y + S(14);
        var well = S(34);
        var lit = on && !needsLink;
        dl.AddRectFilled(new Vector2(x, y), new Vector2(x + well, y + well), Palette.U32(lit ? Palette.EmberWash : Palette.Well), S(9));
        dl.AddRect(new Vector2(x, y) + new Vector2(0.5f), new Vector2(x + well, y + well) - new Vector2(0.5f), Palette.U32(Palette.Line), S(9), ImDrawFlags.None, 1f);
        var icon = S(16);
        SettingsParts.FeatureIcon(dl, f, new Vector2(x + (well - icon) / 2, y + (well - icon) / 2), icon, lit ? Palette.Ember : Palette.Disabled);
        var sw = Kit.SwitchSize;
        var sx = pos.X + w - S(14) - sw.X;
        var nx = x + well + S(10);
        Txt.Line(dl, new Vector2(nx, y), Txt.Fit(Features.Name(f), Face.SansSemibold, 14, sx - S(10) - nx), Face.SansSemibold, 14, lit ? Palette.White : Palette.Muted, 34);
        if (Kit.Switch(FeatureIds[f], new Vector2(sx, y + (well - sw.Y) / 2), on, needsLink) && !needsLink)
        {
            if (f == Feature.Upload) logs.SetOn(!on);
            else
            {
                config.SetFeatures(set.With(f, !on));
                config.Save();
            }
        }
        y += well + S(10);
        CardRun[0] = new Run(Features.Line(f), Face.Sans, 13, Palette.Muted);
        Txt.Flow(new Vector2(x, y), w - S(28), CardRun, 18.5f, out _);
        // The foot: the tag at the left, the link at the right, at the card's bottom.
        var footY = pos.Y + h - S(14) - S(20);
        var link = needsLink ? "Link" : "Settings";
        var lw = Kit.LinkWidth(link, Icon.ChevronRight);
        var (text, kind) = FeatureStatus.Of(f, on, facts);
        SettingsParts.StatusTag(dl, new Vector2(x, footY), text, kind, w - S(28) - lw - S(10));
        if (Kit.Link(FeatureIds[f] + "-go", new Vector2(pos.X + w - S(14) - lw, footY + (S(20) - Txt.Natural(Face.SansMedium, 13)) / 2), link, Icon.ChevronRight))
            GoToPage(needsLink ? SettingsTab.Account : SettingsIndex.PageOf(f));
    }

    // ---------------------------------------------------------------------------------------------
    // Windows: Arrange windows.

    private void DrawArrangeBox()
    {
        var arranging = Arranging?.Invoke() == true;
        if (SettingsParts.Box("##set-arrange", Icon.Move,
                "Arrange windows shows every window at once (the meter, the plan, the callouts and the cooldowns, with made-up content) to drag each into place.",
                arranging ? "Arranging…" : "Arrange windows", Icon.Move, StartArrange == null || arranging))
            StartArrange?.Invoke();
    }

    // ---------------------------------------------------------------------------------------------
    // The meter: how it looks, with its live preview.

    private static readonly string[] ThemeIds = { "##theme0", "##theme1", "##theme2", "##theme3", "##theme4", "##theme5" };

    /// <summary>Settings' live preview of the compact meter: the sample fight playing, drawn as the meter draws it (not interactive).</summary>
    private readonly PlateMeter themePreview = new() { Interactive = false };

    private MeterPreview? themeSample;

    private (LimitBreakGauge? Of, float[] Bars) previewPips = (null, Array.Empty<float>());

    /// <summary>
    /// How it looks: Size, the six themes as tiles (the one picked edged in ember, its idea under them) and Role colours on
    /// the left; on the right (under them in a narrow window) the live preview, the sample fight playing in the theme at its
    /// real size as the meter draws it, and Play a sample fight under it.
    /// </summary>
    private void DrawLookPanel(string title, string? sub)
    {
        Kit.BeginPanel("##set-look", title, sub, 4, 20, 18);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var theme = MeterThemes.Of(config.MeterTheme);
        MeterWindow.WantFonts(fonts, theme.Id);
        var previewW = MathF.Min(S(theme.Win.Width), w);
        var side = w - previewW - S(16) >= S(240);
        var leftW = side ? w - previewW - S(16) : w;
        var x = at.X;
        var y = at.Y;

        // Size.
        var size = LookChoice("##meter-size", "Size", SizeItems, config.MeterCompact ? 0 : 1, x, ref y, leftW, CompactTip);
        if (size != (config.MeterCompact ? 0 : 1))
        {
            config.MeterCompact = size == 0;
            config.Save();
        }
        y += S(14);

        // The themes.
        Txt.Line(dl, new Vector2(x, y), "Theme", Face.SansMedium, 14, Palette.White, 14 * 1.3f);
        y += S(14 * 1.3f) + S(2);
        Txt.Line(dl, new Vector2(x, y), Txt.Fit("Both meters' and the opened-up window's look", Face.Sans, 12, leftW), Face.Sans, 12, Palette.Muted, 17.4f);
        y += S(17.4f) + S(8);
        var cols = leftW >= S(300) ? 3 : 2;
        var tileGap = S(6);
        var tileW = (leftW - (cols - 1) * tileGap) / cols;
        var tileH = S(62);
        var picked = config.MeterTheme;
        string? tip = null;
        for (var k = 0; k < MeterThemes.All.Count; k++)
        {
            var t = MeterThemes.All[k];
            var tmin = new Vector2(x + (k % cols) * (tileW + tileGap), y + (k / cols) * (tileH + tileGap));
            var tmax = tmin + new Vector2(tileW, tileH);
            var on = t.Id == picked;
            var (clicked, hovered, _) = Kit.Hit(ThemeIds[k], tmin, tmax - tmin);
            var tr = S(9);
            dl.AddRectFilled(tmin, tmax, Palette.U32(hovered && !on ? Palette.Over(Palette.HoverButton, Palette.Well) : Palette.Well), tr);
            dl.AddRect(tmin + new Vector2(0.5f), tmax - new Vector2(0.5f), Palette.U32(on ? Palette.Ember : hovered ? Palette.Line3 : Palette.Line2), tr, ImDrawFlags.None, 1f);
            // A swatch of the theme: its ground, an edge, and two bars in its roles' colours.
            var sw0 = tmin + new Vector2(S(8), S(8));
            var sw1 = new Vector2(tmax.X - S(8), sw0.Y + S(26));
            dl.AddRectFilled(sw0, sw1, Palette.U32(t.Win.GradTop ?? t.Win.Bg), S(5));
            dl.AddRect(sw0, sw1, Palette.U32(t.Win.Edge with { W = MathF.Max(0.4f, t.Win.Edge.W) }), S(5), ImDrawFlags.None, 1f);
            var bw = sw1.X - sw0.X - S(12);
            dl.AddRectFilled(sw0 + new Vector2(S(6), S(7)), sw0 + new Vector2(S(6) + bw * 0.9f, S(11)), Palette.U32(t.Roles.Dps), S(2));
            dl.AddRectFilled(sw0 + new Vector2(S(6), S(15)), sw0 + new Vector2(S(6) + bw * 0.6f, S(19)), Palette.U32(t.Roles.Tank with { W = t.Roles.Tank.W * 0.8f }), S(2));
            var face = on ? Face.SansSemibold : Face.SansMedium;
            Txt.Line(dl, new Vector2(tmin.X + S(8), sw1.Y + S(4)), Txt.Fit(t.Name, face, 12, tileW - S(16)), face, 12, on ? Palette.White : Palette.Muted, 18);
            Kit.FocusRing(tmin, tmax, tr);
            if (hovered) tip = t.Kind + ". " + t.Blurb;
            if (clicked && !on)
            {
                config.MeterTheme = t.Id;
                config.Save();
            }
        }
        var rows = (MeterThemes.All.Count + cols - 1) / cols;
        y += rows * tileH + (rows - 1) * tileGap + S(8);
        OneRun[0] = Run.Note(theme.Blurb);
        OneRun[0] = OneRun[0] with { Px = 12 };
        y += Txt.Flow(new Vector2(x, y), leftW, OneRunList, 17.4f, out _);
        if (tip != null) Kit.Tooltip(tip);
        y += S(14);

        // Role colours.
        var roles = LookChoice("##meter-roles", "Role colours", RoleShortItems, (int)config.MeterRoleColours, x, ref y, leftW, RolesTip);
        if (roles != (int)config.MeterRoleColours)
        {
            config.MeterRoleColours = (MeterRoleColours)roles;
            config.Save();
        }
        var leftH = y - at.Y;

        // The preview: the sample fight in the theme picked, as the meter draws it.
        var px = side ? at.X + leftW + S(16) : at.X;
        var py = side ? at.Y : y + S(18);
        Kit.Eyebrow(dl, new Vector2(px, py), "Preview", null, previewW, Palette.Muted);
        py += Kit.EyebrowH + S(10);
        themeSample ??= new MeterPreview();
        var e = themeSample.Current;
        if (e != null)
        {
            MeterWindow.Configure(themePreview, config);
            themePreview.Update(e, themeSample, MeterTab.Damage, config.MeterRank, config.MeterWholeNumbers, Environment.TickCount64 / 1000.0);
            var g = themeSample.LimitBreak;
            if (!Equals(previewPips.Of, g)) previewPips = (g, MeterText.Bars(g));
            themePreview.Pips = config.MeterGauge != GaugePlace.Off ? previewPips.Bars : null;
            themePreview.Pace = null;
            if (themeSample.Pace(e.Id, PaceAgainst.Best) is { } pace)
            {
                themePreview.Pace = Format.SignedPercent(pace.Ahead);
                themePreview.PaceAhead = pace.Ahead >= 0;
            }
            var party = 0;
            foreach (var r in e.Rows)
                if (!r.IsLimitBreak)
                    party++;
            var previewH = S(themePreview.Needed(party));
            themePreview.DrawFight(dl, new Vector2(px, py), previewW, previewH, false);
            py += previewH;
        }
        py += S(12);
        // The meter mid-fight with made-up numbers, to place it (and see Size and Grows) without a pull.
        var sampleOn = SampleOn?.Invoke() == true;
        var waits = !sampleOn && SampleWaits?.Invoke() == true;
        if (Kit.Button("##meter-sample", new Vector2(px, py), sampleOn ? "Stop the sample" : "Play a sample fight", Btn.Outline, BtnSize.Sm, sampleOn ? Icon.Square : Icon.Play,
                previewW, waits || ToggleSample == null, SampleTip))
            ToggleSample?.Invoke();
        py += S(HSm);
        if (waits || sampleOn)
        {
            py += S(6);
            OneRun[0] = new Run(waits ? "After this fight: a real one ends a sample." : "Made-up numbers under a Preview tag, until a fight starts.", Face.Sans, 12, Palette.Muted);
            py += Txt.Flow(new Vector2(px, py), previewW, OneRunList, 17.4f, out _);
        }
        var rightH = side ? py - at.Y : py - at.Y;
        Kit.Reserve(at, w, MathF.Max(leftH, rightH));
        Kit.EndPanel();
    }

    /// <summary>A name with a dense segmented control at the right (under it when there isn't room); returns the item picked.</summary>
    private static int LookChoice(string id, string name, string[] items, int selected, float x, ref float y, float width, string tip)
    {
        var dl = Kit.Dl;
        var segW = Kit.SegmentedWidth(items);
        var nameW = Txt.Width(name, Face.SansMedium, 14);
        var nameH = S(14 * 1.3f);
        var beside = nameW + S(12) + segW <= width;
        var rowH = beside ? S(HSm) : nameH + S(6) + S(HSm);
        var ny = beside ? y + (S(HSm) - nameH) / 2 : y;
        Txt.Line(dl, new Vector2(x, ny), name, Face.SansMedium, 14, Palette.White, 14 * 1.3f);
        if (Kit.Pointed(new Vector2(x, ny), new Vector2(x + nameW, ny + nameH))) Kit.Tooltip(tip);
        var segPos = beside ? new Vector2(x + width - segW, y) : new Vector2(x, y + nameH + S(6));
        var picked = Kit.Segmented(id, segPos, items, selected);
        y += rowH;
        return picked;
    }

    // ---------------------------------------------------------------------------------------------
    // The callouts: their rows with a small preview beside them.

    private static readonly (string Name, string Time)[] PreviewSteps = { ("Pantokrator", "18"), ("Wave Cannon", "41"), ("Akh Morn", "57") };

    /// <summary>
    /// On-screen callouts: the rows on the left; on the right (under them in a narrow window) a small preview of the
    /// callouts as they'll look (as many as How many at once, their background), and Show sample callouts under it.
    /// </summary>
    private void DrawCalloutsPanel(SettingSection section)
    {
        Kit.BeginPanel("##set-callouts", section.Title, null, 0, 0, 0);
        var at = Kit.At;
        var w = Kit.Width;
        var colW = S(210);
        var side = w - colW - S(14) >= S(320);
        if (side) Kit.PushBox(at.X, w - colW - S(14));
        var i = 0;
        foreach (var e in section.Rows)
            if (e.Id != SettingId.CalloutSample)
                DrawRow(e, i++);
        if (side) Kit.PopBox();
        var rowsBottom = ImGui.GetCursorScreenPos().Y;
        if (side)
        {
            var h = CalloutPreview(new Vector2(at.X + w - S(14) - colW, at.Y + S(14)), colW);
            Kit.Reserve(at, w, MathF.Max(rowsBottom - at.Y, h + S(28)));
        }
        else
        {
            Kit.Rule(Kit.Dl, at.X, at.X + w, rowsBottom - 0.5f);
            var h = CalloutPreview(new Vector2(at.X + S(16), rowsBottom + S(14)), w - S(32));
            Kit.Reserve(new Vector2(at.X, rowsBottom), w, h + S(28));
        }
        Kit.EndPanel();
    }

    private float CalloutPreview(Vector2 pos, float width)
    {
        var dl = Kit.Dl;
        var y = pos.Y;
        Kit.Eyebrow(dl, new Vector2(pos.X, y), "Preview", null, width, Palette.Muted);
        y += Kit.EyebrowH + S(8);
        var o = config.CalloutOverlay;
        var bg = Math.Clamp(o.Background, 0, 1);
        // The callouts' own look in small (CalloutWindow, since 0.8.3): the first step on a card with its countdown ring,
        // the next as rows with their times under the ring.
        var pad = S(10);
        var ring = S(o.Compact ? 28 : 34);
        var gap = S(9);
        var bigPx = o.Compact ? 13 : 16;
        var bigH = S(bigPx * 1.1f);
        var chipH = S(18);
        var cardH = MathF.Max(ring, Kit.EyebrowH + S(2) + bigH + S(6) + chipH) + S(16);
        var rowH = S(o.Compact ? 20 : 24);
        var shown = Math.Clamp(config.CalloutCount, 1, PreviewSteps.Length);
        var boxH = pad * 2 + cardH + (shown - 1) * (S(4) + rowH) + (shown > 1 ? S(2) : 0);
        var r = S(Palette.RadiusMd);
        var box = new Vector2(pos.X, y);
        dl.AddRectFilled(box, box + new Vector2(width, boxH), Palette.U32(Palette.Rgba(13, 13, 16, bg)), r);
        dl.AddRect(box + new Vector2(0.5f), box + new Vector2(width, boxH) - new Vector2(0.5f), Palette.U32(Palette.Line2), r, ImDrawFlags.None, 1f);
        var cMin = box + new Vector2(pad);
        var cMax = new Vector2(box.X + width - pad, cMin.Y + cardH);
        dl.AddRectFilled(cMin, cMax, Palette.U32(Palette.Rgba(24, 24, 29, Math.Clamp(0.55f + 0.4f * bg, 0, 0.95f))), S(9));
        dl.AddRect(cMin, cMax, Palette.U32(Palette.Line2), S(9), ImDrawFlags.None, 1);
        // The ring: two thirds of its time left, "8" and a small "s".
        var c = new Vector2(cMin.X + S(8) + ring / 2, cMin.Y + cardH / 2);
        var stroke = S(3);
        var rad = ring / 2 - stroke / 2;
        dl.AddCircleFilled(c, rad, Palette.U32(Palette.Rgba(9, 9, 11, 0.6f)), 32);
        dl.AddCircle(c, rad, Palette.U32(Palette.Track), 32, stroke);
        dl.PathArcTo(c, rad, -MathF.PI / 2, -MathF.PI / 2 + MathF.Tau * 0.55f, 20);
        dl.PathStroke(Palette.U32(Palette.Ember), ImDrawFlags.None, stroke);
        var npx = o.Compact ? 13 : 16;
        var nw = Txt.Width("8", Face.MonoSemibold, npx);
        Txt.Draw(dl, new Vector2(c.X - nw / 2, c.Y - Txt.Natural(Face.MonoSemibold, npx) / 2), "8", Face.MonoSemibold, npx, Palette.White);
        var tx = c.X + ring / 2 + gap;
        var tw = cMax.X - S(10) - tx;
        var ty = cMin.Y + (cardH - (Kit.EyebrowH + S(2) + bigH + S(6) + chipH)) / 2;
        Kit.Eyebrow(dl, new Vector2(tx, ty), PreviewSteps[0].Name, null, tw, Palette.Faint);
        ty += Kit.EyebrowH + S(2);
        Kit.Callout(dl, new Vector2(tx, ty), "Spread, then stack", bigPx, tw, 1, 1.1f);
        ty += bigH + S(6);
        // Your cooldown's chip.
        const string you = "YOU";
        var yw = Txt.SpacedWidth(you, Face.Mono, 10, 0.09f);
        var rest = Txt.Fit("Reprisal", Face.SansSemibold, 13, MathF.Max(0, tw - yw - S(17)));
        var chipW = MathF.Min(tw, S(6) + yw + S(5) + Txt.Width(rest, Face.SansSemibold, 13) + S(7));
        dl.AddRectFilled(new Vector2(tx, ty), new Vector2(tx + chipW, ty + chipH), Palette.U32(Palette.EmberWash), chipH / 2);
        dl.AddRect(new Vector2(tx, ty), new Vector2(tx + chipW, ty + chipH), Palette.U32(Palette.EmberLine), chipH / 2, ImDrawFlags.None, 1);
        Txt.Spaced(dl, new Vector2(tx + S(6), ty + (chipH - Txt.Natural(Face.Mono, 10)) / 2), you, Face.Mono, 10, Palette.Ember, 0.09f);
        Txt.Line(dl, new Vector2(tx + S(6) + yw + S(5), ty), rest, Face.SansSemibold, 13, Palette.White, 18);
        // The next steps.
        var ry = cMax.Y + S(6);
        for (var k = 1; k < shown; k++)
        {
            dl.AddRectFilled(new Vector2(cMin.X, ry), new Vector2(cMax.X, ry + rowH), Palette.U32(Palette.Rgba(13, 13, 16, Math.Clamp(0.3f + 0.4f * bg, 0, 0.9f))), S(6));
            var t = PreviewSteps[k].Time;
            var twid = Txt.Width(t, Face.MonoSemibold, 13);
            Txt.Line(dl, new Vector2(c.X - twid / 2, ry), t, Face.MonoSemibold, 13, Palette.Muted, rowH / S(1));
            Txt.Line(dl, new Vector2(tx, ry), Txt.Fit(PreviewSteps[k].Name, Face.SansSemibold, 13, cMax.X - S(10) - tx), Face.SansSemibold, 13, Palette.Text, rowH / S(1));
            ry += rowH + S(4);
        }
        y += boxH + S(10);
        var sampleOn = CalloutsSampleOn?.Invoke() == true;
        if (Kit.Button("##co-sample", new Vector2(pos.X, y), sampleOn ? "Stop the sample" : "Show sample callouts", Btn.Outline, BtnSize.Sm, sampleOn ? Icon.Square : Icon.Play,
                width, ToggleCalloutsSample == null, CalloutSampleTip))
            ToggleCalloutsSample?.Invoke();
        y += S(HSm);
        return y - pos.Y;
    }

    // ---------------------------------------------------------------------------------------------
    // The cooldowns: each kind's, with a switch each.

    /// <summary>Each kind's panel id (by the kind's number), made once.</summary>
    private static readonly string[] KindPanelIds = Enumerable.Range(0, CooldownCatalog.Kinds.Max(k => (int)k) + 1).Select(k => $"##set-cd{k}").ToArray();


    /// <summary>Each cooldown's switch id and its jobs ("PLD, WAR, DRK, GNB"), as the catalogue lists them, made once.</summary>
    private static readonly (string Id, string Jobs)[] SpecWords = CooldownCatalog.All.Select(s => ("##cd-" + s.Key, string.Join(", ", s.Jobs))).ToArray();

    private static readonly Dictionary<(int, int), string> OnOfWords = new();

    /// <summary>"3 of 12", made once each.</summary>
    private static string OnOf(int on, int of)
    {
        if (!OnOfWords.TryGetValue((on, of), out var text)) OnOfWords[(on, of)] = text = $"{on} of {of}";
        return text;
    }

    /// <summary>Each kind's section: its eyebrow, how many are on, Turn all on (or off) at the right, and a switch for each of its cooldowns.</summary>
    private void DrawCooldownKinds()
    {
        foreach (var kind in CooldownCatalog.Kinds)
        {
            var (on, of) = CooldownCatalog.Count(kind, config.CooldownPicks);
            var all = on == of;
            if (Kit.BeginPanel(KindPanelIds[(int)kind], CooldownCatalog.KindWord(kind), OnOf(on, of), 0, 0, 0, all ? "Turn all off" : "Turn all on"))
            {
                foreach (var spec in CooldownCatalog.All)
                    if (spec.Kind == kind)
                        config.CooldownPicks[spec.Key] = !all;
                config.Save();
            }
            var j = 0;
            for (var k = 0; k < CooldownCatalog.All.Count; k++)
                if (CooldownCatalog.All[k].Kind == kind)
                    CooldownRow(k, j++);
            Kit.EndPanel();
            Kit.Gap(18);
        }
    }

    /// <summary>One cooldown's switch: its name over its jobs.</summary>
    private void CooldownRow(int k, int index)
    {
        var spec = CooldownCatalog.All[k];
        var picked = CooldownCatalog.IsOn(spec, config.CooldownPicks);
        if (SwitchRow(index, SpecWords[k].Id, spec.Key, SpecWords[k].Jobs, picked, null, pad: 8))
        {
            config.CooldownPicks[spec.Key] = !picked;
            config.Save();
        }
    }
}
