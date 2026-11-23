using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RaidPlanner.UI
{
    /// <summary>
    /// Settings' pages (0.8.0: a sidebar, in this order): the main parts' switches, recording and uploads, the meter,
    /// the plan and its callouts, the party cooldowns, where the windows sit, the account, and the rest rarely changed.
    /// </summary>
    public enum SettingsTab
    {
        /// <summary>0.8.0: the main parts' switches, as cards.</summary>
        Features,
        Recording,
        Meter,

        /// <summary>The plan window and the on-screen callouts.</summary>
        Plans,

        /// <summary>The party cooldown tracker (0.4.0).</summary>
        Cooldowns,

        /// <summary>0.8.0: where the overlays sit, and Arrange windows.</summary>
        Windows,
        Account,

        /// <summary>0.8.0: diagnostics, the recordings folder and the rest rarely changed.</summary>
        Advanced,
    }
}

namespace RaidPlanner.UI.Model
{
    /// <summary>Every row Settings draws (a setting, or a button that does something), by what it is.</summary>
    public enum SettingId
    {
        // Recording and uploads.
        AutoRecord,
        RecordAllDuties,
        RecordParty,
        Keep,
        UploadLogs,
        UploadOlder,
        WhoSees,
        TrackProgress,
        SmoothReplays,
        ShowInBar,
        RecordingsFolder,

        // The meter.
        ShowMeter,
        MeterWhere,
        MeterHide,

        /// <summary>0.8.2: the meter stays open between pulls in duties.</summary>
        MeterOpenInDuties,
        MeterSize,
        MeterTheme,
        MeterRoles,
        MeterSample,
        MeterRank,
        MeterNames,
        MeterGrows,
        MeterHead,
        MeterOrder,
        MeterGauge,
        MeterBackground,
        MeterEndAfter,
        MeterKeep,
        MeterPace,
        MeterDecorations,
        MeterWhole,
        MeterPets,
        MeterLbRow,
        MeterLock,
        MeterClickThrough,
        MeterAct,
        MeterClear,

        // The plan window.
        ShowPlan,
        PlanPick,
        PlanWindow,
        SayInChat,
        ShareStep,
        SendRoster,
        PlanLock,

        // The on-screen callouts.
        CalloutShow,
        CalloutAhead,
        CalloutCount,
        CalloutSound,
        CalloutWhich,
        CalloutLead,
        CalloutSample,
        CalloutLock,
        CalloutClickThrough,
        CalloutGrows,
        CalloutCompact,
        CalloutBackground,

        // The party cooldowns.
        CdShow,
        CdWhere,
        CdMine,
        CdHideReady,
        CdGauge,
        CdSample,
        CdLock,
        CdClickThrough,
        CdGrows,
        CdCompact,
        CdBackground,

        /// <summary>Which cooldowns are tracked (each kind's section on the page; each found by name in a search).</summary>
        CdPicks,

        // The rest.

        /// <summary>0.8.1: the windows fit the screen (Settings → Windows → Size).</summary>
        WindowFit,

        /// <summary>0.8.1: the windows' size picked (80–200%), while they don't fit the screen.</summary>
        WindowScale,
        Arrange,
        Account,
        Codex,
        Diagnostics,
        SetupGuide,
    }

    /// <summary>
    /// One row as a page shows it: its page and section, its name and one line (what a search reads), its words in a
    /// page's "N more" line (<paramref name="Short"/>), whether it's a sub-row, folded under the more row, a copy of a
    /// row whose home is another page (<paramref name="Echo"/>: drawn, never a search's answer), a button rather
    /// than a setting (<paramref name="Action"/>: nothing to reset), or left alone by its page's reset (<paramref name="Keep"/>).
    /// </summary>
    public sealed record SettingEntry(SettingId Id, SettingsTab Page, string Section, string Name, string Line, string Short = "", bool Sub = false, bool More = false,
        bool Echo = false, bool Action = false, bool Keep = false);

    /// <summary>A section of a page: its eyebrow and its rows, in order.</summary>
    public sealed record SettingSection(string Title, IReadOnlyList<SettingEntry> Rows);

