using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>A board object made ready to draw: its colours worked out once, and the letters it shows.</summary>
public readonly record struct DrawnObj(BoardObj O, Vector4 Fill, Vector4 Edge, Vector4 Ink, string? Glyph);

/// <summary>
/// The plan window's small board: a step's arena and objects, as the site draws them but plainly:
/// players as dots in their spot's colour with their label, area attacks by their shape (circles,
/// donuts, cones, lines and rectangles, crosses), enemies as rings with their facing, waymarks,
/// head markers, tethers and lines, text; anything else a small dot. Board units are the site's
/// (±500, the arena within ±420, y down; rotation in degrees, 0 north, clockwise).
/// </summary>
public static class PlanBoard
{
    /// <summary>The most objects drawn on a step (players, waymarks and enemies first; the rest after).</summary>
    public const int MaxDrawn = 160;

    private const float ArenaExtent = 420;

    /// <summary>The segments a circle is drawn with, whatever its size (so a frame's work doesn't vary with it).</summary>
    private const int Segments = 48;

    /// <summary>A step's objects as they're drawn: capped, coloured, in drawing order. Made once a step.</summary>
    public static List<DrawnObj> Prepare(IReadOnlyList<BoardObj> objects, out int hidden)
    {
        var kept = objects.Select((o, i) => (o, i)).OrderBy(x => KeepRank(x.o.Kind)).ThenBy(x => x.i).Take(MaxDrawn).ToList();
        hidden = Math.Max(0, objects.Count - kept.Count);
        return kept.OrderBy(x => DrawRank(x.o)).ThenBy(x => x.i).Select(x => Ready(x.o)).ToList();
    }

    private static int KeepRank(BoardKind k) => k switch
    {
        BoardKind.Player => 0,
        BoardKind.Waymark => 1,
        BoardKind.Enemy => 2,
        BoardKind.Marker => 3,
        BoardKind.Text => 4,
        BoardKind.Tether => 5,
        BoardKind.Other => 8,
        BoardKind.Line or BoardKind.Arrow => 7,
        _ => 6,
    };

    /// <summary>The site's drawing order: its layer (To front and To back), else its type's (<see cref="PlanJson.DefaultLayer"/>); in a layer, as listed.</summary>
    public static int DrawRank(BoardObj o) => o.Layer ?? PlanJson.DefaultLayer(o.Type);

    private static DrawnObj Ready(BoardObj o)
    {
        var c = Palette.Hex(o.Color);
        var area = o.Kind is BoardKind.Circle or BoardKind.Donut or BoardKind.Cone or BoardKind.Protean or BoardKind.Rect or BoardKind.LineStack or BoardKind.Cross;
        var fill = area ? Palette.WithAlpha(c, Math.Clamp(o.Opacity, 0.12f, 0.6f)) : c;
        var edge = area ? Palette.WithAlpha(c, 0.9f) : Mix(c, Palette.Ground, 0.35f);
        // Words on a light dot are dark, on a dark one light.
        var light = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z > 0.6f;
        var ink = o.Kind == BoardKind.Text ? c : light ? Palette.Ground : Palette.White;
        string? glyph = o.Kind switch
        {
            BoardKind.Player => Short(o.Label, 3),
            BoardKind.Waymark => Short(o.Variant ?? o.Label, 1),
            BoardKind.Text => Short(o.Label, 40),
            _ => null,
        };
        return new DrawnObj(o, fill, edge, ink, glyph);
    }

