using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Home (the mock-ups' "Home"): the state as a tag in the header with Open the Raid Planner and
/// Settings; then the pull being recorded in the ember box (its duty as a small callout, the clock
/// in mono over a cast bar running to the plan's length, what's being recorded counted in mono,
/// Stop now in the danger outline); the latest pull (its number as a small callout, the outcome as
/// a tag, the facts in mono, Send to Raid Planner as the telegraph); the night's earlier pulls as
/// rows with square wells, a tag and a tick once sent. Before the first pull, the Boss asleep and
/// three steps numbered as waymarks.
/// </summary>
public sealed partial class MainWindow
{
    private const int EarlierShown = 6;
    private bool earlierAll;

    /// <summary>The header's tag: what the plugin is doing now.</summary>
    private (string Text, TagKind Kind) StateNow()
    {
        if (recorder.Current is { } b)
            return ($"Rec · pull {b.Pull} · {Format.Clock(recorder.Elapsed)}", TagKind.Rec);
        // A pull only followed for the plan (recording was off as it began): it stays that way.
        if (recorder.InPull)
            return (recorder.RecordsThisDuty ? "Following your plan · recording starts next pull" : "Following your plan · not recorded", TagKind.Ember);
        if (!config.AutoRecord) return ("Recording is off", TagKind.Default);
        if (recorder.DutyHere is { } duty) return ($"Waiting for the pull · {duty.Name}", TagKind.Plain);
        return ("Not in a duty", TagKind.Plain);
    }

    /// <summary>The duty the header (or the recording box) already names, so the latest pull doesn't say it again.</summary>
    private string? DutyShown => recorder.Current?.Duty.Name ?? recorder.DutyHere?.Name;

    private void DrawHome()
    {
        Frame.BeginHeader();
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var (state, kind) = StateNow();
        var buttons = S(H) * 2 + S(6.4f);
        var tagH = 22f;
        var tagText = state;
        var room = w - buttons - S(12);
        if (Kit.TagWidth(tagText, kind) > room) tagText = Kit.FitSpaced(Txt.Caps(state), Face.Mono, 11, 0.05f, room - (Kit.TagWidth(string.Empty, kind)));
        Kit.Tag(dl, new Vector2(at.X, at.Y + (S(H) - S(tagH)) / 2), tagText, kind);
        if (Kit.IconButton("##open-site", new Vector2(at.X + w - buttons, at.Y), Icon.ExternalLink, "Open the Raid Planner", Btn.Quiet))
            Dalamud.Utility.Util.OpenLink(SiteLink.PlannerUrl);
        if (Kit.IconButton("##settings", new Vector2(at.X + w - S(H), at.Y), Icon.Sliders, "Settings")) OpenSettings(settingsTab);
        Kit.Reserve(at, w, S(H));
        Frame.EndHeader();

        Frame.BeginBody("##home", null);
        var all = store.All;
        var recording = recorder.Current != null;
        if (recording)
        {
            DrawLive();
            Kit.Gap(22);
        }
        else if (recorder.NotKept is { } nk && DateTime.UtcNow - nk.At < TimeSpan.FromMinutes(3)) NotKept(nk.Why);

        if (all.Count == 0)
        {
            if (!recording) DrawGetStarted();
        }
        else
        {
            DrawLatest(all[0], recording);
            DrawEarlier(all.Skip(1).ToList(), recording);
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
    // The pull being recorded.

    /// <summary>The longest kept pull of a duty (what the recording's cast bar runs to with no plan), by name.</summary>
    private double LongestHere(string duty) =>
        store.All.Where(p => string.Equals(p.Duty, duty, StringComparison.OrdinalIgnoreCase)).Select(p => p.Duration).DefaultIfEmpty(0).Max();

    private void DrawLive()
    {
        var b = recorder.Current!;
        Kit.BeginPanel("##live", 14, 16, 14, Palette.RecWash, Palette.RecLine);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var now = DateTimeOffset.Now;

        // The eyebrow in ember, when it started on the right.
        var started = $"Started {b.StartedAt.ToLocalTime():HH:mm}";
        var sw = Txt.Width(started, Face.Sans, 12);
        var night = Format.Day(b.StartedAt, now).StartsWith("tonight", StringComparison.Ordinal) ? " tonight" : string.Empty;
        Kit.Eyebrow(dl, at, $"Recording · pull {b.Pull}{night}", null, w - sw - S(12), Palette.Ember);
        Txt.Draw(dl, new Vector2(at.X + w - sw, at.Y + (Kit.EyebrowH - Txt.Natural(Face.Sans, 12)) / 2), started, Face.Sans, 12, Palette.Faint);
        var y = at.Y + Kit.EyebrowH + S(12 - 2);

        // The duty, a small callout.
        y += Kit.Callout(dl, new Vector2(at.X, y), b.Duty.Name, 17, w, 2, 1.05f);
        y += S(12);

        // The clock (Geist Mono, so it holds still), and a cast bar to the plan's length, else the longest pull here.
        var clock = Format.Clock(recorder.Elapsed);
        var clockW = Txt.Width(clock, Face.MonoSemibold, 30);
        var clockH = Txt.Natural(Face.MonoSemibold, 30) * 0.8f;
        var (name, rest, length) = LiveCast(b.Duty.Name);
        var castX = at.X + clockW + S(16);
        var castW = at.X + w - castX;
        var castH = S(13 * 1.2f) + S(4.8f) + S(6);
        var rowH = MathF.Max(clockH, castH + S(2));
        Txt.Draw(dl, new Vector2(at.X, y + rowH - Txt.Natural(Face.MonoSemibold, 30) * 0.9f), clock, Face.MonoSemibold, 30, Palette.White);
        var fraction = length > 0 ? (float)(recorder.Elapsed / length) : 0;
        Kit.Cast(dl, new Vector2(castX, y + rowH - castH - S(2)), castW, name, length > 0 ? Format.Clock(length) : null, fraction, nameRest: rest);
        Kit.Reserve(at, w, y + rowH - at.Y);
        Kit.Gap(12);

        // What's being recorded, counted in mono.
        var party = Math.Max(1, Services.Party.Length);
        Kit.Facts(new[]
        {
            new Kit.Fact("Casts", Format.Thousands(b.Casts)),
            new Kit.Fact("Markers", Format.Thousands(b.Markers)),
            new Kit.Fact("Tethers", Format.Thousands(b.TethersSeen)),
            config.RecordParty ? new Kit.Fact("Places", party.ToString(System.Globalization.CultureInfo.InvariantCulture), "×2 / s") : new Kit.Fact("Places", "Off", Color: Palette.Muted, Sans: true),
        });
        Kit.Gap(12);

        // Stop now: the danger outline.
        var row = Kit.At;
        const string stop = "Stop now";
        if (Kit.Button("##stop", row, stop, Btn.Danger, BtnSize.Sm, Icon.Square, 0, false, "Ends this pull here and keeps it. It ends by itself when the fight does."))
            recorder.Stop();
        var bw = Kit.ButtonWidth(stop, BtnSize.Sm, Icon.Square, Btn.Danger);
        Txt.Line(dl, new Vector2(row.X + bw + S(12), row.Y), Txt.Fit("Ends by itself when the fight does.", Face.Sans, 13, w - bw - S(12)), Face.Sans, 13, Palette.Muted, HSm);
        Kit.Reserve(row, w, S(HSm));
        Kit.EndPanel();
    }

    /// <summary>
    /// The recording's cast bar: with a plan for this duty, its step's callout ("· step 12 of your
    /// plan") and the plan's length; without, the longest pull kept here.
    /// </summary>
    private (string Name, string? More, double Length) LiveCast(string duty)
    {
        if (plans.Plan is { } plan && plans.ForThisDuty)
        {
            var step = Math.Clamp(plans.Step, 0, plan.Steps.Count - 1);
            var s = plan.Steps[step];
            return (s.Big, $"· step {step + 1} of your plan", PlanLength(plan));
        }
        var longest = LongestHere(duty);
        return longest > 0 ? ("The longest pull here", null, longest) : ("The first pull here", null, 0);
    }

    /// <summary>A plan's length: its last cue's time, else its last step's written time.</summary>
    private static double PlanLength(Plan plan)
    {
        if (plan.Game?.Cues is { Count: > 0 } cues) return cues.Max(c => c.T);
        for (var i = plan.Steps.Count - 1; i >= 0; i--)
            if (Format.ParseClock(plan.Steps[i].Time) is { } t) return t;
        return 0;
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

    /// <summary>The Party fact: how many places ("8 places"), or none kept.</summary>
    private Kit.Fact PartyFact(RecordingInfo info) =>
        info.PartyPositions
            ? new Kit.Fact("Party", PartyCount(info) is { } n ? Format.Thousands(n) : "…", "places")
            : new Kit.Fact("Party", "None", Color: Palette.Muted, Sans: true);

    /// <summary>How many of the party a pull kept, read from its file once on a worker (the index doesn't say); null until it's read.</summary>
    private int? PartyCount(RecordingInfo info)
    {
        if (partyCounts.TryGetValue(info.File, out var n)) return n >= 0 ? n : null;
        partyCounts[info.File] = -1;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                partyCounts[info.File] = store.Read(info).Party.Count;
            }
            catch (Exception ex)
            {
                Services.Log.Warning(ex, "Raid Planner couldn't read a pull's party");
            }
        });
        return null;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> partyCounts = new(StringComparer.OrdinalIgnoreCase);

    private void DrawLatest(RecordingInfo info, bool recording)
    {
        var now = DateTimeOffset.Now;
        Kit.Section("##latest-sh", "Latest pull", Capital(Format.Ago(info.RecordedAt, now)));
        Kit.Gap(10);
        var compact = recording;
        Kit.BeginPanel("##latest", compact ? 14 : 16, 16, compact ? 14 : 16);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var (tag, kind) = OutcomeTag(info.Outcome);

        // Pull 12 [WIPE] ……… 3:31
        var title = $"Pull {Math.Max(1, info.Pull)}";
        var length = Format.Clock(info.Duration);
        float rowH;
        if (compact)
        {
            rowH = S(22);
            var tw = Txt.Line(dl, at, title, Face.SansSemibold, 15, Palette.White, 22);
            Kit.Tag(dl, new Vector2(at.X + tw + S(12), at.Y), tag, kind);
            Txt.LineRight(dl, at.X + w, at.Y, length, Face.Mono, 13, Palette.White, 22);
        }
        else
        {
            rowH = MathF.Max(S(22), S(17 * 1.05f));
            var tw = Txt.Line(dl, at, Txt.CalloutCaps(title), Face.Callout, 17, Palette.White, rowH / S(1));
            Kit.Tag(dl, new Vector2(at.X + tw + S(12), at.Y + (rowH - S(22)) / 2), tag, kind);
            Txt.LineRight(dl, at.X + w, at.Y, length, Face.MonoSemibold, 16, Palette.White, rowH / S(1));
        }
        Kit.Reserve(at, w, rowH);
        // Said once on the screen: the duty, when the header doesn't already.
        if (!string.Equals(info.Duty, DutyShown, StringComparison.OrdinalIgnoreCase))
        {
            Kit.Gap(2);
            var p = Kit.At;
            Txt.Line(dl, p, Txt.Fit(info.Duty, Face.Sans, 13, w), Face.Sans, 13, Palette.Muted, 19.5f);
            Kit.Reserve(p, w, S(19.5f));
        }
        Kit.Gap(compact ? 12 : 14);

        if (compact)
        {
            Kit.Facts(new[] { new Kit.Fact("Length", length), new Kit.Fact("Mechanics", Format.Thousands(info.Mechanics)), PartyFact(info) });
            Kit.Gap(12);
            DrawSendCompact(info);
        }
        else
        {
            Kit.Facts(new[]
            {
                new Kit.Fact("Length", length),
                new Kit.Fact("Ended", info.Outcome == Outcome.Wipe ? "Wipe" : info.Outcome == Outcome.Clear ? "Clear" : "Ended",
                    Color: info.Outcome == Outcome.Wipe ? Palette.Danger : info.Outcome == Outcome.Clear ? Palette.White : Palette.Muted, Sans: true),
                new Kit.Fact("Mechanics", Format.Thousands(info.Mechanics)),
                PartyFact(info),
            });
            Kit.Gap(14);
            var lp = Kit.At;
            if (Kit.Link("##latest-open", lp, "What's in it", Icon.ArrowRight, tip: "Its mechanics, in order, before you send it")) Later(() => ShowPull(info));
            Kit.Reserve(lp, w, Txt.Natural(Face.SansMedium, 13));
            Kit.Gap(14);
            DrawSend("##latest-", info, "It waits in the Raid Planner under From the game, ready to make into a plan.");
        }
        Kit.EndPanel();
    }

    private static string Capital(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    // ---------------------------------------------------------------------------------------------
    // Earlier pulls.

    private void DrawEarlier(List<RecordingInfo> rows, bool recording)
    {
        if (rows.Count == 0) return;
        Kit.Gap(22);
        var now = DateTimeOffset.Now;
        var aside = Format.PullsAside(rows.Select(r => r.RecordedAt).ToList(), now);
        string? link = rows.Count > EarlierShown ? earlierAll ? "Fewer" : $"All {rows.Count}" : null;
        if (Kit.Section("##earlier-all", "Earlier pulls", aside, link)) earlierAll = !earlierAll;
        Kit.Gap(10);
        if (!earlierAll) rows = rows.Take(EarlierShown).ToList();
        Kit.BeginPanel("##earlier", 4, 4, 4);
        var dl = Kit.Dl;
        var shown = DutyShown ?? (store.All.Count > 0 ? store.All[0].Duty : null);
        var hoveredIndex = -1;
        var rects = new List<(Vector2 Min, Vector2 Max)>();
        for (var i = 0; i < rows.Count; i++)
        {
            var at = Kit.At;
            var w = Kit.Width;
            var h = S(50);
            var (clicked, hovered, _) = Kit.Hit($"##pull-{rows[i].File}", at, new Vector2(w, h));
            if (hovered) hoveredIndex = i;
            if (clicked)
            {
                var row = rows[i];
                Later(() => ShowPull(row));
            }
            rects.Add((at, at + new Vector2(w, h)));
            Kit.Reserve(at, w, h);
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var (min, max) = rects[i];
            var row = rows[i];
            var hovered = i == hoveredIndex;
            var w = max.X - min.X;
            var h = max.Y - min.Y;
            // A hairline between rows, gone round the one pointed at.
            if (i > 0 && hoveredIndex != i && hoveredIndex != i - 1) Kit.Rule(dl, min.X, max.X, min.Y - 0.5f);
            if (hovered) dl.AddRectFilled(min, max, Palette.U32(Palette.Hover), S(Palette.RadiusMd));
            Kit.Reticle(dl, $"##pull-ret-{row.File}", min, max, hovered);

            var x = min.X + S(9.6f);
            Kit.IconWell(dl, new Vector2(x, min.Y + (h - S(32)) / 2), 32, Icon.Swords);
            x += S(32) + S(11.2f);
            var chevronX = max.X - S(9.6f) - S(Kit.IconPx);
            var right = chevronX - S(11.2f);
            if (row.SentId != null)
            {
                Kit.Tick(dl, new Vector2(right - S(9), min.Y + h / 2));
                right -= S(18) + S(11.2f);
            }
            var (tag, kind) = OutcomeTag(row.Outcome);
            var tagW = Kit.TagWidth(tag, kind);
            Kit.Tag(dl, new Vector2(right - tagW, min.Y + (h - S(22)) / 2), tag, kind);
            right -= tagW + S(11.2f);

            var nameH = S(14 * 1.25f);
            var subH = Txt.Natural(Face.Sans, 12);
            var top = min.Y + (h - nameH - subH) / 2;
            var title = $"Pull {Math.Max(1, row.Pull)}";
            var tw = Txt.Line(dl, new Vector2(x, top), title, Face.SansMedium, 14, Palette.White, 14 * 1.25f);
            Txt.Line(dl, new Vector2(x + tw + S(4.8f), top), Format.Clock(row.Duration), Face.Mono, 13, Palette.Muted, 14 * 1.25f);
            var sub = Format.Ago(row.RecordedAt, now);
            // A pull from another duty says which.
            if (!string.Equals(row.Duty, shown, StringComparison.OrdinalIgnoreCase)) sub = $"{row.Duty} · {sub}";
            Txt.Draw(dl, new Vector2(x, top + nameH), Txt.Fit(sub, Face.Sans, 12, right - x), Face.Sans, 12, Palette.Muted);
            Icons.Draw(dl, Icon.ChevronRight, new Vector2(chevronX, min.Y + (h - S(Kit.IconPx)) / 2), S(Kit.IconPx), hovered ? Palette.White : Palette.Faint);
        }
        Kit.EndPanel();
    }

    // ---------------------------------------------------------------------------------------------
    // Nothing yet.

    private static readonly (string Title, string Line)[] Steps =
    {
        ("Go into a duty", "Trials, raids and ultimates are recorded by themselves."),
        ("Pull", "The boss's casts and where they land, markers, tethers, waymarks."),
        ("Send it to the Raid Planner", "It becomes a plan: a step for each mechanic, yours to edit."),
    };

    private void DrawGetStarted()
    {
        var dl = Kit.Dl;
        var w = Kit.Width;
        Kit.Gap(10);
        // The Boss asleep, over a small callout and one line.
        var at = Kit.At;
        var drawH = S(96);
        Boss.Empty(dl, new Vector2(at.X + w / 2, at.Y + drawH / 2), 68, BossMood.Asleep, 96);
        Kit.Reserve(at, w, drawH);
        Kit.Gap(8.8f);
        var cy = Kit.At;
        var ch = Kit.Callout(dl, cy, "Nothing pulled yet", 17, w, 2, 1.05f, center: true);
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
            var sq = new Vector2(x, p.Y + (h - S(22)) / 2);
            dl.AddRectFilled(sq, sq + new Vector2(S(22)), Palette.U32(Palette.Waymarks[i]), S(4));
            var n = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var ns = Txt.Measure(n, Face.MonoSemibold, 12);
            Txt.Draw(dl, sq + (new Vector2(S(22)) - ns) / 2, n, Face.MonoSemibold, 12, Palette.OnLight);
            x += S(22) + S(11.2f);
            var top = p.Y + (h - S(14 * 1.25f) - Txt.Natural(Face.Sans, 12) * lines.Count) / 2;
            Txt.Line(dl, new Vector2(x, top), title, Face.SansMedium, 14, Palette.White, 14 * 1.25f);
            for (var j = 0; j < lines.Count; j++)
                Txt.Draw(dl, new Vector2(x, top + S(14 * 1.25f) + Txt.Natural(Face.Sans, 12) * j), lines[j], Face.Sans, 12, Palette.Muted);
            Kit.Reserve(p, rw, h);
        }
        Kit.EndPanel();
        Kit.Gap(18);

        // Link your account is the telegraph until you've linked; Open the Raid Planner outlined.
        var row = Kit.At;
        var x0 = row.X;
        if (!Linked)
        {
            const string link = "Link your account";
            if (Kit.Button("##gs-link", row, link, Btn.Tele, BtnSize.Md, Icon.Link, 0, false, "Once, so pulls go straight to your Raid Planner"))
                OpenSettings(SettingsTab.Account);
            x0 += Kit.ButtonWidth(link, BtnSize.Md, Icon.Link, Btn.Tele) + S(6.4f);
        }
        if (Kit.Button("##gs-open", new Vector2(x0, row.Y), "Open the Raid Planner", Btn.Outline, BtnSize.Md, Icon.ExternalLink))
            Dalamud.Utility.Util.OpenLink(SiteLink.PlannerUrl);
        Kit.Reserve(row, w, Kit.ButtonHeight());
        if (!config.AutoRecord)
        {
            Kit.Gap(18);
            if (Txt.Para(new[] { Run.Note("Recording is off. "), Run.LinkRun("Turn it on", "on") }) == "on")
            {
                config.AutoRecord = true;
                config.Save();
            }
        }
    }
}
