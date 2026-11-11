using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The board's own drawing, in ImGui: the telegraph (a soft fill, brighter towards its rim, a crisp
/// hot rim; a later hit hollow with a dashed rim), chevrons, the tankbuster's shield, the exaflare's
/// hits to come, rings of chevrons for knockbacks and draw-ins, the gaze's eye, the tower, and the
/// rest of the site's board as the mock-ups draw it (assets/board.js), in screen pixels: a point and
/// sizes in pixels, angles in degrees from north, clockwise. Shared by the timeline's wells
/// (<see cref="Glyphs"/>) and the plan window's board (<see cref="PlanBoard"/>).
/// </summary>
public static class BoardArt
{
    private const int Segments = 40;

    /// <summary>A point <paramref name="r"/> from <paramref name="c"/> at <paramref name="deg"/> (0 north, clockwise).</summary>
    public static Vector2 Pol(Vector2 c, float r, float deg)
    {
        var a = (deg - 90) * MathF.PI / 180f;
        return c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
    }

    /// <summary>A local point turned by <paramref name="deg"/> (clockwise) and placed at <paramref name="c"/>.</summary>
    public static Vector2 Turn(Vector2 c, Vector2 local, float deg)
    {
        var a = deg * MathF.PI / 180f;
        var cos = MathF.Cos(a);
        var sin = MathF.Sin(a);
        return c + new Vector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos);
    }

    private static uint C(Vector4 c, float a) => Palette.U32(Palette.WithAlpha(c, c.W * a));

    // ---------------------------------------------------------------------------------------------
    // The telegraph.

    /// <summary>
    /// A round telegraph: the fill (20%, or <paramref name="fill"/>), a glow inside its rim (its colour
    /// rising to 42% at the edge, faked with three rings), the rim at 95%. Later hits (order past 1)
    /// are hollow: 7% and a dashed rim at 70%.
    /// </summary>
    public static void TeleCircle(ImDrawListPtr dl, Vector2 c, float r, Vector4 col, float rim, float fill = 0.2f, bool later = false)
    {
        if (r < 0.5f) return;
        dl.AddCircleFilled(c, r, C(col, later ? 0.07f : fill), Segments);
        if (!later)
        {
            dl.AddCircle(c, r * 0.96f, C(col, 0.22f), Segments, r * 0.08f);
            dl.AddCircle(c, r * 0.88f, C(col, 0.1f), Segments, r * 0.08f);
        }
        if (later) DashedArc(dl, c, r, 0, 360, C(col, 0.7f), rim, rim * 4, rim * 3.2f);
        else dl.AddCircle(c, r, C(col, 0.95f), Segments, rim);
    }

    /// <summary>A donut: the band filled, both rims.</summary>
    public static void TeleDonut(ImDrawListPtr dl, Vector2 c, float r, float inner, Vector4 col, float rim, bool later = false)
    {
        inner = Math.Clamp(inner, 0, r - 1);
        var band = r - inner;
        dl.AddCircle(c, inner + band / 2, C(col, later ? 0.07f : 0.25f), Segments, band);
        dl.AddCircle(c, r, C(col, later ? 0.7f : 0.95f), Segments, rim);
        if (inner > 0.5f) dl.AddCircle(c, inner, C(col, later ? 0.7f : 0.95f), Segments, rim);
    }

    /// <summary>A cone from <paramref name="apex"/>, <paramref name="angle"/>° wide, pointing at <paramref name="rot"/>; filled in convex parts of 90° at most.</summary>
    public static void TeleCone(ImDrawListPtr dl, Vector2 apex, float r, float angle, float rot, Vector4 col, float rim, bool later = false)
    {
        if (angle >= 359.9f)
        {
            TeleCircle(dl, apex, r, col, rim, 0.25f, later);
            return;
        }
        var spread = MathF.Max(1, angle);
        var parts = Math.Max(1, (int)MathF.Ceiling(spread / 90f));
        var step = spread / parts;
        var a0 = rot - spread / 2;
        var fill = C(col, later ? 0.07f : 0.25f);
        for (var i = 0; i < parts; i++)
        {
            dl.PathClear();
            dl.PathLineTo(apex);
            Arc(dl, apex, r, a0 + step * i, a0 + step * (i + 1), 10);
            dl.PathFillConvex(fill);
        }
        dl.PathClear();
        dl.PathLineTo(apex);
        Arc(dl, apex, r, a0, a0 + spread, 10 * parts);
        dl.PathStroke(C(col, later ? 0.7f : 0.95f), ImDrawFlags.Closed, rim);
    }

    /// <summary>A path arc from <paramref name="d0"/> to <paramref name="d1"/> (degrees from north, clockwise).</summary>
    public static void Arc(ImDrawListPtr dl, Vector2 c, float r, float d0, float d1, int segments)
    {
        var a0 = (d0 - 90) * MathF.PI / 180f;
        var a1 = (d1 - 90) * MathF.PI / 180f;
        dl.PathArcTo(c, r, a0, a1, Math.Max(2, segments));
    }

    /// <summary>A rectangle (local x0..x1 by y0..y1, y up the board's north) turned by <paramref name="rot"/> about <paramref name="o"/>.</summary>
    public static void TeleRect(ImDrawListPtr dl, Vector2 o, float x0, float y0, float x1, float y1, float rot, Vector4 col, float rim, bool later = false, float fill = 0.25f)
    {
        var a = Turn(o, new Vector2(x0, y0), rot);
        var b = Turn(o, new Vector2(x1, y0), rot);
        var c = Turn(o, new Vector2(x1, y1), rot);
        var d = Turn(o, new Vector2(x0, y1), rot);
        dl.AddQuadFilled(a, b, c, d, C(col, later ? 0.07f : fill));
        dl.AddQuad(a, b, c, d, C(col, later ? 0.7f : 0.95f), rim);
    }

    /// <summary>A cross: two bars, arms <paramref name="l"/> from the middle, <paramref name="w"/> wide, its outline drawn once.</summary>
    public static void TeleCross(ImDrawListPtr dl, Vector2 o, float w, float l, float rot, Vector4 col, float rim, bool later = false)
    {
        var fill = C(col, later ? 0.07f : 0.25f);
        var h = w / 2;
        Quad(dl, o, -h, -l, h, l, rot, fill);
        Quad(dl, o, -l, -h, -h, h, rot, fill);
        Quad(dl, o, h, -h, l, h, rot, fill);
        // The outline, round the plus.
        Span<Vector2> pts = stackalloc Vector2[12];
        var local = new Vector2[] { new(-h, -l), new(h, -l), new(h, -h), new(l, -h), new(l, h), new(h, h), new(h, l), new(-h, l), new(-h, h), new(-l, h), new(-l, -h), new(-h, -h) };
        for (var i = 0; i < 12; i++) pts[i] = Turn(o, local[i], rot);
        dl.AddPolyline(ref pts[0], 12, C(col, later ? 0.7f : 0.95f), ImDrawFlags.Closed, rim);
    }

    private static void Quad(ImDrawListPtr dl, Vector2 o, float x0, float y0, float x1, float y1, float rot, uint fill) =>
        dl.AddQuadFilled(Turn(o, new Vector2(x0, y0), rot), Turn(o, new Vector2(x1, y0), rot), Turn(o, new Vector2(x1, y1), rot), Turn(o, new Vector2(x0, y1), rot), fill);

    // ---------------------------------------------------------------------------------------------
    // Marks on and round a telegraph.

    /// <summary>A chevron at <paramref name="c"/> pointing at <paramref name="rot"/>, <paramref name="s"/> across, as the board's.</summary>
    public static void Chevron(ImDrawListPtr dl, Vector2 c, float rot, float s, uint col, float width)
    {
        Span<Vector2> p = stackalloc Vector2[3];
        p[0] = Turn(c, new Vector2(-s / 2, s / 3), rot);
        p[1] = Turn(c, new Vector2(0, -s / 3), rot);
        p[2] = Turn(c, new Vector2(s / 2, s / 3), rot);
        dl.AddPolyline(ref p[0], 3, col, ImDrawFlags.None, width);
    }

    /// <summary>A small triangle pointing at <paramref name="rot"/>.</summary>
    public static void Triangle(ImDrawListPtr dl, Vector2 c, float rot, float s, uint col) =>
        dl.AddTriangleFilled(Turn(c, new Vector2(0, -s * 0.62f), rot), Turn(c, new Vector2(s * 0.55f, s * 0.42f), rot), Turn(c, new Vector2(-s * 0.55f, s * 0.42f), rot), col);

    /// <summary>Four pairs of chevrons round a ring, pointing in (a stack) or out.</summary>
    public static void RingChevrons(ImDrawListPtr dl, Vector2 c, float r, Vector4 col, bool inward, float unit, int n = 4, float offset = 0)
    {
        var s = MathF.Min(1.5f * unit, r * 0.5f);
        for (var i = 0; i < n; i++)
        {
            var d = offset + 360f / n * i;
            for (var k = 0; k < 2; k++)
            {
                var at = Pol(c, r + (inward ? (0.55f - k * 0.75f) * unit : (0.1f + k * 0.75f) * unit), d);
                Chevron(dl, at, d + (inward ? 180 : 0), s, C(col, k == 1 ? 0.55f : 1f), MathF.Max(1, 0.26f * unit));
            }
        }
    }

    /// <summary>The tankbuster's shield (`M0 -1C.5 -.72 …`), <paramref name="s"/> its half-height in pixels.</summary>
    public static void ShieldMark(ImDrawListPtr dl, Vector2 c, float s, Vector4 fill, Vector4 edge)
    {
        Span<Vector2> p = stackalloc Vector2[9];
        p[0] = c + new Vector2(0, -1) * s;
        p[1] = c + new Vector2(0.5f, -0.75f) * s;
        p[2] = c + new Vector2(0.9f, -0.7f) * s;
        p[3] = c + new Vector2(0.9f, 0.02f) * s;
        p[4] = c + new Vector2(0.62f, 0.72f) * s;
        p[5] = c + new Vector2(0, 1.06f) * s;
        p[6] = c + new Vector2(-0.62f, 0.72f) * s;
        p[7] = c + new Vector2(-0.9f, 0.02f) * s;
        p[8] = c + new Vector2(-0.9f, -0.7f) * s;
        Span<Vector2> q = stackalloc Vector2[10];
        p.CopyTo(q);
        q[9] = c + new Vector2(-0.5f, -0.75f) * s;
        dl.AddConvexPolyFilled(ref q[0], 10, Palette.U32(fill));
        dl.AddPolyline(ref q[0], 10, Palette.U32(edge), ImDrawFlags.Closed, MathF.Max(1, s * 0.16f));
    }

    /// <summary>An order badge (`badge`): the hit's number on a disc, hollow for later ones.</summary>
    public static void Badge(ImDrawListPtr dl, Vector2 c, int n, Vector4 col, bool later, float unit)
    {
        var r = MathF.Max(S(5), 0.82f * unit);
        dl.AddCircleFilled(c, r, Palette.U32(later ? Palette.Hex(0x17171b) : col), 20);
        dl.AddCircle(c, r, Palette.U32(later ? col : Palette.OnLight), 20, MathF.Max(1, 0.14f * unit));
        var t = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var size = Txt.Measure(t, Face.MonoSemibold, 10);
        if (size.X < r * 2) Txt.Draw(dl, c - size / 2, t, Face.MonoSemibold, 10, later ? col : Palette.OnEmber);
    }

    /// <summary>A dashed arc or circle: dashes and gaps in pixels.</summary>
    public static void DashedArc(ImDrawListPtr dl, Vector2 c, float r, float d0, float d1, uint col, float width, float dash, float gap)
    {
        var length = MathF.PI * 2 * r * (d1 - d0) / 360f;
        if (length <= 0 || r < 1) return;
        var n = Math.Max(1, (int)(length / (dash + gap)));
        var per = (d1 - d0) / n;
        var on = per * dash / (dash + gap);
        for (var i = 0; i < n; i++)
        {
            dl.PathClear();
            Arc(dl, c, r, d0 + per * i, d0 + per * i + on, 3);
            dl.PathStroke(col, ImDrawFlags.None, width);
        }
    }

    /// <summary>A dashed line.</summary>
    public static void DashedLine(ImDrawListPtr dl, Vector2 a, Vector2 b, uint col, float width, float dash, float gap)
    {
        var len = Vector2.Distance(a, b);
        if (len < 0.5f) return;
        var dir = (b - a) / len;
        for (var t = 0f; t < len; t += dash + gap)
            dl.AddLine(a + dir * t, a + dir * MathF.Min(len, t + dash), col, width);
    }

    // ---------------------------------------------------------------------------------------------
    // Mechanics: each as the board draws it. Sizes in pixels; `unit` is a yalm's worth of pixels
    // (the decorations' scale: chevrons, rims, badges).

    public static void Stack(ImDrawListPtr dl, Vector2 c, float r, Vector4 col, float unit, float rim, bool later = false)
    {
        TeleCircle(dl, c, r, col, rim, 0.16f, later);
        RingChevrons(dl, c, r, col, true, unit);
    }

    public static void Spread(ImDrawListPtr dl, Vector2 c, float r, Vector4 col, float unit, float rim, bool later = false)
    {
        TeleCircle(dl, c, r, col, rim, 0.16f, later);
        foreach (var d in new[] { 45f, 135f, 225f, 315f }) Triangle(dl, Pol(c, r + 0.25f * unit, d), d, 0.95f * unit, Palette.U32(col));
    }

    public static void Defamation(ImDrawListPtr dl, Vector2 c, float r, Vector4 col, float unit, float rim, bool later = false)
    {
        TeleCircle(dl, c, r, col, rim, 0.13f, later);
        foreach (var d in new[] { 90f, 270f }) Triangle(dl, Pol(c, r + 0.4f * unit, d), d, 1.5f * unit, Palette.U32(col));
    }

    public static void Buster(ImDrawListPtr dl, Vector2 c, float r, Vector4 col, float unit, float rim, bool cleave = false, bool later = false)
    {
        TeleCircle(dl, c, r, col, rim, 0.18f, later);
        dl.AddCircle(c, r * 0.62f, C(col, 0.55f), Segments, MathF.Max(1, 0.1f * unit));
        if (cleave) foreach (var d in new[] { 0f, 90f, 180f, 270f }) Triangle(dl, Pol(c, r + 0.3f * unit, d), d, unit, Palette.U32(col));
        ShieldMark(dl, c + new Vector2(0, -r * 0.52f), MathF.Min(1.15f * unit, r * 0.3f), col, Palette.Hex(0x2a0808));
    }

    public static void SharedBuster(ImDrawListPtr dl, Vector2 c, float r, Vector4 col, float unit, float rim, bool later = false)
    {
        TeleCircle(dl, c, r, col, rim, 0.18f, later);
        RingChevrons(dl, c, r, col, true, unit);
        ShieldMark(dl, c, MathF.Min(1.1f * unit, r * 0.34f), col, Palette.Hex(0x2a0808));
    }

    /// <summary>A tower: a cool fill, the soak rim, an inner ring, chevrons in, and a dot per soaker (or a shield for a tank tower).</summary>
    public static void Tower(ImDrawListPtr dl, Vector2 c, float r, Vector4 col, float unit, int soakers, bool tank)
    {
        dl.AddCircleFilled(c, r, C(col, 0.2f), Segments);
        dl.AddCircleFilled(c, r * 0.7f, Palette.U32(Palette.Rgba(207, 224, 255, 0.12f)), Segments);
        dl.AddCircle(c, r, Palette.U32(col), Segments, MathF.Max(1, 0.2f * unit));
        dl.AddCircle(c, r * 0.66f, C(col, 0.6f), Segments, MathF.Max(1, 0.08f * unit));
        RingChevrons(dl, c, r, col, true, unit, 4, 45);
        if (tank)
        {
            ShieldMark(dl, c, 0.95f * unit, Palette.Hex(0xdbe6ff), Palette.Hex(0x1b2c5c));
            return;
        }
        var n = Math.Clamp(soakers, 1, 8);
        for (var i = 0; i < n; i++)
        {
            var p = c + new Vector2((i - (n - 1) / 2f) * 0.95f * unit, 0);
            dl.AddCircleFilled(p, MathF.Max(1.5f, 0.36f * unit), Palette.U32(Palette.Hex(0xeef3ff)), 12);
            dl.AddCircle(p, MathF.Max(1.5f, 0.36f * unit), Palette.U32(Palette.Hex(0x1b2c5c)), 12, MathF.Max(1, 0.1f * unit));
        }
    }

    /// <summary>A knockback (rings of chevrons flowing out) or a draw-in (flowing in), over a soft fill.</summary>
    public static void Flow(ImDrawListPtr dl, Vector2 c, float r, float unit, float rim, bool inward)
    {
        TeleCircle(dl, c, r, Palette.Dodge, rim, 0.13f);
        var chev = Palette.U32(Palette.WithAlpha(Palette.Move, 0.78f));
        var w = MathF.Max(1, 0.19f * unit);
        var first = 3.4f * unit;
        var step = 2.5f * unit;
        if (r < first + step)
        {
            // Small (a well): one ring of eight.
            first = r * 0.55f;
            step = r;
        }
        var ring = 0;
        for (var rr = first; rr < r - 0.6f * unit; rr += step, ring++)
        {
            var n = Math.Max(6, (int)MathF.Round(rr / unit * 1.5f));
            for (var i = 0; i < n; i++)
            {
                var d = 360f / n * i + (ring % 2 == 0 ? 0 : 180f / n);
                Chevron(dl, Pol(c, rr, d), d + (inward ? 180 : 0), (0.9f + rr / unit * 0.045f) * unit, chev, w);
            }
        }
    }

    /// <summary>An exaflare: the first hit, the ones to come along <paramref name="rot"/> (hollow, dashed), a chevron the way it goes.</summary>
    public static void Moving(ImDrawListPtr dl, Vector2 c, float r, float rot, float gap, int ahead, float unit, float rim, bool later = false)
    {
        for (var i = Math.Clamp(ahead, 1, 8); i >= 1; i--)
        {
            var p = Pol(c, i * gap, rot);
            dl.AddCircleFilled(p, r, C(Palette.Dodge, 0.05f), Segments);
            DashedArc(dl, p, r, 0, 360, C(Palette.Dodge, 0.45f), MathF.Max(1, 0.1f * unit), 0.5f * unit, 0.5f * unit);
        }
        TeleCircle(dl, c, r, Palette.Dodge, rim, 0.2f, later);
        Chevron(dl, Pol(c, 0.2f * unit, rot), rot, r * 0.9f, Palette.U32(Palette.WithAlpha(Palette.Hex(0xffd2ae), later ? 0.5f : 1f)), MathF.Max(1.2f, 0.45f * unit));
    }

    /// <summary>Proximity: rings rolling out from a marked middle, worse the nearer you stand.</summary>
    public static void Proximity(ImDrawListPtr dl, Vector2 c, float r, float unit)
    {
        dl.AddCircleFilled(c, r, C(Palette.Dodge, 0.05f), Segments);
        dl.AddCircleFilled(c, r * 0.5f, C(Palette.Dodge, 0.12f), Segments);
        dl.AddCircleFilled(c, r * 0.2f, C(Palette.Dodge, 0.2f), Segments);
        foreach (var k in new[] { 0.25f, 0.5f, 0.75f, 1f })
            dl.AddCircle(c, r * k, C(Palette.Dodge, 1.05f - k * 0.6f), Segments, MathF.Max(1, (k == 1 ? 0.3f : 0.12f) * unit));
        dl.AddCircleFilled(c, MathF.Max(2, 0.9f * unit), Palette.U32(Palette.Dodge), 16);
        dl.AddCircleFilled(c, MathF.Max(1, 0.35f * unit), Palette.U32(Palette.OnEmber), 12);
    }

    /// <summary>A gaze: the eye, its red iris and white glint, rays round it (look away).</summary>
    public static void Gaze(ImDrawListPtr dl, Vector2 c, float s)
    {
        var col = Palette.Look;
        foreach (var d in new[] { -60f, -30f, 0f, 30f, 60f, 120f, 150f, 180f, 210f, 240f })
        {
            var a = Pol(Vector2.Zero, 1.25f * s, d);
            var b = Pol(Vector2.Zero, 1.75f * s, d);
            dl.AddLine(c + new Vector2(a.X, a.Y * 0.72f), c + new Vector2(b.X, b.Y * 0.72f), Palette.U32(col), MathF.Max(1, 0.16f * s));
        }
        Span<Vector2> eye = stackalloc Vector2[24];
        for (var i = 0; i < 12; i++)
        {
            var t = i / 11f;
            var x = -1.5f + 3f * t;
            // Two quadratic curves through (0, ∓1.15): y = ∓2.3·t·(1 − t).
            var y = -2.3f * t * (1 - t);
            eye[i] = c + new Vector2(x, y) * s;
            eye[12 + i] = c + new Vector2(1.5f - 3f * t, -y) * s;
        }
        dl.AddConvexPolyFilled(ref eye[0], 24, Palette.U32(Palette.Hex(0x2a0709)));
        dl.AddPolyline(ref eye[0], 24, Palette.U32(col), ImDrawFlags.Closed, MathF.Max(1, 0.2f * s));
        dl.AddCircleFilled(c, 0.52f * s, Palette.U32(col), 16);
        dl.AddCircleFilled(c, MathF.Max(1, 0.2f * s), Palette.U32(Palette.White), 10);
    }

    /// <summary>Line of sight: a pillar, and the shadow behind it (away from <paramref name="from"/>) where you hide.</summary>
    public static void LineOfSight(ImDrawListPtr dl, Vector2 c, float r, Vector2 from, float length, float unit)
    {
        var dir = c - from;
        var a = dir.LengthSquared() < 1e-3f ? 180f : MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI + 90f;
        var safe = Palette.Ready;
        dl.PathClear();
        dl.PathLineTo(c);
        Arc(dl, c, length, a - 31, a + 31, 10);
        dl.PathFillConvex(C(safe, 0.14f));
        DashedArc(dl, c, length, a - 31, a + 31, C(safe, 0.6f), MathF.Max(1, 0.1f * unit), 0.5f * unit, 0.4f * unit);
        dl.AddCircleFilled(c, r, Palette.U32(Palette.Hex(0x3b3c46)), 24);
        dl.AddCircle(c, r, Palette.U32(Palette.Hex(0x6b6c78)), 24, MathF.Max(1, 0.18f * unit));
        dl.AddCircleFilled(c - new Vector2(r * 0.2f), r * 0.45f, Palette.U32(Palette.Rgba(0x55, 0x56, 0x61, 0.5f)), 16);
    }

    /// <summary>A rotation: two arcs with arrowheads (clockwise ember, anticlockwise blue).</summary>
    public static void Rotation(ImDrawListPtr dl, Vector2 c, float r, bool ccw, float unit)
    {
        var col = ccw ? Palette.Soak : Palette.Dodge;
        foreach (var a in new[] { 20f, 200f })
        {
            dl.PathClear();
            Arc(dl, c, r, a, a + 130, 16);
            dl.PathStroke(Palette.U32(col), ImDrawFlags.None, MathF.Max(1.5f, 0.55f * unit));
            var head = ccw ? Pol(c, r, a) : Pol(c, r, a + 130);
            Triangle(dl, head, ccw ? a - 90 : a + 220, 1.7f * unit, Palette.U32(col));
        }
    }

    /// <summary>A knock-up: a soft telegraph with an arrow standing up in it.</summary>
    public static void KnockUp(ImDrawListPtr dl, Vector2 c, float r, float unit, float rim)
    {
        TeleCircle(dl, c, r, Palette.Dodge, rim);
        var s = MathF.Min(r * 0.6f, 1.05f * unit * 2);
        dl.AddTriangleFilled(c + new Vector2(0, -1.05f) * s, c + new Vector2(0.95f, 0.1f) * s, c + new Vector2(-0.95f, 0.1f) * s, Palette.U32(Palette.Hex(0xffd2ae)));
        dl.AddRectFilled(c + new Vector2(-0.38f, 0.1f) * s, c + new Vector2(0.38f, 1f) * s, Palette.U32(Palette.Hex(0xffd2ae)));
    }

    /// <summary>A checkerboard of tiles <paramref name="n"/> by n across <paramref name="size"/> pixels.</summary>
    public static void Tiles(ImDrawListPtr dl, Vector2 c, float size, int n, float rot, Vector4 col, float rim, bool alt = false)
    {
        n = Math.Clamp(n, 2, 12);
        var s = size / n;
        var x0 = -size / 2;
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
        {
            if ((i + j + (alt ? 1 : 0)) % 2 != 0) continue;
            TeleRect(dl, c, x0 + i * s, x0 + j * s, x0 + (i + 1) * s, x0 + (j + 1) * s, rot, col, MathF.Max(1, rim * 0.6f), fill: 0.22f);
        }
    }

    /// <summary>A puddle: a soft teal blob and its ring.</summary>
    public static void Puddle(ImDrawListPtr dl, Vector2 c, float r, Vector4 col, float unit)
    {
        Span<Vector2> pts = stackalloc Vector2[20];
        for (var i = 0; i < 20; i++)
        {
            var k = 0.84f + 0.2f * MathF.Abs(MathF.Sin(12.9f + i / 2 * 4.7f));
            pts[i] = Pol(c, r * (i % 2 == 0 ? k : (k + 0.84f + 0.2f * MathF.Abs(MathF.Sin(12.9f + (i / 2 + 1) * 4.7f))) / 2), i * 18f);
        }
        dl.AddCircleFilled(c, r * 0.86f, C(col, 0.24f), Segments);
        dl.AddPolyline(ref pts[0], 20, Palette.U32(col), ImDrawFlags.Closed, MathF.Max(1, 0.17f * unit));
        dl.AddCircle(c, r * 0.5f, C(col, 0.6f), Segments, MathF.Max(1, 0.08f * unit));
    }

    /// <summary>A landmine: a telegraph with a dashed trigger ring and a dot.</summary>
    public static void Landmine(ImDrawListPtr dl, Vector2 c, float r, float unit, float rim)
    {
        TeleCircle(dl, c, r, Palette.Dodge, rim, 0.24f);
        DashedArc(dl, c, MathF.Max(2, 0.62f * unit), 0, 360, Palette.U32(Palette.Hex(0xffd2ae)), MathF.Max(1, 0.24f * unit), 0.32f * unit, 0.26f * unit);
        dl.AddCircleFilled(c, MathF.Max(1, 0.2f * unit), Palette.U32(Palette.Hex(0xffd2ae)), 10);
    }

    /// <summary>A side knockback: a box of chevrons all pushing one way.</summary>
    public static void SideKnockback(ImDrawListPtr dl, Vector2 o, float w, float l, float rot, float push, float unit, float rim)
    {
        TeleRect(dl, o, -w / 2, -l, w / 2, 0, rot, Palette.Dodge, rim, fill: 0.15f);
        var col = Palette.U32(Palette.WithAlpha(Palette.Move, 0.8f));
        for (var y = -l + 1.6f * unit; y < -0.4f * unit; y += 2.3f * unit)
        for (var x = -w / 2 + 1.5f * unit; x < w / 2 - 0.6f * unit; x += 2.6f * unit)
            Chevron(dl, Turn(o, new Vector2(x, y), rot), rot + push, 1.25f * unit, col, MathF.Max(1, 0.2f * unit));
    }

    /// <summary>A safe spot: a green disc with a tick.</summary>
    public static void Safe(ImDrawListPtr dl, Vector2 c, float r, float unit)
    {
        dl.AddCircleFilled(c, r, Palette.U32(Palette.Hex(0x0f2a1a)), 20);
        dl.AddCircle(c, r, Palette.U32(Palette.Ready), 20, MathF.Max(1, 0.18f * unit));
        Span<Vector2> p = stackalloc Vector2[3];
        p[0] = c + new Vector2(-0.45f, 0.02f) / 1.05f * r;
        p[1] = c + new Vector2(-0.12f, 0.36f) / 1.05f * r;
        p[2] = c + new Vector2(0.5f, -0.32f) / 1.05f * r;
        dl.AddPolyline(ref p[0], 3, Palette.U32(Palette.Ready), ImDrawFlags.None, MathF.Max(1.2f, 0.22f * unit));
    }

    /// <summary>A tether: a dark line under its colour, dashed for a variant (stretch, close, pass, chase).</summary>
    public static void Tether(ImDrawListPtr dl, Vector2 a, Vector2 b, Vector4 col, float unit, bool dashed)
    {
        dl.AddLine(a, b, Palette.U32(Palette.WithAlpha(Palette.OnLight, 0.6f)), MathF.Max(2, 0.5f * unit));
        if (dashed) DashedLine(dl, a, b, Palette.U32(col), MathF.Max(1.2f, 0.24f * unit), 0.7f * unit, 0.5f * unit);
        else dl.AddLine(a, b, Palette.U32(col), MathF.Max(1.2f, 0.24f * unit));
    }

    /// <summary>An arrow: a dark line under its colour and a head.</summary>
    public static void Arrow(ImDrawListPtr dl, Vector2 a, Vector2 b, Vector4 col, float unit, bool dashed)
    {
        dl.AddLine(a, b, Palette.U32(Palette.WithAlpha(Palette.OnLight, 0.7f)), MathF.Max(2, 0.5f * unit));
        if (dashed) DashedLine(dl, a, b, Palette.U32(col), MathF.Max(1, 0.2f * unit), 0.6f * unit, 0.5f * unit);
        else dl.AddLine(a, b, Palette.U32(col), MathF.Max(1, 0.2f * unit));
        if (Vector2.Distance(a, b) > 2)
        {
            var dir = b - a;
            var deg = MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI + 90f;
            Triangle(dl, b, deg, MathF.Max(S(5), 1.05f * unit), Palette.U32(col));
        }
    }

    /// <summary>The reticle round a point (the board's, on you): four corner brackets of a square 2r across.</summary>
    public static void PointReticle(ImDrawListPtr dl, Vector2 c, float r, Vector4 col, float width)
    {
        var k = MathF.Min(r * 0.42f, r);
        var u = Palette.U32(col);
        foreach (var (sx, sy) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var corner = c + new Vector2(sx * r, sy * r);
            dl.AddLine(corner, corner - new Vector2(sx * k, 0), u, width);
            dl.AddLine(corner, corner - new Vector2(0, sy * k), u, width);
        }
    }
}
