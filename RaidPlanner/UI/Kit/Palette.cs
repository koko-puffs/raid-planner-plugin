using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace RaidPlanner.UI;

/// <summary>
/// The Raid Codex's tokens (since 0.9.1 its redesign of 25 November 2026: the site's src/features/raid-codex/styles/ready.css
/// and the redesign's assets/rx.css), each named for its CSS variable. Basalt grounds and hairlines of
/// white; ember is the telegraph (what's live, where you are, the one thing a screen is for);
/// ready green and danger red say how something went; the board's colours (roles, intents) are only
/// for people and mechanics. Every colour the windows draw comes from here.
/// </summary>
public static class Palette
{
    // Ground: basalt, cool and near black.
    public static readonly Vector4 Void = Hex(0x070708);                   // --rp-void
    public static readonly Vector4 Basalt = Hex(0x0c0c0f);                 // --rp-basalt: the footer band, a side column
    public static readonly Vector4 Slab = Hex(0x111115);                   // --rp-slab: panels
    public static readonly Vector4 Raised = Hex(0x18181d);                 // --rp-raised: toasts, a menu row pointed at, a plain tag
    public static readonly Vector4 Well = Hex(0x09090b);                   // --rp-well: fields, segments, steppers
    public static readonly Vector4 Floor = Hex(0x1b1c21);                  // --rp-floor: the board's arena
    /// <summary>The windows' own ground: basalt at 96% (`.dw`), the site's near-solid fallback for its glass (ImGui can't blur).</summary>
    public static readonly Vector4 Window = Rgba(12, 12, 15, 0.96f);
    /// <summary>Dalamud's title bar, coloured (its font and buttons are Dalamud's).</summary>
    public static readonly Vector4 TitleBar = Hex(0x0a0a0c);

    // Lines: white hairlines.
    public static readonly Vector4 Line = Rgba(255, 255, 255, 0.07f);     // --rp-line
    public static readonly Vector4 Line2 = Rgba(255, 255, 255, 0.12f);    // --rp-line-2
    public static readonly Vector4 Line3 = Rgba(255, 255, 255, 0.2f);     // --rp-line-3
    public static readonly Vector4 WindowEdge = Rgba(255, 255, 255, 0.09f);
    public static readonly Vector4 Hover = Rgba(255, 255, 255, 0.025f);   // a row pointed at (`.tr.click:hover`)
    public static readonly Vector4 HoverOpen = Rgba(255, 255, 255, 0.04f); // a row that's open
    public static readonly Vector4 HoverButton = Rgba(255, 255, 255, 0.06f);
    public static readonly Vector4 HoverQuiet = Rgba(255, 255, 255, 0.07f);
    public static readonly Vector4 Picked = Rgba(255, 255, 255, 0.045f);  // a row that's on (`.tr.on`, with a white edge)
    public static readonly Vector4 On = Rgba(255, 255, 255, 0.1f);        // `.btn.is-on`
    public static readonly Vector4 Track = Rgba(255, 255, 255, 0.09f);    // a cast bar's track
    public static readonly Vector4 Tint = Rgba(255, 255, 255, 0.06f);     // a quiet well

    // The controls' own (`.btn`, `.field`, `.seg`): never see-through, the well and its line.
    public static readonly Vector4 CtlLine = Hex(0x303037);                // --ctl-line
    public static readonly Vector4 CtlLineHover = Hex(0x45454f);           // a field pointed at
    public static readonly Vector4 CtlLineFocus = Hex(0x6a6a76);           // a field you type in
    public static readonly Vector4 BtnHover = Hex(0x121216);               // --rp-btn-hover
    public static readonly Vector4 BtnHoverLine = Hex(0x4a4a55);           // --rp-btn-hover-line
    public static readonly Vector4 SwitchOff = Hex(0x2a2a31);              // `.switch`
    public static readonly Vector4 Tip = Hex(0x1f1f25);                    // `.tip`
    public static readonly Vector4 Menu = Hex(0x111116);                   // `.menu`

