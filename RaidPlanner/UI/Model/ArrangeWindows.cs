using System;
using System.Collections.Generic;
using System.Numerics;

namespace RaidPlanner.UI.Model;

/// <summary>A window Arrange windows (0.8.0) shows to drag into place.</summary>
public enum ArrangePart
{
    Meter,
    Plan,
    Callouts,
    Cooldowns,

    /// <summary>0.9.0: the ready check card.</summary>
    ReadyCheck,

    /// <summary>0.9.0: the death recap.</summary>
    DeathRecap,
}

/// <summary>Which windows take part in Arrange windows, what each is called there and which options it has (worked out away from ImGui so it's tested).</summary>
public static class ArrangeParts
{
    /// <summary>Every window that can take part, in the order the mode lists them (the first taking part is picked at first).</summary>
    public static readonly ArrangePart[] All = [ArrangePart.Meter, ArrangePart.Plan, ArrangePart.Callouts, ArrangePart.Cooldowns, ArrangePart.ReadyCheck, ArrangePart.DeathRecap];

    /// <summary>Its label in its arranging head and the options panel (the mock-up's).</summary>
    public static string Label(ArrangePart part) => part switch
    {
        ArrangePart.Meter => "Meter",
        ArrangePart.Plan => "Plan",
        ArrangePart.Callouts => "Callouts",
        ArrangePart.ReadyCheck => "Ready check",
        ArrangePart.DeathRecap => "Death recap",
        _ => "Party cooldowns",
    };

    /// <summary>
    /// Whether the options panel has Grows, Click-through and Background for it: the meter's and the overlays'
    /// own settings. The plan window has none of them (it's sized by its edges and has no anchor).
    /// </summary>
    public static bool HasOptions(ArrangePart part) => part != ArrangePart.Plan;

    /// <summary>
    /// The windows that take part, in <see cref="All"/>'s order: each one turned on (the meter's Show the meter;
    /// the plan window with Show my plan in duties on, or open now; the callouts' and the tracker's Show; since 0.9.0 the ready
    /// check card's and the death recap's Show).
    /// </summary>
    public static List<ArrangePart> Taking(bool meter, bool plan, bool callouts, bool cooldowns, bool readyCheck = false, bool deathRecap = false)
    {
        var list = new List<ArrangePart>(6);
        if (meter) list.Add(ArrangePart.Meter);
        if (plan) list.Add(ArrangePart.Plan);
        if (callouts) list.Add(ArrangePart.Callouts);
        if (cooldowns) list.Add(ArrangePart.Cooldowns);
        if (readyCheck) list.Add(ArrangePart.ReadyCheck);
        if (deathRecap) list.Add(ArrangePart.DeathRecap);
        return list;
    }
}

/// <summary>
/// A window's place and the settings Arrange windows can change, as plain values, kept as the mode starts so
/// Cancel can put them back. For an overlay (<see cref="OverlaySettings"/>) and the meter: locked, click-through,
/// Grows, background, the anchor (screen px) and which corner it is, its width (CSS px; 0 its first) and, the
/// meter's, the height you gave it (CSS px). For the plan window: locked, and its place and size as ImGui last
/// drew it (screen px: <see cref="AnchorX"/>, <see cref="AnchorY"/>, <see cref="Width"/>, <see cref="Height"/>;
/// <see cref="Anchored"/> false when it hadn't been drawn yet).
/// </summary>
public readonly record struct WindowPlace(
    bool Locked,
    bool ClickThrough,
    MeterGrows Grows,
    float Background,
    float Width,
    float Height,
    bool Anchored,
    float AnchorX,
    float AnchorY,
    MeterGrows AnchorFor)
{
    /// <summary>An overlay's, from its settings.</summary>
    public static WindowPlace Of(OverlaySettings s) =>
        new(s.Locked, s.ClickThrough, s.Grows, s.Background, s.Width, 0, s.Anchored, s.AnchorX, s.AnchorY, s.AnchorFor);

    /// <summary>Puts an overlay's settings back as they were (Cancel); its Show, Everywhere and Compact aren't the mode's and stay.</summary>
    public void PutBack(OverlaySettings s)
    {
        s.Locked = Locked;
        s.ClickThrough = ClickThrough;
        s.Grows = Grows;
        s.Background = Background;
        s.Width = Width;
        s.Anchored = Anchored;
        s.AnchorX = AnchorX;
        s.AnchorY = AnchorY;
        s.AnchorFor = AnchorFor;
    }

    /// <summary>The plan window's: its lock, and where and how big ImGui last drew it (null: not drawn yet).</summary>
    public static WindowPlace OfPlan(bool locked, Vector2? pos, Vector2? size) =>
        new(locked, false, MeterGrows.Down, 1, size?.X ?? 0, size?.Y ?? 0, pos != null && size != null, pos?.X ?? 0, pos?.Y ?? 0, MeterGrows.Down);
}

/// <summary>
/// Arrange windows' state (0.8.0): on or off, the windows taking part, the one picked (last pressed or dragged:
/// the options panel's), each one's place as the mode started (<see cref="WindowPlace"/>, for Cancel), and the
/// samples it started (the meter's sample fight, the callouts' and the tracker's samples, the plan window it
/// opened), to stop when it ends. Pure: the windows and the settings are the plugin's (UI/ArrangeWindow.cs).
/// </summary>
public sealed class ArrangeSession
{
    private readonly List<ArrangePart> parts = new(4);
    private readonly Dictionary<ArrangePart, WindowPlace> before = new();
    private readonly HashSet<ArrangePart> started = new();

