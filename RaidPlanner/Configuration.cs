using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;

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

    /// <summary>
    /// Settings → Recording → Smoother replays (0.4.0; off): where everyone stands kept four times a second, not two
    /// (bigger files: about 30 KB more a minute of an eight-player pull; sending thins them back to two when it must).
    /// </summary>
    public bool SmoothReplays { get; set; }

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

    /// <summary>Say each step in chat: a few seconds before its mechanic, its name and callout (<c>PlanJson.ChatLine</c>), with a soft sound.</summary>
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

    /// <summary>
    /// The height you gave the meter (its window's, in CSS px, before Dalamud's scale; 0 not set yet):
    /// in a fight it's as tall as the party needs, never taller than this. It folds to one line between
    /// pulls and opens again to that.
    /// </summary>
    public float MeterHeight { get; set; }

    /// <summary>Compact: 20px rows with smaller words and the tabs in a menu by the lock, so more of the party fits in less room.</summary>
    public bool MeterCompact { get; set; }

    /// <summary>The height you gave the meter in compact (as <see cref="MeterHeight"/>, kept apart: compact is shorter).</summary>
    public float MeterCompactHeight { get; set; }

    /// <summary>
    /// Which way the meter grows when its height changes by itself (folding, opening, a bigger party,
    /// compact): down keeps its top edge where it is, up keeps its foot there (and puts its head at the foot).
    /// </summary>
    public MeterGrows MeterGrows { get; set; } = MeterGrows.Down;

    /// <summary>
    /// The point the meter stays put at (screen px): its top-left corner when it grows down, its
    /// bottom-left when it grows up (<see cref="MeterAnchorFor"/> says which). Set once the meter has
    /// been drawn (<see cref="MeterAnchored"/>), so one saved by an older version keeps where it was;
    /// it follows the meter when you move it.
    /// </summary>
    public float MeterAnchorX { get; set; }

    public float MeterAnchorY { get; set; }

    /// <summary>Whether <see cref="MeterAnchorX"/> and <see cref="MeterAnchorY"/> are set.</summary>
    public bool MeterAnchored { get; set; }

    /// <summary>Which corner the anchor is (moved to the other's place when Grows is changed).</summary>
    public MeterGrows MeterAnchorFor { get; set; } = MeterGrows.Down;

    /// <summary>Where the meter shows the limit break gauge (added in 0.4.0): in its head, in its foot, or not at all.</summary>
    public GaugePlace MeterGauge { get; set; } = GaugePlace.Foot;

    // The party cooldown tracker (0.4.0; Settings → Cooldowns, /raidplan cooldowns).

    /// <summary>The tracker's window: shown, where, lock, click-through, grows, anchor, compact, background, width. Off at first.</summary>
    public OverlaySettings Cooldowns { get; set; } = new() { Show = false, Background = 0.6f };

    /// <summary>The cooldowns you turned on or off (by <see cref="CooldownSpec.Key"/>); the rest are as <see cref="CooldownCatalog"/> has them.</summary>
    public Dictionary<string, bool> CooldownPicks { get; set; } = new();

    /// <summary>Your own cooldowns in the tracker too (the game's own timers).</summary>
    public bool CooldownsMine { get; set; } = true;

    /// <summary>Those ready aren't listed (only what's recharging).</summary>
    public bool CooldownsHideReady { get; set; }

    /// <summary>The limit break gauge as the tracker's last row (when the party has one).</summary>
    public bool CooldownsGauge { get; set; } = true;

    // The on-screen callouts (0.4.0; Settings → Plan window, /raidplan callouts).

    /// <summary>The callouts' window: shown (while the plan follows a pull), lock, click-through, grows, anchor, compact, background, width. On at first.</summary>
    public OverlaySettings CalloutOverlay { get; set; } = new() { Show = true, Everywhere = true, Background = 0.45f };

    /// <summary>How far ahead the callouts look (seconds).</summary>
    public int CalloutAhead { get; set; } = 15;

    /// <summary>How many steps they show at most.</summary>
    public int CalloutCount { get; set; } = 3;

    /// <summary>A sound a few seconds before each step (the game's own &lt;se.N&gt;).</summary>
    public bool CalloutSound { get; set; }

    /// <summary>Which of the game's sound effects (1–16, as &lt;se.1&gt; to &lt;se.16&gt; in a macro).</summary>
    public int CalloutSoundId { get; set; } = 2;

    /// <summary>How many seconds before a step its sound plays.</summary>
    public int CalloutSoundLead { get; set; } = 3;

    /// <summary>The plan picked by hand for a duty (by its ContentFinderCondition), shown there again next time.</summary>
    public Dictionary<uint, string> PlanForDuty { get; set; } = new();

    /// <summary>The token from the site (Settings → Raid Planner → In-game plugin) that sends pulls to your account. Never logged.</summary>
    public string SiteToken { get; set; } = string.Empty;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
