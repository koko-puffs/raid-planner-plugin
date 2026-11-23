using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Settings' own parts (0.8.0): a pane of the window that scrolls by itself (the sidebar, a page), a page's icon,
/// a section's eyebrow over its slab, the dashed "N more" row, a status tag, and a box with an icon, its words and
/// one button (Run the setup guide, Arrange windows).
/// </summary>
public static class SettingsParts
{
    // ---------------------------------------------------------------------------------------------
    // Panes.

    /// <summary>
    /// Starts a part of the window at <paramref name="pos"/> of <paramref name="size"/> that scrolls by itself (an ImGui
    /// child), its rows laid <paramref name="padX"/> in from its sides and <paramref name="padTop"/> down. Pair with <see cref="EndPane"/>.
    /// </summary>
    public static void BeginPane(string id, Vector2 pos, Vector2 size, float padX, float padTop, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.BeginChild(id, Vector2.Max(size, new Vector2(1)), false, flags);
        Frame.Open.Push(false);
        var at = ImGui.GetCursorScreenPos();
        Kit.PushBox(at.X + S(padX), ImGui.GetContentRegionAvail().X - S(padX) * 2);
        Kit.Reserve(at, 1, S(padTop));
    }

    public static void EndPane(float padBottom)
    {
        Kit.Gap(padBottom);
        Kit.PopBox();
        if (Frame.Open.Count > 0) Frame.Open.Pop();
        ImGui.EndChild();
    }

    // ---------------------------------------------------------------------------------------------
    // Icons the kit hasn't got (Lucide's, drawn as the kit draws its own).

    private static readonly string[] ChartPaths = { "M3 3v18h18", "M7 16v-4", "M12 16V7", "M17 16v-7" };

    private static readonly string[] WrenchPaths =
    {
        "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z",
    };

    private static readonly string[] UserPaths = { "M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2", "M8 7a4 4 0 1 0 8 0a4 4 0 1 0 -8 0" };

    private static readonly string[] TrendPaths = { "M22 7 13.5 15.5 8.5 10.5 2 17", "M16 7h6v6" };

    private static readonly string[] ChevronUpPaths = { "m18 15-6-6-6 6" };

    private static readonly Dictionary<string[], List<SvgPath.Sub>> Parsed = new();

    /// <summary>Lucide paths (24-unit grid) drawn stroked, <paramref name="size"/> px square (already scaled), with round caps.</summary>
    private static void Paths(ImDrawListPtr dl, string[] paths, Vector2 pos, float size, Vector4 color, float stroke = 1.75f)
    {
        if (!Parsed.TryGetValue(paths, out var subs))
        {
            subs = new List<SvgPath.Sub>();
            foreach (var d in paths) subs.AddRange(SvgPath.Parse(d));
            Parsed[paths] = subs;
        }
        var k = size / 24f;
        var col = Palette.U32(color);
        var thickness = stroke * k;
        Span<Vector2> buffer = stackalloc Vector2[256];
        foreach (var sub in subs)
        {
            var n = Math.Min(sub.Points.Count, buffer.Length);
            if (n == 0) continue;
            for (var i = 0; i < n; i++) buffer[i] = pos + sub.Points[i] * k;
            if (n == 1)
            {
                dl.AddCircleFilled(buffer[0], thickness * 0.75f, col, 8);
                continue;
            }
            dl.AddPolyline(ref buffer[0], n, col, sub.Closed ? ImDrawFlags.Closed : ImDrawFlags.None, thickness);
            if (!sub.Closed)
            {
                dl.AddCircleFilled(buffer[0], thickness * 0.5f, col, 6);
                dl.AddCircleFilled(buffer[n - 1], thickness * 0.5f, col, 6);
            }
        }
    }

    /// <summary>The recording mark: a ring with a dot in it.</summary>
    private static void RecMark(ImDrawListPtr dl, Vector2 pos, float size, Vector4 color)
    {
        var c = pos + new Vector2(size / 2);
        var k = size / 24f;
        dl.AddCircle(c, 9 * k, Palette.U32(color), 24, 1.75f * k);
        dl.AddCircleFilled(c, 3.5f * k, Palette.U32(color), 16);
    }

