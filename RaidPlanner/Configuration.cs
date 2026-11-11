using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using RaidPlanner.Core.Meter;

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

    // The meter (its own window over the game; Settings → Meter).

    /// <summary>The meter's window is shown (in duties, or everywhere).</summary>
    public bool ShowMeter { get; set; } = true;

    /// <summary>Shown everywhere, not only in duties.</summary>
    public bool MeterEverywhere { get; set; }

    /// <summary>Hidden out of combat (else folded to one line between pulls).</summary>
    public bool MeterHideOutOfCombat { get; set; }

    /// <summary>The number the meter ranks damage by and its bars run to: ENCDPS (the fight's time) or DPS (each player's own).</summary>
    public MeterRank MeterRank { get; set; } = MeterRank.EncDps;

    /// <summary>Jobs only, no names (for a stream).</summary>
    public bool MeterJobsOnly { get; set; }

    /// <summary>The meter's window can't be moved or sized, and has no title bar.</summary>
    public bool MeterLocked { get; set; }

    /// <summary>Locked, the mouse goes through it to the game.</summary>
    public bool MeterClickThrough { get; set; }

    /// <summary>How solid the meter's background is (0–1); its rows stay solid.</summary>
    public float MeterBackground { get; set; } = 0.88f;

    /// <summary>A fight ends when nobody in the party has been in combat this long (seconds).</summary>
    public int MeterEndAfter { get; set; } = 4;

    /// <summary>How many fights the meter keeps to pick from and pace against.</summary>
    public int MeterKeep { get; set; } = 20;

    /// <summary>What pace compares with: the best pull, the last, or nothing.</summary>
    public PaceAgainst MeterPace { get; set; } = PaceAgainst.Best;

    /// <summary>The meter's tab last shown.</summary>
    public MeterTab MeterTab { get; set; } = MeterTab.Damage;

    /// <summary>The plan picked by hand for a duty (by its ContentFinderCondition), shown there again next time.</summary>
    public Dictionary<uint, string> PlanForDuty { get; set; } = new();

    /// <summary>The token from the site (Settings → Raid Planner → In-game plugin) that sends pulls to your account. Never logged.</summary>
    public string SiteToken { get; set; } = string.Empty;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
