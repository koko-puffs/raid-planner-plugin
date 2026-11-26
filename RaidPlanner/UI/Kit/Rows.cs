using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>A tab: its words, an optional icon before them and an optional count after (`.tab .n`).</summary>
public readonly record struct TabSpec(string Label, Icon? Icon = null, string? Count = null);

/// <summary>A tab row's size: Settings' (`.tab`, 40 tall, 14px), dense (`.tab.sm`, 32, 13px) or the meter's (`.mt-tabs`, 27, 12px).</summary>
public enum TabSize
{
    Md,
    Sm,
    Meter,
}

/// <summary>Lays things left to right from the cursor, wrapping at the box's edge.</summary>
public sealed class Wrap
{
    private readonly float gap;
    private readonly Vector2 start;
    private float x;
    private float y;
    private float rowH;

    public Wrap(float gapCss)
    {
        gap = S(gapCss);
        start = Kit.At;
        x = start.X;
        y = start.Y;
    }

    /// <summary>Where the next thing of this size goes.</summary>
    public Vector2 Next(float w, float h)
    {
        if (x > start.X && x + w > Kit.Right + 0.5f)
        {
            x = start.X;
            y += rowH + gap;
            rowH = 0;
        }
        var p = new Vector2(x, y);
        x += w + gap;
        rowH = MathF.Max(rowH, h);
        return p;
    }

    /// <summary>Takes up the rows' room.</summary>
    public void End() => Kit.Reserve(start, Kit.Width, y + rowH - start.Y);
}

/// <summary>
/// The rows the screens share: a section's head (a small callout, a muted aside, a link on the
/// right), tabs with the ember cast bar under the one you're on, the facts box (mono labels over
/// mono values), slabs, and links with their arrow.
/// </summary>
public static partial class Kit
{
    // ---------------------------------------------------------------------------------------------
    // Section heads.

    /// <summary>
    /// A section's head outside a panel (the site's panel head, `.rp-panel-h`, since 0.9.1): its title in Geist 600 15px white,
    /// a faint aside after it on the same baseline (a count, what it holds), and on the right an optional link with its
    /// arrow. Returns true when the link is pressed.
    /// </summary>
    public static bool Section(string id, string title, string? aside = null, string? link = null)
    {
        var at = At;
        var w = Width;
        var dl = Dl;
        var titleH = S(15 * 1.3f);
        var linkW = link != null ? LinkWidth(link, Icon.ArrowRight) : 0;
        var room = w - (link != null ? linkW + S(12) : 0);
        var tw = Txt.Line(dl, at, Txt.Fit(title, Face.SansSemibold, 15, room), Face.SansSemibold, 15, Palette.White, 15 * 1.3f);
        // align-items: baseline.
        var baseline = at.Y + (titleH - Txt.Natural(Face.SansSemibold, 15)) / 2 + Txt.Ascent(Face.SansSemibold, 15);
        if (aside != null && tw + S(10) < room)
            Txt.Draw(dl, new Vector2(at.X + tw + S(10), baseline - Txt.Ascent(Face.Sans, 12)), Txt.Fit(aside, Face.Sans, 12, room - tw - S(10)), Face.Sans, 12, Palette.Faint);
        var pressed = false;
        if (link != null)
        {
            var lh = Txt.Natural(Face.SansMedium, 13);
            pressed = Link(id, new Vector2(at.X + w - linkW, baseline - Txt.Ascent(Face.SansMedium, 13)), link, Icon.ArrowRight);
            _ = lh;
        }
        Reserve(at, w, titleH);
        return pressed;
    }

    // ---------------------------------------------------------------------------------------------
    // Links.

    public static float LinkWidth(string text, Icon? icon = null, int px = 13) =>
        Txt.Width(text, Face.SansMedium, px) + (icon != null ? S(4) + S(14) : 0);

    /// <summary>
    /// A link (`.link`): white 13px at 500, underlined 3px under in a faint line, its arrow after it
    /// (14px); pointed at, the line turns white. Returns true when pressed.
    /// </summary>
    public static bool Link(string id, Vector2 pos, string text, Icon? icon = null, int px = 13, string? tip = null)
    {
        var w = LinkWidth(text, icon, px);
        var h = Txt.Natural(Face.SansMedium, px);
        var (clicked, hovered, _) = Hit(id, pos - new Vector2(0, S(2)), new Vector2(w, h + S(5)));
        if (tip != null && hovered) Tooltip(tip);
        var dl = Dl;
        var color = Palette.White;
        var tw = Txt.Draw(dl, pos, text, Face.SansMedium, px, color);
        var under = MathF.Round(pos.Y + Txt.Ascent(Face.SansMedium, px) + S(3)) + 0.5f;
        dl.AddLine(new Vector2(pos.X, under), new Vector2(pos.X + tw, under), Palette.U32(hovered ? Palette.White : Palette.Line3), 1f);
        if (icon is { } ic) Icons.Draw(dl, ic, new Vector2(pos.X + tw + S(4), pos.Y + (h - S(14)) / 2), S(14), color, 1.75f);
        FocusRing(pos, pos + new Vector2(w, h), S(4));
        return clicked;
    }

