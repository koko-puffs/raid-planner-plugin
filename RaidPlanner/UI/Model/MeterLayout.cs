using System;
using System.Numerics;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.UI.Model;

/// <summary>Which way the meter grows when its height changes by itself (Settings → Meter, Grows).</summary>
public enum MeterGrows
{
    /// <summary>Its top edge stays where it is: the head (the clock, the picker, the lock) on top, the rows reading down from it.</summary>
    Down,

    /// <summary>Its foot stays where it is: the head at the foot, the rows reading up from it (the top player nearest the head).</summary>
    Up,
}

/// <summary>
/// The small meter's sizes (CSS px and font sizes), normal or compact. Compact is the same meter
/// with less air: 20px rows (the spot 16, the name at 12px, the number in mono 600 12px, the share
/// in mono 10px), the bar drawn as the row's background, a 30px head with the tabs in a menu (so the
/// tab row's 27px goes), and slimmer pace, column heads, limit break and foot.
/// </summary>
public sealed record MeterSizes(
    float Head,
    float Tabs,
    float Pace,
    float Cols,
    float Row,
    float LimitBreak,
    float Foot,
    float Pad,
    float Gap,
    float Spot,
    float JobCol,
    float ValueCol,
    float SecondCol,
    int ClockPx,
    int NamePx,
    int JobPx,
    int ValuePx,
    int SecondPx,
    int FootPx,
    float DeathRow)
{
    /// <summary>The mock-up's meter: a 36px head, 27px tabs, 26px pace, 20px column heads, 25px rows, a 27px limit break, a 28px foot.</summary>
    public static readonly MeterSizes Normal = new(36, 27, 26, 20, 25, 27, 28, 12, 8, 18, 30, 66, 40, 15, 13, 10, 13, 11, 11, 0);

    /// <summary>
    /// Compact: a 30px head and no tab row, 22px pace, 16px column heads, 20px rows (one line: the
    /// name's 12px line is 15.6 of them), a 21px limit break, a 22px foot, 10 in at the sides and 6
    /// between columns; a death on one 20px line. The number's column stays wide enough for
    /// "3,912,400" at 12px (64.8).
    /// </summary>
    public static readonly MeterSizes Compact = new(30, 0, 22, 16, 20, 21, 22, 10, 6, 16, 26, 66, 34, 13, 12, 9, 12, 10, 10, 20);

    public static MeterSizes Of(bool compact) => compact ? Compact : Normal;

    public bool IsCompact => Tabs <= 0;

    /// <summary>
    /// How tall a fight's content is for a party of <paramref name="rows"/> (CSS px): the head, the
    /// tabs, pace when it's on, the column heads, a row each, the limit break's row and the foot. The
    /// limit break's and pace's rooms are kept even before they show, so the meter doesn't jump
    /// mid-fight when they come.
    /// </summary>
    public float Needed(int rows, bool pace) => Head + Tabs + (pace ? Pace : 0) + Cols + Math.Max(0, rows) * Row + LimitBreak + Foot;

    /// <summary>How many rows fit in a fight's content this tall (CSS px), the rest as <see cref="Needed"/> has it.</summary>
    public int RowsIn(float content, bool pace) => Row <= 0 ? 0 : Math.Max(0, (int)MathF.Floor((content - Needed(0, pace) + 1) / Row));
}

/// <summary>
/// The meter's bands laid from its head's edge: the top when it grows down, the foot when it grows up.
/// The head first, then what goes next to it (the tabs, pace, the column heads), the rows running away
/// from it, and at the far edge the party's line. Screen pixels.
/// </summary>
public struct MeterStack
{
    private readonly bool up;
    private float near;
    private float far;

    public MeterStack(float top, float bottom, bool up)
    {
        this.up = up;
        near = up ? bottom : top;
        far = up ? top : bottom;
    }

    /// <summary>The head is at the foot (it grows up).</summary>
    public readonly bool Up => up;

    /// <summary>Where the room left starts on the head's side (the first row's edge).</summary>
    public readonly float NearEdge => near;

    /// <summary>Where the room left ends on the far side.</summary>
    public readonly float FarEdge => far;

    /// <summary>The room left between the bands taken.</summary>
    public readonly float Room => up ? near - far : far - near;

    /// <summary>Takes a band next to the last one from the head's side; returns its top.</summary>
    public float Near(float h)
    {
        if (up)
        {
            near -= h;
            return near;
        }
        var y = near;
        near += h;
        return y;
    }

    /// <summary>Takes a band at the far edge (the party's line); returns its top.</summary>
    public float Far(float h)
    {
        if (!up)
        {
            far -= h;
            return far;
        }
        var y = far;
        far += h;
        return y;
    }

    /// <summary>The top of the <paramref name="i"/>th band of this height from the head's side of the room (a row).</summary>
    public readonly float Row(int i, float h) => up ? near - (i + 1) * h : near + i * h;

    /// <summary>How many bands of this height fit in the room left (a pixel of slack, as the rows always had).</summary>
    public readonly int Fits(float h) => h <= 0 ? 0 : Math.Max(0, (int)MathF.Floor((Room + 1) / h));

