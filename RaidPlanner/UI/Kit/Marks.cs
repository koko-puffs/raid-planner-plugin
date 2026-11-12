using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>A tag's kind (`.rp-tag`): what it says in colour.</summary>
public enum TagKind
{
    /// <summary>A hairline and muted words: "Not kept".</summary>
    Default,

    /// <summary>A white wash, no edge: "Waiting for the pull", "DoT".</summary>
    Plain,

    /// <summary>Ready green with a beating dot: "Linked".</summary>
    Live,

    /// <summary>Ember: what's live ("Now").</summary>
    Ember,

    /// <summary>Ember with a beating ember dot: a pull being recorded.</summary>
    Rec,

    /// <summary>Danger red: a wipe.</summary>
    Wipe,

    /// <summary>White with dark words: a clear.</summary>
    Clear,
}

/// <summary>What a mechanic asks (`.rp-intent`), said before what it is, each with its colour and shape.</summary>
public enum Intent
{
    Dodge,
    Share,
    Apart,
    Tanks,
    Soak,
    Move,
    Look,
    Mark,
}

/// <summary>A square well's kind (`.well-ic`): quiet grey, ember, or danger red (a death).</summary>
public enum WellKind
{
    Quiet,
    Ember,
    Danger,
}

/// <summary>
/// Ready Check's marks: tags (mono capitals in 22px boxes), the ready tick, spots (round, in their
/// role's colour, on a dark halo), intents, the mono eyebrow, square wells, the reticle that closes
/// in on what's pointed at, the beating dot, cast bars (all time), the callout, and the error and
/// warning boxes.
/// </summary>
public static partial class Kit
{
    // ---------------------------------------------------------------------------------------------
    // Tags.

    private const float TagEm = 0.05f;

    private static (Vector4 Fill, Vector4 Edge, Vector4 Ink, Vector4? Dot) TagColors(TagKind kind) => kind switch
    {
        TagKind.Plain => (Palette.Rgba(255, 255, 255, 0.07f), Vector4.Zero, Palette.Text, null),
        TagKind.Live => (Palette.ReadyWash, Palette.ReadyLine, Palette.Ready, Palette.Ready),
        TagKind.Ember => (Palette.EmberWash, Palette.EmberLine, Palette.Ember, null),
        TagKind.Rec => (Palette.EmberWash, Palette.EmberLine, Palette.Ember, Palette.Ember),
        TagKind.Wipe => (Palette.DangerWash, Palette.DangerEdge, Palette.Danger, null),
        TagKind.Clear => (Palette.White, Vector4.Zero, Palette.OnEmber, null),
        _ => (Vector4.Zero, Palette.Line2, Palette.Muted, null),
    };

    public static float TagWidth(string text, TagKind kind) =>
        S(8) * 2 + (TagColors(kind).Dot != null ? S(6 + 5.6f) : 0) + Txt.SpacedWidth(Txt.Caps(text), TagFace(kind), 11, TagEm);

    private static Face TagFace(TagKind kind) => kind == TagKind.Clear ? Face.MonoSemibold : Face.Mono;

