using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// A pull's own screen: the header (back, "Pull 3 · today 21:04", the duty, Delete on the right),
/// its facts, and its mechanics in order as the planner's timeline lists steps (time, a rail with a
/// dot, the name, how many, their icons). The footer has Send to Raid Planner and Save as file, and
/// a line saying what Send does. The timeline is read from the pull's file on a worker, once.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The open pull's timeline: its file, and its lines once read (or why not).</summary>
    private sealed record TimelineState(string File, List<TimelineEntry>? Lines, string? Problem, int Waymarks, int Tethers);

    private volatile TimelineState? timeline;

    private void LoadTimeline(RecordingInfo info)
    {
        if (timeline?.File == info.File) return;
        timeline = new TimelineState(info.File, null, null, 0, 0);
        var file = info.File;
        _ = Task.Run(() =>
        {
            try
            {
                var r = store.Read(info);
                var lines = Timeline.Build(r);
                var waymarks = r.Events.OfType<WaymarksEvent>().LastOrDefault()?.Marks.Count ?? 0;
                var tethers = r.Events.OfType<TetherEvent>().Count(t => t.On);
                if (timeline?.File == file) timeline = new TimelineState(file, lines, null, waymarks, tethers);
            }
            catch (Exception ex)
            {
                Services.Log.Warning(ex, "Raid Planner couldn't read a pull");
                if (timeline?.File == file) timeline = new TimelineState(file, null, "This pull's file couldn't be read.", 0, 0);
            }
        });
    }

    private void DrawPull(RecordingInfo info)
    {
        LoadTimeline(info);
        Frame.BeginHeader(20);
        var right = S(36);
        if (ScreenHeader("Back", $"Pull {info.Pull} · {Words.When(info.RecordedAt, DateTimeOffset.Now)}", info.Duty, right, p =>
            {
                if (Kit.IconButton("##delete", p, Icon.Trash, "Delete this pull", 36))
                    Ask("Delete this pull?", info.SentId != null
                            ? "It's gone from the plugin. The copy you sent stays in your Raid Planner."
                            : "It's gone for good: it hasn't been sent to the Raid Planner.",
                        "Delete", "Keep it", () =>
                        {
                            store.Delete(info);
                            screen = Screen.Home;
                            Notify("Deleted");
                        }, danger: true);
            }))
            screen = Screen.Home;
        Frame.EndHeader();

        Frame.BeginBody("##pull", "pull-footer", 18, 22);
        var facts = Facts(info);
        facts.Insert(1, new Kit.Fact("Ended", Words.OutcomeWord(info.Outcome)));
        Kit.Facts("##pull-facts", facts);
        Kit.Gap(24);
        DrawTimeline(info);
        Frame.EndBody();

        Frame.BeginFooter("pull-footer", 16, 18);
        var at = Kit.At;
        var h = SendButtons("##pull-", at, info, Kit.Width);
        Kit.Reserve(at, Kit.Width, h);
        DrawSendState(info, Linked
            ? "It waits in the Raid Planner under From the game. Make it a plan there: a step for each mechanic, yours to edit."
            : "Link your account once, and pulls go straight to your Raid Planner. Or save the file and import it there.");
        Frame.EndFooter();
    }

    private void DrawTimeline(RecordingInfo info)
    {
        var t = timeline;
        if (t?.Lines is not { } lines)
        {
            Kit.Section("What's in it");
            Kit.Gap(10);
            Txt.Note(t?.Problem ?? "Reading it…", t?.Problem != null ? Palette.Danger : Palette.Muted);
            return;
        }
        var extras = new List<string>();
        if (t.Waymarks > 0) extras.Add(Words.Count(t.Waymarks, "waymark"));
        if (t.Tethers > 0) extras.Add(Words.Count(t.Tethers, "tether"));
        Kit.Section("What's in it", extras.Count > 0 ? string.Join(" · ", extras) : null);
        Kit.Gap(10);
        if (lines.Count == 0)
        {
            Txt.Note("No mechanics were seen in this pull.", Palette.Muted);
            return;
        }
        Kit.BeginPanel("##timeline", 8, 6, 8);
        var dl = Kit.Dl;
        var timeW = MathF.Max(S(38), Txt.Measure("00:00", Face.Mono, 12).X);
        var rowH = S(38);
        // Only the rows in view are drawn (a long pull has hundreds); the rest only take their room.
        var clipTop = ImGui.GetWindowPos().Y;
        var clipBottom = clipTop + ImGui.GetWindowSize().Y;
        for (var i = 0; i < lines.Count; i++)
        {
            var e = lines[i];
            var at = Kit.At;
            var w = Kit.Width;
            if (at.Y + rowH < clipTop || at.Y > clipBottom)
            {
                Kit.Reserve(at, w, rowH);
                continue;
            }
            var hovered = !Kit.Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(at, at + new Vector2(w, rowH));
            if (hovered) dl.AddRectFilled(at, at + new Vector2(w, rowH), Palette.U32(Palette.RowHover), S(Palette.RadiusMd));
            var x = at.X + S(10);
            var time = Words.Clock(e.T);
            Txt.Line(dl, new Vector2(x + timeW - Txt.Measure(time, Face.Mono, 12).X, at.Y), time, Face.Mono, 12, Palette.Muted, 38);
            var railX = x + timeW + S(14);
            // The rail through every row, a dot on each (as Present's timeline).
            dl.AddLine(new Vector2(railX, at.Y + (i == 0 ? rowH / 2 : 0)), new Vector2(railX, at.Y + (i == lines.Count - 1 ? rowH / 2 : rowH)), Palette.U32(Palette.LineStrong), 1f);
            dl.AddCircleFilled(new Vector2(railX, at.Y + rowH / 2), S(3.5f), Palette.U32(Palette.Ground), 12);
            dl.AddCircle(new Vector2(railX, at.Y + rowH / 2), S(3.5f), Palette.U32(Palette.Muted), 12, 1.2f);
            var nx = railX + S(16);
            var iconSize = S(18);
            var kinds = e.Kinds;
            var iconsW = kinds.Count == 0 ? 0 : iconSize * 0.8f * (Math.Min(kinds.Count, 4) - 1) + iconSize;
            var count = e.Count > 1 ? $"×{e.Count}" : null;
            var countW = count != null ? Txt.Measure(count, Face.Mono, 12).X + S(8) : 0;
            var nameW = at.X + w - S(10) - iconsW - S(12) - countW - nx;
            var nw = Txt.Line(dl, new Vector2(nx, at.Y), Txt.Fit(e.Name, Face.SansMedium, 14, nameW), Face.SansMedium, 14, Palette.Ink, 38);
            if (count != null) Txt.Line(dl, new Vector2(nx + nw + S(8), at.Y), count, Face.Mono, 12, Palette.Muted, 38);
            Glyphs.Row(dl, new Vector2(at.X + w - S(10) - iconsW, at.Y + (rowH - iconSize) / 2), iconSize, kinds);
            if (hovered) Kit.Tooltip(string.Join(", ", kinds.Select(Glyphs.Word)) + (e.Count > 1 ? $" · went off {e.Count} times" : string.Empty));
            Kit.Reserve(at, w, rowH);
        }
        Kit.EndPanel();
        Kit.Gap(12);
        Txt.Note("The Raid Planner makes a step of each moment something goes off, with the party where they stood, or in standard spots if you'd rather.", Palette.Faint);
    }
}
