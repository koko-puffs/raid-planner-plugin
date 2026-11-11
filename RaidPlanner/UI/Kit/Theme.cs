using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace RaidPlanner.UI;

/// <summary>
/// Pushes the windows' look onto ImGui, and holds Ready Check's sizes and timings. Before
/// ImGui.Begin only what can't touch the title bar's buttons: colours (the near-black bar, basalt
/// at 96% for the window, the white hairline edge), the 14px window corners and no window padding.
/// The title bar keeps Dalamud's own font and frame padding, so its ☰, ▼ and × sit and hit exactly
/// as in every other Dalamud window (Housing Studio's 0.3.0 pushed a font there, which made each
/// button's hit box about 41px wide while they stand 17px apart). Inside the window: no item spacing
/// (every gap is the mock-up's own, placed by the views), and ImGui's own controls in the tokens.
/// </summary>
public static class Theme
{
    // Ready Check's control heights (CSS px): --h, --h-sm, --h-lg.
    public const float H = 40;
    public const float HSm = 32;
    public const float HLg = 46;

    // Its motion (seconds): --t-press, --t-step, --t-settle, --t-resolve.
    public const float TPress = 0.09f;
    public const float TStep = 0.16f;
    public const float TSettle = 0.28f;
    public const float TResolve = 0.42f;

    /// <summary>The main window's padding (`.dw-hd`, `.dw-bd`, `.dw-ft`): 20 at the sides.</summary>
    public const float SidePad = 20;

    /// <summary>CSS pixels to screen pixels (Dalamud's global scale).</summary>
    public static float S(float px) => px * ImGuiHelpers.GlobalScale;

    public static bool ReducedMotion => Services.PluginInterface.UiBuilder.ShouldUseReducedMotion;

    private static readonly Stack<(int Colors, int Vars)> Pushed = new();

    /// <summary>Before ImGui.Begin: the frame's colours, corners, edge and padding; nothing that sizes the title bar.</summary>
    /// <param name="background">The window's ground (basalt at 96%, or the meter's own see-through one).</param>
    public static void PushFrame(Vector4? background = null, float radius = Palette.RadiusLg)
    {
        var colors = 0;
        var vars = 0;
        void C(ImGuiCol col, Vector4 v)
        {
            ImGui.PushStyleColor(col, v);
            colors++;
        }
        void F(ImGuiStyleVar v, float value)
        {
            ImGui.PushStyleVar(v, value);
            vars++;
        }

        C(ImGuiCol.WindowBg, background ?? Palette.Window);
        C(ImGuiCol.Border, Palette.WindowEdge);
        C(ImGuiCol.BorderShadow, Vector4.Zero);
        C(ImGuiCol.TitleBg, Palette.TitleBar);
        C(ImGuiCol.TitleBgActive, Palette.TitleBar);
        C(ImGuiCol.TitleBgCollapsed, Palette.TitleBar);
        C(ImGuiCol.Text, Palette.White);
        C(ImGuiCol.ResizeGrip, Palette.WithAlpha(Palette.White, 0.12f));
        C(ImGuiCol.ResizeGripHovered, Palette.WithAlpha(Palette.White, 0.22f));
        C(ImGuiCol.ResizeGripActive, Palette.WithAlpha(Palette.White, 0.3f));

        F(ImGuiStyleVar.WindowRounding, S(radius));
        F(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        vars++;
        Pushed.Push((colors, vars));
    }

    /// <summary>Inside the window: no spacing of ImGui's own, and its controls in the tokens' colours.</summary>
    public static void PushInside()
    {
        var colors = 0;
        var vars = 0;
        void C(ImGuiCol col, Vector4 v)
        {
            ImGui.PushStyleColor(col, v);
            colors++;
        }
        void V(ImGuiStyleVar v, Vector2 value)
        {
            ImGui.PushStyleVar(v, value);
            vars++;
        }
        void F(ImGuiStyleVar v, float value)
        {
            ImGui.PushStyleVar(v, value);
            vars++;
        }

        C(ImGuiCol.Text, Palette.Text);
        C(ImGuiCol.TextDisabled, Palette.Faint);
        C(ImGuiCol.ChildBg, Vector4.Zero);
        C(ImGuiCol.PopupBg, Palette.Raised);
        C(ImGuiCol.Border, Palette.Line2);
        C(ImGuiCol.FrameBg, Vector4.Zero);
        C(ImGuiCol.FrameBgHovered, Vector4.Zero);
        C(ImGuiCol.FrameBgActive, Vector4.Zero);
        C(ImGuiCol.TextSelectedBg, Palette.WithAlpha(Palette.White, 0.22f));
        C(ImGuiCol.NavHighlight, Palette.White);
        C(ImGuiCol.Header, Palette.Hover);
        C(ImGuiCol.HeaderHovered, Palette.Hover);
        C(ImGuiCol.HeaderActive, Palette.Picked);
        C(ImGuiCol.ModalWindowDimBg, Palette.Rgba(4, 4, 5, 0.74f));
        C(ImGuiCol.ScrollbarBg, Vector4.Zero);
        // The site's scrollbar: #34343a, lighter when pointed at.
        C(ImGuiCol.ScrollbarGrab, Palette.Hex(0x34343a));
        C(ImGuiCol.ScrollbarGrabHovered, Palette.Hex(0x4a4a52));
        C(ImGuiCol.ScrollbarGrabActive, Palette.Muted);

        V(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        V(ImGuiStyleVar.ItemInnerSpacing, Vector2.Zero);
        V(ImGuiStyleVar.FramePadding, Vector2.Zero);
        F(ImGuiStyleVar.FrameBorderSize, 0f);
        F(ImGuiStyleVar.ChildBorderSize, 0f);
        F(ImGuiStyleVar.PopupRounding, S(Palette.RadiusLg));
        F(ImGuiStyleVar.PopupBorderSize, 1f);
        V(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        F(ImGuiStyleVar.ScrollbarSize, S(6));
        F(ImGuiStyleVar.ScrollbarRounding, S(4));
        Pushed.Push((colors, vars));
    }

    public static void Pop()
    {
        if (Pushed.Count == 0) return;
        var (colors, vars) = Pushed.Pop();
        if (vars > 0) ImGui.PopStyleVar(vars);
        if (colors > 0) ImGui.PopStyleColor(colors);
    }

    /// <summary>
    /// The site's beating dot (`@keyframes rec`): a ring going out from 0 to 6px and fading, every
    /// 1.8 s, over its first 70%. Returns (ring's reach 0–1, its opacity); still under reduced motion.
    /// </summary>
    public static (float Reach, float Alpha) Beat()
    {
        if (ReducedMotion) return (0, 0);
        var t = (float)(ImGui.GetTime() % 1.8 / 1.8);
        if (t >= 0.7f) return (1, 0);
        var k = t / 0.7f;
        return (k, 0.6f * (1 - k));
    }

    /// <summary>The Boss's z's breathing (3.2 s, ease-in-out): 0 to 1 and back.</summary>
    public static float Breathe(float period = 3.2f, float delay = 0)
    {
        if (ReducedMotion) return 0.6f;
        var t = (float)((ImGui.GetTime() + delay) % period / period);
        return 0.5f - 0.5f * MathF.Cos(t * MathF.PI * 2);
    }

    /// <summary>A cast bar that runs over and over (`.cast-bar.is-running`): 0 to 1 every <paramref name="seconds"/>.</summary>
    public static float Running(float seconds)
    {
        if (ReducedMotion) return 0.6f;
        return (float)(ImGui.GetTime() % seconds / seconds);
    }
}
