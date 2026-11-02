using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Home: the title with Settings, a pill for what the plugin's doing; then the pull being recorded
/// (its clock and counts, and Stop), the latest pull as the planner's big card (its facts, Send to
/// Raid Planner and Save as file), and the earlier pulls as rows. With none yet, the planner's
/// three steps. Everything else (where files go, the party, how many are kept) is in Settings.
/// </summary>
public sealed partial class MainWindow
{
    private const int EarlierShown = 6;
    private bool earlierAll;

    /// <summary>The header pill: what the plugin is doing now.</summary>
    private (string Text, Tone Tone, bool Pulse) StatusNow()
    {
        if (recorder.Current is { } b)
            return ($"Recording · pull {b.Pull} · {Words.Clock(recorder.Elapsed)}", Tone.Live, true);
        if (!config.AutoRecord) return ("Recording is off", Tone.Neutral, false);
        if (recorder.DutyHere is { } duty) return ($"Ready · records the next pull in {duty.Name}", Tone.Waiting, false);
        return ("Records each pull in a duty by itself", Tone.Neutral, false);
    }

    private void DrawHome()
    {
        Frame.BeginHeader(20);
        var at = Kit.At;
        var w = Kit.Width;
        var titleH = Title(at, "Raid Planner", w - S(48), 22, 1);
        SettingsButton(new Vector2(at.X + w - S(36), at.Y));
        var (status, tone, pulse) = StatusNow();
        var pillY = at.Y + MathF.Max(titleH, S(36)) + S(6);
        Kit.StatusPill(new Vector2(at.X, pillY), status, tone, pulse, w);
        Kit.Reserve(at, w, pillY + S(28) - at.Y);
        Frame.EndHeader();

        Frame.BeginBody("##home", null, 20, 24);
        var all = store.All;
        if (recorder.Current != null) DrawLive();
        else if (recorder.NotKept is { } nk && DateTime.UtcNow - nk.At < TimeSpan.FromMinutes(3))
        {
            Txt.Note(nk.Why, Palette.Muted);
            Kit.Gap(16);
        }

        if (all.Count == 0)
        {
            if (recorder.Current == null) DrawGetStarted();
        }
        else
        {
            DrawLatest(all[0]);
            DrawEarlier(all.Skip(1).ToList());
        }
        Frame.EndBody();
    }

    // ---------------------------------------------------------------------------------------------
    // The pull being recorded.

    private void DrawLive()
    {
        var b = recorder.Current!;
        Kit.BeginPanel("##live", 16, 18, 16, Palette.EmberWash, Palette.EmberEdge);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var clock = Words.Clock(recorder.Elapsed);
        var clockW = Txt.Measure(clock, Face.SansSemibold, 30).X;
        var textW = w - clockW - S(16);
        Kit.Eyebrow(at, $"Recording pull {b.Pull}", Palette.EmberText, textW);
        var nameY = at.Y + Txt.Natural(Face.SansMedium, 12) + S(2);
        var nameH = Title(new Vector2(at.X, nameY), b.Duty.Name, textW, 18, 2);
        var line = $"{Words.Count(b.Casts, "cast")} · {Words.Count(b.Markers, "head marker")}" + (config.RecordParty ? " · the party's places" : " · mechanics only");
        Txt.Line(dl, new Vector2(at.X, nameY + nameH + S(2)), Txt.Fit(line, Face.Sans, 13, textW), Face.Sans, 13, Palette.Muted, 19.5f);
        // The clock, large, on the right; its dot breathes.
        var p = Pulse();
        var dot = new Vector2(at.X + w - clockW - S(12), at.Y + S(20));
        dl.AddCircleFilled(dot, S(4) + S(3) * p, Palette.U32(Palette.WithAlpha(Palette.Ember, 0.25f * (1 - p))), 16);
        dl.AddCircleFilled(dot, S(4), Palette.U32(Palette.Ember), 16);
        Txt.Draw(dl, new Vector2(at.X + w - clockW, at.Y + S(2)), clock, Face.SansSemibold, 30, Palette.Ink);
        var h = nameY + nameH + S(2) + S(19.5f) - at.Y;
        Kit.Reserve(at, w, MathF.Max(h, S(44)));
        Kit.Gap(12);
        var row = Kit.At;
        const string stop = "Stop now";
        if (Kit.Button("##stop", row, stop, Btn.Glass, BtnSize.Tiny, Icon.Square, 0, false, "Ends this pull here and keeps it. It ends by itself when the fight does."))
            recorder.Stop();
        var bw = Kit.ButtonWidth(stop, BtnSize.Tiny, Icon.Square);
        Txt.Line(dl, new Vector2(row.X + bw + S(12), row.Y), Txt.Fit("Ends by itself when the fight does.", Face.Sans, 12, w - bw - S(12)), Face.Sans, 12, Palette.Muted, 30);
        Kit.Reserve(row, w, Kit.ButtonHeight(BtnSize.Tiny));
        Kit.EndPanel();
        Kit.Gap(22);
    }

