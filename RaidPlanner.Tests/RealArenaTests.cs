using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>0.5.0: a real arena's shape (<c>poly</c>): its rings read as the site cleans them, and the board's floor cut from them even-odd.</summary>
public class RealArenaTests
{
    private static Plan PlanWith(string arena) =>
        PlanJson.Read($$"""{ "id": "p", "name": "Plan", "arena": {{arena}}, "phases": [ { "id": "a", "name": "One", "objects": [] } ] }""")!;

    private static string Ring(params float[] v) => "[" + string.Join(",", v.Select(x => x.ToString(CultureInfo.InvariantCulture))) + "]";

    /// <summary>A regular polygon's corners round (cx, cy), flat.</summary>
    private static float[] Round(float cx, float cy, float r, int n, bool backwards = false)
    {
        var v = new float[n * 2];
        for (var i = 0; i < n; i++)
        {
            var a = MathF.PI * 2 * (backwards ? n - i : i) / n;
            v[i * 2] = cx + MathF.Sin(a) * r;
            v[i * 2 + 1] = cy - MathF.Cos(a) * r;
        }
        return v;
    }

    [Fact]
    public void The_sites_contract_is_read()
    {
        var p = PlanWith("""
            { "shape": "poly",
              "rings": [[[-10,-10, 10,-10, 10,10, -10,10], [-2,-2, 2,-2, 2,2, -2,2]], [[11,0, 12,0, 12,1]]],
              "center": {"x":100,"z":100}, "duty": 1026,
              "yalms": 24, "aspect": 1, "outlines": [],
              "grid": "square", "gridDivisions": 12, "color": "#26272c", "cardinals": true, "edge": "death" }
            """);
        var a = p.Arena;
        Assert.Equal("poly", a.Shape);
        Assert.True(a.IsPoly);
        Assert.Equal(2, a.Rings.Count);
        Assert.Equal(2, a.Rings[0].Count);
        Assert.Equal(new float[] { -10, -10, 10, -10, 10, 10, -10, 10 }, a.Rings[0][0]);
        Assert.Equal(new ArenaCenter(100, 100), a.Center);
        Assert.Equal(1026u, a.Duty);
        Assert.Equal(24, a.Yalms);
        Assert.Equal("death", a.Edge);
        Assert.Equal("square", a.Grid);
        Assert.Equal(0x26272cu, a.Color);
        Assert.Empty(a.Outlines);
    }

    [Fact]
    public void Center_duty_and_edge_may_be_missing()
    {
        var a = PlanWith("""{ "shape": "poly", "yalms": 30, "rings": [[[0,0, 5,0, 5,5]]] }""").Arena;
        Assert.True(a.IsPoly);
        Assert.Null(a.Center);
        Assert.Null(a.Duty);
        Assert.Equal("wall", a.Edge);
        Assert.Equal("none", a.Grid);

        var odd = PlanWith("""{ "shape": "poly", "rings": [[[0,0, 5,0, 5,5]]], "center": {"x": 1}, "duty": "1026", "edge": "lava" }""").Arena;
        Assert.Null(odd.Center);
        Assert.Null(odd.Duty);
        Assert.Equal("wall", odd.Edge);
    }

    [Fact]
    public void Its_size_without_yalms_is_twice_its_reach_rounded_up_to_a_tenth()
    {
        var a = PlanWith("""{ "shape": "poly", "rings": [[[-20.01,0, 0,-3, 7,15]]] }""").Arena;
        Assert.Equal(40.1f, a.Yalms, 3);
        Assert.Equal(20, PlanWith("""{ "shape": "poly", "rings": "no" }""").Arena.Yalms);
    }

