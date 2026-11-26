using System;
using System.Numerics;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.UI.Model;

/// <summary>
/// Which way the meter grows when its height changes by itself (Settings → Meter, Grows): the edge that
/// stays put. Since 0.7.4 it's only that: where the head sits is <see cref="MeterHead"/>, the rows' order <see cref="MeterOrder"/>.
/// </summary>
public enum MeterGrows
{
    /// <summary>Its top edge stays where it is; it grows downwards.</summary>
    Down,

    /// <summary>Its bottom edge stays where it is; it grows upwards (a meter sitting on your hotbars).</summary>
    Up,
}

/// <summary>
/// Where the meter's head sits (Settings → Meter, Head; since 0.7.4): the normal meter's clock, tabs, pull
/// menu and lock (pace and the column heads beside them), compact's head band. The foot (the limit break's
/// row and the party's line) sits at the other edge.
/// </summary>
public enum MeterHead
{
    Top,
    Bottom,
}

/// <summary>
/// The rows' order reading down the window (Settings → Meter, Order; since 0.7.4): the top player at the top
/// of the list, or at its bottom. Deaths and events follow it (the first death and the newest event where the top player is).
/// </summary>
public enum MeterOrder
{
    HighestFirst,
    LowestFirst,
}

/// <summary>
/// How the meter is laid out (since 0.7.4; until then one Grows setting flipped all three): the edge that
/// stays put, the edge the head sits at, and the rows' order reading down. Worked out away from ImGui so it's
/// tested: where the head goes, and where each row goes counting from the head's side.
/// </summary>
public readonly record struct MeterArrange(MeterGrows Grows, MeterHead Head, MeterOrder Order)
{
    /// <summary>As a new meter is: growing down, the head on top, highest first.</summary>
    public static readonly MeterArrange Default = new(MeterGrows.Down, MeterHead.Top, MeterOrder.HighestFirst);

    /// <summary>The settings' version that has the three apart (<see cref="Migrate"/>): 0 before 0.7.4.</summary>
    public const int Current = 1;

    /// <summary>The head at the bottom edge (the foot at the top).</summary>
    public bool HeadAtBottom => Head == MeterHead.Bottom;

    /// <summary>The top player nearest the head (highest first under a head on top, lowest first over one at the bottom).</summary>
    public bool TopNearHead => (Head == MeterHead.Top) == (Order == MeterOrder.HighestFirst);

    /// <summary>
    /// Where the <paramref name="k"/>th of <paramref name="n"/> rows shown (0 the top player, n − 1 the last,
    /// compact's pinned you) sits, counting from the head's side (0 next to the head).
    /// </summary>
    public int FromHead(int k, int n) => TopNearHead ? k : n - 1 - k;

    /// <summary>Which row (0 the top player) is <paramref name="d"/>th reading down the window, of <paramref name="n"/> shown.</summary>
    public int ReadingDown(int d, int n) => FromHead(HeadAtBottom ? n - 1 - d : d, n);

    /// <summary>
    /// Compact's dashed rule before you pinned (the last of <paramref name="n"/> rows, furthest from the top player):
    /// the place from the head's side it sits before; the rows from there on are moved past it. The last place when
    /// the top player is nearest the head, else the second (you are first from the head then).
    /// </summary>
    public int PinBoundary(int n) => TopNearHead || n <= 1 ? Math.Max(0, n - 1) : 1;

    /// <summary>
    /// The three as an older version's one Grows has them (before 0.7.4): down, the head on top and the
    /// top player first; up, the head at the foot and the rows reading up from it (so lowest first, reading down).
    /// </summary>
    public static MeterArrange Of(MeterGrows grows) =>
        grows == MeterGrows.Up ? new MeterArrange(MeterGrows.Up, MeterHead.Bottom, MeterOrder.LowestFirst) : Default;

    /// <summary>
    /// Brings an older configuration's layout to this version's once: before 0.7.4 (<paramref name="version"/> 0)
    /// its Grows set the head and the order too (<see cref="Of"/>), so it looks exactly as it did. After that they're the person's.
    /// </summary>
    public static (int Version, MeterHead Head, MeterOrder Order) Migrate(int version, MeterGrows grows, MeterHead head, MeterOrder order)
    {
        if (version >= Current) return (version, head, order);
        var was = Of(grows);
        return (Current, was.Head, was.Order);
    }
}

