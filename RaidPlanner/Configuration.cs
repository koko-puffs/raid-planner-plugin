using System;
using Dalamud.Configuration;

namespace RaidPlanner;

/// <summary>The plugin's settings, kept by Dalamud with the plugin's config.</summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Record every fight in a duty by itself, from the pull until combat ends.</summary>
    public bool AutoRecord { get; set; } = true;

    /// <summary>
    /// Record in every duty, dungeons and field operations too (off: only fights, like trials, raids,
    /// ultimates and criterion dungeons, so trash packs don't push out the pulls you keep).
    /// </summary>
    public bool RecordAllDuties { get; set; }

    /// <summary>Keep where the party stands (off: only the enemies and the mechanics, to place the party yourself).</summary>
    public bool RecordParty { get; set; } = true;

    /// <summary>How many pulls are kept; the oldest go past it.</summary>
    public int Keep { get; set; } = 20;

    /// <summary>"● Rec 2:31" in the server info bar while a pull is recorded.</summary>
    public bool ShowInBar { get; set; } = true;

    /// <summary>The token from the site (Settings → Raid Planner → In-game plugin) that sends pulls to your account. Never logged.</summary>
    public string SiteToken { get; set; } = string.Empty;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