    [Fact]
    public void Anything_malformed_is_skipped_never_thrown()
    {
        // Not rings at all: still poly, with none to draw (the board draws a square, as the site does).
        foreach (var rings in new[] { "null", "7", "\"x\"", "{}", "[]", "[[]]", "[[[]]]", "[[[1,2,3,4]]]", "[null, true, {}, \"a\"]", "[[[[[[1]]]]]]" })
        {
            var a = PlanWith($$"""{ "shape": "poly", "rings": {{rings}} }""").Arena;
            Assert.Equal("poly", a.Shape);
            Assert.False(a.IsPoly);
            Assert.Empty(a.Rings);
            Assert.Null(ArenaFloor.Of(a));
        }

        // A corner that isn't a finite number goes with its pair; an odd one left over is dropped; values within ±500, to 0.01.
        var b = PlanWith("""{ "shape": "poly", "rings": [[[0,0, "1",2, 10,null, 10,0, 10,10, true,false, 1e9,-1e9, 1.23456,2.346, 7]]] }""").Arena;
        Assert.Equal(new float[] { 0, 0, 10, 0, 10, 10, 500, -500, 1.23f, 2.35f }, b.Rings[0][0]);

        // A polygon whose outline isn't one is dropped with its holes; a bad hole after a good outline is skipped.
        var c = PlanWith("""
            { "shape": "poly", "rings": [
                [ [1,2], [0,0, 4,0, 4,4] ],
                [ "x", [0,0, 4,0, 4,4] ],
                [ [0,0, 9,0, 9,9], 5, [1,1], [2,2, 3,2, 3,3] ]
            ] }
            """).Arena;
        Assert.Single(c.Rings);
        Assert.Equal(2, c.Rings[0].Count);
        Assert.Equal(new float[] { 2, 2, 3, 2, 3, 3 }, c.Rings[0][1]);

        // Other shapes never read rings.
        Assert.Empty(PlanWith("""{ "shape": "square", "rings": [[[0,0, 5,0, 5,5]]] }""").Arena.Rings);
    }

    [Fact]
    public void The_caps_are_the_sites()
    {
        // 40 polygons: the first 32.
        var many = string.Join(",", Enumerable.Range(0, 40).Select(i => $"[{Ring(i, 0, i + 1, 0, i, 1)}]"));
        var a = PlanWith($$"""{ "shape": "poly", "rings": [{{many}}] }""").Arena;
        Assert.Equal(Rings.MaxPolygons, a.Rings.Count);
        Assert.Equal(new float[] { 31, 0, 32, 0, 31, 1 }, a.Rings[^1][0]);

        // 40 rings in a polygon: the first 32; a ring of 600 corners: the first 512.
        var holes = string.Join(",", Enumerable.Range(0, 40).Select(_ => Ring(0, 0, 1, 0, 1, 1)));
        Assert.Equal(Rings.MaxRings, PlanWith($$"""{ "shape": "poly", "rings": [[{{holes}}]] }""").Arena.Rings[0].Count);
        Assert.Equal(Rings.MaxPoints * 2, PlanWith($$"""{ "shape": "poly", "rings": [[{{Ring(Round(0, 0, 10, 600))}}]] }""").Arena.Rings[0][0].Length);

        // 4,096 corners in all: ten polygons of 500 keep eight, and 96 of the ninth.
        var big = string.Join(",", Enumerable.Range(0, 10).Select(i => $"[{Ring(Round(0, 0, 10 + i, 500))}]"));
        var t = PlanWith($$"""{ "shape": "poly", "rings": [{{big}}] }""").Arena;
        Assert.Equal(9, t.Rings.Count);
        Assert.Equal(Rings.MaxTotal, t.Rings.Sum(p => p.Sum(r => r.Length / 2)));
        Assert.Equal(96 * 2, t.Rings[8][0].Length);
    }

    [Fact]
    public void On_the_floor_is_even_odd_in_board_units()
    {
        // 24 yalms across: a ring's 10 is 350 board units out (10 × 840 / 24).
        var a = PlanWith("""{ "shape": "poly", "yalms": 24, "rings": [[[-10,-10, 10,-10, 10,10, -10,10], [-2,-2, 2,-2, 2,2, -2,2]], [[11,-1, 12,-1, 12,1, 11,1]]] }""").Arena;
        Assert.True(a.OnFloor(300, 0));
        Assert.False(a.OnFloor(360, 0));
        Assert.False(a.OnFloor(0, 0)); // the hole
        Assert.True(a.OnFloor(400, 0)); // the second platform (11 to 12 yalms: 385 to 420)
        Assert.False(a.OnFloor(-400, 0));

        // The plain shapes, for what tests "inside the arena" the same way.
        Assert.True(PlanArena.Default.OnFloor(0, 419));
        Assert.False(PlanArena.Default.OnFloor(300, 300));
        Assert.True(new PlanArena("square", 1, Array.Empty<float[]>(), 0).OnFloor(410, -410));
        Assert.False(new PlanArena("rectangle", 0.5f, Array.Empty<float[]>(), 0).OnFloor(0, 300));
    }

