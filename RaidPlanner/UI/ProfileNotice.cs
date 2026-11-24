using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The notice as a settings profile comes into effect (0.9.0; the mock-up's "Going in: the profile switches, and says so
/// once"): a small raised pill near the top of the game's screen, the profiles' layers icon in ember, what the profile
/// changes ("Ultimate profile: meter Detailed, callouts big") and Undo, which takes the profile out until you leave the
/// duty. It goes by itself after a few seconds (it waits while the mouse is on it), never takes focus, makes no sounds and
/// ignores Escape (no keys at all: CLAUDE.md). Leaving, a shorter one says your usual settings are back.
/// </summary>
public sealed class ProfileNotice : Window
{
    private readonly Fonts fonts;
    private DateTime failedAt;

    private string text = string.Empty;
    private bool undoable;
    private float used;
    private float life;
    private double lastFrame;

    /// <summary>Its padding (CSS px): 10 down, 14 at the left, 8 at the right, 10 between the parts.</summary>
    private const float PadY = 8, PadLeft = 14, PadRight = 8, Gap = 10, IconCss = 16;

    /// <summary>Its top's distance down the screen (a share of its height).</summary>
    private const float FromTop = 0.14f;

    public ProfileNotice(Fonts fonts)
        : base("Raid Codex profile###RaidPlannerProfileNotice")
    {
        this.fonts = fonts;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        AllowPinning = false;
        AllowClickthrough = false;
        IsOpen = false;
    }

    /// <summary>Undo pressed (set by the plugin): the profile out until you leave the duty.</summary>
    public Action? Undo { get; set; }

    /// <summary>Shows it with these words for a few seconds (Undo with them when <paramref name="canUndo"/>).</summary>
    public void Show(string words, bool canUndo)
    {
        text = words;
        undoable = canUndo;
        used = 0;
        life = canUndo ? 7f : 3.5f;
        lastFrame = 0;
        IsOpen = true;
    }

    /// <summary>Its size (screen px), worked out before Begin from its words and Undo.</summary>
    private Vector2 Measure(float maxWidth, out string shown)
    {
        var undoW = undoable ? Kit.ButtonWidth("Undo", BtnSize.Sm, null, Btn.Quiet) + S(Gap) : 0;
        var room = MathF.Max(S(80), maxWidth - S(PadLeft) - S(IconCss) - S(Gap) - undoW - S(PadRight));
        shown = Txt.Fit(text, Face.SansMedium, 13, room);
        var w = S(PadLeft) + S(IconCss) + S(Gap) + Txt.Width(shown, Face.SansMedium, 13) + undoW + S(undoable ? PadRight : PadLeft);
        var h = S(PadY) * 2 + MathF.Max(S(HSm), S(20));
        return new Vector2(MathF.Ceiling(w), MathF.Ceiling(h));
    }

    private string shownText = string.Empty;

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings;
        try
        {
            var now = ImGui.GetTime();
            var dt = lastFrame > 0 ? (float)Math.Clamp(now - lastFrame, 0, 0.25) : 0;
            lastFrame = now;
            if (!pointed || ReducedMotion) used += dt;
            if (used >= life) IsOpen = false;
            var vp = ImGui.GetMainViewport();
            var size = Measure(vp.WorkSize.X - S(40), out shownText);
            Size = UnitsOfScreen(size);
            SizeCondition = ImGuiCond.Always;
            ImGui.SetNextWindowPos(new Vector2(MathF.Round(vp.WorkPos.X + (vp.WorkSize.X - size.X) / 2), MathF.Round(vp.WorkPos.Y + vp.WorkSize.Y * FromTop)), ImGuiCond.Always);
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
        Theme.PushFrame(Palette.Raised, Palette.RadiusLg, Palette.Line2);
    }

    public override void PostDraw() => Theme.Pop();

    /// <summary>The mouse was on it last frame (its time waits).</summary>
    private bool pointed;

    public override void Draw()
    {
        Kit.ResetFrame();
        Frame.ResetFrame();
        Theme.PushInside();
        try
        {
            using (fonts.Push(Face.Sans, 13))
            {
                try
                {
                    DrawPill();
                }
                finally
                {
                    Frame.Unwind();
                }
            }
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
        finally
        {
            Theme.Pop();
        }
    }

    private void DrawPill()
    {
        var win = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        pointed = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
        var old = Palette.Opacity;
        var left = life - used;
        Palette.Opacity = ReducedMotion ? 1 : Math.Clamp(left / 0.4f, 0, 1);
        try
        {
            Kit.Rings(win + new Vector2(0, S(4)), win + size + new Vector2(0, S(4)), ImGui.GetStyle().WindowRounding, 6, 16, 0.25f, 0.1f);
            var dl = Kit.Dl;
            var x = win.X + S(PadLeft);
            Icons.Draw(dl, Icon.Layers, new Vector2(x, win.Y + (size.Y - S(IconCss)) / 2), S(IconCss), Palette.Ember);
            x += S(IconCss) + S(Gap);
            Txt.Line(dl, new Vector2(x, win.Y), shownText, Face.SansMedium, 13, Palette.White, size.Y / S(1));
            var undo = false;
            if (undoable)
            {
                var uw = Kit.ButtonWidth("Undo", BtnSize.Sm, null, Btn.Quiet);
                undo = Kit.Button("##profile-undo", new Vector2(win.X + size.X - S(PadRight) - uw, win.Y + (size.Y - S(HSm)) / 2), "Undo", Btn.Quiet, BtnSize.Sm, null, 0, false,
                    "Your usual settings back until you leave this duty");
            }
            ImGui.SetCursorScreenPos(win);
            ImGui.Dummy(size);
            if (undo)
            {
                IsOpen = false;
                Undo?.Invoke();
            }
        }
        finally
        {
            Palette.Opacity = old;
        }
    }

    private void Failed(Exception ex)
    {
        if (DateTime.UtcNow - failedAt <= TimeSpan.FromSeconds(10)) return;
        failedAt = DateTime.UtcNow;
        Services.Log.Error(ex, "Raid Codex's profile notice couldn't draw");
    }
}
