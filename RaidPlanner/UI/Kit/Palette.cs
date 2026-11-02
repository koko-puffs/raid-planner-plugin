using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace RaidPlanner.UI;

/// <summary>
/// The Raid Planner's colours, as the site has them (src/styles/base.css and the planner's own):
/// a black ground, near-black cards with hairline edges, white for what goes ahead, grey words,
/// and the planner's orange for what's live and what's picked.
/// </summary>
public static class Palette
{
    // Surfaces.
    public static readonly Vector4 Ground = Hex(0x000000);                 // --ground: the window
    public static readonly Vector4 Footer = Hex(0x0a0a0b);                 // the planner's panels (rgb 10 10 11)
    public static readonly Vector4 Card = Hex(0x0d0d0e);                   // --raised: cards
    public static readonly Vector4 Raised = Hex(0x111113);                 // boxed lists, the question's card
    public static readonly Vector4 Float = Hex(0x0d0d0e);                  // menus, tooltips, toasts
    public static readonly Vector4 Field = Hex(0x141416);                  // fields, tiles
    public static readonly Vector4 Tile = Hex(0x141416);                   // picture tiles and icon wells
    public static readonly Vector4 ButtonFill = Rgba(0, 0, 0, 0);          // an outlined button's inside
    public static readonly Vector4 Picked = Rgba(255, 255, 255, 0.12f);   // the picked one in a pick-one pill

    // Words, brightest to quietest.
    public static readonly Vector4 White = Hex(0xffffff);
    public static readonly Vector4 Ink = Hex(0xf5f5f6);                    // --ink
    public static readonly Vector4 Soft = Hex(0xd4d4d8);                   // words in cards
    public static readonly Vector4 Light = Hex(0xb9b9bf);                  // quiet buttons, links
    public static readonly Vector4 Muted = Hex(0x8e8e95);                  // --muted
    public static readonly Vector4 Faint = Hex(0x5e5e66);                  // what hardly needs reading

    // The planner's orange: live (recording), picked, and the tool's own icons.
    public static readonly Vector4 Ember = Hex(0xff8a3d);
    public static readonly Vector4 EmberHover = Hex(0xffa060);
    public static readonly Vector4 EmberSoft = Hex(0xffd9bf);              // words on its wash (a picked tile's)
    public static readonly Vector4 EmberText = Hex(0xffc49b);              // the planner's orange notes
    public static readonly Vector4 OnEmber = Hex(0x1d130c);
    public static readonly Vector4 EmberWash = Hex(0x1d130c);              // a picked tile's fill
    public static readonly Vector4 EmberTile = Hex(0x29160a);              // the round wells behind its icons (the favicon's)
    public static readonly Vector4 EmberPicked = Rgba(255, 138, 61, 0.16f);
    public static readonly Vector4 EmberEdge = Hex(0x4a2f1b);

    // States.
    public static readonly Vector4 Danger = Hex(0xff8080);                 // .note.bad
    public static readonly Vector4 DangerSolid = Hex(0xff4f4f);
    public static readonly Vector4 DangerWash = Rgba(255, 79, 79, 0.1f);
    public static readonly Vector4 DangerEdge = Rgba(255, 79, 79, 0.45f);
    public static readonly Vector4 Success = Hex(0x3ddc84);                // the planner's safe green (Party live's dot)
    public static readonly Vector4 SuccessWash = Rgba(61, 220, 132, 0.1f);

    // Lines.
    public static readonly Vector4 LineFaint = Rgba(255, 255, 255, 0.06f); // --hover: dividers in cards, hover fills
    public static readonly Vector4 LinePicked = Rgba(255, 255, 255, 0.09f); // --active
    public static readonly Vector4 Line = Hex(0x1f1f22);                   // --line: cards
    public static readonly Vector4 LineStrong = Hex(0x34343a);             // --line-strong: things you press or type in
    public static readonly Vector4 LineHover = Hex(0x4a4a52);              // any edge pointed at
    public static readonly Vector4 LineDashed = Hex(0x34343a);             // things you can add
    public static readonly Vector4 RowHover = Rgba(255, 255, 255, 0.04f);
    public static readonly Vector4 Track = Hex(0x2a2a2f);                  // a slider's or progress bar's track

    // Radii (CSS px, before Dalamud's global scale): the site's five corners.
    public const float RadiusSm = 8;     // icon tiles, swatches, tick boxes
    public const float RadiusMd = 12;    // fields, menu rows, list rows
    public const float RadiusLg = 16;    // cards, tiles, menus
    public const float RadiusXl = 22;    // windows, the question's card
    public const float Radius2Xl = 22;
    public const float RadiusCard = 16;
    public const float RadiusPill = 999; // buttons, pick-one pills, switches, chips

    /// <summary>Everything drawn is multiplied by this (0.5 for a disabled button, 0.4 for a small control).</summary>
    public static float Opacity { get; set; } = 1f;

    public static Vector4 Hex(uint rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static Vector4 Rgba(int r, int g, int b, float a) => new(r / 255f, g / 255f, b / 255f, a);

    /// <summary>A translucent colour flattened over an opaque one.</summary>
    public static Vector4 Over(Vector4 top, Vector4 under) =>
        new(under.X + (top.X - under.X) * top.W, under.Y + (top.Y - under.Y) * top.W, under.Z + (top.Z - under.Z) * top.W, 1f);

    public static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    public static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(new Vector4(c.X, c.Y, c.Z, c.W * Opacity));
}
