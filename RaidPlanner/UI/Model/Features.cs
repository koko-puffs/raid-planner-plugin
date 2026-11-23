using System.Collections.Generic;

namespace RaidPlanner.UI.Model;

/// <summary>
/// The plugin's main parts, each one switch (0.8.0: the setup guide's second step and Settings → Features): what each
/// is called and says, which need a linked account, and the setup guide's quick starts. The configuration maps them
/// onto its own settings (<c>Configuration.FeaturesNow</c>, <c>Configuration.SetFeatures</c>).
/// </summary>
public enum Feature
{
    /// <summary>Record my pulls (<c>AutoRecord</c>).</summary>
    Record,

    /// <summary>Upload my logs (<c>UploadLogs</c>; needs an account).</summary>
    Upload,

    /// <summary>Damage meter (<c>ShowMeter</c>).</summary>
    Meter,

    /// <summary>Plan and callouts (<c>ShowPlanInDuties</c> and <c>CalloutOverlay.Show</c>, together).</summary>
    Plan,

    /// <summary>Party cooldowns (<c>Cooldowns.Show</c>).</summary>
    Cooldowns,

    /// <summary>Track my progress (<c>TrackProgress</c>; needs an account).</summary>
    Progress,
}

/// <summary>The setup guide's quick starts (its "Start from").</summary>
public enum FeaturePreset
{
    /// <summary>Everything but Party cooldowns: the plugin's defaults.</summary>
    Recommended,
    Everything,

    /// <summary>Record my pulls and Upload my logs, nothing on screen.</summary>
    RecordingOnly,
}

/// <summary>Which features are on: one switch each.</summary>
public readonly record struct FeatureSet(bool Record, bool Upload, bool Meter, bool Plan, bool Cooldowns, bool Progress)
{
    public bool this[Feature f] => f switch
    {
        Feature.Record => Record,
        Feature.Upload => Upload,
        Feature.Meter => Meter,
        Feature.Plan => Plan,
        Feature.Cooldowns => Cooldowns,
        _ => Progress,
    };

    public FeatureSet With(Feature f, bool on) => f switch
    {
        Feature.Record => this with { Record = on },
        Feature.Upload => this with { Upload = on },
        Feature.Meter => this with { Meter = on },
        Feature.Plan => this with { Plan = on },
        Feature.Cooldowns => this with { Cooldowns = on },
        _ => this with { Progress = on },
    };

    /// <summary>How many are on.</summary>
    public int Count => (Record ? 1 : 0) + (Upload ? 1 : 0) + (Meter ? 1 : 0) + (Plan ? 1 : 0) + (Cooldowns ? 1 : 0) + (Progress ? 1 : 0);
}

public static class Features
{
    /// <summary>In the order the guide and Settings → Features list them.</summary>
    public static readonly IReadOnlyList<Feature> All = new[] { Feature.Record, Feature.Upload, Feature.Meter, Feature.Plan, Feature.Cooldowns, Feature.Progress };

    public static string Name(Feature f) => f switch
    {
        Feature.Record => "Record my pulls",
        Feature.Upload => "Upload my logs",
        Feature.Meter => "Damage meter",
        Feature.Plan => "Plan and callouts",
        Feature.Cooldowns => "Party cooldowns",
        _ => "Track my progress",
    };

    /// <summary>What it does, in one line.</summary>
    public static string Line(Feature f) => f switch
    {
        Feature.Record => "Every pull in raids and trials, kept on this PC.",
        Feature.Upload => "Each pull goes to your Raid Codex as it ends, private to you.",
        Feature.Meter => "A live damage meter in duties. No ACT needed.",
        Feature.Plan => "Your plan's next steps on screen during the fight.",
        Feature.Cooldowns => "Who has their raid buffs and mitigation ready.",
        _ => "Your best pull each night, and how far you got.",
    };

    /// <summary>It does nothing until your account is linked.</summary>
    public static bool NeedsAccount(Feature f) => f is Feature.Upload or Feature.Progress;

    public static string PresetName(FeaturePreset p) => p switch
    {
        FeaturePreset.Everything => "Everything",
        FeaturePreset.RecordingOnly => "Recording only",
        _ => "Recommended",
    };

    public static FeatureSet Preset(FeaturePreset p) => p switch
    {
        FeaturePreset.Everything => new(true, true, true, true, true, true),
        FeaturePreset.RecordingOnly => new(true, true, false, false, false, false),
        _ => new(true, true, true, true, false, true),
    };

    /// <summary>The quick start these switches are, if they're one (else they're your own pick).</summary>
    public static FeaturePreset? PresetOf(FeatureSet set)
    {
        foreach (var p in new[] { FeaturePreset.Recommended, FeaturePreset.Everything, FeaturePreset.RecordingOnly })
            if (Preset(p) == set) return p;
        return null;
    }
}

/// <summary>
/// The first-time setup guide's state (0.8.0, <c>Configuration.Setup</c>): settings saved before it are an existing
/// player's (never sent through it); a fresh install's start Pending, and the guide opens once the game's ready.
/// </summary>
public static class SetupState
{
    /// <summary>Saved by a version before the guide: an existing player's (treated as Done).</summary>
    public const int Unknown = 0;
    public const int Pending = 1;
    public const int Done = 2;

    /// <summary>The guide still to be shown.</summary>
    public static bool Due(int state) => state == Pending;

    /// <summary>Settings saved before 0.8.0 (Unknown) are an existing player's: Done.</summary>
    public static int Migrate(int state) => state == Unknown ? Done : state;
}
