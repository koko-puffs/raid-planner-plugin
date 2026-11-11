using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// A pull's own screen (the mock-ups' "A pull"): the header (the square back button, the eyebrow in
/// mono capitals, the duty as a small callout, Delete outlined in danger red at the far right), its
/// facts, the pull to its own scale (a tick a mechanic, its phases, a skull at each death), and what's
/// in it: a row a mechanic (mono time on the rail, the mechanic drawn small in a square well, what
/// it asks before what it is, "×12"), deaths as red rows. The footer is the basalt band with Send as
/// the telegraph. The pull is read from its file on a worker, once.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The open pull's view: its file, and what it shows once read (or why not).</summary>
    private sealed record PullState(string File, PullView? View, string? Problem, int Party);

    private volatile PullState? pullView;

    private void LoadPull(RecordingInfo info)
    {
        if (pullView?.File == info.File) return;
        pullView = new PullState(info.File, null, null, 0);
        var file = info.File;
        _ = Task.Run(() =>
        {
            try
            {
                var r = store.Read(info);
                var view = PullView.Build(r);
                partyCounts[file] = r.Party.Count;
                if (pullView?.File == file) pullView = new PullState(file, view, null, r.Party.Count);
            }
            catch (Exception ex)
            {
                Services.Log.Warning(ex, "Raid Planner couldn't read a pull");
                if (pullView?.File == file) pullView = new PullState(file, null, "Its file couldn't be read. Delete it, or look in the recordings folder.", 0);
            }
        });
    }

    private void DrawPull(RecordingInfo info)
    {
        LoadPull(info);
        Frame.BeginHeader();
        var eyebrow = $"Pull {Math.Max(1, info.Pull)} · {Format.Day(info.RecordedAt, DateTimeOffset.Now)}";
        if (ScreenHeader("Back", eyebrow, info.Duty, S(H), p =>
            {
                if (Kit.IconButton("##delete", p, Icon.Trash, "Delete this pull", Btn.Danger))
                    Ask("Delete this pull?", info.SentId != null
                            ? "It's gone from the plugin. The copy you sent stays in your Raid Planner."
                            : "It's gone for good: it hasn't been sent to the Raid Planner.",
                        "Delete", "Keep it", () =>
                        {
                            store.Delete(info);
                            screen = Screen.Home;
                            Notify($"Deleted pull {Math.Max(1, info.Pull)}", ToastKind.News, Icon.Trash);
                        }, danger: true, yesIcon: Icon.Trash);
            }))
            screen = Screen.Home;
        Frame.EndHeader();

        Frame.BeginBody("##pull", "pull-footer");
        Kit.Facts(new[]
        {
            new Kit.Fact("Length", Format.Clock(info.Duration)),
            new Kit.Fact("Ended", info.Outcome == Outcome.Wipe ? "Wipe" : info.Outcome == Outcome.Clear ? "Clear" : "Ended",
                Color: info.Outcome == Outcome.Wipe ? Palette.Danger : info.Outcome == Outcome.Clear ? Palette.White : Palette.Muted, Sans: true),
            new Kit.Fact("Mechanics", Format.Thousands(info.Mechanics)),
            PartyFact(info),
        });
        var state = pullView;
        if (state?.View is { } view)
        {
            Kit.Gap(14);
            DrawScale(view, info.Duration);
        }
        DrawWhatsIn(state);
        Frame.EndBody();

        Frame.BeginFooter("pull-footer");
        DrawSend("##pull-", info, "It waits in the Raid Planner under From the game. Make it a plan there: a step for each mechanic, yours to edit.", grow: true);
        Frame.EndFooter();
    }

    /// <summary>
    /// The pull to its own scale (`.scale`, 34 tall): a 6px track, a tick a mechanic, the phases the
    /// recording saw ("P1", "P2" on a hairline), a skull at each death. No enrage: a pull has none of its own.
    /// </summary>
    private static void DrawScale(PullView view, double duration)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var len = Math.Max(1, duration);
        float X(double t) => at.X + (float)Math.Clamp(t / len, 0, 1) * w;
        dl.AddRectFilled(new Vector2(at.X, at.Y + S(12)), new Vector2(at.X + w, at.Y + S(18)), Palette.U32(Palette.Track), S(2));
        if (view.Phases.Count > 1)
            for (var i = 0; i < view.Phases.Count; i++)
            {
                var x = MathF.Round(X(view.Phases[i]));
                dl.AddLine(new Vector2(x + 0.5f, at.Y), new Vector2(x + 0.5f, at.Y + S(22)), Palette.U32(Palette.Line3), 1f);
                Kit.Label(dl, new Vector2(x + S(4), at.Y), $"P{i + 1}", 9, 0.08f, Palette.Faint);
            }
        var tick = Palette.U32(Palette.Rgba(255, 255, 255, 0.5f));
        foreach (var r in view.Rows)
        {
            if (r.Death) continue;
            var x = X(r.T);
            dl.AddRectFilled(new Vector2(x - S(0.75f), at.Y + S(9)), new Vector2(x + S(0.75f), at.Y + S(21)), tick, S(1));
        }
        foreach (var t in view.Deaths)
            Icons.Draw(dl, Icon.Skull, new Vector2(X(t) - S(6), at.Y + S(20)), S(12), Palette.Danger);
        Kit.Reserve(at, w, S(34));
    }

    /// <summary>What's in it: its head, then the list (reading, failed or empty, the Boss says so), then the footnote.</summary>
    private void DrawWhatsIn(PullState? state)
    {
        Kit.Gap(22);
        var view = state?.View;
        string? aside = null;
        if (view != null)
        {
            var extras = new System.Collections.Generic.List<string>();
            if (view.Waymarks > 0) extras.Add(Words.Count(view.Waymarks, "waymark"));
            if (view.Tethers > 0) extras.Add(Words.Count(view.Tethers, "tether"));
            if (view.Deaths.Count > 0) extras.Add(Words.Count(view.Deaths.Count, "death"));
            if (view.WipeDeaths > 0) extras.Add("the wipe");
            aside = extras.Count > 0 ? string.Join(" · ", extras) : null;
        }
        Kit.Section("##whats-in", "What's in it", aside);
        Kit.Gap(10);

        if (view == null || view.Rows.Count == 0)
        {
            if (state?.Problem is { } problem) EmptyBlock(BossMood.Awake, "This pull won't open", problem);
            else if (view == null) EmptyBlock(null, "Reading it", null);
            else EmptyBlock(BossMood.Asleep, "Nothing went off", "No mechanics were seen in this pull.");
            return;
        }

        // The list in its slab, scrolling inside it; the footnote under it.
        const string foot = "The Raid Planner makes a step of each moment something goes off, with the party where they stood, or in standard spots if you'd rather.";
        var footH = Txt.Flow(Vector2.Zero, Kit.Width, new[] { Run.Note(foot) }, 19.5f, out _, draw: false);
        var rowH = S(42);
        var listH = rowH * view.Rows.Count + S(6) * 2;
        var room = Frame.RoomLeft() - S(12) - footH;
        var h = MathF.Max(MathF.Min(listH, room), MathF.Min(listH, rowH * 4 + S(12)));
        DrawRows(view, h);
        Kit.Gap(12);
        Txt.Note(foot);
    }

    /// <summary>The Boss (or the party running, while it's read), a small callout and a line, centred.</summary>
    private static void EmptyBlock(BossMood? mood, string title, string? line)
    {
        var dl = Kit.Dl;
        var w = Kit.Width;
        Kit.BeginPanel("##pull-empty", 26, 18, 26, Palette.Slab);
        var at = Kit.At;
        var c = new Vector2(at.X + Kit.Width / 2, at.Y + S(40));
        if (mood is { } m) Boss.Empty(dl, c, 58, m);
        else
        {
            // The party runs to its spots, as the site's loading does.
            string[] spots = { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" };
            var x = c.X - (S(20) * 8 + S(4) * 7) / 2 + S(10);
            var t = Running(1.4f);
            for (var i = 0; i < 8; i++)
            {
                var lift = ReducedMotion ? 0 : MathF.Max(0, MathF.Sin((t * 8 - i) * MathF.PI / 2)) * S(2);
                Kit.Spot(dl, new Vector2(x, c.Y - lift), 20, spots[i], Palette.SpotColor(spots[i]));
                x += S(24);
            }
        }
        Kit.Reserve(at, Kit.Width, S(80));
        Kit.Gap(8.8f);
        var p = Kit.At;
        var ch = Kit.Callout(dl, p, title, 13, Kit.Width, 2, 1.1f, center: true);
        Kit.Reserve(p, Kit.Width, ch);
        Kit.Gap(8.8f);
        if (line != null) Txt.Note(line, center: true);
        else
        {
            var bw = MathF.Min(S(200), Kit.Width);
            var bp = Kit.At;
            Kit.CastBar(dl, new Vector2(bp.X + (Kit.Width - bw) / 2, bp.Y), bw, Running(1.4f));
            Kit.Reserve(bp, Kit.Width, S(6));
        }
        Kit.EndPanel();
        _ = w;
    }

    /// <summary>
    /// The rows (`.ml`, 42 tall): the time in mono right-aligned, the rail with a hollow dot, the
    /// mechanic in a 30px square well, what it asks and its name, "×12"; a death in red with its spot.
    /// Only the rows in view are drawn; the words fade at the foot when there's more.
    /// </summary>
    private static void DrawRows(PullView view, float height)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var outer = Kit.Dl;
        var r = S(Palette.RadiusLg);
        outer.AddRectFilled(at, at + new Vector2(w, height), Palette.U32(Palette.Slab), r);
        ImGui.SetCursorScreenPos(at + new Vector2(1));
        var open = ImGui.BeginChild("##pull-rows", new Vector2(w - 2, height - 2), false, ImGuiWindowFlags.None);
        Frame.Open.Push(false);
        try
        {
            if (!open) return;
            var dl = ImGui.GetWindowDrawList();
            var rowH = S(42);
            var start = ImGui.GetCursorScreenPos() + new Vector2(S(5), S(5));
            var rw = ImGui.GetContentRegionAvail().X - S(10);
            var scroll = ImGui.GetScrollY();
            var viewH = height - 2;
            var timeW = S(38);
            var gap = S(10);
            var railX = start.X + S(8) + timeW + gap + S(7);
            // The rail through every row (`.mlist::before`), from the first row's middle to the last's.
            dl.AddLine(new Vector2(railX, start.Y + rowH / 2), new Vector2(railX, start.Y + rowH * (view.Rows.Count - 0.5f)), Palette.U32(Palette.Line3), 1f);
            var first = Math.Max(0, (int)(scroll / rowH) - 1);
            var last = Math.Min(view.Rows.Count, first + (int)(viewH / rowH) + 3);
            for (var i = first; i < last; i++)
            {
                var row = view.Rows[i];
                var min = start + new Vector2(0, rowH * i);
                var max = min + new Vector2(rw, rowH);
                var hovered = Kit.Pointed(min, max);
                if (hovered) dl.AddRectFilled(min, max, Palette.U32(Palette.Hover), S(Palette.RadiusMd));
                Kit.Reticle(dl, $"##ml{i}", min, max, hovered);
                var x = min.X + S(8);
                var time = Format.Clock(row.T);
                Txt.LineRight(dl, x + timeW, min.Y, time, Face.Mono, 12, row.Death ? Palette.Danger : Palette.Muted, 42);
                var dot = new Vector2(railX, min.Y + rowH / 2);
                if (row.Death) dl.AddCircleFilled(dot, S(3.5f), Palette.U32(Palette.Danger), 12);
                else
                {
                    dl.AddCircleFilled(dot, S(3.5f), Palette.U32(Palette.Void), 12);
                    dl.AddCircle(dot, S(3.5f) - S(0.7f), Palette.U32(Palette.Faint), 12, S(1.4f));
                }
                x += timeW + gap + S(14) + gap;
                var well = new Vector2(x, min.Y + (rowH - S(30)) / 2);
                if (row.Death) Kit.IconWell(dl, well, 30, Icon.Skull, WellKind.Danger);
                else
                {
                    Kit.Well(dl, well, 30);
                    if (row.Kinds.Count > 0) Glyphs.Draw(dl, well + new Vector2(S(3)), S(24), row.Kinds[0]);
                }
                x += S(30) + gap;
                var count = row.Count > 1 ? $"×{row.Count}" : null;
                var countW = count != null ? Txt.Width(count, Face.Mono, 12) + gap : 0;
                var nameRoom = max.X - S(8) - countW - x;
                if (row.Death)
                {
                    Kit.Spot(dl, new Vector2(x + S(10), min.Y + rowH / 2), 20, row.DeathSlot!, Palette.SpotColor(row.DeathSlot));
                    x += S(20) + S(9.6f);
                    Txt.Line(dl, new Vector2(x, min.Y), Txt.Fit(row.Name, Face.SansMedium, 14, nameRoom - S(30)), Face.SansMedium, 14, Palette.DangerName, 42);
                }
                else
                {
                    if (row.Kinds.Count > 0 && Glyphs.IntentOf(row.Kinds[0]) is { } intent)
                    {
                        var iw = Kit.IntentMark(dl, new Vector2(x, min.Y), rowH, intent);
                        x += iw + S(9.6f);
                    }
                    Txt.Line(dl, new Vector2(x, min.Y), Txt.Fit(row.Name, Face.SansMedium, 14, max.X - S(8) - countW - x), Face.SansMedium, 14, Palette.White, 42);
                }
                if (count != null) Txt.LineRight(dl, max.X - S(8), min.Y, count, Face.Mono, 12, Palette.Muted, 42);
                if (hovered && !row.Death && row.Kinds.Count > 0)
                    Kit.Tooltip(string.Join(", ", row.Kinds.Select(Glyphs.Word)) + (row.Count > 1 ? $" · went off {row.Count} times" : string.Empty));
            }
            ImGui.SetCursorScreenPos(start);
            ImGui.Dummy(new Vector2(rw, rowH * view.Rows.Count + S(5)));
            // More below: the rows fade at the foot (`.fade-b`).
            if (scroll + viewH < rowH * view.Rows.Count + S(10) - 1)
            {
                var top = ImGui.GetWindowPos().Y + viewH - S(56);
                var a = new Vector2(ImGui.GetWindowPos().X, top);
                var b = new Vector2(ImGui.GetWindowPos().X + w - S(8), top + S(56));
                var bg = Palette.Slab;
                dl.AddRectFilledMultiColor(a, b, Palette.U32(Palette.WithAlpha(bg, 0)), Palette.U32(Palette.WithAlpha(bg, 0)), Palette.U32(bg), Palette.U32(bg));
            }
        }
        finally
        {
            if (Frame.Open.Count > 0) Frame.Open.Pop();
            ImGui.EndChild();
        }
        outer.AddRect(at + new Vector2(0.5f), at + new Vector2(w, height) - new Vector2(0.5f), Palette.U32(Palette.Line), r, ImDrawFlags.None, 1f);
        Kit.Reserve(at, w, height);
    }
}
