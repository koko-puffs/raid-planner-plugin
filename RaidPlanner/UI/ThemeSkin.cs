using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// A meter window's colours in a theme (the opened-up window since 0.7.2, the normal meter since 0.7.3, beside
/// the compact meter's own drawing): Ready Check's the kit's; a theme's its rows' words, its accent, good
/// and bad, its divider and chips, its panels a shade off its ground.
/// </summary>
internal readonly record struct ThemeInk(
    Vector4 Panel,
    Vector4 PanelEdge,
    Vector4 Rule,
    Vector4 StrongRule,
    Vector4 Strong,
    Vector4 Text,
    Vector4 Muted,
    Vector4 Faint,
    Vector4 Hover,
    Vector4 Accent,
    Vector4 Bad,
    Vector4 BadWash,
    Vector4 Good,
    Vector4 Warn,
    Vector4 Chip,
    float PanelRadius,
    float RowRadius,
    float ChipRadius)
{
    public static ThemeInk Of(MeterTheme t)
    {
        if (t.Id == MeterThemeId.ReadyCheck)
            return new ThemeInk(Palette.Slab, Palette.Line, Palette.Line, Palette.Line3, Palette.White, Palette.Text, Palette.Muted, Palette.Faint, Palette.Hover, Palette.Ember,
                Palette.Danger, Palette.DangerWash, Palette.Ready, Palette.Warn, Palette.Line2, Palette.RadiusLg, Palette.RadiusSm, 6);
        var ground = t.Win.GradBottom ?? t.Win.Bg;
        var panel = Rgb.Mix(ground, t.Row.Text, t.Light ? 0.035f : 0.045f) with { W = 1 };
        return new ThemeInk(panel, t.Row.PlateEdge ?? t.Divider, t.Divider, Rgb.A(t.Row.Muted, 0.45f), t.Row.Num, t.Row.Text, t.Row.Muted, t.Row.Faint, t.HoverWash,
            t.Accent, t.Bad, Rgb.A(t.Bad, 0.12f), t.Good, t.Lb, t.ChipLine, MathF.Min(t.Win.Radius, 10), t.Row.Radius, MathF.Max(t.ChipRadius, 0));
    }
}

/// <summary>
/// The normal meter's parts in its theme (since 0.7.3), as the compact meter draws its own: chips (a
/// dropdown, a tag, a button), the lock, tabs with the accent under the one you're on, segments, a
/// switch, a menu's rows, the heads in capitals. Every word in the theme's faces through
/// <see cref="PlateArt"/> (measured, centred by its ink). Ready Check's accent bar is the kit's ember
/// cast gradient; every other part is the theme's tokens, Ready Check's included.
/// </summary>
internal static class ThemeSkin
{
    /// <summary>A chip's corners (screen px): the theme's, a little rounder, at most 10.</summary>
    public static float ChipRound(ThemeInk ink) => S(MathF.Min(ink.ChipRadius + 2, 10));

    /// <summary>A dropdown chip's width: 8 in, its words, the gap, the chevron, 8 (<see cref="Dropdown"/>).</summary>
    public static float DropWidth(string label, ThemeFont f, float chevron) => Model.Dropdown.Width(PlateArt.Width(label, f), chevron, S(8), S(1));

