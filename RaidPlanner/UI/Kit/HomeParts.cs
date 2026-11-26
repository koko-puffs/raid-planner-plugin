using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>A soft tag's colour (the setup guide's and Home's): green for what's on, yellow for what needs something first.</summary>
public enum Tone
{
    Ready,
    Warn,
}

/// <summary>
/// The parts the setup guide, Home and the Pulls tab share (0.8.0, the mock-ups' plug-setup and plug-main): a main
/// part's icon and its soft well, soft tags (ready green, warn yellow), Home's chips (a small dot, an icon, a name, in
/// a pill), the guide's progress (five segments with the step names under), the waymark squares numbering steps,
/// a dashed outline (the guide's picture of your screen), and a pull's row (number, length, when and where it ended,
/// its outcome, a tick once uploaded).
/// </summary>
public static partial class Kit
{
    /// <summary>A main part's icon, as the guide and Home's chips draw it.</summary>
    public static Icon FeatureIcon(Feature f) => f switch
    {
        Feature.Record => Icon.CircleDot,
        Feature.Upload => Icon.Upload,
        Feature.Meter => Icon.ChartColumn,
        Feature.Plan => Icon.Map,
        Feature.Cooldowns => Icon.Shield,
        _ => Icon.TrendingUp,
    };

    /// <summary>A square well with 9px corners, its fill and a hairline, the icon in it at about half its size.</summary>
    public static void SoftWell(ImDrawListPtr dl, Vector2 min, float size, Icon icon, Vector4 fill, Vector4 ink)
    {
        var s = S(size);
        var r = S(MathF.Min(9, size / 4));
        dl.AddRectFilled(min, min + new Vector2(s), Palette.U32(fill), r);
        dl.AddRect(min + new Vector2(0.5f), min + new Vector2(s) - new Vector2(0.5f), Palette.U32(Palette.Line), r, ImDrawFlags.None, 1f);
        var px = MathF.Round(size * 0.46f);
        Icons.Draw(dl, icon, min + new Vector2((s - S(px)) / 2), S(px), ink, 1.9f);
    }

    /// <summary>A feature's well: ember when it's on, dim when it's off, green on the guide's last step.</summary>
    public static void FeatureWell(ImDrawListPtr dl, Vector2 min, float size, Feature f, bool on, bool ready = false)
    {
        if (!on) SoftWell(dl, min, size, FeatureIcon(f), Palette.Well, Palette.Disabled);
        else if (ready) SoftWell(dl, min, size, FeatureIcon(f), Palette.Rgba(61, 220, 132, 0.1f), Palette.Ready);
        else SoftWell(dl, min, size, FeatureIcon(f), Palette.EmberWash, Palette.Ember);
    }

    // ---------------------------------------------------------------------------------------------
    // Soft tags.

    public static float ToneTagWidth(string text) => TagWidth(text, TagKind.Ready);

    /// <summary>A tag in a soft tone (the kit's tag: <see cref="TagKind.Ready"/> green, <see cref="TagKind.Warn"/> amber). Returns its width.</summary>
    public static float ToneTag(ImDrawListPtr dl, Vector2 pos, string text, Tone tone, float height = TagH) =>
        Tag(dl, pos, text, tone == Tone.Warn ? TagKind.Warn : TagKind.Ready, height);

    // ---------------------------------------------------------------------------------------------
    // Chips.

    public static float ChipWidth(string label, Icon? icon) =>
        S(10) * 2 + S(6) + S(6) + (icon != null ? S(13) + S(6) : 0) + Txt.Width(label, Face.SansMedium, 12);

    public static float ChipH => S(HSm);

