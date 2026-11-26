using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The Raid Codex's buttons (`.btn`, since 0.9.1 the redesign's): one primary a screen (ember), the rest plain (the well
/// and the controls' line: never see-through), danger, the destructive answer, and a picked or locked one.
/// </summary>
public enum Btn
{
    /// <summary>The primary (`.btn.primary`, the site's `rp-tele`): ember from light to deep, dark words; the one thing a screen is for.</summary>
    Tele,

    /// <summary>Plain (`.btn`): the well with the controls' line, white words; lighter when pointed at.</summary>
    Outline,

    /// <summary>
    /// A secondary action. Since 0.9.1 drawn as <see cref="Outline"/>: the site's buttons are never see-through (its rule,
    /// 24 November 2026), so a quiet one is a plain one.
    /// </summary>
    Quiet,

    /// <summary>Plain with red words and a red line (`.btn.danger`): Stop, Delete.</summary>
    Danger,

    /// <summary>The destructive answer in a question (`.rp-danger-fill`): red, dark words.</summary>
    DangerFill,

    /// <summary>Picked (`.chip.on`, `.rp-way[aria-pressed]`): white for the picked, its line and words white.</summary>
    On,

    /// <summary>On, in ember (`.rp-on`): an ember line and words; a lock that's locked.</summary>
    Ember,
}

/// <summary>Control heights: 40 (`--ctl`), 32 in a head, a row or a card (`--ctl-sm`), 48 for the one large button (`.btn-lg`).</summary>
public enum BtnSize
{
    Md,
    Sm,
    Lg,
}

/// <summary>
/// The Raid Codex's controls (since 0.9.1 the redesign's), drawn with ImGui's draw lists: 40px buttons with 10px corners
/// (32 and 8 in a head, a row or a card), plain on the well with the controls' line, the one primary in ember, switches
/// on in ember, pick-ones whose picked one is filled, steppers, sliders, the 40px well field. Each
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

    /// <summary>A size's height, side padding, label size, corner and the gap after its icon (CSS px).</summary>
    private static (float Height, float Pad, int Px, float Radius, float Gap) Spec(BtnSize size) => size switch
    {
        // `.btn.sm`: 32 tall, 12px in, 13px words, 8px corners, 6 after the icon.
        BtnSize.Sm => (HSm, 12, 13, Palette.RadiusSm, 6),
        // `.btn.lg`: 48 tall, 22px in, 15px words (14 here: the size loaded), 12px corners.
        BtnSize.Lg => (HLg, 22, 14, 12, 8),
        // `.btn`: 40 tall, 16px in, 14px words, 10px corners, 8 after the icon.
        _ => (H, 16, 14, Palette.RadiusMd, 8),
    };

    /// <summary>The primary's and the destructive answer's words are 600; the rest 500.</summary>
    private static Face FaceOf(Btn style) => style is Btn.Tele or Btn.DangerFill ? Face.SansSemibold : Face.SansMedium;

    /// <summary>The icon in a button, `.i-sm`: 15px.</summary>
    public const float IconPx = 15;

    public static float ButtonHeight(BtnSize size = BtnSize.Md) => S(Spec(size).Height);

    public static float ButtonWidth(string label, BtnSize size = BtnSize.Md, Icon? icon = null, Btn style = Btn.Outline)
    {
        var (h, pad, px, _, gap) = Spec(size);
        if (label.Length == 0) return S(h);
        var text = Txt.Width(label, FaceOf(style), px);
        return S(pad) * 2 + text + (icon != null ? S(IconPx + gap) : 0);
    }

    /// <summary>
    /// A button. Width 0 fits its words; an empty label makes it a square icon button (`.btn.icon`).
    /// <paramref name="dim"/> fades it without disabling it (the telegraph while a send is under way).
    /// </summary>
    public static bool Button(string id, Vector2 pos, string label, Btn style = Btn.Outline, BtnSize size = BtnSize.Md, Icon? icon = null,
        float width = 0, bool disabled = false, string? tip = null, float dim = 1f, Vector4? ink = null)
    {
        var (hCss, padCss, px, rCss, gapCss) = Spec(size);
        var h = S(hCss);
        var w = width > 0 ? width : label.Length == 0 ? h : ButtonWidth(label, size, icon, style);
        var (clicked, hovered, held) = Hit(id, pos, new Vector2(w, h), disabled);
        if (tip != null && Pointed(pos, pos + new Vector2(w, h))) Tooltip(tip);

        var old = Palette.Opacity;
        // A button that can't be pressed now, and a primary held still while it works.
        if (disabled) Palette.Opacity = old * 0.45f;
        Palette.Opacity *= dim;
        var dl = Dl;
        var r = S(rCss);
        // :active dips the shape to 0.97 (the words can't scale crisply, so they stay).
        var inset = held ? new Vector2(w, h) * 0.015f : Vector2.Zero;
        var min = pos + inset;
        var max = pos + new Vector2(w, h) - inset;
        Vector4 text;
        switch (style)
        {
            case Btn.Tele:
                Primary(dl, min, max, r, hovered);
                text = Palette.OnEmber;
                break;
            case Btn.DangerFill:
                dl.AddRectFilled(min, max, Palette.U32(hovered ? Palette.DangerFillHover : Palette.Danger), r);
                dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.DangerFillEdge), r, ImDrawFlags.None, 1f);
                text = Palette.OnDanger;
                break;
            case Btn.Danger:
                Plain(dl, min, max, r, hovered || held, hovered ? Palette.Danger : Palette.DangerLine);
                text = Palette.Danger;
                break;
            case Btn.On:
                dl.AddRectFilled(min, max, Palette.U32(Palette.PanelTop), r);
                dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.Rgba(255, 255, 255, hovered ? 0.55f : 0.4f)), r, ImDrawFlags.None, 1f);
                text = Palette.White;
                break;
            case Btn.Ember:
                dl.AddRectFilled(min, max, Palette.U32(hovered ? Palette.Rgba(255, 138, 61, 0.17f) : Palette.EmberWash), r);
                dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.EmberLine), r, ImDrawFlags.None, 1f);
                text = Palette.Ember;
                break;
            default:
                // Plain, and since 0.9.1 the quiet one too.
                Plain(dl, min, max, r, hovered || held);
                text = Palette.White;
                break;
        }
        if (ink is { } custom) text = custom;

        var face = FaceOf(style);
        var iconPx = S(IconPx);
        var gap = S(gapCss);
        // A label wider than a button of a set width is cut to fit.
        if (width > 0 && label.Length > 0)
            label = Txt.Fit(label, face, px, MathF.Max(0, w - S(padCss) * 2 - (icon != null ? iconPx + gap : 0) + 1));
        var textW = label.Length > 0 ? Txt.Width(label, face, px) : 0;
        var content = (icon != null ? iconPx : 0) + (icon != null && label.Length > 0 ? gap : 0) + textW;
        var x = pos.X + (w - content) / 2;
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(x, pos.Y + (h - iconPx) / 2), iconPx, text);
            x += iconPx + (label.Length > 0 ? gap : 0);
        }
        if (label.Length > 0) Txt.Line(dl, new Vector2(x, pos.Y), label, face, px, text, h / S(1));
        Palette.Opacity = old;
        FocusRing(pos, pos + new Vector2(w, h), r);
        return clicked;
    }

    /// <summary>A plain button's ground (`.btn`): the well and the controls' line; pointed at, #121216 and a lighter line.</summary>
    public static void Plain(ImDrawListPtr dl, Vector2 min, Vector2 max, float r, bool hovered, Vector4? line = null)
    {
        dl.AddRectFilled(min, max, Palette.U32(hovered ? Palette.BtnHover : Palette.Well), r);
        dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(line ?? (hovered ? Palette.BtnHoverLine : Palette.CtlLine)), r, ImDrawFlags.None, 1f);
    }

    /// <summary>
    /// The primary's ground (`.btn.primary`): ember from #ff9a52 at its top through ember at 55% to #f47d2f at its foot (its
    /// rounded ends solid, as ImGui can't round a gradient), a lighter edge, a white line along its top inside, and a soft ember
    /// glow under it.
    /// </summary>
    public static void Primary(ImDrawListPtr dl, Vector2 min, Vector2 max, float r, bool hovered)
    {
        var top = hovered ? Palette.TeleTopHover : Palette.TeleTop;
        var mid = hovered ? Palette.TeleHover : Palette.Ember;
        var foot = hovered ? Palette.TeleFootHover : Palette.TeleFoot;
        var h = max.Y - min.Y;
        // `0 6px 18px -8px rgb(ember / .6)`: a glow under it, faked with two soft bands.
        dl.AddRectFilled(new Vector2(min.X + S(4), max.Y - S(4)), new Vector2(max.X - S(4), max.Y + S(3)), Palette.U32(Palette.Rgba(255, 138, 61, hovered ? 0.22f : 0.14f)), r);
        dl.AddRectFilled(new Vector2(min.X + S(8), max.Y), new Vector2(max.X - S(8), max.Y + S(6)), Palette.U32(Palette.Rgba(255, 138, 61, hovered ? 0.1f : 0.06f)), r);
        var cap = MathF.Min(r, h / 2);
        var split = min.Y + h * 0.55f;
        dl.AddRectFilled(min, new Vector2(max.X, min.Y + cap * 2), Palette.U32(top), r, ImDrawFlags.RoundCornersTop);
        dl.AddRectFilled(new Vector2(min.X, max.Y - cap * 2), max, Palette.U32(foot), r, ImDrawFlags.RoundCornersBottom);
        if (split > min.Y + cap)
            dl.AddRectFilledMultiColor(new Vector2(min.X, min.Y + cap), new Vector2(max.X, split), Palette.U32(top), Palette.U32(top), Palette.U32(mid), Palette.U32(mid));
        if (max.Y - cap > split)
            dl.AddRectFilledMultiColor(new Vector2(min.X, split), new Vector2(max.X, max.Y - cap), Palette.U32(mid), Palette.U32(mid), Palette.U32(foot), Palette.U32(foot));
        dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.TeleEdge), r, ImDrawFlags.None, 1f);
        // `0 1px 0 rgb(255 255 255 / .35) inset`.
        dl.AddLine(new Vector2(min.X + r, min.Y + 1.5f), new Vector2(max.X - r, min.Y + 1.5f), Palette.U32(Palette.Rgba(255, 255, 255, 0.35f)), 1f);
    }

    /// <summary>A square button with only an icon (`.btn.icon`); its words are the tooltip, as aria-label is.</summary>
    public static bool IconButton(string id, Vector2 pos, Icon icon, string tip, Btn style = Btn.Outline, BtnSize size = BtnSize.Md, bool disabled = false,
        Vector4? ink = null) =>
        Button(id, pos, string.Empty, style, size, icon, 0, disabled, tip, 1f, ink);

    // ---------------------------------------------------------------------------------------------
    // Switch, segments, stepper, slider.

    public static Vector2 SwitchSize => new(S(34), S(20));

    /// <summary>The Raid Codex's switch (`.switch`), 34×20: #2a2a31 with a muted knob, ember with a dark knob when on; the knob slides 14px over 140ms. Returns true when flipped.</summary>
    public static bool Switch(string id, Vector2 pos, bool on, bool disabled = false)
    {
        var size = SwitchSize;
        var (clicked, hovered, _) = Hit(id, pos, size, disabled);
        var t = Approach(id + "##knob", on ? 1f : 0f, TQuick);
        var e = Ease.Snap(t);
        var dl = Dl;
        var max = pos + size;
        var r = size.Y / 2;
        var off = hovered ? Palette.Hex(0x34343b) : Palette.SwitchOff;
        dl.AddRectFilled(pos, max, Palette.U32(Palette.Mix(off, Palette.Ember, e)), r);
        var knob = pos + new Vector2(S(3 + 7) + S(14) * e, size.Y / 2);
        dl.AddCircleFilled(knob, S(7), Palette.U32(Palette.Mix(hovered ? Palette.Text : Palette.Muted, Palette.OnEmber, e)), 24);
        FocusRing(pos, max, r);
        return clicked;
    }

    /// <summary>A pick-one's size: dense (`.seg.sm`, 32 tall, 10px in, 12.5px words) or not (40, 14px in, 13.5px words).</summary>
    private static (float H, float R, float ItemR, float Pad, int Px) SegSpec(bool dense) =>
        dense ? (HSm, Palette.RadiusSm, 5, 10, 12) : (H, Palette.RadiusMd, 7, 14, 13);

    /// <summary>The width of a segmented control: its items share it evenly, each as wide as the widest needs.</summary>
    public static float SegmentedWidth(IReadOnlyList<string> items, bool dense = true, float height = 0)
    {
        var (_, _, _, pad, px) = SegSpec(dense);
        var widest = 0f;
        foreach (var s in items) widest = MathF.Max(widest, Txt.Width(s, Face.SansMedium, px));
        return S(3) * 2 + items.Count * (widest + S(pad) * 2) + 2;
    }

    /// <summary>
    /// A pick-one (`.seg`, the site's Toggle): the well with the controls' line, 3px inside; the picked one filled in ember with
    /// dark words (<paramref name="quiet"/>: white, where ember is taken), the rest in the text colour, white when pointed at.
    /// Returns the item picked (the one given when none was).
    /// </summary>
    public static int Segmented(string id, Vector2 pos, IReadOnlyList<string> items, int selected, bool dense = true, bool disabled = false, float height = 0,
        bool quiet = false)
    {
        var (hCss, rCss, itemR, pad, px) = SegSpec(dense);
        var h = height > 0 ? height : S(hCss);
        var total = SegmentedWidth(items, dense);
        var dl = Dl;
        var old = Palette.Opacity;
        if (disabled) Palette.Opacity = old * 0.45f;
        dl.AddRectFilled(pos, pos + new Vector2(total, h), Palette.U32(Palette.Well), S(rCss));
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(total, h) - new Vector2(0.5f), Palette.U32(Palette.CtlLine), S(rCss), ImDrawFlags.None, 1f);
        var itemW = (total - 2 - S(3) * 2) / items.Count;
        var x = pos.X + 1 + S(3);
        var result = selected;
        for (var i = 0; i < items.Count; i++)
        {
            var p = new Vector2(x, pos.Y + 1 + S(3));
            var size = new Vector2(itemW, h - 2 - S(6));
            var on = i == selected;
            var (clicked, hovered, _) = Hit($"{id}{i}", p, size, disabled);
            if (clicked) result = i;
            if (on) dl.AddRectFilled(p, p + size, Palette.U32(quiet ? Palette.White : Palette.Ember), S(itemR));
            var tw = Txt.Width(items[i], Face.SansMedium, px);
            var ink = on ? quiet ? Palette.Void : Palette.OnEmber : hovered ? Palette.White : Palette.Text;
            Txt.Line(dl, new Vector2(p.X + (size.X - tw) / 2, p.Y), items[i], Face.SansMedium, px, ink, size.Y / S(1));
            FocusRing(p, p + size, S(itemR));
            x += itemW;
        }
        Palette.Opacity = old;
        return result;
    }

    /// <summary>The stepper's width: two 30px buttons round the value (mono 600 13px, at least 2.6rem).</summary>
    public static float StepperWidth(string value) => S(30) * 2 + MathF.Max(S(41.6f), Txt.Width(value, Face.MonoSemibold, 13) + S(8)) + 2;

    /// <summary>A stepper (`.rp-stepper`, 40 tall with 10px corners): − value +, in the well with the controls' line. Returns −1, 0 or +1.</summary>
    public static int Stepper(string id, Vector2 pos, string value, bool canDown, bool canUp)
    {
        var dl = Dl;
        var h = S(H);
        var w = StepperWidth(value);
        var r = S(Palette.RadiusMd);
        dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(Palette.Well), r);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.CtlLine), r, ImDrawFlags.None, 1f);
        var result = 0;
        var bw = S(30);
        foreach (var (dir, x, icon, can) in new[] { (-1, pos.X + 1, Icon.Minus, canDown), (1, pos.X + w - 1 - bw, Icon.Plus, canUp) })
        {
            var p = new Vector2(x, pos.Y);
            var (clicked, hovered, held) = Hit($"{id}{dir}", p, new Vector2(bw, h), !can);
            if (clicked) result = dir;
            var old = Palette.Opacity;
            if (!can) Palette.Opacity = old * 0.45f;
            if (hovered || held) dl.AddRectFilled(p + new Vector2(S(3)), p + new Vector2(bw, h) - new Vector2(S(3)), Palette.U32(Palette.Raised), S(6));
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
    /// A field you type in (`.field`): a 40px well with 10px corners and the controls' line (lighter pointed at; lighter
    /// still with a faint 3px ring while you type; red when what's in it is wrong), an icon before the words.
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
        if (active) dl.AddRect(pos - new Vector2(S(2.5f)), max + new Vector2(S(2.5f)), Palette.U32(Palette.Rgba(255, 255, 255, 0.06f)), r + S(2.5f), ImDrawFlags.None, S(3));
        var pointed = !active && Pointed(pos, max);
        var edge = active ? Palette.CtlLineFocus : wrong ? Palette.DangerField : pointed ? Palette.CtlLineHover : Palette.CtlLine;
        dl.AddRect(pos + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);
        return changed;
    }

    // ---------------------------------------------------------------------------------------------
    // Tooltips and menus.

    /// <summary>A tooltip (`.tip`): #1f1f25, a hairline at 12%, 8px corners, 12px words at 500 wrapped at 300.</summary>
    public static void Tooltip(string text)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(S(10), S(7)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, S(Palette.RadiusSm));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Palette.Tip);
        ImGui.PushStyleColor(ImGuiCol.Border, Palette.Line2);
        ImGui.PushStyleColor(ImGuiCol.Text, Palette.White);
        using (Txt.Fonts.Push(Face.SansMedium, 12))
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
    /// Opens a popup styled as the site's menu (`.menu`, #111116, a white hairline at 12%, 14px
    /// corners, 6px inside), its shadow faked with rings round it (<see cref="Rings(ImDrawListPtr, Vector2, Vector2, float, float, float, float, float)"/>).
    /// Pair with <see cref="EndMenu"/> when true.
    /// </summary>
    /// <param name="ground">Its ground (a theme's: the opened-up meter's picker); null the kit's raised slab.</param>
    /// <param name="edge">Its hairline; null the kit's.</param>
    /// <param name="radius">Its corners (CSS px); null the kit's.</param>
    public static bool BeginMenu(string id, Vector4? ground = null, Vector4? edge = null, float? radius = null)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(S(6)));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, S(radius ?? Palette.RadiusLg));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, ground ?? Palette.Menu);
        ImGui.PushStyleColor(ImGuiCol.Border, edge ?? Palette.Line2);
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
            Rings(dl, ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), S(radius ?? Palette.RadiusLg), 6, 14);
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
    /// A row in a menu (`.menu .mi`, 38 tall, dense 34): a 20px slot at the left whatever's in it (so
    /// the names line up), the name (500 14px, the text colour, white when pointed at) with an optional line
    /// under it, something on the right; pointed at, the raised fill with 8px corners. Returns (clicked, hovered, the row's rectangle).
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
            dl.AddRectFilled(min, max, Palette.U32(Palette.Raised), S(Palette.RadiusSm));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        var x = pos.X + S(dense ? 8 : 10) + S(slot) + S(dense ? 8 : 10);
        var top = pos.Y + (h - nameH - subH) / 2;
        var room = pos.X + width - x - S(8) - reserve;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(name, Face.SansMedium, 14, room), Face.SansMedium, 14, hovered ? Palette.White : Palette.Text, nameH / S(1));
        if (sub != null) Txt.Line(dl, new Vector2(x, top + nameH), Txt.Fit(sub, Face.Sans, 12, room), Face.Sans, 12, Palette.Faint);
        return (clicked, hovered, min, max);
    }

    /// <summary>Where a menu row's 20px slot is, and its right edge's inside (for a count or a tag).</summary>
    public static (Vector2 Slot, float Right) MenuRowParts(Vector2 min, Vector2 max, bool dense = false, float slot = 20) =>
        (new Vector2(min.X + S(dense ? 8 : 10), (min.Y + max.Y) / 2 - S(slot) / 2), max.X - S(dense ? 8 : 10));
}