    // Panels (`.pn`): a ground from #141418 to #101013, an edge lit from its top left, and a frame 4px inside.
    public static readonly Vector4 PanelTop = Hex(0x141418);
    public static readonly Vector4 PanelBottom = Hex(0x101013);
    public static readonly Vector4 PanelEdge = Rgba(255, 255, 255, 0.08f);
    public static readonly Vector4 PanelLit = Rgba(255, 138, 61, 0.5f);    // the edge's light (`--fight` at 50%)
    public static readonly Vector4 Frame = Rgba(233, 211, 168, 0.09f);     // --frame
    public static readonly Vector4 FrameQuiet = Rgba(233, 211, 168, 0.055f);
    /// <summary>What's under a dialog (`.scrim`).</summary>
    public static readonly Vector4 Scrim = Rgba(3, 3, 4, 0.66f);

    // Words.
    public static readonly Vector4 White = Hex(0xf5f5f6);                  // --rp-white: headings, values, what you press
    public static readonly Vector4 Text = Hex(0xd6d6db);                   // --rp-text: what you read
    public static readonly Vector4 Muted = Hex(0xa0a0aa);                  // --rp-muted: lines under things
    public static readonly Vector4 Faint = Hex(0x878792);                  // --rp-faint: labels, only what isn't read (#7c7c87 before 0.9.1)
    public static readonly Vector4 Disabled = Hex(0x4a4a52);
    public static readonly Vector4 OnLight = Hex(0x0b0b0d);                // ink on white; the board's halo
    public static readonly Vector4 Black = Hex(0x000000);

    // Ember: the telegraph.
    public static readonly Vector4 Ember = Hex(0xff8a3d);
    public static readonly Vector4 EmberHi = Hex(0xffb27a);
    public static readonly Vector4 EmberDeep = Hex(0xd9651c);
    public static readonly Vector4 OnEmber = Hex(0x1a0d04);
    public static readonly Vector4 EmberWash = Rgba(255, 138, 61, 0.11f);
    public static readonly Vector4 EmberLine = Rgba(255, 138, 61, 0.42f);
    /// <summary>The primary button (`.btn.primary`): its gradient's top and foot, pointed at, and its edge.</summary>
    public static readonly Vector4 TeleTop = Hex(0xff9a52);
    public static readonly Vector4 TeleFoot = Hex(0xf47d2f);
    public static readonly Vector4 TeleTopHover = Hex(0xffa866);
    public static readonly Vector4 TeleFootHover = Hex(0xf8853a);
    public static readonly Vector4 TeleEdge = Hex(0xffa86b);
    /// <summary>The telegraph pointed at, its hot rim at rest and lit, the glow round it.</summary>
    public static readonly Vector4 TeleHover = Hex(0xff9447);
    public static readonly Vector4 TeleRim = Rgba(255, 214, 178, 0.55f);
    public static readonly Vector4 TeleRimHot = Rgba(255, 232, 210, 0.95f);
    /// <summary>A cast bar's hot end.</summary>
    public static readonly Vector4 CastHot = Hex(0xfff3e6);
    /// <summary>The recording box: ember at 7.5% with an ember edge at 32% (the site's ember box).</summary>
    public static readonly Vector4 RecWash = Rgba(255, 138, 61, 0.075f);
    public static readonly Vector4 RecLine = Rgba(255, 138, 61, 0.32f);

    // Ready, danger, warn.
    public static readonly Vector4 Ready = Hex(0x3ddc84);
    public static readonly Vector4 ReadyWash = Rgba(61, 220, 132, 0.11f);
    public static readonly Vector4 ReadyLine = Rgba(61, 220, 132, 0.35f);
    public static readonly Vector4 OnReady = Hex(0x06200f);                // the tick on a ready tick; a clear tag's words
    public static readonly Vector4 Danger = Hex(0xff5a5a);
    public static readonly Vector4 DangerWash = Rgba(255, 90, 90, 0.11f);
    public static readonly Vector4 DangerLine = Rgba(255, 90, 90, 0.4f);   // an error box's edge
    public static readonly Vector4 DangerEdge = Rgba(255, 90, 90, 0.35f);  // the danger button's and the wipe tag's
    public static readonly Vector4 DangerField = Rgba(255, 90, 90, 0.7f);  // a field that's wrong
    public static readonly Vector4 DangerInk = Hex(0xffd0d0);              // words in an error box
    /// <summary>The destructive answer (`.rp-danger-fill`): red, a lighter edge, dark words.</summary>
    public static readonly Vector4 DangerFillEdge = Hex(0xff7b7b);
    public static readonly Vector4 DangerFillHover = Hex(0xff6c6c);
    public static readonly Vector4 OnDanger = Hex(0x2a0606);
    public static readonly Vector4 DangerSoft = Hex(0xff9a9a);             // a bad token's words; damage in a recap
    public static readonly Vector4 DangerName = Hex(0xffc0c0);             // a death's row
    public static readonly Vector4 Warn = Hex(0xffcf4a);
    public static readonly Vector4 WarnWash = Rgba(255, 207, 74, 0.1f);
    public static readonly Vector4 WarnLine = Rgba(255, 207, 74, 0.35f);
    public static readonly Vector4 WarnInk = Hex(0xffe7a6);

