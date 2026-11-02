using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// A mechanic's small round icon, as the planner's timeline shows them: a disc in the mechanic's
/// colour (area attacks orange, stacks yellow, spreads purple, busters red, the planner's own) with
/// its shape cut in dark.
/// </summary>
public static class Glyphs
{
    private static readonly Vector4 Dark = Palette.Hex(0x1a0f08);

    public static Vector4 ColorOf(MechanicKind kind) => kind switch
    {
        MechanicKind.Stack => Palette.Hex(0xffcf4a),
        MechanicKind.Spread => Palette.Hex(0xb46cff),
        MechanicKind.Tankbuster => Palette.Hex(0xff4f4f),
        MechanicKind.Other => Palette.Hex(0x7d7d86),
        _ => Palette.Ember,
    };

    public static string Word(MechanicKind kind) => kind switch
    {
        MechanicKind.Circle => "Circle",
        MechanicKind.Donut => "Donut",
        MechanicKind.Cone => "Cone",
        MechanicKind.Line => "Line",
        MechanicKind.Cross => "Cross",
        MechanicKind.Stack => "Stack",
        MechanicKind.Spread => "Spread",
        MechanicKind.Tankbuster => "Tankbuster",
        MechanicKind.Raidwide => "Raidwide",
        _ => "Other",
    };

    /// <summary>Draws the icon <paramref name="size"/> pixels across (already scaled) with its top-left at <paramref name="pos"/>.</summary>
    public static void Draw(ImDrawListPtr dl, Vector2 pos, float size, MechanicKind kind)
    {
        var r = size / 2;
        var c = pos + new Vector2(r);
        var dark = Palette.U32(Dark);
        dl.AddCircleFilled(c, r, Palette.U32(ColorOf(kind)), 24);
        var k = r / 8f;
        var line = MathF.Max(1f, 1.4f * k);
        switch (kind)
        {
            case MechanicKind.Circle:
                dl.AddCircle(c, 3.6f * k, dark, 16, line);
                break;
            case MechanicKind.Donut:
                dl.AddCircle(c, 4.2f * k, dark, 16, line * 1.6f);
                dl.AddCircleFilled(c, 1.2f * k, dark, 8);
                break;
            case MechanicKind.Cone:
                dl.PathLineTo(c + new Vector2(0, 3.5f * k));
                dl.PathArcTo(c + new Vector2(0, 3.5f * k), 7f * k, -MathF.PI / 2 - 0.62f, -MathF.PI / 2 + 0.62f, 8);
                dl.PathFillConvex(dark);
                break;
            case MechanicKind.Line:
                dl.AddRectFilled(c - new Vector2(1.6f * k, 5.2f * k), c + new Vector2(1.6f * k, 5.2f * k), dark, 0.6f * k);
                break;
            case MechanicKind.Cross:
                dl.AddRectFilled(c - new Vector2(1.3f * k, 5f * k), c + new Vector2(1.3f * k, 5f * k), dark);
                dl.AddRectFilled(c - new Vector2(5f * k, 1.3f * k), c + new Vector2(5f * k, 1.3f * k), dark);
                break;
            case MechanicKind.Stack:
                // Arrows pointing in to the middle.
                for (var i = 0; i < 3; i++)
                {
                    var a = -MathF.PI / 2 + i * MathF.PI * 2 / 3;
                    var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    var side = new Vector2(-d.Y, d.X);
                    var tip = c + d * 1.6f * k;
                    dl.AddTriangleFilled(tip, c + d * 5.2f * k + side * 2.1f * k, c + d * 5.2f * k - side * 2.1f * k, dark);
                }
                break;
            case MechanicKind.Spread:
                dl.AddCircleFilled(c, 1.5f * k, dark, 10);
                dl.AddCircle(c, 4.6f * k, dark, 16, line);
                break;
            case MechanicKind.Tankbuster:
                // A shield.
                dl.PathLineTo(c + new Vector2(-3.8f * k, -4f * k));
                dl.PathLineTo(c + new Vector2(3.8f * k, -4f * k));
                dl.PathLineTo(c + new Vector2(3.4f * k, 1.2f * k));
                dl.PathLineTo(c + new Vector2(0, 4.8f * k));
                dl.PathLineTo(c + new Vector2(-3.4f * k, 1.2f * k));
                dl.PathFillConvex(dark);
                break;
            case MechanicKind.Raidwide:
                dl.AddCircle(c, 5.2f * k, dark, 20, line);
                dl.AddCircle(c, 2.6f * k, dark, 14, line);
                break;
            default:
                dl.AddCircleFilled(c, 1.6f * k, dark, 10);
                break;
        }
    }

    /// <summary>A row of icons, overlapping a little as the planner's do; returns its width.</summary>
    public static float Row(ImDrawListPtr dl, Vector2 pos, float size, System.Collections.Generic.IReadOnlyList<MechanicKind> kinds, int most = 4)
    {
        var n = Math.Min(kinds.Count, most);
        var step = size * 0.8f;
        for (var i = n - 1; i >= 0; i--)
        {
            var p = pos + new Vector2(step * i, 0);
            // A thin ring of the card's black between overlapping discs.
            dl.AddCircleFilled(p + new Vector2(size / 2), size / 2 + S(1.5f), Palette.U32(Palette.Card), 24);
            Draw(dl, p, size, kinds[i]);
        }
        return n == 0 ? 0 : step * (n - 1) + size;
    }
}
