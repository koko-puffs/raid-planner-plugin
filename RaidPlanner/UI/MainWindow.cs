using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using RaidPlanner.Core;
using RaidPlanner.Game;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The plugin's one window, in the Raid Planner's look. Three screens: Home (the pull being
/// recorded, the latest pull with Send to Raid Planner, the earlier ones), a pull (what's in it,
/// as a timeline, with the same buttons) and Settings (Recording and Account). Below Home every
/// screen has the back arrow at its top left, as Housing Studio's plugin has it.
/// </summary>
public sealed partial class MainWindow : Window, IDisposable
{
    private enum Screen
    {
        Home,
        Pull,
        Settings,
    }

    private readonly Configuration config;
    private readonly RecordingStore store;
    private readonly FightRecorder recorder;
    private readonly SiteSession site;
    private readonly PlanService plans;
    private readonly Uploads uploads;
    private readonly Fonts fonts;
    private readonly FileDialogManager dialogs = new();
    private readonly List<Action> later = new();
    private Screen screen;

    /// <summary>The pull open on its own screen (by its file).</summary>
    private string? openFile;

    private DateTime dialogFailedAt;

    public MainWindow(Configuration config, RecordingStore store, FightRecorder recorder, SiteSession site, PlanService plans, Uploads uploads, Fonts fonts)
        : base("Raid Planner###RaidPlannerMain")
    {
        this.config = config;
        this.store = store;
        this.recorder = recorder;
        this.site = site;
        this.plans = plans;
        this.uploads = uploads;
        this.fonts = fonts;
        Txt.Fonts = fonts;

        Size = new Vector2(560, 780);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(440, 520), MaximumSize = new Vector2(1400, 2400) };
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        dialogs.AddedWindowFlags = ImGuiWindowFlags.NoCollapse;
    }

    /// <summary>Opens the window on Home.</summary>
    public void ShowMain()
    {
        Dismiss();
        screen = Screen.Home;
        IsOpen = true;
    }

    /// <summary>Opens Settings (Dalamud's cog, /raidplan settings).</summary>
    public void ShowSettings()
    {
        Dismiss();
        settingsFrom = Screen.Home;
        screen = Screen.Settings;
        IsOpen = true;
    }

    /// <summary>Opens Settings on Account (the plan window's Link your account).</summary>
    public void ShowAccount()
    {
        Dismiss();
        settingsFrom = Screen.Home;
        screen = Screen.Settings;
        settingsTab = SettingsTab.Account;
        IsOpen = true;
    }

    /// <summary>Opens a pull's own screen.</summary>
    public void ShowPull(RecordingInfo info)
    {
        Dismiss();
        openFile = info.File;
        screen = Screen.Pull;
        IsOpen = true;
    }

    public override void OnClose()
    {
        settingsTab = SettingsTab.Recording;
        Dismiss();
    }

    /// <summary>The plugin unloads: a send under way is stopped by the site session (it won't mark or say anything after).</summary>
    public void Dispose()
    {
        Dismiss();
    }

    /// <summary>Shows or hides the plan window (set by the plugin).</summary>
    public Action? TogglePlanWindow { get; set; }

    /// <summary>Is the plan window open (set by the plugin), so its button says Show or Hide.</summary>
    public Func<bool>? PlanWindowOpen { get; set; }

    public override void PreDraw()
    {
        if (!firstSizeFitted)
        {
            // The first size could be taller than the screen, with the footer off it.
            firstSizeFitted = true;
            var screenH = ImGui.GetMainViewport().Size.Y;
            var scale = Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;
            if (screenH > 0 && scale > 0 && Size is { } size && size.Y * scale > screenH * 0.9f)
                Size = new Vector2(size.X, MathF.Max(520, screenH * 0.9f / scale));
        }
        Theme.PushFrame();
    }

    private bool firstSizeFitted;

    public override void PostDraw()
    {
        Theme.Pop();
        // Outside the window's own catch: a fault here would turn off the plugin's whole interface.
        try
        {
            dialogs.Draw();
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - dialogFailedAt > TimeSpan.FromSeconds(10))
            {
                Services.Log.Error(ex, "Raid Planner's file dialog failed");
                dialogFailedAt = DateTime.UtcNow;
            }
        }
    }

    public override void Draw()
    {
        Kit.ResetFrame();
        Frame.ResetFrame();
        Theme.PushInside();
        try
        {
            using (fonts.Push(Face.Sans, 14))
            {
                Kit.Blocked = confirm != null;
                try
                {
                    switch (screen)
                    {
                        case Screen.Settings:
                            DrawSettings();
                            break;
                        case Screen.Pull when openFile != null && store.Find(openFile) is { } info:
                            DrawPull(info);
                            break;
                        default:
                            screen = Screen.Home;
                            DrawHome();
                            break;
                    }
                }
                finally
                {
                    Frame.Unwind();
                    Frame.DrawToast();
                    Kit.Blocked = false;
                    DrawConfirm();
                }
            }
        }
        finally
        {
            Theme.Pop();
        }

        if (later.Count > 0)
        {
            var actions = later.ToArray();
            later.Clear();
            foreach (var a in actions)
            {
                try
                {
                    a();
                }
                catch (Exception ex)
                {
                    Services.Log.Error(ex, "Raid Planner action failed");
                    Notify("Something went wrong (see /xllog)");
                }
            }
        }
    }

    /// <summary>Runs after this frame's drawing is finished.</summary>
    private void Later(Action action) => later.Add(action);

    private static void Notify(string text) => Frame.Toast(text);

    // ---------------------------------------------------------------------------------------------
    // Titles and the header every screen below Home has.

    /// <summary>
    /// A title in Geist 600, as the site's headings (tight, −0.02em), on up to <paramref name="lines"/>
    /// lines, cut with an ellipsis on the last. Returns its height; with <paramref name="draw"/> false, only measures.
    /// </summary>
    private static float Title(Vector2 pos, string text, float maxWidth, int px = 22, int lines = 2, bool draw = true, Vector4? color = null)
    {
        var lineCss = px * 1.15f;
        var lh = S(lineCss);
        maxWidth = MathF.Max(S(40), maxWidth);
        var rows = new List<string>();
        var cur = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length == 0 || Txt.Measure(candidate, Face.SansSemibold, px).X <= maxWidth) cur = candidate;
            else
            {
                rows.Add(cur);
                cur = word;
            }
        }
        rows.Add(cur);
        if (rows.Count > lines) rows = rows.Take(lines - 1).Append(string.Join(' ', rows.Skip(lines - 1))).ToList();
        if (!draw) return lh * rows.Count;
        var dl = Kit.Dl;
        for (var i = 0; i < rows.Count; i++)
            Txt.Line(dl, pos + new Vector2(0, lh * i), Txt.Fit(rows[i], Face.SansSemibold, px, maxWidth), Face.SansSemibold, px, color ?? Palette.Ink, lineCss);
        return lh * rows.Count;
    }

    /// <summary>
    /// The header of every screen below Home: the round back arrow at the top left, the eyebrow and
    /// the title beside it, and the screen's own round buttons on the right (<paramref name="right"/>
    /// draws them from the top-left corner it's given, in <paramref name="rightWidth"/>). True when
    /// back was pressed.
    /// </summary>
    private static bool ScreenHeader(string backTip, string? eyebrow, string title, float rightWidth = 0, Action<Vector2>? right = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var tx = at.X + S(36) + S(12);
        var textW = at.X + w - tx - (rightWidth > 0 ? rightWidth + S(12) : 0);
        var back = Kit.IconButton("##back", at, Icon.ArrowLeft, backTip, 36);
        var y = at.Y;
        if (!string.IsNullOrEmpty(eyebrow))
        {
            y += Kit.Eyebrow(new Vector2(tx, y), eyebrow, null, textW);
            y += S(2);
        }
        else y += MathF.Max(0, (S(36) - S(22 * 1.15f)) / 2);
        y += Title(new Vector2(tx, y), title, textW);
        right?.Invoke(new Vector2(at.X + w - rightWidth, at.Y));
        Kit.Reserve(at, w, MathF.Max(S(36), y - at.Y));
        return back;
    }

    /// <summary>The header's Settings button.</summary>
    private void SettingsButton(Vector2 pos)
    {
        if (Kit.IconButton("##settings", pos, Icon.Sliders, "Settings", 36)) OpenSettings(settingsTab);
    }

    // ---------------------------------------------------------------------------------------------
    // The question: a card over the window, its answers saying what happens.

    private sealed record Confirmation(string Title, string Body, string Yes, string No, Action OnYes, bool Danger);

    private Confirmation? confirm;
    private float confirmCardH;

    /// <summary>The question has been brought to the front once (after that, only when the plugin's window is clicked).</summary>
    private bool confirmFocused;

    private void Ask(string title, string body, string yes, string no, Action onYes, bool danger = false)
    {
        confirm = new Confirmation(title, body, yes, no, onYes, danger);
        confirmFocused = false;
    }

    /// <summary>The question goes unanswered: the window closed, or another screen opened.</summary>
    private void Dismiss()
    {
        confirm = null;
        confirmCardH = 0;
    }

    /// <summary>
    /// The question over the plugin's window: a window of its own the size of the plugin's, kept in
    /// front, which darkens only the plugin's window and takes every click on it; the card in its middle.
    /// </summary>
    private void DrawConfirm()
    {
        if (confirm == null) return;
        var c = confirm;
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        ImGui.SetNextWindowPos(winPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(winSize, ImGuiCond.Always);
        // To the front as it opens, and when the plugin's window is clicked; never taken from another window.
        if (!confirmFocused || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            ImGui.SetNextWindowFocus();
            confirmFocused = true;
        }
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, S(14));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
        var shown = ImGui.Begin("##rp-confirm-over", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoFocusOnAppearing);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);
        try
        {
            if (!shown) return;
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(winPos, winPos + winSize, Palette.U32(Palette.WithAlpha(Palette.Ground, 0.75f)), S(14));
            var width = MathF.Min(S(420), winSize.X - S(48));
            var pad = S(24);
            var inner = width - pad * 2;
            var cardH = confirmCardH > 0 ? confirmCardH : S(200);
            var cardPos = new Vector2(winPos.X + (winSize.X - width) / 2, winPos.Y + MathF.Max(S(40), (winSize.Y - cardH) / 2));
            var r = S(Palette.RadiusXl);
            dl.AddRectFilled(cardPos, cardPos + new Vector2(width, cardH), Palette.U32(Palette.Card), r);
            dl.AddRect(cardPos, cardPos + new Vector2(width, cardH), Palette.U32(Palette.Line), r);

            var at = cardPos + new Vector2(pad, pad);
            ImGui.SetCursorScreenPos(at);
            Kit.PushBox(at.X, inner);
            var titleH = Title(at, c.Title, inner, 18, 2);
            Kit.Reserve(at, inner, titleH);
            Kit.Gap(8);
            Txt.Para(new[] { new Run(c.Body, Face.Sans, 14, Palette.Soft) }, 21);
            Kit.Gap(20);
            var buttons = Kit.At;
            var yesStyle = c.Danger ? Btn.Danger : Btn.Primary;
            var yesW = Kit.ButtonWidth(c.Yes, BtnSize.Md, null, yesStyle);
            var noW = Kit.ButtonWidth(c.No);
            var close = false;
            if (Kit.Button("##no", new Vector2(buttons.X + inner - yesW - S(8) - noW, buttons.Y), c.No)) close = true;
            if (Kit.Button("##yes", new Vector2(buttons.X + inner - yesW, buttons.Y), c.Yes, yesStyle))
            {
                close = true;
                Later(c.OnYes);
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) close = true;
            Kit.Reserve(buttons, inner, Kit.ButtonHeight(BtnSize.Md));
            Kit.PopBox();
            confirmCardH = Kit.At.Y - cardPos.Y + pad;
            if (close && ReferenceEquals(confirm, c)) confirm = null;
        }
        finally
        {
            ImGui.End();
        }
    }
}