    // ---------------------------------------------------------------------------------------------
    // The latest pull: the planner's big card.

    private void DrawLatest(RecordingInfo info)
    {
        Kit.Section("Latest pull", Words.When(info.RecordedAt, DateTimeOffset.Now));
        Kit.Gap(10);
        Kit.BeginPanel("##latest", 18, 18, 18);
        var at = Kit.At;
        var w = Kit.Width;
        // The name opens the pull (its timeline).
        var titleH = Title(at, info.Duty, w, 18, 2, draw: false);
        var (open, hovered, _) = Kit.Hit("##latest-open", at, new Vector2(w, titleH + S(22)));
        Title(at, info.Duty, w, 18, 2, color: hovered ? Palette.White : Palette.Ink);
        var line = $"Pull {info.Pull} · {Words.OutcomeWord(info.Outcome)}";
        var lineY = at.Y + titleH + S(2);
        var lw = Txt.Line(Kit.Dl, new Vector2(at.X, lineY), line, Face.Sans, 13, Palette.Muted, 19.5f);
        Txt.Line(Kit.Dl, new Vector2(at.X + lw + S(10), lineY), "See what's in it ›", Face.SansMedium, 13, hovered ? Palette.Ink : Palette.Light, 19.5f);
        if (hovered) Kit.Tooltip("Its mechanics, in order, before you send it");
        if (open) Later(() => ShowPull(info));
        Kit.Reserve(at, w, titleH + S(2) + S(19.5f));
        Kit.Gap(14);
        Kit.Facts("##latest-facts", Facts(info));
        Kit.Gap(16);
        var buttons = Kit.At;
        var bh = SendButtons("##latest-", buttons, info, w);
        Kit.Reserve(buttons, w, bh);
        DrawSendState(info, Linked ? null : "Linking takes a moment, once: then each pull is a button away from being a plan.");
        Kit.EndPanel();
    }