    /// <summary>
    /// Settings' pages as data (0.8.0): each page's title, its line, its sections of rows and the rows folded under its
    /// "N more" row; the search over every row's name and line; and which rows a page's Reset this page puts back.
    /// The window draws a row by its <see cref="SettingId"/>, so a search's answer works in place.
    /// </summary>
    public static class SettingsIndex
    {
        /// <summary>The pages, as the sidebar lists them.</summary>
        public static readonly IReadOnlyList<SettingsTab> Pages = (SettingsTab[])Enum.GetValues(typeof(SettingsTab));

        public static string Title(SettingsTab t) => t switch
        {
            SettingsTab.Features => "Features",
            SettingsTab.Recording => "Recording and uploads",
            SettingsTab.Meter => "Damage meter",
            SettingsTab.Plans => "Plan and callouts",
            SettingsTab.Cooldowns => "Party cooldowns",
            SettingsTab.Windows => "Windows",
            SettingsTab.Account => "Account and privacy",
            _ => "Advanced",
        };

        /// <summary>The one line under a page's title, saying what it's for.</summary>
        public static string Lead(SettingsTab t) => t switch
        {
            SettingsTab.Features => "Turn each part of the plugin on or off. Their own settings are a click away.",
            SettingsTab.Recording => "What's kept from each pull, and what goes to your Raid Codex.",
            SettingsTab.Meter => "What the meter shows and how it looks. Where it sits:",
            SettingsTab.Plans => "Your static's plan during the fight: the board, and the next steps called out on screen. Where they sit:",
            SettingsTab.Cooldowns => "Your party's raid buffs and mitigation, and when each is ready again. Where it sits:",
            SettingsTab.Windows => "How big the windows are, where each sits, and whether it can be moved or clicked through.",
            SettingsTab.Account => "Your Raid Codex account, and what the plugin shares with the site.",
            _ => "Rarely needed: for checking what the plugin reads, and a few odd ends.",
        };

        /// <summary>A page whose line ends pointing at another (the link's words and where it goes), else null.</summary>
        public static SettingsTab? LeadLink(SettingsTab t) => t is SettingsTab.Meter or SettingsTab.Plans or SettingsTab.Cooldowns ? SettingsTab.Windows : null;

        /// <summary>The page a main part's settings are on (Settings → Features' "Settings ›").</summary>
        public static SettingsTab PageOf(Feature f) => f switch
        {
            Feature.Meter => SettingsTab.Meter,
            Feature.Plan => SettingsTab.Plans,
            Feature.Cooldowns => SettingsTab.Cooldowns,
            _ => SettingsTab.Recording,
        };

        private const SettingsTab Rec = SettingsTab.Recording;
        private const SettingsTab Met = SettingsTab.Meter;
        private const SettingsTab Pla = SettingsTab.Plans;
        private const SettingsTab Cds = SettingsTab.Cooldowns;
        private const SettingsTab Win = SettingsTab.Windows;
        private const SettingsTab Acc = SettingsTab.Account;
        private const SettingsTab Adv = SettingsTab.Advanced;

