using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;

namespace RaidPlanner;

/// <summary>The plugin's settings, kept by Dalamud with the plugin's config.</summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>The settings' version: 2 since 0.6.0 (<see cref="Migrate"/>).</summary>
    public int Version { get; set; } = UploadPrefs.Current;

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
    /// Settings → Recording → Smoother replays (0.4.0; on since 0.7.6, off before): where everyone stands kept four
    /// times a second, not two (bigger files: about 30 KB more a minute of an eight-player pull; sending thins them back
    /// to two when it must). Turned on once for settings saved before 0.7.6 (<see cref="SmoothVersion"/>).
    /// </summary>
    public bool SmoothReplays { get; set; } = true;

    /// <summary>
    /// 1 once Smoother replays has been turned on by default (0.7.6, <see cref="Migrate"/>): settings saved before it had
    /// it off unless chosen, so it's turned on once; turned off after that, it stays off.
    /// </summary>
    public int SmoothVersion { get; set; }

    /// <summary>How many pulls are kept; the oldest go past it.</summary>
    public int Keep { get; set; } = 20;

    /// <summary>"● Rec 2:31" in the server info bar while a pull is recorded.</summary>
    public bool ShowInBar { get; set; } = true;

    /// <summary>
    /// Settings → Recording → <i>Diagnostics</i>: kept as you leave it (since 0.7.5; before, it went off each time the
    /// plugin loaded, which the user didn't want). Off by default.
    /// </summary>
    public bool Diagnostics { get; set; }

    /// <summary>
    /// Upload my logs (since 0.6.0; on): every kept pull goes whole to your logs on the Raid Codex as it ends (when linked). Replaces
    /// 0.5's Send each pull (off by default), which a 0.5 configuration's value is dropped for (<see cref="Migrate"/>).
    /// </summary>
    public bool UploadLogs { get; set; } = true;

    /// <summary>
    /// Since 0.6.0: the pulls uploaded are those that ended since this (set when 0.6.0 first loads, and each time Upload my logs is
    /// turned on); <see cref="DateTimeOffset.MinValue"/> after Upload my older pulls (every kept pull). Null until first set.
    /// </summary>
    public DateTimeOffset? UploadSince { get; set; }

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

    /// <summary>
    /// Compact (round four's "Plate"): the top eight on near-solid plates, the bar in the role's colour, three-figure
    /// numbers, the tabs and the picker in one menu in a 24px head, in the theme picked (<see cref="MeterTheme"/>). Off,
    /// Detailed: the normal meter (the tabs, pace and column heads). Detailed is a fresh install's since 0.8.0 (the user:
    /// "the size of the meter should be detailed by default"; compact from 0.7.0); saved settings keep theirs.
    /// </summary>
    public bool MeterCompact { get; set; }

    /// <summary>
    /// The height given the old compact meter by hand (0.4.0 to 0.6.0). Ignored since 0.7.0: the
    /// compact meter can't be resized (its height is its rows'). Still read from older settings, and
    /// cleared by <see cref="Migrate"/>.
    /// </summary>
    public float MeterCompactHeight { get; set; }

    /// <summary>
    /// A width given the compact meter by hand (0.7.0's test builds only). Ignored: the compact meter
    /// can't be resized (its width is its theme's). Still read, and cleared by <see cref="Migrate"/>.
    /// </summary>
    public float MeterCompactWidth { get; set; }

    /// <summary>The compact meter folded to one line by hand (the clock, the phase, you and your rank, deaths, the LB). Since 0.7.0.</summary>
    public bool MeterCollapsed { get; set; }

    /// <summary>The compact meter's theme (since 0.7.0): Ready Check, Mochi, Native, Readout, Neon or Grimoire.</summary>
    public MeterThemeId MeterTheme { get; set; } = MeterThemeId.ReadyCheck;

    /// <summary>The bars' colours (since 0.7.0): the theme's colour-blind-safe set, or the game's blue, green and red.</summary>
    public MeterRoleColours MeterRoleColours { get; set; } = MeterRoleColours.Theme;

    /// <summary>Whole numbers ("27,840") in the compact meter instead of three figures ("27.8k"). Since 0.7.0.</summary>
    public bool MeterWholeNumbers { get; set; }

    /// <summary>The themes' decorations (sparkles, scanlines, the horizon, the glitch, the ribbon…). On at first. Since 0.7.0.</summary>
    public bool MeterDecorations { get; set; } = true;

    /// <summary>
    /// The meter's look's version (<see cref="UI.Model.MeterLook"/>): 0 before 0.7.0, whose configuration is moved to compact
    /// once. A fresh install's is the current one (0.8.0), so it keeps <see cref="MeterCompact"/>'s default, Detailed.
    /// </summary>
    public int MeterLook { get; set; } = UI.Model.MeterLook.Current;

    /// <summary>
    /// Which way the meter grows when its height changes by itself (folding, opening, a bigger party,
    /// compact): down keeps its top edge where it is, up keeps its bottom edge there. Until 0.7.4 up put its
    /// head at the foot and read its rows up too; those are <see cref="MeterHead"/> and <see cref="MeterOrder"/> now.
    /// </summary>
    public MeterGrows MeterGrows { get; set; } = MeterGrows.Down;

    /// <summary>Where the meter's head sits (since 0.7.4): the top or the bottom edge, whichever way it grows.</summary>
    public MeterHead MeterHead { get; set; } = MeterHead.Top;

    /// <summary>The rows' order reading down the window (since 0.7.4): the top player first or last.</summary>
    public MeterOrder MeterOrder { get; set; } = MeterOrder.HighestFirst;

    /// <summary>
    /// The meter's layout's version (<see cref="UI.Model.MeterArrange"/>): 0 before 0.7.4, whose Grows Up is moved
    /// once to the head at the bottom and lowest first (as it looked), by <see cref="Migrate"/>.
    /// </summary>
    public int MeterArrangeVersion { get; set; }

    /// <summary>The meter's three layout settings together.</summary>
    public MeterArrange MeterArrangeNow() => new(MeterGrows, MeterHead, MeterOrder);

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

    // The party cooldown tracker (0.4.0; Settings → Cooldowns, /raidcodex cooldowns).

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

    // The on-screen callouts (0.4.0; Settings → Plan window, /raidcodex callouts).

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

    // How big the windows are (0.8.0; Settings → Windows → Size, /raidcodex size).

    /// <summary>
    /// Every window's size, on top of Dalamud's own scale (<see cref="UI.Model.WindowSize"/>): 0 fits the screen (bigger on
    /// one taller than 1440 px: 150% on 4K), else the size picked, 80 to 200 (%).
    /// </summary>
    public int WindowSize { get; set; } = UI.Model.WindowSize.Fit;

    /// <summary>
    /// Screen px per CSS px the windows were last drawn at (Dalamud's scale times the plugin's; 0 before 0.8.0, when it
    /// was Dalamud's alone): the sizes ImGui remembers from then are at it, so the windows it sizes (the main window, the
    /// plan window, the meter) are grown or shrunk from it, and fitting the screen builds the fonts at it first.
    /// </summary>
    public float WindowScaleWas { get; set; }

    /// <summary>The plan picked by hand for a duty (by its ContentFinderCondition), shown there again next time.</summary>
    public Dictionary<uint, string> PlanForDuty { get; set; } = new();

    /// <summary>The token from the site (Settings → Raid Codex → In-game plugin) that sends pulls to your account. Never logged.</summary>
    public string SiteToken { get; set; } = string.Empty;

    /// <summary>
    /// The first-time setup guide (0.8.0, <see cref="UI.Model.SetupState"/>): 0 in settings saved before it (an existing
    /// player's: <see cref="Migrate"/> makes it Done), Pending for a fresh install (set by the plugin as it makes the
    /// settings), Done once the guide is finished or skipped.
    /// </summary>
    public int Setup { get; set; }

    /// <summary>
    /// Which of the main parts are on (Settings → Features, the setup guide), read from their own settings. Plan and
    /// callouts is on while either the plan window or the on-screen callouts is.
    /// </summary>
    public UI.Model.FeatureSet FeaturesNow() =>
        new(AutoRecord, UploadLogs, ShowMeter, ShowPlanInDuties || CalloutOverlay.Show, Cooldowns.Show, TrackProgress);

    /// <summary>
    /// Turns each main part on or off. Plan and callouts, the plan window and the on-screen callouts together, is
    /// written only when it changes: turning another part on or off never touches one of the two left as you set it.
    /// </summary>
    public void SetFeatures(UI.Model.FeatureSet f)
    {
        var plan = FeaturesNow().Plan;
        AutoRecord = f.Record;
        UploadLogs = f.Upload;
        ShowMeter = f.Meter;
        if (f.Plan != plan)
        {
            ShowPlanInDuties = f.Plan;
            CalloutOverlay.Show = f.Plan;
        }
        Cooldowns.Show = f.Cooldowns;
        TrackProgress = f.Progress;
    }

    /// <summary>What's uploaded, as <see cref="UploadPrefs"/> has it (a method, so it isn't saved with the settings).</summary>
    public UploadPrefs UploadPrefsNow() => new(Version, UploadLogs, UploadSince);

    /// <summary>Brings settings saved by an older version up to this one (<see cref="UploadPrefs.Migrate"/>, <see cref="UI.Model.MeterLook.Migrate"/>, <see cref="MeterArrange.Migrate"/>); true when anything changed (then save).</summary>
    public bool Migrate(DateTimeOffset now)
    {
        var changed = false;
        var was = UploadPrefsNow();
        var to = UploadPrefs.Migrate(was, now);
        if (to != was)
        {
            Version = to.Version;
            UploadLogs = to.UploadLogs;
            UploadSince = to.UploadSince;
            changed = true;
        }
        // 0.7.0: the new compact meter is the default and replaces the old compact (MeterLook.Migrate).
        var look = UI.Model.MeterLook.Migrate(MeterLook, MeterCompact, MeterCompactHeight);
        if (look != (MeterLook, MeterCompact, MeterCompactHeight))
        {
            (MeterLook, MeterCompact, MeterCompactHeight) = look;
            changed = true;
        }
        // 0.7.4: Grows, Head and Order apart; an older Up keeps its look (the head at the bottom, lowest first).
        var arrange = MeterArrange.Migrate(MeterArrangeVersion, MeterGrows, MeterHead, MeterOrder);
        if (arrange != (MeterArrangeVersion, MeterHead, MeterOrder))
        {
            (MeterArrangeVersion, MeterHead, MeterOrder) = arrange;
            changed = true;
        }
        // 0.8.0: settings saved before the setup guide are an existing player's: it's never shown them.
        var setup = UI.Model.SetupState.Migrate(Setup);
        if (setup != Setup)
        {
            Setup = setup;
            changed = true;
        }
        // 0.7.6: Smoother replays on by default, once (turned off after this, it stays off).
        var smooth = Core.SmoothDefault.Migrate(SmoothVersion, SmoothReplays);
        if (smooth != (SmoothVersion, SmoothReplays))
        {
            (SmoothVersion, SmoothReplays) = smooth;
            changed = true;
        }
        // 0.7.0: the compact meter isn't sized by hand (its width is its theme's, its height its rows'):
        // a height or width given it before is read but ignored, and cleared here.
        if (MeterCompactHeight != 0 || MeterCompactWidth != 0)
        {
            MeterCompactHeight = 0;
            MeterCompactWidth = 0;
            changed = true;
        }
        return changed;
    }

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
