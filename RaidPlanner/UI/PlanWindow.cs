using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using RaidPlanner.Core;
using RaidPlanner.Game;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The plan in the game: a small window to put anywhere (and lock there), in the main window's
/// look. The plan's name, the step's name and time, the step's board, its notes, and the previous
/// and next buttons; its list button picks another of your plans, for any duty. During a pull it
/// follows along by itself (<see cref="PlanService"/>).
///
/// What's drawn is worked out once a step (the board's objects, coloured and capped; the words;
/// the notes wrapped to the width), so a frame only draws. Nothing in Draw is let throw.
/// </summary>
public sealed class PlanWindow : Window
{
    private const string PlansMenu = "##rp-plans";

    private readonly Configuration config;
    private readonly PlanService plans;
    private readonly Fonts fonts;

    /// <summary>What the step shown draws, made once a step.</summary>
    private sealed class StepView
    {
        public required Plan Plan;
        public required int Step;
        public required PlanArena Arena;
        public required List<DrawnObj> Objects;
        public required string Title;
        public required string Eyebrow;
        public required string Count;
        public required string Notes;
        public string? Hidden;
        public List<string> Lines = new();
        public float LinesWidth = -1;
        public float LinesScale;

        // The plan's name, the step's title (on one or two lines) and the eyebrow, fitted to the
        // width once (again when the width or the scale changes), not every frame.
        public float FitWidth = -1;
        public float FitScale;
        public string NameFit = string.Empty;
        public string TitleFirst = string.Empty;
        public string? TitleSecond;
        public string EyebrowFit = string.Empty;
    }

    private StepView? view;
    private int viewVersion = -1;

    /// <summary>The plans menu's rows (name · duty, and fitted to the menu), made again when the list or the width changes.</summary>
    private readonly List<(string Id, string Key, string Shown, bool Here)> menuRows = new();
    private int menuVersion = -1;
    private float menuWidth = -1;

    /// <summary>The empty window's line (and whether it offers Try again), made again when anything it says changes.</summary>
    private Run[] emptyLine = { Run.Note(string.Empty) };
    private bool emptyRetry;
    private int emptyVersion = -1;

    private static readonly Run[] LinkLine = { Run.Note("Link your Raid Planner account to see your plans here.") };

    /// <summary>The plan's error under the header, fitted once for its width.</summary>
    private (string? Why, float Width, string Fit) whyLine = (null, -1, string.Empty);

    private DateTime drawFailedAt;
    private bool askedForList;

