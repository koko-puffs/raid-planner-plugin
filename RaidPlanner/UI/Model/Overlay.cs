using System;
using System.Numerics;

namespace RaidPlanner.UI.Model;

/// <summary>
/// An overlay's own settings (the cooldown tracker's, the on-screen callouts'), kept in the plugin's
/// config: shown or not, where (in duties or everywhere), locked, click-through, which way it grows
/// and where it's anchored (as the meter's: <see cref="MeterLayout"/>), compact, its background, its width.
/// </summary>
[Serializable]
public sealed class OverlaySettings
{
    /// <summary>It's shown (when there's something to show).</summary>
    public bool Show { get; set; }

    /// <summary>Shown everywhere, not only in duties.</summary>
    public bool Everywhere { get; set; }

    /// <summary>No title bar, no moving or sizing.</summary>
    public bool Locked { get; set; }

    /// <summary>Locked, the mouse goes through it to the game.</summary>
    public bool ClickThrough { get; set; }

    /// <summary>Which edge stays put as its height changes by itself.</summary>
    public MeterGrows Grows { get; set; } = MeterGrows.Down;

    /// <summary>Smaller rows.</summary>
    public bool Compact { get; set; }

    /// <summary>How solid its background is (0–1; the rows' words stay solid).</summary>
    public float Background { get; set; } = 0.6f;

    /// <summary>Its width (CSS px; 0: its first width).</summary>
    public float Width { get; set; }

    /// <summary>The point it stays put at (screen px): its top-left growing down, bottom-left growing up.</summary>
    public float AnchorX { get; set; }

    public float AnchorY { get; set; }

    public bool Anchored { get; set; }

    /// <summary>Which corner the anchor is (moved to the other's place when Grows is changed).</summary>
    public MeterGrows AnchorFor { get; set; } = MeterGrows.Down;
}

/// <summary>Where an overlay goes before it's been placed, worked out away from ImGui so it's tested.</summary>
public static class OverlayLayout
{
    /// <summary>
    /// An overlay's first anchor, before you've moved it: centred across the screen at
    /// <paramref name="fromTop"/> of its height (as the corner its Grows keeps), so the callouts sit
    /// over the middle of the fight and the tracker by the party list.
    /// </summary>
    public static Vector2 FirstAnchor(Vector2 screenMin, Vector2 screenMax, float width, float height, float fromLeft, float fromTop, MeterGrows grows)
    {
        var size = screenMax - screenMin;
        var x = screenMin.X + size.X * fromLeft - width / 2;
        var y = screenMin.Y + size.Y * fromTop;
        x = Math.Clamp(x, screenMin.X, MathF.Max(screenMin.X, screenMax.X - width));
        return grows == MeterGrows.Up ? new Vector2(x, MathF.Min(screenMax.Y, y + height)) : new Vector2(x, y);
    }
}
