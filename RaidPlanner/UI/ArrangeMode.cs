using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Arrange windows (0.8.0) as the windows read it: on or off, which take part, which is picked
/// (<see cref="ArrangeSession"/>; started and ended by <see cref="ArrangeWindow"/>). A window taking part shows
/// (with its sample), unlocked whatever its lock says (no title bar: its arranging head instead, which it's dragged
/// by), never click-through, with a dashed outline (ember when picked). Its settings' lock isn't changed until Done.
/// </summary>
public static class ArrangeMode
{
    /// <summary>The one session (the plugin has one of each window).</summary>
    public static ArrangeSession Session { get; } = new();

    public static bool On => Session.On;

    /// <summary>The mode is on and this window takes part.</summary>
    public static bool Shows(ArrangePart part) => Session.Takes(part);

    public static bool IsPicked(ArrangePart part) => Session.On && Session.Picked == part;

    // ---------------------------------------------------------------------------------------------
    // A window's look while it's arranged (the mock-up's: a 26px head with the move icon and its name, washed
    // white, or ember when picked; a 1.5px dashed edge, white at 55%, ember when picked).

    /// <summary>The head's height (CSS px): the window's chrome while it's arranged, in place of its title bar.</summary>
    public const float HeadCss = 26;

    /// <summary>The head's height (screen px).</summary>
    public static float HeadH => S(HeadCss);

    private static readonly Vector4 HeadGround = Palette.Rgba(13, 13, 16, 0.82f);
    private static readonly Vector4 HeadWash = Palette.Rgba(255, 255, 255, 0.06f);
    private static readonly Vector4 HeadWashPicked = Palette.Rgba(255, 138, 61, 0.18f);
    private static readonly Vector4 Edge = Palette.Rgba(255, 255, 255, 0.55f);

    /// <summary>Each window's dashes, worked out again only when its rectangle changes.</summary>
    private static readonly Dictionary<ArrangePart, (Vector2 Min, Vector2 Max, float Radius, List<Vector2[]> Dashes)> DashesOf = new();

    /// <summary>
    /// Inside the window, first: a press anywhere in it picks it; its head across its top (nothing in it takes the
    /// mouse, so a drag there moves the window), reserved so what follows starts under it.
    /// </summary>
    public static void Head(ArrangePart part)
    {
        var win = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            Session.Pick(part);
        var picked = IsPicked(part);
        var dl = ImGui.GetWindowDrawList();
        var h = HeadH;
        var r = ImGui.GetStyle().WindowRounding;
        var min = win;
        var max = new Vector2(win.X + size.X, win.Y + h);
        dl.PushClipRect(win, win + size, false);
        dl.AddRectFilled(min, max, Palette.U32(HeadGround), r, ImDrawFlags.RoundCornersTop);
        dl.AddRectFilled(min, max, Palette.U32(picked ? HeadWashPicked : HeadWash), r, ImDrawFlags.RoundCornersTop);
        var x = win.X + S(8);
        Icons.Draw(dl, Icon.Move, new Vector2(x, win.Y + (h - S(12)) / 2), S(12), Palette.White);
        x += S(12 + 6);
        Txt.Line(dl, new Vector2(x, win.Y), Txt.Fit(ArrangeParts.Label(part), Face.SansSemibold, 12, MathF.Max(0, win.X + size.X - S(8) - x)), Face.SansSemibold, 12,
            Palette.White, HeadCss);
        dl.PopClipRect();
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max)) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        ImGui.SetCursorScreenPos(win);
        ImGui.Dummy(new Vector2(size.X, h));
    }

    /// <summary>Inside the window, last: its dashed edge over everything, ember when picked.</summary>
    public static void Outline(ArrangePart part)
    {
        var win = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var r = ImGui.GetStyle().WindowRounding;
        var k = S(0.75f);
        var min = win + new Vector2(k);
        var max = win + size - new Vector2(k);
        if (!DashesOf.TryGetValue(part, out var d) || d.Min != min || d.Max != max || d.Radius != r)
        {
            d = (min, max, r, Dashes.Along(Dashes.RoundedRect(min, max, MathF.Max(0, r - k)), S(9), 0.55f));
            DashesOf[part] = d;
        }
        var dl = ImGui.GetWindowDrawList();
        var col = Palette.U32(IsPicked(part) ? Palette.Ember : Edge);
        var w = S(1.5f);
        dl.PushClipRect(win, win + size, false);
        foreach (var dash in d.Dashes)
            if (dash.Length >= 2)
                dl.AddPolyline(ref dash[0], dash.Length, col, ImDrawFlags.None, w);
        dl.PopClipRect();
    }
}