        /// <summary>Every row of every page, in the pages' order and each page's own.</summary>
        public static readonly IReadOnlyList<SettingEntry> All = new SettingEntry[]
        {
            // Recording and uploads.
            new(SettingId.AutoRecord, Rec, "Recording", "Record my pulls", "Every pull, by itself, from the pull until the fight ends."),
            new(SettingId.RecordAllDuties, Rec, "Recording", "Which duties", "Raids and trials, or every duty's pulls (dungeons too).", Sub: true),
            new(SettingId.RecordParty, Rec, "Recording", "Record where the party stands", "Their places come along into the plan and the replay.", Sub: true),
            new(SettingId.Keep, Rec, "Recording", "Keep the last", "Past that, the oldest already on the site go first.", Sub: true),
            // Kept by Reset this page: on would upload pulls, and uploads are public on the site (as on FF Logs).
            new(SettingId.UploadLogs, Rec, "Uploads", "Upload my logs", "Each pull goes to your Raid Codex as it ends.", Keep: true),
            new(SettingId.UploadOlder, Rec, "Uploads", "Upload my older pulls", "The pulls kept here from before, the oldest first.", Sub: true, Action: true),
            new(SettingId.WhoSees, Rec, "Uploads", "Who sees my logs", "Public on the site, with names. Make yours private there.", Sub: true, Action: true),
            new(SettingId.TrackProgress, Rec, "Uploads", "Track my progress", "How far each pull got, for Progress on the site."),
            new(SettingId.SmoothReplays, Rec, "More", "Smoother replays", "Places four times a second, not two: bigger files.", "smoother replays", More: true),
            new(SettingId.ShowInBar, Rec, "More", "Show it in the server info bar", "“Rec 2:31” while a pull is recorded.", "the server info bar", More: true),
            new(SettingId.RecordingsFolder, Rec, "More", "Recordings folder", "Where the pulls are kept.", "recordings folder", More: true, Action: true),

            // The meter.
            new(SettingId.ShowMeter, Met, "When it shows", "Show the meter", "Its own window, over the game. /raidcodex meter too."),
            new(SettingId.MeterWhere, Met, "When it shows", "Where", "Everywhere includes the open world and dungeons.", Sub: true),
            new(SettingId.MeterOpenInDuties, Met, "When it shows", "Keep it open in duties", "Between pulls it shows the last one. Off, it folds to a line.", Sub: true),
            new(SettingId.MeterHide, Met, "When it shows", "Hide it out of combat", "Where it isn't kept open. Otherwise it folds to a line between pulls.", Sub: true),
            new(SettingId.MeterSize, Met, "How it looks", "Size", "Compact: the top eight on plates. Detailed: tabs, pace and columns."),
            new(SettingId.MeterTheme, Met, "How it looks", "Theme", "Both meters' and the opened-up window's look."),
            new(SettingId.MeterRoles, Met, "How it looks", "Role colours", "The bars' and job icons' colours: the theme's, or the game's."),
            new(SettingId.MeterSample, Met, "How it looks", "Play a sample fight", "A made-up pull with its clock running, to place the meter.", Action: true),
            new(SettingId.MeterRank, Met, "The numbers", "What it ranks by", "ENCDPS: over the fight's whole time, as FF Logs ranks. DPS: over each player's own time."),
            new(SettingId.MeterNames, Met, "The numbers", "Show", "Names, or only jobs when you share screenshots or stream."),
            new(SettingId.MeterGrows, Met, "More", "Grows", "The edge that stays put as it grows.", "grows up or down", More: true, Echo: true),
            new(SettingId.MeterHead, Met, "More", "Head", "The clock, tabs, pulls and lock: at the top or the bottom.", "head", More: true),
            new(SettingId.MeterOrder, Met, "More", "Order", "Reading down the list.", "order", More: true),
            new(SettingId.MeterGauge, Met, "More", "Limit break gauge", "Its bars, while a fight is on.", "limit break gauge", More: true),
            new(SettingId.MeterBackground, Met, "More", "Background", "Rows stay solid.", "background", More: true, Echo: true),
            new(SettingId.MeterEndAfter, Met, "More", "A fight ends after", "Nobody in combat for this long.", "when a fight ends", More: true),
            new(SettingId.MeterKeep, Met, "More", "Keep the last", "Fights to pick from and pace against.", "kept fights", More: true),
            new(SettingId.MeterPace, Met, "More", "Pace against", "Damage tab only, from 10 s into a phase.", "pace against", More: true),
            new(SettingId.MeterDecorations, Met, "More", "Decorations", "Sparkles, scanlines, the horizon, the glitch, the ribbon.", "decorations", More: true),
            new(SettingId.MeterWhole, Met, "More", "Whole numbers", "27,840 instead of 27.8k, in compact.", "whole numbers", More: true),
            new(SettingId.MeterPets, Met, "More", "Pets in their owners' rows", "Fairies, demi-summons, the automaton, Living Shadow.", "pets", More: true),
            new(SettingId.MeterLbRow, Met, "More", "Limit break on its own row", "It isn't anyone's damage, so it has its own.", "the limit break's row", More: true),

            // The plan and the callouts.
            new(SettingId.ShowPlan, Pla, "Plan window", "Show my plan in duties", "Its window opens in a duty you have a plan for."),
            new(SettingId.PlanPick, Pla, "Plan window", "Plan for this duty", "Picked on the site, or here from your plans for it.", Action: true),
            new(SettingId.PlanWindow, Pla, "Plan window", "The plan window", "Your plan, anywhere on screen. /raidcodex plan too.", Action: true),
            new(SettingId.SayInChat, Pla, "Plan window", "Say each step in chat", "Its callout, five seconds before, with a soft sound."),
            new(SettingId.CalloutShow, Pla, "On-screen callouts", "Show the callouts", "Your plan's next steps, counting down. /raidcodex callouts too."),
            new(SettingId.CalloutAhead, Pla, "On-screen callouts", "How far ahead", "A step shows this long before it goes off.", Sub: true),
            new(SettingId.CalloutCount, Pla, "On-screen callouts", "How many at once", "The soonest first.", Sub: true),
            new(SettingId.CalloutSound, Pla, "On-screen callouts", "A sound before each step", "One of the game's own. It follows your game's sound settings.", Sub: true),
            new(SettingId.CalloutWhich, Pla, "On-screen callouts", "Which sound", "As <se.N> in a macro.", Sub: true),
            new(SettingId.CalloutSample, Pla, "On-screen callouts", "Show sample callouts", "Made-up steps counting down, to place them.", Action: true),
            new(SettingId.ShareStep, Pla, "With your static", "Share my step with the site", "Present on the site can follow along."),
            new(SettingId.SendRoster, Pla, "With your static", "Send my party to my roster", "Their names, by spot, to your Raid Codex.", Action: true),
            new(SettingId.CalloutLead, Pla, "More", "How early the sound plays", "Before the step goes off.", "how early the sound plays", More: true),
            new(SettingId.CalloutCompact, Pla, "More", "Smaller callouts", "Smaller words and rows.", "smaller callouts", More: true),
            new(SettingId.CalloutBackground, Pla, "More", "The callouts' background", "Their words stay solid.", "the callouts' background", More: true, Echo: true),

            // The party cooldowns.
            new(SettingId.CdShow, Cds, "What shows", "Show the tracker", "The party's cooldowns over the fight. /raidcodex cooldowns too."),
            new(SettingId.CdWhere, Cds, "What shows", "Where", "Everywhere includes the open world and dungeons.", Sub: true),
            new(SettingId.CdMine, Cds, "What shows", "Yours too", "From the game's own timers, exactly."),
            new(SettingId.CdSample, Cds, "What shows", "See it", "A made-up party's cooldowns, to place it.", Action: true),
            new(SettingId.CdPicks, Cds, "Cooldowns", "The cooldowns tracked", "Each kind's, with a switch each."),
            new(SettingId.CdHideReady, Cds, "More", "Hide those ready", "Only what's recharging is listed.", "hide those ready", More: true),
            new(SettingId.CdGauge, Cds, "More", "Limit break gauge", "Its bars as the last row.", "limit break gauge", More: true),
            new(SettingId.CdCompact, Cds, "More", "Smaller rows", "Smaller rows and words.", "smaller rows", More: true),

            // Windows.
            new(SettingId.WindowFit, Win, "Size", "Fit to my screen", "Bigger windows on a big screen: 150% on 4K."),
            new(SettingId.WindowScale, Win, "Size", "Window size", "Every window, its words and icons, beside Dalamud's own scale.", "window size", Sub: true),
            new(SettingId.Arrange, Win, "Arrange", "Arrange windows", "Shows every window at once, to drag each into place.", Action: true),
            new(SettingId.ShowMeter, Win, "Damage meter", "Show the meter", "Its own window, over the game.", Echo: true, Keep: true),
            new(SettingId.MeterLock, Win, "Damage meter", "Lock it where it is", "No title bar, and no moving it by accident."),
            new(SettingId.MeterClickThrough, Win, "Damage meter", "Click-through when locked", "The mouse goes through to the game.", Sub: true),
            new(SettingId.MeterGrows, Win, "Damage meter", "Grows", "The edge that stays put as it grows."),
            new(SettingId.MeterBackground, Win, "Damage meter", "Background", "Rows stay solid."),
            new(SettingId.ShowPlan, Win, "Plan window", "Show my plan in duties", "Its window opens in a duty you have a plan for.", Echo: true, Keep: true),
            new(SettingId.PlanLock, Win, "Plan window", "Lock it where it is", "No moving or resizing it by accident."),
            new(SettingId.PlanWindow, Win, "Plan window", "The plan window", "Your plan, anywhere on screen.", Echo: true, Action: true),
            new(SettingId.CalloutShow, Win, "Callouts", "Show the callouts", "While your plan follows a pull.", Echo: true, Keep: true),
            new(SettingId.CalloutLock, Win, "Callouts", "Lock it where it is", "No title bar, and no moving or sizing it."),
            new(SettingId.CalloutClickThrough, Win, "Callouts", "Click-through when locked", "The mouse goes through to the game.", Sub: true),
            new(SettingId.CalloutGrows, Win, "Callouts", "Grows", "Which edge stays put as it grows."),
            new(SettingId.CalloutBackground, Win, "Callouts", "Background", "Their words stay solid."),
            new(SettingId.CdShow, Win, "Party cooldowns", "Show the tracker", "The party's cooldowns over the fight.", Echo: true, Keep: true),
            new(SettingId.CdLock, Win, "Party cooldowns", "Lock it where it is", "No title bar, and no moving or sizing it."),
            new(SettingId.CdClickThrough, Win, "Party cooldowns", "Click-through when locked", "The mouse goes through to the game.", Sub: true),
            new(SettingId.CdGrows, Win, "Party cooldowns", "Grows", "Which edge stays put as it grows."),
            new(SettingId.CdBackground, Win, "Party cooldowns", "Background", "Its words stay solid."),

            // Account and privacy.
            new(SettingId.Account, Acc, "Your account", "Raid Codex account", "Link it with a token from the site, or unlink it.", Action: true),
            new(SettingId.Codex, Acc, "Your account", "The Raid Codex", "Make plans from your pulls, draw your strats, present them.", Action: true),
            new(SettingId.WhoSees, Acc, "Privacy", "Who sees my logs", "Public on the site, with names. Make yours private there.", Echo: true, Action: true),

            // Advanced.
            new(SettingId.Diagnostics, Adv, "Diagnostics", "Diagnostics", "Writes what it reads from the game to a file, for checking."),
            new(SettingId.RecordingsFolder, Adv, "Diagnostics", "Recordings folder", "Where the pulls and the diagnostics files are kept.", Echo: true, Action: true),
            new(SettingId.MeterAct, Adv, "Damage meter", "Use ACT's numbers", "When IINACT is running, show its numbers instead."),
            new(SettingId.MeterClear, Adv, "Damage meter", "Kept fights", "Forget the fights the meter kept this session.", Action: true),
            new(SettingId.SetupGuide, Adv, "Setup", "The setup guide", "Turn each part on or off again, a step at a time.", Action: true),
        };

