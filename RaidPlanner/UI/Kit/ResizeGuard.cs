using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;

namespace RaidPlanner.UI;

/// <summary>
/// Keeps a resizable window's own controls (a lock, a menu's button) from losing a press to a resize
/// (<see cref="ResizeZones"/>). Dalamud turns on ImGui's resizing from every edge, and ImGui looks at
/// the edges and grips in <c>Begin</c>, before the window's buttons: so a control near an edge or a
/// bottom corner is moved in out of their reach (<see cref="ShiftLeft"/>), and while the mouse is over
/// one (where it was drawn last frame) the window is made not resizable for that frame
/// (<see cref="Over"/>), so a press there is always the control's. Resizing works everywhere else.
/// </summary>
internal sealed class ResizeGuard
{
    /// <summary>The controls drawn (this frame's in Draw; before Begin, last frame's).</summary>
    private readonly List<(Vector2 Min, Vector2 Max)> kept = new();
    private Vector2 wmin;
    private Vector2 wmax;

    /// <summary>The reach this frame (worked out in <see cref="Frame"/>).</summary>
    public ResizeReach Reach { get; private set; } = ResizeReach.None;

    /// <summary>
    /// Inside the window, before its controls and before a font is pushed (ImGui's font at Begin is the
    /// grip's): the window's rectangle and the reach, worked out as if it could be resized whether or
    /// not it can now (so its controls don't move as it's locked and unlocked).
    /// </summary>
    public void Frame()
    {
        kept.Clear();
        wmin = ImGui.GetWindowPos();
        wmax = wmin + ImGui.GetWindowSize();
        var style = ImGui.GetStyle();
        var touch = style.TouchExtraPadding;
        Reach = ResizeReach.Of(ImGui.GetFontSize(), style.WindowRounding, ImGui.GetIO().ConfigWindowsResizeFromEdges, System.MathF.Max(touch.X, touch.Y));
    }

    /// <summary>How far left a control at the window's right side moves to be out of reach (screen px).</summary>
    public float ShiftLeft(Vector2 min, Vector2 max) => ResizeZones.ShiftLeft(wmin, wmax, min, max, Reach);

    /// <summary>How far right a control at the window's left side moves to be out of reach (screen px).</summary>
    public float ShiftRight(Vector2 min, Vector2 max) => ResizeZones.ShiftRight(wmin, wmax, min, max, Reach);

    /// <summary>A control drawn this frame: while the mouse is over it, the window isn't resizable.</summary>
    public void Keep(Vector2 min, Vector2 max) => kept.Add((min, max));

    /// <summary>
    /// Before Begin: the mouse is over one of last frame's controls and isn't dragging (up, or pressed
    /// just now): then the window gets <c>NoResize</c> this frame. Not mid-drag, so a resize already
    /// under way isn't cut short as it passes over one.
    /// </summary>
    public bool Over()
    {
        if (kept.Count == 0) return false;
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseClicked(ImGuiMouseButton.Left)) return false;
        var p = ImGui.GetMousePos();
        foreach (var (min, max) in kept)
            if (ResizeZones.Inside(p, min - Vector2.One, max + Vector2.One))
                return true;
        return false;
    }

    /// <summary>Forgets the controls drawn (the window wasn't drawn: they're not where they were).</summary>
    public void Clear() => kept.Clear();
}