    /// <summary>A dropdown in the theme's chip ("Pull 12 ⌄", "Damage ⌄"): its hairline (Mochi's fill), its words, the gap, the chevron. Returns pressed.</summary>
    public static bool DropButton(ImDrawListPtr dl, MeterTheme t, ThemeInk ink, string id, Vector2 pos, float width, float h, string label, ThemeFont f)
    {
        var size = new Vector2(width, h);
        var (clicked, hovered, _) = Kit.Hit(id, pos, size);
        var r = ChipRound(ink);
        if (t.ChipFill is { } fill) dl.AddRectFilled(pos, pos + size, Palette.U32(fill), r);
        if (hovered) dl.AddRectFilled(pos, pos + size, Palette.U32(ink.Hover), r);
        dl.AddRect(pos + new Vector2(0.5f), pos + size - new Vector2(0.5f), Palette.U32(ink.Chip), r, ImDrawFlags.None, 1f);
        var chev = S(13);
        var lw = PlateArt.Line(dl, new Vector2(pos.X + S(8), pos.Y), label, f, ink.Text, h / S(1));
        Icons.Draw(dl, Icon.ChevronDown, new Vector2(pos.X + Model.Dropdown.ChevronAt(lw, S(8), S(1)), pos.Y + (h - chev) / 2), chev, ink.Faint);
        Kit.FocusRing(pos, pos + size, r);
        return clicked;
    }

    /// <summary>A square icon button (the lock, the fold's chevron): quiet, the hover wash pointed at, the accent's wash when on.</summary>
    public static bool IconButton(ImDrawListPtr dl, ThemeInk ink, string id, Vector2 pos, float s, Icon icon, bool on, string tip)
    {
        var (clicked, hovered, _) = Kit.Hit(id, pos, new Vector2(s));
        var r = ChipRound(ink);
        if (on) dl.AddRectFilled(pos, pos + new Vector2(s), Palette.U32(Rgb.A(ink.Accent, 0.14f)), r);
        else if (hovered) dl.AddRectFilled(pos, pos + new Vector2(s), Palette.U32(ink.Hover), r);
        var i = s * 14 / 24;
        Icons.Draw(dl, icon, pos + new Vector2((s - i) / 2), i, on ? ink.Accent : hovered ? ink.Strong : ink.Muted);
        if (hovered) Kit.Tooltip(tip);
        Kit.FocusRing(pos, pos + new Vector2(s), r);
        return clicked;
    }

    /// <summary>A tag's width in the theme's chip (Readout's in brackets, no chip).</summary>
    public static float TagWidth(MeterTheme t, string word, ThemeFont f) => t.Id == MeterThemeId.Readout ? PlateArt.Width(Bracketed(word), f) : PlateArt.Width(word, f) + S(10);

    /// <summary>A tag ("Preview", "Wipe", "Clear", "Now") in the theme's chip, its words in <paramref name="color"/>, on a line <paramref name="h"/> tall from its top. Returns its width.</summary>
    public static float Tag(ImDrawListPtr dl, MeterTheme t, ThemeInk ink, Vector2 pos, float h, string word, ThemeFont f, Vector4 color)
    {
        if (t.Id == MeterThemeId.Readout) return PlateArt.Line(dl, pos, Bracketed(word), f, color, h / S(1));
        var w = PlateArt.Width(word, f) + S(10);
        var a = new Vector2(pos.X, pos.Y + (h - S(16)) / 2);
        var b = a + new Vector2(w, S(16));
        var r = S(t.ChipRadius);
        if (t.ChipFill is { } fill) dl.AddRectFilled(a, b, Palette.U32(fill), r);
        else dl.AddRect(a + new Vector2(0.5f), b - new Vector2(0.5f), Palette.U32(ink.Chip), r, ImDrawFlags.None, 1f);
        PlateArt.Line(dl, new Vector2(a.X + S(5), a.Y), word, f, color, 16);
        return w;
    }

    private static readonly Dictionary<string, string> BracketsOf = new();

    /// <summary>Readout's tags: "[WIPE]", made once a word.</summary>
    private static string Bracketed(string word) => BracketsOf.TryGetValue(word, out var b) ? b : BracketsOf[word] = "[" + Txt.Caps(word) + "]";

    /// <summary>A tab's width in the theme.</summary>
    public static float TabWidth(string label, ThemeFont f, float pad) => S(pad) * 2 + PlateArt.Width(label, f);

