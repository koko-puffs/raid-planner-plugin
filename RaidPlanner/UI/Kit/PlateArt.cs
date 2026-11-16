using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>The meter's own glyphs (the mock-ups' 16-unit paths, filled): the roles' marks, hearts, a star, a ribbon, the limit break's bars.</summary>
public enum PlateGlyph
{
    Tank,
    Healer,
    Dps,
    Heart,
    BrokenHeart,
    Star,
    LimitBreak,
    Ribbon,
    Diamond,
}

/// <summary>
/// What the compact meter's themes draw with that the kit hadn't (round four's kit additions): words in
/// a theme's face (outlined as the game draws them, digits in cells for a face without tabular figures,
/// capitals spaced), the filled glyphs, a rounded rectangle with a vertical gradient (its vertices
/// shaded, as the Boss is), and the game's job icon in its socket. Nothing here allocates once warm.
/// </summary>
public static class PlateArt
{
    // ---------------------------------------------------------------------------------------------
    // Words in a theme's face.

    /// <summary>The game's 1px outline: the words eight times a pixel off, in black, then on top.</summary>
    private static readonly Vector2[] OutlineOffsets =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(-1, -1), new(1, -1), new(-1, 1),
    };

    private static readonly Vector4 OutlineInk = new(0, 0, 0, 0.85f);

    /// <summary>The widest digit of a face at a size (screen px), measured once a build of the face.</summary>
    private static readonly Dictionary<(Face, int, int), float> DigitW = new();

    /// <summary>The font's own line (screen px) in a theme's face.</summary>
    public static float Natural(ThemeFont f) => Txt.Natural(f.Face, f.Px);

    /// <summary>How wide the words are in a theme's face (capitals and digit cells as they're drawn).</summary>
    public static float Width(string text, ThemeFont f) => Run(default, Vector2.Zero, text, f, Vector4.Zero, false, null);

    /// <summary>
    /// Draws one line in a CSS line box <paramref name="lineCss"/> tall, its capitals' ink centred in it
    /// (<see cref="InkCentre"/>: a face whose ascent and descent are lopsided, Zen Maru Gothic's, sits where
    /// Geist's does; Geist's capitals are centred in its line already, so it doesn't move); returns its width.
    /// </summary>
    /// <param name="glow">An outline in this colour instead of the face's own (Neon's clock); null: the face's.</param>
    public static float Line(ImDrawListPtr dl, Vector2 lineTop, string text, ThemeFont f, Vector4 color, float lineCss, Vector4? glow = null) =>
        Run(dl, new Vector2(lineTop.X, lineTop.Y + S(lineCss) / 2), text, f, color, true, glow);

    /// <summary>Draws one line right-aligned at <paramref name="right"/>; returns its width.</summary>
    public static float LineRight(ImDrawListPtr dl, float right, float lineTop, string text, ThemeFont f, Vector4 color, float lineCss)
    {
        var w = Width(text, f);
        Line(dl, new Vector2(right - w, lineTop), text, f, color, lineCss);
        return w;
    }

    /// <summary>The words cut to fit a width in a theme's face (kept, as <see cref="Txt.Fit"/>; an outlined face's tracking kept out of the width).</summary>
    public static string Fit(string text, ThemeFont f, float width) =>
        f.Cells > 0 || f.Caps ? text : Txt.Fit(text, f.Face, f.Px, f.Outline ? width - OutlineTrack * (text.Length + 1) : width);

    /// <summary>An outlined face's tracking (screen px): the outline's pixel each side, so two glyphs' outlines never merge.</summary>
    private const float OutlineTrack = 1;

    /// <summary>Each font's capitals' ink middle (its own px, from its line's top), by the font; let go when the fonts are built again.</summary>
    private static readonly Dictionary<nint, float> InkMid = new();

    private static int inkOf = -1;

    /// <param name="pos">Drawing: its left and its line's middle (the ink is centred on it). Measuring: unused.</param>
    private static float Run(ImDrawListPtr dl, Vector2 pos, string text, ThemeFont f, Vector4 color, bool draw, Vector4? glow)
    {
        var ink = glow ?? (f.Outline ? new Vector4(OutlineInk.X, OutlineInk.Y, OutlineInk.Z, OutlineInk.W * color.W) : (Vector4?)null);
        var face = Txt.For(f.Face, text);
        if (f.Caps) text = Txt.Caps(text);
        using (Txt.Fonts.Push(face, f.Px))
        {
            // Outlined (Native's), a pixel more between glyphs: their outlines don't merge ("9k").
            var spacing = (f.Caps ? S(f.Px * 0.06f) : 0) + (f.Outline ? OutlineTrack : 0);
            var cells = f.Cells > 0 ? MathF.Max(S(f.Cells), DigitWidth(face, f.Px)) : 0;
            var font = ImGui.GetFont();
            var size = ImGui.GetFontSize();
            // A face whose first glyph reaches left of its pen, or last right of its advance (the game's
            // Jupiter), is moved in and measured with it, so the words never run into what's beside them;
            // an outlined one by its outline's pixel too.
            var (lead, tail) = Overhang(font, size, text, cells > 0);
            if (f.Outline)
            {
                lead += OutlineTrack;
                tail += OutlineTrack;
            }
            if (draw) pos.Y = InkCentre.Top(pos.Y, CapsMid(font, size));
            if (cells <= 0 && spacing <= 0)
            {
                if (draw) Put(dl, pos + new Vector2(lead, 0), text, color, ink);
                return lead + ImGui.CalcTextSize(text).X + tail;
            }
            var x = pos.X + lead;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var adv = font.GetCharAdvance(c) * size / font.FontSize;
                var digit = cells > 0 && c is >= '0' and <= '9';
                var w = digit ? cells : adv;
                if (draw) Put(dl, new Vector2(x + (digit ? (cells - adv) / 2 : 0), pos.Y), text.AsSpan(i, 1), color, ink);
                x += w + spacing;
            }
            return MathF.Max(0, x - pos.X - spacing + tail);
        }
    }

    /// <summary>How far the first glyph's ink reaches left of the pen and the last's right of its advance (screen px; 0 when they don't). A digit in a cell is centred in it, so has none.</summary>
    private static unsafe (float Lead, float Tail) Overhang(ImFontPtr font, float size, string text, bool cells)
    {
        if (text.Length == 0) return (0, 0);
        var k = size / font.FontSize;
        float lead = 0, tail = 0;
        var first = text[0];
        if (!(cells && first is >= '0' and <= '9'))
        {
            var g = font.FindGlyph(first);
            if (g != null) lead = MathF.Max(0, -g->X0 * k);
        }
        var last = text[^1];
        if (!(cells && last is >= '0' and <= '9'))
        {
            var g = font.FindGlyph(last);
            if (g != null) tail = MathF.Max(0, (g->X1 - g->AdvanceX) * k);
        }
        return (MathF.Ceiling(lead), MathF.Ceiling(tail));
    }

    /// <summary>The pushed font's capitals' ink middle ('H', from its line's top; screen px), read from ImGui's glyph once a font build. Without an 'H', the line's middle.</summary>
    private static unsafe float CapsMid(ImFontPtr font, float size)
    {
        var gen = Txt.Fonts.Generation;
        if (gen != inkOf)
        {
            InkMid.Clear();
            inkOf = gen;
        }
        var key = (nint)font.Handle;
        if (!InkMid.TryGetValue(key, out var mid))
        {
            var g = font.FindGlyphNoFallback('H');
            mid = g != null && g->Y1 > g->Y0 ? InkCentre.Mid(g->Y0, g->Y1) : font.FontSize / 2;
            InkMid[key] = mid;
        }
        return mid * size / font.FontSize;
    }

    private static void Put(ImDrawListPtr dl, Vector2 pos, ReadOnlySpan<char> text, Vector4 color, Vector4? outline)
    {
        var p = Txt.Snap(pos);
        if (outline is { } o4)
        {
            var ink = Palette.U32(o4);
            foreach (var o in OutlineOffsets) dl.AddText(p + o, ink, text);
        }
        dl.AddText(p, Palette.U32(color), text);
    }

    private static float DigitWidth(Face face, int px)
    {
        var font = ImGui.GetFont();
        var size = ImGui.GetFontSize();
        // By the font's size too: a rebuild at another scale measures again.
        var key = (face, px, (int)MathF.Round(size * 10));
        if (DigitW.TryGetValue(key, out var w)) return w;
        for (var c = '0'; c <= '9'; c++) w = MathF.Max(w, font.GetCharAdvance(c) * size / font.FontSize);
        // Kept only once the face is built (before, it's measured in Dalamud's font).
        if (Txt.Ready(face, px)) DigitW[key] = w;
        return w;
    }

    // ---------------------------------------------------------------------------------------------
    // Glyphs.

    private static readonly Dictionary<PlateGlyph, string> Paths = new()
    {
        [PlateGlyph.Tank] = "M8 1.6 13.4 3.6V8.1C13.4 11.3 11.1 13.4 8 14.6 4.9 13.4 2.6 11.3 2.6 8.1V3.6Z",
        [PlateGlyph.Healer] = "M6.2 2.2h3.6v4h4v3.6h-4v4H6.2v-4h-4V6.2h4Z",
        [PlateGlyph.Dps] = "M8 1.2 11.6 8 8 14.8 4.4 8Z",
        [PlateGlyph.Heart] = "M8 14.2S1.6 10.3 1.6 5.8C1.6 3.6 3.3 2.2 5.1 2.2c1.3 0 2.3.6 2.9 1.6.6-1 1.6-1.6 2.9-1.6 1.8 0 3.5 1.4 3.5 3.6 0 4.5-6.4 8.4-6.4 8.4Z",
        [PlateGlyph.BrokenHeart] =
            "M7.4 3.4 6.2 6.6l2.2 1.7-1.5 3.1.6 2.6S1.6 10.3 1.6 5.8C1.6 3.6 3.3 2.2 5.1 2.2c1 0 1.8.4 2.3 1.2ZM9 3.2c.5-.6 1.2-1 2-1 1.8 0 3.4 1.4 3.4 3.6 0 3.6-4 6.8-5.6 7.9l.8-4.6-2-1.8Z",
        [PlateGlyph.Star] = "M8 .6C8.6 5.4 10.6 7.4 15.4 8 10.6 8.6 8.6 10.6 8 15.4 7.4 10.6 5.4 8.6.6 8 5.4 7.4 7.4 5.4 8 .6Z",
        [PlateGlyph.LimitBreak] = "M2.2 10.6h3.2v3.2H2.2zM6.4 7h3.2v6.8H6.4zM10.6 3.2h3.2v10.6h-3.2z",
        // The ribbon: a strip with a notch cut in its foot (the mock-up's clip-path), 8 by 16.
        [PlateGlyph.Ribbon] = "M4 0 12 0 12 16 8 12.2 4 16Z",
        [PlateGlyph.Diamond] = "M8 2 14 8 8 14 2 8Z",
    };

    private sealed record Piece(Vector2[] Outline, int[] Triangles);

    private static readonly Dictionary<PlateGlyph, List<Piece>> Pieces = new();

    private static List<Piece> Get(PlateGlyph g)
    {
        if (Pieces.TryGetValue(g, out var list)) return list;
        list = new List<Piece>();
        foreach (var o in Polygon.Outlines(Paths[g], 6)) list.Add(new Piece(o, Polygon.Triangulate(o)));
        return Pieces[g] = list;
    }

    /// <summary>A glyph filled, its 16-unit box at <paramref name="min"/>, <paramref name="size"/> screen pixels square.</summary>
    public static void Glyph(ImDrawListPtr dl, PlateGlyph g, Vector2 min, float size, Vector4 color) => Glyph(dl, g, min, new Vector2(size), color);

    /// <summary>A glyph stretched to a box (the ribbon is taller than wide).</summary>
    public static void Glyph(ImDrawListPtr dl, PlateGlyph g, Vector2 min, Vector2 box, Vector4 color)
    {
        var k = box / 16f;
        var uv = ImGui.GetFontTexUvWhitePixel();
        var col = Palette.U32(color);
        foreach (var p in Get(g))
        {
            if (p.Triangles.Length < 3) continue;
            dl.PrimReserve(p.Triangles.Length, p.Triangles.Length);
            foreach (var i in p.Triangles) dl.PrimVtx(min + p.Outline[i] * k, uv, col);
        }
    }

    /// <summary>A role's mark.</summary>
    public static PlateGlyph RoleGlyph(string? role) => role switch
    {
        "tank" => PlateGlyph.Tank,
        "healer" => PlateGlyph.Healer,
        _ => PlateGlyph.Dps,
    };

    // ---------------------------------------------------------------------------------------------
    // Grounds.

    /// <summary>
    /// A rounded rectangle filled with a vertical gradient (top to bottom): its outline's vertices
    /// shaded by their height and cut as a fan (it's convex). Square-ended gradients are ImGui's own.
    /// </summary>
    public static void GradientRect(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector4 top, Vector4 bottom, float radius)
    {
        if (max.X - min.X < 1 || max.Y - min.Y < 1) return;
        radius = MathF.Min(radius, MathF.Min(max.X - min.X, max.Y - min.Y) / 2);
        if (radius < 0.5f)
        {
            var t = Palette.U32(top);
            var b = Palette.U32(bottom);
            dl.AddRectFilledMultiColor(min, max, t, t, b, b);
            return;
        }
        const int seg = 5;
        Span<Vector2> pts = stackalloc Vector2[4 * (seg + 1)];
        var n = 0;
        void Corner(Span<Vector2> into, Vector2 c, float a0)
        {
            for (var i = 0; i <= seg; i++)
            {
                var a = a0 + MathF.PI / 2 * i / seg;
                into[n++] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            }
        }
        Corner(pts, new Vector2(max.X - radius, min.Y + radius), -MathF.PI / 2);
        Corner(pts, new Vector2(max.X - radius, max.Y - radius), 0);
        Corner(pts, new Vector2(min.X + radius, max.Y - radius), MathF.PI / 2);
        Corner(pts, new Vector2(min.X + radius, min.Y + radius), MathF.PI);
        var uv = ImGui.GetFontTexUvWhitePixel();
        var h = max.Y - min.Y;
        uint Col(Vector2 p) => Palette.U32(Vector4.Lerp(top, bottom, Math.Clamp((p.Y - min.Y) / h, 0, 1)));
        dl.PrimReserve((n - 2) * 3, (n - 2) * 3);
        for (var i = 1; i < n - 1; i++)
        {
            dl.PrimVtx(pts[0], uv, Col(pts[0]));
            dl.PrimVtx(pts[i], uv, Col(pts[i]));
            dl.PrimVtx(pts[i + 1], uv, Col(pts[i + 1]));
        }
    }

    /// <summary>A dashed line across (the rule before you pinned at the foot).</summary>
    public static void Dashed(ImDrawListPtr dl, float x0, float x1, float y, Vector4 color)
    {
        var col = Palette.U32(color);
        var yy = MathF.Round(y) + 0.5f;
        var dash = S(3);
        for (var x = x0; x < x1; x += dash * 2) dl.AddLine(new Vector2(x, yy), new Vector2(MathF.Min(x + dash, x1), yy), col, 1f);
    }

    // ---------------------------------------------------------------------------------------------
    // The job icon.

    /// <summary>
    /// The game's job icon (its framed one, <see cref="JobIcons.Icon"/>), <paramref name="size"/> CSS px at
    /// <paramref name="min"/>: tinted grey when dead. A job not known, or an icon not loaded yet, is the
    /// role's mark in a socket (the theme's ground and ring: the role's colour unless it has its own).
    /// </summary>
    public static void JobIcon(ImDrawListPtr dl, Vector2 min, float size, string? job, string? role, Vector4 roleColor, ThemeIcon? socket, bool dead,
        Vector4 deadColor)
    {
        var s = S(size);
        var max = min + new Vector2(s);
        var icon = JobIcons.Icon(job);
        if (icon != 0)
        {
            try
            {
                var wrap = Services.Textures.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
                if (wrap.Width > 1)
                {
                    var tint = dead ? Palette.U32(new Vector4(0.55f, 0.55f, 0.58f, 0.75f)) : Palette.U32(Vector4.One);
                    dl.AddImage(wrap.Handle, min, max, Vector2.Zero, Vector2.One, tint);
                    return;
                }
            }
            catch (Exception)
            {
                // An icon the game can't give: the role's mark instead.
            }
        }
        var c = dead ? deadColor : roleColor;
        var round = socket?.Round ?? false;
        var bg = socket?.Bg;
        var ring = socket?.Ring ?? c;
        var ringW = MathF.Max(1, S(socket?.RingWidth ?? 1));
        if (round)
        {
            var center = min + new Vector2(s / 2);
            if (bg is { } b) dl.AddCircleFilled(center, s / 2, Palette.U32(b), 20);
            dl.AddCircle(center, s / 2 - ringW / 2, Palette.U32(ring), 20, ringW);
        }
        else
        {
            if (bg is { } b) dl.AddRectFilled(min, max, Palette.U32(b), S(3));
            dl.AddRect(min + new Vector2(ringW / 2), max - new Vector2(ringW / 2), Palette.U32(ring), S(3), ImDrawFlags.None, ringW);
        }
        var g = s * 0.62f;
        Glyph(dl, RoleGlyph(role), min + new Vector2((s - g) / 2), g, c);
    }

    /// <summary>
    /// A member's job icon where the windows drew their spot (since 0.7.0): centred, <paramref name="diameter"/> CSS px,
    /// in Ready Check's socket when the icon isn't there; the spot's label ("M1") in a tooltip when pointed at.
    /// </summary>
    public static void JobBadge(ImDrawListPtr dl, Vector2 center, float diameter, string? job, string? role, MeterRoleColours roles, bool dead = false,
        string? tip = null)
    {
        var t = MeterThemes.ReadyCheck;
        var s = S(diameter);
        var min = center - new Vector2(s / 2);
        JobIcon(dl, min, diameter, job, role, t.RoleColor(role, roles), t.Icon, dead, t.DeadIcon);
        if (!string.IsNullOrEmpty(tip) && Kit.Pointed(min, min + new Vector2(s))) Kit.Tooltip(tip);
    }
}
