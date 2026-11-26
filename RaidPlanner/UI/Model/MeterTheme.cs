using System;
using System.Collections.Generic;
using System.Numerics;

namespace RaidPlanner.UI.Model;

/// <summary>The compact meter's themes (Settings → Meter → Theme; since 0.7.0). New ones go last: saved themes keep their numbers.</summary>
public enum MeterThemeId
{
    /// <summary>The plugin's own look: basalt, white hairlines, ember for you. The default.</summary>
    ReadyCheck,

    /// <summary>Cute: a cream sticker, pastel pills, a heart on your row, sparkles; Zen Maru Gothic.</summary>
    Mochi,

    /// <summary>The game's own: slate with a gilt rule, a title band, HP-bar shading, every word outlined; the game's fonts.</summary>
    Native,

    /// <summary>Crisp: a terminal, one mono face, a column of lit cells, your row in reverse video, scanlines.</summary>
    Readout,

    /// <summary>Synthwave: indigo, a magenta edge with a glow of alpha rings, a horizon in the head, a glitch for a death.</summary>
    Neon,

    /// <summary>A tome: parchment, a brown frame with a gilt rule and pips, a rubric capital, ink underlines, a ribbon for you; Fraunces.</summary>
    Grimoire,
}

/// <summary>The bars' colours (Settings → Meter → Role colours): the theme's colour-blind-safe set, or the game's blue, green and red.</summary>
public enum MeterRoleColours
{
    Theme,
    Game,
}

/// <summary>A row's bar, one small draw function each.</summary>
public enum BarStyle
{
    /// <summary>The role's colour filling the plate at the bar's alpha, a solid line at its foot (Ready Check).</summary>
    Fill,

    /// <summary>A pastel pill, the role's colour over white (Mochi).</summary>
    Pill,

    /// <summary>The game's HP bar: the role's colour shaded top to bottom, a lit line on top (Native).</summary>
    Hp,

    /// <summary>A column of lit cells beside the number (Readout).</summary>
    Segments,

    /// <summary>Brightening to its end, a hot cap with alpha rings (Neon).</summary>
    Glow,

    /// <summary>An ink underline with a faint wash above it (Grimoire).</summary>
    Underline,
}

/// <summary>How your row is marked.</summary>
public enum YouStyle
{
    /// <summary>An ember notch at its left and an ember outline.</summary>
    Notch,

    /// <summary>A pink outline and a heart at its end.</summary>
    Heart,

    /// <summary>A gold outline.</summary>
    Gold,

    /// <summary>Reverse video: the row filled, its words dark.</summary>
    Invert,

    /// <summary>A white outline with alpha rings.</summary>
    White,

    /// <summary>A ribbon bookmark hanging over its left.</summary>
    Ribbon,
}

/// <summary>How a death is marked (always also the bar losing its colour).</summary>
public enum DeathStyle
{
    Skull,
    Broken,
    Cross,

    /// <summary>"DEAD 2:51" in place of the cells, the name struck through.</summary>
    Tag,

    /// <summary>The name drawn three times, red and cyan a pixel apart (a cross without decorations).</summary>
    Glitch,

    /// <summary>A dagger (†), the name struck through.</summary>
    Dagger,
}

/// <summary>A theme's ornaments, each a few polygons or lines (all off with Decorations off).</summary>
[Flags]
public enum Ornaments
{
    None = 0,

    /// <summary>Mochi's sparkles at the window's corners.</summary>
    Sparkles = 1,

    /// <summary>Native's title band, shaded, with its gilt rule.</summary>
    TitleBand = 2,

    /// <summary>Neon's horizon grid in the head.</summary>
    Horizon = 4,

    /// <summary>Readout's scanlines.</summary>
    Scanlines = 8,

    /// <summary>Grimoire's gilt pips at the frame's corners.</summary>
    Pips = 16,

    /// <summary>Grimoire's red capital at the head's word.</summary>
    Rubric = 32,

    /// <summary>The theme's own "you" or death flourish (Mochi's heart, Grimoire's ribbon, Neon's glitch).</summary>
    Flourish = 64,
}

