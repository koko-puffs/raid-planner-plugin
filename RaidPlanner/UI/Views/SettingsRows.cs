using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Settings' rows (0.8.0): each row drawn by what it is (<see cref="SettingId"/>), wherever it's shown (its page, a
/// copy on another page, a search's answer), with the words <see cref="SettingsIndex"/> gives it; and the row itself:
/// a setting's name (550 14px) over one muted line, its control at the right, a sub-row 34 in, a row that can't be
/// used dimmed and saying why ("Link your account first: Account").
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] GrowsItems = { "Down", "Up" };
    private static readonly string[] HeadItems = { "Top", "Bottom" };
    private static readonly string[] OrderItems = { "Highest first", "Lowest first" };
    private static readonly string[] GaugeItems = { "Head", "Foot", "Off" };
    private static readonly string[] WhereItems = { "In duties", "Everywhere" };
    private static readonly string[] RoleItems = { "The theme's", "The game's" };
    private static readonly string[] RoleShortItems = { "Theme's", "Game's" };
    private static readonly string[] SizeItems = { "Compact", "Detailed" };
    private static readonly string[] DutyItems = { "Raids and trials", "Every duty" };
    private static readonly string[] RankItems = { "ENCDPS", "DPS" };
    private static readonly string[] NameItems = { "Names", "Jobs only" };
    private static readonly string[] PaceItems = { "Best", "Last", "Off" };

    private const string CompactTip =
        "Compact: one head with the clock, the phase, the limit break and a menu (the tabs, the pulls, pace), the top eight on near-solid plates with the bar in the role's "
        + "colour and the job's icon, three-figure numbers in fixed columns, you pinned at the foot when you're below them; its size is its theme's and its rows', not set by "
        + "hand. Detailed: the normal meter, in the same theme, with its tabs, pace bar and column heads, sized by its edge. The meter's own menu has this too.";

    private const string RolesTip =
        "The theme's: a blue, a green and a red-orange told apart with any colour blindness (each theme has its own set). The game's: its own blue, green and red, as the party list has them.";

    private const string SampleTip =
        "Eight players mid-pull, with a limit break, a death, pace and the clock running: the meter as it looks in a fight, to place it (and size the normal meter). Nothing in "
        + "it is real, it's never mixed with real numbers, and a real fight ends it at once.";

    private const string CalloutSampleTip =
        "A few made-up steps counting down and starting again, as the callouts show your plan's: to place and size them without a pull.";

    private const string GrowsTip =
        "Down: its top edge stays where you put it and it grows downwards. Up: its foot stays, and it grows upwards.";

    private const string PlanPickMenu = "##set-plan-pick";

    /// <summary>
    /// Draws a row by what it is. <paramref name="found"/>: a search's answer (no sub-row indent; a row that lives in a
    /// page's own panel is a Go row there).
    /// </summary>
    private void DrawRow(SettingEntry e, int i, bool found = false)
    {
        var sub = e.Sub && !found;
        var n = e.Name;
        var l = e.Line;
        switch (e.Id)
        {
            // ---- Recording and uploads.
            case SettingId.AutoRecord:
                if (SwitchRow(i, "##auto", n, l, config.AutoRecord,
                        "Each pull is recorded on its own: the bosses' casts and where they land, stacks, spreads, tankbusters, tethers, head markers and waymarks. Off: nothing is recorded."))
                {
                    config.AutoRecord = !config.AutoRecord;
                    config.Save();
                }
                break;
            case SettingId.RecordAllDuties:
            {
                var picked = SegmentRow(i, "##everywhere", n, l, DutyItems, config.RecordAllDuties ? 1 : 0, sub, !config.AutoRecord,
                    "Raids and trials: only fights are recorded (trials, raids, ultimates, criterion dungeons), so a run of trash packs never pushes out the pulls you want. "
                    + "Every duty: every duty's pulls are, but never the Occult Crescent's.");
                if (picked != (config.RecordAllDuties ? 1 : 0))
                {
                    config.RecordAllDuties = picked == 1;
                    config.Save();
                }
                break;
            }
            case SettingId.RecordParty:
                if (SwitchRow(i, "##party", n, l, config.RecordParty,
                        "On, the plan shows where everyone stood at each mechanic (you can still swap to standard spots on the site). Off, only the enemies and their attacks are kept, "
                        + "for when you'd rather work out your own strats. Each pull keeps the party's jobs, and (since 0.6.0, for your logs) their names, home worlds and levels.", sub: sub))
                {
                    config.RecordParty = !config.RecordParty;
                    config.Save();
                }
                break;
            case SettingId.Keep:
            {
                var delta = StepperRow(i, "##keep", n, l, config.Keep.ToString(System.Globalization.CultureInfo.InvariantCulture), config.Keep > 5, config.Keep < 100,
                    "Pulls are kept in the plugin's folder until there are more than this; then, as each new one is kept, the oldest already on the site goes first, and only then the "
                    + "oldest that isn't. Pulls still waiting to be uploaded are held (up to 200) until they've gone. Lowering it deletes nothing now.", sub: sub);
                if (delta != 0)
                {
                    config.Keep = Math.Clamp(config.Keep + delta * 5, 5, 100);
                    config.Save();
                    // Nothing is deleted on a click: the next pull kept lets the extra ones go.
                    store.Keep = config.Keep;
                }
                break;
            }
            case SettingId.UploadLogs:
                if (SwitchRow(i, "##upload", n, l, config.UploadLogs,
                        "Each pull worth keeping goes whole to your logs on the site as soon as it's kept, in the background, one at a time. Pulls kept while you're offline go when "
                        + "you're back. Your logs are public on the site, with your party's names and home worlds, as on FFLogs; you can make yours private there. Off, nothing is "
                        + "uploaded (you can still send a pull yourself).", off: !Linked, linkFirst: !Linked))
                    logs.SetOn(!config.UploadLogs);
                break;
            case SettingId.UploadOlder:
            {
                var live = Linked && config.UploadLogs;
                var older = live ? logs.OlderCount : 0;
                if (ButtonRow(i, "##upload-older", n, live ? UploadStatus(older) : l, "Upload", Btn.Outline, !live || older == 0,
                        live && older == 0 ? "Every pull kept here is on the site or on its way." : null,
                        "The pulls kept here from before Upload my logs was on go too, the oldest first (the site takes 60 an hour).", off: !live, linkFirst: !Linked,
                        why: Linked && !config.UploadLogs ? "With Upload my logs on." : null, sub: sub))
                    logs.UploadOlder();
                break;
            }
            case SettingId.WhoSees:
                if (ButtonRow(i, "##logs-private", n, l, "Change on the site", Btn.Outline, false, null,
                        "Your logs are public on the Raid Codex, as on FFLogs: each pull with your party's names and home worlds. In the site's Settings → Your logs you can make "
                        + "yours private (then they're unranked too) or hide your character.", icon: Icon.ExternalLink, sub: sub))
                    Dalamud.Utility.Util.OpenLink(SiteLink.LogsSettingsUrl);
                break;
            case SettingId.TrackProgress:
                if (SwitchRow(i, "##progress", n, l, config.TrackProgress,
                        "After each kept pull, a few numbers go to your account: the duty, how long it lasted, the furthest phase, the boss's HP left, who died to what and what hit the "
                        + "party (by spot and job, never names). The site charts your progress from them.", off: !Linked, linkFirst: !Linked))
                {
                    config.TrackProgress = !config.TrackProgress;
                    config.Save();
                }
                break;
            case SettingId.SmoothReplays:
                if (SwitchRow(i, "##smooth", n, l, config.SmoothReplays,
                        "Where everyone stands is kept four times a second instead of two, so the site's replay of the pull moves more smoothly. Files grow by about 30 KB a minute of an "
                        + "eight-player pull; a pull too big to send is thinned back to two first.", off: !config.RecordParty,
                        why: config.RecordParty ? null : "With Record where the party stands on."))
                {
                    config.SmoothReplays = !config.SmoothReplays;
                    config.Save();
                }
                break;
            case SettingId.ShowInBar:
                if (SwitchRow(i, "##bar", n, l, config.ShowInBar,
                        "The bar at the top right of the screen shows that a pull is being recorded, and for how long. Click it to open the plugin."))
                {
                    config.ShowInBar = !config.ShowInBar;
                    config.Save();
                }
                break;
            case SettingId.RecordingsFolder:
                if (ButtonRow(i, "##folder", n, l, "Open", Btn.Outline, false, null, store.Folder, icon: Icon.Folder))
                    Dalamud.Utility.Util.OpenLink(store.Folder);
                break;

            // ---- The meter.
            case SettingId.ShowMeter:
                if (SwitchRow(i, "##meter-show", n, l, config.ShowMeter,
                        "Damage, healing, damage taken and deaths for your party, worked out in the game from what it already tells the plugin. Its numbers and names stay in the plugin.", pad: 10))
                {
                    config.ShowMeter = !config.ShowMeter;
                    config.Save();
                }
                break;
            case SettingId.MeterWhere:
            {
                var where = SegmentRow(i, "##meter-where", n, l, WhereItems, config.MeterEverywhere ? 1 : 0, sub, !config.ShowMeter);
                if (where != (config.MeterEverywhere ? 1 : 0))
                {
                    config.MeterEverywhere = where == 1;
                    config.Save();
                }
                break;
            }
            case SettingId.MeterOpenInDuties:
                if (SwitchRow(i, "##meter-open-duties", n, l, config.MeterOpenInDuties,
                        "In a duty the meter stays open between pulls, on the pull just done, so you can read it while you reset. Off, it folds to one line between pulls (or hides, "
                        + "with Hide it out of combat on), as it does out of duties.", sub: sub, pad: 10, off: !config.ShowMeter))
                {
                    config.MeterOpenInDuties = !config.MeterOpenInDuties;
                    config.Save();
                }
                break;
            case SettingId.MeterHide:
                if (SwitchRow(i, "##meter-hide", n, l, config.MeterHideOutOfCombat, null, sub: sub, pad: 10, off: !config.ShowMeter))
                {
                    config.MeterHideOutOfCombat = !config.MeterHideOutOfCombat;
                    config.Save();
                }
                break;
            case SettingId.MeterSize:
            {
                var size = SegmentRow(i, "##meter-size", n, l, SizeItems, config.MeterCompact ? 0 : 1, tip: CompactTip);
                if (size != (config.MeterCompact ? 0 : 1))
                {
                    config.MeterCompact = size == 0;
                    config.Save();
                }
                break;
            }
            case SettingId.MeterRoles:
            {
                var roles = SegmentRow(i, "##meter-roles", n, l, RoleItems, (int)config.MeterRoleColours, tip: RolesTip);
                if (roles != (int)config.MeterRoleColours)
                {
                    config.MeterRoleColours = (MeterRoleColours)roles;
                    config.Save();
                }
                break;
            }
            case SettingId.MeterSample:
            {
                var sampleOn = SampleOn?.Invoke() == true;
                var waits = !sampleOn && SampleWaits?.Invoke() == true;
                if (ButtonRow(i, "##meter-sample", n,
                        waits ? "After this fight: a real one ends a sample" : sampleOn ? "Made-up numbers under a Preview tag, until a fight starts" : l,
                        sampleOn ? "Stop the sample" : "Play", Btn.Outline, waits || ToggleSample == null, null, SampleTip, icon: sampleOn ? Icon.Square : Icon.Play, pad: 10))
                    ToggleSample?.Invoke();
                break;
            }
            case SettingId.MeterRank:
            {
                var rank = SegmentRow(i, "##meter-rank", n, l, RankItems, (int)config.MeterRank);
                if (rank != (int)config.MeterRank)
                {
                    config.MeterRank = (MeterRank)rank;
                    config.Save();
                }
                break;
            }
            case SettingId.MeterNames:
            {
                var names = SegmentRow(i, "##meter-names", n, l, NameItems, config.MeterJobsOnly ? 1 : 0, tip: "Names are kept in the plugin, never sent.");
                if (names != (config.MeterJobsOnly ? 1 : 0))
                {
                    config.MeterJobsOnly = names == 1;
                    config.Save();
                }
                break;
            }
            case SettingId.MeterGrows:
            {
                // Since 0.7.4 three settings apart (until then Grows flipped all three): the edge that stays put, where the
                // head sits, the rows' order. Both meters; an older configuration growing up was moved to Up, Bottom, Lowest first.
                var grows = SegmentRow(i, "##meter-grows", n, l, GrowsItems, (int)config.MeterGrows,
                    tip: "Down: its top edge stays where you put it and it grows downwards. Up: its bottom edge stays and it grows upwards (for a meter sitting on your hotbars). "
                    + "Where its head sits and the rows' order are Head and Order, on Damage meter.");
                if (grows != (int)config.MeterGrows)
                {
                    config.MeterGrows = (MeterGrows)grows;
                    config.Save();
                }
                break;
            }
            case SettingId.MeterHead:
            {
                var head = SegmentRow(i, "##meter-head", n, l, HeadItems, (int)config.MeterHead,
                    tip: "Top or Bottom of the meter, whichever way it grows: the normal meter's clock, tabs, pull menu and lock (pace and the column heads beside them), compact's head. "
                    + "The party's line sits at the other edge.");
                if (head != (int)config.MeterHead)
                {
                    config.MeterHead = (MeterHead)head;
                    config.Save();
                }
                break;
            }
            case SettingId.MeterOrder:
            {
                var order = SegmentRow(i, "##meter-order", n, l, OrderItems, (int)config.MeterOrder,
                    tip: "Highest first: the top player at the top of the list. Lowest first: at its bottom. The first death and the newest event go where the top player does; compact's "
                    + "pinned you stays at the end away from them.");
                if (order != (int)config.MeterOrder)
                {
                    config.MeterOrder = (MeterOrder)order;
                    config.Save();
                }
                break;
            }
            case SettingId.MeterGauge:
            {
                var gauge = SegmentRow(i, "##meter-gauge", n, l, GaugeItems, (int)config.MeterGauge,
                    tip: "The party's limit break bars, in the meter's head (after the clock, as room allows) or its foot (before what it says on the right), while a fight is on. Off, nowhere.");
                if (gauge != (int)config.MeterGauge)
                {
                    config.MeterGauge = (GaugePlace)gauge;
                    config.Save();
                }
                break;
            }
            case SettingId.MeterBackground:
            {
                // 20% at the least, as the meter draws it (below that its edge and words would float on nothing).
                var theme = MeterThemes.Of(config.MeterTheme);
                var floor = theme.Win.Floor;
                var bg = Math.Clamp(SliderRow(i, "##meter-bg", n, floor > 0 ? $"Rows stay solid; {theme.Name} keeps {Format.PercentWhole(floor)} at least" : l,
                    Math.Clamp(config.MeterBackground, 0.2f, 1f)), 0.2f, 1f);
                if (MathF.Abs(bg - config.MeterBackground) > 0.001f)
                {
                    config.MeterBackground = bg;
                    backgroundDirty = true;
                }
                break;
            }
            case SettingId.MeterEndAfter:
            {
                var end = StepperRow(i, "##meter-end", n, l, $"{config.MeterEndAfter} s", config.MeterEndAfter > 2, config.MeterEndAfter < 15, pad: 10);
                if (end != 0)
                {
                    config.MeterEndAfter = Math.Clamp(config.MeterEndAfter + end, 2, 15);
                    config.Save();
                }
                break;
            }
            case SettingId.MeterKeep:
            {
                var keep = StepperRow(i, "##meter-keep", n, l, config.MeterKeep.ToString(System.Globalization.CultureInfo.InvariantCulture), config.MeterKeep > 5, config.MeterKeep < 50, pad: 10);
                if (keep != 0)
                {
                    config.MeterKeep = Math.Clamp(config.MeterKeep + keep * 5, 5, 50);
                    config.Save();
                }
                break;
            }
            case SettingId.MeterPace:
            {
                var pace = SegmentRow(i, "##meter-pace", n, l, PaceItems, (int)config.MeterPace);
                if (pace != (int)config.MeterPace)
                {
                    config.MeterPace = (PaceAgainst)pace;
                    config.Save();
                }
                break;
            }
            case SettingId.MeterDecorations:
                if (SwitchRow(i, "##meter-decor", n, l, config.MeterDecorations,
                        "Each theme's flourishes, in both meters and the opened-up window. Off, the theme keeps its colours, fonts and frame without them.", pad: 10))
                {
                    config.MeterDecorations = !config.MeterDecorations;
                    config.Save();
                }
                break;
            case SettingId.MeterWhole:
                if (SwitchRow(i, "##meter-whole", n, l, config.MeterWholeNumbers,
                        "Three figures (27.8k, 9.05k, 165k) keep still at every snapshot and read at a glance; whole numbers widen the column a little. The normal meter always writes them whole.",
                        pad: 10, off: !config.MeterCompact, why: config.MeterCompact ? null : "Compact only: Detailed always writes them whole."))
                {
                    config.MeterWholeNumbers = !config.MeterWholeNumbers;
                    config.Save();
                }
                break;
            // The meter always keeps pets in their owners' rows and the limit break in its own, as ACT and FF Logs do.
            case SettingId.MeterPets:
                SwitchRow(i, "##meter-pets", n, l, true, "A pet's or summon's hits and heals count for whoever it belongs to.", pad: 10, off: true, why: "Always, as ACT counts them.");
                break;
            case SettingId.MeterLbRow:
                SwitchRow(i, "##meter-lb", n, l, true, "The limit break isn't anyone's damage: it has its own row, and counts in the party's number.", pad: 10, off: true,
                    why: "Always, as ACT and FF Logs keep it.");
                break;
            case SettingId.MeterLock:
                if (SwitchRow(i, "##meter-lock", n, l, config.MeterLocked,
                        "Locked, the meter has no title bar and can't be moved (nor the normal meter sized by its edges); its menu, tabs and picker still answer the mouse. Compact is "
                        + "never sized by hand: its width is its theme's and its height its rows'.", pad: 10))
                {
                    config.MeterLocked = !config.MeterLocked;
                    config.Save();
                }
                break;
            case SettingId.MeterClickThrough:
                if (SwitchRow(i, "##meter-through", n, l, config.MeterClickThrough,
                        "The meter doesn't take the mouse at all while it's locked: /raidcodex meter unlock, or this, gives it back.", sub: sub, pad: 10, off: !config.MeterLocked))
                {
                    config.MeterClickThrough = !config.MeterClickThrough;
                    config.Save();
                }
                break;
            // ACT's numbers (IINACT over OverlayPlugin's local connection) aren't read by this version.
            case SettingId.MeterAct:
                SwitchRow(i, "##meter-act", n, l, false, null, pad: 10, off: true, why: "Not in this version yet.");
                break;
            case SettingId.MeterClear:
            {
                var meter = Meter?.Invoke();
                if (ButtonRow(i, "##meter-clear", n, meter is { } m ? Words.Count(m.Kept.Count, "fight") + " kept for the picker" : "None yet", "Clear", Btn.Danger,
                        meter == null || meter.Kept.Count == 0, null, "Forget every fight the meter kept this session (the picker's pulls and pace's best).", pad: 10))
                    meter?.Clear();
                break;
            }

            // ---- The plan window.
            case SettingId.ShowPlan:
                if (SwitchRow(i, "##plan-show", n, l, config.ShowPlanInDuties,
                        "In a duty you've made a plan for on the site, its window opens: the step, its board and its notes, moving along with the pull. /raidcodex plan shows it anywhere."))
                {
                    config.ShowPlanInDuties = !config.ShowPlanInDuties;
                    config.Save();
                }
                break;
            case SettingId.PlanPick:
                PlanPickRow(i, e);
                break;
            case SettingId.PlanWindow:
                if (ButtonRow(i, "##plan-window", n, plans.Plan is { } p ? p.Name : l, PlanWindowOpen?.Invoke() == true ? "Hide" : "Show", Btn.Outline))
                    TogglePlanWindow?.Invoke();
                break;
            case SettingId.SayInChat:
                if (SwitchRow(i, "##plan-callouts", n, l, config.Callouts,
                        "Five seconds before each step's mechanic, its name and its callout (what a raid lead would say) are said in chat, with a soft sound. Once a step, each pull."))
                {
                    config.Callouts = !config.Callouts;
                    config.Save();
                }
                break;
            case SettingId.ShareStep:
                if (SwitchRow(i, "##plan-share", n, l, config.ShareStep,
                        "Each time the plan window's step changes, the site hears which plan and step, so Present on kokos-tools.com (on your stream, or a second screen) can follow the "
                        + "fight. Only the plan and the step are sent.", off: !Linked, linkFirst: !Linked))
                {
                    config.ShareStep = !config.ShareStep;
                    config.Save();
                }
                break;
            case SettingId.SendRoster:
            {
                var note = found && rosterNote is { } r && DateTime.UtcNow - r.At < TimeSpan.FromMinutes(1) ? r.Text : l;
                if (ButtonRow(i, "##roster", n, note, uploads.RosterSending ? "Sending…" : "Send", Btn.Outline, !Linked || uploads.RosterSending, null,
                        "Your party's names go to your roster on the site, each in their spot (MT, OT, H1…), so your plans show who stands where. It replaces the roster there. Only "
                        + "when you press this.", off: !Linked, linkFirst: !Linked))
                    uploads.SendRoster((text, ok) => rosterNote = (text, ok, DateTime.UtcNow));
                break;
            }
            case SettingId.PlanLock:
                if (SwitchRow(i, "##plan-lock", n, l, config.PlanWindowLocked,
                        "Locked, the plan window can't be moved or resized (Dalamud's own moving and resizing are off too), and clicking it doesn't bring it to the front."))
                {
                    config.PlanWindowLocked = !config.PlanWindowLocked;
                    config.Save();
                }
                break;

            // ---- The on-screen callouts.
            case SettingId.CalloutShow:
                if (SwitchRow(i, "##co-show", n, l, config.CalloutOverlay.Show,
                        "While your plan follows a pull, its next steps' callouts sit over the fight, the next one big, each counting down to when it goes off (the plan's own times, kept "
                        + "to the fight by its cues).", pad: 10))
                {
                    config.CalloutOverlay.Show = !config.CalloutOverlay.Show;
                    config.Save();
                }
                break;
            case SettingId.CalloutAhead:
            {
                var ahead = StepperRow(i, "##co-ahead", n, l, $"{config.CalloutAhead} s", config.CalloutAhead > 5, config.CalloutAhead < 60, pad: 10, sub: sub);
                if (ahead != 0)
                {
                    config.CalloutAhead = Math.Clamp(config.CalloutAhead + ahead * 5, 5, 60);
                    config.Save();
                }
                break;
            }
            case SettingId.CalloutCount:
            {
                var count = StepperRow(i, "##co-count", n, l, config.CalloutCount.ToString(System.Globalization.CultureInfo.InvariantCulture), config.CalloutCount > 1,
                    config.CalloutCount < 5, pad: 10, sub: sub);
                if (count != 0)
                {
                    config.CalloutCount = Math.Clamp(config.CalloutCount + count, 1, 5);
                    config.Save();
                }
                break;
            }
            case SettingId.CalloutSound:
                if (SwitchRow(i, "##co-sound", n, l, config.CalloutSound,
                        "A few seconds before each step goes off, the game plays one of its own sound effects (the ones a macro's <se.1> to <se.16> play), once a step, each pull. It "
                        + "follows your game's sound settings.", sub: sub, pad: 10))
                {
                    config.CalloutSound = !config.CalloutSound;
                    config.Save();
                }
                break;
            case SettingId.CalloutWhich:
                SoundRow(i, n, l, sub);
                break;
            case SettingId.CalloutLead:
            {
                var lead = StepperRow(i, "##co-lead", n, l, $"{config.CalloutSoundLead} s", config.CalloutSoundLead > 1, config.CalloutSoundLead < 10, pad: 10);
                if (lead != 0)
                {
                    config.CalloutSoundLead = Math.Clamp(config.CalloutSoundLead + lead, 1, 10);
                    config.Save();
                }
                break;
            }
            case SettingId.CalloutSample:
            {
                var sampleOn = CalloutsSampleOn?.Invoke() == true;
                if (ButtonRow(i, "##co-sample", n, sampleOn ? "Made-up steps under a Preview tag" : l, sampleOn ? "Stop the sample" : "Show", Btn.Outline,
                        ToggleCalloutsSample == null, null, CalloutSampleTip, icon: sampleOn ? Icon.Square : Icon.Play, pad: 10))
                    ToggleCalloutsSample?.Invoke();
                break;
            }
            case SettingId.CalloutLock:
                LockRow(config.CalloutOverlay, "co", i, n, l, "Locked, the callouts have no title bar and can't be moved or sized.");
                break;
            case SettingId.CalloutClickThrough:
                ClickThroughRow(config.CalloutOverlay, "co", i, n, l, sub);
                break;
            case SettingId.CalloutGrows:
                GrowsRow(config.CalloutOverlay, "co", i, n, l);
                break;
            case SettingId.CalloutCompact:
                CompactRow(config.CalloutOverlay, "co", i, n, l);
                break;
            case SettingId.CalloutBackground:
                BackgroundRow(config.CalloutOverlay, "co", i, n, l);
                break;

            // ---- The party cooldowns.
            case SettingId.CdShow:
                if (SwitchRow(i, "##cd-show", n, l, config.Cooldowns.Show,
                        "Small rows of your party's raid buffs, mitigation and the rest you pick below, each with the time until it's ready: yours from the game's own timers, the "
                        + "others' worked out from when they used them.", pad: 10))
                {
                    config.Cooldowns.Show = !config.Cooldowns.Show;
                    config.Save();
                }
                break;
            case SettingId.CdWhere:
            {
                var o = config.Cooldowns;
                var where = SegmentRow(i, "##cd-where", n, l, WhereItems, o.Everywhere ? 1 : 0, sub, !o.Show);
                if (where != (o.Everywhere ? 1 : 0))
                {
                    o.Everywhere = where == 1;
                    config.Save();
                }
                break;
            }
            case SettingId.CdMine:
                if (SwitchRow(i, "##cd-mine", n, l, config.CooldownsMine,
                        "Your own cooldowns are read from the game's timers (as your hotbars show them). Off, only the rest of the party's are listed.", pad: 10))
                {
                    config.CooldownsMine = !config.CooldownsMine;
                    config.Save();
                }
                break;
            case SettingId.CdHideReady:
                if (SwitchRow(i, "##cd-hide", n, l, config.CooldownsHideReady,
                        "Off, every tracked cooldown has its row (\"Ready\" in green when it is). On, a row comes when it's used and goes when it's back.", pad: 10))
                {
                    config.CooldownsHideReady = !config.CooldownsHideReady;
                    config.Save();
                }
                break;
            case SettingId.CdGauge:
                if (SwitchRow(i, "##cd-gauge", n, l, config.CooldownsGauge,
                        "The party's limit break bars as the tracker's last row, when the party has a gauge (it's read with the meter's numbers).", pad: 10))
                {
                    config.CooldownsGauge = !config.CooldownsGauge;
                    config.Save();
                }
                break;
            case SettingId.CdSample:
            {
                var sampleOn = CooldownsSampleOn?.Invoke() == true;
                if (ButtonRow(i, "##cd-sample", n, sampleOn ? "A made-up party under a Preview tag" : l, sampleOn ? "Stop the sample" : "Show a sample", Btn.Outline,
                        ToggleCooldownsSample == null, null,
                        "A made-up party's cooldowns recharging and coming back, with the ones you've picked: to place and size the tracker without a party.", pad: 10))
                    ToggleCooldownsSample?.Invoke();
                break;
            }
            case SettingId.CdLock:
                LockRow(config.Cooldowns, "cd", i, n, l, "Locked, the tracker has no title bar and can't be moved or sized.");
                break;
            case SettingId.CdClickThrough:
                ClickThroughRow(config.Cooldowns, "cd", i, n, l, sub);
                break;
            case SettingId.CdGrows:
                GrowsRow(config.Cooldowns, "cd", i, n, l);
                break;
            case SettingId.CdCompact:
                CompactRow(config.Cooldowns, "cd", i, n, l);
                break;
            case SettingId.CdBackground:
                BackgroundRow(config.Cooldowns, "cd", i, n, l);
                break;

            // ---- The rest.
            case SettingId.WindowFit:
                if (SwitchRow(i, "##win-fit", n, l, config.WindowSize == WindowSize.Fit,
                        "On, the windows grow with a screen taller than 1440 pixels, as much taller as it is: 150% on 4K, 200% at 5K, unless Dalamud's own scale (its Global Font Scale) "
                        + "already makes them that big. Off, they're the size you pick under it. Their title bars stay Dalamud's own size."))
                {
                    config.WindowSize = config.WindowSize == WindowSize.Fit ? WindowSize.Clamp((int)MathF.Round(UiScale.Wanted * 100)) : WindowSize.Fit;
                    config.Save();
                }
                break;
            case SettingId.WindowScale:
            {
                var fit = config.WindowSize == WindowSize.Fit;
                var now = fit ? UiScale.Wanted : config.WindowSize / 100f;
                var step = StepperRow(i, "##win-size", n, fit ? "Fitting your screen. Turn that off to pick." : l, WindowSize.Words(now),
                    !fit && config.WindowSize > WindowSize.Smallest, !fit && config.WindowSize < WindowSize.Largest,
                    "Every window, its words, icons and spacing grow or shrink together: the meter, the plan window, the callouts, the cooldowns and this one. 100% is Dalamud's own size "
                    + "(its Global Font Scale, in Dalamud's settings). The fonts are built again at the new size, which takes a moment.", sub: sub);
                if (step != 0)
                {
                    config.WindowSize = WindowSize.Stepped(config.WindowSize, step, now);
                    config.Save();
                }
                break;
            }
            case SettingId.Arrange:
            {
                var arranging = Arranging?.Invoke() == true;
                if (ButtonRow(i, "##arrange", n, l, arranging ? "Arranging…" : "Arrange", Btn.Outline, StartArrange == null || arranging, null, null, icon: Icon.Move))
                    StartArrange?.Invoke();
                break;
            }
            case SettingId.Account:
                if (found) GoRow(i, e);
                else DrawAccountRow(i);
                break;
            case SettingId.Codex:
                if (ButtonRow(i, "##codex", n, l, "Open", Btn.Outline, false, null, "kokos-tools.com/raid-codex", icon: Icon.ExternalLink))
                    Dalamud.Utility.Util.OpenLink(SiteLink.CodexUrl);
                break;
            case SettingId.Diagnostics:
                if (SwitchRow(i, "##diagnostics", n, l, recorder.Diagnostics.On,
                        "While on, the raw numbers behind the readings not yet checked in the game (a cast stopped, a hit's bonus %, potions, raises, Weakness, the enmity list, the limit "
                        + "break gauge, head signs, your recasts, DoT and HoT ticks) go to diagnostics-<date>.log in the recordings folder. It holds character names. Off, nothing is written. It stays as you leave it."))
                {
                    recorder.Diagnostics.On = !recorder.Diagnostics.On;
                    config.Diagnostics = recorder.Diagnostics.On;
                    config.Save();
                }
                break;
            case SettingId.SetupGuide:
                if (ButtonRow(i, "##setup-guide", n, l, "Run the setup guide", Btn.Outline, false, null, null, icon: Icon.Play))
                    Later(ShowSetup);
                break;

            // Rows drawn in a page's own panel: a search's answer opens the page.
            default:
                GoRow(i, e);
                break;
        }
    }

    /// <summary>A row a search finds that's drawn in its page's own panel (the themes, the cooldowns' kinds): Go opens the page.</summary>
    private void GoRow(int i, SettingEntry e)
    {
        if (ButtonRow(i, "##go-" + e.Id, e.Name, e.Line, "Go", Btn.Outline, false, null, null, icon: Icon.ArrowRight))
            GoToPage(e.Page, e.More);
    }

    /// <summary>Which sound: the stepper (stepping it plays it) and Hear it beside it.</summary>
    private void SoundRow(int i, string name, string line, bool sub)
    {
        var value = $"<se.{config.CalloutSoundId}>";
        var sw = Kit.StepperWidth(value);
        const string hear = "Hear it";
        var hw = Kit.ButtonWidth(hear, BtnSize.Sm, Icon.Volume);
        // Where the row is narrow (beside the callouts' preview), Hear it is its icon alone, so the name keeps its room.
        var iconOnly = Kit.Width - S(sub ? 34 : 16) - S(16) - S(14) - (sw + S(6) + hw) < S(120);
        if (iconOnly) hw = S(HSm);
        var delta = 0;
        SetRow(i, name, line, sw + S(6) + hw, S(H), p =>
        {
            delta = Kit.Stepper("##co-which", p, value, config.CalloutSoundId > 1, config.CalloutSoundId < 16);
            var hp = new Vector2(p.X + sw + S(6), p.Y + (S(H) - S(HSm)) / 2);
            var pressed = iconOnly
                ? Kit.IconButton("##co-hear", hp, Icon.Volume, "Hear it: plays it now", Btn.Outline, BtnSize.Sm, PlaySound == null)
                : Kit.Button("##co-hear", hp, hear, Btn.Outline, BtnSize.Sm, Icon.Volume, 0, PlaySound == null, "Plays it now");
            if (pressed) PlaySound?.Invoke(config.CalloutSoundId);
        }, "The game's numbering, as <se.1> to <se.16> in a macro. Stepping it plays it.", sub: sub, pad: 10);
        if (delta == 0) return;
        config.CalloutSoundId = Math.Clamp(config.CalloutSoundId + delta, 1, 16);
        config.Save();
        PlaySound?.Invoke(config.CalloutSoundId);
    }

    /// <summary>
    /// Plan for this duty: the plan shown for the duty you're in (a button with its name), opening a menu of your plans
    /// for the duty (the one shown ticked) and Read them again. Not in a duty, or with none for it, it says so.
    /// </summary>
    private void PlanPickRow(int i, SettingEntry e)
    {
        var inDuty = plans.Content != 0;
        var any = inDuty && plans.ForDuty.Any();
        var label = plans.ForThisDuty && plans.Plan is { } shown ? Txt.Fit(shown.Name, Face.SansMedium, 13, S(150)) : "Pick a plan";
        var line = !inDuty ? "In a duty, pick one of your plans for it here."
            : plans.ListLoading && !any ? "Reading your plans…"
            : !any ? $"No plans for {plans.DutyName ?? "this duty"} on the site yet."
            : e.Line;
        if (ButtonRow(i, "##plan-pick", e.Name, line, label, Btn.Outline, !Linked || !any, null,
                "The plan the window and the callouts follow in this duty. Picked here, it's remembered for the duty, as picking it in the plan window's list is.",
                off: !Linked, linkFirst: !Linked, icon: Icon.ChevronDown))
        {
            ImGui.OpenPopup(PlanPickMenu);
        }
        ImGui.SetNextWindowSizeConstraints(new Vector2(S(220), 0), new Vector2(S(320), S(420)));
        if (!Kit.BeginMenu(PlanPickMenu)) return;
        try
        {
            var w = S(320) - S(11.2f);
            var dl = ImGui.GetWindowDrawList();
            Kit.MenuEyebrow($"This duty · {plans.DutyName ?? "here"}", w);
            foreach (var p in plans.ForDuty)
            {
                var (clicked, _, min, max) = Kit.MenuRow("##set-plan-" + p.Id, w, p.Name);
                var (slot, _) = Kit.MenuRowParts(min, max);
                if (plans.Plan?.Id == p.Id) Kit.Tick(dl, slot + new Vector2(S(10)), 20);
                else Icons.Draw(dl, Icon.Map, slot + new Vector2((S(20) - S(Kit.IconPx)) / 2), S(Kit.IconPx), Palette.Faint);
                if (clicked)
                {
                    plans.Open(p.Id, true);
                    ImGui.CloseCurrentPopup();
                }
            }
            Kit.MenuRule(w);
            var reading = plans.ListLoading;
            var (again, hovered, amin, amax) = Kit.MenuRow("##set-plan-refresh", w, reading ? "Reading your plans…" : "Read them again");
            var (aslot, _) = Kit.MenuRowParts(amin, amax);
            Icons.Draw(dl, Icon.RefreshCw, aslot + new Vector2((S(20) - S(Kit.IconPx)) / 2), S(Kit.IconPx), hovered ? Palette.White : Palette.Muted);
            if (again) plans.RefreshList(true);
        }
        finally
        {
            Kit.EndMenu();
        }
    }

    /// <summary>Upload my older pulls' line: what's waiting, why it's held up, or why it stopped.</summary>
    private string UploadStatus(int older)
    {
        if (logs.Stopped is { } stopped) return "Stopped: " + stopped;
        var waiting = logs.WaitingCount;
        if (logs.Held is { } held && waiting > 0)
        {
            var until = logs.HeldUntil;
            var left = until > DateTime.UtcNow ? until - DateTime.UtcNow : TimeSpan.Zero;
            return $"{Words.Count(waiting, "pull")} waiting. {held.TrimEnd('.')}{(left >= TimeSpan.FromMinutes(1) ? $": again in {(int)Math.Ceiling(left.TotalMinutes)} min" : "")}.";
        }
        if (waiting > 0) return logs.Uploading ? $"Uploading: {Words.Count(waiting, "pull")} to go" : $"{Words.Count(waiting, "pull")} waiting";
        return older > 0 ? $"{Words.Count(older, "pull")} kept from before" : "Every pull is on the site";
    }

    // ---------------------------------------------------------------------------------------------
    // An overlay's own rows (the callouts', the cooldown tracker's).

    private void LockRow(OverlaySettings o, string id, int i, string name, string line, string tip)
    {
        if (!SwitchRow(i, $"##{id}-lock", name, line, o.Locked, tip, pad: 10)) return;
        o.Locked = !o.Locked;
        config.Save();
    }

    private void ClickThroughRow(OverlaySettings o, string id, int i, string name, string line, bool sub)
    {
        if (!SwitchRow(i, $"##{id}-through", name, line, o.ClickThrough,
                "It doesn't take the mouse at all while it's locked: its command with unlock, or this, gives it back.", sub: sub, pad: 10, off: !o.Locked))
            return;
        o.ClickThrough = !o.ClickThrough;
        config.Save();
    }

    private void GrowsRow(OverlaySettings o, string id, int i, string name, string line)
    {
        var grows = SegmentRow(i, $"##{id}-grows", name, line, GrowsItems, (int)o.Grows, tip: GrowsTip);
        if (grows == (int)o.Grows) return;
        o.Grows = (MeterGrows)grows;
        config.Save();
    }

    private void CompactRow(OverlaySettings o, string id, int i, string name, string line)
    {
        if (!SwitchRow(i, $"##{id}-compact", name, line, o.Compact, null, pad: 10)) return;
        o.Compact = !o.Compact;
        config.Save();
    }

    private void BackgroundRow(OverlaySettings o, string id, int i, string name, string line)
    {
        var bg = Math.Clamp(SliderRow(i, $"##{id}-bg", name, line, Math.Clamp(o.Background, 0, 1)), 0, 1);
        if (MathF.Abs(bg - o.Background) <= 0.001f) return;
        o.Background = bg;
        backgroundDirty = true;
    }

    // ---------------------------------------------------------------------------------------------
    // The row.

    private static readonly Run[] SetNote = new Run[1];

    /// <summary>
    /// A setting's row (`.set`: 11px 16px inside, a hairline between, 34 at the left for a sub-row): its
    /// name (550 14px white) and one muted line under it (12px), the control at the right, 14px from
    /// the words. A row that's off dims its words and control to 45%, and with <paramref name="linkFirst"/>
    /// says "Link your account first: Account" (the link goes there), or <paramref name="why"/>. The
    /// longer words are the tooltip over the words.
    /// </summary>
    private void SetRow(int index, string name, string? note, float controlW, float controlH, Action<Vector2> control, string? tip = null, bool off = false,
        bool sub = false, bool linkFirst = false, string? why = null, float pad = 11)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        if (index > 0) Kit.Rule(dl, at.X, at.X + w, at.Y - 0.5f);
        var x = at.X + S(sub ? 34 : 16);
        var wordsW = at.X + w - S(16) - (controlW > 0 ? controlW + S(14) : 0) - x;
        var nameH = S(14 * 1.3f);
        var noteH = 0f;
        if (!string.IsNullOrEmpty(note))
        {
            SetNote[0] = new Run(note, Face.Sans, 12, Palette.Muted);
            noteH = S(2) + Txt.Flow(Vector2.Zero, wordsW, SetNote, 17.4f, out _, draw: false);
        }
        var whyText = linkFirst ? "Link your account first: " : why;
        var whyH = whyText != null ? S(2) + S(17.4f) : 0;
        var wordsH = nameH + noteH + whyH;
        var h = S(pad) * 2 + MathF.Max(wordsH, controlH);
        var top = at.Y + (h - wordsH) / 2;
        var old = Palette.Opacity;
        if (off) Palette.Opacity = old * 0.45f;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(name, Face.SansMedium, 14, wordsW), Face.SansMedium, 14, Palette.White, 14 * 1.3f);
        if (!string.IsNullOrEmpty(note))
        {
            SetNote[0] = new Run(note, Face.Sans, 12, Palette.Muted);
            Txt.Flow(new Vector2(x, top + nameH + S(2)), wordsW, SetNote, 17.4f, out _);
        }
        Palette.Opacity = old;
        if (whyText != null)
        {
            // The reason stays bright (`.set .why`), its link underlined.
            var wy = top + nameH + noteH + S(2);
            var ww = Txt.Line(dl, new Vector2(x, wy), Txt.Fit(whyText, Face.Sans, 12, wordsW), Face.Sans, 12, Palette.Text, 17.4f);
            if (linkFirst && Kit.Link($"##why{name}{index}", new Vector2(x + ww, wy + (S(17.4f) - Txt.Natural(Face.SansMedium, 12)) / 2), "Account", null, 12))
                GoToPage(SettingsTab.Account);
        }
        if (tip != null && Kit.Pointed(new Vector2(x, top), new Vector2(x + wordsW, top + nameH + noteH))) Kit.Tooltip(tip);
        if (controlW > 0)
        {
            if (off) Palette.Opacity = old * 0.45f;
            control(new Vector2(at.X + w - S(16) - controlW, at.Y + (h - controlH) / 2));
            Palette.Opacity = old;
        }
        Kit.Reserve(at, w, h);
    }

    /// <summary>A setting with a switch; true when it's flipped (never while it's off).</summary>
    private bool SwitchRow(int index, string id, string name, string? note, bool on, string? tip = null, bool off = false, bool sub = false, bool linkFirst = false,
        string? why = null, float pad = 11)
    {
        var flipped = false;
        var size = Kit.SwitchSize;
        SetRow(index, name, note, size.X, size.Y, p => flipped = Kit.Switch(id, p, on, off), tip, off, sub, linkFirst, why, pad);
        return flipped && !off;
    }

    /// <summary>A setting with a − value + stepper (40 tall, the value in mono); returns −1, 0 or +1.</summary>
    private int StepperRow(int index, string id, string name, string? note, string value, bool canDown, bool canUp, string? tip = null, float pad = 11, bool sub = false)
    {
        var delta = 0;
        SetRow(index, name, note, Kit.StepperWidth(value), S(H), p => delta = Kit.Stepper(id, p, value, canDown, canUp), tip, sub: sub, pad: pad);
        return delta;
    }

    /// <summary>A setting with a dense segmented control; returns the item picked.</summary>
    private int SegmentRow(int index, string id, string name, string? note, string[] items, int selected, bool sub = false, bool off = false, string? tip = null)
    {
        var picked = selected;
        SetRow(index, name, note, Kit.SegmentedWidth(items), S(HSm), p => picked = Kit.Segmented(id, p, items, selected, true, off), tip, off, sub, pad: 10);
        return off ? selected : picked;
    }

    /// <summary>A setting with a slider and its value in mono (150 wide together); returns the value.</summary>
    private float SliderRow(int index, string id, string name, string? note, float value)
    {
        var result = value;
        var vw = Txt.Width("100%", Face.Mono, 13);
        SetRow(index, name, note, S(150), S(20), p =>
        {
            var sw = S(150) - vw - S(12);
            result = Kit.Slider(id, p, sw, value);
            Txt.LineRight(Kit.Dl, p.X + S(150), p.Y, Format.PercentWhole(result), Face.Mono, 13, Palette.White, 20);
        }, pad: 10);
        return result;
    }

    /// <summary>A setting with a dense button on the right; true when it's pressed.</summary>
    private bool ButtonRow(int index, string id, string name, string? note, string label, Btn style = Btn.Outline, bool disabled = false,
        string? buttonTip = null, string? tip = null, bool off = false, bool linkFirst = false, Icon? icon = null, float pad = 11, bool sub = false, string? why = null)
    {
        var pressed = false;
        SetRow(index, name, note, Kit.ButtonWidth(label, BtnSize.Sm, icon, style), S(HSm),
            p => pressed = Kit.Button(id, p, label, style, BtnSize.Sm, icon, 0, disabled, buttonTip), tip, off, sub, linkFirst, why, pad);
        return pressed;
    }
}