    /// <summary>The mode is on.</summary>
    public bool On { get; private set; }

    /// <summary>The window picked (its outline in ember, its options in the panel); null with none taking part.</summary>
    public ArrangePart? Picked { get; private set; }

    /// <summary>The windows taking part, in <see cref="ArrangeParts.All"/>'s order.</summary>
    public IReadOnlyList<ArrangePart> Parts => parts;

    /// <summary>The mode is on and this window takes part (it shows, unlocked, with its head and outline).</summary>
    public bool Takes(ArrangePart part) => On && parts.Contains(part);

    /// <summary>
    /// Starts the mode with these windows and their places now (each once; in <see cref="ArrangeParts.All"/>'s
    /// order); the first is picked. False (and nothing changes) when it's on already.
    /// </summary>
    public bool Start(IEnumerable<(ArrangePart Part, WindowPlace Place)> taking)
    {
        if (On) return false;
        parts.Clear();
        before.Clear();
        started.Clear();
        var given = new Dictionary<ArrangePart, WindowPlace>();
        foreach (var (part, place) in taking) given[part] = place;
        foreach (var part in ArrangeParts.All)
        {
            if (!given.TryGetValue(part, out var place)) continue;
            parts.Add(part);
            before[part] = place;
        }
        Picked = parts.Count > 0 ? parts[0] : null;
        On = true;
        return true;
    }

    /// <summary>A window's place as the mode started (null when it isn't taking part).</summary>
    public WindowPlace? Before(ArrangePart part) => On && before.TryGetValue(part, out var p) ? p : null;

    /// <summary>A window's place learnt after the start (the plan window opened by the mode: where it is once first drawn).</summary>
    public void Learnt(ArrangePart part, WindowPlace place)
    {
        if (Takes(part)) before[part] = place;
    }

    /// <summary>The mode started this window's sample (or opened it): stopped again when it ends.</summary>
    public void StartedSample(ArrangePart part)
    {
        if (Takes(part)) started.Add(part);
    }

    /// <summary>Whether the mode started this window's sample (or opened it).</summary>
    public bool StartedSampleOf(ArrangePart part) => On && started.Contains(part);

    /// <summary>Picks a window taking part (pressed or dragged); one that isn't is ignored.</summary>
    public void Pick(ArrangePart part)
    {
        if (Takes(part)) Picked = part;
    }

    /// <summary>Ends the mode (Done or Cancel; nothing when it's off): returns the windows whose samples it started, to stop.</summary>
    public List<ArrangePart> End()
    {
        var stop = new List<ArrangePart>(started.Count);
        if (!On) return stop;
        foreach (var part in ArrangeParts.All)
            if (started.Contains(part))
                stop.Add(part);
        On = false;
        Picked = null;
        parts.Clear();
        before.Clear();
        started.Clear();
        return stop;
    }
}

/// <summary>Where Arrange windows puts things (screen px), worked out away from ImGui so it's tested.</summary>
public static class ArrangeLayout
{
    /// <summary>
    /// Where ImGui puts a window it has never drawn: 60px in from the screen's top-left corner. The meter's and the
    /// plan window's first place (neither sets one: ImGui does, then the meter's anchor is read from it).
    /// </summary>
    public static readonly Vector2 ImGuiFirst = new(60, 60);

    /// <summary>The meter's first place (Reset places): its anchor (its Grows corner) for a meter this big, its chrome on top, at ImGui's first place.</summary>
    public static Vector2 MeterFirst(Vector2 screenMin, Vector2 size, MeterGrows grows, float chrome = 0) => MeterLayout.Corner(screenMin + ImGuiFirst, size, grows, chrome);

    /// <summary>
    /// The plan window's place on Reset places: at the right of the screen, 60px in, centred down it (its ImGui first
    /// place, 60 in from the top-left, is the meter's too); kept on the screen.
    /// </summary>
    public static Vector2 PlanFirst(Vector2 screenMin, Vector2 screenMax, Vector2 size)
    {
        var x = screenMax.X - ImGuiFirst.X - size.X;
        var y = screenMin.Y + (screenMax.Y - screenMin.Y - size.Y) / 2;
        x = Math.Clamp(x, screenMin.X, MathF.Max(screenMin.X, screenMax.X - size.X));
        y = Math.Clamp(y, screenMin.Y, MathF.Max(screenMin.Y, screenMax.Y - size.Y));
        return new Vector2(x, y);
    }

    /// <summary>The toolbar's top-left: centred across the screen, its foot <paramref name="gap"/> above the screen's bottom edge.</summary>
    public static Vector2 Toolbar(Vector2 screenMin, Vector2 screenMax, Vector2 size, float gap) =>
        new(screenMin.X + (screenMax.X - screenMin.X - size.X) / 2, MathF.Max(screenMin.Y, screenMax.Y - gap - size.Y));

    /// <summary>The options panel's top-left: <paramref name="gap"/> in from the screen's top-right corner.</summary>
    public static Vector2 Panel(Vector2 screenMin, Vector2 screenMax, float width, float gap) =>
        new(MathF.Max(screenMin.X, screenMax.X - gap - width), screenMin.Y + gap);
}