/// <summary>A theme's face at a size: drawn outlined (the game's 1px black), digit by digit in cells (no tabular figures), in capitals.</summary>
public readonly record struct ThemeFont(Face Face, int Px, bool Outline = false, float Cells = 0, bool Caps = false);

/// <summary>The faces a theme draws in: names, numbers, small numbers and labels, the head's clock or title, labels.</summary>
public sealed record ThemeFonts(ThemeFont Name, ThemeFont Num, ThemeFont Small, ThemeFont Head, ThemeFont Label)
{
    public IEnumerable<ThemeFont> All()
    {
        yield return Name;
        yield return Num;
        yield return Small;
        yield return Head;
        yield return Label;
    }
}

/// <summary>A ring of colour outside the frame (a glow, a shadow): its distance out and colour.</summary>
public readonly record struct Ring(float Px, Vector4 Color);

/// <summary>
/// The window's ground: its colour (or a vertical gradient), its <see cref="Floor"/> (the lowest the
/// Background slider may take it: a plate-less theme stays readable over a bright arena), corners, padding,
/// the head's height and the window's default width; its frame (an edge and its width, an outer line, an
/// inner gilt rule, rings, a sticker shadow).
/// </summary>
public sealed record ThemeWindow(
    Vector4 Bg,
    Vector4? GradTop,
    Vector4? GradBottom,
    float Floor,
    float Radius,
    float Pad,
    Vector4 Edge,
    float EdgeWidth,
    Vector4? Outer,
    Vector4? Inner,
    float InnerInset,
    IReadOnlyList<Ring> Rings,
    Vector2 DropOffset,
    Vector4? Drop,
    float HeadH,
    float HeadGap,
    float FootPad,
    float Width,
    float CollapsedH);

/// <summary>A row: its height, the gap after it, corners, its plate and the plate's alpha, its words' colours, an edge or a rule.</summary>
public sealed record ThemeRow(
    float H,
    float Gap,
    float Radius,
    Vector4 Plate,
    float PlateAlpha,
    Vector4 Text,
    Vector4 Muted,
    Vector4 Faint,
    Vector4 Num,
    Vector4? PlateEdge = null,
    Vector4? Rule = null);

/// <summary>
/// A row's bar: its style and alphas (<see cref="Alpha0"/> the glow's start; <see cref="Wash"/> the underline's), its foot
/// line, the cells; and whether its track ends before the number column too (<see cref="BeforeNumber"/>: a bar whose end is
/// a hard edge, Native's HP bar, Mochi's pill, Neon's bright cap, would cut through the number; as the game's party list
/// has it, the numbers sit on the plate beside the bar).
/// </summary>
public sealed record ThemeBar(BarStyle Style, float Alpha, float Edge, float Alpha0 = 0, float Wash = 0, float Cell = 0, float CellGap = 0, float SegWidth = 0, float SegH = 0,
    bool BeforeNumber = false);

/// <summary>The three roles' colours.</summary>
public sealed record ThemeRoles(Vector4 Tank, Vector4 Healer, Vector4 Dps)
{
    public Vector4 Of(string? role) => role switch
    {
        "tank" => Tank,
        "healer" => Healer,
        _ => Dps,
    };
}

/// <summary>The job icon's socket: its ground and ring (null: the role's colour), round or square; none for a theme that writes a role letter.</summary>
public sealed record ThemeIcon(Vector4? Bg, Vector4? Ring, bool Round, float Size, float RingWidth);

