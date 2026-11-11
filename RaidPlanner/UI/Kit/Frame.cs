using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>A toast's kind (`.toast`): it worked (green tick), it didn't (red), or news (ember).</summary>
public enum ToastKind
{
    Ok,
    Bad,
    News,
}

/// <summary>
/// The window, top to bottom, as the mock-ups' `.dw`: a header (16px from the top, 20 at the sides)
/// that stays put, a body (16 above, 18 below) that scrolls when it's tall, and a footer on the
/// basalt band pinned to the bottom (14 above, 16 below) with a hairline over it. Inside the body,
/// <see cref="BeginBottom"/> pushes what follows to the body's foot, as a <c>flex:1</c> spacer does.
/// The toast sits at the window's foot on the window's own layer.
/// </summary>
public static class Frame
{
    private static readonly Dictionary<string, float> Heights = new();
    private static readonly Stack<(string Id, float Start, float PadBottom)> Footers = new();
    private static readonly Stack<(string Id, float Start)> Bottoms = new();
    private static float bodyPadBottom;

    /// <summary>At the start of each frame: nothing left over from a frame whose drawing threw part way.</summary>
    public static void ResetFrame()
    {
        Footers.Clear();
        Bottoms.Clear();
        Open.Clear();
    }

    /// <summary>The ImGui children and popups the kit has open now, innermost last.</summary>
    internal static readonly Stack<bool> Open = new();

    /// <summary>
    /// Ends what the kit left open (a view that threw between a Begin and its End), innermost first, so
    /// the window ends with ImGui's stack as it found it rather than with a child or popup still open
    /// (which ImGui asserts on). Nothing to do on a frame drawn to the end.
    /// </summary>
    public static void Unwind()
    {
        while (Open.Count > 0)
        {
            if (Open.Pop()) ImGui.EndPopup();
            else ImGui.EndChild();
        }
    }

    /// <summary>The window's faked shadow (`.dw`: rings of black 5 and 11px out), and the hairline under Dalamud's title bar.</summary>
    public static void Window(bool titleBar = true)
    {
        var win = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        Kit.Rings(win, win + size, ImGui.GetStyle().WindowRounding, 5, 11, 0.16f, 0.08f);
        if (!titleBar) return;
        var top = ImGui.GetCursorScreenPos().Y;
        Kit.Dl.AddLine(new Vector2(win.X + 1, top + 0.5f), new Vector2(win.X + size.X - 1, top + 0.5f), Palette.U32(Palette.Line), 1f);
    }

