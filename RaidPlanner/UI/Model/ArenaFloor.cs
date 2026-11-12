using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>
/// A real arena (<c>poly</c>) made ready to draw, once a plan (kept with its <see cref="PlanArena"/>): in board units
/// (a ring's value × 840 / yalms, from the board's centre, y down), so a frame only scales and moves it.
///
/// ImGui fills only convex shapes and has no even-odd rule, so each polygon (its outline and holes) is cut into
/// triangles by a scanline: the board is cut into slabs at every corner's height (and where two edges cross), the
/// edges crossing a slab are paired off left to right, even-odd, and each pair is a trapezoid (two triangles), joined
/// with the one above while the same two edges bound it. That needs no winding, takes holes, touching rings, overlaps
/// and self-crossings as the site's SVG fill does, and its work is bounded: past <see cref="MaxTriangles"/> or
/// <see cref="MaxWork"/> it stops with what it has (a broken shape draws partly, never freezes the game).
///
/// Also made once: each ring's corners (the rim), a drop's dashes along every ring, and the grid's lines cut to the
/// floor (even-odd along each line, as the site clips its grid to the arena). Pure, so it's tested.
/// </summary>
public sealed class ArenaFloor
{
    /// <summary>The board's half-width the arena fills (board units).</summary>
    public const float Extent = 420;

    /// <summary>The most triangles the floor is cut into, all polygons together.</summary>
    public const int MaxTriangles = 24576;

    /// <summary>How much scanline work (edges looked at) is spent on a floor at most.</summary>
    public const long MaxWork = 6_000_000;

    /// <summary>The most dashes along a drop's rings, all together (fewer, longer ones past it).</summary>
    public const int MaxDashes = 8192;

    /// <summary>The most grid pieces drawn.</summary>
    public const int MaxGridPieces = 8192;

    /// <summary>The floor's triangles, three corners each, polygon by polygon (board units).</summary>
    public IReadOnlyList<Vector2[]> Floors { get; }

    /// <summary>Every ring's corners (board units, not closed), for the rim.</summary>
    public IReadOnlyList<Vector2[]> Rims { get; }

    /// <summary>The square grid's lines and the radial grid's rings and spokes, cut to the floor (pairs of ends, board units).</summary>
    public IReadOnlyList<(Vector2 A, Vector2 B)> SquareGrid { get; }

    public IReadOnlyList<(Vector2 A, Vector2 B)> RadialGrid { get; }

    /// <summary>A drop's dashes along every ring (each a short polyline, board units).</summary>
    public IReadOnlyList<Vector2[]> Dashes { get; }

    /// <summary>Whether the cut stopped at a cap (a malformed or far too detailed shape).</summary>
    public bool Truncated { get; }

    private ArenaFloor(PlanArena arena)
    {
        var scale = Extent * 2 / MathF.Max(1, arena.Yalms);
        var polys = new List<Vector2[][]>(arena.Rings.Count);
        var rims = new List<Vector2[]>();
        foreach (var poly in arena.Rings)
        {
            var rings = new List<Vector2[]>(poly.Count);
            foreach (var ring in poly)
            {
                var n = ring.Length / 2;
                if (n < 3) continue;
                var pts = new Vector2[n];
                for (var i = 0; i < n; i++) pts[i] = new Vector2(ring[i * 2], ring[i * 2 + 1]) * scale;
                rings.Add(pts);
                rims.Add(pts);
            }
            if (rings.Count > 0) polys.Add(rings.ToArray());
        }
        Rims = rims;

        var floors = new List<Vector2[]>(polys.Count);
        var budget = new Budget();
        foreach (var rings in polys)
        {
            var tris = new List<Vector2>();
            Fill(rings, tris, budget);
            floors.Add(tris.ToArray());
            if (budget.Out) break;
        }
        Floors = floors;
        Truncated = budget.Out;

        // A drop's dashes: as the board dashes a drop elsewhere (0.9 on, 0.6 off, in its marks' units), fewer past the cap.
        var mark = Extent / MathF.Max(1, arena.Yalms);
        var period = 1.5f * mark;
        var length = 0.0;
        foreach (var r in rims) length += Perimeter(r);
        if (length / period > MaxDashes) period = (float)(length / MaxDashes);
        var dashes = new List<Vector2[]>();
        var closed = new List<Vector2>();
        foreach (var r in rims)
        {
            closed.Clear();
            closed.AddRange(r);
            closed.Add(r[0]);
            dashes.AddRange(Model.Dashes.Along(closed, period, 0.6f));
            if (dashes.Count >= MaxDashes * 2) break;
        }
        Dashes = dashes;

        SquareGrid = Square(polys);
        RadialGrid = Radial(polys);
    }