/// <summary>
/// One theme of the compact meter (`Kit/MeterTheme` in the proposal, round four): every value its draw
/// code and the details panel read instead of the kit's palette, so a theme is a table, not code. The
/// values are the mock-ups' (kokos-tools docs/proposals/raid-codex/meter/assets/meter.js); the window's
/// own see-through ground is the Background slider, never under <see cref="ThemeWindow.Floor"/>.
/// </summary>
public sealed record MeterTheme(
    MeterThemeId Id,
    string Name,
    string Kind,
    string Blurb,
    ThemeFonts Font,
    ThemeWindow Win,
    ThemeRow Row,
    ThemeBar Bar,
    ThemeRoles Roles,
    Vector4 You,
    YouStyle YouStyle,
    Vector4 Death,
    DeathStyle DeathStyle,
    Vector4 Accent,
    Vector4 Good,
    Vector4 Bad,
    Vector4 Lb,
    Vector4 ChipLine,
    ThemeIcon? Icon,
    Ornaments Ornaments,
    // What the mock-ups draw per theme beyond the token list: the head's and the rows' small parts.
    Vector4 Clock,
    Vector4 Dot,
    float ChipRadius,
    Vector4? ChipFill,
    Vector4 Divider,
    Vector4 Dashed,
    Vector4 HoverWash,
    Vector4 DeadBar,
    Vector4 DeadIcon,
    Vector4 Link,
    float ValueCol,
    float ShareCol,
    bool Light,
    bool HeadPlates)
{
    /// <summary>Whether the head and foot sit on plates as the rows do (Ready Check and Neon; Native's head is its title band).</summary>
    public bool FootPlate => HeadPlates || Id == MeterThemeId.Native;

    /// <summary>A role's colour on this theme's bars, or the game's (Settings → Role colours).</summary>
    public Vector4 RoleColor(string? role, MeterRoleColours set) => set == MeterRoleColours.Game ? MeterThemes.GameRoles.Of(role) : Roles.Of(role);

    /// <summary>The window's alpha for a Background slider value: never under the floor.</summary>
    public float Alpha(float background) => Math.Clamp(Math.Max(background, Win.Floor), 0.2f, 1f);

    /// <summary>The theme's ornaments drawn: none with Decorations off.</summary>
    public Ornaments Drawn(bool decorations) => decorations ? Ornaments : Ornaments.None;

    /// <summary>Your row's style as drawn: without decorations Mochi's heart and Grimoire's ribbon are a plain outline.</summary>
    public YouStyle YouDrawn(bool decorations) => decorations || YouStyle is not (YouStyle.Heart or YouStyle.Ribbon) ? YouStyle : YouStyle.Gold;

    /// <summary>A death's style as drawn: without decorations Neon's glitch is a cross.</summary>
    public DeathStyle DeathDrawn(bool decorations) => decorations || DeathStyle != DeathStyle.Glitch ? DeathStyle : DeathStyle.Cross;

    /// <summary>The number column's width (CSS px): whole numbers widen it by 10.</summary>
    public float ValueWidth(bool whole) => ValueCol + (whole ? 10 : 0);
}

