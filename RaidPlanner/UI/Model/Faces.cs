namespace RaidPlanner.UI;

/// <summary>
/// The faces the windows draw in. Ready Check's three voices, by weight: the read (Geist), the count
/// (Geist Mono), the callout (Archivo); and since 0.7.0 the meter's themes' own (<see cref="Model.MeterTheme"/>):
/// Zen Maru Gothic (Mochi), Fraunces (Grimoire) and the game's fonts through Dalamud (Native), loaded
/// only while a theme that draws in them is picked (<c>Fonts.Want</c>). Here, game-free, so the
/// themes' token tables are tested.
/// </summary>
public enum Face
{
    /// <summary>Geist 400: what you read.</summary>
    Sans,

    /// <summary>Geist 500 (the site's 550): buttons, names in lists, tabs, a setting's name.</summary>
    SansMedium,

    /// <summary>Geist 600 (and the site's 650 and 700): small headings, the telegraph's words, a cast's name, a spot's label.</summary>
    SansSemibold,

    /// <summary>Geist Mono 500: eyebrows, tags, times and counts that line up.</summary>
    Mono,

    /// <summary>Geist Mono 600: the clock, values, the meter's numbers.</summary>
    MonoSemibold,

    /// <summary>Archivo Expanded ExtraBold (wdth 125, 800), in capitals: the callout, what a raid lead says.</summary>
    Callout,

    /// <summary>Zen Maru Gothic 700 (Mochi's names and labels; latin only, OFL). Since 0.7.0.</summary>
    Round,

    /// <summary>Zen Maru Gothic 900 (Mochi's name on your row; its numbers and clock are Geist SemiBold since 0.7.0's test builds).</summary>
    RoundBlack,

    /// <summary>Fraunces 500 at opsz 14 (Grimoire's names and labels; latin, OFL).</summary>
    Serif,

    /// <summary>Fraunces 600 at opsz 14 (Grimoire's numbers and head).</summary>
    SerifSemibold,

    /// <summary>The game's Axis (Native's names and labels), through Dalamud: nothing shipped, and it has Japanese.</summary>
    GameAxis,

    /// <summary>The game's MiedingerMid (as the party list's HP). Not drawn by a theme: at the meter's sizes it lost its decimal points, so Native's numbers are Axis.</summary>
    GameNumbers,

    /// <summary>The game's Jupiter (Native's title band).</summary>
    GameTitle,
}

/// <summary>
/// Centring words by their ink, not their line: a face's line box (ImGui's font size, its ascent to its
/// descent) puts its capitals at the box's middle only when its ascent and descent are balanced around
/// them. Geist's are (its 'H' is centred in its line to the unit, Geist Mono's too), Zen Maru Gothic's
/// aren't (its ascent is tall: centred by the box its capitals sit about 0.086 em low, 1.2px at 14px),
/// nor the game's own. So each line is drawn with its capitals' ink (ImGui's glyph for 'H': Y0 to Y1,
/// from the line's top) on the line box's middle: Geist stays where it was, the others line up with it.
/// </summary>
public static class InkCentre
{
    /// <summary>The ink's middle from the line's top, from a glyph's top and bottom (ImGui's Y0, Y1).</summary>
    public static float Mid(float inkTop, float inkBottom) => (inkTop + inkBottom) / 2;

    /// <summary>Where the line's top goes so ink with this middle (<see cref="Mid"/>) sits on <paramref name="lineMiddle"/>.</summary>
    public static float Top(float lineMiddle, float inkMid) => lineMiddle - inkMid;

    /// <summary>How far (px; up negative) centring by the ink moves words from centring by their line box <paramref name="fontSize"/> tall.</summary>
    public static float Shift(float fontSize, float inkMid) => fontSize / 2 - inkMid;
}
