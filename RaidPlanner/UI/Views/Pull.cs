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
/// A pull's own screen (0.8.0, the mock-ups' plug-main "A pull"), under the tabs (the Pulls tab's): All pulls to go
/// back, Save as file and Delete (asked first); the eyebrow (when, the duty), the pull's number as a callout with its
/// outcome and length; the pull in a sentence; the pull to its own scale (its phases, a skull at each death); its
/// upload (On your Raid Codex since 23:49 with Open in Review, or the send); and what happened: a row a mechanic
/// (mono time on the rail, the mechanic drawn small in a square well, what it asks before what it is, "×12"), deaths
/// as red rows with who died to what. The pull is read from its file on a worker, once.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The open pull's view: its file, and what it shows once read (or why not).</summary>
    private sealed record PullState(string File, PullView? View, string? Problem);

    private volatile PullState? pullView;

    /// <summary>The pull's two views (0.9.0): what happened, and the pull played back.</summary>
    private static readonly string[] PullModes = { "Summary", "Replay" };

    private void LoadPull(RecordingInfo info)
    {
        if (pullView?.File == info.File) return;
        pullView = new PullState(info.File, null, null);
        var file = info.File;
        _ = Task.Run(() =>
        {
            try
            {
                var r = store.Read(info);
                var view = PullView.Build(r);
                // Its facts come from this read too: the list never reads the same file again for them.
                gists[file] = new GistState(PullGist.Of(r, view));
                if (pullView?.File == file) pullView = new PullState(file, view, null);
            }
            catch (Exception ex)
            {
                Services.Log.Warning(ex, "Raid Codex couldn't read a pull");
                if (pullView?.File == file) pullView = new PullState(file, null, "Its file couldn't be read. Delete it, or look in the recordings folder.");
            }
        });
    }

    private void DrawPull(RecordingInfo info)
    {
        LoadPull(info);
        Frame.BeginHeader();
        NavTabs();
        Kit.Gap(10);
        var row = Kit.At;
        var w = Kit.Width;
        if (Kit.Button("##pull-back", row, "All pulls", Btn.Quiet, BtnSize.Sm, Icon.ChevronLeft)) screen = Screen.Pulls;
        const string delete = "Delete";
        const string save = "Save as file";
        var dw = Kit.ButtonWidth(delete, BtnSize.Sm, Icon.Trash, Btn.Quiet);
        var sw = Kit.ButtonWidth(save, BtnSize.Sm, Icon.Download, Btn.Quiet);
        if (Kit.Button("##pull-save", new Vector2(row.X + w - dw - S(6.4f) - sw, row.Y), save, Btn.Quiet, BtnSize.Sm, Icon.Download, 0, false,
                "A file to import in the Raid Codex yourself (Import on its front page), or to share."))
            SaveAs(info);
        if (Kit.Button("##delete", new Vector2(row.X + w - dw, row.Y), delete, Btn.Quiet, BtnSize.Sm, Icon.Trash, 0, false, "Delete this pull"))
            Ask("Delete this pull?", info.SentId != null
                    ? "It's gone from the plugin. The copy on your Raid Codex stays there."
                    : "It's gone for good: it isn't on your Raid Codex.",
                "Delete", "Keep it", () =>
                {
                    // Out of the list at once; its file's delete and the index's and marks' writes on a worker, never in a frame.
                    _ = store.DeleteLater(info).ContinueWith(t => Services.Log.Error(t.Exception!, "Raid Codex couldn't delete a pull"),
                        TaskContinuationOptions.OnlyOnFaulted);
                    screen = Screen.Pulls;
                    Notify($"Deleted pull {Math.Max(1, info.Pull)}", ToastKind.News, Icon.Trash);
                }, danger: true, yesIcon: Icon.Trash);
        Kit.Reserve(row, w, S(HSm));
        Frame.EndHeader();

        Frame.BeginBody("##pull", null, 14);
        var dl = Kit.Dl;
        w = Kit.Width;
        var ep = Kit.At;
        Kit.Eyebrow(dl, ep, $"{Capital(Format.Day(info.RecordedAt, DateTimeOffset.Now))} · {info.Duty}", null, w);
        Kit.Reserve(ep, w, Kit.EyebrowH);
        Kit.Gap(10);

        // PULL 12 [WIPE] ……… 6:31 [Summary | Replay] (0.9.0: the pull played back, Replay.cs)
        var at = Kit.At;
        var (tag, kind) = OutcomeTag(info.Outcome);
        var length = Format.Clock(info.Duration);
        var lengthW = Txt.Width(length, Face.MonoSemibold, 18);
        var rowH = S(26);
        var title = $"Pull {Math.Max(1, info.Pull)}";
        var modeW = Kit.SegmentedWidth(PullModes);
        // On the Raid Codex: a link to it there beside the switch, while Replay is shown (its summary has its own).
        const string openSite = "Open on the Raid Codex";
        var sentId = pullReplay ? (store.Find(info.File) ?? info).SentId : null;
        var openW = sentId != null ? Kit.ButtonWidth(openSite, BtnSize.Sm, Icon.ExternalLink, Btn.Quiet) + S(8) : 0;
        var room = w - lengthW - S(12) - Kit.TagWidth(tag, kind) - S(12) - modeW - S(16) - openW;
        var line = Kit.CalloutLines(title, 22, room, 1)[0];
        var tw = Txt.Line(dl, at, line, Face.Callout, 22, Palette.White, 26);
        Kit.Tag(dl, new Vector2(at.X + tw + S(12), at.Y + (rowH - S(22)) / 2), tag, kind);
        Txt.LineRight(dl, at.X + w - modeW - S(16) - openW, at.Y, length, Face.MonoSemibold, 18, Palette.White, 26);
        if (sentId != null
            && Kit.Button("##pull-open-site", new Vector2(at.X + w - modeW - openW, at.Y + (rowH - S(HSm)) / 2), openSite, Btn.Quiet, BtnSize.Sm, Icon.ExternalLink))
            Dalamud.Utility.Util.OpenLink(ReviewUrl(info, sentId));
        var mode = Kit.Segmented("##pull-mode", new Vector2(at.X + w - modeW, at.Y + (rowH - S(HSm)) / 2), PullModes, pullReplay ? 1 : 0);
        if (mode != (pullReplay ? 1 : 0))
        {
            pullReplay = mode == 1;
            replayPlaying = false;
            ImGui.SetScrollY(0);
        }
        Kit.Reserve(at, w, rowH);
        if (pullReplay)
        {
            DrawReplay(info);
            Frame.EndBody();
            return;
        }
        Kit.Gap(10);

        var state = pullView;
        var before = Before(info);
        var gist = GistOf(info, false);
        Txt.Para(Runs(PullGist.Sentence(info.Outcome, info.Duration, gist, before is { } b ? (b.Pull, b.Duration) : null)), 20);
        if (state?.View is { } view)
        {
            Kit.Gap(14);
            DrawScale(view, info.Duration);
        }
        Kit.Gap(14);
        DrawPullUpload(info);
        DrawWhatsIn(state);
        Frame.EndBody();
    }

    /// <summary>
    /// The pull's upload: on your Raid Codex, a box with a tick, since when, and Open in Review as the telegraph (Send
    /// another copy under it); else where Upload my logs stands with it, and the send as it's always been (link, send,
    /// sending, failed).
    /// </summary>
    private void DrawPullUpload(RecordingInfo info)
    {
        sends.TryGetValue(info.File, out var s);
        var now = store.Find(info.File) ?? info;
        if (now.SentId is { } sentId && s is not { Result: null } && !(s?.Result is { Sent: false } r && SendFailure.Of(r) is { Fix: not SendFix.Stopped }))
        {
            Kit.BeginPanel("##pull-sent", 12, 14, 12, Palette.Slab, Palette.Line, Palette.RadiusMd + 2);
            var at = Kit.At;
            var w = Kit.Width;
            var dl = Kit.Dl;
            const string open = "Open in Review";
            var bw = Kit.ButtonWidth(open, BtnSize.Sm, Icon.ExternalLink, Btn.Tele);
            var h = S(HSm);
            Kit.Tick(dl, new Vector2(at.X + S(8), at.Y + h / 2), 16);
            var text = PullGist.SinceLine(now.SentAt ?? now.RecordedAt, DateTimeOffset.Now);
            var tx = at.X + S(16) + S(10);
            Txt.Line(dl, new Vector2(tx, at.Y), Txt.Fit(text, Face.Sans, 13, at.X + w - bw - S(12) - tx), Face.Sans, 13, Palette.Text, HSm);
            if (Kit.Button("##pull-review", new Vector2(at.X + w - bw, at.Y), open, Btn.Tele, BtnSize.Sm, Icon.ExternalLink))
                Dalamud.Utility.Util.OpenLink(ReviewUrl(info, sentId));
            Kit.Reserve(at, w, h);
            Kit.EndPanel();
            Kit.Gap(8);
            var note = s?.Result is { Thinned: true } ? "Sent with fewer of the party's places: it was too big whole. " : string.Empty;
            if (Txt.Para(new[] { new Run(note, Face.Sans, 12, Palette.Faint), new Run("Send another copy", Face.SansMedium, 12, Palette.White, "again") }, 17) == "again")
                Send(info);
            return;
        }
        if (s is not { Result: null } && UploadWaitLine(info) is { } waiting)
        {
            var at = Kit.At;
            var w = Kit.Width;
            Icons.Draw(Kit.Dl, Icon.Clock, new Vector2(at.X, at.Y + (S(19) - S(15)) / 2), S(15), Palette.Faint);
            Txt.Line(Kit.Dl, new Vector2(at.X + S(23), at.Y), Txt.Fit(waiting, Face.Sans, 13, w - S(23)), Face.Sans, 13, Palette.Muted, 19);
            Kit.Reserve(at, w, S(19));
            Kit.Gap(10);
        }
        DrawSend("##pull-", info, "It waits in the Raid Codex under From the game. Make it a plan there: a step for each mechanic, yours to edit.", grow: true, withSave: false);
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

    /// <summary>What happened: its head, then the list (reading, failed or empty, the Boss says so).</summary>
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
        Kit.Section("##whats-in", "What happened", aside);
        Kit.Gap(10);

        if (view == null || view.Rows.Count == 0)
        {
            if (state?.Problem is { } problem) EmptyBlock(BossMood.Awake, "This pull won't open", problem);
            else if (view == null) EmptyBlock(null, "Reading it", null);
            else EmptyBlock(BossMood.Asleep, "Nothing went off", "No mechanics were seen in this pull.");
            return;
        }

        // The list in its slab, scrolling inside it, filling what's left of the window.
        var rowH = S(42);
        var listH = rowH * view.Rows.Count + S(6) * 2;
        var room = Frame.RoomLeft();
        var h = MathF.Max(MathF.Min(listH, room), MathF.Min(listH, rowH * 4 + S(12)));
        DrawRows(view, h);
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
                    PlateArt.JobBadge(dl, new Vector2(x + S(10), min.Y + rowH / 2), 20, row.DeathJob, Format.RoleOfSlot(row.DeathSlot), MeterRoleColours.Theme, false, row.DeathSlot);
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
