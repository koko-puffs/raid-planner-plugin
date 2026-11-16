using System;
using System.Collections.Generic;

namespace RaidPlanner.UI.Model;

/// <summary>
/// The opened-up meter's faces in a theme (since 0.7.2 it follows the compact meter's theme): each part
/// of the window at its size there, in the theme's face for that kind of word. Ready Check's are the
/// window as it was before themes (Geist and Geist Mono); another theme's are its own faces (its name's
/// for words, its number's for numbers, its small one's for the small numbers and the heads, in
/// capitals), each a step up or down from Ready Check's as the theme's compact faces are from Ready
/// Check's there (Mochi's words two steps up: its face reads small).
/// </summary>
public sealed record DetailFonts(
    ThemeFont Name,
    ThemeFont Title,
    ThemeFont Body,
    ThemeFont BodyStrong,
    ThemeFont Note,
    ThemeFont Num,
    ThemeFont NumStrong,
    ThemeFont Small,
    ThemeFont SmallStrong,
    ThemeFont Tiny,
    ThemeFont Head,
    ThemeFont HeadSmall,
    ThemeFont Clock)
{
    private static ThemeFont F(Face face, int px, bool caps = false) => new(face, px, Caps: caps);

    /// <summary>The window as it was: names Geist 500 14, titles 600 15, words 400 13, numbers Geist Mono 13 (600 the main one), heads mono capitals 10 and 9, the clock 600 18.</summary>
    public static readonly DetailFonts ReadyCheck = new(
        F(Face.SansMedium, 14), F(Face.SansSemibold, 15), F(Face.Sans, 13), F(Face.SansMedium, 13), F(Face.Sans, 12),
        F(Face.Mono, 13), F(Face.MonoSemibold, 13), F(Face.Mono, 12), F(Face.MonoSemibold, 12), F(Face.Mono, 11),
        F(Face.Mono, 10, true), F(Face.Mono, 9, true), F(Face.MonoSemibold, 18));

    /// <summary>A theme's: Ready Check's sizes moved by the theme's own steps from Ready Check's compact faces, in its faces.</summary>
    public static DetailFonts For(MeterTheme t)
    {
        if (t.Id == MeterThemeId.ReadyCheck) return ReadyCheck;
        var rc = MeterThemes.ReadyCheck.Font;
        var f = t.Font;
        var w = f.Name.Px - rc.Name.Px;
        var n = f.Num.Px - rc.Num.Px;
        var s = f.Small.Px - rc.Small.Px;
        ThemeFont Words(int px, bool heavy = false) => f.Name with { Face = heavy ? MeterThemes.Heavier(f.Name.Face) : f.Name.Face, Px = px + w };
        ThemeFont Numbers(ThemeFont of, int px, bool heavy = false) => of with { Face = heavy ? MeterThemes.Heavier(of.Face) : of.Face, Px = px };
        var clockOf = t.Id is MeterThemeId.Native or MeterThemeId.Grimoire ? f.Num : f.Head;
        return new DetailFonts(
            Words(14), Words(15, true), Words(13), Words(13, true), Words(12),
            Numbers(f.Num, 13 + n), Numbers(f.Num, 13 + n, true), Numbers(f.Small, 12 + s), Numbers(f.Small, 12 + s, true), Numbers(f.Small, 11 + s),
            f.Small with { Px = 10 + s, Caps = true, Cells = 0 }, f.Small with { Px = 9 + s, Caps = true, Cells = 0 },
            clockOf with { Px = 18 + clockOf.Px - 13 });
    }

    public IEnumerable<ThemeFont> All()
    {
        yield return Name;
        yield return Title;
        yield return Body;
        yield return BodyStrong;
        yield return Note;
        yield return Num;
        yield return NumStrong;
        yield return Small;
        yield return SmallStrong;
        yield return Tiny;
        yield return Head;
        yield return HeadSmall;
        yield return Clock;
    }

    /// <summary>The faces and sizes to load while the theme is picked (with the compact meter's: <see cref="MeterThemes.FacesOf"/>).</summary>
    public IEnumerable<(Face Face, int Px)> Faces()
    {
        foreach (var f in All()) yield return (f.Face, f.Px);
    }
}

/// <summary>
/// A table's columns as drawn (screen px): each at least its CSS width at the scale, and at least as
/// wide as its head and every cell it shows, measured in the face each is drawn in (so "1,337,000" in a
/// face wider than the mock-up's never runs into the column before it).
/// </summary>
public static class DetailColumns
{
    public static void Widths(float[] into, IReadOnlyList<float> css, float scale, IReadOnlyList<string> heads, ThemeFont head, IReadOnlyList<ThemeFont> cellFonts,
        IEnumerable<IReadOnlyList<string>> rows, Func<string, ThemeFont, float> measure)
    {
        for (var c = 0; c < into.Length; c++) into[c] = MathF.Max(css[c] * scale, measure(heads[c], head));
        foreach (var row in rows)
            for (var c = 0; c < into.Length && c < row.Count; c++)
                if (row[c].Length > 0)
                    into[c] = MathF.Max(into[c], measure(row[c], cellFonts[c]));
    }
}

/// <summary>A dropdown's button ("Pull 12 · now ⌄", "DPS ⌄"): its padding, the words, a gap, the chevron, its padding; the gap 6 (CSS px), so the chevron never touches the words.</summary>
public static class Dropdown
{
    /// <summary>Between the words and the chevron (CSS px).</summary>
    public const float Gap = 6;

    /// <summary>The button's width (screen px) for words this wide, a chevron this wide, this padding each side, at this scale.</summary>
    public static float Width(float words, float chevron, float pad, float scale) => pad + words + Gap * scale + chevron + pad;

    /// <summary>Where the chevron starts, from the button's left (screen px).</summary>
    public static float ChevronAt(float words, float pad, float scale) => pad + words + Gap * scale;
}