    /// <summary>A pull's facts, as the planner's boxes: length, mechanics, the party.</summary>
    private static List<Kit.Fact> Facts(RecordingInfo info) => new()
    {
        new Kit.Fact("Length", Words.Clock(info.Duration)),
        new Kit.Fact("Mechanics", info.Mechanics.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)),
        new Kit.Fact("Party", info.PartyPositions ? "Where they stood" : "Not recorded", Color: info.PartyPositions ? null : Palette.Muted),
    };

    // ---------------------------------------------------------------------------------------------
    // Earlier pulls.

    private void DrawEarlier(List<RecordingInfo> rows)
    {
        if (rows.Count == 0) return;
        Kit.Gap(26);
        if (rows.Count > EarlierShown)
        {
            if (Kit.SectionLink("##earlier-all", "Earlier pulls", earlierAll ? "Show fewer" : $"Show all {rows.Count}")) earlierAll = !earlierAll;
        }
        else Kit.Section("Earlier pulls", Words.Count(rows.Count, "pull"));
        Kit.Gap(10);
        if (!earlierAll) rows = rows.Take(EarlierShown).ToList();
        Kit.BeginPanel("##earlier", 6, 6, 6);
        var dl = Kit.Dl;
        var now = DateTimeOffset.Now;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var at = Kit.At;
            var w = Kit.Width;
            var nameH = Txt.Natural(Face.SansMedium, 14);
            var h = S(10) * 2 + MathF.Max(S(36), nameH + S(2) + S(19.5f));
            if (i > 0) dl.AddLine(new Vector2(at.X + S(10), at.Y - 0.5f), new Vector2(at.X + w - S(10), at.Y - 0.5f), Palette.U32(Palette.Line), 1f);
            var (clicked, hovered, _) = Kit.Hit($"##pull-{row.File}", at, new Vector2(w, h));
            if (clicked) Later(() => ShowPull(row));
            if (hovered) dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.LineFaint), S(Palette.RadiusMd));

            var x = at.X + S(12);
            Kit.IconWell(new Vector2(x, at.Y + (h - S(36)) / 2), S(36), Icon.Swords, quiet: row.SentId != null);
            x += S(36) + S(12);
            var chevronX = at.X + w - S(12) - S(16);
            var tag = row.SentId != null ? "Sent" : null;
            var tagW = tag != null ? Kit.ChipWidth(tag, true) : 0;
            var textW = chevronX - S(10) - (tag != null ? tagW + S(10) : 0) - x;
            var top = at.Y + (h - (nameH + S(2) + S(19.5f))) / 2;
            Txt.Line(dl, new Vector2(x, top), Txt.Fit(row.Duty, Face.SansMedium, 14, textW), Face.SansMedium, 14, Palette.Ink);
            var line = $"{Words.PullLine(row.Pull, row.Duration, row.Outcome)} · {Words.When(row.RecordedAt, now)}";
            Txt.Line(dl, new Vector2(x, top + nameH + S(2)), Txt.Fit(line, Face.Sans, 13, textW), Face.Sans, 13, Palette.Muted, 19.5f);
            if (tag != null) Kit.Chip(new Vector2(chevronX - S(10) - tagW, at.Y + (h - S(24)) / 2), tag, true);
            Icons.Draw(dl, Icon.ChevronRight, new Vector2(chevronX, at.Y + (h - S(16)) / 2), S(16), hovered ? Palette.Ink : Palette.Faint);
            Kit.Reserve(at, w, h);
        }
        Kit.EndPanel();
    }

    // ---------------------------------------------------------------------------------------------
    // Nothing yet.

    private static readonly (Icon Icon, string Title, string Line)[] Steps =
    {
        (Icon.Map, "Go into a duty", "Each pull is recorded by itself, from the pull until the fight ends."),
        (Icon.Swords, "Fight", "The boss's casts and where they land, stacks, spreads, tankbusters, tethers, markers and waymarks."),
        (Icon.Send, "Send it to the Raid Planner", "It becomes a plan, a step for each mechanic, ready for your strats."),
    };

    private void DrawGetStarted()
    {
        Kit.Section("Get started", "Three steps to your first plan");
        Kit.Gap(12);
        var dl = Kit.Dl;
        for (var i = 0; i < Steps.Length; i++)
        {
            var (icon, title, line) = Steps[i];
            Kit.BeginPanel($"##step{i}", 16, 16, 16);
            var at = Kit.At;
            var w = Kit.Width;
            Kit.IconWell(at, S(40), icon);
            var x = at.X + S(40) + S(14);
            var numW = Txt.Measure((i + 1).ToString(), Face.SansSemibold, 18).X;
            Txt.Draw(dl, new Vector2(at.X + w - numW, at.Y), (i + 1).ToString(), Face.SansSemibold, 18, Palette.Faint);
            var tw = w - (x - at.X) - numW - S(12);
            Txt.Line(dl, new Vector2(x, at.Y), Txt.Fit(title, Face.SansMedium, 14, tw), Face.SansMedium, 14, Palette.Ink);
            var h = Txt.Flow(new Vector2(x, at.Y + Txt.Natural(Face.SansMedium, 14) + S(2)), tw, new[] { new Run(line, Face.Sans, 13, Palette.Muted) }, 19.5f, out _);
            Kit.Reserve(at, w, MathF.Max(S(40), Txt.Natural(Face.SansMedium, 14) + S(2) + h));
            Kit.EndPanel();
            Kit.Gap(8);
        }
        Kit.Gap(12);
        var row = Kit.At;
        var x0 = row.X;
        if (!Linked)
        {
            const string link = "Link your account";
            if (Kit.Button("##gs-link", row, link, Btn.Primary, BtnSize.Md, Icon.Link, 0, false, "Once, so pulls go straight to your Raid Planner"))
                OpenSettings(SettingsTab.Account);
            x0 += Kit.ButtonWidth(link, BtnSize.Md, Icon.Link, Btn.Primary) + S(8);
        }
        if (Kit.Button("##gs-open", new Vector2(x0, row.Y), "Open the Raid Planner", Btn.Glass, BtnSize.Md, Icon.ExternalLink))
            Dalamud.Utility.Util.OpenLink(SiteLink.PlannerUrl);
        Kit.Reserve(row, Kit.Width, Kit.ButtonHeight(BtnSize.Md));
        if (!config.AutoRecord)
        {
            Kit.Gap(14);
            if (Txt.Para(new[] { Run.Note("Recording is off. "), new Run("Turn it on", Face.SansMedium, 13, Palette.Ink, Link: "on") }) == "on")
            {
                config.AutoRecord = true;
                config.Save();
            }
        }
    }
}