    private static readonly ConditionalWeakTable<PlanArena, ArenaFloor> Made = new();

    /// <summary>A real arena made ready to draw, made the first time it's asked for and kept with the arena; null when it isn't one.</summary>
    public static ArenaFloor? Of(PlanArena arena)
    {
        if (!arena.IsPoly) return null;
        if (Made.TryGetValue(arena, out var made)) return made;
        made = new ArenaFloor(arena);
        Made.AddOrUpdate(arena, made);
        return made;
    }

    /// <summary>A real arena made ready to draw, not kept (for tests).</summary>
    public static ArenaFloor Make(PlanArena arena) => new(arena);

    private static double Perimeter(Vector2[] r)
    {
        var s = 0.0;
        for (int i = 0, j = r.Length - 1; i < r.Length; j = i++) s += Vector2.Distance(r[i], r[j]);
        return s;
    }

    // ---------------------------------------------------------------------------------------------
    // The floor: an even-odd scanline cut into trapezoids.

    private sealed class Budget
    {
        public long Work;
        public int Triangles;
        public bool Out;
    }

    private readonly record struct Edge(double X0, double Y0, double X1, double Y1)
    {
        public double At(double y) => Y1 == Y0 ? X0 : X0 + (y - Y0) * (X1 - X0) / (Y1 - Y0);
        public double Slope => (X1 - X0) / (Y1 - Y0);
    }

    /// <summary>
    /// One polygon's floor (its rings, read even-odd) as triangles added to <paramref name="into"/> (three corners
    /// each). Exposed for tests; <paramref name="maxTriangles"/> and <paramref name="maxWork"/> bound it.
    /// </summary>
    public static bool Fill(IReadOnlyList<Vector2[]> rings, List<Vector2> into, int maxTriangles = MaxTriangles, long maxWork = MaxWork)
    {
        var b = new Budget { Triangles = MaxTriangles - maxTriangles, Work = MaxWork - maxWork };
        Fill(rings, into, b);
        return !b.Out;
    }

