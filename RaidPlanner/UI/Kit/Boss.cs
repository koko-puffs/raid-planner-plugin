using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>How the Boss looks on an empty page: awake (something's wrong), asleep (nothing yet), tilted (not found).</summary>
public enum BossMood
{
    Awake,
    Asleep,
    Tilted,
}

/// <summary>Which enemy shield: horns for a boss, ears for a medium enemy, plain for a small add.</summary>
public enum EnemyKind
{
    Boss,
    Medium,
    Add,
}

/// <summary>
/// The Boss, as the site draws an enemy (board/enemyArt.ts): a shield with an angry face in a
/// 100-unit box, shaded as the game's markers are (deep at its top and lit from below, a dark
/// outline, a bright rim, a soft glow of its colour outside, the face glowing). Its outlines are
/// flattened and cut into triangles once (<see cref="Polygon"/>); the shading is per-vertex, since
/// ImGui has no gradients on shapes. The empty pages' Boss asleep has lids and closed eyes; tilted
/// it turns its head 16°.
/// </summary>
public static class Boss
{
    private const string BossShield =
        "M-46 -50 C-47 -36 -38 -27 -24 -27 L0 -31 L24 -27 C38 -27 47 -36 46 -50 C55 -36 53 -18 42 -10 L40 6 C38 27 20 40 0 50 C-20 40 -38 27 -40 6 L-42 -10 C-53 -18 -55 -36 -46 -50 Z";

    private const string MediumShield = "M-40 -50 L-20 -31 L0 -35 L20 -31 L40 -50 L41 4 C39 26 21 39 0 49 C-21 39 -39 26 -41 4 Z";

    private const string AddShield = "M-40 -38 L-14 -33 L0 -40 L14 -33 L40 -38 L38 6 C35 27 19 39 0 49 C-19 39 -35 27 -38 6 Z";

    private const string Face =
        "M-27 -15 L-6 -5 L-8 4 L-26 -3 Z M27 -15 L6 -5 L8 4 L26 -3 Z M-24 10 L-15 27 L-8 17 L0 29 L8 17 L15 27 L24 10 L15 17 L8 11 L0 20 L-8 11 L-15 17 Z";

    /// <summary>The asleep Boss's lids over its eyes (the mock-ups' overlay, in the same units; it sits 2.5 lower).</summary>
    private const string Lids = "M-28 -8.5 L-6 1 L-10 11 L-25 6 Z M28 -8.5 L6 1 L10 11 L25 6 Z";

    private sealed record Shape(Vector2[] Outline, int[] Triangles);

    private static readonly Dictionary<string, List<Shape>> Shapes = new();

    private static List<Shape> Get(string d)
    {
        if (Shapes.TryGetValue(d, out var s)) return s;
        s = new List<Shape>();
        foreach (var o in Polygon.Outlines(d, 8)) s.Add(new Shape(o, Polygon.Triangulate(o)));
        return Shapes[d] = s;
    }

    private static string ShieldOf(EnemyKind k) => k switch
    {
        EnemyKind.Medium => MediumShield,
        EnemyKind.Add => AddShield,
        _ => BossShield,
    };

    /// <summary>Mixes a colour toward black (the site's shadeOf).</summary>
    private static Vector4 Shade(Vector4 c, float amount) => Palette.Mix(c, new Vector4(0, 0, 0, c.W), amount);

    /// <summary>Mixes a colour toward white (the site's tintOf).</summary>
    private static Vector4 Tint(Vector4 c, float amount) => Palette.Mix(c, new Vector4(1, 1, 1, c.W), amount);

    /// <summary>
    /// Draws the Boss centred on <paramref name="center"/>, <paramref name="size"/> pixels across (the
    /// site's `data-size`: its 2.5-unit box, the shield 0.8 of it), turned by <paramref name="rotation"/> radians.
    /// </summary>
    public static void Draw(ImDrawListPtr dl, Vector2 center, float size, BossMood mood = BossMood.Awake, Vector4? color = null, EnemyKind kind = EnemyKind.Boss,
        float rotation = 0)
    {
        var col = color ?? Palette.Boss;
        var k = size / 125f;
        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);
        Vector2 At(Vector2 p) => center + new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos) * k;

        var shield = Get(ShieldOf(kind));
        // The glow outside: its colour stroked wide and soft (a blur, faked with three strokes).
        foreach (var s in shield)
        {
            Stroke(dl, s.Outline, At, Palette.WithAlpha(col, 0.1f), 14 * k);
            Stroke(dl, s.Outline, At, Palette.WithAlpha(col, 0.16f), 10 * k);
        }
        // The dark outline, the shield shaded from deep at its top to lit at its point, the bright rim.
        foreach (var s in shield) Stroke(dl, s.Outline, At, Palette.Hex(0x0b0b0c), MathF.Max(1, 7 * k));
        var top = Shade(col, 0.82f);
        var mid = Shade(col, 0.6f);
        var bottom = Shade(col, 0.25f);
        foreach (var s in shield) Fill(dl, s, At, y => y < 5 ? Palette.Mix(top, mid, (y + 50) / 55f) : Palette.Mix(mid, bottom, (y - 5) / 45f));
        foreach (var s in shield) Stroke(dl, s.Outline, At, Tint(col, 0.5f), MathF.Max(1, 3.4f * k));

        // The face, glowing.
        var face = Get(Face);
        var faceColor = Tint(col, 0.45f);
        var glow = Palette.WithAlpha(Tint(col, 0.15f), 0.35f);
        if (mood == BossMood.Asleep)
        {
            // Only the mouth glows; the eyes are lidded and shut.
            var mouth = face[^1];
            Stroke(dl, mouth.Outline, At, glow, 4 * k);
            Fill(dl, mouth, At, _ => faceColor);
            foreach (var lid in Get(Lids)) Fill(dl, lid, At, _ => Palette.Hex(0x170d22));
            var shut = Palette.U32(Palette.Hex(0xf0dcff));
            foreach (var sx in new[] { -1f, 1f })
            {
                dl.PathClear();
                dl.PathLineTo(At(new Vector2(26 * sx, 2.5f)));
                dl.PathBezierQuadraticCurveTo(At(new Vector2(17 * sx, 10f)), At(new Vector2(8.5f * sx, 5f)), 8);
                dl.PathStroke(shut, ImDrawFlags.None, MathF.Max(1, 4.5f * k));
            }
        }
        else
        {
            foreach (var s in face) Stroke(dl, s.Outline, At, glow, 4 * k);
            foreach (var s in face) Fill(dl, s, At, _ => faceColor);
        }
    }

    /// <summary>
    /// The Boss on an empty page: its dashed ring round it when <paramref name="ring"/> (96px, or as
    /// given), its z's floating up when asleep, its head turned and nudged left when tilted.
    /// </summary>
    public static void Empty(ImDrawListPtr dl, Vector2 center, float size, BossMood mood, float ring = 0)
    {
        if (ring > 0) DashedCircle(dl, center, S(ring) / 2, Palette.Line3, S(1.5f));
        var tilt = mood == BossMood.Tilted;
        Draw(dl, center + (tilt ? new Vector2(-S(3), 0) : Vector2.Zero), S(size), mood, rotation: tilt ? -16f * MathF.PI / 180f : 0);
        if (mood != BossMood.Asleep) return;
        // The z's (`.zzz b`): from the Boss's top right, bigger as they rise, breathing in turn.
        var box = S(size);
        var tr = center + new Vector2(box / 2, -box / 2);
        var zs = new (float Right, float Top, int Px, float Delay)[] { (-8, 6, 10, 0), (-19, -7, 12, 0.5f), (-32, -22, 15, 1f) };
        foreach (var (right, top, px, delay) in zs)
        {
            var b = Breathe(3.2f, -delay);
            var a = 0.25f + 0.75f * b;
            var dy = S(2) - S(4) * b;
            var w = Txt.Width("z", Face.MonoSemibold, px);
            Txt.Draw(dl, new Vector2(tr.X - S(right) - w, tr.Y + S(top) + dy), "z", Face.MonoSemibold, px, Palette.WithAlpha(Palette.Text, 0.8f * a));
        }
    }

    private static void Fill(ImDrawListPtr dl, Shape s, Func<Vector2, Vector2> at, Func<float, Vector4> colorAtY)
    {
        if (s.Triangles.Length < 3) return;
        var uv = ImGui.GetFontTexUvWhitePixel();
        dl.PrimReserve(s.Triangles.Length, s.Triangles.Length);
        foreach (var i in s.Triangles)
        {
            var p = s.Outline[i];
            dl.PrimVtx(at(p), uv, Palette.U32(colorAtY(p.Y)));
        }
    }

    private static void Stroke(ImDrawListPtr dl, Vector2[] outline, Func<Vector2, Vector2> at, Vector4 color, float thickness)
    {
        Span<Vector2> pts = stackalloc Vector2[Math.Min(outline.Length, 256)];
        for (var i = 0; i < pts.Length; i++) pts[i] = at(outline[i]);
        dl.AddPolyline(ref pts[0], pts.Length, Palette.U32(color), ImDrawFlags.Closed, thickness);
    }

    /// <summary>A dashed circle (`border: 1.5px dashed`): dashes three times the line's width with gaps as long.</summary>
    public static void DashedCircle(ImDrawListPtr dl, Vector2 c, float r, Vector4 color, float thickness)
    {
        var dash = thickness * 3;
        var n = Math.Max(8, (int)(MathF.PI * 2 * r / (dash * 2)));
        var step = MathF.PI * 2 / n;
        var col = Palette.U32(color);
        for (var i = 0; i < n; i++)
        {
            var a0 = step * i;
            var a1 = a0 + step / 2;
            dl.PathClear();
            dl.PathArcTo(c, r, a0, a1, 4);
            dl.PathStroke(col, ImDrawFlags.None, thickness);
        }
    }
}
