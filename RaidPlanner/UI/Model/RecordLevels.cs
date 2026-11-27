using System.Collections.Generic;

namespace RaidPlanner.UI.Model;

/// <summary>
/// Settings → Recording → <i>Which duties</i> (since the dungeons round, not yet released; replaces <i>In every duty</i>): one
/// pick of three, each taking in the one before. 1 <see cref="Hard"/> (the default): Savage, Ultimate, Extreme and Unreal
/// trials, Criterion (normal and Savage) and the Chaotic alliance raid; 2 <see cref="Normal"/>: normal raids and trials too;
/// 3 <see cref="Every"/>: dungeons (Variant too), alliance raids and every other duty too. A duty's level is its
/// <see cref="DutyKind"/>'s (<see cref="DutyKinds.Of"/>, from the game's data: the ContentType row, the HighEndDuty flag, the
/// name's "(Savage)", "(Extreme)"…). The Occult Crescent is never recorded, whatever the level (the recorder's own rule).
/// Game-free, so it's tested.
/// </summary>
public static class RecordLevels
{
    public const int Hard = 1, Normal = 2, Every = 3;

    /// <summary>The segments' words, in order (level 1 first).</summary>
    public static readonly IReadOnlyList<string> Labels = new[] { "Hard content", "+ Normal", "+ Dungeons" };

    /// <summary>Each level's one line, in order (the setting's tooltip, and the site's plugin page).</summary>
    public static readonly IReadOnlyList<string> Hints = new[]
    {
        "Savage, Ultimate, Extreme and Unreal trials, Criterion, and the Chaotic alliance raid.",
        "Normal raids and normal trials too.",
        "Dungeons, alliance raids and every other duty too (never the Occult Crescent).",
    };

    /// <summary>A saved level as it's used: 1 to 3 (0, not yet set, and anything else read as hard content).</summary>
    public static int Clamp(int level) => level is Normal or Every ? level : Hard;

    /// <summary>The level a duty of this kind is first recorded at.</summary>
    public static int Of(DutyKind kind) => kind switch
    {
        DutyKind.Ultimate or DutyKind.Savage or DutyKind.Extreme or DutyKind.Criterion => Hard,
        DutyKind.NormalRaid => Normal,
        // Dungeons, alliance raids, the field operations' zones (open world inside a duty), PvP, quest battles, the rest.
        _ => Every,
    };

    /// <summary>
    /// The level a duty is first recorded at, from the game's data (<see cref="Of(DutyKind)"/> of <see cref="DutyKinds.Of"/>, as
    /// the recorder decides): each pull's <c>level</c>, in the file and the summary. Null when the zone isn't in the game's data
    /// (no facts), so its duty can't be told; the field operations' zones are <see cref="Every"/>.
    /// </summary>
    public static int? For(DutyFacts? facts) => facts is null ? null : Of(DutyKinds.Of(facts));

    /// <summary>Is a duty of this kind recorded at this level?</summary>
    public static bool Records(int level, DutyKind kind) => Of(kind) <= Clamp(level);

    /// <summary>What a level records, in a line's words: "hard content", "hard content, normal raids and trials", "every duty" (Home's line).</summary>
    public static string Words(int level) => Clamp(level) switch
    {
        Normal => "hard content, normal raids and trials",
        Every => "every duty",
        _ => "hard content",
    };

    /// <summary>The level's word ("Hard content"), for the setup guide's note.</summary>
    public static string Label(int level) => Labels[Clamp(level) - 1];

    /// <summary>
    /// Settings saved before the levels: <i>In every duty</i> on is <see cref="Every"/>, off hard content. Only when the level
    /// isn't set yet (0); once set it stays as chosen. Returns the level and the old switch, now cleared.
    /// </summary>
    public static (int Level, bool AllDuties) Migrate(int level, bool allDuties) =>
        level is Hard or Normal or Every ? (level, false) : (allDuties ? Every : Hard, false);
}