    /// <summary>Starts the header: the window's top-left plus its padding.</summary>
    public static void BeginHeader(float padTop = 16)
    {
        var win = ImGui.GetWindowPos();
        var content = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(content.X, content.Y + S(padTop)));
        ImGui.Dummy(Vector2.Zero);
        ImGui.SetCursorScreenPos(new Vector2(content.X, content.Y + S(padTop)));
        Kit.PushBox(win.X + S(SidePad), ImGui.GetWindowWidth() - S(SidePad) * 2);
    }

    public static void EndHeader() => Kit.PopBox();

    /// <summary>
    /// Starts the scrolling body, as tall as what's left above the footer (its height from last
    /// frame; a first guess the first time). Padding in CSS pixels.
    /// </summary>
    public static void BeginBody(string id, string? footerId, float padTop = 16, float padBottom = 18)
    {
        var footer = footerId != null && Heights.TryGetValue(footerId, out var f) ? f : footerId != null ? S(120) : 0;
        var x = ImGui.GetWindowPos().X;
        var y = ImGui.GetCursorScreenPos().Y;
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var height = MathF.Max(S(40), ImGui.GetWindowPos().Y + ImGui.GetWindowHeight() - footer - y);
        ImGui.BeginChild(id, new Vector2(ImGui.GetWindowWidth(), height), false, ImGuiWindowFlags.None);
        Open.Push(false);
        var at = ImGui.GetCursorScreenPos();
        Kit.PushBox(at.X + S(SidePad), ImGui.GetContentRegionAvail().X - S(SidePad) * 2);
        Kit.Reserve(at, 1, S(padTop));
        bodyPadBottom = padBottom;
    }

    public static void EndBody()
    {
        Kit.Gap(bodyPadBottom);
        Kit.PopBox();
        if (Open.Count > 0) Open.Pop();
        ImGui.EndChild();
    }

    /// <summary>
    /// The mock-ups' <c>flex:1</c> spacer: a gap, then as much room as there is, then a gap, so what
    /// follows (until <see cref="EndBottom"/>) sits at the foot of the body. When the body is full,
    /// it's just the two gaps.
    /// </summary>
    public static void BeginBottom(string id, float gap = 12)
    {
        Kit.Gap(gap);
        var h = Heights.TryGetValue(id, out var v) ? v : 0;
        var bottom = ImGui.GetWindowPos().Y + ImGui.GetWindowHeight() - S(bodyPadBottom);
        var y = ImGui.GetCursorScreenPos().Y;
        var room = bottom - h - y;
        if (room > 0) Kit.Reserve(Kit.At, 1, room);
        Bottoms.Push((id, ImGui.GetCursorScreenPos().Y));
    }

    /// <summary>
    /// The room left in the body below where you are, as if it weren't scrolled (so what fills it doesn't
    /// grow as you scroll), less the body's bottom padding and, given <paramref name="bottomId"/>, the
    /// <see cref="BeginBottom"/> part that comes after (its gap and its height last frame).
    /// </summary>
    public static float RoomLeft(string? bottomId = null, float gap = 12)
    {
        var bottom = ImGui.GetWindowPos().Y + ImGui.GetWindowHeight() - S(bodyPadBottom);
        var y = ImGui.GetCursorScreenPos().Y + ImGui.GetScrollY();
        var after = bottomId != null ? S(gap) + (Heights.TryGetValue(bottomId, out var h) ? h : 0) : 0;
        return bottom - after - y;
    }

    public static void EndBottom()
    {
        var (id, start) = Bottoms.Pop();
        Heights[id] = ImGui.GetCursorScreenPos().Y - start;
    }

    /// <summary>Starts the footer (`.dw-ft`): the basalt band under the body, a hairline over it, to the window's bottom edge.</summary>
    public static void BeginFooter(string id, float padTop = 14, float padBottom = 16)
    {
        var win = ImGui.GetWindowPos();
        var w = ImGui.GetWindowWidth();
        var top = ImGui.GetCursorScreenPos().Y;
        var bottom = win.Y + ImGui.GetWindowHeight();
        var dl = Kit.Dl;
        dl.AddRectFilled(new Vector2(win.X + 1, top), new Vector2(win.X + w - 1, bottom - 1), Palette.U32(Palette.Basalt), ImGui.GetStyle().WindowRounding - 1, ImDrawFlags.RoundCornersBottom);
        dl.AddLine(new Vector2(win.X + 1, top + 0.5f), new Vector2(win.X + w - 1, top + 0.5f), Palette.U32(Palette.Line), 1f);
        Kit.PushBox(win.X + S(SidePad), w - S(SidePad) * 2);
        Kit.Reserve(new Vector2(win.X, top), 1, S(padTop));
        Footers.Push((id, top, padBottom));
        footerTop = top;
    }

    public static void EndFooter()
    {
        var (id, top, padBottom) = Footers.Pop();
        Heights[id] = ImGui.GetCursorScreenPos().Y + S(padBottom) - top;
        Kit.PopBox();
    }

    // ---------------------------------------------------------------------------------------------
    // Toasts: one at a time, at the window's foot.

    private sealed record ToastState(string Text, ToastKind Kind, Icon Icon, string? Action, Action? OnAction)
    {
        /// <summary>Seconds of its life used up (it doesn't count while pointed at).</summary>
        public float Used;
    }

    /// <summary>A toast's life.</summary>
    public const float ToastLife = 2.6f;

    private static ToastState? toast;
    private static double lastFrame;

    /// <summary>
    /// Says something for a moment: <see cref="ToastKind.Ok"/> with a green tick, <see cref="ToastKind.Bad"/>
    /// in red, news in ember with <paramref name="icon"/>; at most one action (an ember-light word).
    /// </summary>
    public static void Toast(string text, ToastKind kind = ToastKind.News, Icon? icon = null, string? action = null, Action? onAction = null) =>
        toast = new ToastState(text, kind, icon ?? (kind == ToastKind.Ok ? Icon.CircleCheck : kind == ToastKind.Bad ? Icon.CircleX : Icon.Info), action, onAction);

    /// <summary>The top of the footer drawn this frame (0 when none): the toast sits above it, not over its buttons.</summary>
    private static float footerTop;

    /// <summary>
    /// Draws the toast (`.toast`): raised near-solid with 10px corners and a hairline, its icon in its
    /// colour, 13px words, its action, and a 2px bar along its foot counting its 2.6 seconds down in
    /// its colour; it waits while pointed at, and fades over its last 0.3 s. Drawn in a part of the
    /// window of its own laid over the body, so it never paints over another window over this one.
    /// </summary>
    public static void DrawToast()
    {
        var top = footerTop;
        footerTop = 0;
        var now = ImGui.GetTime();
        var dt = (float)Math.Clamp(now - lastFrame, 0, 0.25);
        lastFrame = now;
        var t = toast;
        if (t == null) return;
        if (t.Used >= ToastLife)
        {
            toast = null;
            return;
        }

        var win = ImGui.GetWindowPos();
        var winW = ImGui.GetWindowWidth();
        var bottom = win.Y + ImGui.GetWindowHeight();
        var padL = S(11.2f);
        var padR = S(13.6f);
        var icon = S(Kit.IconPx);
        var gap = S(10.4f);
        var actionW = t.Action != null ? S(6.4f) + Txt.Width(t.Action, Face.SansSemibold, 13) : 0;
        var maxText = MathF.Max(S(60), winW - S(SidePad) * 2 - padL - padR - icon - gap - actionW);
        var text = Txt.Fit(t.Text, Face.Sans, 13, maxText);
        var textW = Txt.Width(text, Face.Sans, 13);
        var box = new Vector2(padL + icon + gap + textW + actionW + padR, S(9.6f) * 2 + S(19.5f));
        var y = top > win.Y && top < bottom ? top - S(10) - box.Y : bottom - box.Y - S(20);
        var pos = new Vector2(MathF.Round(win.X + (winW - box.X) / 2), MathF.Round(y));

        ImGui.SetCursorScreenPos(pos);
        var open = ImGui.BeginChild("##rp-toast", box, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        Open.Push(false);
        try
        {
            if (!open) return;
            var pointed = ImGui.IsWindowHovered();
            if (!pointed || ReducedMotion) t.Used += dt;
            var left = ToastLife - t.Used;
            var old = Palette.Opacity;
            Palette.Opacity = ReducedMotion ? 1 : Math.Clamp(left / 0.3f, 0, 1);
            var dl = ImGui.GetWindowDrawList();
            var r = S(Palette.RadiusMd);
            dl.AddRectFilled(pos, pos + box, Palette.U32(Palette.Raised), r);
            dl.AddRect(pos + new Vector2(0.5f), pos + box - new Vector2(0.5f), Palette.U32(Palette.Line2), r, ImDrawFlags.None, 1f);
            var color = t.Kind switch
            {
                ToastKind.Ok => Palette.Ready,
                ToastKind.Bad => Palette.Danger,
                _ => Palette.Ember,
            };
            Icons.Draw(dl, t.Icon, pos + new Vector2(padL, (box.Y - icon) / 2), icon, color, 1.75f);
            var x = pos.X + padL + icon + gap;
            x += Txt.Line(dl, new Vector2(x, pos.Y), text, Face.Sans, 13, Palette.White, box.Y / S(1));
            if (t.Action != null)
            {
                var ap = new Vector2(x + S(6.4f), pos.Y);
                var aw = Txt.Width(t.Action, Face.SansSemibold, 13);
                var (clicked, hovered, _) = Kit.Hit("##rp-toast-action", ap, new Vector2(aw, box.Y));
                Txt.Line(dl, ap, t.Action, Face.SansSemibold, 13, hovered ? Palette.White : Palette.EmberHi, box.Y / S(1));
                if (clicked)
                {
                    toast = null;
                    t.OnAction?.Invoke();
                }
            }
            // The bar along its foot, counting down.
            var frac = Math.Clamp(left / ToastLife, 0, 1);
            dl.PushClipRect(pos, pos + box, true);
            dl.AddRectFilled(new Vector2(pos.X, pos.Y + box.Y - S(2)), new Vector2(pos.X + box.X * frac, pos.Y + box.Y), Palette.U32(color));
            dl.PopClipRect();
            Palette.Opacity = old;
        }
        finally
        {
            if (Open.Count > 0) Open.Pop();
            ImGui.EndChild();
        }
    }
}