    /// <summary>
    /// Home's chip (the site's `.chip`): 32 tall with 8px corners on the well with the controls' line, a 6px dot (green on,
    /// faint off, yellow waiting), the part's icon and its name (fainter when off). Pointed at, a lighter line, white words
    /// and the tip. Returns true when pressed.
    /// </summary>
    public static bool Chip(string id, Vector2 pos, string label, Vector4 dot, bool on, Icon? icon = null, string? tip = null)
    {
        var w = ChipWidth(label, icon);
        var h = ChipH;
        var (clicked, hovered, _) = Hit(id, pos, new Vector2(w, h));
        if (tip != null && hovered) Tooltip(tip);
        var dl = Dl;
        var r = S(Palette.RadiusSm);
        dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(Palette.Well), r);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(hovered ? Palette.CtlLineHover : Palette.CtlLine), r, ImDrawFlags.None, 1f);
        var x = pos.X + S(10);
        dl.AddCircleFilled(new Vector2(x + S(3), pos.Y + h / 2), S(3), Palette.U32(dot), 12);
        x += S(6) + S(6);
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(x, pos.Y + (h - S(13)) / 2), S(13), Palette.Muted, 1.9f);
            x += S(13) + S(6);
        }
        Txt.Line(dl, new Vector2(x, pos.Y), label, Face.SansMedium, 12, hovered ? Palette.White : on ? Palette.Text : Palette.Faint, h / S(1));
        FocusRing(pos, pos + new Vector2(w, h), r);
        return clicked;
    }

    // ---------------------------------------------------------------------------------------------
    // The guide's progress, waymarks, dashed outlines.

    /// <summary>
    /// The setup guide's progress: a segment a step (4px, 5px apart), ember up to the one you're on; the steps' names
    /// under them, the one you're on in white. Laid at the cursor.
    /// </summary>
    public static void StepsBar(IReadOnlyList<string> names, int at)
    {
        var p = At;
        var w = Width;
        var dl = Dl;
        var gap = S(5);
        var segW = (w - gap * (names.Count - 1)) / names.Count;
        for (var i = 0; i < names.Count; i++)
        {
            var x = p.X + (segW + gap) * i;
            dl.AddRectFilled(new Vector2(x, p.Y), new Vector2(x + segW, p.Y + S(4)), Palette.U32(i <= at ? Palette.Ember : Palette.Rgba(255, 255, 255, 0.1f)), S(2));
            var on = i == at;
            Txt.Line(dl, new Vector2(x, p.Y + S(4) + S(7)), Txt.Fit(names[i], on ? Face.SansMedium : Face.Sans, 12, segW), on ? Face.SansMedium : Face.Sans, 12,
                on ? Palette.White : Palette.Faint, 16);
        }
        Reserve(p, w, S(4) + S(7) + S(16));
    }

    /// <summary>A step's number on a waymark square (1 red, 2 yellow, 3 blue, 4 purple), dark mono figures; 24px.</summary>
    public static void Waymark(ImDrawListPtr dl, Vector2 min, int n, float size = 24)
    {
        var s = S(size);
        dl.AddRectFilled(min, min + new Vector2(s), Palette.U32(Palette.Waymarks[Math.Clamp(n - 1, 0, Palette.Waymarks.Length - 1)]), S(6));
        var t = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ts = Txt.Measure(t, Face.MonoSemibold, 12);
        Txt.Draw(dl, min + (new Vector2(s) - ts) / 2, t, Face.MonoSemibold, 12, Palette.OnLight);
    }

    /// <summary>A dashed outline (ImGui has none): dashes along each side, <paramref name="dash"/> long with <paramref name="gap"/> between (CSS px).</summary>
    public static void DashedRect(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector4 color, float dash = 5, float gap = 4, float thickness = 1.5f)
    {
        var col = Palette.U32(color);
        var d = S(dash);
        var g = S(gap);
        var t = MathF.Max(1, S(thickness));
        void Side(Vector2 a, Vector2 b)
        {
            var len = Vector2.Distance(a, b);
            if (len < 1) return;
            var dir = (b - a) / len;
            for (var s = 0f; s < len; s += d + g)
                dl.AddLine(a + dir * s, a + dir * MathF.Min(len, s + d), col, t);
        }
        Side(min, new Vector2(max.X, min.Y));
        Side(new Vector2(max.X, min.Y), max);
        Side(max, new Vector2(min.X, max.Y));
        Side(new Vector2(min.X, max.Y), min);
    }

    /// <summary>A box with a fill and a hairline edge, its corners <paramref name="radius"/> CSS px.</summary>
    public static void Box(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector4 fill, Vector4 edge, float radius = Palette.RadiusMd)
    {
        var r = S(radius);
        if (fill.W > 0) dl.AddRectFilled(min, max, Palette.U32(fill), r);
        if (edge.W > 0) dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);
    }

    // ---------------------------------------------------------------------------------------------
    // A pull's row.

    /// <summary>What a pull's row shows (Home's Tonight, the Pulls tab).</summary>
    public readonly record struct PullLine(string Number, string Length, string Sub, string Outcome, TagKind OutcomeKind, bool Uploaded, string? Waiting, bool Best);

    /// <summary>A pull's row's height.</summary>
    public static float PullRowH => S(RowTall);

    /// <summary>
    /// A pull's row (`pull_row`): its number in mono, its length in mono with a Best tag, how long ago and where it
    /// ended under it, the outcome tag, a ready tick once uploaded (else a faint word), a chevron; pointed at, a faint
    /// fill and the reticle. Laid at the cursor, the box's width. Returns true when pressed.
    /// </summary>
    public static bool PullListRow(string id, PullLine row, bool first)
    {
        var at = At;
        var w = Width;
        var h = PullRowH;
        var (clicked, hovered, _) = Hit(id, at, new Vector2(w, h));
        var dl = Dl;
        var min = at;
        var max = at + new Vector2(w, h);
        if (!first) Rule(dl, min.X, max.X, min.Y - 0.5f);
        if (hovered) dl.AddRectFilled(min, max, Palette.U32(Palette.Hover));
        Reticle(dl, id + "##ret", min, max, hovered);
        var x = min.X + S(20);
        Txt.Line(dl, new Vector2(x, min.Y), Txt.Fit(row.Number, Face.MonoSemibold, 13, S(34)), Face.MonoSemibold, 13, Palette.White, RowTall);
        x += S(34) + S(12);

        // From the right: the tick (or its word), the outcome. A row that opens a page has no chevron (the site's rule:
        // a chevron is for a row that opens in place).
        var right = max.X - S(20);
        if (row.Uploaded)
        {
            Icons.Draw(dl, Icon.Check, new Vector2(right - S(15), min.Y + (h - S(15)) / 2), S(15), Palette.Ready, 2.2f);
            if (Pointed(new Vector2(right - S(15), min.Y), new Vector2(right, max.Y))) Tooltip("On your Raid Codex");
            right -= S(15) + S(12);
        }
        else if (row.Waiting is { } waiting)
        {
            var ww = Txt.Width(waiting, Face.Sans, 12);
            Txt.Line(dl, new Vector2(right - ww, min.Y), waiting, Face.Sans, 12, Palette.Faint, RowTall);
            right -= ww + S(12);
        }
        var tw = TagWidth(row.Outcome, row.OutcomeKind);
        Tag(dl, new Vector2(right - tw, min.Y + (h - S(TagH)) / 2), row.Outcome, row.OutcomeKind);
        right -= tw + S(12);

        // Two lines, 2px apart (`.rp-two`): the length in mono with Best, and when and where it ended.
        var lineH = S(18);
        var subH = S(16);
        var top = min.Y + (h - lineH - subH - S(2)) / 2;
        var lw = Txt.Line(dl, new Vector2(x, top), row.Length, Face.MonoSemibold, 13, Palette.White, 18);
        if (row.Best && x + lw + S(8) + ToneTagWidth("Best") < right) ToneTagEmber(dl, new Vector2(x + lw + S(8), top + (lineH - S(TagH)) / 2), "Best");
        Txt.Line(dl, new Vector2(x, top + lineH + S(2)), Txt.Fit(row.Sub, Face.Sans, 12, MathF.Max(0, right - x)), Face.Sans, 12, Palette.Faint, 16);
        Reserve(at, w, h);
        return clicked;
    }

    /// <summary>An ember tag (the Best pull's), small.</summary>
    public static float ToneTagEmber(ImDrawListPtr dl, Vector2 pos, string text, float height = TagH) => Tag(dl, pos, text, TagKind.Ember, height);
}