    /// <summary>The line on a band's side away from the head (the tabs' and pace's hairline): its y.</summary>
    public readonly float Away(float top, float h) => up ? top : top + h - 1;

    /// <summary>The line on a band's side toward the head (the limit break's and the foot's hairline): its y.</summary>
    public readonly float Toward(float top, float h) => up ? top + h - 1 : top;
}

/// <summary>
/// Where the meter goes and how tall it is, worked out away from ImGui so it's tested: the point that
/// stays put (its anchor: the top-left corner growing down, the bottom-left growing up), the window
/// placed from it and kept on the screen, its height in a fight, the menus' side and edge, the tabs' order.
/// </summary>
public static class MeterLayout
{
    /// <summary>The four tabs in the menu's order (compact's dropdown), as the tab row has them.</summary>
    public static readonly MeterTab[] TabOrder = { MeterTab.Damage, MeterTab.Healing, MeterTab.Taken, MeterTab.Deaths };

    private static readonly string[] Words = { "Damage", "Healing", "Taken", "Deaths" };

    /// <summary>A tab's word: Damage, Healing, Taken, Deaths.</summary>
    public static string TabWord(MeterTab tab) => (int)tab is >= 0 and < 4 ? Words[(int)tab] : Words[0];

    /// <summary>The point that stays put for a window at <paramref name="pos"/> this size: its top-left growing down, its bottom-left growing up.</summary>
    public static Vector2 Corner(Vector2 pos, Vector2 size, MeterGrows grows) => grows == MeterGrows.Up ? new Vector2(pos.X, pos.Y + size.Y) : pos;

    /// <summary>
    /// Where the window's top-left goes for its anchor and size: the anchor's edge kept, the rest
    /// growing away from it, the whole window kept on the screen (the game's window), moved in as
    /// little as it takes (its head's edge first when it's taller than the screen; after the game's
    /// resolution shrinks, back onto it). The anchor itself isn't moved, so it goes back when it's
    /// smaller. With Dalamud's multi-monitor windows on (<paramref name="otherScreens"/>) an anchor
    /// off the game's screen is on another monitor, and left as it is.
    /// </summary>
    public static Vector2 Place(Vector2 anchor, Vector2 size, MeterGrows grows, Vector2 screenMin, Vector2 screenMax, bool otherScreens = false)
    {
        var pos = grows == MeterGrows.Up ? new Vector2(anchor.X, anchor.Y - size.Y) : anchor;
        const float slack = 1;
        var off = anchor.X < screenMin.X - slack || anchor.X > screenMax.X + slack || anchor.Y < screenMin.Y - slack || anchor.Y > screenMax.Y + slack;
        if (off && otherScreens) return pos;
        pos.X = Keep(pos.X, size.X, screenMin.X, screenMax.X, false);
        pos.Y = Keep(pos.Y, size.Y, screenMin.Y, screenMax.Y, grows == MeterGrows.Up);
        return pos;
    }

    /// <summary>One axis kept between two edges: moved in as little as it takes; too long for them, its near edge (or far one) on the edge.</summary>
    private static float Keep(float at, float length, float min, float max, bool farFirst)
    {
        if (length >= max - min) return farFirst ? max - length : min;
        return Math.Clamp(at, min, max - length);
    }

    /// <summary>
    /// The window's height in a fight (CSS px): what the party needs (<see cref="MeterSizes.Needed"/>)
    /// plus the title bar's (<paramref name="chrome"/>), at most the height you gave it (0: never sized),
    /// within the window's limits.
    /// </summary>
    public static float FightHeight(float needed, float chrome, float given, float min, float max)
    {
        var h = needed + chrome;
        if (given > 0) h = MathF.Min(h, given);
        return Math.Clamp(h, min, MathF.Max(min, max));
    }

    /// <summary>
    /// Whether a menu opened from the head goes up: away from the edge the meter is anchored at (up
    /// from a head at the foot, down from one on top), the other way when there isn't room for it that
    /// way and there's more the other.
    /// </summary>
    public static bool MenuUp(bool headAtFoot, float roomAbove, float roomBelow, float menuHeight)
    {
        var room = headAtFoot ? roomAbove : roomBelow;
        var other = headAtFoot ? roomBelow : roomAbove;
        return room < menuHeight && other > room ? !headAtFoot : headAtFoot;
    }

    /// <summary>
    /// A menu's left edge: at its button's, but no further right than keeps it <paramref name="inset"/>
    /// inside the meter's right edge, nor further left than that inside its left (the picker, wider
    /// than the room, sits 14px in as the mock-up's); and on the screen.
    /// </summary>
    public static float MenuLeft(float buttonLeft, float meterLeft, float meterRight, float menuWidth, float inset, float screenLeft, float screenRight)
    {
        var x = MathF.Max(MathF.Min(buttonLeft, meterRight - inset - menuWidth), meterLeft + inset);
        return Math.Clamp(x, screenLeft, MathF.Max(screenLeft, screenRight - menuWidth));
    }
}