    // ---------------------------------------------------------------------------------------------
    // Tabs.

    private static (float H, float Pad, int Px, float Gap, float BarH, float BarBottom, float BarIn) TabSpecOf(TabSize size) => size switch
    {
        TabSize.Sm => (HSm, 9.6f, 13, 3.2f, 2, 0, 9.6f),
        TabSize.Meter => (27, 7, 12, 2, 2, 0, 7),
        _ => (H, 12, 14, 2, 2, 0, 12),
    };

    public static float TabWidth(TabSpec tab, TabSize size = TabSize.Md)
    {
        var (_, pad, px, _, _, _, _) = TabSpecOf(size);
        return S(pad) * 2 + Txt.Width(tab.Label, Face.SansMedium, px)
            + (tab.Icon != null ? S(IconPx + 7.2f) : 0)
            + (tab.Count != null ? S(7.2f) + Txt.Width(tab.Count, Face.Mono, 11) : 0);
    }

    public static float TabsWidth(IReadOnlyList<TabSpec> tabs, TabSize size = TabSize.Md)
    {
        var gap = S(TabSpecOf(size).Gap);
        var w = gap * (tabs.Count - 1);
        foreach (var t in tabs) w += TabWidth(t, size);
        return w;
    }

    /// <summary>
    /// Tabs as the site's (`.tabs`): muted words (500), the one you're on white with the ember line under it
    /// (a square-ended gradient, filling from the left over 180ms when you arrive); pointed at, white. <paramref name="rule"/> draws the line under the row
    /// across <paramref name="ruleWidth"/>. Returns the tab picked (the one given when none was).
    /// </summary>
    public static int Tabs(string id, Vector2 at, IReadOnlyList<TabSpec> tabs, int selected, TabSize size = TabSize.Md, bool rule = false, float ruleWidth = 0,
        float ruleLeft = float.NaN)
    {
        var (hCss, pad, px, gapCss, barH, barBottom, barIn) = TabSpecOf(size);
        var h = S(hCss);
        var dl = Dl;
        if (rule)
        {
            var l = float.IsNaN(ruleLeft) ? at.X : ruleLeft;
            dl.AddLine(new Vector2(l, at.Y + h - 0.5f), new Vector2(l + ruleWidth, at.Y + h - 0.5f), Palette.U32(Palette.Line), 1f);
        }
        var result = selected;
        var x = at.X;
        // The bar fills from the left each time the tab changes.
        var storage = ImGui.GetStateStorage();
        var lastKey = ImGui.GetID(id + "##last");
        var arrived = Since(id + "##arrived", storage.GetInt(lastKey, -1) != selected);
        storage.SetInt(lastKey, selected);
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            var w = TabWidth(tab, size);
            var p = new Vector2(x, at.Y);
            var max = p + new Vector2(w, h);
            var (clicked, hovered, _) = Hit($"{id}{i}", p, new Vector2(w, h));
            if (clicked) result = i;
            var on = i == selected;
            var ink = on || hovered ? Palette.White : Palette.Muted;
            var tx = p.X + S(pad);
            if (tab.Icon is { } ic)
            {
                Icons.Draw(dl, ic, new Vector2(tx, p.Y + (h - S(IconPx)) / 2), S(IconPx), ink, 1.75f);
                tx += S(IconPx + 7.2f);
            }
            tx += Txt.Line(dl, new Vector2(tx, p.Y), tab.Label, Face.SansMedium, px, ink, hCss);
            if (tab.Count != null) Txt.Line(dl, new Vector2(tx + S(7.2f), p.Y), tab.Count, Face.Mono, 11, Palette.Faint, hCss);
            if (on)
            {
                var fill = ReducedMotion ? 1 : Ease.Out(Math.Clamp(arrived / TStep, 0, 1));
                var bx = p.X + S(barIn);
                var bw = (w - S(barIn) * 2) * fill;
                var by = max.Y - S(barBottom) - S(barH);
                if (bw >= 1) CastGradient(dl, new Vector2(bx, by), new Vector2(bx + bw, by + S(barH)));
            }
            FocusRing(p, max, S(Palette.RadiusSm));
            x += w + S(gapCss);
        }
        return result;
    }

    /// <summary>The cast bar's ember gradient over a rectangle (deep, ember at 70%, light), square-ended.</summary>
    public static void CastGradient(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        var mid = min.X + (max.X - min.X) * 0.7f;
        var deep = Palette.U32(Palette.EmberDeep);
        var ember = Palette.U32(Palette.Ember);
        var hi = Palette.U32(Palette.EmberHi);
        dl.AddRectFilledMultiColor(min, new Vector2(mid, max.Y), deep, ember, ember, deep);
        dl.AddRectFilledMultiColor(new Vector2(mid, min.Y), max, ember, hi, hi, ember);
    }

    // ---------------------------------------------------------------------------------------------
    // Facts.

    /// <summary>One fact in the facts box: its label over its value, a quieter note after the value ("8 places"), the value's colour and face.</summary>
    public readonly record struct Fact(string Label, string Value, string? Note = null, Vector4? Color = null, bool Sans = false);

    /// <summary>
    /// The facts box (`.facts4`): one box with a hairline edge and 10px corners split into equal cells
    /// by hairlines; each a mono label in capitals (500 10px, +0.09em, faint) over its value in mono
    /// (600 16px, white) or sans (600 14px), a 12px faint note after it.
    /// </summary>
    public static void Facts(IReadOnlyList<Fact> facts)
    {
        if (facts.Count == 0) return;
        var at = At;
        var w = Width;
        var cellW = w / facts.Count;
        var labelH = S(10);
        var valueH = S(18);
        var h = S(9) + labelH + S(7) + valueH + S(10);
        var dl = Dl;
        var r = S(Palette.RadiusMd);
        dl.AddRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.Line), r, ImDrawFlags.None, 1f);
        for (var i = 0; i < facts.Count; i++)
        {
            var p = at + new Vector2(cellW * i, 0);
            if (i > 0) dl.AddLine(new Vector2(MathF.Round(p.X) + 0.5f, p.Y + 1), new Vector2(MathF.Round(p.X) + 0.5f, p.Y + h - 1), Palette.U32(Palette.Line), 1f);
            var f = facts[i];
            var inner = cellW - S(24);
            var lx = p.X + S(12);
            Label(dl, new Vector2(lx, p.Y + S(9) + (labelH - Txt.Natural(Face.Mono, 10)) / 2), f.Label, 10, 0.09f, Palette.Faint, inner);
            var vy = p.Y + S(9) + labelH + S(7);
            var (vf, vpx) = f.Sans ? (Face.SansSemibold, 14) : (Face.MonoSemibold, 16);
            var baseline = vy + (valueH - Txt.Natural(vf, vpx)) / 2 + Txt.Ascent(vf, vpx);
            var vw = Txt.Draw(dl, new Vector2(lx, baseline - Txt.Ascent(vf, vpx)), Txt.Fit(f.Value, vf, vpx, inner), vf, vpx, f.Color ?? Palette.White);
            if (f.Note != null && vw + S(4) < inner)
                Txt.Draw(dl, new Vector2(lx + vw + S(4), baseline - Txt.Ascent(Face.Sans, 12)), Txt.Fit(f.Note, Face.Sans, 12, inner - vw - S(4)), Face.Sans, 12, Palette.Faint);
        }
        Reserve(at, w, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Panels: the content is laid first, the panel drawn behind it at the height it had last frame.

    private static readonly Dictionary<uint, float> Heights = new();

    private sealed class PanelState
    {
        public uint Id;
        public Vector2 Start;
        public float Width;
        public float PadBottom;
        public Vector4 Edge;
        public float Radius;
        public bool Lit;
        public bool Headed;
    }

    private static readonly Stack<PanelState> Panels = new();

    /// <summary>
    /// A panel (`.pn`, since 0.9.1 the redesign's): its ground from #141418 to #101013, a hairline edge lit ember from its top
    /// left, and a frame 4px inside it, 14px corners. Given another <paramref name="fill"/> or <paramref name="edge"/> it's a
    /// plain slab of those (a box in a box). Lay its rows between this and <see cref="EndPanel"/>; padding is CSS pixels
    /// (top, sides, bottom).
    /// </summary>
    public static void BeginPanel(string id, float padTop, float padX, float padBottom, Vector4? fill = null, Vector4? edge = null, float radius = Palette.RadiusLg)
    {
        var at = At;
        var w = Width;
        var key = ImGui.GetID(id);
        var r = S(radius);
        // The slabs of before 0.9.1 (#111115 with a hairline) are panels now; a box of another colour stays itself.
        var lit = (fill == null || fill == Palette.Slab) && (edge == null || edge == Palette.Line || edge == Palette.Line2);
        var f = fill ?? Palette.Slab;
        if (Heights.TryGetValue(key, out var h) && h > 0 && f.W > 0)
        {
            if (lit) PanelGround(Dl, at, at + new Vector2(w, h), r);
            else Dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(f), r);
        }
        Panels.Push(new PanelState { Id = key, Start = at, Width = w, PadBottom = S(padBottom), Edge = edge ?? Palette.PanelEdge, Radius = r, Lit = lit });
        ImGui.SetCursorScreenPos(at + new Vector2(0, S(padTop)));
        ImGui.Dummy(Vector2.Zero);
        ImGui.SetCursorScreenPos(at + new Vector2(0, S(padTop)));
        PushBox(at.X + S(padX), w - S(padX) * 2);
    }

    /// <summary>
    /// A panel with its head (`.pn-h`, the site's Panel's title and sub): <paramref name="title"/> in Geist 600 15px white, a
    /// faint line under it, and at the right one 32px plain button (<paramref name="button"/>); the rows under it each with a
    /// hairline over them. True when the button is pressed.
    /// </summary>
    public static bool BeginPanel(string id, string title, string? sub, float padTop, float padX, float padBottom, string? button = null, Icon? buttonIcon = null,
        string? buttonTip = null)
    {
        BeginPanel(id, 0, padX, padBottom);
        var pressed = PanelHead(id, title, sub, button, buttonIcon, buttonTip);
        Gap(padTop);
        return pressed;
    }

    /// <summary>
    /// The head of the panel being laid (`.pn-h`: at least 56 tall, 12 over and 10 under, 20 in at the left and 16 at the
    /// right), across the panel's whole width. True when its button is pressed.
    /// </summary>
    public static bool PanelHead(string id, string title, string? sub = null, string? button = null, Icon? buttonIcon = null, string? buttonTip = null)
    {
        var (start, pw, _) = Panel;
        var y = ImGui.GetCursorScreenPos().Y;
        var dl = Dl;
        var left = start.X + S(20);
        var right = start.X + pw - S(16);
        var bw = button != null ? ButtonWidth(button, BtnSize.Sm, buttonIcon) : 0;
        var room = right - left - (bw > 0 ? bw + S(12) : 0);
        var titleH = S(15 * 1.3f);
        var subH = sub != null ? S(2) + S(12 * 1.4f) : 0;
        var h = MathF.Max(S(56), S(12) + titleH + subH + S(10));
        var top = y + (h - titleH - subH) / 2;
        Txt.Line(dl, new Vector2(left, top), Txt.Fit(title, Face.SansSemibold, 15, room), Face.SansSemibold, 15, Palette.White, 15 * 1.3f);
        if (sub != null)
            Txt.Line(dl, new Vector2(left, top + titleH + S(2)), Txt.Fit(sub, Face.Sans, 12, room), Face.Sans, 12, Palette.Faint, 12 * 1.4f);
        var pressed = button != null && Button(id + "##head", new Vector2(right - bw, y + (h - S(HSm)) / 2), button, Btn.Outline, BtnSize.Sm, buttonIcon, 0, false, buttonTip);
        if (Panels.Count > 0) Panels.Peek().Headed = true;
        Reserve(new Vector2(Left, y), Width, h);
        return pressed;
    }

    /// <summary>The panel being laid has a head: its first row has a hairline over it too.</summary>
    public static bool PanelHeaded => Panels.Count > 0 && Panels.Peek().Headed;

    /// <summary>A panel's ground: #141418 at its top to #101013 at its foot (its rounded ends solid: ImGui can't round a gradient).</summary>
    public static void PanelGround(ImDrawListPtr dl, Vector2 min, Vector2 max, float r)
    {
        var h = max.Y - min.Y;
        var cap = MathF.Min(r, h / 2);
        dl.AddRectFilled(min, new Vector2(max.X, min.Y + cap * 2), Palette.U32(Palette.PanelTop), r, ImDrawFlags.RoundCornersTop);
        dl.AddRectFilled(new Vector2(min.X, max.Y - cap * 2), max, Palette.U32(Palette.PanelBottom), r, ImDrawFlags.RoundCornersBottom);
        if (max.Y - cap > min.Y + cap)
            dl.AddRectFilledMultiColor(new Vector2(min.X, min.Y + cap), new Vector2(max.X, max.Y - cap), Palette.U32(Palette.PanelTop), Palette.U32(Palette.PanelTop),
                Palette.U32(Palette.PanelBottom), Palette.U32(Palette.PanelBottom));
    }

    /// <summary>
    /// A panel's edge (`--panel-edge`): a hairline, lit ember along its top and down its left from the top-left corner (two
    /// fading 1px gradients: ImGui has no gradient border), and the concentric frame 4px inside (`.pn::after`).
    /// </summary>
    public static void PanelEdge(ImDrawListPtr dl, Vector2 min, Vector2 max, float r, bool quiet = false)
    {
        dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.PanelEdge), r, ImDrawFlags.None, 1f);
        var lit = Palette.WithAlpha(Palette.PanelLit, quiet ? 0.26f : 0.5f);
        var none = Palette.WithAlpha(Palette.PanelLit, 0);
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        var x1 = min.X + MathF.Max(r, w * 0.32f);
        var y1 = min.Y + MathF.Max(r, MathF.Min(h * 0.45f, S(120)));
        // The lit corner itself: the hairline's arc in the lit colour, so the light turns the corner instead of
        // stopping short of it (the two straight lines started inside the curve, which stayed the plain edge: the
        // corner looked cut off, the owner, 26 November 2026).
        if (r > 0.5f)
        {
            dl.PathArcTo(min + new Vector2(r), r - 0.5f, MathF.PI, MathF.PI * 1.5f, Math.Max(4, (int)r));
            dl.PathStroke(Palette.U32(lit), ImDrawFlags.None, 1f);
        }
        if (x1 > min.X + r) dl.AddRectFilledMultiColor(new Vector2(min.X + r, min.Y), new Vector2(x1, min.Y + 1), Palette.U32(lit), Palette.U32(none), Palette.U32(none), Palette.U32(lit));
        if (y1 > min.Y + r) dl.AddRectFilledMultiColor(new Vector2(min.X, min.Y + r), new Vector2(min.X + 1, y1), Palette.U32(lit), Palette.U32(lit), Palette.U32(none), Palette.U32(none));
        var k = S(4) + 0.5f;
        if (w > k * 4 && h > k * 4)
            dl.AddRect(min + new Vector2(k), max - new Vector2(k), Palette.U32(quiet ? Palette.FrameQuiet : Palette.Frame), MathF.Max(0, r - S(5)), ImDrawFlags.None, 1f);
    }

    /// <summary>The panel being laid: its top-left, width and corners (for rows that go edge to edge).</summary>
    public static (Vector2 Start, float Width, float Radius) Panel => Panels.Count > 0 ? (Panels.Peek().Start, Panels.Peek().Width, Panels.Peek().Radius) : (At, Width, 0);

    public static void EndPanel()
    {
        PopBox();
        var p = Panels.Pop();
        var bottom = ImGui.GetCursorScreenPos().Y + p.PadBottom;
        var h = bottom - p.Start.Y;
        Heights[p.Id] = h;
        var dl = Dl;
        var max = p.Start + new Vector2(p.Width, h);
        if (p.Lit) PanelEdge(dl, p.Start, max, p.Radius);
        else if (p.Edge.W > 0) dl.AddRect(p.Start + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(p.Edge), p.Radius, ImDrawFlags.None, 1f);
        Reserve(p.Start, p.Width, h);
    }

    /// <summary>A row inside a slab pointed at: a fill edge to edge, rounded where it meets the slab's corners.</summary>
    public static void RowFill(Vector2 min, Vector2 max, Vector4 color, bool first, bool last)
    {
        var (start, w, r) = Panel;
        var flags = (first ? ImDrawFlags.RoundCornersTop : 0) | (last ? ImDrawFlags.RoundCornersBottom : 0);
        if (flags == 0) flags = ImDrawFlags.RoundCornersNone;
        var a = new Vector2(start.X + 1, min.Y + (first ? 1 : 0));
        var b = new Vector2(start.X + w - 1, max.Y - (last ? 1 : 0));
        Dl.AddRectFilled(a, b, Palette.U32(color), r - 1, flags);
    }

    /// <summary>A hairline across a width.</summary>
    public static void Rule(ImDrawListPtr dl, float x0, float x1, float y, Vector4? color = null) =>
        dl.AddLine(new Vector2(x0, MathF.Round(y) + 0.5f), new Vector2(x1, MathF.Round(y) + 0.5f), Palette.U32(color ?? Palette.Line), 1f);
}
