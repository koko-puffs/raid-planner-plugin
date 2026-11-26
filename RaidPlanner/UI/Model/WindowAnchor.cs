using System.Numerics;

namespace RaidPlanner.UI.Model;

/// <summary>What <see cref="WindowAnchor.Track"/> did to the anchor this frame (the window saves on all but <see cref="None"/>).</summary>
public enum AnchorChange
{
    /// <summary>Nothing: it's kept as it is (locked, or unlocked and not held). Where the window was kept on the screen isn't read.</summary>
    None,

    /// <summary>Never anchored: read from where the window is, the first time it's drawn.</summary>
    First,

    /// <summary>Grows changed: moved to the other corner of the window where it is now, so it doesn't move then.</summary>
    Turned,

    /// <summary>You moved or sized it (unlocked, held): it follows the window.</summary>
    Moved,
}

/// <summary>
/// The anchor of a window that keeps its place by the edge Grows keeps, as its settings keep it: the meter's
/// (<c>MeterAnchorX</c>, <c>MeterAnchorY</c>, <c>MeterAnchored</c>, <c>MeterAnchorFor</c>, <c>MeterAnchorVersion</c>) and each
/// overlay's, the cooldowns, callouts, ready check and death recap (<see cref="OverlaySettings"/>'s <c>AnchorX</c> and on);
/// and what each frame does with it, away from ImGui so it's tested:
/// where the window goes (<see cref="Place"/>, before ImGui's Begin) and what the anchor becomes (<see cref="Track"/>,
/// inside the window). Only the player moving or sizing it (unlocked, or in Arrange windows), changing Grows, Arrange
/// windows' Reset places and the first time it's drawn change it; keeping it on the screen never does, nor locking or
/// unlocking it (the title bar is chrome over its content's corner), so it goes back exactly.
/// </summary>
public readonly record struct WindowAnchor(Vector2 At, bool Anchored, MeterGrows For, int Version)
{
    /// <summary>
    /// The anchor's meaning: 1 (since the release after 0.10.0) its content's corner, growing down under the title bar or
    /// the arranging head (<see cref="MeterLayout.Corner"/>); 0 the window's top-left, the title bar's top, so locking it
    /// (or Arrange windows' Done) lifted what it shows by the title bar's (or the head's) height.
    /// </summary>
    public const int Current = 1;

    /// <summary>
    /// An anchor saved before <see cref="Current"/>, as this version reads it, once: growing down, the chrome it's shown
    /// with now added (<paramref name="chrome"/>: 0 locked, the title bar's height unlocked), so it stays exactly where it
    /// is. One growing up (its foot is its content's), or never anchored, is as it was. Already current: itself.
    /// </summary>
    public WindowAnchor Migrated(float chrome)
    {
        if (Version >= Current) return this;
        var at = Anchored && For == MeterGrows.Down && float.IsFinite(chrome) && chrome > 0 ? At + new Vector2(0, chrome) : At;
        return this with { At = at, Version = Current };
    }

    /// <summary>
    /// Where the window's top-left goes this frame for its size and <paramref name="chrome"/> (screen px), kept on the
    /// screen (<see cref="MeterLayout.Place"/>; the anchor isn't changed by it); null when it isn't placed: never anchored,
    /// anchored for the other Grows (moved to the other corner inside the window first), or held (you're moving or sizing it).
    /// </summary>
    public Vector2? Place(MeterGrows grows, Vector2 size, float chrome, Vector2 screenMin, Vector2 screenMax, bool otherScreens, bool held)
    {
        if (!Anchored || For != grows || held || !float.IsFinite(size.Y)) return null;
        return MeterLayout.Place(At, size, grows, screenMin, screenMax, otherScreens, chrome);
    }

    /// <summary>
    /// Inside the window, each frame, with where it is and its size (screen px) and its chrome this frame: never anchored,
    /// read from there; anchored for the other Grows, the other corner of where it is (as you change Grows, so it doesn't
    /// move then); <paramref name="moved"/> (held, and it moved or changed size since last frame), following it. Otherwise
    /// as it is: where it was kept on the screen this frame is never read back.
    /// </summary>
    public (WindowAnchor Anchor, AnchorChange Change) Track(MeterGrows grows, Vector2 pos, Vector2 size, float chrome, bool moved)
    {
        var change = !Anchored ? AnchorChange.First : For != grows ? AnchorChange.Turned : moved ? AnchorChange.Moved : AnchorChange.None;
        if (change == AnchorChange.None) return (this, change);
        return (new WindowAnchor(MeterLayout.Corner(pos, size, grows, chrome), true, grows, Current), change);
    }
}
