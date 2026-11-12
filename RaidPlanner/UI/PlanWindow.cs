using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using RaidPlanner.Core;
using RaidPlanner.Game;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The plan in the game (the mock-ups' "The plan window"): a small window to put anywhere and lock
/// there. Its eyebrow as Present writes it ("13 / 19 · 2:44 · P2", the step's number in ember), the
/// step's callout big in Archivo with its name under it, a slim cast bar filling to the next step
/// while a pull moves it, the step's board in the board's language, its notes (fading at the foot
/// when there's more), and previous / next. Its list button picks another of your plans, for any
/// duty; with nothing to show, the Boss says why.
///
/// What's drawn is worked out once a step (the board's objects, the words, the notes wrapped to the
/// width), so a frame only draws. Nothing in Draw is let throw.
/// </summary>
public sealed class PlanWindow : Window
{
    private const string PlansMenu = "##rp-plans";

    private readonly Configuration config;
    private readonly PlanService plans;
    private readonly FightRecorder recorder;
    private readonly GameData data;
    private readonly Fonts fonts;

    /// <summary>What the step shown draws, made once a step.</summary>
    private sealed class StepView
    {
        public required Plan Plan;
        public required int Step;
        public required PlanArena Arena;
        public required List<DrawnObj> Objects;
        public required string Big;
        public required string? Name;
        public required string Lead;
        public required string Eyebrow;
        public required string Count;
        public required string Notes;
        public required double? CueAt;
        public required double? NextAt;

        /// <summary>The step's planned cooldowns (since 0.5.0), in the order they go.</summary>
        public required IReadOnlyList<PlanCooldown> Cooldowns;

        /// <summary>The smallest window's one line for a spot (<see cref="SmallFor"/>), and its tooltip; made again when your spot changes.</summary>
        public string? SmallFor;
        public string? SmallLine;
        public string? CooldownsTip;
        public string? SmallMore;
        public int SmallMoreCount;
        public string? Hidden;
        public List<string> Lines = new();
        public float LinesWidth = -1;
        public float LinesScale;
        public bool LinesReady;
    }

    private StepView? view;
    private int viewVersion = -1;
    private string?[] phases = Array.Empty<string?>();
    private Plan? phasesOf;

    /// <summary>The plans menu's rows, made again when the list changes.</summary>
    private readonly List<(PlanListItem Item, bool Here)> menuRows = new();
    private int menuVersion = -1;

    private DateTime drawFailedAt;
    private bool askedForList;

    public PlanWindow(Configuration config, PlanService plans, FightRecorder recorder, GameData data, Fonts fonts)
        : base("Raid plan###RaidPlannerPlan")
    {
        this.config = config;
        this.plans = plans;
        this.recorder = recorder;
        this.data = data;
        this.fonts = fonts;
        Size = new Vector2(340, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(240, 300), MaximumSize = new Vector2(1200, 1600) };
        // A plan on screen during a fight shouldn't close on Escape, take the game's focus, or click.
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
    }

    /// <summary>Opens Settings on Account (set by the plugin), for Link your account.</summary>
    public Action? OpenSettings { get; set; }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing;
        // Locked: Dalamud's own moving and resizing are off too.
        if (config.PlanWindowLocked) Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus;
        Theme.PushFrame();
    }

    public override void PostDraw() => Theme.Pop();

    public override void OnOpen() => askedForList = false;

    public override void Draw()
    {
        Kit.ResetFrame();
        Frame.ResetFrame();
        Theme.PushInside();
        try
        {
            using (fonts.Push(Face.Sans, 14))
            {
                try
                {
                    Frame.Window();
                    DrawInside();
                }
                finally
                {
                    Frame.Unwind();
                }
            }
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - drawFailedAt > TimeSpan.FromSeconds(10))
            {
                drawFailedAt = DateTime.UtcNow;
                Services.Log.Error(ex, "Raid Planner's plan window couldn't draw");
            }
        }
        finally
        {
            Theme.Pop();
        }
    }

    /// <summary>The smallest window (240 × 300) draws a smaller callout and board, and says the notes are there.</summary>
    private static bool Small(float w, float h) => w < S(270) || h < S(400);

    private void DrawInside()
    {
        var win = ImGui.GetWindowPos();
        var top = ImGui.GetCursorScreenPos();
        var width = ImGui.GetWindowWidth();
        var small = Small(width, win.Y + ImGui.GetWindowHeight() - top.Y);
        var padX = S(small ? 12 : 14);
        var left = win.X + padX;
        var inner = width - padX * 2;
        var bottom = win.Y + ImGui.GetWindowHeight() - S(small ? 10 : 12);
        Kit.PushBox(left, inner);
        try
        {
            var y = top.Y + S(small ? 8 : 10);
            if (plans.Plan is { } plan)
            {
                Refresh(plan);
                DrawPlan(view!, left, y, inner, bottom, small);
            }
            else
            {
                DrawEmpty(left, y, inner, small);
            }
            DrawPlansMenu();
        }
        finally
        {
            Kit.PopBox();
        }
    }

    /// <summary>The step's view, made again when the plan or the step changes.</summary>
    private void Refresh(Plan plan)
    {
        if (view != null && viewVersion == plans.Version && ReferenceEquals(view.Plan, plan) && view.Step == plans.Step) return;
        viewVersion = plans.Version;
        var step = Math.Clamp(plans.Step, 0, plan.Steps.Count - 1);
        if (view != null && ReferenceEquals(view.Plan, plan) && view.Step == step) return;
        if (!ReferenceEquals(phasesOf, plan))
        {
            phasesOf = plan;
            phases = PlanJson.Phases(plan.Steps);
        }
        var s = plan.Steps[step];
        var objects = PlanBoard.Prepare(s.Objects, out var hidden);
        var name = PlanJson.WithoutPhase(s.Name);
        var rest = $" / {plan.Steps.Count}" + (s.Time.Length > 0 ? $" · {s.Time}" : string.Empty) + (phases[step] is { } ph ? $" · {ph}" : string.Empty);
        var (cueAt, nextAt) = StepProgress.Window(plan.Game?.Cues, step);
        view = new StepView
        {
            Plan = plan,
            Step = step,
            Arena = plan.ArenaAt(step),
            Objects = objects,
            Big = s.Big,
            // The name under the callout, when the callout isn't just the name again.
            Name = !string.IsNullOrWhiteSpace(s.Callout) && !string.Equals(s.Callout.Trim(), name, StringComparison.OrdinalIgnoreCase) ? name : null,
            Lead = Format.StepNumber(step + 1),
            Eyebrow = rest,
            Count = $"{step + 1} / {plan.Steps.Count}",
            Notes = s.Notes,
            CueAt = cueAt,
            NextAt = nextAt,
            Cooldowns = s.Cooldowns,
            Hidden = hidden > 0 ? $"{hidden} more objects not drawn" : null,
        };
        // A real arena's floor is cut into triangles here, once a plan (kept with its arena), never as a frame draws.
        ArenaFloor.Of(view.Arena);
    }

    private void DrawPlan(StepView v, float left, float y, float w, float bottom, bool small)
    {
        var dl = Kit.Dl;
        // The eyebrow, with the list and the lock.
        var headH = S(small ? 28 : 32);
        var buttons = S(HSm) * (small ? 1 : 2) + (small ? 0 : S(6.4f));
        Kit.Eyebrow(dl, new Vector2(left, y + (headH - Kit.EyebrowH) / 2), v.Eyebrow, v.Lead, w - buttons - S(8));
        HeaderButtons(new Vector2(left + w - buttons, y + (headH - S(HSm)) / 2), true, !small);
        y += headH;

        // Another plan on its way (the old step stays until it's in), or one that couldn't be read.
        if (plans.Loading != null)
        {
            y += S(6);
            y += Kit.Cast(dl, new Vector2(left, y), w, "Opening the plan", null, Running(1.4f), true, Face.SansMedium, 12, Palette.Text);
        }
        else if (plans.PlanWhy is { } why)
        {
            y += S(6);
            var lh = S(HSm);
            Icons.Draw(dl, Icon.CircleX, new Vector2(left, y + (lh - S(Kit.IconPx)) / 2), S(Kit.IconPx), Palette.Danger);
            Txt.Line(dl, new Vector2(left + S(Kit.IconPx + 8), y), Txt.Fit(why, Face.Sans, 12, w - S(Kit.IconPx + 8) - S(HSm) - S(6)), Face.Sans, 12, Palette.DangerSoft, HSm);
            if (Kit.IconButton("##rp-retry-top", new Vector2(left + w - S(HSm), y), Icon.RefreshCw, "Try again", Btn.Quiet, BtnSize.Sm)) plans.Retry();
            y += lh;
        }

        // The step's callout, big (three lines at most, at its smallest too); its name under it.
        var px = small ? 16 : 22;
        y += S(small ? 4 : 6);
        y += Kit.Callout(dl, new Vector2(left, y), v.Big, px, w, 3, 1.02f);
        if (v.Name != null && !small)
        {
            y += S(3);
            Txt.Line(dl, new Vector2(left, y), Txt.Fit(v.Name, Face.Sans, 13, w), Face.Sans, 13, Palette.Muted, 19.5f);
            y += S(19.5f);
        }

        // Following the pull: a slim cast bar filling to the next step, with when it comes.
        if (plans.Following && recorder.InPull && !small)
        {
            y += S(9);
            var now = recorder.Elapsed;
            var clock = plans.PlanClock;
            var (fraction, nextIn) = StepProgress.Of(v.CueAt, v.NextAt, clock);
            var time = Format.Clock(now) + (nextIn is { } n ? $" · next {Format.InSeconds(n)}" : string.Empty);
            y += Kit.Cast(dl, new Vector2(left, y), w, "Following the pull", time, fraction, true, Face.SansMedium, 12, Palette.Text);
        }

        // The step's planned cooldowns (since 0.5.0): who uses what and when, yours washed in ember.
        if (v.Cooldowns.Count > 0)
        {
            y += S(small ? 6 : 10);
            // As many rows as leave the board its least (110) above the footer: fewer (the last "+3 more"), else the one line.
            var fit = small ? 0 : (int)MathF.Floor((bottom - S(HSm) - S(8) - S(110) - S(10) - y) / S(20));
            y += Cooldowns(v, left, y, w, Math.Min(CooldownRows, fit));
        }

        // The footer: previous, where you are, next. At its smallest there's none (as the mock-up draws
        // it: the callout, the board and the notes' line take the room; /raidplan next and prev still move it).
        var footH = small ? 0 : S(HSm);
        var footY = bottom - footH;

        // The board, as big as leaves room for a few lines of notes.
        y += S(small ? 8 : 10);
        var notesMin = small ? 0 : S(19.5f) * 3;
        var room = footY - S(8) - notesMin - S(8) - y;
        var board = small ? MathF.Min(S(110), MathF.Min(w, room)) : MathF.Max(S(110), MathF.Min(w, room));
        board = MathF.Max(S(60), MathF.Min(board, w));
        var boardX = left + (w - board) / 2;
        PlanBoard.Draw(dl, new Vector2(boardX, y), board, v.Arena, v.Objects, small || board < S(160), You());
        if (v.Hidden != null && Kit.Pointed(new Vector2(boardX, y), new Vector2(boardX + board, y + board))) Kit.Tooltip(v.Hidden);
        y += board + S(8);

        // The notes, scrolling when long; too small a window says they're there.
        var notesH = footY - (small ? 0 : S(8)) - y;
        Notes(v, left, y, w, notesH, small);

        // Previous and next: 32px squares, the count in mono between them.
        if (!small)
        {
            if (Kit.IconButton("##rp-prev", new Vector2(left, footY), Icon.ChevronLeft, "Previous step", Btn.Outline, BtnSize.Sm, v.Step == 0)) plans.Move(-1);
            if (Kit.IconButton("##rp-next", new Vector2(left + w - S(HSm), footY), Icon.ChevronRight, "Next step", Btn.Outline, BtnSize.Sm,
                    v.Step >= v.Plan.Steps.Count - 1))
                plans.Move(1);
            var cw = Txt.Width(v.Count, Face.Mono, 13);
            Txt.Line(dl, new Vector2(left + (w - cw) / 2, footY), v.Count, Face.Mono, 13, Palette.Muted, HSm);
        }
        ImGui.SetCursorScreenPos(new Vector2(left, bottom));
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>Your spot (MT, H1…) from the party now (<see cref="PartyMembers.YourSpot"/>, read once a second).</summary>
    private string? You()
    {
        try
        {
            return PartyMembers.YourSpot(data);
        }
        catch (Exception)
        {
            // The party can't be read this frame (a zone change): no reticle, not an error.
            return null;
        }
    }

    /// <summary>The most planned cooldowns listed in the window (past it, the last row says how many more; all in its tooltip).</summary>
    private const int CooldownRows = 4;

    /// <summary>
    /// The step's planned cooldowns (since 0.5.0): a row each (20 tall), the spot in its role's colour, the action, its time off the
    /// step's ("−3 s") in mono, its note muted; yours washed in ember. Four at most (the fourth "+3 more" past that, every one in its
    /// tooltip); fewer when the window is short (<paramref name="rows"/>). At its smallest, or with room for under two rows, one
    /// line: yours ("You: Reprisal"), else how many. Returns the height used.
    /// </summary>
    private float Cooldowns(StepView v, float left, float y, float w, int rows)
    {
        var dl = Kit.Dl;
        var you = You();
        var lh = S(20);
        if (rows < 2)
        {
            // Made once a step and spot, not each frame.
            if (v.SmallLine == null || v.SmallFor != you)
            {
                v.SmallFor = you;
                v.SmallLine = PlannedUses.YouLine(v.Plan.Steps[v.Step], you) ?? (v.Cooldowns.Count == 1 ? "1 cooldown planned" : $"{v.Cooldowns.Count} cooldowns planned");
            }
            var line = v.SmallLine;
            Txt.Line(dl, new Vector2(left, y), Txt.Fit(line, Face.Sans, 13, w), Face.Sans, 13, line.StartsWith("You:", StringComparison.Ordinal) ? Palette.White : Palette.Muted, 20);
            if (Kit.Pointed(new Vector2(left, y), new Vector2(left + w, y + lh))) Kit.Tooltip(v.CooldownsTip ??= CooldownsTip(v.Cooldowns));
            return lh;
        }
        var shown = v.Cooldowns.Count <= rows ? v.Cooldowns.Count : rows - 1;
        for (var i = 0; i < shown; i++)
        {
            var c = v.Cooldowns[i];
            var top = y + lh * i;
            var mine = c.Spot == you;
            if (mine) dl.AddRectFilled(new Vector2(left - S(4), top), new Vector2(left + w + S(4), top + lh), Palette.U32(Palette.EmberWash), S(4));
            Kit.Spot(dl, new Vector2(left + S(8), top + lh / 2), 16, c.Spot, Palette.SpotColor(c.Spot), 1.5f);
            var x = left + S(16 + 8);
            var right = left + w;
            if (c.At != 0) right -= Txt.LineRight(dl, right, top, c.AtWords, Face.Mono, 11, Palette.Muted, 20) + S(8);
            var nw = Txt.Line(dl, new Vector2(x, top), Txt.Fit(c.Name, Face.SansSemibold, 13, MathF.Max(0, right - x)), Face.SansSemibold, 13, mine ? Palette.White : Palette.Text, 20);
            if (c.Note is { } note && right - (x + nw + S(8)) > S(24))
                Txt.Line(dl, new Vector2(x + nw + S(8), top), Txt.Fit(note, Face.Sans, 12, right - (x + nw + S(8))), Face.Sans, 12, Palette.Muted, 20);
            if (Kit.Pointed(new Vector2(left, top), new Vector2(left + w, top + lh))) Kit.Tooltip(CooldownTip(c));
        }
        if (shown < v.Cooldowns.Count)
        {
            var top = y + lh * shown;
            // "+3 more", made again only when the count left over changes (the window made shorter or taller).
            var more = v.Cooldowns.Count - shown;
            if (v.SmallMore == null || v.SmallMoreCount != more)
            {
                v.SmallMoreCount = more;
                v.SmallMore = $"+{more} more";
            }
            Txt.Line(dl, new Vector2(left + S(16 + 8), top), v.SmallMore, Face.Sans, 12, Palette.Muted, 20);
            if (Kit.Pointed(new Vector2(left, top), new Vector2(left + w, top + lh))) Kit.Tooltip(v.CooldownsTip ??= CooldownsTip(v.Cooldowns));
            shown++;
        }
        return lh * shown;
    }

    private static string CooldownTip(PlanCooldown c) =>
        $"{c.Spot} · {c.Name}{(c.At != 0 ? $" {c.AtWords}" : string.Empty)}{(c.Note is { } n ? $" · {n}" : string.Empty)}";

    private static string CooldownsTip(IReadOnlyList<PlanCooldown> list) => string.Join("\n", list.Select(CooldownTip));

    /// <summary>The notes, wrapped once for the width, in a part of their own that scrolls, fading at the foot when there's more.</summary>
    private void Notes(StepView v, float left, float y, float w, float h, bool small)
    {
        var dl = Kit.Dl;
        if (v.Notes.Length == 0)
        {
            if (h >= S(19.5f)) Txt.Line(dl, new Vector2(left, y), "No notes for this step.", Face.Sans, 13, Palette.Muted, 19.5f);
            return;
        }
        var scale = ImGuiHelpers.GlobalScale;
        // Wrapped again for a new width or scale, and until the face is built (measured in Dalamud's font before).
        if (MathF.Abs(v.LinesWidth - w) > 0.5f || v.LinesScale != scale || !v.LinesReady)
        {
            v.LinesWidth = w;
            v.LinesScale = scale;
            v.LinesReady = Txt.Ready(Face.Sans, 13);
            v.Lines = Wrap(v.Notes, w - S(10));
        }
        var lh = S(19.5f);
        if (small || h < lh * 1.5f)
        {
            // Too small for the notes: say they're there, rather than hide them without a word.
            if (h < S(16)) return;
            Icons.Draw(dl, Icon.ChevronDown, new Vector2(left, y + (lh - S(Kit.IconPx)) / 2), S(Kit.IconPx), Palette.Muted);
            var words = v.Lines.Count == 1 ? "Notes: 1 line, make it taller" : $"Notes: {v.Lines.Count} lines, make it taller";
            Txt.Line(dl, new Vector2(left + S(Kit.IconPx + 5.6f), y), Txt.Fit(words, Face.Sans, 13, w - S(Kit.IconPx + 5.6f)), Face.Sans, 13, Palette.Muted, 19.5f);
            return;
        }
        ImGui.SetCursorScreenPos(new Vector2(left, y));
        var open = ImGui.BeginChild("##rp-notes", new Vector2(w, h), false, ImGuiWindowFlags.None);
        Frame.Open.Push(false);
        try
        {
            if (!open) return;
            var cdl = ImGui.GetWindowDrawList();
            var start = ImGui.GetCursorScreenPos();
            var scroll = ImGui.GetScrollY();
            // Only the lines in view are drawn.
            var first = Math.Max(0, (int)(scroll / lh) - 1);
            var last = Math.Min(v.Lines.Count, first + (int)(h / lh) + 3);
            for (var i = first; i < last; i++)
                Txt.Line(cdl, start + new Vector2(0, lh * i), v.Lines[i], Face.Sans, 13, Palette.Text, 19.5f);
            ImGui.Dummy(new Vector2(1, lh * v.Lines.Count));
            // More below: the words fade at the foot (`.fade-b`, 26px into the window's ground).
            if (scroll + h < lh * v.Lines.Count - 1)
            {
                var a = new Vector2(left, y + h - S(26));
                var b = new Vector2(left + w - S(8), y + h);
                var bg = Palette.Over(Palette.Window, Palette.Void);
                cdl.AddRectFilledMultiColor(a, b, Palette.U32(Palette.WithAlpha(bg, 0)), Palette.U32(Palette.WithAlpha(bg, 0)), Palette.U32(bg), Palette.U32(bg));
            }
        }
        finally
        {
            if (Frame.Open.Count > 0) Frame.Open.Pop();
            ImGui.EndChild();
        }
    }

    /// <summary>The notes as lines that fit <paramref name="width"/>: their own line breaks kept, "- " lines as "• ".</summary>
    private static List<string> Wrap(string notes, float width)
    {
        var lines = new List<string>();
        foreach (var raw in notes.Replace("\r", string.Empty).Split('\n'))
        {
            var para = raw.TrimEnd();
            if (para.StartsWith("- ", StringComparison.Ordinal) || para.StartsWith("* ", StringComparison.Ordinal)) para = "• " + para[2..].TrimStart();
            if (para.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }
            lines.AddRange(Txt.Wrap(para, Face.Sans, 13, width));
            if (lines.Count > 400) break;
        }
        return lines;
    }

    /// <summary>The list (on while the menu is open) and the lock (ember-washed when locked, quiet when not); the lock always, even unlinked.</summary>
    private void HeaderButtons(Vector2 pos, bool list, bool lockButton)
    {
        var x = pos.X;
        if (list)
        {
            var open = ImGui.IsPopupOpen(PlansMenu);
            if (Kit.IconButton("##rp-pick", new Vector2(x, pos.Y), Icon.List, "Your plans", open ? Btn.On : Btn.Quiet, BtnSize.Sm)) ImGui.OpenPopup(PlansMenu);
            x += S(HSm) + S(6.4f);
        }
        if (!lockButton) return;
        var locked = config.PlanWindowLocked;
        if (Kit.IconButton("##rp-lock", new Vector2(x, pos.Y), locked ? Icon.Lock : Icon.LockOpen,
                locked ? "Locked where it is. Press to move and resize it again." : "Lock it where it is", locked ? Btn.Ember : Btn.Quiet, BtnSize.Sm))
        {
            config.PlanWindowLocked = !locked;
            config.Save();
        }
    }

    /// <summary>No plan shown: the Boss, a small callout, one line, at most one button.</summary>
    private void DrawEmpty(float left, float y, float w, bool small)
    {
        var dl = Kit.Dl;
        var headH = S(32);
        var canPick = plans.Linked && plans.List.Count > 0;
        var buttons = S(HSm) * (canPick ? 2 : 1) + (canPick ? S(6.4f) : 0);
        Kit.Eyebrow(dl, new Vector2(left, y + (headH - Kit.EyebrowH) / 2), "Raid plan", null, w - buttons - S(8));
        HeaderButtons(new Vector2(left + w - buttons, y + (headH - S(HSm)) / 2), canPick, true);
        y += headH + S(small ? 4 : 10);

        if (plans.Linked && !plans.ListLoaded && !plans.ListLoading && plans.ListWhy == null && !askedForList)
        {
            askedForList = true;
            plans.RefreshList();
        }
        var state = EmptyState.Of(plans.Linked, plans.Loading != null, plans.ListLoading, plans.PlanWhy ?? plans.ListWhy, plans.List.Count, plans.Content != 0);
        var (mood, ring, icon, tele) = state.Kind switch
        {
            EmptyKind.NotLinked => (BossMood.Asleep, true, Icon.Link, true),
            EmptyKind.Wrong => (BossMood.Awake, false, Icon.RefreshCw, false),
            EmptyKind.NoPlans => (BossMood.Asleep, true, Icon.ExternalLink, false),
            EmptyKind.NoPlanHere => (BossMood.Tilted, false, Icon.List, true),
            _ => (BossMood.Asleep, false, Icon.List, true),
        };
        var cx = left + w / 2;
        var drawH = S(small ? 60 : 84);
        var c = new Vector2(cx, y + drawH / 2);
        if (state.Kind == EmptyKind.Reading) Spots(dl, c);
        else Boss.Empty(dl, c, small ? 40 : 56, mood, ring && !small ? 84 : 0);
        y += drawH + S(8.8f);
        y += Kit.Callout(dl, new Vector2(left, y), state.Title, 13, w, 2, 1.1f, center: true);
        y += S(8.8f);
        if (state.Kind == EmptyKind.Reading)
        {
            var bw = MathF.Min(S(180), w);
            Kit.CastBar(dl, new Vector2(cx - bw / 2, y), bw, Running(1.4f));
            return;
        }
        var runs = new[] { Run.Note(state.Line) };
        var lineH = Txt.Flow(new Vector2(left, y), w, runs, 19.5f, out _, center: true);
        y += lineH + S(8.8f);
        if (state.Button == null) return;
        var style = tele ? Btn.Tele : Btn.Outline;
        var bw2 = Kit.ButtonWidth(state.Button, BtnSize.Md, icon, style);
        if (Kit.Button("##rp-empty", new Vector2(cx - bw2 / 2, y), state.Button, style, BtnSize.Md, icon))
        {
            switch (state.Kind)
            {
                case EmptyKind.NotLinked:
                    OpenSettings?.Invoke();
                    break;
                case EmptyKind.Wrong:
                    plans.Retry();
                    break;
                case EmptyKind.NoPlans:
                    Dalamud.Utility.Util.OpenLink(SiteLink.PlannerUrl);
                    break;
                default:
                    ImGui.OpenPopup(PlansMenu);
                    break;
            }
        }
    }

    /// <summary>The party in a row, as the site's loader (eight spots in their roles' colours).</summary>
    private static void Spots(ImDrawListPtr dl, Vector2 c)
    {
        string[] spots = { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" };
        var d = S(20);
        var gap = S(4);
        var total = d * spots.Length + gap * (spots.Length - 1);
        var x = c.X - total / 2 + d / 2;
        // They run to their spots: each in turn lifts a little.
        var t = Running(1.4f);
        for (var i = 0; i < spots.Length; i++)
        {
            var lift = MathF.Max(0, MathF.Sin((t * spots.Length - i) * MathF.PI / 2)) * S(2) * (ReducedMotion ? 0 : 1);
            Kit.Spot(dl, new Vector2(x, c.Y - lift), 20, spots[i], Palette.SpotColor(spots[i]));
            x += d + gap;
        }
    }

    /// <summary>
    /// Your plans (`.menu`): this duty's under an eyebrow, the one shown with a ready tick and the rest
    /// with the map, then the other duties' with their duty under the name; every row keeps a 20px
    /// slot at the left, so the names line up. Read them again at the foot.
    /// </summary>
    private void DrawPlansMenu()
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(S(220), 0), new Vector2(S(320), S(420)));
        if (!Kit.BeginMenu(PlansMenu)) return;
        try
        {
            var w = S(320) - S(11.2f);
            if (menuVersion != plans.Version)
            {
                menuVersion = plans.Version;
                menuRows.Clear();
                foreach (var p in plans.List) menuRows.Add((p, plans.Content != 0 && p.Content == plans.Content));
            }
            var dl = ImGui.GetWindowDrawList();
            var here = menuRows.Where(r => r.Here).ToList();
            var others = menuRows.Where(r => !r.Here).ToList();
            if (here.Count > 0)
            {
                Kit.MenuEyebrow($"This duty · {plans.DutyName ?? "here"}", w);
                foreach (var (p, _) in here) PlanRow(dl, p, w, null);
            }
            if (others.Count > 0)
            {
                if (here.Count > 0) Kit.MenuRule(w);
                Kit.MenuEyebrow(here.Count > 0 || plans.Content != 0 ? "Other duties" : "Your plans", w);
                foreach (var (p, _) in others) PlanRow(dl, p, w, string.IsNullOrEmpty(p.Duty) ? null : p.Duty);
            }
            if (menuRows.Count == 0)
            {
                var pos = ImGui.GetCursorScreenPos();
                Txt.Line(dl, pos + new Vector2(S(8.8f), 0), "No plans yet", Face.Sans, 13, Palette.Muted, 38);
                ImGui.Dummy(new Vector2(w, S(38)));
            }
            Kit.MenuRule(w);
            var reading = plans.ListLoading;
            var (clicked, hovered, min, max) = Kit.MenuRow("##rp-refresh", w, reading ? "Reading your plans…" : "Read them again");
            var (slot, _) = Kit.MenuRowParts(min, max);
            Icons.Draw(dl, Icon.RefreshCw, slot + new Vector2((S(20) - S(Kit.IconPx)) / 2), S(Kit.IconPx), hovered ? Palette.White : Palette.Muted);
            if (clicked) plans.RefreshList(true);
        }
        finally
        {
            Kit.EndMenu();
        }
    }

    private void PlanRow(ImDrawListPtr dl, PlanListItem p, float w, string? sub)
    {
        var shown = plans.Plan?.Id == p.Id;
        var steps = shown ? plans.Plan!.Steps.Count : p.Steps;
        var count = steps is { } n ? (n == 1 ? "1 step" : $"{n} steps") : null;
        var reserve = count != null ? Txt.Width(count, Face.Mono, 12) + S(10) : 0;
        var (clicked, _, min, max) = Kit.MenuRow("##rp-plan-" + p.Id, w, p.Name, sub, reserve: reserve);
        var (slot, right) = Kit.MenuRowParts(min, max);
        if (shown) Kit.Tick(dl, slot + new Vector2(S(10)), 20);
        else if (sub == null) Icons.Draw(dl, Icon.Map, slot + new Vector2((S(20) - S(Kit.IconPx)) / 2), S(Kit.IconPx), Palette.Faint);
        if (count != null) Txt.LineRight(dl, right, min.Y, count, Face.Mono, 12, Palette.Faint, (max.Y - min.Y) / S(1));
        if (clicked)
        {
            plans.Open(p.Id, true);
            ImGui.CloseCurrentPopup();
        }
    }
}
