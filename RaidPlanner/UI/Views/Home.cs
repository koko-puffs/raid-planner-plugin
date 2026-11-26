using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Home (0.8.0, the mock-ups' plug-main "Home · ready" and "Home · recording"), under the tabs. Between pulls: the
/// status card (ready for the next pull, waiting for it, not in a duty, recording off; the duty and the pull's number
/// when known) with a chip per main part, each opening its settings; the latest pull (its number as a small callout,
/// the outcome, the length, the pull in one sentence, its upload and Open the pull); the night's earlier pulls as
/// rows, with All pulls. While a pull is recorded: the ember card (REC, Stop and keep it, the clock over a cast bar to
/// the plan's length, the step it's on and the next), what's being recorded, and the latest pull dimmed under it.
/// Before the first pull, the Boss asleep and three steps numbered as waymarks.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The night's earlier pulls Home shows (the rest are in the Pulls tab).</summary>
    private const int EarlierShown = 5;

    /// <summary>The duty the status card (or the recording card) already names, so the latest pull doesn't say it again.</summary>
    private string? DutyShown => recorder.Current?.Duty.Name ?? recorder.DutyHere?.Name;

    private void DrawHome()
    {
        Frame.BeginHeader();
        NavTabs();
        Frame.EndHeader();

        Frame.BeginBody("##home", null);
        var all = store.All;
        if (recorder.Current != null)
        {
            DrawLive();
            if (all.Count > 0)
            {
                Kit.Gap(22);
                DrawLatestQuiet(all[0]);
            }
        }
        else
        {
            if (recorder.NotKept is { } nk && DateTime.UtcNow - nk.At < TimeSpan.FromMinutes(3)) NotKept(nk.Why);
            DrawStatus();
            if (all.Count == 0) DrawGetStarted();
            else
            {
                Kit.Gap(22);
                DrawLatest(all[0]);
                DrawTonight(all);
            }
        }
        Frame.EndBody();
    }

    /// <summary>A pull that wasn't kept: a Not kept tag and why, for three minutes.</summary>
    private static void NotKept(string why)
    {
        var at = Kit.At;
        var dl = Kit.Dl;
        var tw = Kit.Tag(dl, at, "Not kept", TagKind.Default);
        var x = at.X + tw + S(12);
        Txt.Line(dl, new Vector2(x, at.Y), Txt.Fit(why, Face.Sans, 13, at.X + Kit.Width - x), Face.Sans, 13, Palette.Muted, 22);
        Kit.Reserve(at, Kit.Width, S(22));
        Kit.Gap(14);
    }

    // ---------------------------------------------------------------------------------------------
    // The status card.

    /// <summary>What the plugin is doing now: the card's title, its line, and its well (green ready, ember following, quiet otherwise).</summary>
    private (string Title, string Line, WellTone Well) StatusNow()
    {
        // A pull only followed for the plan (recording was off as it began): it stays that way.
        if (recorder.InPull)
            return ("Following your plan", recorder.RecordsThisDuty ? "This pull isn't recorded: recording starts with the next" : "This pull isn't recorded", WellTone.Ember);
        var duty = recorder.DutyHere;
        if (!config.AutoRecord) return ("Recording is off", duty != null ? $"{duty.Name} · pulls aren't kept" : "Pulls aren't kept until it's on", WellTone.Off);
        if (duty == null) return ("Not in a duty", "Pulls in raids and trials are recorded by themselves", WellTone.Quiet);
        if (!recorder.RecordsThisDuty) return ("Not recorded here", $"{duty.Name} · only raids and trials are recorded", WellTone.Quiet);
        var pulls = recorder.PullsHere;
        var next = $"{duty.Name} · Pull {PullNoAt(duty.Name, DateTimeOffset.Now).ToString(CultureInfo.InvariantCulture)}";
        return pulls > 0 ? ("Ready for the next pull", next, WellTone.Ready) : ("Waiting for the pull", next, WellTone.Ready);
    }

    private enum WellTone
    {
        Ready,
        Ember,
        Quiet,
        Off,
    }

    /// <summary>The Settings page a main part's chip opens.</summary>
    private static SettingsTab ChipTab(Feature f) => f switch
    {
        Feature.Meter => SettingsTab.Meter,
        Feature.Plan => SettingsTab.Plans,
        Feature.Cooldowns => SettingsTab.Cooldowns,
        _ => SettingsTab.Recording,
    };

    private static readonly string[] ChipIds = { "##chip0", "##chip1", "##chip2", "##chip3", "##chip4", "##chip5" };

    /// <summary>
    /// The status card: a well, what the plugin's doing in a line of 15px and the duty and pull under it; then a chip
    /// per main part (green on, faint off, yellow when it waits for your account; the plan's name for Plan), each
    /// opening its settings.
    /// </summary>
    private void DrawStatus()
    {
        var (title, line, tone) = StatusNow();
        Kit.BeginPanel("##status", 18, 18, 18, Palette.Slab, Palette.Line2);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var (fill, ink) = tone switch
        {
            WellTone.Ready => (Palette.Rgba(61, 220, 132, 0.1f), Palette.Ready),
            WellTone.Ember => (Palette.EmberWash, Palette.Ember),
            WellTone.Off => (Palette.Well, Palette.Disabled),
            _ => (Palette.Well, Palette.Muted),
        };
        Kit.SoftWell(dl, at, 40, Icon.CircleDot, fill, ink);
        var x = at.X + S(40) + S(12);
        var room = at.X + w - x;
        var top = at.Y + (S(40) - S(20) - S(4) - S(18)) / 2;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(title, Face.SansSemibold, 15, room), Face.SansSemibold, 15, Palette.White, 20);
        var ly = top + S(20) + S(4);
        if (!config.AutoRecord)
        {
            // Off: the way back on, beside why.
            const string on = "Turn it on";
            var lw = Kit.LinkWidth(on);
            Txt.Line(dl, new Vector2(x, ly), Txt.Fit(line, Face.Sans, 13, room - lw - S(12)), Face.Sans, 13, Palette.Muted, 18);
            if (Kit.Link("##status-on", new Vector2(at.X + w - lw, ly + (S(18) - Txt.Natural(Face.SansMedium, 13)) / 2), on))
            {
                config.AutoRecord = true;
                config.Save();
            }
        }
        else Txt.Line(dl, new Vector2(x, ly), Txt.Fit(line, Face.Sans, 13, room), Face.Sans, 13, Palette.Muted, 18);
        Kit.Reserve(at, w, S(40));
        Kit.Gap(14);

        var set = config.FeaturesNow();
        var plan = plans.Plan is { } p && plans.ForThisDuty ? p.Name : null;
        var wrap = new Wrap(6);
        for (var i = 0; i < Features.All.Count; i++)
        {
            var f = Features.All[i];
            var on = set[f];
            var waits = on && Features.NeedsAccount(f) && !Linked;
            var label = SetupGuide.ChipName(f, plan);
            var cw = MathF.Min(Kit.ChipWidth(label, Kit.FeatureIcon(f)), w);
            if (cw < Kit.ChipWidth(label, Kit.FeatureIcon(f))) label = Txt.Fit(label, Face.SansMedium, 12, w - (Kit.ChipWidth(string.Empty, Kit.FeatureIcon(f))));
            var tip = !on ? $"{Features.Name(f)} is off. Opens its settings."
                : waits ? $"{Features.Name(f)} is on, and waits for your account. Opens its settings."
                : $"{Features.Name(f)} is on. Opens its settings.";
            var pos = wrap.Next(Kit.ChipWidth(label, Kit.FeatureIcon(f)), Kit.ChipH);
            if (Kit.Chip(ChipIds[i], pos, label, !on ? Palette.Faint : waits ? Palette.Warn : Palette.Ready, on, Kit.FeatureIcon(f), tip))
            {
                var tab = ChipTab(f);
                Later(() => OpenSettings(tab));
            }
        }
        // 0.9.0: the settings profile in effect here, if one is.
        if (ProfileLayer.Active(config) is { } profile)
        {
            var label = Txt.Fit("Profile: " + profile.Name, Face.SansMedium, 12, w - Kit.ChipWidth(string.Empty, Icon.Layers));
            if (Kit.Chip("##chip-profile", wrap.Next(Kit.ChipWidth(label, Icon.Layers), Kit.ChipH), label, Palette.Ember, true, Icon.Layers,
                    $"The {ProfileItems.Title(profile)} is in effect here. Opens Profiles."))
                Later(() => OpenSettings(SettingsTab.Profiles));
        }
        wrap.End();
        Kit.Gap(12);
        var np = Kit.At;
        Txt.Line(dl, np, Txt.Fit("What's on, at a glance. Each opens its settings.", Face.Sans, 12, w), Face.Sans, 12, Palette.Faint, 16);
        Kit.Reserve(np, w, S(16));
        Kit.EndPanel();
    }

    // ---------------------------------------------------------------------------------------------
    // The pull being recorded.

    /// <summary>The longest kept pull of a duty (what the recording's cast bar runs to with no plan), by name.</summary>
    private double LongestHere(string duty) =>
        store.All.Where(p => string.Equals(p.Duty, duty, StringComparison.OrdinalIgnoreCase)).Select(p => p.Duration).DefaultIfEmpty(0).Max();

    /// <summary>
    /// The ember card: the beating dot and "Recording pull 13" in ember mono capitals, Stop and keep it in the danger
    /// outline; the duty and when it began; the clock (mono 30) and what it runs to ("of 9:41 in your plan"); the
    /// cast bar; the step of the plan it's on and the next with its time. Under it, what's being recorded as chips.
    /// </summary>
    private void DrawLive()
    {
        var b = recorder.Current!;
        Kit.BeginPanel("##live", 18, 18, 18, Palette.Rgba(255, 138, 61, 0.08f), Palette.Rgba(255, 138, 61, 0.45f));
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;

        // REC · Recording pull 13 ……… Stop and keep it.
        const string stop = "Stop and keep it";
        var stopW = Kit.ButtonWidth(stop, BtnSize.Sm, Icon.Square, Btn.Danger);
        var rowH = S(HSm);
        Kit.BeatDot(dl, new Vector2(at.X + S(4.5f), at.Y + rowH / 2), Palette.Ember, true, 9);
        Kit.Label(dl, new Vector2(at.X + S(9) + S(10), at.Y + (rowH - Txt.Natural(Face.Mono, 12)) / 2), $"Recording pull {PullNoAt(b.Duty.Name, b.StartedAt)}", 12, 0.08f, Palette.EmberHi,
            w - stopW - S(12) - S(19));
        if (Kit.Button("##stop", new Vector2(at.X + w - stopW, at.Y), stop, Btn.Danger, BtnSize.Sm, Icon.Square, 0, false,
                "Ends this pull here and keeps it. It ends by itself when the fight does."))
            recorder.Stop();
        Kit.Reserve(at, w, rowH);
        Kit.Gap(4);
        var dp = Kit.At;
        Txt.Line(dl, dp, Txt.Fit($"{b.Duty.Name} · started {b.StartedAt.ToLocalTime():HH:mm}", Face.Sans, 13, w), Face.Sans, 13, Palette.Muted, 19);
        Kit.Reserve(dp, w, S(19));
        Kit.Gap(12);

        // The clock, and what it runs to: the plan's length, else the longest pull here.
        var plan = plans.Plan is { } p && plans.ForThisDuty ? p : null;
        var length = plan != null ? PlanProgress.Length(plan) : LongestHere(b.Duty.Name);
        var of = plan != null && length > 0 ? $"of {Format.Clock(length)} in your plan" : length > 0 ? $"of {Format.Clock(length)}, the longest here" : "the first pull here";
        var cp = Kit.At;
        var clockH = S(34);
        var cw = Txt.Line(dl, cp, Format.Clock(recorder.Elapsed), Face.MonoSemibold, 30, Palette.White, 34);
        var baseline = cp.Y + (clockH - Txt.Natural(Face.MonoSemibold, 30)) / 2 + Txt.Ascent(Face.MonoSemibold, 30);
        var ox = cp.X + cw + S(12);
        Txt.Draw(dl, new Vector2(ox, baseline - Txt.Ascent(Face.Sans, 13)), Txt.Fit(of, Face.Sans, 13, cp.X + w - ox), Face.Sans, 13, Palette.Muted);
        Kit.Reserve(cp, w, clockH);
        Kit.Gap(10);
        var bp = Kit.At;
        Kit.CastBar(dl, bp, w, length > 0 ? (float)(recorder.Elapsed / length) : 0);
        Kit.Reserve(bp, w, S(6));

        // The step it's on, and the next.
        if (plan != null && plan.Steps.Count > 0)
        {
            Kit.Gap(12);
            var sp = Kit.At;
            var step = Math.Clamp(plans.Step, 0, plan.Steps.Count - 1);
            var clock = plans.PlanClock ?? recorder.Elapsed;
            var next = PlanProgress.Next(plan, step, clock);
            var nextText = next is { } n ? $"next: {n.Name}{(n.In is { } left ? $" in {Format.Clock(left)}" : string.Empty)}" : "the last step";
            var nw = MathF.Min(Txt.Width(nextText, Face.Sans, 12), w * 0.45f);
            Txt.LineRight(dl, sp.X + w, sp.Y, Txt.Fit(nextText, Face.Sans, 12, nw), Face.Sans, 12, Palette.Faint, 19);
            var lead = $"Step {(step + 1).ToString(CultureInfo.InvariantCulture)} of your plan";
            var lw = Txt.Line(dl, sp, lead, Face.Sans, 13, Palette.Muted, 19);
            var nx = sp.X + lw + S(10);
            Txt.Line(dl, new Vector2(nx, sp.Y), Txt.Fit(plan.Steps[step].Big, Face.SansSemibold, 13, sp.X + w - nw - S(12) - nx), Face.SansSemibold, 13, Palette.White, 19);
            Kit.Reserve(sp, w, S(19));
        }
        Kit.EndPanel();

        // What's being recorded, and what happens when it ends.
        Kit.Gap(10);
        var counts = $"{Words.Count(b.Casts, "cast")} · {Words.Count(b.Markers, "marker")} · {Words.Count(b.TethersSeen, "tether")}";
        var places = config.RecordParty ? $"Places of {Words.Count(Game.PartyMembers.Count, "player")}" : "Places off";
        var uploads = config.UploadPrefsNow().UploadLogs && Linked ? "Uploads when it ends" : "Kept on this PC when it ends";
        var wrap = new Wrap(8);
        Kit.Chip("##rec-counts", wrap.Next(Kit.ChipWidth(counts, Icon.CircleDot), Kit.ChipH), counts, Palette.Ember, true, Icon.CircleDot, "What's recorded so far: the boss's casts, the markers and the tethers");
        Kit.Chip("##rec-places", wrap.Next(Kit.ChipWidth(places, Icon.Users), Kit.ChipH), places, config.RecordParty ? Palette.Ready : Palette.Faint, config.RecordParty, Icon.Users,
            config.RecordParty ? $"Where everyone stands, {(config.SmoothReplays ? "four times" : "twice")} a second" : "Where the party stands isn't recorded (Settings › Recording and uploads)");
        Kit.Chip("##rec-upload", wrap.Next(Kit.ChipWidth(uploads, Icon.Upload), Kit.ChipH), uploads, Palette.Ready, true, Icon.Upload);
        wrap.End();
    }

    // ---------------------------------------------------------------------------------------------
    // The latest pull.

    /// <summary>An outcome's tag: Wipe (red), Clear (white), Ended (a hairline).</summary>
    private static (string Text, TagKind Kind) OutcomeTag(Outcome o) => o switch
    {
        Outcome.Wipe => ("Wipe", TagKind.Wipe),
        Outcome.Clear => ("Clear", TagKind.Clear),
        _ => ("Ended", TagKind.Default),
    };

    private static string Capital(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    /// <summary>The pull before this one in the same duty, kept, from the same night (what "2 seconds further than pull 11" is against).</summary>
    private RecordingInfo? Before(RecordingInfo info)
    {
        var all = store.All;
        var i = all.ToList().FindIndex(x => string.Equals(x.File, info.File, StringComparison.OrdinalIgnoreCase));
        for (var j = i + 1; i >= 0 && j < all.Count; j++)
        {
            var o = all[j];
            if (info.RecordedAt - o.RecordedAt > TimeSpan.FromHours(12)) return null;
            if (string.Equals(o.Duty, info.Duty, StringComparison.OrdinalIgnoreCase)) return o;
        }
        return null;
    }

    /// <summary>A sentence as runs: its words at 13px in the text colour, the part to see in white 600.</summary>
    private static Run[] Runs(IReadOnlyList<Said> said, Vector4? color = null)
    {
        var runs = new Run[said.Count];
        for (var i = 0; i < said.Count; i++)
            runs[i] = said[i].Strong ? new Run(said[i].Text, Face.SansSemibold, 13, Palette.White) : new Run(said[i].Text, Face.Sans, 13, color ?? Palette.Text);
        return runs;
    }

    /// <summary>
    /// The latest pull's card: "Pull 12" as a small callout, its outcome and its length in mono; the duty when the
    /// status card doesn't name it; the pull in a sentence; its upload, with Open the pull. The top part opens the pull.
    /// </summary>
    private void DrawLatest(RecordingInfo info)
    {
        Kit.Section("##latest-head", "Latest pull");
        Kit.Gap(10);
        Kit.BeginPanel("##latest", 16, 20, 18);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var (tag, kind) = OutcomeTag(info.Outcome);
        var length = Format.Clock(info.Duration);
        var rowH = S(24);
        var lw = Txt.Width(length, Face.MonoSemibold, 18);
        var title = $"Pull {PullNo(info)}";
        Kit.Callout(dl, new Vector2(at.X, at.Y + (rowH - S(17 * 1.05f)) / 2), title, 17, w - lw - S(140), 1);
        var titleW = Txt.Width(Kit.CalloutLines(title, 17, w - lw - S(140), 1)[0], Face.Callout, 17);
        Kit.Tag(dl, new Vector2(at.X + titleW + S(10), at.Y + (rowH - S(Kit.TagH)) / 2), tag, kind);
        Txt.LineRight(dl, at.X + w, at.Y, length, Face.MonoSemibold, 18, Palette.White, 24);
        Kit.Reserve(at, w, rowH);
        if (!string.Equals(info.Duty, DutyShown, StringComparison.OrdinalIgnoreCase))
        {
            Kit.Gap(2);
            var p = Kit.At;
            Txt.Line(dl, p, Txt.Fit($"{info.Duty} · {Format.Ago(info.RecordedAt, DateTimeOffset.Now)}", Face.Sans, 13, w), Face.Sans, 13, Palette.Muted, 19.5f);
            Kit.Reserve(p, w, S(19.5f));
        }
        Kit.Gap(12);
        var gist = GistOf(info);
        var said = PullGist.Sentence(info.Outcome, info.Duration, gist);
        Txt.Para(Runs(said), 19.5f);
        // The top part opens the pull.
        var (open, hovered, _) = Kit.Hit("##latest-open", at - new Vector2(S(6), S(6)), new Vector2(w + S(12), Kit.At.Y - at.Y + S(12)));
        Kit.Reticle(dl, "##latest-ret", at - new Vector2(S(6), S(6)), new Vector2(at.X + w + S(6), Kit.At.Y + S(6)), hovered);
        if (hovered) Kit.Tooltip("Open this pull: what happened, Save as file, Delete");
        if (open) Later(() => ShowPull(info));
        Kit.Gap(14);
        DrawUploadLine("##latest-", info);
        Kit.EndPanel();
    }

    /// <summary>The latest pull while another is recorded: dimmed, one line (its number, outcome, length, a tick once uploaded), and a word on why.</summary>
    private void DrawLatestQuiet(RecordingInfo info)
    {
        var old = Palette.Opacity;
        Palette.Opacity = old * 0.55f;
        Kit.Section("##latest-quiet-head", "Latest pull");
        Kit.Gap(8);
        Kit.BeginPanel("##latest-quiet", 14, 16, 14, Palette.Slab, Palette.Line, Palette.RadiusMd + 2);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var (tag, kind) = OutcomeTag(info.Outcome);
        var rowH = S(22);
        var title = $"Pull {PullNo(info)}";
        var x = at.X + Txt.Line(dl, new Vector2(at.X, at.Y + (rowH - S(16 * 1.05f)) / 2), Txt.CalloutCaps(title), Face.Callout, 16, Palette.White, 16 * 1.05f) + S(10);
        x += Kit.Tag(dl, new Vector2(x, at.Y), tag, kind) + S(10);
        Txt.Line(dl, new Vector2(x, at.Y), Format.Clock(info.Duration), Face.MonoSemibold, 15, Palette.White, 22);
        if ((store.Find(info.File)?.SentId ?? info.SentId) != null) Icons.Draw(dl, Icon.Check, new Vector2(at.X + w - S(15), at.Y + (rowH - S(15)) / 2), S(15), Palette.Ready, 2.2f);
        Kit.Reserve(at, w, rowH);
        Kit.EndPanel();
        Kit.Gap(8);
        Txt.Para(new[] { new Run("The rest waits until the pull ends: nothing here needs you while you fight.", Face.Sans, 12, Palette.Faint) }, 17);
        Palette.Opacity = old;
    }

    // ---------------------------------------------------------------------------------------------
    // Tonight.

    /// <summary>The local times of the kept pulls (the list's order), for grouping by night.</summary>
    private static List<DateTime> LocalTimes(IReadOnlyList<RecordingInfo> all) => all.Select(i => i.RecordedAt.ToLocalTime().DateTime).ToList();

    // ---------------------------------------------------------------------------------------------
    // Pull numbers (0.9.1): by night, as the site says them ("Pull 17" is the 17th pull of that fight that night).

    private IReadOnlyList<RecordingInfo>? numberedFor;
    private List<(DateTime Local, string Duty)> numberedPulls = new();
    private readonly Dictionary<string, int> pullNumbers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The kept pulls' numbers, worked out again when the list changes (it's a new list then).</summary>
    private void NumberPulls()
    {
        var all = store.All;
        if (ReferenceEquals(all, numberedFor)) return;
        numberedFor = all;
        numberedPulls = all.Select(i => (i.RecordedAt.ToLocalTime().DateTime, i.Duty)).ToList();
        var n = PullNights.Numbers(numberedPulls);
        pullNumbers.Clear();
        for (var i = 0; i < all.Count; i++) pullNumbers[all[i].File] = n[i];
    }

    /// <summary>A kept pull's number: its place in its night of its duty (<see cref="PullNights.Numbers"/>).</summary>
    internal int PullNo(RecordingInfo info)
    {
        NumberPulls();
        return pullNumbers.TryGetValue(info.File, out var no) ? no : Math.Max(1, info.Pull);
    }

    /// <summary>The number of a pull of <paramref name="duty"/> beginning at <paramref name="at"/> (being recorded, or the next): its night's kept pulls of it, and one.</summary>
    internal int PullNoAt(string duty, DateTimeOffset at)
    {
        NumberPulls();
        return PullNights.Next(numberedPulls, duty, at.ToLocalTime().DateTime);
    }

    /// <summary>A pull's row's words: its number, length, how long ago and where it ended, its outcome, uploaded or waiting.</summary>
    private Kit.PullLine LineOf(RecordingInfo info, bool best, string? dutyShown, bool wantGist)
    {
        var (tag, kind) = OutcomeTag(info.Outcome);
        var now = DateTimeOffset.Now;
        var gist = GistOf(info, wantGist);
        var sub = Format.Ago(info.RecordedAt, now);
        if (PullGist.Where(gist) is { } where) sub += $" · {where}";
        if (!string.Equals(info.Duty, dutyShown, StringComparison.OrdinalIgnoreCase)) sub = $"{info.Duty} · {sub}";
        var sent = (store.Find(info.File)?.SentId ?? info.SentId) != null;
        string? waiting = null;
        if (!sent && Linked && config.UploadPrefsNow().Wants(info)) waiting = "waiting";
        return new Kit.PullLine(PullNo(info).ToString(CultureInfo.InvariantCulture), Format.Clock(info.Duration), sub, tag, kind, sent, waiting, best);
    }

    /// <summary>Pulls as rows in a slab; a row opens its pull. Its facts are read only for rows in view.</summary>
    private void DrawPullRows(string id, IReadOnlyList<RecordingInfo> rows, ISet<string> best, string? dutyShown)
    {
        Kit.BeginPanel(id, 0, 0, 0);
        var top = ImGui.GetWindowPos().Y;
        var bottom = top + ImGui.GetWindowHeight();
        for (var i = 0; i < rows.Count; i++)
        {
            var info = rows[i];
            var y = Kit.At.Y;
            var visible = y + Kit.PullRowH > top && y < bottom;
            if (Kit.PullListRow($"{id}-{info.File}", LineOf(info, best.Contains(info.File), dutyShown, visible), i == 0)) Later(() => ShowPull(info));
        }
        Kit.EndPanel();
    }

    /// <summary>The night's earlier pulls (up to five), its name and count, and All pulls.</summary>
    private void DrawTonight(IReadOnlyList<RecordingInfo> all)
    {
        if (all.Count < 2) return;
        var nights = PullNights.Group(LocalTimes(all), DateTime.Now);
        var night = nights.FirstOrDefault(n => n.Items.Contains(1));
        if (night == null) return;
        var rows = night.Items.Where(i => i > 0).Take(EarlierShown).Select(i => all[i]).ToList();
        if (rows.Count == 0) return;
        Kit.Gap(22);
        if (Kit.Section("##home-all", night.Label, Words.Count(night.Items.Count, "pull"), "All pulls")) Later(ShowPulls);
        Kit.Gap(10);
        var best = BestOf(all, night);
        DrawPullRows("##tonight", rows, best, all[0].Duty);
    }

    /// <summary>A night's best pull in each duty (by file).</summary>
    private static HashSet<string> BestOf(IReadOnlyList<RecordingInfo> all, PullNight night)
    {
        var items = night.Items.Select(i => all[i]).ToList();
        var best = PullNights.Best(items.Select(i => (i.Duty, i.Duration, i.Outcome)).ToList());
        return best.Select(i => items[i].File).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------
    // Nothing yet.

    private static readonly (string Title, string Line)[] Steps =
    {
        ("Go into a duty", "Trials, raids and ultimates are recorded by themselves."),
        ("Pull", "The boss's casts and where they land, markers, tethers, waymarks."),
        ("Send it to the Raid Codex", "It becomes a plan: a step for each mechanic, yours to edit."),
    };

    private void DrawGetStarted()
    {
        var dl = Kit.Dl;
        var w = Kit.Width;
        Kit.Gap(22);
        // The Boss asleep, over a small callout and one line.
        var at = Kit.At;
        var drawH = S(96);
        Boss.Empty(dl, new Vector2(at.X + w / 2, at.Y + drawH / 2), 68, BossMood.Asleep, 96);
        Kit.Reserve(at, w, drawH);
        Kit.Gap(8.8f);
        var cy = Kit.At;
        var ch = Kit.Callout(dl, cy, "Nothing pulled yet", 22, w, 2, 1.05f, center: true);
        Kit.Reserve(cy, w, ch);
        Kit.Gap(8.8f);
        var lw = MathF.Min(w, S(304));
        var lp = Kit.At;
        var lh = Txt.Flow(new Vector2(lp.X + (w - lw) / 2, lp.Y), lw, new[] { Run.Note("Each pull in a duty is recorded by itself, from the pull until the fight ends.") }, 19.5f, out _, center: true);
        Kit.Reserve(lp, w, lh);
        Kit.Gap(18);

        // Three steps, numbered as waymarks (1 red, 2 yellow, 3 blue squares).
        Kit.BeginPanel("##steps", 6, 6, 6);
        for (var i = 0; i < Steps.Length; i++)
        {
            var (title, line) = Steps[i];
            var p = Kit.At;
            var rw = Kit.Width;
            var lines = Txt.Wrap(line, Face.Sans, 12, rw - S(9.6f) * 2 - S(22) - S(11.2f));
            var h = MathF.Max(S(50), S(8) * 2 + S(14 * 1.25f) + Txt.Natural(Face.Sans, 12) * lines.Count);
            if (i > 0) Kit.Rule(dl, p.X, p.X + rw, p.Y - 0.5f);
            var x = p.X + S(9.6f);
            Kit.Waymark(dl, new Vector2(x, p.Y + (h - S(22)) / 2), i + 1, 22);
            x += S(22) + S(11.2f);
            var top = p.Y + (h - S(14 * 1.25f) - Txt.Natural(Face.Sans, 12) * lines.Count) / 2;
            Txt.Line(dl, new Vector2(x, top), title, Face.SansMedium, 14, Palette.White, 14 * 1.25f);
            for (var j = 0; j < lines.Count; j++)
                Txt.Draw(dl, new Vector2(x, top + S(14 * 1.25f) + Txt.Natural(Face.Sans, 12) * j), lines[j], Face.Sans, 12, Palette.Muted);
            Kit.Reserve(p, rw, h);
        }
        Kit.EndPanel();

        // Link your account is the telegraph until you've linked.
        if (!Linked)
        {
            Kit.Gap(18);
            var row = Kit.At;
            const string link = "Link your account";
            if (Kit.Button("##gs-link", row, link, Btn.Tele, BtnSize.Md, Icon.Link, 0, false, "Once, so pulls go straight to your Raid Codex"))
                OpenSettings(SettingsTab.Account);
            Kit.Reserve(row, w, Kit.ButtonHeight());
        }
    }
}
