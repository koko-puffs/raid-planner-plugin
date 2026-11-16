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

    /// <summary>Zen Maru Gothic 900 (Mochi's numbers and clock).</summary>
    RoundBlack,

    /// <summary>Fraunces 500 at opsz 14 (Grimoire's names and labels; latin, OFL).</summary>
    Serif,

    /// <summary>Fraunces 600 at opsz 14 (Grimoire's numbers and head).</summary>
    SerifSemibold,

    /// <summary>The game's Axis (Native's names and labels), through Dalamud: nothing shipped, and it has Japanese.</summary>
    GameAxis,

    /// <summary>The game's MiedingerMid (Native's numbers, as the party list's HP).</summary>
    GameNumbers,

    /// <summary>The game's Jupiter (Native's title band).</summary>
    GameTitle,
}