    // The board's colours: roles and what a mechanic asks. Chrome never uses them.
    public static readonly Vector4 Tank = Hex(0x3d6fd9);
    public static readonly Vector4 Healer = Hex(0x3e9b54);
    public static readonly Vector4 Dps = Hex(0xc9453c);
    public static readonly Vector4 AnyRole = Hex(0x6c6c76);
    public static readonly Vector4 Dodge = Hex(0xff8a3d);
    public static readonly Vector4 Share = Hex(0xffcf4a);
    public static readonly Vector4 Apart = Hex(0xb46cff);
    public static readonly Vector4 Tanks = Hex(0xff4f4f);
    public static readonly Vector4 Soak = Hex(0x7aa2ff);
    public static readonly Vector4 Move = Hex(0xffc79c);
    public static readonly Vector4 Look = Hex(0xe8383d);
    public static readonly Vector4 Mark = Hex(0xc084fc);
    public static readonly Vector4 Boss = Hex(0xb44cf0);

    /// <summary>The waymarks' colours (A and 1 red, B and 2 yellow, C and 3 blue, D and 4 purple): the site numbers a raid night with them.</summary>
    public static readonly Vector4[] Waymarks = { Hex(0xe0473c), Hex(0xe9c43f), Hex(0x3f8be9), Hex(0xb45cdd) };

    // Corners (CSS px, before Dalamud's global scale): --r-hair … --r-xl.
    public const float RadiusHair = 2;  // bars
    public const float RadiusTag = 4;   // tags
    public const float RadiusXs = 6;    // 26px buttons, a job's square
    public const float RadiusSm = 8;    // 32px controls, wells, tooltips, menu rows, the board's corners
    public const float RadiusMd = 10;   // 40px controls, fields, boxes
    public const float RadiusLg = 14;   // panels, menus, toasts, windows
    public const float RadiusXl = 20;   // dialogs

    /// <summary>Everything drawn is multiplied by this (a disabled control, a fading toast, a dialog fading in).</summary>
    public static float Opacity { get; set; } = 1f;

    public static Vector4 Hex(uint rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static Vector4 Rgba(int r, int g, int b, float a) => new(r / 255f, g / 255f, b / 255f, a);

    /// <summary>A translucent colour flattened over an opaque one.</summary>
    public static Vector4 Over(Vector4 top, Vector4 under) =>
        new(under.X + (top.X - under.X) * top.W, under.Y + (top.Y - under.Y) * top.W, under.Z + (top.Z - under.Z) * top.W, 1f);

    public static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    /// <summary>A colour mixed towards another (0: a, 1: b), alpha too.</summary>
    public static Vector4 Mix(Vector4 a, Vector4 b, float t) => a + (b - a) * t;

    public static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(new Vector4(c.X, c.Y, c.Z, c.W * Opacity));

    /// <summary>A role's colour (tank, healer, dps; grey for anything else).</summary>
    public static Vector4 RoleColor(string? role) => role switch
    {
        "tank" => Tank,
        "healer" => Healer,
        "dps" => Dps,
        _ => AnyRole,
    };

    /// <summary>A spot's role colour, from its label (MT/OT tanks, H1/H2 healers, M/R/D dps).</summary>
    public static Vector4 SpotColor(string? slot) => RoleColor(Model.Format.RoleOfSlot(slot));
}
