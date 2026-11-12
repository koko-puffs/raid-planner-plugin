using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>A piece of styled text in a paragraph: words in a face, size and colour, or a link.</summary>
public readonly record struct Run(string Text, Face Face, int Px, Vector4 Color, string? Link = null)
{
    /// <summary>A line as the site's `.small`: 13px, muted.</summary>
    public static Run Note(string text, Vector4? color = null) => new(text, Face.Sans, 13, color ?? Palette.Muted);

    /// <summary>A link as the site's `.lnk`: white 13px at 550, underlined in a faint line; ember-light when pointed at.</summary>
    public static Run LinkRun(string text, string link, int px = 13) => new(text, Face.SansMedium, px, Palette.White, link);
}

/// <summary>
/// Text as the browser lays it out: line boxes of a CSS line height with the glyphs centred in them
/// (half-leading), letter-spacing drawn glyph by glyph (eyebrows +0.09em, tags +0.05em), capitals
/// for the callout, ellipses, and paragraphs of mixed runs wrapped at word boundaries with links,
/// underlined as the site's are.
/// </summary>
public static class Txt
{
    public static Fonts Fonts { get; set; } = null!;

    /// <summary>Words already put in capitals (tags and labels are drawn every frame): kept, so a frame makes no strings.</summary>
    private static readonly Dictionary<string, string> CapsKept = new();

    /// <summary>The site's text-transform for the mono capitals (tags, eyebrows, labels): the windows' own words, in capitals on every client.</summary>
    public static string Caps(string text)
    {
        if (CapsKept.TryGetValue(text, out var caps)) return caps;
        caps = Format.Caps(text, false);
        if (CapsKept.Count > 512) CapsKept.Clear();
        return CapsKept[text] = caps;
    }

    /// <summary>The callout's capitals: as written on a Japanese client (Dalamud's font, the plan's own words), else in capitals.</summary>
    public static string CalloutCaps(string text) => Format.Caps(text, Fonts.Japanese);

    /// <summary>
    /// Whether a face is built and in the atlas (it's built after the plugin loads, and again when
    /// Dalamud's fonts change): what's measured before then is measured in Dalamud's font, so it isn't kept.
    /// </summary>
    public static bool Ready(Face face, int px) => Fonts.Handle(face, px).Available;

    /// <summary>
    /// The face text is drawn in: on a Japanese client the mono (Geist Mono, which has no kana or
    /// kanji) gives way to the sans (Dalamud's font there) for text with Japanese in it, as a duty's
    /// name in an eyebrow or a job's abbreviation in the meter.
    /// </summary>
    public static Face For(Face face, ReadOnlySpan<char> text)
    {
        if (face is not (Face.Mono or Face.MonoSemibold) || !Fonts.Japanese) return face;
        foreach (var c in text)
            if (c >= '⺀')
                return face == Face.Mono ? Face.Sans : Face.SansSemibold;
        return face;
    }

    public static Vector2 Measure(string text, Face face, int px)
    {
        using (Fonts.Push(For(face, text), px)) return ImGui.CalcTextSize(text);
    }

    public static float Width(string text, Face face, int px) => Measure(text, face, px).X;

    /// <summary>The font's own line (ImGui's font size): CSS "line-height: normal".</summary>
    public static float Natural(Face face, int px)
    {
        using (Fonts.Push(face, px)) return ImGui.GetFontSize();
    }

    /// <summary>The ascent, for lining up baselines of different sizes.</summary>
    public static float Ascent(Face face, int px)
    {
        using (Fonts.Push(face, px)) return ImGui.GetFont().Ascent;
    }

    /// <summary>Draws text with its glyph box's top at <paramref name="pos"/>; returns its width.</summary>
    public static float Draw(ImDrawListPtr dl, Vector2 pos, string text, Face face, int px, Vector4 color)
    {
        face = For(face, text);
        using (Fonts.Push(face, px))
        {
            dl.AddText(Snap(pos), Palette.U32(color), text);
            // Dalamud's font has no bold: a callout drawn in it (a Japanese client) is drawn twice, a pixel apart.
            if (face == Face.Callout && Fonts.Japanese) dl.AddText(Snap(pos) + new Vector2(1, 0), Palette.U32(color), text);
            return ImGui.CalcTextSize(text).X;
        }
    }