    /// <summary>A page's icon in the sidebar and the page picker.</summary>
    public static void PageIcon(ImDrawListPtr dl, SettingsTab page, Vector2 pos, float size, Vector4 color)
    {
        switch (page)
        {
            case SettingsTab.Features:
                Icons.Draw(dl, Icon.Sliders, pos, size, color);
                break;
            case SettingsTab.Recording:
                RecMark(dl, pos, size, color);
                break;
            case SettingsTab.Meter:
                Paths(dl, ChartPaths, pos, size, color);
                break;
            case SettingsTab.Plans:
                Icons.Draw(dl, Icon.Map, pos, size, color);
                break;
            case SettingsTab.Cooldowns:
                Icons.Draw(dl, Icon.Shield, pos, size, color);
                break;
            case SettingsTab.Windows:
                Icons.Draw(dl, Icon.Move, pos, size, color);
                break;
            case SettingsTab.Account:
                Paths(dl, UserPaths, pos, size, color);
                break;
            default:
                Paths(dl, WrenchPaths, pos, size, color);
                break;
        }
    }

    /// <summary>A main part's icon on its card (Settings → Features).</summary>
    public static void FeatureIcon(ImDrawListPtr dl, Feature f, Vector2 pos, float size, Vector4 color)
    {
        switch (f)
        {
            case Feature.Record:
                RecMark(dl, pos, size, color);
                break;
            case Feature.Upload:
                Icons.Draw(dl, Icon.Upload, pos, size, color);
                break;
            case Feature.Meter:
                Paths(dl, ChartPaths, pos, size, color);
                break;
            case Feature.Plan:
                Icons.Draw(dl, Icon.Map, pos, size, color);
                break;
            case Feature.Cooldowns:
                Icons.Draw(dl, Icon.Shield, pos, size, color);
                break;
            default:
                Paths(dl, TrendPaths, pos, size, color);
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Sections.

    /// <summary>
    /// A section's head: its eyebrow (mono capitals, muted) at the left; at the right a faint hint and an optional link
    /// after it. Leaves 10px under it for the slab. True when the link is pressed.
    /// </summary>
    public static bool SectionHead(string id, string title, string? hint = null, string? link = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var lineH = S(18);
        var linkW = link != null ? Kit.LinkWidth(link, Icon.ChevronRight) : 0;
        var right = at.X + w - S(2);
        var pressed = false;
        if (link != null)
        {
            right -= linkW;
            pressed = Kit.Link(id, new Vector2(right, at.Y + (lineH - Txt.Natural(Face.SansMedium, 13)) / 2), link, Icon.ChevronRight);
            right -= S(12);
        }
        var eyebrowRoom = right - at.X - S(2);
        if (hint != null)
        {
            var hw = MathF.Min(Txt.Width(hint, Face.Sans, 12), MathF.Max(0, eyebrowRoom - S(120)));
            if (hw > S(20))
            {
                Txt.Line(dl, new Vector2(right - hw, at.Y), Txt.Fit(hint, Face.Sans, 12, hw), Face.Sans, 12, Palette.Faint, 18);
                eyebrowRoom -= hw + S(12);
            }
        }
        Kit.Eyebrow(dl, new Vector2(at.X + S(2), at.Y + (lineH - Kit.EyebrowH) / 2), title, null, eyebrowRoom, Palette.Muted);
        Kit.Reserve(at, w, lineH + S(8));
        return pressed;
    }

    // ---------------------------------------------------------------------------------------------
    // The more row.

    /// <summary>
    /// The row a page's rarely changed settings fold under: 44 tall, a dashed hairline with 14px corners, a chevron
    /// (down while closed, up while open) and the words naming what's inside, cut to fit. True when pressed.
    /// </summary>
    public static bool MoreRow(string id, string words, bool open)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var h = S(44);
        var dl = Kit.Dl;
        var (clicked, hovered, _) = Kit.Hit(id, at, new Vector2(w, h));
        var r = S(Palette.RadiusLg);
        if (hovered) dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.Hover), r);
        var outline = Dashes.RoundedRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), r, 6);
        var edge = Palette.U32(hovered ? Palette.Line3 : Palette.Line2);
        foreach (var dash in Dashes.Along(outline, S(8), 0.55f))
            if (dash.Length > 1)
                dl.AddPolyline(ref dash[0], dash.Length, edge, ImDrawFlags.None, 1f);
        var ink = hovered ? Palette.White : Palette.Muted;
        var icon = S(14);
        var ix = at.X + S(14);
        var iy = at.Y + (h - icon) / 2;
        if (open) Paths(dl, ChevronUpPaths, new Vector2(ix, iy), icon, ink);
        else Icons.Draw(dl, Icon.ChevronDown, new Vector2(ix, iy), icon, ink);
        var tx = ix + icon + S(8);
        Txt.Line(dl, new Vector2(tx, at.Y), Txt.Fit(words, Face.SansMedium, 13, at.X + w - S(14) - tx), Face.SansMedium, 13, ink, h / S(1));
        Kit.FocusRing(at, at + new Vector2(w, h), r);
        Kit.Reserve(at, w, h);
        return clicked;
    }

    // ---------------------------------------------------------------------------------------------
    // Status tags.

    private static (Vector4 Fill, Vector4 Edge, Vector4 Ink) StatusColors(StatusKind kind) => kind switch
    {
        StatusKind.Ready => (Palette.ReadyWash, Palette.ReadyLine, Palette.Ready),
        StatusKind.Busy => (Palette.EmberWash, Palette.EmberLine, Palette.EmberHi),
        StatusKind.Problem => (Palette.WarnWash, Palette.WarnLine, Palette.Warn),
        StatusKind.Off => (Vector4.Zero, Palette.Line2, Palette.Faint),
        _ => (Vector4.Zero, Palette.Line3, Palette.Text),
    };

    public static float StatusTagWidth(string text) => S(7) * 2 + Txt.SpacedWidth(Txt.Caps(text), Face.Mono, 10, 0.05f);

    /// <summary>A status tag (mono capitals 10px, 20 tall, 5px corners) in its kind's colours, cut to <paramref name="maxWidth"/>. Returns its width.</summary>
    public static float StatusTag(ImDrawListPtr dl, Vector2 pos, string text, StatusKind kind, float maxWidth)
    {
        var (fill, edge, ink) = StatusColors(kind);
        var caps = Txt.Caps(text);
        var w = MathF.Min(StatusTagWidth(text), maxWidth);
        if (w < StatusTagWidth(text)) caps = Kit.FitSpaced(caps, Face.Mono, 10, 0.05f, w - S(14));
        var h = S(20);
        var r = S(5);
        if (fill.W > 0) dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(fill), r);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);
        Txt.Spaced(dl, new Vector2(pos.X + S(7), pos.Y + (h - Txt.Natural(Face.Mono, 10)) / 2), caps, Face.Mono, 10, ink, 0.05f);
        return w;
    }

    // ---------------------------------------------------------------------------------------------
    // A box with one thing to do.

    private static readonly Run[] BoxRun = new Run[1];

    /// <summary>
    /// A well-coloured box with 14px corners and a hairline: an icon, its words (13px muted, wrapped) and one dense
    /// outlined button at the right (under the words when there isn't room beside them). True when pressed.
    /// </summary>
    public static bool Box(string id, Icon icon, string words, string button, Icon? buttonIcon = null, bool disabled = false, string? tip = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var padX = S(14);
        var padY = S(12);
        var iconPx = S(16);
        var bw = Kit.ButtonWidth(button, BtnSize.Sm, buttonIcon);
        var textX = at.X + padX + iconPx + S(12);
        var beside = w - padX * 2 - iconPx - S(12) - bw - S(12) >= S(180);
        var textW = beside ? at.X + w - padX - bw - S(12) - textX : at.X + w - padX - textX;
        BoxRun[0] = new Run(words, Face.Sans, 13, Palette.Muted);
        var th = Txt.Flow(Vector2.Zero, textW, BoxRun, 19.5f, out _, draw: false);
        var bh = S(HSm);
        var h = beside ? padY * 2 + MathF.Max(th, bh) : padY * 2 + th + S(10) + bh;
        var r = S(Palette.RadiusLg);
        dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.Well), r);
        dl.AddRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.Line), r, ImDrawFlags.None, 1f);
        var textTop = beside ? at.Y + (h - th) / 2 : at.Y + padY;
        Icons.Draw(dl, icon, new Vector2(at.X + padX, textTop + (S(19.5f) - iconPx) / 2), iconPx, Palette.Muted);
        BoxRun[0] = new Run(words, Face.Sans, 13, Palette.Muted);
        Txt.Flow(new Vector2(textX, textTop), textW, BoxRun, 19.5f, out _);
        var bp = beside ? new Vector2(at.X + w - padX - bw, at.Y + (h - bh) / 2) : new Vector2(textX, at.Y + padY + th + S(10));
        var pressed = Kit.Button(id, bp, button, Btn.Outline, BtnSize.Sm, buttonIcon, 0, disabled, tip);
        Kit.Reserve(at, w, h);
        return pressed;
    }
}
