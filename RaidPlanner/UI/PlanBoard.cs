using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>A board object made ready to draw: its colour worked out once, and the letters it shows.</summary>
public readonly record struct DrawnObj(BoardObj O, Vector4 Color, string? Glyph);

/// <summary>
/// The plan window's board, in the board's language (the site's board, as the mock-ups draw it in
/// assets/board.js): the arena's floor with its grid and edge (a wall, an ember death wall, a dashed
/// drop), the bezel's ticks every 15° and the compass round a round arena; telegraphs with their
/// soft fill, inner glow and hot rim (later hits hollow and numbered); stacks, spreads, busters,
/// towers, exaflares, gazes, knockbacks and the rest drawn as themselves; enemies as the Boss's
/// shield; players as spots on a dark halo, you under the reticle. Board units are the site's
/// (±500, the arena within ±420, y down; rotation in degrees, 0 north, clockwise); the marks are
/// sized by the arena's yalms. A real arena (<c>poly</c>, since 0.5.0) is its polygons, holes cut
/// out, made ready once a plan (<see cref="ArenaFloor"/>).
/// </summary>
public static class PlanBoard
{
    /// <summary>The most objects drawn on a step (players, waymarks and enemies first; the rest after).</summary>
    public const int MaxDrawn = 160;

    private const float ArenaExtent = 420;

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
        BoardKind.Line or BoardKind.Arrow or BoardKind.Pen => 7,
        _ => 6,
    };

    /// <summary>The site's drawing order: its layer (To front and To back), else its type's (<see cref="PlanJson.DefaultLayer"/>); in a layer, as listed.</summary>
    public static int DrawRank(BoardObj o) => o.Layer ?? PlanJson.DefaultLayer(o.Type);

    private static DrawnObj Ready(BoardObj o)
    {
        string? glyph = o.Kind switch
        {
            BoardKind.Player => Short(o.Label, 3),
            BoardKind.Waymark => Short(o.Variant ?? o.Label, 1),
            BoardKind.Text => Short(o.Label, 40),
            _ => null,
        };
        return new DrawnObj(o, Palette.Hex(o.Color), glyph);
    }

    private static string? Short(string? s, int max)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length <= max ? s : s[..max];
    }

    /// <summary>
    /// Draws the board in a square from <paramref name="min"/>, <paramref name="size"/> pixels wide,
    /// clipped to it. <paramref name="thumb"/> leaves out the bezel and the compass (a small board);
    /// <paramref name="you"/> is your spot's label (its player takes the reticle).
    /// </summary>
    public static void Draw(ImDrawListPtr dl, Vector2 min, float size, PlanArena arena, IReadOnlyList<DrawnObj> objects, bool thumb = false, string? you = null)
    {
        var max = min + new Vector2(size);
        dl.PushClipRect(min, max, true);
        try
        {
            var k = size / 1000f;
            var center = min + new Vector2(size / 2);
            var unit = ArenaExtent * k / MathF.Max(1, arena.Yalms);
            DrawArena(dl, center, k, unit, arena, thumb);
            for (var i = 0; i < objects.Count; i++) DrawObj(dl, center, k, unit, objects[i], you);
        }
        finally
        {
            dl.PopClipRect();
        }
    }

    /// <summary>A custom arena's outlines, each cut into triangles once (indices into its points) and kept with it.</summary>
    private static readonly ConditionalWeakTable<float[], int[]> Cut = new();

    /// <summary>The triangles of an outline's first <paramref name="n"/> points (board units: the cut doesn't depend on the board's size).</summary>
    private static int[] TrianglesOf(float[] outline, int n)
    {
        if (Cut.TryGetValue(outline, out var tris)) return tris;
        var pts = new Vector2[n];
        for (var i = 0; i < n; i++) pts[i] = new Vector2(outline[i * 2], outline[i * 2 + 1]);
        tris = Polygon.Triangulate(pts);
        Cut.AddOrUpdate(outline, tris);
        return tris;
    }

    private static void DrawArena(ImDrawListPtr dl, Vector2 c, float k, float unit, PlanArena arena, bool thumb)
    {
        var floorColor = arena.Color == PlanArena.Default.Color || arena.Color == 0x2a2a2f ? Palette.Floor : Palette.Hex(arena.Color);
        var floor = Palette.U32(floorColor);
        var lit = Palette.U32(Palette.Rgba(255, 255, 255, 0.05f));
        var grid = Palette.U32(Palette.Rgba(255, 255, 255, 0.055f));
        var e = ArenaExtent * k;
        var death = arena.Edge == "death";
        var edgeCol = death ? Palette.U32(Palette.Ember) : Palette.U32(Palette.Hex(0x3a3b45));
        var edgeW = MathF.Max(1, (death ? 0.34f : 0.2f) * unit);
        var drop = arena.Edge == "drop";
        var radialGrid = arena.Grid is "radial" or "both";
        var squareGrid = arena.Grid is "square" or "both";
        switch (arena.Shape)
        {
            case "poly" when ArenaFloor.Of(arena) is { } real:
                DrawReal(dl, c, k, real, floor, grid, squareGrid, radialGrid, arena.Edge, edgeCol, edgeW);
                break;
            case "rectangle":
            case "square":
            case "custom" when arena.Outlines.Count == 0:
            case "poly":
            {
                var half = arena.Shape == "rectangle"
                    ? arena.Aspect <= 1 ? new Vector2(e, e * arena.Aspect) : new Vector2(e / arena.Aspect, e)
                    : new Vector2(e);
                dl.AddRectFilled(c - half, c + half, floor);
                dl.AddRectFilled(c - half * 0.7f, c + half * 0.7f, lit);
                if (squareGrid || radialGrid)
                    foreach (var f in new[] { -0.5f, 0f, 0.5f })
                    {
                        dl.AddLine(c + new Vector2(half.X * f, -half.Y), c + new Vector2(half.X * f, half.Y), grid, 1f);
                        dl.AddLine(c + new Vector2(-half.X, half.Y * f), c + new Vector2(half.X, half.Y * f), grid, 1f);
                    }
                if (drop)
                {
                    var a = c - half;
                    var b = c + half;
                    var col = Palette.U32(Palette.Hex(0x3a3b45));
                    BoardArt.DashedLine(dl, a, new Vector2(b.X, a.Y), col, edgeW, 0.9f * unit, 0.6f * unit);
                    BoardArt.DashedLine(dl, new Vector2(b.X, a.Y), b, col, edgeW, 0.9f * unit, 0.6f * unit);
                    BoardArt.DashedLine(dl, b, new Vector2(a.X, b.Y), col, edgeW, 0.9f * unit, 0.6f * unit);
                    BoardArt.DashedLine(dl, new Vector2(a.X, b.Y), a, col, edgeW, 0.9f * unit, 0.6f * unit);
                }
                else dl.AddRect(c - half, c + half, edgeCol, 0, ImDrawFlags.None, edgeW);
                break;
            }
            case "custom":
            {
                Span<Vector2> pts = stackalloc Vector2[PlanJson.MaxOutlinePoints];
                var fill = Palette.U32(Palette.WithAlpha(floorColor, 0.6f));
                var uv = ImGui.GetFontTexUvWhitePixel();
                foreach (var outline in arena.Outlines)
                {
                    var n = Math.Min(outline.Length / 2, pts.Length);
                    if (n < 3) continue;
                    for (var i = 0; i < n; i++) pts[i] = c + new Vector2(outline[i * 2], outline[i * 2 + 1]) * k;
                    // Its floor, from the outline cut into triangles once (it may be concave: an L, a cross,
                    // which ImGui's convex fill would spill out of), then its edge.
                    var tris = TrianglesOf(outline, n);
                    if (tris.Length >= 3)
                    {
                        dl.PrimReserve(tris.Length, tris.Length);
                        foreach (var t in tris) dl.PrimVtx(pts[t], uv, fill);
                    }
                    dl.AddPolyline(ref pts[0], n, edgeCol, ImDrawFlags.Closed, edgeW);
                }
                break;
            }
            default:
            {
                dl.AddCircleFilled(c, e, floor, 72);
                dl.AddCircleFilled(c, e * 0.7f, lit, 64);
                // The rp-floor gradient's dark rim.
                dl.AddCircle(c, e * 0.96f, Palette.U32(Palette.Rgba(0, 0, 0, 0.18f)), 72, e * 0.08f);
                if (radialGrid)
                {
                    for (var r = e / 4; r < e - 0.5f; r += e / 4) dl.AddCircle(c, r, grid, 64, 1f);
                    for (var d = 0f; d < 180f; d += 45f) dl.AddLine(BoardArt.Pol(c, e, d), BoardArt.Pol(c, e, d + 180), grid, 1f);
                }
                if (squareGrid)
                    foreach (var f in new[] { -0.5f, 0f, 0.5f })
                    {
                        var y = e * f;
                        var x = MathF.Sqrt(MathF.Max(0, e * e - y * y));
                        dl.AddLine(c + new Vector2(-x, y), c + new Vector2(x, y), grid, 1f);
                        dl.AddLine(c + new Vector2(y, -x), c + new Vector2(y, x), grid, 1f);
                    }
                if (drop) BoardArt.DashedArc(dl, c, e, 0, 360, Palette.U32(Palette.Hex(0x3a3b45)), edgeW, 0.9f * unit, 0.6f * unit);
                else
                {
                    if (death) dl.AddCircle(c, e, Palette.U32(Palette.WithAlpha(Palette.Ember, 0.3f)), 72, edgeW * 3);
                    dl.AddCircle(c, e, edgeCol, 72, edgeW);
                }
                if (!thumb)
                {
                    // The bezel: a tick every 15°, longer at 45° and longest at the cardinals; the compass outside.
                    var tick = Palette.U32(Palette.Rgba(255, 255, 255, 0.2f));
                    for (var d = 0; d < 360; d += 15)
                    {
                        var major = d % 90 == 0;
                        var a = BoardArt.Pol(c, e + 0.7f * unit, d);
                        var b = BoardArt.Pol(c, e + (major ? 1.7f : d % 45 == 0 ? 1.3f : 1.05f) * unit, d);
                        dl.AddLine(a, b, tick, 1f);
                    }
                    if (unit * 1.1f >= S(6))
                        foreach (var (l, d) in new[] { ("N", 0f), ("E", 90f), ("S", 180f), ("W", 270f) })
                        {
                            var p = BoardArt.Pol(c, e + 3.3f * unit, d);
                            var t = Txt.Measure(l, Face.MonoSemibold, 10);
                            Txt.Draw(dl, p - t / 2, l, Face.MonoSemibold, 10, Palette.Hex(0x7b7b86));
                        }
                }
                break;
            }
        }
    }

    /// <summary>The most corners of a floor's triangles given to ImGui at once (well under its 65,536 a draw).</summary>
    private const int FloorChunk = 3 * 4096;

    /// <summary>
    /// A real arena (<c>poly</c>), as the site draws it (a custom arena's way, its holes even-odd): each polygon's floor
    /// from its triangles (cut once, <see cref="ArenaFloor"/>), the grid cut to the floor, and the rim along every ring,
    /// outlines and holes alike (a wall, an ember death wall with its glow, or a dashed drop). No bezel, no compass (as
    /// the board's other shapes but the round one).
    /// </summary>
    private static void DrawReal(ImDrawListPtr dl, Vector2 c, float k, ArenaFloor real, uint floor, uint grid, bool squareGrid, bool radialGrid,
        string edge, uint edgeCol, float edgeW)
    {
        var uv = ImGui.GetFontTexUvWhitePixel();
        foreach (var tris in real.Floors)
            for (var i = 0; i < tris.Length; i += FloorChunk)
            {
                var n = Math.Min(FloorChunk, tris.Length - i);
                dl.PrimReserve(n, n);
                for (var j = 0; j < n; j++) dl.PrimVtx(c + tris[i + j] * k, uv, floor);
            }
        if (squareGrid)
            foreach (var (a, b) in real.SquareGrid) dl.AddLine(c + a * k, c + b * k, grid, 1f);
        if (radialGrid)
            foreach (var (a, b) in real.RadialGrid) dl.AddLine(c + a * k, c + b * k, grid, 1f);

        Span<Vector2> pts = stackalloc Vector2[Rings.MaxPoints + 2];
        if (edge == "drop")
        {
            var col = Palette.U32(Palette.Hex(0x3a3b45));
            foreach (var dash in real.Dashes)
            {
                var n = Math.Min(dash.Length, pts.Length);
                if (n < 2) continue;
                for (var i = 0; i < n; i++) pts[i] = c + dash[i] * k;
                dl.AddPolyline(ref pts[0], n, col, ImDrawFlags.None, edgeW);
            }
            return;
        }
        var glow = Palette.U32(Palette.WithAlpha(Palette.Ember, 0.3f));
        foreach (var rim in real.Rims)
        {
            var n = Math.Min(rim.Length, pts.Length);
            if (n < 3) continue;
            for (var i = 0; i < n; i++) pts[i] = c + rim[i] * k;
            if (edge == "death") dl.AddPolyline(ref pts[0], n, glow, ImDrawFlags.Closed, edgeW * 3);
            dl.AddPolyline(ref pts[0], n, edgeCol, ImDrawFlags.Closed, edgeW);
        }
    }

    private static void DrawObj(ImDrawListPtr dl, Vector2 c, float k, float unit, in DrawnObj d, string? you)
    {
        var o = d.O;
        var p = c + new Vector2(o.X, o.Y) * k;
        var col = d.Color;
        var rot = o.Rotation;
        var r = MathF.Max(S(2), o.Size * k);
        var rim = MathF.Max(1, 0.17f * unit);
        var later = o.Order > 1;
        switch (o.Kind)
        {
            case BoardKind.Circle:
                BoardArt.TeleCircle(dl, p, r, col, rim, 0.2f, later);
                break;
            case BoardKind.Danger:
                BoardArt.TeleCircle(dl, p, r, col, rim, 0.24f, later);
                break;
            case BoardKind.Donut:
                BoardArt.TeleDonut(dl, p, r, o.Inner * k, col, rim, later);
                break;
            case BoardKind.Cone:
                BoardArt.TeleCone(dl, p, r, o.Angle, rot, col, rim, later);
                break;
            case BoardKind.Protean:
                Protean(dl, p, k, o, col, rim, later);
                break;
            case BoardKind.Rect:
            {
                var w = (o.Width > 0 ? o.Width : o.Size) * k;
                var l = (o.Length > 0 ? o.Length : o.Size * 2) * k;
                BoardArt.TeleRect(dl, p, -w / 2, -l / 2, w / 2, l / 2, rot, col, rim, later);
                break;
            }
            case BoardKind.LineStack:
            {
                var w = (o.Width > 0 ? o.Width : 60) * k;
                var l = (o.Length > 0 ? o.Length : o.Size * 2) * k;
                BoardArt.TeleRect(dl, p, -w / 2, -l, w / 2, 0, rot, col, rim, later);
                var chev = Palette.U32(col);
                foreach (var f in new[] { 0.3f, 0.6f })
                {
                    BoardArt.Chevron(dl, BoardArt.Turn(p, new Vector2(-w / 2 - 0.5f * unit, -l * f), rot), rot + 90, 1.3f * unit, chev, MathF.Max(1, 0.26f * unit));
                    BoardArt.Chevron(dl, BoardArt.Turn(p, new Vector2(w / 2 + 0.5f * unit, -l * f), rot), rot - 90, 1.3f * unit, chev, MathF.Max(1, 0.26f * unit));
                }
                break;
            }
            case BoardKind.Cross:
            {
                var w = (o.Width > 0 ? o.Width : o.Size / 3) * k;
                var l = (o.Length > 0 ? o.Length : o.Size * 2) * k;
                BoardArt.TeleCross(dl, p, w, l / 2, rot, col, rim, later);
                break;
            }
            case BoardKind.Stack:
                BoardArt.Stack(dl, p, r, col, unit, rim, later);
                if (o.Count > 1)
                    for (var i = 0; i < Math.Min(o.Count, 8); i++)
                        dl.AddCircleFilled(p + new Vector2((i - (o.Count - 1) / 2f) * 0.62f * unit, r + 1.1f * unit), MathF.Max(1, 0.2f * unit), Palette.U32(col), 8);
                break;
            case BoardKind.Spread:
                BoardArt.Spread(dl, p, r, col, unit, rim, later);
                break;
            case BoardKind.Defamation:
                BoardArt.Defamation(dl, p, r, col, unit, rim, later);
                break;
            case BoardKind.Tankbuster:
                BoardArt.Buster(dl, p, r, col, unit, rim, o.Variant == "cleave", later);
                break;
            case BoardKind.SharedBuster:
                BoardArt.SharedBuster(dl, p, r, col, unit, rim, later);
                break;
            case BoardKind.Tower:
                BoardArt.Tower(dl, p, r, col, unit, o.Count > 0 ? o.Count : 1, o.Variant == "tank");
                break;
            case BoardKind.Proximity:
                BoardArt.Proximity(dl, p, r, unit);
                break;
            case BoardKind.Puddle:
                BoardArt.Puddle(dl, p, r, col, unit);
                break;
            case BoardKind.Landmine:
                BoardArt.Landmine(dl, p, r, unit, rim);
                break;
            case BoardKind.Safe:
                BoardArt.Safe(dl, p, MathF.Max(S(5), MathF.Min(r, 1.05f * unit * 2)), unit);
                break;
            case BoardKind.Moving:
            {
                var gap = (o.Spacing > 0 ? o.Spacing : o.Size * 1.3f) * k;
                var ahead = o.Reach > 0 && gap > 0 ? (int)MathF.Round(o.Reach * k / gap) : 3;
                BoardArt.Moving(dl, p, r, rot, gap, Math.Clamp(ahead, 1, 8), unit, rim, later);
                break;
            }
            case BoardKind.Gaze:
                BoardArt.Gaze(dl, p, MathF.Max(S(4), r / 1.75f));
                break;
            case BoardKind.Knockback:
            case BoardKind.DrawIn:
                BoardArt.Flow(dl, p, r, unit, rim, o.Kind == BoardKind.DrawIn);
                break;
            case BoardKind.KnockUp:
                BoardArt.KnockUp(dl, p, r, unit, rim);
                break;
            case BoardKind.SideKnockback:
            {
                var w = (o.Width > 0 ? o.Width : o.Size) * k;
                var l = (o.Length > 0 ? o.Length : o.Size * 2) * k;
                BoardArt.SideKnockback(dl, p, w, l, rot, o.Variant == "left" ? -90 : 90, unit, rim);
                break;
            }
            case BoardKind.Rotation:
                BoardArt.Rotation(dl, p, r, o.Variant == "ccw", unit);
                break;
            case BoardKind.Tiles:
                BoardArt.Tiles(dl, p, o.Size * 2 * k, o.Count > 0 ? o.Count : 4, rot, col, rim, o.Variant == "alt");
                break;
            case BoardKind.LineOfSight:
                BoardArt.LineOfSight(dl, p, r, c, MathF.Max(r * 3, (o.Length > 0 ? o.Length : o.Size * 4) * k), unit);
                break;
            case BoardKind.Pen when o.Points is { Length: >= 4 } pts:
            {
                var n = Math.Min(pts.Length / 2, PlanJson.MaxPenPoints);
                Span<Vector2> line = stackalloc Vector2[n];
                for (var i = 0; i < n; i++) line[i] = p + new Vector2(pts[i * 2], pts[i * 2 + 1]) * k;
                dl.AddPolyline(ref line[0], n, Palette.U32(col), ImDrawFlags.None, MathF.Max(1.5f, 0.25f * unit));
                break;
            }
            case BoardKind.Player:
                Player(dl, p, MathF.Max(S(5), MathF.Min(o.Size * k, 1.3f * unit)), d, col, you != null && string.Equals(d.Glyph, you, StringComparison.OrdinalIgnoreCase), unit);
                break;
            case BoardKind.Enemy:
                Enemy(dl, p, MathF.Max(S(6), r), rot, col, o, unit);
                break;
            case BoardKind.Waymark:
                Waymark(dl, p, MathF.Max(S(6), MathF.Min(r, 1.3f * unit * 1.6f)), col, d.Glyph);
                break;
            case BoardKind.Marker:
                Marker(dl, p, o, unit);
                break;
            case BoardKind.Tether:
                BoardArt.Tether(dl, p, BoardArt.Turn(p, new Vector2(o.Dx, o.Dy) * k, rot), col, unit, o.Variant is { Length: > 0 });
                break;
            case BoardKind.Line:
            {
                var to = BoardArt.Turn(p, new Vector2(o.Dx, o.Dy) * k, rot);
                if (o.Dashed) BoardArt.DashedLine(dl, p, to, Palette.U32(col), MathF.Max(1, S(1.5f)), 0.6f * unit, 0.5f * unit);
                else dl.AddLine(p, to, Palette.U32(col), S(1.5f));
                break;
            }
            case BoardKind.Arrow:
                BoardArt.Arrow(dl, p, BoardArt.Turn(p, new Vector2(o.Dx, o.Dy) * k, rot), col, unit, o.Dashed);
                break;
            case BoardKind.Text:
                if (d.Glyph != null) Outlined(dl, p, d.Glyph, Face.SansSemibold, 13, col, S(160));
                break;
            default:
                dl.AddCircleFilled(p, S(2.5f), Palette.U32(col), 8);
                break;
        }
        // A later hit's number (and the first's, in a run), as the board badges them.
        if (o.Order > 0 && o.Kind is not (BoardKind.Player or BoardKind.Enemy or BoardKind.Waymark or BoardKind.Marker or BoardKind.Text or BoardKind.Tether or BoardKind.Line or BoardKind.Arrow))
        {
            var at = o.Kind is BoardKind.Rect or BoardKind.LineStack
                ? BoardArt.Turn(p, new Vector2(0, -(o.Length > 0 ? o.Length : o.Size * 2) * k * 0.55f), rot)
                : BoardArt.Pol(p, r, 38);
            var bc = o.Kind is BoardKind.Tankbuster or BoardKind.SharedBuster ? Palette.Tanks : col;
            BoardArt.Badge(dl, at, o.Order, bc, later, unit);
        }
    }

    /// <summary>
    /// A protean, as the site draws it (its <c>proteanShape</c>): a bait for each player, fanned
    /// evenly round the middle, the first pointing the object's way. Cones (30° by default, never
    /// overlapping), or lines (60 wide) for its <c>line</c> variant.
    /// </summary>
    private static void Protean(ImDrawListPtr dl, Vector2 p, float k, BoardObj o, Vector4 col, float rim, bool later)
    {
        var n = Math.Clamp(o.Count > 0 ? o.Count : 8, 1, 16);
        var step = 360f / n;
        var r = o.Size * k;
        if (o.Variant == "line")
        {
            var w = MathF.Max(4, o.Width > 0 ? o.Width : 60) * k;
            var start = n > 2 ? MathF.Min(r * 0.6f, w / 2 / MathF.Tan(MathF.PI / n)) : 0;
            for (var i = 0; i < n; i++) BoardArt.TeleRect(dl, p, -w / 2, -r, w / 2, -start, o.Rotation + step * i, col, rim, later);
            return;
        }
        var angle = Math.Clamp(o.Angle, 2, MathF.Max(2, step - 0.5f));
        for (var i = 0; i < n; i++) BoardArt.TeleCone(dl, p, r, angle, o.Rotation + step * i, col, rim, later);
    }

    /// <summary>A player as a spot (`player`): the dark halo, its colour with a faint white ring (white and thicker when it's you), its label; you under the reticle.</summary>
    private static void Player(ImDrawListPtr dl, Vector2 p, float r, in DrawnObj d, Vector4 col, bool you, float unit)
    {
        dl.AddCircleFilled(p, r * 1.32f / 1.08f, Palette.U32(Palette.WithAlpha(Palette.OnLight, 0.85f)), 24);
        dl.AddCircleFilled(p, r, Palette.U32(col), 24);
        dl.AddCircle(p, r, Palette.U32(you ? Palette.White : Palette.Rgba(255, 255, 255, 0.28f)), 24, you ? MathF.Max(1.5f, r * 0.18f) : 1f);
        if (d.Glyph != null)
        {
            var px = r >= S(10) ? 10 : r >= S(8) ? 8 : 7;
            var size = Txt.Measure(d.Glyph, Face.SansSemibold, px);
            if (size.X <= r * 2.1f) Txt.Draw(dl, p - size / 2, d.Glyph, Face.SansSemibold, px, Palette.White);
        }
        if (you) BoardArt.PointReticle(dl, p, r * 2.1f / 1.08f, Palette.White, MathF.Max(1.2f, 0.16f * unit));
    }

    /// <summary>
    /// An enemy (`enemy`): a boss with its hitbox ring (its back quarter dashed: flank and rear) and its
    /// facing triangle, then its shield and face (the Boss); a medium enemy or an add, the shield alone.
    /// </summary>
    private static void Enemy(ImDrawListPtr dl, Vector2 p, float hit, float rot, Vector4 col, BoardObj o, float unit)
    {
        var kind = o.Variant switch
        {
            "medium" => EnemyKind.Medium,
            "add" or "small" => EnemyKind.Add,
            _ => EnemyKind.Boss,
        };
        if (kind == EnemyKind.Boss)
        {
            dl.AddCircleFilled(p, hit, Palette.U32(Palette.WithAlpha(col, 0.14f)), 40);
            dl.PathClear();
            BoardArt.Arc(dl, p, hit, rot + 225 - 360, rot + 135, 30);
            dl.PathStroke(Palette.U32(col), ImDrawFlags.None, MathF.Max(1, 0.2f * unit));
            BoardArt.DashedArc(dl, p, hit, rot + 135, rot + 225, Palette.U32(Palette.WithAlpha(col, 0.5f)), MathF.Max(1, 0.12f * unit), 0.4f * unit, 0.4f * unit);
            BoardArt.Triangle(dl, BoardArt.Pol(p, hit + 0.5f * unit, rot), rot, MathF.Max(S(5), 1.3f * unit), Palette.U32(col));
        }
        if (o.NoIcon && kind == EnemyKind.Boss) return;
        var face = (kind == EnemyKind.Boss ? hit * 0.72f : hit * 0.86f) * 2 * 1.25f;
        Boss.Draw(dl, p, MathF.Max(S(12), face), BossMood.Awake, col, kind, rot * MathF.PI / 180f);
    }

    /// <summary>A waymark: its letter's circle or its number's square, a soft fill of its colour, the letter in it.</summary>
    private static void Waymark(ImDrawListPtr dl, Vector2 p, float r, Vector4 col, string? glyph)
    {
        var square = glyph is "1" or "2" or "3" or "4";
        var fill = Palette.U32(Palette.WithAlpha(col, 0.16f * 0.82f));
        var edge = Palette.U32(Palette.WithAlpha(col, 0.82f));
        if (square)
        {
            var h = r * 1.2f / 1.3f;
            dl.AddRectFilled(p - new Vector2(h), p + new Vector2(h), fill, h * 0.25f);
            dl.AddRect(p - new Vector2(h), p + new Vector2(h), edge, h * 0.25f, ImDrawFlags.None, MathF.Max(1, r * 0.11f));
        }
        else
        {
            dl.AddCircleFilled(p, r, fill, 24);
            dl.AddCircle(p, r, edge, 24, MathF.Max(1, r * 0.11f));
        }
        if (glyph == null) return;
        var px = r >= S(9) ? 13 : r >= S(7) ? 10 : 8;
        var size = Txt.Measure(glyph, Face.SansSemibold, px);
        if (size.X <= r * 2) Txt.Draw(dl, p - size / 2, glyph, Face.SansSemibold, px, Palette.WithAlpha(col, 0.82f));
    }

    /// <summary>Markers over heads: a limit cut's number as dots, a PlayStation marker's shape, a sign's square.</summary>
    private static void Marker(ImDrawListPtr dl, Vector2 p, BoardObj o, float unit)
    {
        var s = MathF.Max(S(5), 0.62f * unit * 1.6f);
        var at = p + new Vector2(0.95f, -1.25f) * unit;
        switch (o.Type)
        {
            case "limitcut":
            {
                var n = Math.Clamp(o.Count > 0 ? o.Count : 1, 1, 8);
                var col = n % 2 == 1 ? Palette.Hex(0x5ad1ff) : Palette.Hex(0xff6b6b);
                var w = n > 4 ? s * 1.8f : s;
                dl.AddRectFilled(at - new Vector2(w, s), at + new Vector2(w, s), Palette.U32(Palette.Hex(0x08131c)), s * 0.45f);
                dl.AddRect(at - new Vector2(w, s), at + new Vector2(w, s), Palette.U32(col), s * 0.45f, ImDrawFlags.None, 1f);
                for (var i = 0; i < n; i++)
                {
                    var col4 = i % 4;
                    var x = (n > 4 ? (i < 4 ? -0.5f : 0.55f) : 0) * s * 1.6f + (col4 % 2 == 0 ? -0.3f : 0.3f) * s;
                    var y = (col4 < 2 ? -0.3f : 0.3f) * s;
                    dl.AddCircleFilled(at + new Vector2(x, y), MathF.Max(1, s * 0.22f), Palette.U32(col), 8);
                }
                break;
            }
            case "psmarker":
            {
                var top = p + new Vector2(0, -2.05f * unit);
                dl.AddCircleFilled(top, s * 1.4f, Palette.U32(Palette.WithAlpha(Palette.OnLight, 0.8f)), 20);
                var (col, shape) = o.Variant switch
                {
                    "cross" => (Palette.Hex(0x5aa2ff), 1),
                    "square" => (Palette.Hex(0xff7ad9), 2),
                    "triangle" => (Palette.Hex(0x45d483), 3),
                    _ => (Palette.Hex(0xff5468), 0),
                };
                var u = Palette.U32(col);
                var w = MathF.Max(1, s * 0.3f);
                var h = s * 0.75f;
                if (shape == 0) dl.AddCircle(top, h, u, 16, w);
                else if (shape == 1)
                {
                    dl.AddLine(top - new Vector2(h), top + new Vector2(h), u, w);
                    dl.AddLine(top + new Vector2(h, -h), top + new Vector2(-h, h), u, w);
                }
                else if (shape == 2) dl.AddRect(top - new Vector2(h), top + new Vector2(h), u, 0, ImDrawFlags.None, w);
                else dl.AddTriangle(top + new Vector2(0, -h), top + new Vector2(h, h * 0.75f), top + new Vector2(-h, h * 0.75f), u, w);
                break;
            }
            default:
            {
                var col = Palette.Hex(o.Color);
                dl.AddRectFilled(at - new Vector2(s * 0.8f), at + new Vector2(s * 0.8f), Palette.U32(col), S(2));
                dl.AddRect(at - new Vector2(s * 0.8f), at + new Vector2(s * 0.8f), Palette.U32(Palette.OnLight), S(2), ImDrawFlags.None, 1f);
                break;
            }
        }
    }

    /// <summary>Words centred on a point with a dark edge round them (paint-order: stroke), left out when they don't fit.</summary>
    private static void Outlined(ImDrawListPtr dl, Vector2 p, string text, Face face, int px, Vector4 color, float maxWidth)
    {
        var size = Txt.Measure(text, face, px);
        if (size.X > maxWidth) return;
        var at = Txt.Snap(p - size / 2);
        var dark = Palette.WithAlpha(Palette.OnLight, 0.9f);
        foreach (var o in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) })
            Txt.Draw(dl, at + o, text, face, px, dark);
        Txt.Draw(dl, at, text, face, px, color);
    }
}