    private static void Fill(IReadOnlyList<Vector2[]> rings, List<Vector2> into, Budget budget)
    {
        // The edges, top end first; flat ones never cross a slab, so even-odd needs none of them.
        var edges = new List<Edge>();
        foreach (var r in rings)
            for (int i = 0, j = r.Length - 1; i < r.Length; j = i++)
            {
                Vector2 a = r[j], c = r[i];
                if (!float.IsFinite(a.X) || !float.IsFinite(a.Y) || !float.IsFinite(c.X) || !float.IsFinite(c.Y) || a.Y == c.Y) continue;
                edges.Add(a.Y < c.Y ? new Edge(a.X, a.Y, c.X, c.Y) : new Edge(c.X, c.Y, a.X, a.Y));
            }
        if (edges.Count < 2) return;
        edges.Sort((p, q) => p.Y0.CompareTo(q.Y0));
        var ys = new List<double>(edges.Count * 2);
        foreach (var e in edges)
        {
            ys.Add(e.Y0);
            ys.Add(e.Y1);
        }
        ys.Sort();
        var unique = new List<double>(ys.Count);
        foreach (var y in ys)
            if (unique.Count == 0 || y > unique[^1]) unique.Add(y);

        // Trapezoids still growing, by their left and right edges: where each started.
        var open = new Dictionary<(int L, int R), double>();
        var pairs = new HashSet<(int, int)>();
        var active = new List<int>();
        var cuts = new List<double>();
        var next = 0;
        void Close((int L, int R) key, double from, double to)
        {
            if (budget.Out || to <= from) return;
            Trapezoid(edges[key.L], edges[key.R], from, to, into, budget);
        }
        for (var s = 0; s + 1 < unique.Count && !budget.Out; s++)
        {
            double y0 = unique[s], y1 = unique[s + 1];
            while (next < edges.Count && edges[next].Y0 <= y0) active.Add(next++);
            active.RemoveAll(i => edges[i].Y1 <= y0);
            budget.Work += active.Count + 1;

            // Where two edges cross inside the slab: it's cut there too, so a pair's order holds in each piece.
            cuts.Clear();
            cuts.Add(y0);
            if (active.Count >= 2)
            {
                var ym = (y0 + y1) / 2;
                active.Sort((p, q) => edges[p].At(ym).CompareTo(edges[q].At(ym)));
                if (active.Count <= 64)
                {
                    budget.Work += active.Count * active.Count / 2;
                    for (var i = 0; i < active.Count; i++)
                        for (var j = i + 1; j < active.Count; j++)
                            Cross(edges[active[i]], edges[active[j]], y0, y1, cuts);
                }
                else
                    for (var i = 0; i + 1 < active.Count; i++)
                        Cross(edges[active[i]], edges[active[i + 1]], y0, y1, cuts);
            }
            cuts.Add(y1);
            if (cuts.Count > 2)
            {
                cuts.Sort();
                // At most 256 pieces a slab (the first ones, and its foot).
                if (cuts.Count > 257) cuts.RemoveRange(256, cuts.Count - 257);
            }

            for (var k = 0; k + 1 < cuts.Count && !budget.Out; k++)
            {
                double ya = cuts[k], yb = cuts[k + 1];
                if (yb - ya <= 1e-9) continue;
                var ym = (ya + yb) / 2;
                if (cuts.Count > 2) active.Sort((p, q) => edges[p].At(ym).CompareTo(edges[q].At(ym)));
                budget.Work += active.Count + 1;
                if (budget.Work > MaxWork) budget.Out = true;
                // Even-odd: the floor is between the 1st and 2nd edge, the 3rd and 4th, …
                pairs.Clear();
                for (var i = 0; i + 1 < active.Count; i += 2) pairs.Add((active[i], active[i + 1]));
                foreach (var key in new List<(int L, int R)>(open.Keys))
                    if (!pairs.Contains(key))
                    {
                        Close(key, open[key], ya);
                        open.Remove(key);
                    }
                foreach (var key in pairs) open.TryAdd(key, ya);
            }
        }
        var end = unique[^1];
        foreach (var (key, from) in open) Close(key, from, end);
    }

    /// <summary>Where two edges cross strictly inside a slab, added to <paramref name="cuts"/>.</summary>
    private static void Cross(in Edge p, in Edge q, double y0, double y1, List<double> cuts)
    {
        var top = p.At(y0) - q.At(y0);
        var bottom = p.At(y1) - q.At(y1);
        if (!(top * bottom < 0)) return;
        var y = y0 + (y1 - y0) * top / (top - bottom);
        if (y > y0 + 1e-9 && y < y1 - 1e-9) cuts.Add(y);
    }

    private static void Trapezoid(in Edge l, in Edge r, double ya, double yb, List<Vector2> into, Budget budget)
    {
        var a = new Vector2((float)l.At(ya), (float)ya);
        var b = new Vector2((float)r.At(ya), (float)ya);
        var c = new Vector2((float)r.At(yb), (float)yb);
        var d = new Vector2((float)l.At(yb), (float)yb);
        if (b.X - a.X > 1e-6f) Triangle(a, b, c, into, budget);
        if (c.X - d.X > 1e-6f) Triangle(a, c, d, into, budget);
    }

    private static void Triangle(Vector2 a, Vector2 b, Vector2 c, List<Vector2> into, Budget budget)
    {
        if (budget.Triangles >= MaxTriangles)
        {
            budget.Out = true;
            return;
        }
        budget.Triangles++;
        into.Add(a);
        into.Add(b);
        into.Add(c);
    }

    // ---------------------------------------------------------------------------------------------
    // The grid, cut to the floor.