    /// <summary>
    /// Tabs in the theme: its words, the one you're on in its strongest ink with its accent under it (Ready
    /// Check's the ember cast gradient), pointed at on its hover wash. Returns the one picked.
    /// </summary>
    public static int Tabs(ImDrawListPtr dl, MeterTheme t, ThemeInk ink, string id, Vector2 at, float h, IReadOnlyList<string> labels, int selected, ThemeFont f, float pad)
    {
        var x = at.X;
        var result = selected;
        for (var i = 0; i < labels.Count; i++)
        {
            var w = TabWidth(labels[i], f, pad);
            var p = new Vector2(x, at.Y);
            var (clicked, hovered, _) = Kit.Hit(id + i, p, new Vector2(w, h));
            if (clicked) result = i;
            var on = i == selected;
            if (hovered && !on) dl.AddRectFilled(p + new Vector2(0, S(3)), p + new Vector2(w, h - S(3)), Palette.U32(ink.Hover), S(ink.RowRadius));
            PlateArt.Line(dl, new Vector2(p.X + S(pad), p.Y), labels[i], f, on ? ink.Strong : hovered ? ink.Text : ink.Muted, h / S(1));
            if (on)
            {
                var a = new Vector2(p.X + S(pad), p.Y + h - S(2));
                var b = new Vector2(p.X + w - S(pad), p.Y + h);
                if (t.Id == MeterThemeId.ReadyCheck) Kit.CastGradient(dl, a, b);
                else dl.AddRectFilled(a, b, Palette.U32(ink.Accent), S(1));
            }
            x += w + S(2);
        }
        return result;
    }

    /// <summary>The tabs' width.</summary>
    public static float TabsWidth(IReadOnlyList<string> labels, ThemeFont f, float pad)
    {
        var w = S(2) * (labels.Count - 1);
        foreach (var l in labels) w += TabWidth(l, f, pad);
        return w;
    }

    public static float SegmentsWidth(IReadOnlyList<string> items, ThemeFont f)
    {
        var widest = 0f;
        foreach (var s in items) widest = MathF.Max(widest, PlateArt.Width(s, f));
        return S(3) * 2 + S(2) * (items.Count - 1) + items.Count * (widest + S(9) * 2) + 2;
    }

