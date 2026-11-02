using System;
using System.Collections.Generic;
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

    /// <summary>Each kept pull is sent to the Raid Planner by itself (when linked).</summary>
    public bool SendEachPull { get; set; }

    /// <summary>Each kept pull's summary (how far it got, who died to what) goes to the site's progress tracker (when linked).</summary>
    public bool TrackProgress { get; set; } = true;

    /// <summary>In a duty you have a plan for, its window opens.</summary>
    public bool ShowPlanInDuties { get; set; } = true;

    /// <summary>A few seconds before each step's mechanic, its name and first note in chat, with a soft sound.</summary>
    public bool Callouts { get; set; } = true;

    /// <summary>The step the plan window shows is shared with the site (Present can follow it).</summary>
    public bool ShareStep { get; set; }

    /// <summary>The plan window can't be moved or resized.</summary>
    public bool PlanWindowLocked { get; set; }

    /// <summary>The plan picked by hand for a duty (by its ContentFinderCondition), shown there again next time.</summary>
    public Dictionary<uint, string> PlanForDuty { get; set; } = new();

    /// <summary>The token from the site (Settings → Raid Planner → In-game plugin) that sends pulls to your account. Never logged.</summary>
    public string SiteToken { get; set; } = string.Empty;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
