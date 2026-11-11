using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>Ready Check's buttons (`.btn`): one telegraph a screen, the rest outlined, quiet or danger.</summary>
public enum Btn
{
    /// <summary>The telegraph (`.btn.tele`): ember with a hot rim, the one thing a screen is for.</summary>
    Tele,

    /// <summary>Outlined (`.btn`): a white hairline at 20%.</summary>
    Outline,

    /// <summary>Words only (`.btn.quiet`), a faint fill when pointed at.</summary>
    Quiet,

    /// <summary>Outlined in danger red (`.btn.danger`): Stop, Delete.</summary>
    Danger,

    /// <summary>A toggle that's on (`.btn.is-on`): a white wash, no edge.</summary>
    On,

    /// <summary>Ember-washed (`.btn.is-ember`): a lock that's locked.</summary>
    Ember,
}

/// <summary>Control heights: 40 (`--h`), 32 for dense rows (`--h-sm`), 46 for the one big call (`--h-lg`).</summary>
public enum BtnSize
{
    Md,
    Sm,
    Lg,
}

/// <summary>
/// Ready Check's controls, drawn with ImGui's draw lists: 40px buttons with 10px corners (32 and 8
/// in dense rows), the ember telegraph (hot rim, a glow of two rings when pointed at, a ring going
/// out when pressed), switches on in ember, segments, steppers, sliders, the 40px well field. Each
/// takes the screen position to draw at (layout is the views'), hit-tests with an invisible ImGui
/// button (so keyboard navigation works), and shows hover, press, focus (the white ring 3px out)
/// and disabled (45%). Every size and colour is the tokens' (<see cref="Palette"/>, <see cref="Theme"/>).
/// </summary>
public static partial class Kit
{
    /// <summary>While a dialog is open, everything under it is drawn but can't be pointed at or pressed.</summary>
    public static bool Blocked { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Layout: a stack of boxes (left edge and width) the views lay rows into, top to bottom.

    private static readonly Stack<(float Left, float Width)> Boxes = new();

    public static void PushBox(float left, float width) => Boxes.Push((left, MathF.Max(0, width)));

    /// <summary>At the start of each frame: nothing left over from a frame whose drawing threw part way.</summary>
    public static void ResetFrame()
    {
        Boxes.Clear();
        Panels.Clear();
        // A control that threw while drawn disabled left the whole window faded.
        Palette.Opacity = 1f;
    }

    public static void PopBox() => Boxes.Pop();

    public static float Left => Boxes.Count > 0 ? Boxes.Peek().Left : ImGui.GetCursorScreenPos().X;

    public static float Width => Boxes.Count > 0 ? Boxes.Peek().Width : ImGui.GetContentRegionAvail().X;

    public static float Right => Left + Width;

    /// <summary>Where the next row starts: the box's left edge, at the cursor's height.</summary>
    public static Vector2 At => new(Left, ImGui.GetCursorScreenPos().Y);

    /// <summary>Takes up a row's space, so the next row goes under it.</summary>
    public static void Reserve(Vector2 start, float width, float height)
    {
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(MathF.Max(width, 1), MathF.Max(height, 0)));
    }

    /// <summary>A vertical gap of CSS pixels.</summary>
    public static void Gap(float px)
    {
        var at = At;
        Reserve(at, 1, S(px));
    }

    public static ImDrawListPtr Dl => ImGui.GetWindowDrawList();

    // ---------------------------------------------------------------------------------------------
    // Hit testing, motion.

