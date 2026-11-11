using System;
using System.Collections.Generic;
using System.Numerics;

namespace RaidPlanner.UI.Model;

/// <summary>
/// Filling shapes ImGui can only fill when convex: SVG path data flattened into closed outlines,
/// and an outline cut into triangles (ear clipping), done once for a shape and kept. Used for the
/// Boss (the board's enemy shield and its face, from the site's board/enemyArt.ts). Pure, so it's tested.
/// </summary>
public static class Polygon
{
    /// <summary>Each closed outline of a path, its points in order (the closing point not repeated).</summary>
    public static List<Vector2[]> Outlines(string d, int curveSegments = 10)
    {
        var list = new List<Vector2[]>();
        foreach (var sub in SvgPath.Parse(d, curveSegments))
        {
            var pts = new List<Vector2>(sub.Points);
            while (pts.Count > 1 && Vector2.Distance(pts[0], pts[^1]) < 1e-4f) pts.RemoveAt(pts.Count - 1);
            // Drop points that repeat the one before (a curve's start is the line's end).
            for (var i = pts.Count - 1; i > 0; i--)
                if (Vector2.Distance(pts[i], pts[i - 1]) < 1e-4f) pts.RemoveAt(i);
            if (pts.Count >= 3) list.Add(pts.ToArray());
        }
        return list;
    }

    /// <summary>The signed area: positive when the points turn left as they go (anticlockwise with y up; clockwise on a screen, whose y runs down).</summary>
    public static float Area(IReadOnlyList<Vector2> p)
    {
        var a = 0f;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += (p[j].X - p[i].X) * (p[j].Y + p[i].Y);
        return a / 2;
    }

    /// <summary>
    /// A simple polygon cut into triangles by ear clipping: indices into <paramref name="p"/>, three a
    /// triangle, covering exactly its inside. A polygon that isn't simple still gives what it can.
    /// </summary>
    public static int[] Triangulate(IReadOnlyList<Vector2> p)
    {
        var n = p.Count;
        if (n < 3) return Array.Empty<int>();
        var idx = new List<int>(n);
        // Work in one winding (the ear test below wants it).
        if (Area(p) > 0) for (var i = 0; i < n; i++) idx.Add(i);
        else for (var i = n - 1; i >= 0; i--) idx.Add(i);
        var tris = new List<int>((n - 2) * 3);
        var guard = n * n;
        while (idx.Count > 3 && guard-- > 0)
        {
            var cut = false;
            for (var i = 0; i < idx.Count; i++)
            {
                var a = idx[(i + idx.Count - 1) % idx.Count];
                var b = idx[i];
                var c = idx[(i + 1) % idx.Count];
                if (!Convex(p[a], p[b], p[c])) continue;
                var inside = false;
                foreach (var k in idx)
                {
                    if (k == a || k == b || k == c) continue;
                    if (InTriangle(p[k], p[a], p[b], p[c]))
                    {
                        inside = true;
                        break;
                    }
                }
                if (inside) continue;
                tris.Add(a);
                tris.Add(b);
                tris.Add(c);
                idx.RemoveAt(i);
                cut = true;
                break;
            }
            if (!cut)
            {
                // Not simple (or all collinear): clip the next corner anyway, so it ends.
                tris.Add(idx[^1]);
                tris.Add(idx[0]);
                tris.Add(idx[1]);
                idx.RemoveAt(0);
            }
        }
        if (idx.Count == 3)
        {
            tris.Add(idx[0]);
            tris.Add(idx[1]);
            tris.Add(idx[2]);
        }
        return tris.ToArray();
    }

    /// <summary>The total area of a triangulation (for checking it covers the polygon).</summary>
    public static float TrianglesArea(IReadOnlyList<Vector2> p, int[] tris)
    {
        var a = 0f;
        for (var i = 0; i + 2 < tris.Length; i += 3)
            a += MathF.Abs(Cross(p[tris[i + 1]] - p[tris[i]], p[tris[i + 2]] - p[tris[i]])) / 2;
        return a;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    // The points are put in the order that turns left, so an ear's corner turns left too.
    private static bool Convex(Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, c - b) > 0;

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var d1 = Cross(b - a, p - a);
        var d2 = Cross(c - b, p - b);
        var d3 = Cross(a - c, p - c);
        var neg = d1 < 0 || d2 < 0 || d3 < 0;
        var pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
}