    /// <summary>Draws one line centred in a CSS line box <paramref name="lineCss"/> tall (0: the font's own line); returns its width.</summary>
    public static float Line(ImDrawListPtr dl, Vector2 lineTop, string text, Face face, int px, Vector4 color, float lineCss = 0)
    {
        face = For(face, text);
        using (Fonts.Push(face, px))
        {
            var size = ImGui.GetFontSize();
            var box = lineCss > 0 ? S(lineCss) : size;
            var p = Snap(lineTop + new Vector2(0, (box - size) / 2));
            dl.AddText(p, Palette.U32(color), text);
            if (face == Face.Callout && Fonts.Japanese) dl.AddText(p + new Vector2(1, 0), Palette.U32(color), text);
            return ImGui.CalcTextSize(text).X;
        }
    }

    /// <summary>Draws one line right-aligned at <paramref name="right"/>, centred in its line box; returns its width.</summary>
    public static float LineRight(ImDrawListPtr dl, float right, float lineTop, string text, Face face, int px, Vector4 color, float lineCss = 0)
    {
        var w = Width(text, face, px);
        Line(dl, new Vector2(right - w, lineTop), text, face, px, color, lineCss);
        return w;
    }

    /// <summary>The height of a CSS line box in screen pixels.</summary>
    public static float LineBox(Face face, int px, float lineCss) => lineCss > 0 ? S(lineCss) : Natural(face, px);