/// <summary>Colours from hex, as the mock-ups write them.</summary>
public static class Rgb
{
    public static Vector4 Hex(uint rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static Vector4 Rgba(int r, int g, int b, float a) => new(r / 255f, g / 255f, b / 255f, a);

    /// <summary>A colour at another alpha.</summary>
    public static Vector4 A(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    /// <summary>A translucent colour (its own alpha, times <paramref name="alpha"/>) flattened over an opaque one.</summary>
    public static Vector4 Over(Vector4 top, float alpha, Vector4 under)
    {
        var a = top.W * alpha;
        return new Vector4(under.X + (top.X - under.X) * a, under.Y + (top.Y - under.Y) * a, under.Z + (top.Z - under.Z) * a, 1f);
    }

    /// <summary>Mixed toward another colour (0: a, 1: b), alpha too.</summary>
    public static Vector4 Mix(Vector4 a, Vector4 b, float t) => a + (b - a) * t;
}

/// <summary>The six themes (the registry the meter and Settings read), in Settings' order.</summary>
public static class MeterThemes
{
    /// <summary>The game's own role colours (and the plugin's board's): blue, green and red.</summary>
    public static readonly ThemeRoles GameRoles = new(Rgb.Hex(0x3d6fd9), Rgb.Hex(0x3e9b54), Rgb.Hex(0xc9453c));

    private static readonly Vector4 None = Vector4.Zero;

    private static ThemeFont F(Face face, int px, bool outline = false, float cells = 0, bool caps = false) => new(face, px, outline, cells, caps);

    public static readonly MeterTheme ReadyCheck = new(
        MeterThemeId.ReadyCheck, "Ready Check", "Today's look, sharpened", "The plugin's own look: basalt, white hairlines, ember for you. The default.",
        new ThemeFonts(F(Face.SansMedium, 12), F(Face.MonoSemibold, 12), F(Face.Mono, 10), F(Face.MonoSemibold, 13), F(Face.SansMedium, 11)),
        new ThemeWindow(Rgb.Hex(0x0c0c0f), null, null, 0, 8, 4, Rgb.Rgba(255, 255, 255, 0.09f), 1, null, null, 0,
            new[] { new Ring(4, Rgb.Rgba(0, 0, 0, 0.18f)) }, Vector2.Zero, null, 24, 2, 4, 248, 26),
        new ThemeRow(20, 1, 4, Rgb.Hex(0x18181d), 0.92f, Rgb.Hex(0xf5f5f6), Rgb.Hex(0xa0a0aa), Rgb.Hex(0x878792), Rgb.Hex(0xf5f5f6)),
        new ThemeBar(BarStyle.Fill, 0.3f, 2),
        new ThemeRoles(Rgb.Hex(0x7c85de), Rgb.Hex(0x47e19e), Rgb.Hex(0xe8402f)),
        Rgb.Hex(0xff8a3d), YouStyle.Notch, Rgb.Hex(0xff5a5a), DeathStyle.Skull,
        Rgb.Hex(0xff8a3d), Rgb.Hex(0x3ddc84), Rgb.Hex(0xff5a5a), Rgb.Hex(0xff8a3d), Rgb.Rgba(255, 255, 255, 0.12f),
        new ThemeIcon(Rgb.Hex(0x0b0b0d), null, false, 16, 1), Ornaments.None,
        Rgb.Hex(0xf5f5f6), Rgb.Hex(0xff8a3d), 4, null, Rgb.Rgba(255, 255, 255, 0.1f), Rgb.Rgba(255, 255, 255, 0.2f), Rgb.Rgba(255, 255, 255, 0.07f),
        Rgb.Hex(0x8a8a92), Rgb.Hex(0x6d6d74), Rgb.Hex(0xffb27a), 38, 32, false, true);

    public static readonly MeterTheme Mochi = new(
        MeterThemeId.Mochi, "Mochi", "Cute",
        "A pastel sticker: cream, round pills, a heart on your row and two sparkles. Light, so it also reads over a dark arena.",
        // Zen Maru Gothic for the names and labels, a step up from the mock-ups (its small x-height reads small in the game);
        // the numbers and the clock in Geist SemiBold (Zen Maru's digits are wide and loose there). Rows 24 tall for them.
        new ThemeFonts(F(Face.Round, 14), F(Face.SansSemibold, 13, cells: 7.4f), F(Face.SansSemibold, 11, cells: 6.3f), F(Face.SansSemibold, 14, cells: 8f), F(Face.Round, 12)),
        new ThemeWindow(Rgb.Hex(0xfff7f2), null, null, 0.9f, 16, 6, Rgb.Hex(0xffffff), 3, Rgb.Hex(0xf3b9cf), null, 0,
            Array.Empty<Ring>(), new Vector2(0, 4), Rgb.Hex(0xf2a9c6, 0.55f), 30, 2, 6, 268, 32),
        new ThemeRow(24, 3, 12, Rgb.Hex(0xffffff), 1, Rgb.Hex(0x4b2a45), Rgb.Hex(0x8f7189), Rgb.Hex(0xb49aae), Rgb.Hex(0x4b2a45), Rgb.Hex(0xf6e1ea)),
        new ThemeBar(BarStyle.Pill, 0.62f, 0, BeforeNumber: true),
        new ThemeRoles(Rgb.Hex(0xa99cff), Rgb.Hex(0x7fe9cf), Rgb.Hex(0xffad6b)),
        Rgb.Hex(0xff4f9a), YouStyle.Heart, Rgb.Hex(0xd63a72), DeathStyle.Broken,
        Rgb.Hex(0xff6fae), Rgb.Hex(0x1f9c6c), Rgb.Hex(0xd63a72), Rgb.Hex(0xffb21e), Rgb.Hex(0xf6dde6),
        new ThemeIcon(Rgb.Hex(0xffffff), null, true, 16, 2), Ornaments.Sparkles | Ornaments.Flourish,
        Rgb.Hex(0x8a2f5b), Rgb.Hex(0xff4f9a), 8, Rgb.Hex(0xe3f8f0), Rgb.Hex(0xf3d7e2), Rgb.Rgba(90, 50, 70, 0.3f), Rgb.Rgba(80, 40, 60, 0.07f),
        Rgb.Hex(0xd9ccd3), Rgb.Hex(0xb8a9a0), Rgb.Hex(0xff6fae), 38, 32, true, false);

    public static readonly MeterTheme Native = new(
        MeterThemeId.Native, "Native", "Fits the game's own windows",
        "Dark translucent slate with a thin gilt frame, a title band, the job icon in a ringed socket and outlined numbers, as the game's own HUD windows are drawn.",
        // The numbers in the game's Axis: MiedingerMid at these sizes drew no decimal point and a 0 like a C ("24.CK").
        new ThemeFonts(F(Face.GameAxis, 12, outline: true), F(Face.GameAxis, 13, outline: true, cells: 6.6f), F(Face.GameAxis, 10, outline: true, cells: 5.4f),
            F(Face.GameTitle, 13), F(Face.GameAxis, 11)),
        new ThemeWindow(Rgb.Hex(0x1d1d22), Rgb.Hex(0x35353c), Rgb.Hex(0x121215), 0, 5, 5, Rgb.Rgba(0, 0, 0, 0.75f), 1, null, Rgb.Rgba(214, 184, 120, 0.6f), 1,
            Array.Empty<Ring>(), Vector2.Zero, null, 26, 4, 5, 262, 26),
        new ThemeRow(22, 2, 3, Rgb.Hex(0x0b0b0d), 0.62f, Rgb.Hex(0xffffff), Rgb.Hex(0xa9a39a), Rgb.Hex(0x837c72), Rgb.Hex(0xffffff)),
        new ThemeBar(BarStyle.Hp, 0.62f, 0, BeforeNumber: true),
        new ThemeRoles(Rgb.Hex(0x4c6be6), Rgb.Hex(0x3fbf2a), Rgb.Hex(0xd65c64)),
        Rgb.Hex(0xf0d48a), YouStyle.Gold, Rgb.Hex(0xff5b4f), DeathStyle.Cross,
        Rgb.Hex(0xf0d48a), Rgb.Hex(0x7fd36b), Rgb.Hex(0xff6b5f), Rgb.Hex(0x6ec1ff), Rgb.Rgba(214, 184, 120, 0.45f),
        new ThemeIcon(Rgb.Hex(0x121215), Rgb.Hex(0xc9a96a), true, 18, 1), Ornaments.TitleBand,
        Rgb.Hex(0xffffff), Rgb.Hex(0xff6b4f), 4, null, Rgb.Rgba(214, 184, 120, 0.3f), Rgb.Rgba(255, 255, 255, 0.2f), Rgb.Rgba(255, 255, 255, 0.07f),
        Rgb.Hex(0x8a8a92), Rgb.Hex(0x6d6d74), Rgb.Hex(0xe6cf96), 46, 32, false, false);

    public static readonly MeterTheme Readout = new(
        MeterThemeId.Readout, "Readout", "Crisp, a terminal",
        "Mono, no rounding, a segmented bar, a role letter on every row, your row in reverse video. The cheapest to draw and the sharpest at a glance.",
        new ThemeFonts(F(Face.Mono, 11), F(Face.MonoSemibold, 11), F(Face.Mono, 10), F(Face.MonoSemibold, 11), F(Face.Mono, 10)),
        new ThemeWindow(Rgb.Hex(0x050806), null, null, 0.82f, 0, 4, Rgb.Hex(0x2c4a35), 1, null, null, 0,
            Array.Empty<Ring>(), Vector2.Zero, null, 20, 2, 4, 248, 22),
        new ThemeRow(17, 0, 0, Rgb.Hex(0x050806), 0, Rgb.Hex(0xc4f5cd), Rgb.Hex(0x6f9a78), Rgb.Hex(0x4d6e55), Rgb.Hex(0xe9ffe9)),
        new ThemeBar(BarStyle.Segments, 1, 0, Cell: 3, CellGap: 1, SegWidth: 64, SegH: 7),
        new ThemeRoles(Rgb.Hex(0x3cacdd), Rgb.Hex(0x5cff3a), Rgb.Hex(0xf4aa71)),
        Rgb.Hex(0xc4f5cd), YouStyle.Invert, Rgb.Hex(0xff6b6b), DeathStyle.Tag,
        Rgb.Hex(0x8dffa8), Rgb.Hex(0x8dffa8), Rgb.Hex(0xff6b6b), Rgb.Hex(0xffd75f), Rgb.Hex(0x2c4a35),
        null, Ornaments.Scanlines,
        Rgb.Hex(0x8dffa8), Rgb.Hex(0x8dffa8), 0, null, Rgb.Rgba(255, 255, 255, 0.1f), Rgb.Rgba(255, 255, 255, 0.2f), Rgb.Rgba(255, 255, 255, 0.07f),
        Rgb.Hex(0x3a4a3e), Rgb.Hex(0x6d6d74), Rgb.Hex(0x8dffa8), 38, 30, false, false);

    public static readonly MeterTheme Neon = new(
        MeterThemeId.Neon, "Neon", "Synthwave",
        "Night indigo, a magenta frame with a glow of alpha rings, a horizon grid in the head, bars that brighten to their end. Your row outlined in white; a death glitches.",
        new ThemeFonts(F(Face.SansSemibold, 12), F(Face.MonoSemibold, 12), F(Face.Mono, 10), F(Face.Callout, 13, caps: true), F(Face.Callout, 9, caps: true)),
        new ThemeWindow(Rgb.Hex(0x140a28), Rgb.Hex(0x22104a), Rgb.Hex(0x0a0418), 0, 10, 5, Rgb.Hex(0xff4fd8), 1, null, null, 0,
            new[] { new Ring(2, Rgb.Rgba(255, 79, 216, 0.32f)), new Ring(5, Rgb.Rgba(255, 79, 216, 0.12f)) }, Vector2.Zero, null, 28, 2, 5, 256, 26),
        new ThemeRow(20, 3, 3, Rgb.Hex(0x100622), 0.88f, Rgb.Hex(0xffffff), Rgb.Hex(0xb6a6d8), Rgb.Hex(0x8170a8), Rgb.Hex(0xffffff)),
        new ThemeBar(BarStyle.Glow, 0.45f, 1, Alpha0: 0.1f, BeforeNumber: true),
        new ThemeRoles(Rgb.Hex(0x3decff), Rgb.Hex(0xc5f04c), Rgb.Hex(0xff3d91)),
        Rgb.Hex(0xffffff), YouStyle.White, Rgb.Hex(0xff3b5c), DeathStyle.Glitch,
        Rgb.Hex(0xff4fd8), Rgb.Hex(0xc5f04c), Rgb.Hex(0xff3b5c), Rgb.Hex(0xffe14d), Rgb.Rgba(61, 236, 255, 0.55f),
        new ThemeIcon(Rgb.Hex(0x0a0418), null, false, 16, 1), Ornaments.Horizon | Ornaments.Flourish,
        Rgb.Hex(0xffffff), Rgb.Hex(0xff4fd8), 4, null, Rgb.Rgba(255, 255, 255, 0.1f), Rgb.Rgba(255, 255, 255, 0.2f), Rgb.Rgba(255, 255, 255, 0.07f),
        Rgb.Hex(0x8a8a92), Rgb.Hex(0x6d6d74), Rgb.Hex(0x3decff), 38, 32, false, true);

    public static readonly MeterTheme Grimoire = new(
        MeterThemeId.Grimoire, "Grimoire", "A parchment tome",
        "A page of a ledger: parchment, a brown frame with a gilt rule and corner pips, a red rubric capital, ink underlines for bars, a ribbon bookmark on your row and a dagger for the dead.",
        new ThemeFonts(F(Face.Serif, 13), F(Face.SerifSemibold, 13, cells: 7.4f), F(Face.Serif, 11, cells: 6), F(Face.SerifSemibold, 15), F(Face.Serif, 11)),
        new ThemeWindow(Rgb.Hex(0xf1e2c0), Rgb.Hex(0xf6ead0), Rgb.Hex(0xe6d1a5), 0.92f, 3, 8, Rgb.Hex(0x4a3020), 2, null, Rgb.Hex(0xb08d4f), 4,
            Array.Empty<Ring>(), Vector2.Zero, null, 32, 5, 8, 268, 34),
        new ThemeRow(21, 0, 0, Rgb.Hex(0xf1e2c0), 0, Rgb.Hex(0x2a1a0c), Rgb.Hex(0x6e5236), Rgb.Hex(0x94775a), Rgb.Hex(0x2a1a0c), null, Rgb.Rgba(90, 60, 30, 0.22f)),
        new ThemeBar(BarStyle.Underline, 1, 3, Wash: 0.13f),
        new ThemeRoles(Rgb.Hex(0x1e2f8c), Rgb.Hex(0x2aa198), Rgb.Hex(0xc7501a)),
        Rgb.Hex(0x9b1c1c), YouStyle.Ribbon, Rgb.Hex(0x9b1c1c), DeathStyle.Dagger,
        Rgb.Hex(0x9b1c1c), Rgb.Hex(0x2f6b3a), Rgb.Hex(0x9b1c1c), Rgb.Hex(0xa8822e), Rgb.Rgba(90, 60, 30, 0.35f),
        new ThemeIcon(null, null, true, 15, 1.3f), Ornaments.Pips | Ornaments.Rubric | Ornaments.Flourish,
        Rgb.Hex(0x3b2412), Rgb.Hex(0x9b1c1c), 0, null, Rgb.Rgba(90, 60, 30, 0.35f), Rgb.Rgba(90, 50, 70, 0.3f), Rgb.Rgba(80, 40, 60, 0.07f),
        Rgb.Hex(0x8a7a68), Rgb.Hex(0xb8a9a0), Rgb.Hex(0x9b1c1c), 42, 32, true, false);

    /// <summary>The six, in Settings' order (Ready Check first, the default).</summary>
    public static readonly IReadOnlyList<MeterTheme> All = new[] { ReadyCheck, Mochi, Native, Readout, Neon, Grimoire };

    /// <summary>A theme by its id (one this version doesn't have, a newer version's, is Ready Check).</summary>
    public static MeterTheme Of(MeterThemeId id) => (int)id >= 0 && (int)id < All.Count ? All[(int)id] : ReadyCheck;

    /// <summary>The faces (and sizes) a theme draws in: what's loaded while it's picked.</summary>
    public static IEnumerable<(Face Face, int Px)> FacesOf(MeterTheme t)
    {
        foreach (var f in t.Font.All()) yield return (f.Face, f.Px);
        // A name a weight heavier for your row; the LB's number a size down; the head's title a size up (the rubric capital).
        yield return (Heavier(t.Font.Name.Face), t.Font.Name.Px);
        yield return (t.Font.Num.Face, t.Font.Num.Px - 1);
        if (t.Id == MeterThemeId.Grimoire) yield return (Face.SerifSemibold, 19);
    }

    /// <summary>A face a weight heavier (your row's name).</summary>
    public static Face Heavier(Face f) => f switch
    {
        Face.Sans => Face.SansMedium,
        Face.SansMedium => Face.SansSemibold,
        Face.Mono => Face.MonoSemibold,
        Face.Round => Face.RoundBlack,
        Face.Serif => Face.SerifSemibold,
        _ => f,
    };
}

/// <summary>
/// Colour arithmetic for the themes' checks (the mock-ups' pages work them out from the same tokens):
/// WCAG contrast, CIE Lab ΔE (1976), and colour-blindness simulated with Machado, Oliveira and Fernandes'
/// 2009 matrices at full severity (in linear RGB).
/// </summary>
public static class ColorMath
{
    private static float Lin(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float Unlin(float c)
    {
        c = Math.Clamp(c, 0, 1);
        return c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;
    }

    /// <summary>Relative luminance.</summary>
    public static float Luminance(Vector4 c) => 0.2126f * Lin(c.X) + 0.7152f * Lin(c.Y) + 0.0722f * Lin(c.Z);

    /// <summary>WCAG contrast of two opaque colours (1 to 21).</summary>
    public static float Contrast(Vector4 a, Vector4 b)
    {
        var x = Luminance(a);
        var y = Luminance(b);
        return (MathF.Max(x, y) + 0.05f) / (MathF.Min(x, y) + 0.05f);
    }

    private static readonly float[][] Protan = { new[] { 0.152286f, 1.052583f, -0.204868f }, new[] { 0.114503f, 0.786281f, 0.099216f }, new[] { -0.003882f, -0.048116f, 1.051998f } };
    private static readonly float[][] Deutan = { new[] { 0.367322f, 0.860646f, -0.227968f }, new[] { 0.280085f, 0.672501f, 0.047413f }, new[] { -0.011820f, 0.042940f, 0.968881f } };
    private static readonly float[][] Tritan = { new[] { 1.255528f, -0.076749f, -0.178779f }, new[] { -0.078411f, 0.930809f, 0.147602f }, new[] { 0.004733f, 0.691367f, 0.303900f } };

    /// <summary>A colour as a protan (0), deutan (1) or tritan (2) sees it.</summary>
    public static Vector4 Simulate(Vector4 c, int kind)
    {
        var m = kind switch
        {
            0 => Protan,
            1 => Deutan,
            _ => Tritan,
        };
        var l = new[] { Lin(c.X), Lin(c.Y), Lin(c.Z) };
        float Row(int i) => Unlin(m[i][0] * l[0] + m[i][1] * l[1] + m[i][2] * l[2]);
        return new Vector4(Row(0), Row(1), Row(2), 1);
    }

    private static (float L, float A, float B) Lab(Vector4 c)
    {
        var r = Lin(c.X);
        var g = Lin(c.Y);
        var b = Lin(c.Z);
        var x = (0.4124f * r + 0.3576f * g + 0.1805f * b) / 0.95047f;
        var y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        var z = (0.0193f * r + 0.1192f * g + 0.9505f * b) / 1.08883f;
        static float F(float t) => t > 0.008856f ? MathF.Cbrt(t) : 7.787f * t + 16f / 116f;
        var fx = F(x);
        var fy = F(y);
        var fz = F(z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>CIE76 ΔE between two colours.</summary>
    public static float DeltaE(Vector4 a, Vector4 b)
    {
        var x = Lab(a);
        var y = Lab(b);
        return MathF.Sqrt((x.L - y.L) * (x.L - y.L) + (x.A - y.A) * (x.A - y.A) + (x.B - y.B) * (x.B - y.B));
    }

    /// <summary>The smallest ΔE between the three roles' colours, as a colour-blind kind sees them (−1: normal sight).</summary>
    public static float RolesApart(ThemeRoles r, int kind = -1)
    {
        Vector4 S(Vector4 c) => kind < 0 ? c : Simulate(c, kind);
        var t = S(r.Tank);
        var h = S(r.Healer);
        var d = S(r.Dps);
        return MathF.Min(DeltaE(t, h), MathF.Min(DeltaE(t, d), DeltaE(h, d)));
    }
}
