using System;
using System.Numerics;

namespace RaidPlanner.UI.Model;

/// <summary>
/// How far into an unlocked window its edges and corners take a press for resizing (Dear ImGui 1.88,
/// as Dalamud has it, with Dalamud's <c>ConfigWindowsResizeFromEdges</c> on). ImGui checks these in
/// <c>Begin</c>, before the window's own buttons, so a button under one loses the press to a resize.
/// Screen pixels.
/// </summary>
/// <param name="Border">How far either side of each edge takes a press (ImGui's <c>WINDOWS_HOVER_PADDING</c>, 4, or the touch padding when larger); 0 without resizing from the edges.</param>
/// <param name="Corner">How far in from a bottom corner its grip reaches, along both sides (three quarters of the grip's drawn size, which grows with the font).</param>
/// <param name="Edges">Resizing from every edge (and from the bottom-left corner too), not only the bottom-right grip.</param>
public readonly record struct ResizeReach(float Border, float Corner, bool Edges)
{
    /// <summary>ImGui's <c>WINDOWS_HOVER_PADDING</c>: how far either side of an edge it takes a press.</summary>
    public const float HoverPadding = 4;

    /// <summary>No resizing at all.</summary>
    public static readonly ResizeReach None = new(0, 0, false);

    /// <summary>
    /// The reach for a window drawn with this font size (ImGui's at <c>Begin</c>, screen px) and window
    /// rounding, as ImGui 1.88 works it out: the grip is drawn
    /// <c>floor(max(font × 1.35, rounding + 1 + font × 0.2))</c> big and takes a press three quarters of that in.
    /// </summary>
    public static ResizeReach Of(float fontSize, float rounding, bool fromEdges, float touchPadding = 0)
    {
        var drawn = MathF.Floor(MathF.Max(fontSize * 1.35f, rounding + 1 + fontSize * 0.2f));
        var corner = MathF.Floor(drawn * 0.75f);
        var border = fromEdges ? MathF.Max(HoverPadding, touchPadding) : 0;
        return new ResizeReach(border, corner, fromEdges);
    }

    /// <summary>How far in from the side a control in a bottom corner keeps (the grip's reach and <see cref="HoverPadding"/> more).</summary>
    public float CornerInset => Corner + HoverPadding;
}

/// <summary>
/// Keeping a window's controls clear of where a press would resize it (<see cref="ResizeReach"/>):
/// whether a control meets a grab zone, and how far to move a control at the window's right (or left)
/// side so it doesn't. The edges are taken along their whole length (ImGui leaves the corners' grips
/// out of them; counting them in only keeps more room).
/// </summary>
public static class ResizeZones
{
    /// <summary>Whether the control (<paramref name="min"/>–<paramref name="max"/>) meets a grab zone of the window (<paramref name="wmin"/>–<paramref name="wmax"/>).</summary>
    public static bool Hits(Vector2 wmin, Vector2 wmax, Vector2 min, Vector2 max, ResizeReach r)
    {
        if (r.Corner <= 0 && r.Border <= 0) return false;
        var b = r.Border;
        // The bottom-right grip (always), the bottom-left (resizing from the edges).
        if (Meets(min, max, new Vector2(wmax.X - r.Corner, wmax.Y - r.Corner), wmax + new Vector2(b))) return true;
        if (r.Edges && Meets(min, max, new Vector2(wmin.X - b, wmax.Y - r.Corner), new Vector2(wmin.X + r.Corner, wmax.Y + b))) return true;
        if (!r.Edges || b <= 0) return false;
        return Meets(min, max, new Vector2(wmin.X - b, wmin.Y - b), new Vector2(wmin.X + b, wmax.Y + b)) // left
            || Meets(min, max, new Vector2(wmax.X - b, wmin.Y - b), new Vector2(wmax.X + b, wmax.Y + b)) // right
            || Meets(min, max, new Vector2(wmin.X - b, wmin.Y - b), new Vector2(wmax.X + b, wmin.Y + b)) // top
            || Meets(min, max, new Vector2(wmin.X - b, wmax.Y - b), new Vector2(wmax.X + b, wmax.Y + b)); // bottom
    }

    /// <summary>
    /// How far left (0 or more) a control at the window's right side moves to clear the right edge and,
    /// when it's level with the bottom-right grip, the grip and <see cref="ResizeReach.HoverPadding"/> more.
    /// (The top and bottom edges are the layout's to keep clear: moving sideways doesn't.)
    /// </summary>
    public static float ShiftLeft(Vector2 wmin, Vector2 wmax, Vector2 min, Vector2 max, ResizeReach r)
    {
        var limit = r.Edges ? wmax.X - r.Border : wmax.X;
        if (r.Corner > 0 && max.Y > wmax.Y - r.Corner) limit = MathF.Min(limit, wmax.X - r.CornerInset);
        return MathF.Max(0, max.X - limit);
    }

    /// <summary>As <see cref="ShiftLeft"/>, for a control at the window's left side: how far right it moves (the bottom-left grip is there only resizing from the edges).</summary>
    public static float ShiftRight(Vector2 wmin, Vector2 wmax, Vector2 min, Vector2 max, ResizeReach r)
    {
        if (!r.Edges) return 0;
        var limit = wmin.X + r.Border;
        if (r.Corner > 0 && max.Y > wmax.Y - r.Corner) limit = MathF.Max(limit, wmin.X + r.CornerInset);
        return MathF.Max(0, limit - min.X);
    }

    /// <summary>Whether a point is in a rectangle (its edges in).</summary>
    public static bool Inside(Vector2 p, Vector2 min, Vector2 max) => p.X >= min.X && p.Y >= min.Y && p.X <= max.X && p.Y <= max.Y;

    /// <summary>Two rectangles overlap (touching edges don't).</summary>
    private static bool Meets(Vector2 amin, Vector2 amax, Vector2 bmin, Vector2 bmax) =>
        amin.X < bmax.X && amax.X > bmin.X && amin.Y < bmax.Y && amax.Y > bmin.Y;
}
