using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace RaidPlanner.Core;

/// <summary>
/// A real arena's shape (since 0.5.0; the site's <c>shape: 'poly'</c>, model/rings.ts): polygons with holes,
/// <c>[polygon][ring 0 its outline, then its holes][x0, y0, x1, y1, …]</c> in yalms from the arena's centre,
/// x east and y south, as the party is placed. Rings aren't closed (the last corner joins the first) and are
/// read even-odd: a point is on the floor when it's inside an odd number of rings, so which way a ring goes
/// doesn't matter and a hole is simply a ring inside its outline. Read as the site cleans them
/// (<c>cleanRings</c>), never throwing.
/// </summary>
public static class Rings
{
    /// <summary>At most this many polygons, rings in a polygon, corners in a ring and corners in all (the site's <c>RING_LIMITS</c>).</summary>
    public const int MaxPolygons = 32, MaxRings = 32, MaxPoints = 512, MaxTotal = 4096;

    /// <summary>How far from the centre a corner may be, yalms (the site's <c>REACH</c>).</summary>
    public const double Reach = 500;

    /// <summary>
    /// Rings as the site keeps them, from anything: numbers only, two to a corner (an odd one left over is
    /// dropped), a corner that isn't a finite number dropped with its pair, kept within ±500 and to 0.01; a
    /// ring of fewer than three corners dropped, a polygon without an outline dropped (its holes would be
    /// holes in nothing); at most 32 polygons of 32 rings of 512 corners, 4,096 corners in all (the first
    /// kept). An empty list for anything that isn't rings.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<float[]>> Read(JsonNode? raw)
    {
        if (raw is not JsonArray polys) return Array.Empty<IReadOnlyList<float[]>>();
        var output = new List<IReadOnlyList<float[]>>();
        var total = 0;
        var pts = new List<float>();
        for (var p = 0; p < polys.Count && p < MaxPolygons * 4; p++)
        {
            if (output.Count >= MaxPolygons || total >= MaxTotal) break;
            if (polys[p] is not JsonArray poly) continue;
            var rings = new List<float[]>();
            for (var r = 0; r < poly.Count && r < MaxRings * 4; r++)
            {
                if (rings.Count >= MaxRings) break;
                if (poly[r] is not JsonArray ring)
                {
                    if (rings.Count == 0) break;
                    continue;
                }
                pts.Clear();
                var room = Math.Min(MaxPoints, MaxTotal - total);
                for (var i = 0; i + 1 < ring.Count && pts.Count / 2 < room; i += 2)
                {
                    if (Num(ring[i]) is not { } x || Num(ring[i + 1]) is not { } y) continue;
                    pts.Add(R2(Math.Clamp(x, -Reach, Reach)));
                    pts.Add(R2(Math.Clamp(y, -Reach, Reach)));
                }
                if (pts.Count < 6)
                {
                    if (rings.Count == 0) break;
                    continue;
                }
                rings.Add(pts.ToArray());
                total += pts.Count / 2;
            }
            if (rings.Count > 0) output.Add(rings);
        }
        return output;
    }

    /// <summary>
    /// Whether (x, y) (in the rings' units) is on the floor: inside any polygon, each polygon read
    /// even-odd on its own (its outline less its holes), so overlapping platforms are floor, as the
    /// board draws them (the site's <c>inRings</c>).
    /// </summary>
    public static bool Contains(IReadOnlyList<IReadOnlyList<float[]>> rings, float x, float y)
    {
        foreach (var poly in rings)
        {
            var inside = false;
            foreach (var ring in poly)
                if (InRing(ring, x, y)) inside = !inside;
            if (inside) return true;
        }
        return false;
    }

    /// <summary>Whether (x, y) is inside one ring (a flat list of corners), even-odd.</summary>
    public static bool InRing(float[] pts, float x, float y)
    {
        var inside = false;
        var n = pts.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = pts[i * 2], yi = pts[i * 2 + 1], xj = pts[j * 2], yj = pts[j * 2 + 1];
            if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    /// <summary>How far the rings reach from their 0, 0 along either axis, yalms (the site's <c>ringsReach</c>); 0 with none.</summary>
    public static float ReachOf(IReadOnlyList<IReadOnlyList<float[]>> rings)
    {
        var reach = 0f;
        foreach (var poly in rings)
            foreach (var ring in poly)
                foreach (var v in ring)
                    reach = MathF.Max(reach, MathF.Abs(v));
        return reach;
    }

    /// <summary>To 0.01, as the site keeps a corner (JavaScript's <c>Math.round</c>: halves up), never −0.</summary>
    private static float R2(double v)
    {
        var r = (float)(Math.Floor(v * 100 + 0.5) / 100);
        return r == 0 ? 0 : r;
    }

    private static double? Num(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d) || (v.TryGetValue<long>(out var l) && (d = l) == l)) return double.IsFinite(d) ? d : null;
        return null;
    }
}