    /// <summary>The cut covers the floor exactly: every sample point on the floor (even-odd) in exactly one triangle, none off it in any.</summary>
    private static void AssertCovers(PlanArena arena, ArenaFloor floor, int step = 7)
    {
        var tris = floor.Floors.SelectMany(t => t).ToArray();
        Assert.True(tris.Length % 3 == 0);
        for (var y = -497; y < 500; y += step)
            for (var x = -499; x < 500; x += step)
            {
                var p = new Vector2(x + 0.37f, y + 0.61f);
                var count = 0;
                for (var i = 0; i < tris.Length; i += 3)
                    if (InTriangle(p, tris[i], tris[i + 1], tris[i + 2])) count++;
                // One per polygon it's on (polygons on top of each other each fill, as the site's paths do).
                var expected = arena.Rings.Count(poly => Rings.Contains(new[] { poly }, p.X * arena.Yalms / 840, p.Y * arena.Yalms / 840));
                Assert.True(expected == count, $"({p.X}, {p.Y}): {count} triangles, expected {expected}");
            }
    }

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;
        var d1 = Cross(b - a, p - a);
        var d2 = Cross(c - b, p - b);
        var d3 = Cross(a - c, p - c);
        return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
    }

    private static PlanArena Poly(float yalms, params float[][][] polys) =>
        new("poly", 1, Array.Empty<float[]>(), 0) { Yalms = yalms, Rings = polys.Select(p => (IReadOnlyList<float[]>)p).ToList() };

    [Fact]
    public void A_square_with_a_hole_fills_round_the_hole()
    {
        var arena = Poly(24, new[] { new float[] { -12, -12, 12, -12, 12, 12, -12, 12 }, new float[] { -3, -3, 3, -3, 3, 3, -3, 3 } });
        var f = ArenaFloor.Make(arena);
        Assert.False(f.Truncated);
        Assert.Single(f.Floors);
        Assert.Equal(840f * 840 - 210f * 210, Area(f.Floors[0]), 0);
        AssertCovers(arena, f);
        Assert.Equal(2, f.Rims.Count);
        Assert.Equal(new Vector2(-420, -420), f.Rims[0][0]);
    }

    [Fact]
    public void Winding_doesnt_matter()
    {
        // A round arena with a round hole, the hole going the same way as its outline, then the other way.
        var same = Poly(40, new[] { Round(0, 0, 20, 64), Round(0, 0, 8, 48) });
        var other = Poly(40, new[] { Round(0, 0, 20, 64), Round(0, 0, 8, 48, backwards: true) });
        var a = ArenaFloor.Make(same);
        var b = ArenaFloor.Make(other);
        Assert.Equal(Area(a.Floors[0]), Area(b.Floors[0]), 1);
        AssertCovers(same, a);
        AssertCovers(other, b);
    }

    [Fact]
    public void Concave_split_touching_and_crossing_rings_fill_even_odd()
    {
        // An L, a separate platform, a bow tie (a ring crossing itself), and a hole touching its outline at a corner.
        var arena = Poly(30,
            new[] { new float[] { -15, -15, -5, -15, -5, 5, 5, 5, 5, 15, -15, 15 } },
            new[] { new float[] { 8, -14, 14, -14, 14, -8, 8, -8 } },
            new[] { new float[] { 6, 0, 14, 10, 14, 0, 6, 10 } },
            new[] { new float[] { 8, -6, 14, -6, 14, -1, 8, -1 }, new float[] { 8, -6, 10, -4, 9, -2 } });
        var f = ArenaFloor.Make(arena);
        Assert.Equal(4, f.Floors.Count);
        AssertCovers(arena, f, 5);
    }

    [Fact]
    public void A_malformed_shape_is_bounded_and_never_freezes()
    {
        // 4,096 corners zig-zagging across the whole board, every edge crossing many others.
        var zig = new float[Rings.MaxPoints * 2];
        var rng = new Random(7);
        var polys = new float[8][][];
        for (var p = 0; p < polys.Length; p++)
        {
            for (var i = 0; i < Rings.MaxPoints; i++)
            {
                zig[i * 2] = rng.Next(-500, 501);
                zig[i * 2 + 1] = rng.Next(-500, 501);
            }
            polys[p] = new[] { (float[])zig.Clone() };
        }
        var arena = Poly(1000, polys);
        var clock = Stopwatch.StartNew();
        var f = ArenaFloor.Make(arena);
        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        Assert.True(f.Floors.Sum(t => t.Length) / 3 <= ArenaFloor.MaxTriangles);
        Assert.True(f.SquareGrid.Count <= ArenaFloor.MaxGridPieces);
        Assert.True(f.Dashes.Count <= ArenaFloor.MaxDashes * 2 + Rings.MaxPoints);

        // NaN or infinite corners (never from the reader, but the cut doesn't rely on that) are left out.
        var bad = Poly(20, new[] { new float[] { 0, 0, float.NaN, 1, 5, 5, float.PositiveInfinity, 0 } });
        ArenaFloor.Make(bad);
    }

    [Fact]
    public void A_detailed_real_arena_is_cut_whole()
    {
        // 32 polygons of 128 corners each (4,096 in all), each a ring with a hole: nothing capped.
        var polys = Enumerable.Range(0, 32).Select(i =>
        {
            var (cx, cy) = (-17.5f + i % 8 * 5, -17.5f + i / 8 * 5 * 2);
            return new[] { Round(cx, cy, 2.4f, 96), Round(cx, cy, 1f, 32) };
        }).ToArray();
        var arena = Poly(40, polys);
        var clock = Stopwatch.StartNew();
        var f = ArenaFloor.Make(arena);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
        Assert.False(f.Truncated);
        Assert.Equal(32, f.Floors.Count);
        AssertCovers(arena, f, 13);
    }

    [Fact]
    public void The_grid_is_cut_to_the_floor()
    {
        // 840 units across: the square's middle lines cross the hole, so each is two pieces; the ±210 lines one each.
        var arena = Poly(24, new[] { new float[] { -12, -12, 12, -12, 12, 12, -12, 12 }, new float[] { -3, -3, 3, -3, 3, 3, -3, 3 } });
        var f = ArenaFloor.Make(arena);
        Assert.Equal(8, f.SquareGrid.Count);
        var middle = f.SquareGrid.Where(s => s.A.Y == 0 && s.B.Y == 0).OrderBy(s => s.A.X).ToList();
        Assert.Equal(2, middle.Count);
        Assert.Equal(-420, middle[0].A.X, 2);
        Assert.Equal(-105, middle[0].B.X, 2);
        Assert.Equal(105, middle[1].A.X, 2);
        Assert.Equal(420, middle[1].B.X, 2);
        // Every radial piece is on the floor.
        Assert.NotEmpty(f.RadialGrid);
        foreach (var (a, b) in f.RadialGrid)
            Assert.True(arena.OnFloor((a.X + b.X) / 2, (a.Y + b.Y) / 2));
    }

    [Fact]
    public void A_drop_is_dashed_along_every_ring()
    {
        var arena = Poly(24, new[] { new float[] { -12, -12, 12, -12, 12, 12, -12, 12 }, new float[] { -3, -3, 3, -3, 3, 3, -3, 3 } });
        var f = ArenaFloor.Make(arena);
        // 96 yalms round the outline and 24 round the hole, a dash every 1.5 of the marks' units (half a yalm each), so every 0.75 yalm: 128 and 32.
        Assert.Equal(128 + 32, f.Dashes.Count);
    }

    [Fact]
    public void Made_once_and_kept_with_its_arena()
    {
        var arena = PlanWith("""{ "shape": "poly", "yalms": 20, "rings": [[[-10,-10, 10,-10, 10,10]]] }""").Arena;
        Assert.Same(ArenaFloor.Of(arena), ArenaFloor.Of(arena));
        Assert.Null(ArenaFloor.Of(PlanArena.Default));
    }

    private static float Area(Vector2[] tris)
    {
        var a = 0f;
        for (var i = 0; i + 2 < tris.Length; i += 3)
        {
            var u = tris[i + 1] - tris[i];
            var v = tris[i + 2] - tris[i];
            a += MathF.Abs(u.X * v.Y - u.Y * v.X) / 2;
        }
        return a;
    }
}
