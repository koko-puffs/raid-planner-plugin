using System;

namespace RaidPlanner.UI.Model;

/// <summary>
/// Ready Check's three curves (CSS cubic-bezier), so the windows move as the site does:
/// <c>--e-snap</c> (0.2, 0.8, 0.2, 1) for a switch or a reticle closing in, <c>--e-out</c>
/// (0.16, 1, 0.3, 1) for what arrives or resolves, <c>--e-in</c> (0.5, 0, 0.9, 0.4) for what leaves.
/// </summary>
public static class Ease
{
    public static float Snap(float t) => Bezier(0.2f, 0.8f, 0.2f, 1f, t);

    public static float Out(float t) => Bezier(0.16f, 1f, 0.3f, 1f, t);

    public static float In(float t) => Bezier(0.5f, 0f, 0.9f, 0.4f, t);

    /// <summary>A CSS cubic-bezier(x1, y1, x2, y2) at progress <paramref name="t"/> (0–1, clamped): y for the x that is t.</summary>
    public static float Bezier(float x1, float y1, float x2, float y2, float t)
    {
        t = Math.Clamp(t, 0, 1);
        if (t is 0 or 1) return t;
        // Find the curve's parameter u whose x is t (Newton, then halving if it wanders), then its y.
        var u = t;
        for (var i = 0; i < 8; i++)
        {
            var x = Coord(x1, x2, u) - t;
            if (MathF.Abs(x) < 1e-5f) return Coord(y1, y2, u);
            var d = Slope(x1, x2, u);
            if (MathF.Abs(d) < 1e-6f) break;
            u -= x / d;
        }
        float lo = 0, hi = 1;
        u = t;
        for (var i = 0; i < 30; i++)
        {
            var x = Coord(x1, x2, u);
            if (MathF.Abs(x - t) < 1e-5f) break;
            if (x < t) lo = u;
            else hi = u;
            u = (lo + hi) / 2;
        }
        return Coord(y1, y2, u);
    }

    private static float Coord(float a, float b, float u)
    {
        var v = 1 - u;
        return 3 * v * v * u * a + 3 * v * u * u * b + u * u * u;
    }

    private static float Slope(float a, float b, float u)
    {
        var v = 1 - u;
        return 3 * v * v * a + 6 * v * u * (b - a) + 3 * u * u * (1 - b);
    }
}