/// <summary>
/// The normal meter's sizes (CSS px and font sizes). Until 0.7.0 there was a compact set too (20px
/// rows, the tabs in a menu); 0.7.0's compact meter is round four's Plate, sized by its theme
/// (<see cref="PlateSizes"/>).
/// </summary>
public sealed record MeterSizes(
    float Head,
    float Tabs,
    float Pace,
    float UnderTabs,
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
    int FootPx)
{
    /// <summary>
    /// The mock-up's meter: a 36px head, 27px tabs, 26px pace, 6px under them (since 0.8.2; it was the columns' heads,
    /// ENCDPS and %, 20 tall), 25px rows, a 27px limit break, a 28px foot.
    /// </summary>
    public static readonly MeterSizes Normal = new(36, 27, 26, 6, 25, 27, 28, 12, 8, 18, 30, 66, 40, 15, 13, 10, 13, 11, 11);

    /// <summary>
    /// How tall a fight's content is for <paramref name="rows"/> rows (CSS px): the head, the tabs, pace's
    /// row when it shows, the space under them, a row each, the limit break's row when it shows, and the foot.
    /// Since 0.7.4 the meter is fitted to what it shows (<see cref="MeterFit"/>): no room is kept for pace or
    /// the limit break before they come (it grows by their rows then), so there's no empty band in it.
    /// </summary>
    public float Needed(int rows, bool pace, bool limitBreak = true) => Head + Tabs + (pace ? Pace : 0) + UnderTabs + Math.Max(0, rows) * Row + (limitBreak ? LimitBreak : 0) + Foot;

    /// <summary>
    /// The normal meter's sizes in a theme (since 0.7.3 it draws in the compact meter's theme): Ready Check's are
    /// <see cref="Normal"/>; a theme's rows are as tall as its plates need for its bigger faces, 5 more than its
    /// compact rows, never under 25 (Mochi 29, Native 27, Grimoire 26).
    /// </summary>
    public static MeterSizes For(MeterTheme t) => t.Id == MeterThemeId.ReadyCheck ? Normal : Normal with { Row = MathF.Max(Normal.Row, t.Row.H + 5) };

    /// <summary>How many rows fit in a fight's content this tall (CSS px), the rest as <see cref="Needed"/> has it.</summary>
    public int RowsIn(float content, bool pace, bool limitBreak = true) => Row <= 0 ? 0 : Math.Max(0, (int)MathF.Floor((content - Needed(0, pace, limitBreak) + 1) / Row));
}

/// <summary>
/// The normal meter's height fitted to what it shows (since 0.7.4; CSS px, the window's content without its
/// title bar): no empty band at the far edge from the head, whichever way it grows. A fight's rows, pace's row
/// and the limit break's only when they show; waiting, the party with dashes; Deaths and Events, their lines
/// (at most the room the party's rows take, so a long list doesn't make it taller than the fight). The height
/// you gave it caps it (<see cref="MeterLayout.FightHeight"/>): the most it grows to.
/// </summary>
public static class MeterFit
{
    /// <summary>An empty list's line ("Nobody died", "Deaths, raises… show here"): 14 above, its 19.5 line, 14 under.</summary>
    public const float Message = 14 + 19.5f + 14;

    /// <summary>The Deaths tab's lines under a live fight's deaths ("7 alive" and its hint), in place of the foot.</summary>
    public const float Alive = 14 + 19.5f + 7.2f + 16 + 6;

    /// <summary>A fight on Damage, Healing or Taken: the head, the tabs, pace when it shows, the space under them, the rows, the limit break when it shows, the foot.</summary>
    public static float Fight(MeterSizes z, int rows, bool pace, bool limitBreak) => z.Needed(rows, pace, limitBreak);

    /// <summary>Waiting for the first hit: the head, the tabs, the space under them, the party with dashes, the foot.</summary>
    public static float Waiting(MeterSizes z, int rows) => z.Needed(rows, false, false);

    /// <summary>The Events tab's row (23 tall, or the theme's row less 2).</summary>
    public static float EventRow(MeterSizes z) => MathF.Max(23, z.Row - 2);

    /// <summary>
    /// The Deaths tab: the head, the tabs, a <paramref name="rowH"/> row a death, then (live) the alive line in the
    /// foot's place, else the foot and, with none, its line; at most the party's fight (<see cref="ListCap"/>).
    /// </summary>
    public static float Deaths(MeterSizes z, int deaths, float rowH, bool live, int party)
    {
        var n = Math.Max(0, deaths);
        var own = z.Head + z.Tabs + n * rowH + (live ? Alive : z.Foot + (n == 0 ? Message : 0));
        return MathF.Min(own, MathF.Max(ListCap(z, party), z.Head + z.Tabs + (live ? Alive : z.Foot + Message)));
    }