        private static readonly Dictionary<SettingsTab, (IReadOnlyList<SettingSection> Sections, IReadOnlyList<SettingEntry> More, string MoreWords)> PageCache = new();

        /// <summary>Every row on a page, in order (its sections', then those under the more row).</summary>
        public static IEnumerable<SettingEntry> On(SettingsTab page) => All.Where(e => e.Page == page);

        /// <summary>A page's sections (not the rows under the more row), each with its rows in order.</summary>
        public static IReadOnlyList<SettingSection> Sections(SettingsTab page) => Of(page).Sections;

        /// <summary>The rows folded under a page's more row (none: no more row).</summary>
        public static IReadOnlyList<SettingEntry> More(SettingsTab page) => Of(page).More;

        /// <summary>The more row's words: "3 more: smoother replays, the server info bar, recordings folder" (empty without one).</summary>
        public static string MoreWords(SettingsTab page) => Of(page).MoreWords;

        /// <summary>The section of a page with this title (null when it hasn't one).</summary>
        public static SettingSection? Section(SettingsTab page, string title) => Sections(page).FirstOrDefault(s => s.Title == title);

        private static (IReadOnlyList<SettingSection> Sections, IReadOnlyList<SettingEntry> More, string MoreWords) Of(SettingsTab page)
        {
            lock (PageCache)
            {
                if (PageCache.TryGetValue(page, out var kept)) return kept;
                var sections = new List<SettingSection>();
                foreach (var group in On(page).Where(e => !e.More).GroupBy(e => e.Section))
                    sections.Add(new SettingSection(group.Key, group.ToList()));
                var more = On(page).Where(e => e.More).ToList();
                return PageCache[page] = (sections, more, Words(more.Select(e => e.Short).ToList()));
            }
        }