    /// <summary>An invisible button over a rectangle: (clicked, hovered, held). Disabled and blocked areas never react.</summary>
    public static (bool Clicked, bool Hovered, bool Held) Hit(string id, Vector2 pos, Vector2 size, bool disabled = false)
    {
        var keep = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, Vector2.Max(size, new Vector2(1)));
        var live = !disabled && !Blocked;
        var hovered = live && ImGui.IsItemHovered();
        var held = live && ImGui.IsItemActive();
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        ImGui.SetCursorScreenPos(keep);
        return (clicked && live, hovered, held);
    }

    /// <summary>Pointed at, within this window and nothing over it.</summary>
    public static bool Pointed(Vector2 min, Vector2 max) => !Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max);

    /// <summary>The focus ring (`outline: 2px solid white; outline-offset: 3px`), when the keyboard drives and the last item has focus.</summary>
    public static void FocusRing(Vector2 min, Vector2 max, float radius)
    {
        if (Blocked || !ImGui.IsItemFocused() || !ImGui.GetIO().NavVisible) return;
        var o = S(3) + S(1);
        Dl.AddRect(min - new Vector2(o), max + new Vector2(o), Palette.U32(Palette.White), radius + o, ImDrawFlags.None, S(2));
    }

    /// <summary>
    /// A value kept between frames in the window's state, moved towards <paramref name="target"/>
    /// over <paramref name="seconds"/> (linear: ease it where it's used); at once under reduced motion.
    /// </summary>
    public static float Approach(string id, float target, float seconds)
    {
        var storage = ImGui.GetStateStorage();
        var key = ImGui.GetID(id);
        var v = storage.GetFloat(key, target);
        v = ReducedMotion || seconds <= 0 ? target : MoveTowards(v, target, ImGui.GetIO().DeltaTime / seconds);
        storage.SetFloat(key, v);
        return v;
    }

    /// <summary>Seconds since something began (kept in the window's state); <paramref name="restart"/> starts it again.</summary>
    public static float Since(string id, bool restart)
    {
        var storage = ImGui.GetStateStorage();
        var key = ImGui.GetID(id);
        var now = (float)ImGui.GetTime();
        if (restart) storage.SetFloat(key, now);
        var at = storage.GetFloat(key, -1000f);
        return now - at;
    }

    private static float MoveTowards(float v, float target, float step) =>
        v < target ? MathF.Min(target, v + step) : MathF.Max(target, v - step);

    // ---------------------------------------------------------------------------------------------
    // Buttons.

    /// <summary>A size's height, side padding, label size and corner (CSS px).</summary>
    private static (float Height, float Pad, int Px, float Radius) Spec(BtnSize size) => size switch
    {
        // `.btn.sm`: 32 tall, .7rem, 13px, 8px corners.
        BtnSize.Sm => (HSm, 11.2f, 13, Palette.RadiusSm),
        // `.btn.lg`: 46 tall, 1.2rem.
        BtnSize.Lg => (HLg, 19.2f, 14, Palette.RadiusMd),
        // `.btn` in a window (`.dw .btn`): 40 tall, .95rem, 13px, 10px corners.
        _ => (H, 15.2f, 13, Palette.RadiusMd),
    };

    /// <summary>The telegraph's words are 650 (Geist 600 here); the rest 550 (Geist 500).</summary>
    private static Face FaceOf(Btn style) => style == Btn.Tele ? Face.SansSemibold : Face.SansMedium;

    /// <summary>The icon in a button, `.i-sm`: 15px.</summary>
    public const float IconPx = 15;

    public static float ButtonHeight(BtnSize size = BtnSize.Md) => S(Spec(size).Height);

    public static float ButtonWidth(string label, BtnSize size = BtnSize.Md, Icon? icon = null, Btn style = Btn.Outline)
    {
        var (h, pad, px, _) = Spec(size);
        if (label.Length == 0) return S(h);
        var text = Txt.Width(label, FaceOf(style), px);
        return S(pad) * 2 + text + (icon != null ? S(IconPx + 8) : 0);
    }

    /// <summary>
    /// A button. Width 0 fits its words; an empty label makes it a square icon button (`.btn.icon`).
    /// <paramref name="dim"/> fades it without disabling it (the telegraph while a send is under way).
    /// </summary>
    public static bool Button(string id, Vector2 pos, string label, Btn style = Btn.Outline, BtnSize size = BtnSize.Md, Icon? icon = null,
        float width = 0, bool disabled = false, string? tip = null, float dim = 1f, Vector4? ink = null)
    {
        var (hCss, padCss, px, rCss) = Spec(size);
        var h = S(hCss);
        var w = width > 0 ? width : label.Length == 0 ? h : ButtonWidth(label, size, icon, style);
        var (clicked, hovered, held) = Hit(id, pos, new Vector2(w, h), disabled);
        if (tip != null && Pointed(pos, pos + new Vector2(w, h))) Tooltip(tip);

        var old = Palette.Opacity;
        // `.btn.is-dis` and a telegraph held still while it works.
        if (disabled) Palette.Opacity = old * 0.45f;
        Palette.Opacity *= dim;
        var dl = Dl;
        var r = S(rCss);
        // :active dips the shape to 0.97 (the words can't scale crisply, so they stay).
        var inset = held ? new Vector2(w, h) * 0.015f : Vector2.Zero;
        var min = pos + inset;
        var max = pos + new Vector2(w, h) - inset;
        Vector4 text;
        var iconInk = (Vector4?)null;
        switch (style)
        {
            case Btn.Tele:
            {
                var press = Since(id + "##resolve", clicked);
                if (hovered)
                {
                    // The glow: two soft rings of ember round it (ImGui has no blur).
                    dl.AddRect(min - new Vector2(S(1.5f)), max + new Vector2(S(1.5f)), Palette.U32(Palette.Rgba(255, 138, 61, 0.18f)), r + S(1.5f), ImDrawFlags.None, S(3));
                    dl.AddRect(min - new Vector2(S(4.5f)), max + new Vector2(S(4.5f)), Palette.U32(Palette.Rgba(255, 138, 61, 0.08f)), r + S(4.5f), ImDrawFlags.None, S(3));
                }
                dl.AddRectFilled(min, max, Palette.U32(hovered ? Palette.TeleHover : Palette.Ember), r);
                // The hot rim, 1.5px inside.
                var k = S(0.75f);
                dl.AddRect(min + new Vector2(k), max - new Vector2(k), Palette.U32(hovered ? Palette.TeleRimHot : Palette.TeleRim), MathF.Max(0, r - k), ImDrawFlags.None, S(1.5f));
                // Pressed, it resolves: a ring goes out, 1.22× wide and 1.7× tall, fading over 420ms.
                if (press < TResolve && !ReducedMotion)
                {
                    var e = Ease.Out(press / TResolve);
                    var grow = new Vector2(w * 0.22f, h * 0.7f) * e / 2;
                    dl.AddRect(pos - grow, pos + new Vector2(w, h) + grow, Palette.U32(Palette.WithAlpha(Palette.Ember, 0.9f * (1 - e))), r + grow.Y * 0.5f, ImDrawFlags.None, S(1.5f));
                }
                text = Palette.OnEmber;
                break;
            }
            case Btn.Quiet:
                if (hovered || held) dl.AddRectFilled(min, max, Palette.U32(Palette.HoverQuiet), r);
                text = hovered ? Palette.White : Palette.Text;
                break;
            case Btn.Danger:
                if (hovered || held) dl.AddRectFilled(min, max, Palette.U32(Palette.HoverButton), r);
                dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.DangerEdge), r, ImDrawFlags.None, 1f);
                text = Palette.Danger;
                break;
            case Btn.On:
                dl.AddRectFilled(min, max, Palette.U32(Palette.On), r);
                text = Palette.White;
                break;
            case Btn.Ember:
                dl.AddRectFilled(min, max, Palette.U32(hovered ? Palette.Rgba(255, 138, 61, 0.17f) : Palette.EmberWash), r);
                text = Palette.Ember;
                break;
            default:
                if (hovered || held) dl.AddRectFilled(min, max, Palette.U32(Palette.HoverButton), r);
                dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(hovered ? Palette.Rgba(255, 255, 255, 0.3f) : Palette.Line3), r, ImDrawFlags.None, 1f);
                text = Palette.White;
                break;
        }
        if (ink is { } custom) text = custom;

        var face = FaceOf(style);
        var iconPx = S(IconPx);
        // A label wider than a button of a set width is cut to fit.
        if (width > 0 && label.Length > 0)
            label = Txt.Fit(label, face, px, MathF.Max(0, w - S(padCss) * 2 - (icon != null ? iconPx + S(8) : 0) + 1));
        var textW = label.Length > 0 ? Txt.Width(label, face, px) : 0;
        var content = (icon != null ? iconPx : 0) + (icon != null && label.Length > 0 ? S(8) : 0) + textW;
        var x = pos.X + (w - content) / 2;
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(x, pos.Y + (h - iconPx) / 2), iconPx, iconInk ?? text);
            x += iconPx + (label.Length > 0 ? S(8) : 0);
        }
        if (label.Length > 0) Txt.Line(dl, new Vector2(x, pos.Y), label, face, px, text, h / S(1));
        Palette.Opacity = old;
        FocusRing(pos, pos + new Vector2(w, h), r);
        return clicked;
    }

    /// <summary>A square button with only an icon (`.btn.icon`); its words are the tooltip, as aria-label is.</summary>
    public static bool IconButton(string id, Vector2 pos, Icon icon, string tip, Btn style = Btn.Outline, BtnSize size = BtnSize.Md, bool disabled = false,
        Vector4? ink = null) =>
        Button(id, pos, string.Empty, style, size, icon, 0, disabled, tip, 1f, ink);

    // ---------------------------------------------------------------------------------------------
    // Switch, segments, stepper, slider.

    public static Vector2 SwitchSize => new(S(34), S(20));

    /// <summary>The planner's switch (`.switch`), 34×20: white 14% with a white knob, ember with a dark knob when on; the knob slides 14px over 160ms. Returns true when flipped.</summary>
    public static bool Switch(string id, Vector2 pos, bool on, bool disabled = false)
    {
        var size = SwitchSize;
        var (clicked, hovered, _) = Hit(id, pos, size, disabled);
        var t = Approach(id + "##knob", on ? 1f : 0f, TStep);
        var e = Ease.Snap(t);
        var dl = Dl;
        var max = pos + size;
        var r = size.Y / 2;
        var off = hovered ? Palette.Rgba(255, 255, 255, 0.2f) : Palette.Rgba(255, 255, 255, 0.14f);
        dl.AddRectFilled(pos, max, Palette.U32(Palette.Mix(off, Palette.Ember, e)), r);
        var knob = pos + new Vector2(S(3 + 7) + S(14) * e, size.Y / 2);
        dl.AddCircleFilled(knob, S(7), Palette.U32(Palette.Mix(Palette.White, Palette.OnEmber, e)), 24);
        FocusRing(pos, max, r);
        return clicked;
    }

    /// <summary>A segmented control's size: dense (`.seg.sm`, 32 tall) or not (40).</summary>
    private static (float H, float R, float ItemR, float Pad, int Px) SegSpec(bool dense) =>
        dense ? (HSm, Palette.RadiusSm, 5, 9.6f, 12) : (H, Palette.RadiusMd, 7, 12.8f, 13);

    /// <summary>The width of a segmented control: its items share it evenly, each as wide as the widest needs.</summary>
    public static float SegmentedWidth(IReadOnlyList<string> items, bool dense = true, float height = 0)
    {
        var (_, _, _, pad, px) = SegSpec(dense);
        var widest = 0f;
        foreach (var s in items) widest = MathF.Max(widest, Txt.Width(s, Face.SansMedium, px));
        return S(3) * 2 + S(2) * (items.Count - 1) + items.Count * (widest + S(pad) * 2) + 2;
    }

    /// <summary>
    /// A segmented control (`.seg`): a well with a hairline, 3px inside; the picked one raised with its
    /// own hairline and white words, the rest muted. Returns the item picked (the one given when none was).
    /// </summary>
    public static int Segmented(string id, Vector2 pos, IReadOnlyList<string> items, int selected, bool dense = true, bool disabled = false, float height = 0)
    {
        var (hCss, rCss, itemR, pad, px) = SegSpec(dense);
        var h = height > 0 ? height : S(hCss);
        var total = SegmentedWidth(items, dense);
        var dl = Dl;
        var old = Palette.Opacity;
        if (disabled) Palette.Opacity = old * 0.45f;
        dl.AddRectFilled(pos, pos + new Vector2(total, h), Palette.U32(Palette.Well), S(rCss));
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(total, h) - new Vector2(0.5f), Palette.U32(Palette.Line2), S(rCss), ImDrawFlags.None, 1f);
        var itemW = (total - 2 - S(3) * 2 - S(2) * (items.Count - 1)) / items.Count;
        var x = pos.X + 1 + S(3);
        var result = selected;
        for (var i = 0; i < items.Count; i++)
        {
            var p = new Vector2(x, pos.Y + 1 + S(3));
            var size = new Vector2(itemW, h - 2 - S(6));
            var on = i == selected;
            var (clicked, hovered, _) = Hit($"{id}{i}", p, size, disabled);
            if (clicked) result = i;
            if (on)
            {
                dl.AddRectFilled(p, p + size, Palette.U32(Palette.Raised), S(itemR));
                dl.AddRect(p + new Vector2(0.5f), p + size - new Vector2(0.5f), Palette.U32(Palette.Line2), S(itemR), ImDrawFlags.None, 1f);
            }
            var tw = Txt.Width(items[i], Face.SansMedium, px);
            Txt.Line(dl, new Vector2(p.X + (size.X - tw) / 2, p.Y), items[i], Face.SansMedium, px, on || hovered ? Palette.White : Palette.Muted, size.Y / S(1));
            FocusRing(p, p + size, S(itemR));
            x += itemW + S(2);
        }
        Palette.Opacity = old;
        return result;
    }

    /// <summary>The stepper's width: two 30px buttons round the value (mono 600 13px, at least 2.6rem).</summary>
    public static float StepperWidth(string value) => S(30) * 2 + MathF.Max(S(41.6f), Txt.Width(value, Face.MonoSemibold, 13) + S(8)) + 2;

    /// <summary>A stepper (`.stepper`, 40 tall with 10px corners in Settings): − value +, in a well with a hairline. Returns −1, 0 or +1.</summary>
    public static int Stepper(string id, Vector2 pos, string value, bool canDown, bool canUp)
    {
        var dl = Dl;
        var h = S(H);
        var w = StepperWidth(value);
        var r = S(Palette.RadiusMd);
        dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(Palette.Well), r);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.Line2), r, ImDrawFlags.None, 1f);
        var result = 0;
        var bw = S(30);
        foreach (var (dir, x, icon, can) in new[] { (-1, pos.X + 1, Icon.Minus, canDown), (1, pos.X + w - 1 - bw, Icon.Plus, canUp) })
        {
            var p = new Vector2(x, pos.Y);
            var (clicked, hovered, held) = Hit($"{id}{dir}", p, new Vector2(bw, h), !can);
            if (clicked) result = dir;
            var old = Palette.Opacity;
            if (!can) Palette.Opacity = old * 0.45f;
            if (hovered || held) dl.AddRectFilled(p + new Vector2(S(3)), p + new Vector2(bw, h) - new Vector2(S(3)), Palette.U32(Palette.HoverButton), S(6));
            Icons.Draw(dl, icon, p + new Vector2((bw - S(IconPx)) / 2, (h - S(IconPx)) / 2), S(IconPx), hovered ? Palette.White : Palette.Muted);
            Palette.Opacity = old;
            FocusRing(p, p + new Vector2(bw, h), S(6));
        }
        var tw = Txt.Width(value, Face.MonoSemibold, 13);
        Txt.Line(dl, new Vector2(pos.X + (w - tw) / 2, pos.Y), value, Face.MonoSemibold, 13, Palette.White, H);
        return result;
    }

    /// <summary>
    /// A slider (`.slider`), 20 tall: a 2px track in the line, ember to the value, a white 14px knob
    /// with a soft ember ring. Dragged or clicked anywhere on it; returns the value (0–1).
    /// </summary>
    public static float Slider(string id, Vector2 pos, float width, float value, bool disabled = false)
    {
        var h = S(20);
        var (_, hovered, held) = Hit(id, pos, new Vector2(width, h), disabled);
        if (held)
        {
            var mx = ImGui.GetMousePos().X;
            value = Math.Clamp((mx - pos.X) / MathF.Max(1, width), 0, 1);
        }
        value = Math.Clamp(value, 0, 1);
        var dl = Dl;
        var y = pos.Y + S(9);
        dl.AddRectFilled(new Vector2(pos.X, y), new Vector2(pos.X + width, y + S(2)), Palette.U32(Palette.Line3));
        dl.AddRectFilled(new Vector2(pos.X, y), new Vector2(pos.X + width * value, y + S(2)), Palette.U32(Palette.Ember));
        var c = new Vector2(pos.X + width * value, pos.Y + S(10));
        dl.AddCircleFilled(c, S(7 + 4), Palette.U32(Palette.Rgba(255, 138, 61, hovered || held ? 0.35f : 0.25f)), 24);
        dl.AddCircleFilled(c, S(7), Palette.U32(Palette.White), 24);
        FocusRing(pos, pos + new Vector2(width, h), S(4));
        return value;
    }

    /// <summary>
    /// A field you type in (`.field`): a 40px well with 10px corners, a hairline edge (white with a
    /// faint 3px ring while you type, red when what's in it is wrong), an icon before the words.
    /// The caret and the selection are ImGui's own. Returns true when the text changed.
    /// </summary>
    /// <param name="flags">ImGui's own, as Password for a secret.</param>
    public static bool Field(string id, Vector2 pos, float width, ref string text, string hint, Icon? icon = null, int maxLength = 128,
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.None, bool wrong = false, Face face = Face.Sans, int px = 14)
    {
        var dl = Dl;
        var h = S(H);
        var r = S(Palette.RadiusMd);
        var max = pos + new Vector2(width, h);
        dl.AddRectFilled(pos, max, Palette.U32(Palette.Well), r);
        var x = pos.X + S(12);
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(x, pos.Y + (h - S(IconPx)) / 2), S(IconPx), Palette.Faint);
            x += S(IconPx + 8);
        }
        bool changed;
        bool active;
        using (Txt.Fonts.Push(face, px))
        {
            var fh = ImGui.GetFontSize();
            ImGui.PushStyleColor(ImGuiCol.Text, Palette.White);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Palette.Faint);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, (h - fh) / 2));
            var keep = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(new Vector2(x, pos.Y));
            ImGui.SetNextItemWidth(MathF.Max(S(20), max.X - S(12) - x));
            if (Blocked) ImGui.BeginDisabled();
            changed = ImGui.InputTextWithHint(id, hint, ref text, maxLength, flags);
            if (Blocked) ImGui.EndDisabled();
            active = ImGui.IsItemActive();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(2);
            ImGui.SetCursorScreenPos(keep);
        }
        if (active) dl.AddRect(pos - new Vector2(S(2.5f)), max + new Vector2(S(2.5f)), Palette.U32(Palette.Rgba(255, 255, 255, 0.08f)), r + S(2.5f), ImDrawFlags.None, S(3));
        var edge = active ? Palette.White : wrong ? Palette.DangerField : Palette.Line2;
        dl.AddRect(pos + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);
        return changed;
    }

    // ---------------------------------------------------------------------------------------------
    // Tooltips and menus.

    /// <summary>A tooltip: raised near-solid, a hairline, 8px corners, 13px words wrapped at 300.</summary>
    public static void Tooltip(string text)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(S(12), S(8)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, S(Palette.RadiusSm));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Palette.Raised);
        ImGui.PushStyleColor(ImGuiCol.Border, Palette.Line2);
        ImGui.PushStyleColor(ImGuiCol.Text, Palette.White);
        using (Txt.Fonts.Push(Face.Sans, 13))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(S(300));
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(3);
    }

    /// <summary>
    /// Opens a popup styled as the site's menu (`.menu`, raised #18181d, a white hairline at 12%, 14px
    /// corners, 5.6px inside), its shadow faked with rings round it (<see cref="Rings(ImDrawListPtr, Vector2, Vector2, float, float, float, float, float)"/>).
    /// Pair with <see cref="EndMenu"/> when true.
    /// </summary>
    public static bool BeginMenu(string id)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(S(5.6f)));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, S(Palette.RadiusLg));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Palette.Raised);
        ImGui.PushStyleColor(ImGuiCol.Border, Palette.Line2);
        var open = ImGui.BeginPopup(id, ImGuiWindowFlags.NoMove);
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
        if (open)
        {
            Frame.Open.Push(true);
            // The rings go in the menu's own draw list, unclipped: on the background they'd sit under the
            // window the menu opens over (all but hidden by its near-solid ground).
            var dl = ImGui.GetWindowDrawList();
            dl.PushClipRectFullScreen();
            Rings(dl, ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), S(Palette.RadiusLg), 6, 14);
            dl.PopClipRect();
        }
        return open;
    }

    public static void EndMenu()
    {
        if (Frame.Open.Count > 0) Frame.Open.Pop();
        ImGui.EndPopup();
    }

    /// <summary>
    /// The site's floating shadow, faked as the mock-ups fake it: two rings of black outside a box
    /// (`0 0 0 6px rgb(0 0 0/.25), 0 0 0 14px rgb(0 0 0/.12)` for menus and dialogs; 5 and 11 for
    /// windows), on the background, so they sit under every window.
    /// </summary>
    public static void Rings(Vector2 min, Vector2 max, float radius, float inner, float outer, float innerAlpha = 0.25f, float outerAlpha = 0.12f) =>
        Rings(ImGui.GetBackgroundDrawList(), min, max, radius, inner, outer, innerAlpha, outerAlpha);

    /// <summary>The faked shadow's rings, in a draw list of your choosing (a menu's own, over the window under it).</summary>
    public static void Rings(ImDrawListPtr dl, Vector2 min, Vector2 max, float radius, float inner, float outer, float innerAlpha = 0.25f, float outerAlpha = 0.12f)
    {
        var a = S(inner) / 2;
        var b = S(inner) + (S(outer) - S(inner)) / 2;
        dl.AddRect(min - new Vector2(a), max + new Vector2(a), Palette.U32(Palette.WithAlpha(Palette.Black, innerAlpha)), radius + a, ImDrawFlags.None, S(inner));
        dl.AddRect(min - new Vector2(b), max + new Vector2(b), Palette.U32(Palette.WithAlpha(Palette.Black, outerAlpha)), radius + b, ImDrawFlags.None, S(outer) - S(inner));
    }

    /// <summary>A menu's eyebrow (`.menu .eyebrow`: .55rem .55rem .3rem).</summary>
    public static void MenuEyebrow(string text, float width)
    {
        var pos = ImGui.GetCursorScreenPos();
        var h = S(8.8f) + Txt.Natural(Face.Mono, 11) + S(4.8f);
        Eyebrow(ImGui.GetWindowDrawList(), pos + new Vector2(S(8.8f), S(8.8f)), text, null, width - S(17.6f));
        ImGui.Dummy(new Vector2(width, h));
    }

    /// <summary>A menu's rule (`.menu hr`: 1px line, .3rem .4rem round it).</summary>
    public static void MenuRule(float width)
    {
        var pos = ImGui.GetCursorScreenPos();
        var y = pos.Y + S(4.8f);
        ImGui.GetWindowDrawList().AddLine(new Vector2(pos.X + S(6.4f), y + 0.5f), new Vector2(pos.X + width - S(6.4f), y + 0.5f), Palette.U32(Palette.Line), 1f);
        ImGui.Dummy(new Vector2(width, S(9.6f) + 1));
    }

    /// <summary>
    /// A row in a menu (`.menu .row`, 38 tall, dense 34): a 20px slot at the left whatever's in it (so
    /// the names line up), the name (550 14px white) with an optional line under it, something on the
    /// right; pointed at, a faint fill and the reticle. Returns (clicked, hovered, the row's rectangle).
    /// </summary>
    public static (bool Clicked, bool Hovered, Vector2 Min, Vector2 Max) MenuRow(string id, float width, string name, string? sub = null, bool dense = false,
        float slot = 20, float reserve = 0)
    {
        var pos = ImGui.GetCursorScreenPos();
        var nameH = Txt.Natural(Face.SansMedium, 14) * 1.25f / 1.3f;
        var subH = sub != null ? Txt.Natural(Face.Sans, 12) : 0;
        var padY = S(dense ? 4 : 5.6f);
        var h = MathF.Max(S(dense ? 34 : 38), padY * 2 + nameH + subH);
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var min = pos;
        var max = pos + new Vector2(width, h);
        if (hovered)
        {
            dl.AddRectFilled(min, max, Palette.U32(Palette.Hover), S(Palette.RadiusMd));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        Reticle(dl, id, min, max, hovered);
        var x = pos.X + S(dense ? 8 : 8.8f) + S(slot) + S(dense ? 8 : 10.4f);
        var top = pos.Y + (h - nameH - subH) / 2;
        var room = pos.X + width - x - S(8) - reserve;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(name, Face.SansMedium, 14, room), Face.SansMedium, 14, Palette.White, nameH / S(1));
        if (sub != null) Txt.Line(dl, new Vector2(x, top + nameH), Txt.Fit(sub, Face.Sans, 12, room), Face.Sans, 12, Palette.Muted);
        return (clicked, hovered, min, max);
    }

    /// <summary>Where a menu row's 20px slot is, and its right edge's inside (for a count or a tag).</summary>
    public static (Vector2 Slot, float Right) MenuRowParts(Vector2 min, Vector2 max, bool dense = false, float slot = 20) =>
        (new Vector2(min.X + S(dense ? 8 : 8.8f), (min.Y + max.Y) / 2 - S(slot) / 2), max.X - S(dense ? 8 : 8.8f));
}
