using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.Game;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The plugin's main window, in Ready Check (the mock-ups' "The plugin"). Three screens: Home (the
/// state as a tag, the pull being recorded in the ember box with its clock as a cast bar, the
/// latest pull with Send to Raid Planner as the telegraph, the night's earlier pulls), a pull (its
/// facts, the pull to its own scale, what's in it as a row a mechanic, deaths too) and Settings
/// (Recording, Plan window, Meter, Account). Below Home every screen has the square back button at
/// its top left. Dalamud's title bar is kept.
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
    private DateTime drawFailedAt;

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

        Size = new Vector2(560, 800);
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

    /// <summary>The meter's numbers (set by the plugin), for Settings → Meter's kept fights.</summary>
    public Func<IMeterSource?>? Meter { get; set; }

    /// <summary>Why the meter stopped, if it did (set by the plugin): said in a warning box on Settings → Meter.</summary>
    public Func<string?>? MeterProblem { get; set; }

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
                Frame.Window();
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
        catch (Exception ex)
        {
            // Said now and then, not every frame (Dalamud's own catch would log it sixty times a second).
            if (DateTime.UtcNow - drawFailedAt > TimeSpan.FromSeconds(10))
            {
                drawFailedAt = DateTime.UtcNow;
                Services.Log.Error(ex, "Raid Planner's window couldn't draw");
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
                    Notify("Something went wrong (see /xllog)", ToastKind.Bad);
                }
            }
        }
    }

    /// <summary>Runs after this frame's drawing is finished.</summary>
    private void Later(Action action) => later.Add(action);

    /// <summary>A toast: it worked, it didn't, or news (with its icon), and at most one action.</summary>
    private static void Notify(string text, ToastKind kind = ToastKind.News, Icon? icon = null, string? action = null, Action? onAction = null) =>
        Frame.Toast(text, kind, icon, action, onAction);

    // ---------------------------------------------------------------------------------------------
    // The header every screen below Home has.

    /// <summary>
    /// The header of every screen below Home (`.dw-hd`): the 40px square back button at the top left,
    /// the eyebrow in mono capitals and the screen's small callout beside it, and the screen's own
    /// buttons on the right (<paramref name="right"/> draws them from the top-left corner it's given,
    /// in <paramref name="rightWidth"/>). True when back was pressed.
    /// </summary>
    private static bool ScreenHeader(string backTip, string? eyebrow, string title, float rightWidth = 0, Action<Vector2>? right = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var tx = at.X + S(H) + S(12);
        var textW = at.X + w - tx - (rightWidth > 0 ? rightWidth + S(12) : 0);
        var back = Kit.IconButton("##back", at, Icon.ArrowLeft, backTip);
        float h;
        if (!string.IsNullOrEmpty(eyebrow))
        {
            // `.col` with 6px between, 2px down.
            var y = at.Y + S(2);
            Kit.Eyebrow(dl, new Vector2(tx, y), eyebrow, null, textW);
            y += Kit.EyebrowH + S(6);
            y += Kit.Callout(dl, new Vector2(tx, y), title, 17, textW, 2, 1.05f);
            h = MathF.Max(S(H), y - at.Y);
        }
        else
        {
            var th = Kit.CalloutHeight(title, 17, textW, 1);
            Kit.Callout(dl, new Vector2(tx, at.Y + (S(H) - th) / 2), title, 17, textW, 1);
            h = S(H);
        }
        right?.Invoke(new Vector2(at.X + w - rightWidth, at.Y));
        Kit.Reserve(at, w, h);
        return back;
    }

    // ---------------------------------------------------------------------------------------------
    // The question: the site's dialog over the window, its answers saying what happens.

    private sealed record Confirmation(string Title, string Body, string Yes, string No, Action OnYes, bool Danger, Icon? YesIcon);

    private Confirmation? confirm;
    private float confirmCardH;

    /// <summary>The question has been brought to the front once (after that, only when the plugin's window is clicked).</summary>
    private bool confirmFocused;

    /// <summary>When the question was asked (it fades in from then).</summary>
    private double confirmAt;

    private void Ask(string title, string body, string yes, string no, Action onYes, bool danger = false, Icon? yesIcon = null)
    {
        confirm = new Confirmation(title, body, yes, no, onYes, danger, yesIcon);
        confirmFocused = false;
        confirmAt = ImGui.GetTime();
    }

    /// <summary>The question goes unanswered: the window closed, or another screen opened.</summary>
    private void Dismiss()
    {
        confirm = null;
        confirmCardH = 0;
    }

    /// <summary>
    /// The question over the plugin's window: a window of its own the size of the plugin's, kept in
    /// front, which dims the window under Dalamud's title bar (rgb 4 4 5 at 74%) and takes every
    /// click; in its middle the site's dialog: raised near-solid #18181d with 20px corners, a white
    /// hairline and the shadow faked with rings, its title a small callout, its words, its two
    /// answers on the right (a destructive one in the danger outline). Escape is No. It fades in
    /// over 280ms (text can't scale crisply in ImGui, so it doesn't grow).
    /// </summary>
    private void DrawConfirm()
    {
        if (confirm == null) return;
        var c = confirm;
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var titleH = ImGui.GetCursorStartPos().Y;
        var under = winPos + new Vector2(0, MathF.Max(0, titleH));
        var underSize = winSize - new Vector2(0, MathF.Max(0, titleH));
        ImGui.SetNextWindowPos(under, ImGuiCond.Always);
        ImGui.SetNextWindowSize(underSize, ImGuiCond.Always);
        // To the front as it opens, and when the plugin's window is clicked; never taken from another window.
        if (!confirmFocused || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            ImGui.SetNextWindowFocus();
            confirmFocused = true;
        }
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
        var shown = ImGui.Begin("##rp-confirm-over", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoFocusOnAppearing);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);
        try
        {
            if (!shown) return;
            var old = Palette.Opacity;
            var since = (float)(ImGui.GetTime() - confirmAt);
            Palette.Opacity = ReducedMotion ? 1 : Model.Ease.Out(Math.Clamp(since / TSettle, 0, 1));
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(under, under + underSize, Palette.U32(Palette.Rgba(4, 4, 5, 0.74f)), S(Palette.RadiusLg), ImDrawFlags.RoundCornersBottom);
            var width = MathF.Min(S(440), winSize.X - S(48));
            var pad = S(24);
            var inner = width - pad * 2;
            var cardH = confirmCardH > 0 ? confirmCardH : S(200);
            var cardPos = new Vector2(winPos.X + (winSize.X - width) / 2, under.Y + MathF.Max(S(40), (underSize.Y - cardH) / 2));
            var rr = S(Palette.RadiusXl);
            // The shadow, faked with rings, inside this window (it would otherwise sit under the window it's over).
            dl.AddRect(cardPos - new Vector2(S(3)), cardPos + new Vector2(width, cardH) + new Vector2(S(3)), Palette.U32(Palette.WithAlpha(Palette.Black, 0.25f)), rr + S(3), ImDrawFlags.None, S(6));
            dl.AddRect(cardPos - new Vector2(S(10)), cardPos + new Vector2(width, cardH) + new Vector2(S(10)), Palette.U32(Palette.WithAlpha(Palette.Black, 0.12f)), rr + S(10), ImDrawFlags.None, S(8));
            dl.AddRectFilled(cardPos, cardPos + new Vector2(width, cardH), Palette.U32(Palette.Raised), rr);
            dl.AddRect(cardPos + new Vector2(0.5f), cardPos + new Vector2(width, cardH) - new Vector2(0.5f), Palette.U32(Palette.Line2), rr, ImDrawFlags.None, 1f);

            var at = cardPos + new Vector2(pad, pad);
            ImGui.SetCursorScreenPos(at);
            Kit.PushBox(at.X, inner);
            var th = Kit.Callout(dl, at, c.Title, 17, inner, 2, 1.05f);
            Kit.Reserve(at, inner, th);
            Kit.Gap(10);
            Txt.Para(new[] { new Run(c.Body, Face.Sans, 14, Palette.Text) }, 21.7f);
            Kit.Gap(20);
            var buttons = Kit.At;
            var yesStyle = c.Danger ? Btn.Danger : Btn.Tele;
            var yesW = Kit.ButtonWidth(c.Yes, BtnSize.Md, c.YesIcon, yesStyle);
            var noW = Kit.ButtonWidth(c.No);
            var close = false;
            if (Kit.Button("##no", new Vector2(buttons.X + inner - yesW - S(6.4f) - noW, buttons.Y), c.No)) close = true;
            if (Kit.Button("##yes", new Vector2(buttons.X + inner - yesW, buttons.Y), c.Yes, yesStyle, BtnSize.Md, c.YesIcon))
            {
                close = true;
                Later(c.OnYes);
            }
            // Escape is No, when the question has the keyboard (not an Escape meant for the game or another window).
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape)) close = true;
            Kit.Reserve(buttons, inner, Kit.ButtonHeight());
            Kit.PopBox();
            confirmCardH = Kit.At.Y - cardPos.Y + pad;
            Palette.Opacity = old;
            if (close && ReferenceEquals(confirm, c)) confirm = null;
        }
        finally
        {
            ImGui.End();
        }
    }
}