    /// <summary>
    /// Letter-spaced text (CSS letter-spacing, which also follows the last letter): eyebrows at
    /// .09em, tags at .05em, intents at .1em. Drawn glyph by glyph without making strings. Returns
    /// the width (the last letter's spacing left off, as it sits outside the words).
    /// </summary>
    public static float Spaced(ImDrawListPtr dl, Vector2 pos, string text, Face face, int px, Vector4 color, float em, bool draw = true)
    {
        using (Fonts.Push(For(face, text), px))
        {
            var spacing = S(px * em);
            var x = pos.X;
            var col = Palette.U32(color);
            var font = ImGui.GetFont();
            var size = ImGui.GetFontSize();
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text.AsSpan(i, 1);
                if (draw) dl.AddText(Snap(new Vector2(x, pos.Y)), col, ch);
                x += font.GetCharAdvance(text[i]) * size / font.FontSize + spacing;
            }
            return MathF.Max(0, x - pos.X - spacing);
        }
    }

    public static float SpacedWidth(string text, Face face, int px, float em) =>
        Spaced(default, Vector2.Zero, text, face, px, Vector4.Zero, em, draw: false);

    /// <summary>The text cut to fit a width, with an ellipsis.</summary>
    public static string Fit(string text, Face face, int px, float width)
    {
        using (Fonts.Push(For(face, text), px))
            return Format.Fit(text, s => ImGui.CalcTextSize(s).X, width);
    }

    /// <summary>Words wrapped to a width, at most <paramref name="maxLines"/> lines (the last cut with "…").</summary>
    public static List<string> Wrap(string text, Face face, int px, float width, int maxLines = int.MaxValue)
    {
        using (Fonts.Push(For(face, text), px))
            return Format.Wrap(text, s => ImGui.CalcTextSize(s).X, width, maxLines);
    }

    private readonly record struct Token(string Text, int Run, float Width, float Trailing);

    /// <summary>
    /// Lays out runs of text wrapped to <paramref name="width"/>, in line boxes <paramref name="lineCss"/>
    /// CSS pixels tall, from <paramref name="pos"/>. Links are underlined (a faint line 3px under the
    /// words), turn ember-light when pointed at, and are clickable: <paramref name="clicked"/> is
    /// the link pressed this frame. Leaves the cursor where it was. Returns the height.
    /// </summary>
    public static float Flow(Vector2 pos, float width, IReadOnlyList<Run> runs, float lineCss, out string? clicked, bool center = false, bool draw = true)
    {
        clicked = null;
        var tokens = new List<Token>();
        for (var r = 0; r < runs.Count; r++)
        {
            var run = runs[r];
            using (Fonts.Push(For(run.Face, run.Text), run.Px))
            {
                var space = ImGui.CalcTextSize(" ").X;
                var text = run.Text;
                var i = 0;
                while (i < text.Length)
                {
                    var j = i;
                    while (j < text.Length && text[j] != ' ') j++;
                    var word = text[i..j];
                    var spaces = 0;
                    while (j < text.Length && text[j] == ' ')
                    {
                        spaces++;
                        j++;
                    }
                    var ww = word.Length == 0 ? 0 : ImGui.CalcTextSize(word).X;
                    // A word wider than a whole line (Japanese or Chinese have no spaces) is broken where it fills it.
                    while (word.Length > 1 && ww > width)
                    {
                        var n = Format.Fitting(word, s => ImGui.CalcTextSize(s).X, width);
                        if (n >= word.Length) break;
                        var part = word[..n];
                        tokens.Add(new Token(part, r, ImGui.CalcTextSize(part).X, 0));
                        word = word[n..];
                        ww = ImGui.CalcTextSize(word).X;
                    }
                    tokens.Add(new Token(word, r, ww, spaces * space));
                    i = j;
                }
            }
        }

        // Break into lines.
        var lines = new List<(int From, int To, float Width)>();
        var from = 0;
        var x = 0f;
        var lineWidth = 0f;
        for (var t = 0; t < tokens.Count; t++)
        {
            var tok = tokens[t];
            if (t > from && x + tok.Width > width + 0.5f)
            {
                lines.Add((from, t, lineWidth));
                from = t;
                x = 0;
            }
            lineWidth = x + tok.Width;
            x += tok.Width + tok.Trailing;
        }
        if (from < tokens.Count) lines.Add((from, tokens.Count, lineWidth));

        var box = S(lineCss);
        if (!draw) return lines.Count * box;

        var dl = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        // The link under the mouse, found first: a link of several words lights up whole.
        string? hoveredLink = null;
        if (!Kit.Blocked && ImGui.IsWindowHovered())
        {
            var hy = pos.Y;
            foreach (var (a, b, w) in lines)
            {
                var hx = pos.X + (center ? (width - w) / 2 : 0);
                for (var t = a; t < b && hoveredLink == null; t++)
                {
                    var tok = tokens[t];
                    var link = runs[tok.Run].Link;
                    if (link != null && ImGui.IsMouseHoveringRect(new Vector2(hx, hy), new Vector2(hx + tok.Width + tok.Trailing, hy + box)))
                        hoveredLink = link;
                    hx += tok.Width + tok.Trailing;
                }
                if (hoveredLink != null) break;
                hy += box;
            }
        }
        var y = pos.Y;
        var linkIndex = 0;
        foreach (var (a, b, w) in lines)
        {
            var lx = pos.X + (center ? (width - w) / 2 : 0);
            for (var t = a; t < b; t++)
            {
                var tok = tokens[t];
                var run = runs[tok.Run];
                using (Fonts.Push(For(run.Face, run.Text), run.Px))
                {
                    var size = ImGui.GetFontSize();
                    var top = new Vector2(lx, y + (box - size) / 2);
                    var color = run.Color;
                    if (run.Link != null)
                    {
                        // The space after a word belongs to the link when the next word is the same link's.
                        var joined = t < b - 1 && runs[tokens[t + 1].Run].Link == run.Link;
                        var min = new Vector2(lx, y);
                        var max = new Vector2(lx + tok.Width + (joined ? tok.Trailing : 0), y + box);
                        ImGui.SetCursorScreenPos(min);
                        var hit = ImGui.InvisibleButton($"##link{run.Link}{linkIndex++}", Vector2.Max(max - min, new Vector2(1)));
                        if (hit && !Kit.Blocked) clicked = run.Link;
                        if (ImGui.IsItemHovered() && !Kit.Blocked) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        var hot = run.Link == hoveredLink;
                        if (hot) color = Palette.EmberHi;
                        // text-decoration: underline, var(--line-3), 3px under the baseline (ember-light when pointed at).
                        var under = MathF.Round(top.Y + ImGui.GetFont().Ascent + S(3)) + 0.5f;
                        dl.AddLine(new Vector2(lx, under), new Vector2(max.X, under), Palette.U32(hot ? Palette.EmberHi : Palette.Line3), 1f);
                    }
                    if (tok.Text.Length > 0) dl.AddText(Snap(top), Palette.U32(color), tok.Text);
                }
                lx += tok.Width + tok.Trailing;
            }
            y += box;
        }
        ImGui.SetCursorScreenPos(cursor);
        return lines.Count * box;
    }

    /// <summary>A paragraph at the cursor across the box's width; the cursor moves below it.</summary>
    public static string? Para(IReadOnlyList<Run> runs, float lineCss = 19.5f, bool center = false, float? width = null)
    {
        var at = Kit.At;
        var w = width ?? Kit.Width;
        var h = Flow(at, w, runs, lineCss, out var clicked, center);
        Kit.Reserve(at, w, h);
        return clicked;
    }

    /// <summary>A line as the site's `.small` (13px, muted, line-height 1.5) at the cursor.</summary>
    public static void Note(string text, Vector4? color = null, bool center = false) => Para(new[] { Run.Note(text, color) }, 19.5f, center);

    internal static Vector2 Snap(Vector2 p) => new(MathF.Round(p.X), MathF.Round(p.Y));
}