    /// <summary>A tag (`.rp-tag`): mono capitals, 11px at +0.05em, in a box 22 tall (or <paramref name="height"/>) with 6px corners. Returns its width.</summary>
    public static float Tag(ImDrawListPtr dl, Vector2 pos, string text, TagKind kind, float height = 22)
    {
        var (fill, edge, ink, dot) = TagColors(kind);
        var caps = Txt.Caps(text);
        var w = TagWidth(text, kind);
        var h = S(height);
        var r = S(Palette.RadiusXs);
        if (fill.W > 0) dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(fill), r);
        if (edge.W > 0) dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);
        var x = pos.X + S(8);
        if (dot is { } d)
        {
            BeatDot(dl, new Vector2(x + S(3), pos.Y + h / 2), d, true);
            x += S(6 + 5.6f);
        }
        var face = TagFace(kind);
        var th = Txt.Natural(face, 11);
        Txt.Spaced(dl, new Vector2(x, pos.Y + (h - th) / 2), caps, face, 11, ink, TagEm);
        return w;
    }

    /// <summary>
    /// The site's beating dot (`.tag.rec::before`, `.mt-clock::before`): 6px, with a ring of its colour
    /// going out to 6px and fading every 1.8 s; still (and <paramref name="beating"/> false) it just sits.
    /// </summary>
    public static void BeatDot(ImDrawListPtr dl, Vector2 center, Vector4 color, bool beating, float diameter = 6)
    {
        var r = S(diameter / 2);
        if (beating)
        {
            var (reach, alpha) = Beat();
            if (alpha > 0) dl.AddCircleFilled(center, r + S(6) * reach, Palette.U32(Palette.WithAlpha(color, alpha)), 20);
        }
        dl.AddCircleFilled(center, r, Palette.U32(color), 16);
    }

    // ---------------------------------------------------------------------------------------------
    // The ready tick, spots.

    /// <summary>The ready tick (`.tick.is-ready`): a green disc with a dark tick, 18px (16 in a line, 20 in a menu).</summary>
    public static void Tick(ImDrawListPtr dl, Vector2 center, float diameter = 18)
    {
        var d = S(diameter);
        dl.AddCircleFilled(center, d / 2, Palette.U32(Palette.Ready), 24);
        var icon = d * 11f / 18f;
        Icons.Draw(dl, Icon.Check, center - new Vector2(icon / 2), icon, Palette.OnReady, 2.6f);
    }

    /// <summary>
    /// A spot (`.rp-spot`): round in its role's colour, a dark halo round it and a faint white ring
    /// inside, its label in white (26px with 10px words, 20 with 8, 18 with 7).
    /// </summary>
    public static void Spot(ImDrawListPtr dl, Vector2 center, float diameter, string label, Vector4 color, float halo = 2)
    {
        var d = S(diameter);
        var r = d / 2;
        dl.AddCircleFilled(center, r + S(halo), Palette.U32(Palette.OnLight), 28);
        dl.AddCircleFilled(center, r, Palette.U32(color), 28);
        dl.AddCircle(center, r - 0.5f, Palette.U32(Palette.Rgba(255, 255, 255, 0.28f)), 28, 1f);
        var px = diameter >= 26 ? 10 : diameter >= 20 ? 8 : 7;
        var text = label.Length > 3 ? label[..3] : label;
        var size = Txt.Measure(text, Face.SansSemibold, px);
        Txt.Draw(dl, center - size / 2, text, Face.SansSemibold, px, Palette.White);
    }

    /// <summary>The limit break's mark in the meter (`.lbm`): an 18px square, 4px corners, a white wash, "LB" in mono 7px.</summary>
    public static void LimitMark(ImDrawListPtr dl, Vector2 min, float size = 18)
    {
        var s = S(size);
        dl.AddRectFilled(min, min + new Vector2(s), Palette.U32(Palette.Rgba(255, 255, 255, 0.08f)), S(4));
        var t = Txt.Measure("LB", Face.SansSemibold, 7);
        Txt.Draw(dl, min + (new Vector2(s) - t) / 2, "LB", Face.SansSemibold, 7, Palette.Text);
    }

    // ---------------------------------------------------------------------------------------------
    // Intents.

    public static Vector4 IntentColor(Intent i) => i switch
    {
        Intent.Share => Palette.Share,
        Intent.Apart => Palette.Apart,
        Intent.Tanks => Palette.Tanks,
        Intent.Soak => Palette.Soak,
        Intent.Move => Palette.Move,
        Intent.Look => Palette.Look,
        Intent.Mark => Palette.Mark,
        _ => Palette.Dodge,
    };

    public static string IntentWord(Intent i) => i switch
    {
        Intent.Share => "Share",
        Intent.Apart => "Apart",
        Intent.Tanks => "Tanks",
        Intent.Soak => "Soak",
        Intent.Move => "Move",
        Intent.Look => "Look",
        Intent.Mark => "Mark",
        _ => "Dodge",
    };

    public static float IntentWidth(Intent i) => S(8 + 5.6f) + Txt.SpacedWidth(Txt.Caps(IntentWord(i)), Face.MonoSemibold, 10, 0.1f);

    /// <summary>What a mechanic asks (`.rp-intent`): its 8px shape, then its word in mono capitals (600 10px, +0.1em), in its colour. Returns its width.</summary>
    public static float IntentMark(ImDrawListPtr dl, Vector2 pos, float lineH, Intent intent)
    {
        var color = IntentColor(intent);
        var s = S(8);
        var o = new Vector2(pos.X, pos.Y + (lineH - s) / 2);
        IntentShape(dl, o, s, intent, Palette.U32(color));
        var th = Txt.Natural(Face.MonoSemibold, 10);
        var w = Txt.Spaced(dl, new Vector2(pos.X + s + S(5.6f), pos.Y + (lineH - th) / 2), Txt.Caps(IntentWord(intent)), Face.MonoSemibold, 10, color, 0.1f);
        return s + S(5.6f) + w;
    }

    /// <summary>An intent's shape in a square from <paramref name="o"/>: round, diamond, four corners, shield, bar, chevron, eye, triangle.</summary>
    public static void IntentShape(ImDrawListPtr dl, Vector2 o, float s, Intent intent, uint col)
    {
        Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
        switch (intent)
        {
            case Intent.Dodge:
                dl.AddCircleFilled(o + new Vector2(s / 2), s / 2, col, 16);
                break;
            case Intent.Share:
                dl.AddQuadFilled(P(0.5f, 0), P(1, 0.5f), P(0.5f, 1), P(0, 0.5f), col);
                break;
            case Intent.Apart:
                dl.AddRectFilled(P(0, 0), P(0.35f, 0.35f), col);
                dl.AddRectFilled(P(0.65f, 0), P(1, 0.35f), col);
                dl.AddRectFilled(P(0, 0.65f), P(0.35f, 1), col);
                dl.AddRectFilled(P(0.65f, 0.65f), P(1, 1), col);
                break;
            case Intent.Tanks:
                dl.AddQuadFilled(P(0, 0), P(1, 0), P(1, 0.55f), P(0, 0.55f), col);
                dl.AddTriangleFilled(P(0, 0.55f), P(1, 0.55f), P(0.5f, 1), col);
                break;
            case Intent.Soak:
                dl.AddRectFilled(P(0.2f, 0), P(0.8f, 1), col);
                break;
            case Intent.Move:
                // A chevron, as two convex halves.
                dl.AddQuadFilled(P(0, 0), P(0.55f, 0), P(1, 0.5f), P(0.45f, 0.5f), col);
                dl.AddQuadFilled(P(0.45f, 0.5f), P(1, 0.5f), P(0.55f, 1), P(0, 1), col);
                break;
            case Intent.Look:
            {
                // ellipse(50% 32%): wide and flat.
                Span<Vector2> pts = stackalloc Vector2[20];
                for (var i = 0; i < pts.Length; i++)
                {
                    var a = MathF.PI * 2 * i / pts.Length;
                    pts[i] = P(0.5f + 0.5f * MathF.Cos(a), 0.5f + 0.32f * MathF.Sin(a));
                }
                dl.AddConvexPolyFilled(ref pts[0], pts.Length, col);
                break;
            }
            default:
                dl.AddTriangleFilled(P(0.5f, 0), P(1, 1), P(0, 1), col);
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The eyebrow.

    private const float EyebrowEm = 0.09f;

    /// <summary>
    /// The eyebrow (`.rp-eyebrow`): mono capitals, 500 11px at +0.09em, faint, cut to fit; a lead
    /// part in ember before it (`.eyebrow b`: the step's number). Returns its width.
    /// </summary>
    public static float Eyebrow(ImDrawListPtr dl, Vector2 pos, string text, string? lead = null, float maxWidth = 0, Vector4? color = null)
    {
        var x = pos.X;
        if (lead != null) x += Txt.Spaced(dl, new Vector2(x, pos.Y), Txt.Caps(lead), Face.Mono, 11, Palette.Ember, EyebrowEm) + S(11 * EyebrowEm);
        var caps = Txt.Caps(text);
        if (maxWidth > 0)
        {
            var room = maxWidth - (x - pos.X);
            if (Txt.SpacedWidth(caps, Face.Mono, 11, EyebrowEm) > room) caps = FitSpaced(caps, Face.Mono, 11, EyebrowEm, room);
        }
        x += Txt.Spaced(dl, new Vector2(x, pos.Y), caps, Face.Mono, 11, color ?? Palette.Faint, EyebrowEm);
        return x - pos.X;
    }

    public static float EyebrowWidth(string text, string? lead = null) =>
        Txt.SpacedWidth(Txt.Caps(text), Face.Mono, 11, EyebrowEm) + (lead != null ? Txt.SpacedWidth(Txt.Caps(lead), Face.Mono, 11, EyebrowEm) + S(11 * EyebrowEm) : 0);

    /// <summary>The eyebrow's line (its font's own).</summary>
    public static float EyebrowH => Txt.Natural(Face.Mono, 11);

    /// <summary>Letter-spaced text cut to a width with "…".</summary>
    public static string FitSpaced(string text, Face face, int px, float em, float width) =>
        Format.Fit(text, s => Txt.SpacedWidth(s, face, px, em), width);

    /// <summary>Mono capitals in a small label (`.fx small`, the meter's column heads): letter-spaced, cut to fit. Returns the width.</summary>
    public static float Label(ImDrawListPtr dl, Vector2 pos, string text, int px, float em, Vector4 color, float maxWidth = 0)
    {
        var caps = Txt.Caps(text);
        if (maxWidth > 0 && Txt.SpacedWidth(caps, Face.Mono, px, em) > maxWidth) caps = FitSpaced(caps, Face.Mono, px, em, maxWidth);
        return Txt.Spaced(dl, pos, caps, Face.Mono, px, color, em);
    }

    // ---------------------------------------------------------------------------------------------
    // Wells.

    /// <summary>A square well (`.well-ic`): 32px with 8px corners (or <paramref name="size"/>), its wash; returns the colour for what goes in it.</summary>
    public static Vector4 Well(ImDrawListPtr dl, Vector2 min, float size, WellKind kind = WellKind.Quiet)
    {
        var (fill, ink) = kind switch
        {
            WellKind.Ember => (Palette.EmberWash, Palette.Ember),
            WellKind.Danger => (Palette.DangerWash, Palette.Danger),
            _ => (Palette.Tint, Palette.Text),
        };
        var s = S(size);
        dl.AddRectFilled(min, min + new Vector2(s), Palette.U32(fill), S(Palette.RadiusSm));
        return ink;
    }

    /// <summary>A well with a Lucide icon in it (15px).</summary>
    public static void IconWell(ImDrawListPtr dl, Vector2 min, float size, Icon icon, WellKind kind = WellKind.Quiet)
    {
        var ink = Well(dl, min, size, kind);
        var s = S(size);
        Icons.Draw(dl, icon, min + new Vector2((s - S(IconPx)) / 2), S(IconPx), ink, 1.75f);
    }

    // ---------------------------------------------------------------------------------------------
    // The reticle.

    /// <summary>
    /// The reticle (`.ret`): four corner brackets 9px long and 1.5px thick, 5px outside what's
    /// targeted, closing in from 1.035 and fading in over 160ms when it's pointed at or picked. A side
    /// that would fall outside what the window (or the list, menu or child it's in) shows is drawn just
    /// inside its edge instead, so a row that runs the list's full width, or one scrolled half out of
    /// it, never has its corners cut off.
    /// </summary>
    public static void Reticle(ImDrawListPtr dl, string id, Vector2 min, Vector2 max, bool on, Vector4? color = null)
    {
        var t = Approach(id + "##ret", on ? 1f : 0f, TStep);
        if (t <= 0.001f) return;
        var e = Ease.Snap(t);
        var o = S(5);
        var a = min - new Vector2(o);
        var b = max + new Vector2(o);
        var c = (a + b) / 2;
        var k = 1.035f - 0.035f * e;
        a = c + (a - c) * k;
        b = c + (b - c) * k;
        // Kept inside what's drawn here (the draw list's clip rect, less a hair so the line isn't on it).
        var edge = S(0.5f);
        a = Vector2.Max(a, dl.GetClipRectMin() + new Vector2(edge));
        b = Vector2.Min(b, dl.GetClipRectMax() - new Vector2(edge));
        if (b.X - a.X < S(4) || b.Y - a.Y < S(4)) return;
        var col = Palette.U32(Palette.WithAlpha(color ?? Palette.White, e));
        var l = S(9);
        var w = S(1.5f);
        // Each bracket as two filled bars, so the corner is square and crisp.
        void Corner(Vector2 p, float dx, float dy)
        {
            dl.AddRectFilled(Min(p, p + new Vector2(l * dx, w * dy)), Max(p, p + new Vector2(l * dx, w * dy)), col);
            dl.AddRectFilled(Min(p, p + new Vector2(w * dx, l * dy)), Max(p, p + new Vector2(w * dx, l * dy)), col);
        }
        Corner(a, 1, 1);
        Corner(new Vector2(b.X, a.Y), -1, 1);
        Corner(new Vector2(a.X, b.Y), 1, -1);
        Corner(b, -1, -1);
    }

    private static Vector2 Min(Vector2 a, Vector2 b) => Vector2.Min(a, b);

    private static Vector2 Max(Vector2 a, Vector2 b) => Vector2.Max(a, b);

    // ---------------------------------------------------------------------------------------------
    // Cast bars: all time and progress.

    /// <summary>
    /// A cast bar's bar (`.cast-bar`): a 6px track (3 slim) of white at 9%, filled from deep to light
    /// ember (a square-ended gradient: ImGui can't round one), its hot end a 2px line with a glow;
    /// <paramref name="ready"/> fills it green instead.
    /// </summary>
    public static void CastBar(ImDrawListPtr dl, Vector2 pos, float width, float fraction, float height = 6, bool ready = false)
    {
        var h = S(height);
        var r = S(2);
        dl.AddRectFilled(pos, pos + new Vector2(width, h), Palette.U32(Palette.Track), r);
        fraction = Math.Clamp(float.IsFinite(fraction) ? fraction : 0, 0, 1);
        var fw = MathF.Round(width * fraction);
        if (fw < 1) return;
        if (ready)
        {
            dl.AddRectFilled(pos, pos + new Vector2(fw, h), Palette.U32(Palette.Ready), r);
            return;
        }
        // linear-gradient(90deg, deep, ember 70%, light).
        var mid = pos.X + fw * 0.7f;
        var deep = Palette.U32(Palette.EmberDeep);
        var ember = Palette.U32(Palette.Ember);
        var hi = Palette.U32(Palette.EmberHi);
        dl.AddRectFilledMultiColor(pos, new Vector2(mid, pos.Y + h), deep, ember, ember, deep);
        dl.AddRectFilledMultiColor(new Vector2(mid, pos.Y), new Vector2(pos.X + fw, pos.Y + h), ember, hi, hi, ember);
        // The hot end, and its glow inside the bar (the bar clips it, as overflow: hidden does).
        var end = pos.X + fw;
        var glow = Palette.U32(Palette.WithAlpha(Palette.EmberHi, 0.55f));
        var none = Palette.U32(Palette.WithAlpha(Palette.EmberHi, 0));
        dl.AddRectFilledMultiColor(new Vector2(MathF.Max(pos.X, end - S(8)), pos.Y), new Vector2(end, pos.Y + h), none, glow, glow, none);
        if (end + 1 < pos.X + width)
            dl.AddRectFilledMultiColor(new Vector2(end, pos.Y), new Vector2(MathF.Min(pos.X + width, end + S(8)), pos.Y + h), glow, none, none, glow);
        dl.AddRectFilled(new Vector2(end - S(2), pos.Y), new Vector2(end, pos.Y + h), Palette.U32(Palette.CastHot));
    }

    /// <summary>
    /// A cast (`.cast`): its name (600 13px white, cut to fit) and its time (mono 500 12px muted) on one
    /// line, the bar 4.8px under them across the width. Returns its height.
    /// </summary>
    public static float Cast(ImDrawListPtr dl, Vector2 pos, float width, string name, string? time, float fraction, bool slim = false,
        Face nameFace = Face.SansSemibold, int namePx = 13, Vector4? nameColor = null, bool ready = false, string? nameRest = null)
    {
        var nameH = S(namePx * 1.2f);
        var timeW = time != null ? Txt.Width(time, Face.Mono, 12) : 0;
        var room = width - (time != null ? timeW + S(12) : 0);
        var shown = Txt.Fit(name, nameFace, namePx, room);
        var nw = Txt.Line(dl, pos, shown, nameFace, namePx, nameColor ?? Palette.White, namePx * 1.2f);
        // A quieter part after the name (`· step 12 of your plan`), as much as fits.
        if (nameRest != null && shown == name && nw + S(4) < room)
            Txt.Line(dl, new Vector2(pos.X + nw + S(4), pos.Y), Txt.Fit(nameRest, Face.Sans, namePx, room - nw - S(4)), Face.Sans, namePx, Palette.Muted, namePx * 1.2f);
        if (time != null)
        {
            // Baselines line up (align-items: baseline).
            var y = pos.Y + (nameH - Txt.Natural(nameFace, namePx)) / 2 + Txt.Ascent(nameFace, namePx) - Txt.Ascent(Face.Mono, 12);
            Txt.Draw(dl, new Vector2(pos.X + width - timeW, y), time, Face.Mono, 12, Palette.Muted);
        }
        var barY = pos.Y + nameH + S(4.8f);
        CastBar(dl, new Vector2(pos.X, barY), width, fraction, slim ? 3 : 6, ready);
        return nameH + S(4.8f) + S(slim ? 3 : 6);
    }

    // ---------------------------------------------------------------------------------------------
    // The callout.

    private static readonly Dictionary<(string Text, int Px, int Width, int Lines), List<string>> CalloutCache = new();

    /// <summary>
    /// The callout's lines for a width: in capitals, wrapped, at most <paramref name="maxLines"/> (the
    /// last cut with "…"). Kept, so a frame measures nothing; but not while the face isn't built yet
    /// (measured in Dalamud's font then, the lines would stay wrong once it is).
    /// </summary>
    public static List<string> CalloutLines(string text, int px, float width, int maxLines)
    {
        var key = (text, px, (int)width, maxLines);
        if (CalloutCache.TryGetValue(key, out var lines)) return lines;
        lines = Txt.Wrap(Txt.CalloutCaps(text), Face.Callout, px, width, maxLines);
        if (!Txt.Ready(Face.Callout, px)) return lines;
        if (CalloutCache.Count > 256) CalloutCache.Clear();
        return CalloutCache[key] = lines;
    }

    /// <summary>
    /// The callout (`.rp-callout`): Archivo Expanded ExtraBold in capitals, what a raid lead says; at
    /// 13 (xs), 16, 17 (sm) or 22px, its lines <paramref name="lineFactor"/> × its size apart. Returns its height.
    /// </summary>
    public static float Callout(ImDrawListPtr dl, Vector2 pos, string text, int px, float width, int maxLines = 2, float lineFactor = 1.05f,
        Vector4? color = null, bool center = false)
    {
        var lines = CalloutLines(text, px, width, maxLines);
        var lh = S(px * lineFactor);
        for (var i = 0; i < lines.Count; i++)
        {
            var x = pos.X;
            if (center) x += (width - Txt.Width(lines[i], Face.Callout, px)) / 2;
            Txt.Line(dl, new Vector2(x, pos.Y + lh * i), lines[i], Face.Callout, px, color ?? Palette.White, px * lineFactor);
        }
        return lh * lines.Count;
    }

    public static float CalloutHeight(string text, int px, float width, int maxLines = 2, float lineFactor = 1.05f) =>
        S(px * lineFactor) * CalloutLines(text, px, width, maxLines).Count;

    // ---------------------------------------------------------------------------------------------
    // Boxes: an error, a warning.

    /// <summary>
    /// A box that says something went wrong (`.err`) or that the game hides something (`.warnbox`):
    /// its wash and edge, 10px corners, an icon and 13px words wrapped. Laid at the cursor; returns its height.
    /// </summary>
    public static float NoteBox(string text, bool warn = false)
    {
        var at = At;
        var w = Width;
        var padX = S(12);
        var padY = S(9.6f);
        var icon = S(IconPx);
        var gap = S(8.8f);
        var textW = w - padX * 2 - icon - gap;
        var runs = new[] { new Run(text, Face.Sans, 13, warn ? Palette.WarnInk : Palette.DangerInk) };
        var th = Txt.Flow(Vector2.Zero, textW, runs, 19.5f, out _, draw: false);
        var h = padY * 2 + th;
        var dl = Dl;
        var r = S(Palette.RadiusMd);
        dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(warn ? Palette.WarnWash : Palette.DangerWash), r);
        dl.AddRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(warn ? Palette.WarnLine : Palette.DangerLine), r, ImDrawFlags.None, 1f);
        Icons.Draw(dl, warn ? Icon.AlertTriangle : Icon.CircleX, at + new Vector2(padX, padY + (S(19.5f) - icon) / 2), icon, warn ? Palette.Warn : Palette.Danger, 1.75f);
        Txt.Flow(at + new Vector2(padX + icon + gap, padY), textW, runs, 19.5f, out _);
        Reserve(at, w, h);
        return h;
    }
}