        /// <summary>"N more: a, b, c" for the rows folded away (empty for none).</summary>
        public static string Words(IReadOnlyList<string> shorts) =>
            shorts.Count == 0 ? string.Empty : $"{shorts.Count.ToString(CultureInfo.InvariantCulture)} more: {string.Join(", ", shorts)}";

        /// <summary>
        /// The rows Reset this page puts back to the defaults: its settings (not its buttons), copies of another page's
        /// included (the meter's Grows and Background are on both), but not the windows' Show rows (their own pages').
        /// Features and Account have none.
        /// </summary>
        public static IReadOnlyList<SettingId> Resets(SettingsTab page) =>
            On(page).Where(e => !e.Action && !e.Keep).Select(e => e.Id).Distinct().ToList();

        /// <summary>Whether a page has a Reset this page (it has something to reset).</summary>
        public static bool CanReset(SettingsTab page) => Resets(page).Count > 0;

        /// <summary>
        /// A search's answer: the rows (each once, at its home, never a copy) whose name or line has every word typed,
        /// in the pages' order. Nothing for an empty search.
        /// </summary>
        public static IReadOnlyList<SettingEntry> Find(string? query)
        {
            var words = QueryWords(query);
            if (words.Count == 0) return Array.Empty<SettingEntry>();
            return All.Where(e => !e.Echo && Matches(words, e.Name, e.Line, e.Short)).ToList();
        }