    public PlanWindow(Configuration config, PlanService plans, Fonts fonts)
        : base("Raid plan###RaidPlannerPlan")
    {
        this.config = config;
        this.plans = plans;
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

    private void DrawInside()
    {
        var pad = S(14);
        var win = ImGui.GetWindowPos();
        var top = ImGui.GetCursorScreenPos();
        var width = ImGui.GetWindowWidth();
        var bottom = win.Y + ImGui.GetWindowHeight() - pad;
        var left = win.X + pad;
        var inner = width - pad * 2;
        Kit.Dl.AddLine(new Vector2(win.X + 1, top.Y + 0.5f), new Vector2(win.X + width - 1, top.Y + 0.5f), Palette.U32(Palette.Line), 1f);
        Kit.PushBox(left, inner);
        try
        {
            var y = top.Y + S(12);
            if (plans.Plan is { } plan)
            {
                Refresh(plan);
                DrawPlan(view!, left, y, inner, bottom);
            }
            else
            {
                DrawEmpty(left, y, inner);
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
        var s = plan.Steps[step];
        var objects = PlanBoard.Prepare(s.Objects, out var hidden);
        view = new StepView
        {
            Plan = plan,
            Step = step,
            Arena = plan.ArenaAt(step),
            Objects = objects,
            Title = s.Name,
            Eyebrow = s.Time.Length > 0 ? $"Step {step + 1} of {plan.Steps.Count} · {s.Time}" : $"Step {step + 1} of {plan.Steps.Count}",
            Count = $"{step + 1} / {plan.Steps.Count}",
            Notes = s.Notes,
            Hidden = hidden > 0 ? $"{Words.Count(hidden, "more object")} not drawn" : null,
        };
    }

    private void DrawPlan(StepView v, float left, float y, float w, float bottom)
    {
        var dl = Kit.Dl;
        var buttons = S(28) * 2 + S(6);
        Fit(v, w, buttons);
        // The plan's name, with the list and the lock.
        Txt.Line(dl, new Vector2(left, y), v.NameFit, Face.SansMedium, 12, Palette.Muted, 28);
        HeaderButtons(new Vector2(left + w - buttons, y));
        y += S(28) + S(4);

        // Another plan on its way, or one that couldn't be read: said under the header.
        if (plans.Loading != null || plans.PlanWhy != null)
        {
            var failed = plans.Loading == null;
            var line = "Opening the plan…";
            if (failed)
            {
                var room = w - S(30);
                if (!ReferenceEquals(whyLine.Why, plans.PlanWhy) || MathF.Abs(whyLine.Width - room) > 0.5f)
                    whyLine = (plans.PlanWhy, room, Txt.Fit(plans.PlanWhy!, Face.Sans, 12, room));
                line = whyLine.Fit;
            }
            Txt.Line(dl, new Vector2(left, y), line, Face.Sans, 12, failed ? Palette.Danger : Palette.Muted, 22);
            if (failed && Kit.IconButton("##rp-retry-top", new Vector2(left + w - S(22), y), Icon.Refresh, "Try again", 22)) plans.Retry();
            y += S(22) + S(6);
        }

        // The step: its name, then where it is (and Following, while a pull moves it).
        var lh = S(18 * 1.2f);
        Txt.Line(dl, new Vector2(left, y), v.TitleFirst, Face.SansSemibold, 18, Palette.Ink, 18 * 1.2f);
        if (v.TitleSecond != null) Txt.Line(dl, new Vector2(left, y + lh), v.TitleSecond, Face.SansSemibold, 18, Palette.Ink, 18 * 1.2f);
        y += (v.TitleSecond != null ? lh * 2 : lh) + S(2);
        var eyebrowW = Txt.Line(dl, new Vector2(left, y), v.EyebrowFit, Face.Sans, 12, Palette.Muted, 18);
        if (plans.Following)
        {
            var dot = new Vector2(left + eyebrowW + S(12), y + S(9));
            var p = Pulse();
            dl.AddCircleFilled(dot, S(3) + S(2) * p, Palette.U32(Palette.WithAlpha(Palette.Ember, 0.25f * (1 - p))), 12);
            dl.AddCircleFilled(dot, S(3), Palette.U32(Palette.Ember), 12);
            Txt.Line(dl, new Vector2(dot.X + S(8), y), "Following the pull", Face.Sans, 12, Palette.EmberText, 18);
        }
        y += S(18) + S(10);

        // The footer: previous, where you are, next.
        var footH = S(30);
        var footY = bottom - footH;

        // The board, as big as leaves room for a few lines of notes.
        var notesMin = S(19.5f) * 3 + S(12);
        var board = MathF.Max(S(110), MathF.Min(w, footY - S(10) - notesMin - y));
        board = MathF.Min(board, w);
        var boardX = left + (w - board) / 2;
        PlanBoard.Draw(dl, new Vector2(boardX, y), board, v.Arena, v.Objects);
        if (v.Hidden != null && ImGui.IsMouseHoveringRect(new Vector2(boardX, y), new Vector2(boardX + board, y + board)) && ImGui.IsWindowHovered())
            Kit.Tooltip(v.Hidden);
        y += board + S(12);

        // The notes, scrolling when long.
        var notesH = footY - S(10) - y;
        if (notesH > S(16)) Notes(v, left, y, w, notesH);

        // Previous and next.
        if (Kit.IconButton("##rp-prev", new Vector2(left, footY), Icon.ChevronLeft, "Previous step", 30, Btn.Glass, v.Step == 0)) plans.Move(-1);
        if (Kit.IconButton("##rp-next", new Vector2(left + w - S(30), footY), Icon.ChevronRight, "Next step", 30, Btn.Glass, v.Step >= v.Plan.Steps.Count - 1))
            plans.Move(1);
        var cw = Txt.Measure(v.Count, Face.Mono, 12).X;
        Txt.Line(dl, new Vector2(left + (w - cw) / 2, footY), v.Count, Face.Mono, 12, Palette.Muted, 30);
        ImGui.SetCursorScreenPos(new Vector2(left, bottom));
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>The words fitted to the width: done once, and again only when the width or the scale changes.</summary>
    private static void Fit(StepView v, float w, float buttons)
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (MathF.Abs(v.FitWidth - w) <= 0.5f && v.FitScale == scale) return;
        v.FitWidth = w;
        v.FitScale = scale;
        v.NameFit = Txt.Fit(v.Plan.Name, Face.SansMedium, 12, w - buttons - S(10));
        (v.TitleFirst, v.TitleSecond) = TitleLines(v.Title, w);
        v.EyebrowFit = Txt.Fit(v.Eyebrow, Face.Sans, 12, w * 0.7f);
    }

    /// <summary>The step's name in Geist 600, on up to two lines: as many words as fit on the first, the rest cut on the second.</summary>
    private static (string First, string? Second) TitleLines(string text, float w)
    {
        if (Txt.Measure(text, Face.SansSemibold, 18).X <= w) return (text, null);
        var cut = text.Length;
        while (cut > 0)
        {
            cut = text.LastIndexOf(' ', cut - 1);
            if (cut <= 0) break;
            if (Txt.Measure(text[..cut], Face.SansSemibold, 18).X <= w) break;
        }
        if (cut <= 0) return (Txt.Fit(text, Face.SansSemibold, 18, w), null);
        return (text[..cut], Txt.Fit(text[(cut + 1)..], Face.SansSemibold, 18, w));
    }

    /// <summary>The notes, wrapped once for the width (again when it changes), in a part of their own that scrolls.</summary>
    private void Notes(StepView v, float left, float y, float w, float h)
    {
        if (v.Notes.Length == 0)
        {
            Txt.Line(Kit.Dl, new Vector2(left, y), "No notes for this step.", Face.Sans, 13, Palette.Faint, 19.5f);
            return;
        }
        var scale = ImGuiHelpers.GlobalScale;
        if (MathF.Abs(v.LinesWidth - w) > 0.5f || v.LinesScale != scale)
        {
            v.LinesWidth = w;
            v.LinesScale = scale;
            v.Lines = Wrap(v.Notes, w - S(10));
        }
        ImGui.SetCursorScreenPos(new Vector2(left, y));
        var open = ImGui.BeginChild("##rp-notes", new Vector2(w, h), false, ImGuiWindowFlags.None);
        Frame.Open.Push(false);
        try
        {
            if (!open) return;
            var dl = ImGui.GetWindowDrawList();
            var lh = S(19.5f);
            var start = ImGui.GetCursorScreenPos();
            var scroll = ImGui.GetScrollY();
            // Only the lines in view are drawn.
            var first = Math.Max(0, (int)(scroll / lh) - 1);
            var last = Math.Min(v.Lines.Count, first + (int)(h / lh) + 3);
            for (var i = first; i < last; i++)
                Txt.Line(dl, start + new Vector2(0, lh * i), v.Lines[i], Face.Sans, 13, Palette.Soft, 19.5f);
            ImGui.Dummy(new Vector2(1, lh * v.Lines.Count));
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
            var cur = string.Empty;
            foreach (var word in para.Split(' '))
            {
                var next = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length > 0 && Txt.Measure(next, Face.Sans, 13).X > width)
                {
                    lines.Add(cur);
                    cur = word;
                }
                else cur = next;
            }
            lines.Add(cur);
            if (lines.Count > 400) break;
        }
        return lines;
    }

    private void HeaderButtons(Vector2 pos)
    {
        if (Kit.IconButton("##rp-pick", pos, Icon.List, "Your plans", 28)) ImGui.OpenPopup(PlansMenu);
        var locked = config.PlanWindowLocked;
        if (Kit.IconButton("##rp-lock", pos + new Vector2(S(28) + S(6), 0), locked ? Icon.Lock : Icon.LockOpen,
                locked ? "Locked where it is. Press to move and resize it again." : "Lock it where it is", 28, locked ? Btn.Picked : Btn.Glass))
        {
            config.PlanWindowLocked = !locked;
            config.Save();
        }
    }

    /// <summary>No plan shown yet: why, and what to do.</summary>
    private void DrawEmpty(float left, float y, float w)
    {
        var dl = Kit.Dl;
        var buttons = S(28) * 2 + S(6);
        Txt.Line(dl, new Vector2(left, y), "Raid plan", Face.SansMedium, 12, Palette.Muted, 28);
        if (plans.Linked) HeaderButtons(new Vector2(left + w - buttons, y));
        y += S(28) + S(14);
        ImGui.SetCursorScreenPos(new Vector2(left, y));
        if (!plans.Linked)
        {
            Txt.Para(LinkLine);
            Kit.Gap(12);
            if (Kit.Button("##rp-link", Kit.At, "Link your account", Btn.Primary, BtnSize.Tiny, Icon.Link)) OpenSettings?.Invoke();
            return;
        }
        if (!plans.ListLoaded && !plans.ListLoading && plans.ListWhy == null && !askedForList)
        {
            askedForList = true;
            plans.RefreshList();
        }
        if (emptyVersion != plans.Version)
        {
            // Everything it reads bumps the plans' version when it changes.
            emptyVersion = plans.Version;
            string line;
            var retry = false;
            if (plans.Loading != null) line = "Opening the plan…";
            else if (plans.ListLoading) line = "Reading your plans…";
            else if (plans.PlanWhy is { } pw)
            {
                line = pw;
                retry = true;
            }
            else if (plans.ListWhy is { } lw)
            {
                line = lw;
                retry = true;
            }
            else if (plans.List.Count == 0) line = "You have no plans yet. Make one on the site, or make one from a pull you've sent.";
            else if (plans.Content != 0) line = $"No plan for {plans.DutyName ?? "this duty"} yet. Pick one of yours, or make one on the site.";
            else line = "Pick one of your plans to show it here.";
            emptyLine = new[] { Run.Note(line, retry ? Palette.Danger : null) };
            emptyRetry = retry;
        }
        var retrying = emptyRetry;
        Txt.Para(emptyLine);
        Kit.Gap(12);
        var at = Kit.At;
        if (retrying)
        {
            if (Kit.Button("##rp-retry", at, "Try again", Btn.Glass, BtnSize.Tiny, Icon.Refresh)) plans.Retry();
        }
        else if (plans.List.Count > 0 && Kit.Button("##rp-pick-btn", at, "Pick a plan", Btn.Primary, BtnSize.Tiny, Icon.List))
        {
            ImGui.OpenPopup(PlansMenu);
        }
    }

    /// <summary>Your plans: this duty's first, then the rest, newest first; Refresh at the top.</summary>
    private void DrawPlansMenu()
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(S(220), 0), new Vector2(S(360), S(380)));
        if (!Kit.BeginMenu(PlansMenu)) return;
        try
        {
            var w = S(320);
            if (menuVersion != plans.Version || MathF.Abs(menuWidth - w) > 0.5f)
            {
                menuVersion = plans.Version;
                menuWidth = w;
                menuRows.Clear();
                foreach (var p in plans.List.OrderByDescending(p => plans.Content != 0 && p.Content == plans.Content))
                {
                    var here = plans.Content != 0 && p.Content == plans.Content;
                    var label = string.IsNullOrEmpty(p.Duty) ? p.Name : $"{p.Name} · {p.Duty}";
                    menuRows.Add((p.Id, "##rp-plan-" + p.Id, Txt.Fit(label, Face.Sans, 13, w - S(48)), here));
                }
            }
            if (Kit.MenuItem("##rp-refresh", plans.ListLoading ? "Reading your plans…" : "Read them again", Icon.Refresh, w)) plans.RefreshList(true);
            if (menuRows.Count == 0)
            {
                Txt.Line(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos() + new Vector2(S(12), 0), "No plans yet", Face.Sans, 13, Palette.Faint, 36);
                ImGui.Dummy(new Vector2(w, S(36)));
            }
            for (var i = 0; i < menuRows.Count; i++)
            {
                var (id, key, shown, here) = menuRows[i];
                if (Kit.MenuItem(key, shown, plans.Plan?.Id == id ? Icon.Check : here ? Icon.MapPin : null, w))
                {
                    plans.Open(id, true);
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        finally
        {
            Kit.EndMenu();
        }
    }
}
