using System;
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

    /// <summary>
    /// Screen px per CSS px the windows were drawn at when the plugin last ran (Dalamud's scale times the plugin's:
    /// <see cref="Configuration.WindowScaleWas"/>): the sizes ImGui remembers from then are at it.
    /// </summary>
    public static float WasTotal { get; private set; } = 1;

    /// <summary>Counts <see cref="Factor"/>'s moves.</summary>
    public static int Generation => Volatile.Read(ref generation);

    /// <summary>As the plugin starts: the scale the fonts are first built at, and the total the windows were last drawn at.</summary>
    public static void Start(float factor, float wasTotal)
    {
        Factor = Wanted = factor;
        WasTotal = wasTotal > 0 ? wasTotal : 1;
    }

    public static void Want(float factor) => Wanted = factor;

    /// <summary>The fonts are built at a new scale and drawn with from this frame.</summary>
    public static void Moved(float factor)
    {
        Factor = factor;
        Interlocked.Increment(ref generation);
    }

    /// <summary>A size (screen px) kept to 95% of the screen's width and 90% of its height (screen px; zero: not known).</summary>
    public static Vector2 OnScreen(Vector2 size, Vector2 screen) => new(
        screen.X > 0 ? MathF.Min(size.X, screen.X * CssLimits.ScreenShare) : size.X,
        screen.Y > 0 ? MathF.Min(size.Y, screen.Y * CssLimits.ScreenHeightShare) : size.Y);
}

/// <summary>
/// A window ImGui sizes (its size kept as you leave it): grown or shrunk with the windows' scale (Dalamud's times the
/// plugin's) when it moves, from the size it was last drawn at, so its words keep their room (the main window, the plan
/// window, the opened-up meter). It starts from the scale the windows were drawn at when the plugin last ran: until the
/// window has been drawn, ImGui's size for it is the one from then (<see cref="Seen"/>, for its limits that frame).
/// </summary>
public sealed class ScaleFollower
{
    private float seen = UiScale.WasTotal;

    /// <summary>The scale its size was last known at (screen px per CSS px).</summary>
    public float Seen => seen;

    /// <summary>
    /// In PreDraw: the window's new size (Dalamud's units, for <see cref="Window.Size"/> with <c>ImGuiCond.Always</c> for
    /// one frame) when the scale has moved since it was last drawn at <paramref name="drawn"/> (screen px; zero before
    /// it's been drawn, which waits), kept on the <paramref name="screen"/>; else null.
    /// </summary>
    public Vector2? Resize(Vector2 drawn, Vector2 screen)
    {
        var now = Theme.Scale;
        if (MathF.Abs(now - seen) < 0.001f || drawn.X <= 0 || drawn.Y <= 0) return null;
        var size = UiScale.OnScreen(drawn * (now / seen), screen);
        seen = now;
        return size / ImGuiHelpers.GlobalScale;
    }
}

/// <summary>A window's smallest and largest size (CSS px), given to Dalamud at a scale (worked out again only when something it hangs on moves).</summary>
public sealed class CssLimits
{
    /// <summary>How much of the screen's width and height a window's smallest size may take at most (<see cref="Within"/>).</summary>
    public const float ScreenShare = 0.95f;

    public const float ScreenHeightShare = 0.9f;

    private (float Scale, float Units, Vector2 Screen) at = (-1, -1, default);
    private WindowSizeConstraints given;

    public CssLimits(Vector2 min, Vector2 max)
    {
        Min = min;
        Max = max;
    }

    public Vector2 Min { get; }
    public Vector2 Max { get; }

    /// <summary>For <see cref="Window.SizeConstraints"/>, at the windows' scale now.</summary>
    public WindowSizeConstraints Now => Within(default, Theme.Scale);

    /// <summary>
    /// For <see cref="Window.SizeConstraints"/>, at <paramref name="scale"/> (screen px per CSS px; the windows' now if
    /// 0), the smallest kept to 95% of the screen's width and 90% of its height (screen px; zero: not known), so a big
    /// window at a big size never needs more than the screen has.
    /// </summary>
    public WindowSizeConstraints Within(Vector2 screen, float scale = 0)
    {
        if (scale <= 0) scale = Theme.Scale;
        var units = ImGuiHelpers.GlobalScale;
        if (units <= 0) units = 1;
        if (at.Scale == scale && at.Units == units && at.Screen == screen) return given;
        at = (scale, units, screen);
        var min = UiScale.OnScreen(Min * scale, screen) / units;
        given = new WindowSizeConstraints { MinimumSize = min, MaximumSize = Vector2.Max(Max * scale / units, min) };
        return given;
    }
}