        /// <summary>The words of a search, lower-cased and with their marks evened out (empty when there are none).</summary>
        public static IReadOnlyList<string> QueryWords(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
            return Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Does every word appear somewhere in these words (a word's start or inside one; case and quotes don't matter)?</summary>
        public static bool Matches(IReadOnlyList<string> words, params string?[] texts)
        {
            if (words.Count == 0) return false;
            var hay = Fold(string.Join(" ", texts.Where(t => !string.IsNullOrEmpty(t))));
            foreach (var w in words)
                if (!hay.Contains(w, StringComparison.Ordinal))
                    return false;
            return true;
        }

        /// <summary>Lower case, curly quotes straight, accents off, anything else that isn't a letter or digit a space.</summary>
        public static string Fold(string text)
        {
            var d = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                if (c is '’' or '‘' or '\'') continue;
                sb.Append(char.IsLetterOrDigit(c) || c == '<' || c == '>' || c == '.' ? char.ToLowerInvariant(c) : ' ');
            }
            return sb.ToString();
        }
    }

    /// <summary>A main part's status tag on Settings → Features: what it says and in which colour.</summary>
    public enum StatusKind
    {
        /// <summary>Green: on and nothing waiting.</summary>
        Ready,

        /// <summary>Ember: on, with something going on (pulls waiting to upload, a pull being recorded).</summary>
        Busy,

        /// <summary>A hairline: on, saying where or what.</summary>
        Plain,

        /// <summary>Faint: off.</summary>
        Off,

        /// <summary>Amber: it can't do its work (no account, stopped).</summary>
        Problem,
    }

    /// <summary>What the Features cards' tags are worked out from, read from the plugin each frame.</summary>
    public readonly record struct FeatureFacts(bool Linked, bool Recording, int Waiting, bool UploadStopped, bool MeterStopped, bool MeterEverywhere, string? PlanName,
        bool CooldownsEverywhere);

    public static class FeatureStatus
    {
        /// <summary>A part's tag: "Ready", "3 waiting to upload", "Off", "Needs an account", "In duties", "Plan: TOP v3"…</summary>
        public static (string Text, StatusKind Kind) Of(Feature f, bool on, FeatureFacts facts)
        {
            if (Features.NeedsAccount(f) && !facts.Linked) return ("Needs an account", StatusKind.Problem);
            if (!on) return ("Off", StatusKind.Off);
            switch (f)
            {
                case Feature.Record:
                    return facts.Recording ? ("Recording", StatusKind.Busy) : ("Ready", StatusKind.Ready);
                case Feature.Upload:
                    if (facts.UploadStopped) return ("Stopped", StatusKind.Problem);
                    return facts.Waiting > 0 ? ($"{facts.Waiting.ToString(CultureInfo.InvariantCulture)} waiting to upload", StatusKind.Busy) : ("Ready", StatusKind.Ready);
                case Feature.Meter:
                    if (facts.MeterStopped) return ("Stopped", StatusKind.Problem);
                    return (facts.MeterEverywhere ? "Everywhere" : "In duties", StatusKind.Plain);
                case Feature.Plan:
                    return string.IsNullOrEmpty(facts.PlanName) ? ("Ready", StatusKind.Ready) : ("Plan: " + facts.PlanName, StatusKind.Plain);
                case Feature.Cooldowns:
                    return (facts.CooldownsEverywhere ? "Everywhere" : "In duties", StatusKind.Plain);
                default:
                    return ("Ready", StatusKind.Ready);
            }
        }
    }
}