    /// <summary>The Events tab: the head, the tabs, a row an event (the newest kept), the foot, and with none its line; at most the party's fight.</summary>
    public static float Events(MeterSizes z, int events, int party)
    {
        var n = Math.Max(0, events);
        var own = z.Head + z.Tabs + n * EventRow(z) + z.Foot + (n == 0 ? Message : 0);
        return MathF.Min(own, MathF.Max(ListCap(z, party), z.Head + z.Tabs + z.Foot + Message));
    }

    /// <summary>The most a list (Deaths, Events) takes: the party's rows on Damage with the limit break's, as the meter is in the fight.</summary>
    public static float ListCap(MeterSizes z, int party) => z.Needed(party, false, true);
}

/// <summary>
/// The meter's bands laid from its head's edge: the top, or the bottom with its head there (up;
/// since 0.7.4 <see cref="MeterHead"/>, apart from which way it grows). The head first, then what goes next to it
/// (the tabs, pace, the space under them), the rows running away from it (each row's place from the head is
/// <see cref="MeterArrange.FromHead"/>'s), and at the far edge the party's line. Screen pixels.
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

    /// <summary>The head is at the bottom edge.</summary>
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
    /// <summary>The five tabs in the menu's order (compact's dropdown), as the tab row has them (Events since 0.4.0).</summary>
    public static readonly MeterTab[] TabOrder = { MeterTab.Damage, MeterTab.Healing, MeterTab.Taken, MeterTab.Deaths, MeterTab.Events };

    private static readonly string[] Words = { "Damage", "Healing", "Taken", "Deaths", "Events" };

    /// <summary>A tab's word: Damage, Healing, Taken, Deaths, Events (an unknown one, saved by a newer version, is Damage).</summary>
    public static string TabWord(MeterTab tab) => (int)tab >= 0 && (int)tab < Words.Length ? Words[(int)tab] : Words[0];

    /// <summary>A tab as saved: one this version doesn't have (a newer version's) is Damage.</summary>
    public static MeterTab Known(MeterTab tab) => (int)tab >= 0 && (int)tab < Words.Length ? tab : MeterTab.Damage;

    /// <summary>
    /// The point that stays put for a window at <paramref name="pos"/> this size: its content's top-left growing down (under
    /// its <paramref name="chrome"/>, the title bar unlocked or the arranging head, so locking it or Arrange windows' Done
    /// doesn't move what it shows), its bottom-left growing up (the chrome is at its top, away from it).
    /// </summary>
    public static Vector2 Corner(Vector2 pos, Vector2 size, MeterGrows grows, float chrome = 0) =>
        grows == MeterGrows.Up ? new Vector2(pos.X, pos.Y + size.Y) : new Vector2(pos.X, pos.Y + chrome);

    /// <summary>
    /// Where the window's top-left goes for its anchor and size: the anchor's edge kept, the rest
    /// growing away from it, the whole window kept on the screen (the game's window), moved in as
    /// little as it takes (its head's edge first when it's taller than the screen; after the game's
    /// resolution shrinks, back onto it). The anchor itself isn't moved, so it goes back when it's
    /// smaller. With Dalamud's multi-monitor windows on (<paramref name="otherScreens"/>) an anchor
    /// off the game's screen is on another monitor, and left as it is. Growing down the window's top is
    /// <paramref name="chrome"/> over the anchor (<see cref="Corner"/>: the anchor is its content's).
    /// </summary>
    public static Vector2 Place(Vector2 anchor, Vector2 size, MeterGrows grows, Vector2 screenMin, Vector2 screenMax, bool otherScreens = false, float chrome = 0)
    {
        var pos = grows == MeterGrows.Up ? new Vector2(anchor.X, anchor.Y - size.Y) : new Vector2(anchor.X, anchor.Y - chrome);
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
    /// The window's height in a fight while the mouse is on it (since 0.7.4; CSS px): the height it had as the mouse
    /// came (<paramref name="holding"/>; 0 none yet) is kept, so a tab switch or a row coming or going doesn't move its
    /// head or tabs from under the mouse (with the head at the edge that isn't anchored, they'd move). Rows that don't
    /// fit it are cut, as under a height you gave it; fewer leave room at the far side. With the mouse off it, or
    /// nothing held yet, it's the fitted height (<paramref name="fitted"/>). Sizing it by its edge isn't this: your drag wins.
    /// </summary>
    public static float Hold(float fitted, float holding, bool pointed) => pointed && holding > 0 && float.IsFinite(holding) ? holding : fitted;

    /// <summary>
    /// Whether the meter's window can be resized by its edges: only the normal meter, unlocked. Compact
    /// (0.7.0) never can, locked or not: its width is its theme's and its height its rows', so a size
    /// given by hand would only snap back.
    /// </summary>
    public static bool Resizable(bool compact, bool locked) => !compact && !locked;

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