    private static string? Short(string? s, int max)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length <= max ? s : s[..max];
    }

    private static Vector4 Mix(Vector4 a, Vector4 b, float t) => a + (b - a) * t;

    /// <summary>Draws the board in a square from <paramref name="min"/>, <paramref name="size"/> pixels wide.</summary>
    public static void Draw(ImDrawListPtr dl, Vector2 min, float size, PlanArena arena, IReadOnlyList<DrawnObj> objects)
    {
        var max = min + new Vector2(size);
        var r = S(Palette.RadiusMd);
        dl.AddRectFilled(min, max, Palette.U32(Palette.Card), r);
        dl.PushClipRect(min, max, true);
        try
        {
            var k = size / 1000f;
            var center = min + new Vector2(size / 2);
            DrawArena(dl, center, k, arena);
            for (var i = 0; i < objects.Count; i++) DrawObj(dl, center, k, objects[i]);
        }
        finally
        {
            dl.PopClipRect();
        }
        dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.Line), r, ImDrawFlags.None, 1f);
    }

    private static void DrawArena(ImDrawListPtr dl, Vector2 c, float k, PlanArena arena)
    {
        var floor = Palette.U32(Palette.WithAlpha(Mix(Palette.Hex(arena.Color), Palette.Ground, 0.45f), 1f));
        var edge = Palette.U32(Palette.LineHover);
        var e = ArenaExtent * k;
        switch (arena.Shape)
        {
            case "rectangle":
                var half = arena.Aspect <= 1 ? new Vector2(e, e * arena.Aspect) : new Vector2(e / arena.Aspect, e);
                dl.AddRectFilled(c - half, c + half, floor);
                dl.AddRect(c - half, c + half, edge, 0, ImDrawFlags.None, 1f);
                break;
            case "square":
            case "custom" when arena.Outlines.Count == 0:
                // A custom arena with no outline drawn yet is a square, as on the site.
                dl.AddRectFilled(c - new Vector2(e), c + new Vector2(e), floor);
                dl.AddRect(c - new Vector2(e), c + new Vector2(e), edge, 0, ImDrawFlags.None, 1f);
                break;
            case "custom":
                Span<Vector2> pts = stackalloc Vector2[PlanJson.MaxOutlinePoints];
                foreach (var outline in arena.Outlines)
                {
                    var n = Math.Min(outline.Length / 2, pts.Length);
                    if (n < 3) continue;
                    for (var i = 0; i < n; i++) pts[i] = c + new Vector2(outline[i * 2], outline[i * 2 + 1]) * k;
                    dl.AddPolyline(ref pts[0], n, edge, ImDrawFlags.Closed, 1.5f);
                }
                break;
            default:
                dl.AddCircleFilled(c, e, floor, 64);
                dl.AddCircle(c, e, edge, 64, 1f);
                break;
        }
    }

    /// <summary>A point turned with the object (clockwise, from north) and placed on the screen.</summary>
    private static Vector2 At(Vector2 origin, float lx, float ly, float cos, float sin, float k) =>
        origin + new Vector2(lx * cos - ly * sin, lx * sin + ly * cos) * k;

    private static void DrawObj(ImDrawListPtr dl, Vector2 c, float k, in DrawnObj d)
    {
        var o = d.O;
        var p = c + new Vector2(o.X, o.Y) * k;
        var rad = o.Rotation * MathF.PI / 180f;
        var cos = MathF.Cos(rad);
        var sin = MathF.Sin(rad);
        var fill = Palette.U32(d.Fill);
        var edge = Palette.U32(d.Edge);
        switch (o.Kind)
        {
            case BoardKind.Circle:
            {
                var r = MathF.Max(S(2), o.Size * k);
                dl.AddCircleFilled(p, r, fill, Segments);
                dl.AddCircle(p, r, edge, Segments, 1f);
                break;
            }
            case BoardKind.Donut:
            {
                var inner = MathF.Min(o.Inner, o.Size - 1);
                var band = MathF.Max(1, (o.Size - inner) * k);
                dl.AddCircle(p, (o.Size + inner) / 2 * k, fill, Segments, band);
                dl.AddCircle(p, o.Size * k, edge, Segments, 1f);
                if (inner > 0) dl.AddCircle(p, inner * k, edge, Segments, 1f);
                break;
            }
            case BoardKind.Cone:
                Cone(dl, p, o.Size * k, rad, o.Angle, fill, edge);
                break;
            case BoardKind.Protean:
                Protean(dl, p, k, o, rad, fill, edge);
                break;
            case BoardKind.Rect:
            case BoardKind.LineStack:
            {
                var w = o.Width > 0 ? o.Width : o.Size;
                var l = o.Length > 0 ? o.Length : o.Size * 2;
                var top = o.Kind == BoardKind.LineStack ? -l : -l / 2;
                var bottom = o.Kind == BoardKind.LineStack ? 0 : l / 2;
                Quad(dl, p, -w / 2, top, w / 2, bottom, cos, sin, k, fill, edge);
                break;
            }
            case BoardKind.Cross:
            {
                var w = o.Width > 0 ? o.Width : o.Size / 3;
                var l = o.Length > 0 ? o.Length : o.Size * 2;
                Quad(dl, p, -w / 2, -l / 2, w / 2, l / 2, cos, sin, k, fill, edge);
                Quad(dl, p, -l / 2, -w / 2, l / 2, w / 2, cos, sin, k, fill, edge);
                break;
            }
            case BoardKind.Player:
            {
                var r = Math.Clamp(o.Size * k, S(5), S(14));
                dl.AddCircleFilled(p, r, fill, 20);
                dl.AddCircle(p, r, edge, 20, 1f);
                if (d.Glyph != null && r >= S(6)) Centered(dl, p, d.Glyph, Face.SansMedium, 12, d.Ink, r * 2.2f);
                break;
            }
            case BoardKind.Enemy:
            {
                var r = MathF.Max(S(5), o.Size * k);
                dl.AddCircle(p, r, Palette.U32(Palette.Hex(o.Color)), Segments, S(2));
                dl.AddLine(p, At(p, 0, -o.Size, cos, sin, k), Palette.U32(Palette.Hex(o.Color)), S(2));
                break;
            }
            case BoardKind.Waymark:
            {
                var r = MathF.Max(S(6), o.Size * k);
                var color = Palette.Hex(o.Color);
                var square = d.Glyph is "1" or "2" or "3" or "4";
                if (square)
                {
                    dl.AddRectFilled(p - new Vector2(r), p + new Vector2(r), Palette.U32(Palette.WithAlpha(color, 0.25f)));
                    dl.AddRect(p - new Vector2(r), p + new Vector2(r), Palette.U32(color), 0, ImDrawFlags.None, 1.5f);
                }
                else
                {
                    dl.AddCircleFilled(p, r, Palette.U32(Palette.WithAlpha(color, 0.25f)), 24);
                    dl.AddCircle(p, r, Palette.U32(color), 24, 1.5f);
                }
                if (d.Glyph != null && r >= S(6)) Centered(dl, p, d.Glyph, Face.SansSemibold, 15, color, r * 2.4f);
                break;
            }
            case BoardKind.Marker:
            {
                var r = S(5);
                dl.AddRectFilled(p - new Vector2(r), p + new Vector2(r), fill, S(2));
                dl.AddRect(p - new Vector2(r), p + new Vector2(r), Palette.U32(Palette.Ground), S(2), ImDrawFlags.None, 1f);
                break;
            }
            case BoardKind.Tether:
            case BoardKind.Line:
            case BoardKind.Arrow:
            {
                // Its end turned with the object, as the site turns the whole of it.
                var to = At(p, o.Dx, o.Dy, cos, sin, k);
                var col = Palette.U32(Palette.Hex(o.Color));
                dl.AddLine(p, to, col, o.Kind == BoardKind.Tether ? S(2) : S(1.5f));
                if (o.Kind == BoardKind.Arrow && Vector2.Distance(p, to) > S(4))
                {
                    var dir = Vector2.Normalize(to - p);
                    var side = new Vector2(-dir.Y, dir.X);
                    var head = S(6);
                    dl.AddTriangleFilled(to, to - dir * head + side * head * 0.5f, to - dir * head - side * head * 0.5f, col);
                }
                break;
            }
            case BoardKind.Text:
                if (d.Glyph != null) Centered(dl, p, d.Glyph, Face.SansMedium, 12, d.Ink, S(160));
                break;
            default:
                dl.AddCircleFilled(p, S(2.5f), Palette.U32(Palette.Hex(o.Color)), 8);
                break;
        }
    }

    /// <summary>A cone with its tip at <paramref name="p"/>, opening north turned by <paramref name="rad"/>, filled in parts of at most 90° (each convex).</summary>
    private static void Cone(ImDrawListPtr dl, Vector2 p, float r, float rad, float angle, uint fill, uint edge)
    {
        r = MathF.Max(S(3), r);
        if (angle >= 359.9f)
        {
            dl.AddCircleFilled(p, r, fill, Segments);
            dl.AddCircle(p, r, edge, Segments, 1f);
            return;
        }
        var spread = MathF.Max(1, angle) * MathF.PI / 180f;
        var dir = -MathF.PI / 2 + rad;
        var a0 = dir - spread / 2;
        var parts = Math.Max(1, (int)MathF.Ceiling(spread / (MathF.PI / 2)));
        var step = spread / parts;
        for (var i = 0; i < parts; i++)
        {
            dl.PathClear();
            dl.PathLineTo(p);
            dl.PathArcTo(p, r, a0 + step * i, a0 + step * (i + 1), 12);
            dl.PathFillConvex(fill);
        }
        dl.PathClear();
        dl.PathLineTo(p);
        dl.PathArcTo(p, r, a0, a0 + spread, 12 * parts);
        dl.PathStroke(edge, ImDrawFlags.Closed, 1f);
    }

    /// <summary>
    /// A protean, as the site draws it (its <c>proteanShape</c>): a bait for each player, fanned
    /// evenly round the middle, the first pointing the object's way. Cones (30° by default, never
    /// overlapping), or lines (60 wide) for its <c>line</c> variant.
    /// </summary>
    private static void Protean(ImDrawListPtr dl, Vector2 p, float k, BoardObj o, float rad, uint fill, uint edge)
    {
        var n = Math.Clamp(o.Count > 0 ? o.Count : 8, 1, 16);
        var step = MathF.PI * 2 / n;
        var r = o.Size;
        if (o.Variant == "line")
        {
            var w = MathF.Max(4, o.Width > 0 ? o.Width : 60);
            var start = n > 2 ? MathF.Min(r * 0.6f, w / 2 / MathF.Tan(MathF.PI / n)) : 0;
            for (var i = 0; i < n; i++)
            {
                var a = rad + step * i;
                Quad(dl, p, -w / 2, -r, w / 2, -start, MathF.Cos(a), MathF.Sin(a), k, fill, edge);
            }
            return;
        }
        var angle = Math.Clamp(o.Angle, 2, MathF.Max(2, 360f / n - 0.5f));
        for (var i = 0; i < n; i++) Cone(dl, p, r * k, rad + step * i, angle, fill, edge);
    }

    private static void Quad(ImDrawListPtr dl, Vector2 p, float x0, float y0, float x1, float y1, float cos, float sin, float k, uint fill, uint edge)
    {
        var a = At(p, x0, y0, cos, sin, k);
        var b = At(p, x1, y0, cos, sin, k);
        var c = At(p, x1, y1, cos, sin, k);
        var d = At(p, x0, y1, cos, sin, k);
        dl.AddQuadFilled(a, b, c, d, fill);
        dl.AddQuad(a, b, c, d, edge, 1f);
    }

    /// <summary>Words centred on a point, left out when they don't fit <paramref name="maxWidth"/>.</summary>
    private static void Centered(ImDrawListPtr dl, Vector2 p, string text, Face face, int px, Vector4 color, float maxWidth)
    {
        using (Txt.Fonts.Push(face, px))
        {
            var size = ImGui.CalcTextSize(text);
            if (size.X > maxWidth) return;
            dl.AddText(new Vector2(MathF.Round(p.X - size.X / 2), MathF.Round(p.Y - size.Y / 2)), Palette.U32(color), text);
        }
    }
}
