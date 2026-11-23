using System.Numerics;
using System.Threading;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace RaidPlanner.UI;

/// <summary>
/// The plugin's own scale, on top of Dalamud's (since 0.8.0; Settings → Windows → Size, <see cref="Model.WindowSize"/>):
/// <see cref="Theme.S"/> draws every size at it, the fonts are built at it (<see cref="Fonts.Follow"/>), and the windows
/// are given their sizes at it (<see cref="Theme.Units(Vector2)"/>). It moves when the fonts have been built at a new one,
/// so the words and the windows move together.
/// </summary>
public static class UiScale
{
    private static int generation;

    /// <summary>The plugin's scale drawn at now (1: Dalamud's own size).</summary>
    public static float Factor { get; private set; } = 1;

    /// <summary>The scale the size setting and the screen want now (<see cref="Factor"/> follows once the fonts are built at it).</summary>
    public static float Wanted { get; private set; } = 1;

    /// <summary>The scale the windows were drawn at when the plugin last ran (<see cref="Configuration.WindowScaleWas"/>).</summary>
    public static float Was { get; private set; } = 1;

    /// <summary>Counts <see cref="Factor"/>'s moves.</summary>
    public static int Generation => Volatile.Read(ref generation);

    /// <summary>As the plugin starts: the scale the fonts are first built at, and the one the windows were last drawn at.</summary>
    public static void Start(float factor, float was)
    {
        Factor = Wanted = factor;
        Was = was > 0 ? was : 1;
    }

    public static void Want(float factor) => Wanted = factor;

    /// <summary>The fonts are built at a new scale and drawn with from this frame.</summary>
    public static void Moved(float factor)
    {
        Factor = factor;
        Interlocked.Increment(ref generation);
    }
}

/// <summary>
/// A window ImGui sizes (its size kept as you leave it): grown or shrunk with the plugin's scale when it moves, from
/// the size it was last drawn at, so its words keep their room (the main window, the plan window, the opened-up meter).
/// It starts from the scale the windows were drawn at when the plugin last ran, so a window ImGui remembers from then
/// follows too.
/// </summary>
public sealed class ScaleFollower
{
    private float seen = UiScale.Was;

    /// <summary>
    /// In PreDraw: the window's new size (Dalamud's units, for <see cref="Window.Size"/> with
    /// <c>ImGuiCond.Always</c> for one frame) when the scale has moved since it was last drawn at
    /// <paramref name="drawn"/> (screen px; zero before it's been drawn, which waits); else null.
    /// </summary>
    public Vector2? Resize(Vector2 drawn)
    {
        var f = UiScale.Factor;
        if (System.MathF.Abs(f - seen) < 0.001f || drawn.X <= 0 || drawn.Y <= 0) return null;
        var by = f / seen;
        seen = f;
        return drawn * by / ImGuiHelpers.GlobalScale;
    }
}

/// <summary>A window's smallest and largest size (CSS px), given to Dalamud at the plugin's scale (worked out again only when it moves).</summary>
public sealed class CssLimits
{
    private float at = -1;
    private WindowSizeConstraints given;

    public CssLimits(Vector2 min, Vector2 max)
    {
        Min = min;
        Max = max;
    }

    public Vector2 Min { get; }
    public Vector2 Max { get; }

    /// <summary>How much of the screen's width a window's smallest size may take at most (<see cref="Within"/>).</summary>
    public const float ScreenShare = 0.95f;

    private (float Factor, Vector2 Screen) withinAt = (-1, default);
    private WindowSizeConstraints within;

    /// <summary>
    /// For <see cref="Window.SizeConstraints"/>, the smallest size kept to 95% of the screen's width and 90% of its height
    /// (screen px; zero: not known), so a big window at a big size never needs more than the screen has.
    /// </summary>
    public WindowSizeConstraints Within(Vector2 screen)
    {
        var f = UiScale.Factor;
        if (withinAt.Factor == f && withinAt.Screen == screen) return within;
        withinAt = (f, screen);
        var min = Min * f;
        var units = ImGuiHelpers.GlobalScale;
        if (units > 0 && screen.X > 0) min.X = System.MathF.Min(min.X, screen.X * ScreenShare / units);
        if (units > 0 && screen.Y > 0) min.Y = System.MathF.Min(min.Y, screen.Y * 0.9f / units);
        within = new WindowSizeConstraints { MinimumSize = min, MaximumSize = Vector2.Max(Max * f, min) };
        return within;
    }

    /// <summary>For <see cref="Window.SizeConstraints"/>.</summary>
    public WindowSizeConstraints Now
    {
        get
        {
            var f = UiScale.Factor;
            if (f != at)
            {
                at = f;
                given = new WindowSizeConstraints { MinimumSize = Min * f, MaximumSize = Max * f };
            }
            return given;
        }
    }
}