    /// <summary>The square grid as the board draws it on a square (lines at −½, 0 and ½ of the arena's half-width), across the board.</summary>
    private static List<(Vector2, Vector2)> Square(List<Vector2[][]> polys)
    {
        var lines = new List<(Vector2, Vector2)>();
        const float far = 500;
        foreach (var f in new[] { -0.5f, 0f, 0.5f })
        {
            lines.Add((new Vector2(Extent * f, -far), new Vector2(Extent * f, far)));
            lines.Add((new Vector2(-far, Extent * f), new Vector2(far, Extent * f)));
        }
        return CutAll(lines, polys);
    }

    /// <summary>The radial grid as the board draws it round a round arena (a ring every quarter of the half-width, four diameters), out to the floor's furthest corner.</summary>
    private static List<(Vector2, Vector2)> Radial(List<Vector2[][]> polys)
    {
        var reach = 0f;
        foreach (var poly in polys)
            foreach (var ring in poly)
                foreach (var p in ring)
                    reach = MathF.Max(reach, p.Length());
        var lines = new List<(Vector2, Vector2)>();
        const int sides = 64;
        for (var r = Extent / 4; r < reach - 0.5f && lines.Count < 2048; r += Extent / 4)
            for (var i = 0; i < sides; i++)
            {
                var a0 = MathF.PI * 2 * i / sides;
                var a1 = MathF.PI * 2 * (i + 1) / sides;
                lines.Add((new Vector2(MathF.Sin(a0), -MathF.Cos(a0)) * r, new Vector2(MathF.Sin(a1), -MathF.Cos(a1)) * r));
            }
        for (var d = 0; d < 180; d += 45)
        {
            var a = d * MathF.PI / 180;
            var u = new Vector2(MathF.Sin(a), -MathF.Cos(a)) * reach;
            lines.Add((u, -u));
        }
        return CutAll(lines, polys);
    }

    private static List<(Vector2, Vector2)> CutAll(List<(Vector2, Vector2)> lines, List<Vector2[][]> polys)
    {
        var pieces = new List<(Vector2, Vector2)>();
        var spans = new List<(float, float)>();
        var crossings = new List<float>();
        foreach (var (a, b) in lines)
        {
            spans.Clear();
            foreach (var poly in polys) Spans(a, b, poly, crossings, spans);
            // A point on floor in any polygon is floor (the site clips to every polygon's path).
            spans.Sort();
            var i = 0;
            while (i < spans.Count && pieces.Count < MaxGridPieces)
            {
                var (t0, t1) = spans[i++];
                while (i < spans.Count && spans[i].Item1 <= t1) t1 = MathF.Max(t1, spans[i++].Item2);
                if (t1 - t0 > 1e-4f) pieces.Add((Vector2.Lerp(a, b, t0), Vector2.Lerp(a, b, t1)));
            }
        }
        return pieces;
    }

    /// <summary>
    /// The parts of the segment a→b on one polygon's floor (as fractions along it), added to <paramref name="spans"/>: the
    /// rings' crossings along the whole line, paired off even-odd from outside (a corner on the line counted once, by
    /// which side its edges are on), then cut to the segment.
    /// </summary>
    public static void Spans(Vector2 a, Vector2 b, IReadOnlyList<Vector2[]> rings, List<float> crossings, List<(float, float)> spans)
    {
        var dir = b - a;
        if (dir.LengthSquared() < 1e-12f) return;
        crossings.Clear();
        foreach (var r in rings)
            for (int i = 0, j = r.Length - 1; i < r.Length; j = i++)
            {
                Vector2 p = r[j], q = r[i];
                var sp = Cross(dir, p - a) > 0;
                var sq = Cross(dir, q - a) > 0;
                if (sp == sq) continue;
                var den = Cross(dir, q - p);
                if (den == 0) continue;
                crossings.Add(Cross(p - a, q - p) / den);
            }
        if (crossings.Count < 2) return;
        crossings.Sort();
        for (var i = 0; i + 1 < crossings.Count; i += 2)
        {
            var t0 = MathF.Max(0, crossings[i]);
            var t1 = MathF.Min(1, crossings[i + 1]);
            if (t1 > t0) spans.Add((t0, t1));
        }
    }

    private static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;
}