    /// <summary>Segments in the theme: its chip's hairline round them, the one picked on its accent's wash in its accent.</summary>
    public static int Segments(ImDrawListPtr dl, MeterTheme t, ThemeInk ink, string id, Vector2 pos, float h, IReadOnlyList<string> items, int selected, ThemeFont f)
    {
        var total = SegmentsWidth(items, f);
        var r = ChipRound(ink);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(total, h) - new Vector2(0.5f), Palette.U32(ink.Chip), r, ImDrawFlags.None, 1f);
        var itemW = (total - 2 - S(3) * 2 - S(2) * (items.Count - 1)) / items.Count;
        var x = pos.X + 1 + S(3);
        var result = selected;
        for (var i = 0; i < items.Count; i++)
        {
            var p = new Vector2(x, pos.Y + 1 + S(3));
            var size = new Vector2(itemW, h - 2 - S(6));
            var on = i == selected;
            var (clicked, hovered, _) = Kit.Hit(id + i, p, size);
            if (clicked) result = i;
            if (on) dl.AddRectFilled(p, p + size, Palette.U32(Rgb.A(ink.Accent, t.Light ? 0.16f : 0.2f)), MathF.Max(0, r - S(3)));
            else if (hovered) dl.AddRectFilled(p, p + size, Palette.U32(ink.Hover), MathF.Max(0, r - S(3)));
            var tw = PlateArt.Width(items[i], f);
            PlateArt.Line(dl, new Vector2(p.X + (size.X - tw) / 2, p.Y), items[i], f, on ? ink.Accent : hovered ? ink.Text : ink.Muted, size.Y / S(1));
            x += itemW + S(2);
        }
        return result;
    }

    /// <summary>A switch in the theme (34×20): a track of its words' ink, its accent when on; its knob the panel's. Returns flipped.</summary>
    public static bool Switch(ImDrawListPtr dl, ThemeInk ink, string id, Vector2 pos, bool on)
    {
        var size = Kit.SwitchSize;
        var (clicked, hovered, _) = Kit.Hit(id, pos, size);
        var r = size.Y / 2;
        dl.AddRectFilled(pos, pos + size, Palette.U32(on ? ink.Accent : Rgb.A(ink.Text, hovered ? 0.26f : 0.18f)), r);
        var knob = pos + new Vector2(on ? size.X - S(10) : S(10), size.Y / 2);
        dl.AddCircleFilled(knob, S(7), Palette.U32(on ? ink.Panel : Rgb.Mix(ink.Panel, ink.Strong, 0.85f) with { W = 1 }), 24);
        Kit.FocusRing(pos, pos + size, r);
        return clicked;
    }

    /// <summary>A menu's row in the theme (34 tall): a mark's slot, its words (and a quieter line after them), and room kept on the right. Returns pressed and its box.</summary>
    public static (bool Clicked, Vector2 Min, Vector2 Max) MenuRow(ImDrawListPtr dl, ThemeInk ink, string id, float w, string name, string? sub, ThemeFont f, ThemeFont subFont,
        float reserve)
    {
        var at = ImGui.GetCursorScreenPos();
        var rh = S(34);
        var min = at;
        var max = at + new Vector2(w, rh);
        var (clicked, hovered, _) = Kit.Hit(id, min, max - min);
        if (hovered) dl.AddRectFilled(min, max, Palette.U32(ink.Hover), S(ink.RowRadius));
        var x = min.X + S(8.8f) + S(16) + S(8);
        var room = max.X - S(8.8f) - reserve - x;
        var nw = PlateArt.Line(dl, new Vector2(x, min.Y), PlateArt.Fit(name, f, room), f, ink.Strong, 34);
        if (sub != null && room - nw > S(30)) PlateArt.Line(dl, new Vector2(x + nw + S(8), min.Y), PlateArt.Fit(sub, subFont, room - nw - S(8)), subFont, ink.Muted, 34);
        ImGui.SetCursorScreenPos(new Vector2(at.X, max.Y));
        ImGui.Dummy(Vector2.Zero);
        return (clicked, min, max);
    }

    /// <summary>A menu row's mark's slot (16 square, 8.8 in) and its right edge for what's kept there.</summary>
    public static (Vector2 Slot, float Right) MenuParts(Vector2 min, Vector2 max) => (new Vector2(min.X + S(8.8f), min.Y + (max.Y - min.Y - S(16)) / 2), max.X - S(8.8f));

    /// <summary>A hairline between a menu's parts.</summary>
    public static void MenuRule(ImDrawListPtr dl, ThemeInk ink, float w)
    {
        var at = ImGui.GetCursorScreenPos();
        Kit.Rule(dl, at.X + S(4), at.X + w - S(4), at.Y + S(4), ink.Rule);
        ImGui.SetCursorScreenPos(new Vector2(at.X, at.Y + S(9)));
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>A menu's eyebrow in the theme's heads (capitals, faint).</summary>
    public static void MenuEyebrow(ImDrawListPtr dl, ThemeInk ink, float w, string text, ThemeFont f)
    {
        var at = ImGui.GetCursorScreenPos();
        PlateArt.Line(dl, new Vector2(at.X + S(8.8f), at.Y), PlateArt.Fit(text, f, w - S(17.6f)), f, ink.Faint, 26);
        ImGui.SetCursorScreenPos(new Vector2(at.X, at.Y + S(26)));
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>An event's mark's colour in the theme: its bad, good, accent, or its muted ink.</summary>
    public static Vector4 Tone(MeterTheme t, ThemeInk ink, EventTone tone) => tone switch
    {
        EventTone.Bad => t.Bad,
        EventTone.Good => t.Good,
        EventTone.Ember => t.Accent,
        _ => ink.Muted,
    };
}
