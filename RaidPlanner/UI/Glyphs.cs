using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;

namespace RaidPlanner.UI;

/// <summary>
/// A mechanic drawn small, as the board draws it (the site's MechanicGlyph, the mock-ups'
/// <c>RP.glyph</c>): in a 10.4-unit box clipped square, a circle telegraph, a donut, a cone from
/// below, a line, a cross, a stack with its chevrons in, a spread with its triangles out, a
/// tankbuster with its shield. They sit in square wells (round is for people). A raidwide is the
/// arena's rings rolling out; anything else, a quiet ring.
/// </summary>
public static class Glyphs
{
    /// <summary>What a mechanic asks, for the word said before its name; none for a raidwide or anything else.</summary>
    public static Intent? IntentOf(MechanicKind kind) => kind switch
    {
        MechanicKind.Circle or MechanicKind.Donut or MechanicKind.Cone or MechanicKind.Line or MechanicKind.Cross => Intent.Dodge,
        MechanicKind.Stack => Intent.Share,
        MechanicKind.Spread => Intent.Apart,
        MechanicKind.Tankbuster => Intent.Tanks,
        _ => null,
    };

    /// <summary>The shape's name, as the site's Kit names it (the row's tooltip).</summary>
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

    /// <summary>Draws the mechanic in a square <paramref name="size"/> pixels across (already scaled) from <paramref name="min"/>, clipped to it.</summary>
    public static void Draw(ImDrawListPtr dl, Vector2 min, float size, MechanicKind kind)
    {
        var u = size / 10.4f;
        var c = min + new Vector2(size / 2);
        var rim = MathF.Max(1, 0.17f * u);
        dl.PushClipRect(min, min + new Vector2(size), true);
        try
        {
            switch (kind)
            {
                case MechanicKind.Circle:
                    BoardArt.TeleCircle(dl, c, 3.6f * u, Palette.Dodge, rim);
                    break;
                case MechanicKind.Donut:
                    BoardArt.TeleDonut(dl, c, 4.3f * u, 2f * u, Palette.Dodge, rim);
                    break;
                case MechanicKind.Cone:
                    BoardArt.TeleCone(dl, c + new Vector2(0, 3.2f * u), 7.4f * u, 70, 0, Palette.Dodge, rim);
                    break;
                case MechanicKind.Line:
                    BoardArt.TeleRect(dl, c + new Vector2(0, 4.4f * u), -1.6f * u, -8.8f * u, 1.6f * u, 0, 0, Palette.Dodge, rim);
                    break;
                case MechanicKind.Cross:
                    BoardArt.TeleCross(dl, c, 2.4f * u, 4.6f * u, 0, Palette.Dodge, rim);
                    break;
                case MechanicKind.Stack:
                    BoardArt.Stack(dl, c, 3f * u, Palette.Share, u, rim);
                    break;
                case MechanicKind.Spread:
                    BoardArt.Spread(dl, c, 3f * u, Palette.Apart, u, rim);
                    break;
                case MechanicKind.Tankbuster:
                    BoardArt.Buster(dl, c, 3.4f * u, Palette.Tanks, u, rim);
                    break;
                case MechanicKind.Raidwide:
                    BoardArt.Proximity(dl, c, 4.6f * u, u);
                    break;
                default:
                    dl.AddCircle(c, 2.4f * u, Palette.U32(Palette.WithAlpha(Palette.Muted, 0.8f)), 24, rim);
                    dl.AddCircleFilled(c, MathF.Max(1, 0.5f * u), Palette.U32(Palette.Muted), 10);
                    break;
            }
        }
        finally
        {
            dl.PopClipRect();
        }
    }
}
