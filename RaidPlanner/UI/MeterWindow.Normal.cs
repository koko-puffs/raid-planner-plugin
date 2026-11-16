using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The normal meter (compact off) in the picked theme (since 0.7.3), as a bigger sibling of the compact
/// one: the theme's ground and frame (<see cref="PlateMeter.Ground"/>), its head band, its faces by role
/// (<see cref="DetailFonts.ForNormal"/>, words measured and centred by their ink), the rows on its plates
/// (<see cref="MeterSizes.For"/>: 25 tall, or what its faces need) with its job icons and role colours,
/// its bar style along a track that ends before the second column (and before the number and the job
/// for a hard-ended bar: <see cref="ThemeBar.BeforeNumber"/>), its mark for you and for the dead; and its
/// own parts, the tabs, the column heads, pace, the limit break's row, the foot, the dropdowns, the tags,
/// the lock and the menus, in the theme's chips, accent and lines (<see cref="ThemeSkin"/>). Ready Check
/// is today's normal meter refined to match compact Ready Check (its rows on plates, the bar in the role's
/// colour, the ember notch for you). Its columns, tabs, pace, growing up or down (its head at the top or the bottom, its rows highest or lowest first: <see cref="MeterArrange"/>), resizing (with the
/// resize zones kept clear) and the lock are as before.
/// </summary>
public sealed partial class MeterWindow
{
    private static readonly string[] TabLabels = { "Damage", "Healing", "Taken", "Deaths", "Events" };

    /// <summary>The normal meter's look in the theme: its faces, colours and sizes, worked out once a theme.</summary>
    private (MeterThemeId Id, DetailFonts F, ThemeInk I, MeterSizes Z) normalLook = (MeterThemeId.ReadyCheck, DetailFonts.NormalReadyCheck, ThemeInk.Of(MeterThemes.ReadyCheck), MeterSizes.Normal);

    private (MeterThemeId Id, DetailFonts F, ThemeInk I, MeterSizes Z) NormalLook()
    {
        var t = PlateTheme;
        if (normalLook.Id != t.Id) normalLook = (t.Id, DetailFonts.ForNormal(t), ThemeInk.Of(t), MeterSizes.For(t));
        return normalLook;
    }

    private DetailFonts NF => NormalLook().F;

    private ThemeInk NI => NormalLook().I;

    /// <summary>The plates' inset from the window's sides (CSS px).</summary>
    private const float PlateInset = 6;

    // ---------------------------------------------------------------------------------------------
    // The grid: where a row's parts go, its columns as wide as their widest words.

    /// <summary>A row's parts (screen px): the plate's sides, the icon's and name's left, the job's left, the number's and second's right edges, Mochi's heart's place, and the columns' widths.</summary>
    private readonly record struct NormalGrid(float PlateL, float PlateR, float Icon, float IconSize, float Name, float Job, float ValueR, float SecondR, float Heart, float ValueW,
        float SecondW, float JobW, float Gap, float PadR);

    /// <summary>The columns' widths, measured for a snapshot, a tab, a theme, a scale, a font build and Jobs only.</summary>
    private ((MeterTab, MeterRank, MeterThemeId, float, int, bool) Key, float Value, float Second, float Job) columnsNow;

    /// <summary>The snapshot the columns were measured for (compared by reference: an encounter is a record).</summary>
    private MeterEncounter? columnsOf;

    private NormalGrid GridOf(float left, float w, MeterEncounter? e, MeterTab tab)
    {
        var t = PlateTheme;
        var z = Z;
        var f = NF;
        var gap = S(8);
        var padR = S(t.Id == MeterThemeId.Mochi ? 8 : 6);
        var key = (tab, config.MeterRank, t.Id, S(1), Txt.Fonts.Generation, config.MeterJobsOnly);
        if (!ReferenceEquals(columnsOf, e) || columnsNow.Key != key)
        {
            float value = S(z.ValueCol), second = S(z.SecondCol), job = config.MeterJobsOnly ? 0 : S(z.JobCol);
            value = MathF.Max(value, PlateArt.Width("888,888", f.NumStrong));
            second = MathF.Max(second, MathF.Max(PlateArt.Width("100.0", f.Small), PlateArt.Width("100%", f.Small)));
            if (e != null)
            {
                var (rows, lb, _) = LinesOf(e, tab is MeterTab.Deaths or MeterTab.Events ? MeterTab.Damage : tab, config.MeterRank);
                foreach (var l in rows)
                {
                    value = MathF.Max(value, PlateArt.Width(l.Value, f.NumStrong));
                    second = MathF.Max(second, PlateArt.Width(l.Second, f.Small));
                    if (!config.MeterJobsOnly) job = MathF.Max(job, PlateArt.Width(l.Row.Job, f.HeadSmall));
                }
                if (lb != null) value = MathF.Max(value, PlateArt.Width(lb.Value, f.NumStrong));
            }
            var ready = Txt.Ready(f.NumStrong.Face, f.NumStrong.Px) && Txt.Ready(f.Small.Face, f.Small.Px);
            columnsNow = (ready ? key : default, value, second, job);
            columnsOf = ready ? e : null;
        }
        var plateL = left + S(PlateInset);
        var plateR = left + w - S(PlateInset);
        var heart = t.YouDrawn(config.MeterDecorations) == YouStyle.Heart ? S(10) + S(2) : 0;
        var secondR = plateR - padR - heart;
        var valueR = secondR - columnsNow.Second - gap;
        var jobL = valueR - columnsNow.Value - gap - columnsNow.Job;
        var iconSize = (t.Icon?.Size ?? 16) + 2;
        var icon = plateL + S(4);
        return new NormalGrid(plateL, plateR, icon, iconSize, icon + S(iconSize) + S(8), jobL, valueR, secondR, heart, columnsNow.Value, columnsNow.Second, columnsNow.Job, gap, padR);
    }

    /// <summary>A row's bar's track (screen px): to just before the second column, or (a hard-ended bar) before the job and the number too.</summary>
    private float TrackOf(NormalGrid g)
    {
        var beforeNumber = PlateTheme.Bar.BeforeNumber ? g.ValueW + (g.JobW > 0 ? g.Gap + g.JobW : 0) : 0;
        return PlateColumns.Track(g.PlateR - g.PlateL, g.PadR, g.Heart, g.SecondW, S(1), beforeNumber, g.Gap);
    }

    // ---------------------------------------------------------------------------------------------
    // The head: the clock, the tags, which pull, the lock.

    /// <summary>
    /// The head (36 tall) at the anchored edge, on the theme's band (a plate for Ready Check and Neon, the
    /// title band for Native, Grimoire's rules): on the right the lock and the picker; on the left the
    /// theme's clock (Mochi's pill with its heart, Native's diamond and title, Readout's prompt, Grimoire's
    /// rubric, else the beating dot), then the Preview and outcome tags, the limit break's pips or the
    /// waiting line, as far as they fit before the buttons.
    /// </summary>
    private void Header(ImDrawListPtr dl, float left, ref MeterStack stack, float w, string clock, bool beating, string? tag, TagKind tagKind, string? pick,
        IMeterSource? m, string? waitingLine, bool hasFight)
    {
        var t = PlateTheme;
        var ink = NI;
        var f = NF;
        var z = Z;
        var h = S(z.Head);
        var y = stack.Near(h);
        headTop = y;
        headBottom = y + h;
        var cy = y + h / 2;
        HeadGround(dl, left, w, y, h);
        var right = left + w - S(PlateInset) - S(2);
        // The lock: the accent's wash when locked, a pointer when click-through, quiet when open. Out of the
        // reach of the window's right edge and (its head at the bottom, whichever way it grows) its bottom-right grip.
        var ls = S(24);
        var lx = right - ls;
        lx -= guard.ShiftLeft(new Vector2(lx, cy - ls / 2), new Vector2(lx + ls, cy + ls / 2));
        guard.Keep(new Vector2(lx, cy - ls / 2), new Vector2(lx + ls, cy + ls / 2));
        var locked = config.MeterLocked;
        var icon = !locked ? Icon.LockOpen : config.MeterClickThrough ? Icon.MousePointer : Icon.Lock;
        if (ThemeSkin.IconButton(dl, ink, "##meter-lock", new Vector2(lx, cy - ls / 2), ls, icon, locked,
                locked ? "Locked where it is. Press to move and size it again." : "Lock it where it is"))
        {
            config.MeterLocked = !locked;
            config.Save();
        }
        var edge = lx;
        if (pick != null && m != null)
        {
            // The picker: the theme's chip, "Pull 12 ⌄".
            var pw = ThemeSkin.DropWidth(pick, f.BodyStrong, S(13));
            var px = edge - S(8) - pw;
            guard.Keep(new Vector2(px, cy - S(12)), new Vector2(px + pw, cy + S(12)));
            if (ThemeSkin.DropButton(dl, t, ink, "##meter-pick", new Vector2(px, cy - S(12)), pw, S(24), pick, f.BodyStrong)) ImGui.OpenPopup(Picker);
            DrawPicker(m, px);
            edge = px;
        }
        var room = edge - S(8);
        var x = HeadClock(dl, left + S(PlateInset) + S(6), y, h, clock, beating, waitingLine != null, room);
        if (Preview != null)
        {
            // The sample's mark; pressed, it stops the sample. Where it doesn't fit, the foot shows it.
            var pw = ThemeSkin.TagWidth(t, "Preview", f.Small);
            if (x + pw <= room)
            {
                PreviewTag(dl, new Vector2(x, y), h);
                x += pw + S(8);
            }
            else previewInFoot = true;
        }
        if (tag != null)
        {
            var tw = ThemeSkin.TagWidth(t, tag, f.Small);
            if (x + tw <= room) x += ThemeSkin.Tag(dl, t, ink, new Vector2(x, y), h, tag, f.Small, tagKind == TagKind.Clear ? (t.Light ? t.Good : ink.Strong) : t.Bad) + S(8);
        }
        // The limit break's pips, when they're wanted in the head, as far as they fit before the buttons.
        if (config.MeterGauge == GaugePlace.Head && beating && Bars(m) is { Length: > 0 } bars)
        {
            var gw = PipsWidth(bars.Length);
            if (x + gw <= room)
            {
                Pips(dl, new Vector2(x, cy), bars);
                if (Kit.Pointed(new Vector2(x, y), new Vector2(x + gw, y + h)) && m?.LimitBreak is { } g) Kit.Tooltip(MeterText.GaugeTip(g));
                x += gw + S(8);
            }
        }
        // "Waiting for the first hit", as much as fits before the buttons.
        if (waitingLine != null && room - x > S(20))
            PlateArt.Line(dl, new Vector2(x, y), PlateArt.Fit(waitingLine, f.Body, room - x), f.Body, ink.Muted, z.Head);
        _ = hasFight;
    }

    /// <summary>The head's ground: the theme's band, and a plate for a theme whose head sits on one (Ready Check, Neon).</summary>
    private void HeadGround(ImDrawListPtr dl, float left, float w, float y, float h)
    {
        var t = PlateTheme;
        if (t.HeadPlates)
        {
            var a = new Vector2(left + S(PlateInset) - S(2), y + S(4));
            var b = new Vector2(left + w - S(PlateInset) + S(2), y + h - S(4));
            dl.AddRectFilled(a, b, Palette.U32(Rgb.A(t.Row.Plate, t.Row.PlateAlpha)), S(t.Row.Radius));
        }
        plate.HeadBand(dl, left, left + w, y, h, HeadBottom);
    }

    /// <summary>The clock in the theme's way, from <paramref name="x"/>; returns where what follows it starts.</summary>
    private float HeadClock(ImDrawListPtr dl, float x, float y, float h, string clock, bool beating, bool waiting, float room)
    {
        var t = PlateTheme;
        var f = NF;
        var ink = NI;
        var cy = y + h / 2;
        var hCss = h / S(1);
        var gap = S(8);
        switch (t.Id)
        {
            case MeterThemeId.Mochi:
            {
                var cw = PlateArt.Width(clock, f.Clock);
                var pillW = S(8) + S(12) + S(6) + cw + S(10);
                var pill = new Vector2(x, cy - S(12));
                dl.AddRectFilled(pill, pill + new Vector2(pillW, S(24)), Palette.U32(Rgb.Hex(0xffdcea)), S(12));
                PlateArt.Glyph(dl, PlateGlyph.Heart, new Vector2(x + S(8), cy - S(6)), S(12), beating ? Rgb.Hex(0xff4f9a) : Rgb.Hex(0xd9a7bd));
                PlateArt.Line(dl, new Vector2(x + S(8) + S(12) + S(6), y), clock, f.Clock, t.Clock, hCss);
                return x + pillW + gap;
            }
            case MeterThemeId.Native:
            {
                var d = S(8);
                PlateArt.Glyph(dl, PlateGlyph.Diamond, new Vector2(x - S(1), cy - d / 2 - S(1)), d + S(2), Rgb.Hex(0x000000));
                PlateArt.Glyph(dl, PlateGlyph.Diamond, new Vector2(x, cy - d / 2), d, Rgb.Hex(0xd6b878));
                x += d + gap;
                var titleFont = t.Font.Head with { Px = t.Font.Head.Px + 2 };
                var title = MeterLayout.TabWord(MeterLayout.Known(config.MeterTab));
                if (x + PlateArt.Width(title, titleFont) + gap + PlateArt.Width(clock, f.Clock) <= room)
                    x += PlateArt.Line(dl, new Vector2(x, y), title, titleFont, Rgb.Hex(0xefe3c4), hCss, Rgb.Rgba(0, 0, 0, 0.8f)) + gap;
                return x + PlateArt.Line(dl, new Vector2(x, y), clock, f.Clock, t.Clock, hCss) + gap;
            }
            case MeterThemeId.Readout:
                x += PlateArt.Line(dl, new Vector2(x, y), ">", f.Clock, t.Accent, hCss) + S(6);
                x += PlateArt.Line(dl, new Vector2(x, y), clock, f.Clock, t.Clock, hCss) + S(2);
                if (beating) dl.AddRectFilled(new Vector2(x, cy - S(7)), new Vector2(x + S(7), cy + S(7)), Palette.U32(t.Accent));
                return x + S(7) + gap;
            case MeterThemeId.Grimoire:
            {
                var title = MeterLayout.TabWord(MeterLayout.Known(config.MeterTab));
                if ((t.Drawn(config.MeterDecorations) & Ornaments.Rubric) != 0 && title.Length > 1)
                {
                    var cap = new ThemeFont(Face.SerifSemibold, 19);
                    x += PlateArt.Line(dl, new Vector2(x, y - S(1.5f)), title[..1], cap, Rgb.Hex(0x9b1c1c), hCss) + S(1);
                    x += PlateArt.Line(dl, new Vector2(x, y), title[1..], t.Font.Head, Rgb.Hex(0x3b2412), hCss) + gap;
                }
                return x + PlateArt.Line(dl, new Vector2(x, y), clock, f.Clock, t.Clock, hCss) + gap;
            }
            default:
            {
                var c = new Vector2(x + S(3), cy);
                if (beating) Kit.BeatDot(dl, c, t.Dot, true);
                else if (waiting) dl.AddCircle(c, S(3) - S(0.75f), Palette.U32(ink.Faint), 16, S(1.5f));
                else Kit.BeatDot(dl, c, ink.Faint, false);
                x += S(6) + S(7);
                return x + PlateArt.Line(dl, new Vector2(x, y), clock, f.Clock, t.Clock, hCss, t.Id == MeterThemeId.Neon ? Rgb.Rgba(61, 236, 255, 0.35f) : null) + gap;
            }
        }
    }

    /// <summary>The Preview tag: the sample's mark in the theme's chip. Pressed, the sample stops.</summary>
    private void PreviewTag(ImDrawListPtr dl, Vector2 lineTop, float h)
    {
        var t = PlateTheme;
        var w = ThemeSkin.TagWidth(t, "Preview", NF.Small);
        var pos = new Vector2(lineTop.X, lineTop.Y + (h - S(18)) / 2);
        guard.Keep(pos, pos + new Vector2(w, S(18)));
        var (clicked, hovered, _) = Kit.Hit("##meter-preview", pos, new Vector2(w, S(18)));
        ThemeSkin.Tag(dl, t, NI, lineTop, h, "Preview", NF.Small, t.Accent);
        if (hovered) Kit.Tooltip("A sample fight with made-up numbers. Press to stop it; a real fight stops it too.");
        if (clicked) Preview = null;
    }

    /// <summary>The Preview tag in the foot, its right edge at <paramref name="right"/> (moved in out of the bottom-right grip's reach when the foot is at the bottom).</summary>
    private void FootPreviewTag(ImDrawListPtr dl, float right, float footY, float footH)
    {
        var w = ThemeSkin.TagWidth(PlateTheme, "Preview", NF.Small);
        var pos = new Vector2(right - w, footY + (footH - S(18)) / 2);
        var x = pos.X - guard.ShiftLeft(pos, pos + new Vector2(w, S(18)));
        PreviewTag(dl, new Vector2(x, footY), footH);
    }

    /// <summary>The limit break's pips' width: "LB" in the heads' capitals and a 14px pip each, 3 apart.</summary>
    private float PipsWidth(int n) => PlateArt.Width("LB", NF.Head) + S(5) + n * S(14) + Math.Max(0, n - 1) * S(2);

    /// <summary>The limit break's pips in the theme (as compact's, a size up), "LB" before them.</summary>
    private void Pips(ImDrawListPtr dl, Vector2 leftMid, float[] bars)
    {
        var lw = PlateArt.Line(dl, new Vector2(leftMid.X, leftMid.Y - S(8)), "LB", NF.Head, NI.Faint, 16);
        plate.DrawPips(dl, new Vector2(leftMid.X + lw + S(5), leftMid.Y), bars, 14, 5);
    }

    // ---------------------------------------------------------------------------------------------
    // The tabs, pace, the column heads.

    /// <summary>The five tabs (27 tall) next to the head, in the theme's words with its accent under the one you're on, a hairline on their far side; a narrower meter has a dropdown in their place.</summary>
    private void TabsRow(ImDrawListPtr dl, float left, ref MeterStack stack, float w)
    {
        var t = PlateTheme;
        var ink = NI;
        var f = NF.BodyStrong;
        var h = S(Z.Tabs);
        var y = stack.Near(h);
        var current = MeterLayout.Known(config.MeterTab);
        if (ThemeSkin.TabsWidth(TabLabels, f, 7) + S(PlateInset) * 2 > w)
        {
            var word = MeterLayout.TabWord(current);
            var tx = left + S(PlateInset) + S(2);
            if (ThemeSkin.DropButton(dl, t, ink, "##meter-tabs-row", new Vector2(tx, y + (h - S(24)) / 2), ThemeSkin.DropWidth(word, f, S(13)), S(24), word, f)) ImGui.OpenPopup(TabMenu);
            headTop = y;
            headBottom = y + h;
            DrawTabMenu(tx, shown != null);
            Kit.Rule(dl, left + S(PlateInset), left + w - S(PlateInset), stack.Away(y, h), ink.Rule);
            return;
        }
        var picked = (MeterTab)ThemeSkin.Tabs(dl, t, ink, "##meter-tab", new Vector2(left + S(PlateInset) - S(1), y), h, TabLabels, (int)current, f, 7);
        Kit.Rule(dl, left + S(PlateInset), left + w - S(PlateInset), stack.Away(y, h), ink.Rule);
        if (picked == config.MeterTab) return;
        config.MeterTab = picked;
        config.Save();
    }

    /// <summary>Pace (26 tall): the phase and how far into it in the heads' capitals, the bar (this pull in the accent, the other's a tick), "on pull 4 +3.4%".</summary>
    private void PaceRow(ImDrawListPtr dl, float left, ref MeterStack stack, float w, PaceLine pace)
    {
        var t = PlateTheme;
        var ink = NI;
        var f = NF;
        var z = Z;
        var h = S(z.Pace);
        var y = stack.Near(h);
        var (label, ahead, on) = PaceWords(pace);
        var x = left + S(z.Pad);
        var lw = PlateArt.Line(dl, new Vector2(x, y), label, f.Head, ink.Faint, z.Pace);
        var rx = left + w - S(z.Pad);
        var aw = PlateArt.LineRight(dl, rx, y, ahead, f.SmallStrong, pace.Ahead >= 0 ? (t.Id == MeterThemeId.ReadyCheck ? ink.Strong : t.Good) : t.Bad, z.Pace);
        var ow = PlateArt.LineRight(dl, rx - aw, y, on, f.Note, ink.Muted, z.Pace);
        var bx = x + lw + S(8);
        var bw = rx - aw - ow - S(8) - bx;
        if (bw > S(20))
        {
            var (fill, mark) = MeterView.PaceBar(pace);
            var by = y + h / 2 - S(1.5f);
            dl.AddRectFilled(new Vector2(bx, by), new Vector2(bx + bw, by + S(3)), Palette.U32(Rgb.A(ink.Text, 0.1f)), S(2));
            if (fill > 0)
            {
                if (t.Id == MeterThemeId.ReadyCheck) Kit.CastGradient(dl, new Vector2(bx, by), new Vector2(bx + bw * fill, by + S(3)));
                else dl.AddRectFilled(new Vector2(bx, by), new Vector2(bx + bw * fill, by + S(3)), Palette.U32(t.Accent), S(2));
            }
            var mx = bx + bw * mark;
            dl.AddRectFilled(new Vector2(mx - S(0.75f), by - S(3)), new Vector2(mx + S(0.75f), by + S(6)), Palette.U32(ink.Strong));
        }
        Kit.Rule(dl, left + S(PlateInset), left + w - S(PlateInset), stack.Away(y, h), ink.Rule);
    }

    /// <summary>The columns' heads (20 tall; the heads' capitals, faint) next to the rows, right-aligned to their columns.</summary>
    private void Columns(ImDrawListPtr dl, float left, ref MeterStack stack, float w, MeterTab tab)
    {
        var z = Z;
        var f = NF.Head;
        var h = S(z.Cols);
        var y = stack.Near(h);
        var (a, b) = MeterView.Heads(tab, config.MeterRank);
        var g = GridOf(left, w, shown, tab);
        // Against the rows: at the band's foot when they're under it, at its top when they're above (the head at the bottom).
        var ly = stack.Up ? y + S(2) : y + h - S(2) - S(14);
        PlateArt.LineRight(dl, g.ValueR, ly, a, f, NI.Faint, 14);
        PlateArt.LineRight(dl, g.SecondR, ly, b, f, NI.Faint, 14);
    }

    // ---------------------------------------------------------------------------------------------
    // The rows.

    /// <summary>The rows on their plates, biggest first from the head's side, then the limit break's after a hairline. Past the room they're cut.</summary>
    private void Rows(ImDrawListPtr dl, float left, float w, MeterEncounter e, MeterTab tab, MeterStack stack)
    {
        var z = Z;
        var (rows, lb, _) = LinesOf(e, tab, config.MeterRank);
        var g = GridOf(left, w, e, tab);
        var rowH = S(z.Row);
        var lbH = S(z.LimitBreak);
        var n = Math.Min(rows.Count, stack.Fits(rowH));
        var arr = Arr;
        var a = MathF.Min(stack.NearEdge, stack.FarEdge);
        var b = MathF.Max(stack.NearEdge, stack.FarEdge);
        dl.PushClipRect(new Vector2(left, a - S(4)), new Vector2(left + w, b + S(4)), true);
        try
        {
            // The top n (the rest cut past the room), the top player nearest the head or furthest from it as Order has it.
            for (var i = 0; i < n; i++)
            {
                var l = rows[i];
                Row(dl, l.Row, l.Value, l.Second, l.Bar, l.Id, stack.Row(arr.FromHead(i, n), rowH), rowH, g, e, tab, false);
            }
            if (lb != null && stack.Room - n * rowH + 1 >= lbH)
            {
                var y = stack.Up ? stack.NearEdge - n * rowH - lbH : stack.NearEdge + n * rowH;
                LimitBreakRow(dl, lb, y, lbH, g, stack);
            }
        }
        finally
        {
            dl.PopClipRect();
        }
    }

    /// <summary>
    /// A player's row on its plate: the bar in the theme's style and the role's colour (to the top
    /// player's, on its track), the job's icon, the name (yours a weight heavier; the dead muted with
    /// the theme's death mark), the job in the heads' capitals, the number, its second column; you in the
    /// theme's mark. Pressed, the meter opens up on them. Waiting, the number is a dash and there's no bar.
    /// </summary>
    private void Row(ImDrawListPtr dl, MeterRow r, string value, string second, float bar, string id, float y, float rowH, NormalGrid g, MeterEncounter? e, MeterTab tab, bool waiting)
    {
        var t = PlateTheme;
        var ink = NI;
        var f = NF;
        var min = new Vector2(g.PlateL, y + S(1));
        var max = new Vector2(g.PlateR, y + rowH - S(1));
        var ph = max.Y - min.Y;
        var lineCss = rowH / S(1);
        var (clicked, hovered, _) = Kit.Hit(id, min, max - min);
        if (clicked && e != null) OpenDetail?.Invoke(tab, e.Id, r.PartyId, null);
        var dead = r.Dead && !waiting;
        var you = r.IsYou;
        var youStyle = t.YouDrawn(config.MeterDecorations);
        var inv = you && youStyle == YouStyle.Invert;
        var rad = S(t.Row.Radius);
        var ink2 = Rgb.Hex(0x031005);
        // The plate (Readout's reverse video for you), pointed at, the bar, a rule (Grimoire's).
        if (t.Row.PlateAlpha > 0)
        {
            dl.AddRectFilled(min, max, Palette.U32(Rgb.A(t.Row.Plate, t.Row.PlateAlpha)), rad);
            if (t.Row.PlateEdge is { } pe) dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(pe), rad, ImDrawFlags.None, 1f);
        }
        if (inv) dl.AddRectFilled(min, max, Palette.U32(t.You), rad);
        if (hovered) dl.AddRectFilled(min, max, Palette.U32(ink.Hover), rad);
        if (!waiting && !inv) plate.BarOn(dl, min, TrackOf(g), ph, bar, r.Role, dead);
        if (t.Row.Rule is { } rule) Kit.Rule(dl, min.X, max.X, max.Y, rule);
        if (you && !inv) plate.YouMark(dl, min, max, rad, ph, youStyle);
        // From the left: the icon (past the ribbon), the name.
        var past = you && youStyle == YouStyle.Ribbon ? S(PlateMeter.PastRibbon) - S(4) : 0;
        var icon = g.Icon + past;
        PlateArt.JobIcon(dl, new Vector2(icon, y + (rowH - S(g.IconSize)) / 2), g.IconSize, r.Job, r.Role, t.RoleColor(r.Role, config.MeterRoleColours), t.Icon, dead, t.DeadIcon);
        var nameX = g.Name + past;
        // From the right: Mochi's heart, the second column, the number, the job.
        if (you && g.Heart > 0) PlateArt.Glyph(dl, PlateGlyph.Heart, new Vector2(g.SecondR + S(2), y + (rowH - S(10)) / 2), S(10), t.You);
        if (!waiting) PlateArt.LineRight(dl, g.SecondR, y, second, f.Small, inv ? ink2 : ink.Muted, lineCss);
        PlateArt.LineRight(dl, g.ValueR, y, waiting ? "–" : value, f.NumStrong, inv ? ink2 : dead ? ink.Muted : ink.Strong, lineCss);
        if (!config.MeterJobsOnly) PlateArt.Line(dl, new Vector2(g.Job, y), r.Job, f.HeadSmall, inv ? ink2 : ink.Faint, lineCss);
        var deathStyle = t.DeathDrawn(config.MeterDecorations);
        var mark = deathStyle is DeathStyle.Tag or DeathStyle.Glitch ? DeathStyle.Cross : deathStyle;
        var markW = dead ? S(mark == DeathStyle.Broken ? 13 : mark == DeathStyle.Skull ? 12 : 11) + S(5) : 0;
        var nameEnd = (config.MeterJobsOnly ? g.ValueR - g.ValueW : g.Job) - g.Gap;
        var nameFont = you && t.Id != MeterThemeId.Readout ? f.Name with { Face = MeterThemes.Heavier(f.Name.Face) } : f.Name;
        var nameText = PlateArt.Fit(config.MeterJobsOnly ? r.Job : r.Name, nameFont, MathF.Max(S(10), nameEnd - markW - nameX));
        var nameInk = inv ? ink2 : dead ? ink.Muted : ink.Strong;
        if (dead && deathStyle == DeathStyle.Glitch)
        {
            PlateArt.Line(dl, new Vector2(nameX - 1, y), nameText, nameFont, Rgb.A(t.Death, PlateMeter.GlitchAlpha), lineCss);
            PlateArt.Line(dl, new Vector2(nameX + 1, y), nameText, nameFont, Rgb.Rgba(61, 236, 255, PlateMeter.GlitchAlpha), lineCss);
        }
        var nw = PlateArt.Line(dl, new Vector2(nameX, y), nameText, nameFont, nameInk, lineCss);
        if (!dead) return;
        if (deathStyle is DeathStyle.Dagger or DeathStyle.Tag)
            dl.AddLine(new Vector2(nameX, y + rowH / 2 + S(0.5f)), new Vector2(nameX + nw, y + rowH / 2 + S(0.5f)), Palette.U32(t.Death), S(1.5f));
        PlateMeter.DeathMark(dl, nameX + nw + markW, y, rowH, mark, t.Death, f.NumStrong);
    }

    /// <summary>The limit break's row after a hairline: its mark in the theme's colour (Mochi's star), "Limit break", its number and share in the rows' columns. No plate, no bar (as compact's).</summary>
    private void LimitBreakRow(ImDrawListPtr dl, MeterLine lb, float y, float h, NormalGrid g, MeterStack stack)
    {
        var t = PlateTheme;
        var ink = NI;
        var f = NF;
        var lineCss = h / S(1);
        Kit.Rule(dl, g.PlateL + S(4), g.PlateR - S(4), stack.Toward(y, h), ink.Rule);
        var gs = S(14);
        PlateArt.Glyph(dl, t.Id == MeterThemeId.Mochi ? PlateGlyph.Star : PlateGlyph.LimitBreak, new Vector2(g.Icon + (S(g.IconSize) - gs) / 2, y + (h - gs) / 2), gs, t.Lb);
        PlateArt.LineRight(dl, g.SecondR, y, lb.Second, f.Small, ink.Muted, lineCss);
        PlateArt.LineRight(dl, g.ValueR, y, lb.Value, f.NumStrong, ink.Strong, lineCss);
        PlateArt.Line(dl, new Vector2(g.Name, y), PlateArt.Fit("Limit break", f.Name, g.ValueR - g.ValueW - g.Gap - g.Name), f.Name, ink.Text, lineCss);
    }

    // ---------------------------------------------------------------------------------------------
    // The foot.

    /// <summary>The foot (28 tall) at the far edge, on a plate for a theme whose foot sits on one: "Party · 165,000" and what's worth saying on the right, the pips before it when they're wanted there.</summary>
    private void Footer(ImDrawListPtr dl, float left, float y, float w, MeterEncounter e, MeterTab tab, bool ended, MeterStack stack)
    {
        var z = Z;
        var ink = NI;
        var f = NF;
        var h = S(z.Foot);
        FootGround(dl, left, w, y, h, stack);
        var (value, unit) = LinesOf(e, tab, config.MeterRank).Party;
        var x = left + S(z.Pad);
        var rx = left + w - S(z.Pad);
        if (previewInFoot)
        {
            FootPreviewTag(dl, rx, y, h);
            rx -= ThemeSkin.TagWidth(PlateTheme, "Preview", f.Small) + S(10);
        }
        else
        {
            var right = DateTime.UtcNow - copiedAt < CopiedFor ? "copied to the clipboard"
                : config.MeterLocked && config.MeterClickThrough ? "click-through"
                : ended ? said.KeptAs
                : tab switch
                {
                    MeterTab.Healing => "shields not counted",
                    MeterTab.Taken => "incl. bleeds",
                    _ => said.Where ?? (config.MeterRank == MeterRank.Dps ? "DPS" : "ENCDPS"),
                };
            rx -= PlateArt.LineRight(dl, rx, y, right, f.Small, ink.Muted, z.Foot) + S(10);
        }
        // The party's line in what's left (the word goes first when it doesn't fit).
        var label = PlateTheme.Id == MeterThemeId.Readout ? "party " : "Party · ";
        var lw = PlateArt.Width(label, f.Small);
        var vw = PlateArt.Width(value, f.SmallStrong);
        if (x + lw + vw <= rx) x += PlateArt.Line(dl, new Vector2(x, y), label, f.Small, ink.Muted, z.Foot);
        x += PlateArt.Line(dl, new Vector2(x, y), value, f.SmallStrong, ink.Strong, z.Foot);
        if (unit != null && x + PlateArt.Width(" " + unit, f.Small) <= rx) x += PlateArt.Line(dl, new Vector2(x, y), " " + unit, f.Small, ink.Muted, z.Foot);
        if (!previewInFoot) FootGauge(dl, x + S(12), rx, y, h, !ended);
    }

    /// <summary>The foot's ground: a hairline toward the rows, and a plate for a theme whose foot sits on one (Ready Check, Neon, Native).</summary>
    private void FootGround(ImDrawListPtr dl, float left, float w, float y, float h, MeterStack stack)
    {
        var t = PlateTheme;
        if (t.FootPlate)
        {
            var a = new Vector2(left + S(PlateInset) - S(2), y + S(3));
            var b = new Vector2(left + w - S(PlateInset) + S(2), y + h - S(3));
            dl.AddRectFilled(a, b, Palette.U32(Rgb.A(t.Row.Plate, t.Row.PlateAlpha)), S(t.Row.Radius));
        }
        else Kit.Rule(dl, left + S(PlateInset), left + w - S(PlateInset), stack.Toward(y, h), NI.Rule);
    }

    /// <summary>The limit break's pips in the foot (when they're wanted there and the fight is live), right-aligned before what the foot says on the right, when there's room.</summary>
    private void FootGauge(ImDrawListPtr dl, float from, float to, float y, float h, bool live)
    {
        if (config.MeterGauge != GaugePlace.Foot || !live || Bars(Meter) is not { Length: > 0 } bars) return;
        var gw = PipsWidth(bars.Length);
        if (to - gw < from) return;
        var gx = to - gw;
        Pips(dl, new Vector2(gx, y + h / 2), bars);
        if (Kit.Pointed(new Vector2(gx, y), new Vector2(to, y + h)) && Meter?.LimitBreak is { } g) Kit.Tooltip(MeterText.GaugeTip(g));
    }

    // ---------------------------------------------------------------------------------------------
    // Deaths, events, waiting, nothing yet, folded.

    /// <summary>
    /// The Deaths tab: each death on a plate, its icon and name, the job, its time in the theme's death
    /// colour on the right, what killed them and how big under it; pressed, its recap opens. The first
    /// nearest the head. Past the room (the foot, or the alive count after them) they're cut, and said.
    /// </summary>
    private void DrawDeaths(ImDrawListPtr dl, float left, float w, MeterStack stack, MeterEncounter e, bool live)
    {
        var z = Z;
        var ink = NI;
        var f = NF;
        var deaths = said.Deaths;
        var footY = live ? 0 : stack.Far(S(z.Foot));
        var nameH = PlateArt.Natural(f.Name);
        var rowH = DeathRowH();
        var aliveH = !live ? 0 : S(MeterFit.Alive);
        var fit = Math.Max(0, (int)MathF.Floor((stack.Room - aliveH + 1) / rowH));
        var n = Math.Min(deaths.Count, fit);
        var cut = deaths.Count - n;
        var a = MathF.Min(stack.NearEdge, stack.FarEdge);
        var b = MathF.Max(stack.NearEdge, stack.FarEdge);
        dl.PushClipRect(new Vector2(left, a), new Vector2(left + w, MathF.Max(a, b)), true);
        try
        {
            for (var i = 0; i < n; i++) DeathRow(dl, left, stack.Row(Arr.FromHead(i, n), rowH), w, rowH, nameH, e, i);
        }
        finally
        {
            dl.PopClipRect();
        }
        var listEnd = stack.Up ? stack.NearEdge - n * rowH : stack.NearEdge + n * rowH;
        if (live)
        {
            var by = stack.Up ? listEnd - aliveH : listEnd;
            var t1 = AliveLine(said.Alive, cut);
            var c = left + w / 2;
            PlateArt.Line(dl, new Vector2(c - PlateArt.Width(t1, f.Body) / 2, by + S(14)), t1, f.Body, ink.Muted, 19.5f);
            var t2 = deaths.Count > 0 ? "A death's recap opens from its row" : "No deaths yet";
            PlateArt.Line(dl, new Vector2(c - PlateArt.Width(t2, f.Note) / 2, by + S(14) + S(19.5f) + S(7.2f)), t2, f.Note, ink.Faint, 16);
            return;
        }
        if (deaths.Count == 0)
        {
            var t = said.Wipe > 0 ? "No deaths but the wipe" : "Nobody died";
            var ty = stack.Up ? listEnd - S(14) - S(19.5f) : listEnd + S(14);
            PlateArt.Line(dl, new Vector2(left + (w - PlateArt.Width(t, f.Body)) / 2, ty), t, f.Body, ink.Muted, 19.5f);
        }
        var fh = S(z.Foot);
        FootGround(dl, left, w, footY, fh, stack);
        var rx = left + w - S(z.Pad);
        if (previewInFoot)
        {
            FootPreviewTag(dl, rx, footY, fh);
            rx -= ThemeSkin.TagWidth(PlateTheme, "Preview", f.Small) + S(10);
        }
        else rx -= PlateArt.LineRight(dl, rx, footY, said.PullWord, f.Small, ink.Muted, z.Foot) + S(10);
        var x = left + S(z.Pad);
        PlateArt.Line(dl, new Vector2(x, footY), PlateArt.Fit(DeathFoot(deaths.Count, cut, said.Wipe > 0), f.Small, rx - x), f.Small, ink.Muted, z.Foot);
    }

    /// <summary>A death's row (screen px): 6 above, the name's line, 2, the note's line, 6 under, in the theme's faces.</summary>
    private float DeathRowH() => S(6) * 2 + PlateArt.Natural(NF.Name) + S(2) + PlateArt.Natural(NF.Note);

    /// <summary>A death on its plate: the icon and name, the job in the heads' capitals, its time in the theme's death colour on the right, what killed them and how big under it.</summary>
    private void DeathRow(ImDrawListPtr dl, float left, float y, float w, float h, float nameH, MeterEncounter e, int i)
    {
        var t = PlateTheme;
        var ink = NI;
        var f = NF;
        var d = said.Deaths[i];
        var min = new Vector2(left + S(PlateInset), y + S(1));
        var max = new Vector2(left + w - S(PlateInset), y + h - S(1));
        var (clicked, hovered, _) = Kit.Hit(said.DeathIds[i], min, max - min);
        var rad = S(t.Row.Radius);
        if (t.Row.PlateAlpha > 0) dl.AddRectFilled(min, max, Palette.U32(Rgb.A(t.Row.Plate, t.Row.PlateAlpha)), rad);
        if (t.Row.Rule is { } rule) Kit.Rule(dl, min.X, max.X, max.Y, rule);
        if (hovered) dl.AddRectFilled(min, max, Palette.U32(ink.Hover), rad);
        if (clicked) OpenDetail?.Invoke(MeterTab.Deaths, e.Id, d.PartyId, IndexIn(e.Deaths, d));
        var x = min.X + S(6);
        var row = RowOf(e, d.PartyId);
        var isz = (t.Icon?.Size ?? 16) + 2;
        PlateArt.JobIcon(dl, new Vector2(x, y + S(6) + (nameH - S(isz)) / 2), isz, row?.Job, row?.Role, t.RoleColor(row?.Role, config.MeterRoleColours), t.Icon, false, t.DeadIcon);
        var nx = x + S(isz) + S(8);
        var rx = max.X - S(8);
        var tw = PlateArt.LineRight(dl, rx, y + S(6), said.DeathTimes[i], f.SmallStrong, t.Death, nameH / S(1));
        var job = row?.Job ?? string.Empty;
        var name = config.MeterJobsOnly ? job : d.Name;
        var jw = config.MeterJobsOnly ? 0 : PlateArt.Width(job, f.HeadSmall) + S(5);
        var nw = PlateArt.Line(dl, new Vector2(nx, y + S(6)), PlateArt.Fit(name, f.Name, rx - tw - S(10) - jw - nx), f.Name, ink.Strong, nameH / S(1));
        if (!config.MeterJobsOnly && nx + nw + jw <= rx - tw - S(10)) PlateArt.Line(dl, new Vector2(nx + nw + S(5), y + S(6)), job, f.HeadSmall, ink.Faint, nameH / S(1));
        PlateArt.Line(dl, new Vector2(nx, y + S(6) + nameH + S(2)), PlateArt.Fit(said.DeathLines[i], f.Note, rx - nx), f.Note, ink.Muted, PlateArt.Natural(f.Note) / S(1));
    }

    /// <summary>
    /// The Events tab: the fight's events as a feed on plates, the newest nearest the head: the time,
    /// the kind's mark in the theme's colours, the member's icon and what happened, cut to fit. Pressed,
    /// the meter opens up on Events (a death, on its recap). Past the room the oldest are cut; the foot counts them by kind.
    /// </summary>
    private void DrawEvents(ImDrawListPtr dl, float left, float w, MeterStack stack, MeterEncounter e, IMeterSource m)
    {
        var z = Z;
        var ink = NI;
        var f = NF;
        var fd = Feed(m.Events(e.Id));
        var fh = S(z.Foot);
        var footY = stack.Far(fh);
        var rowH = S(MeterFit.EventRow(z));
        var n = Math.Min(fd.Newest.Count, stack.Fits(rowH));
        var arr = Arr;
        var a = MathF.Min(stack.NearEdge, stack.FarEdge);
        var b = MathF.Max(stack.NearEdge, stack.FarEdge);
        dl.PushClipRect(new Vector2(left, a), new Vector2(left + w, MathF.Max(a, b)), true);
        try
        {
            for (var i = 0; i < n; i++) EventRow(dl, fd.Newest[i], fd.Times[i], left, stack.Row(arr.FromHead(i, n), rowH), w, rowH, e, i);
            if (fd.Newest.Count == 0)
            {
                const string none = "Deaths, raises, interrupts, potions and limit breaks show here.";
                var ty = stack.Up ? stack.NearEdge - S(14) - S(19.5f) : stack.NearEdge + S(14);
                var t = PlateArt.Fit(none, f.Body, w - S(24));
                PlateArt.Line(dl, new Vector2(left + (w - PlateArt.Width(t, f.Body)) / 2, ty), t, f.Body, ink.Muted, 19.5f);
            }
        }
        finally
        {
            dl.PopClipRect();
        }
        FootGround(dl, left, w, footY, fh, stack);
        var rx = left + w - S(z.Pad);
        if (previewInFoot)
        {
            FootPreviewTag(dl, rx, footY, fh);
            rx -= ThemeSkin.TagWidth(PlateTheme, "Preview", f.Small) + S(10);
        }
        else rx -= PlateArt.LineRight(dl, rx, footY, DateTime.UtcNow - copiedAt < CopiedFor ? "copied" : said.PullWord, f.Small, ink.Muted, z.Foot) + S(10);
        var x = left + S(z.Pad);
        var lw = PlateArt.Line(dl, new Vector2(x, footY), PlateArt.Fit(fd.Foot, f.Small, MathF.Min(w * 0.62f, rx - x)), f.Small, ink.Muted, z.Foot);
        if (!previewInFoot) FootGauge(dl, x + lw + S(12), rx, footY, fh, e.Live);
    }

    /// <summary>An event's row on its plate: its time, its mark, the member's icon, what happened. Pressed, the meter opens up on it.</summary>
    private void EventRow(ImDrawListPtr dl, MeterEvent ev, string time, float left, float y, float w, float h, MeterEncounter e, int i)
    {
        var t = PlateTheme;
        var ink = NI;
        var f = NF;
        var min = new Vector2(left + S(PlateInset), y + S(1));
        var max = new Vector2(left + w - S(PlateInset), y + h - S(1));
        var (clicked, hovered, _) = Kit.Hit(EventIds[i % EventIds.Length], min, max - min);
        var rad = S(t.Row.Radius);
        if (t.Row.PlateAlpha > 0) dl.AddRectFilled(min, max, Palette.U32(Rgb.A(t.Row.Plate, t.Row.PlateAlpha)), rad);
        if (t.Row.Rule is { } rule) Kit.Rule(dl, min.X, max.X, max.Y, rule);
        if (hovered)
        {
            dl.AddRectFilled(min, max, Palette.U32(ink.Hover), rad);
            Kit.Tooltip($"{MeterText.KindWord(ev.Kind)} at {time}: {ev.Text}");
        }
        if (clicked)
        {
            var death = ev.Kind == MeterEventKind.Death ? DeathAt(e, ev) : -1;
            if (death >= 0) OpenDetail?.Invoke(MeterTab.Deaths, e.Id, ev.PartyId, death);
            else OpenDetail?.Invoke(MeterTab.Events, e.Id, ev.PartyId > 0 ? ev.PartyId : null, null);
        }
        var lineCss = h / S(1);
        var x = min.X + S(6);
        var timeW = MathF.Max(S(30), PlateArt.Width("88:88", f.Tiny));
        PlateArt.LineRight(dl, x + timeW, y, time, f.Tiny, ink.Faint, lineCss);
        x += timeW + S(8);
        var mark = S(13);
        Icons.Draw(dl, EventIcon(ev.Kind), new Vector2(x, y + (h - mark) / 2), mark, ThemeSkin.Tone(t, ink, MeterText.Tone(ev.Kind)));
        x += mark + S(7);
        if (ev.Slot.Length > 0)
        {
            var who = RowOf(e, ev.PartyId);
            var isz = t.Icon?.Size ?? 16;
            PlateArt.JobIcon(dl, new Vector2(x, y + (h - S(isz)) / 2), isz, who?.Job, who?.Role ?? Format.RoleOfSlot(ev.Slot), t.RoleColor(who?.Role ?? Format.RoleOfSlot(ev.Slot), config.MeterRoleColours),
                t.Icon, false, t.DeadIcon);
            x += S(isz) + S(6);
        }
        var room = max.X - S(8) - x;
        if (room > S(16)) PlateArt.Line(dl, new Vector2(x, y), PlateArt.Fit(ev.Text, f.Body, room), f.Body, ev.Kind == MeterEventKind.Death ? t.Bad : ink.Text, lineCss);
    }

    /// <summary>Waiting for the first hit: the clock at 0:00 with a hollow dot, the party listed on their plates (in spot order) with a dash for their number.</summary>
    private void DrawWaiting(ImDrawListPtr dl, Vector2 top, float w, float bottom, MeterEncounter? last, IMeterSource m)
    {
        var z = Z;
        var ink = NI;
        var f = NF;
        var stack = new MeterStack(top.Y, bottom, HeadBottom);
        Header(dl, top.X, ref stack, w, "0:00", false, null, TagKind.Wipe, null, m, "Waiting for the first hit", false);
        TabsRow(dl, top.X, ref stack, w);
        Columns(dl, top.X, ref stack, w, MeterTab.Damage);
        var fy = stack.Far(S(z.Foot));
        var g = GridOf(top.X, w, last, MeterTab.Damage);
        var rowH = S(z.Row);
        var rows = last != null ? said.Spots : null;
        var n = rows == null ? 0 : Math.Min(rows.Count, stack.Fits(rowH));
        var arr = Arr;
        for (var i = 0; i < n; i++) Row(dl, rows![i], "–", string.Empty, 0, WaitIds[i % WaitIds.Length], stack.Row(arr.FromHead(i, n), rowH), rowH, g, null, MeterTab.Damage, true);
        FootGround(dl, top.X, w, fy, S(z.Foot), stack);
        if (last == null) return;
        var rw = PlateArt.LineRight(dl, top.X + w - S(z.Pad), fy, said.NextPull, f.Small, ink.Muted, z.Foot);
        PlateArt.Line(dl, new Vector2(top.X + S(z.Pad), fy), PlateArt.Fit(last.Duty, f.Small, w - S(z.Pad) * 2 - rw - S(10)), f.Small, ink.Muted, z.Foot);
    }

    private static readonly string[] WaitIds = MakeIds("##mwait", 24);

    /// <summary>
    /// Before any fight, the Boss asleep, "No fight yet", one line and a quiet link to a sample fight;
    /// stopped (<see cref="IMeterSource.Problem"/>), the Boss awake, "The meter has stopped" and why.
    /// The head and the tabs on its anchored side, the Boss and its words beyond them, in the theme's
    /// faces. Returns how tall it is (the window is that tall).
    /// </summary>
    private float DrawEmpty(ImDrawListPtr dl, Vector2 top, float w, IMeterSource? m)
    {
        var t = PlateTheme;
        var z = Z;
        var ink = NI;
        var f = NF;
        var stopped = view == View.Stopped && m?.Problem != null;
        // Stopped, a kept fight can still be picked to look at.
        var pick = stopped && shown != null && m!.Kept.Count > 0 ? said.Pick : null;
        var line = stopped ? StoppedLine(m!.Problem!)
            : m == null ? "The meter starts with the plugin's reader."
            : "It starts on the first hit.";
        var title = stopped ? "The meter has stopped" : "No fight yet";
        var sample = !stopped && Preview == null && StartSample != null;
        var lines = EmptyLines(line, w - S(28));
        var lineH = lines.Count * S(19.5f);
        var titleH = S(24);
        var linkH = sample ? S(6) + S(18) + S(4) : 0;
        var messageH = S(16) + S(40) + S(7.2f) + titleH + S(7.2f) + lineH + linkH + S(18);
        var total = S(z.Head) + S(z.Tabs) + messageH;
        var stack = new MeterStack(top.Y, top.Y + total, HeadBottom);
        Header(dl, top.X, ref stack, w, "0:00", false, null, TagKind.Wipe, pick, m, null, pick != null);
        TabsRow(dl, top.X, ref stack, w);
        // The message fills what's left, whichever side the head is on.
        var y = stack.Up ? stack.FarEdge : stack.NearEdge;
        var c = new Vector2(top.X + w / 2, y + S(16) + S(20));
        Boss.Empty(dl, c, 40, stopped ? BossMood.Awake : BossMood.Asleep);
        var ty = c.Y + S(20) + S(7.2f);
        var tf = f.Title with { Px = f.Title.Px + 2 };
        var tt = PlateArt.Fit(title, tf, w - S(28));
        PlateArt.Line(dl, new Vector2(top.X + (w - PlateArt.Width(tt, tf)) / 2, ty), tt, tf, stopped ? t.Bad : ink.Strong, 24);
        ty += titleH + S(7.2f);
        foreach (var l in lines)
        {
            PlateArt.Line(dl, new Vector2(top.X + (w - PlateArt.Width(l, f.Body)) / 2, ty), l, f.Body, ink.Muted, 19.5f);
            ty += S(19.5f);
        }
        if (sample)
        {
            // "See a sample": the meter mid-fight, with made-up numbers.
            const string see = "See a sample";
            var lw = PlateArt.Width(see, f.BodyStrong);
            var lp = new Vector2(top.X + (w - lw) / 2, ty + S(6));
            var (clicked, hovered, _) = Kit.Hit("##meter-sample", lp, new Vector2(lw, S(18)));
            PlateArt.Line(dl, lp, see, f.BodyStrong, hovered ? ink.Strong : t.Link, 18);
            if (hovered) Kit.Tooltip("A made-up pull, to see the meter mid-fight. A real fight ends it.");
            if (clicked) StartSample?.Invoke();
        }
        return total;
    }

    /// <summary>The empty meter's line, wrapped (again only for other words, a width, a theme or a font build).</summary>
    private List<string> EmptyLines(string line, float width)
    {
        var f = NF.Body;
        var key = (line, MathF.Round(width), f, Txt.Fonts.Generation);
        if (emptyLines.Key != key) emptyLines = (key, Txt.Wrap(line, f.Face, f.Px, width - (f.Outline ? S(1) * 24 : 0), 3));
        return emptyLines.Lines;
    }

    private ((string?, float, ThemeFont, int) Key, List<string> Lines) emptyLines = (default, new List<string>());

    /// <summary>Folded between pulls: one line on the head's band, the theme's clock and the outcome's tag, "Pull 12 · party 163,188", and a chevron that opens it.</summary>
    private void Folded(ImDrawListPtr dl, Vector2 top, float w, MeterEncounter e)
    {
        var t = PlateTheme;
        var z = Z;
        var ink = NI;
        var f = NF;
        var h = S(z.Head);
        var cy = top.Y + h / 2;
        HeadGround(dl, top.X, w, top.Y, h);
        var right = top.X + w - S(PlateInset) - S(2);
        var ls = S(24);
        if (ThemeSkin.IconButton(dl, ink, "##meter-open", new Vector2(right - ls, cy - ls / 2), ls, Icon.ChevronDown, false, "Open it")) unfolded = true;
        var rx = right - ls - S(8);
        var x = HeadClock(dl, top.X + S(PlateInset) + S(6), top.Y, h, said.Clock, false, false, rx);
        if (MeterView.OutcomeWord(e.Ended) is { } word)
        {
            var tw = ThemeSkin.TagWidth(t, word, f.Small);
            if (x + tw <= rx) x += ThemeSkin.Tag(dl, t, ink, new Vector2(x, top.Y), h, word, f.Small, e.Ended == Core.Outcome.Clear ? (t.Light ? t.Good : ink.Strong) : t.Bad) + S(8);
        }
        var (value, _) = LinesOf(e, MeterTab.Damage, config.MeterRank).Party;
        var vw = PlateArt.Width(value, f.NumStrong);
        if (rx - vw < x) return;
        PlateArt.Line(dl, new Vector2(rx - vw, top.Y), value, f.NumStrong, ink.Strong, z.Head);
        var lw = PlateArt.Width(said.FoldLabel, f.Body);
        if (rx - vw - lw > x) PlateArt.Line(dl, new Vector2(rx - vw - lw, top.Y), said.FoldLabel, f.Body, ink.Muted, z.Head);
    }

    // ---------------------------------------------------------------------------------------------
    // The menus: the picker, the tabs' (a narrow meter's), in the theme.

    /// <summary>Opens a menu in the theme: its panel and chip line (Ready Check's the kit's raised slab).</summary>
    private bool BeginThemedMenu(string id)
    {
        var t = PlateTheme;
        return t.Id == MeterThemeId.ReadyCheck ? Kit.BeginMenu(id) : Kit.BeginMenu(id, NI.Panel, NI.Chip, MathF.Min(t.Win.Radius, 12));
    }

    /// <summary>
    /// The tab menu (200 wide; the tab row's place in a meter too narrow for the five): the five, each with
    /// the opened-up window's icon, the one you're on ticked in the accent, the deaths and events counted.
    /// </summary>
    private void DrawTabMenu(float buttonLeft, bool hasFight)
    {
        var mw = S(TabMenuWidth);
        ImGui.SetNextWindowSizeConstraints(new Vector2(mw, 0), new Vector2(mw, S(400)));
        PlaceMenu(buttonLeft, mw, tabMenuH > 0 ? tabMenuH : S(MeterLayout.TabOrder.Length * 34 + 11.2f));
        if (!BeginThemedMenu(TabMenu)) return;
        try
        {
            var w = mw - S(11.2f);
            var dl = ImGui.GetWindowDrawList();
            var ink = NI;
            var f = NF;
            foreach (var t in MeterLayout.TabOrder)
            {
                var on = t == config.MeterTab;
                var count = !hasFight ? null : t == MeterTab.Deaths ? said.DeathCount : t == MeterTab.Events ? EventCount() : null;
                var reserve = on ? S(16) + S(8) : count != null ? PlateArt.Width(count, f.Small) + S(8) : 0;
                var (clicked, min, max) = ThemeSkin.MenuRow(dl, ink, TabIds[(int)t], w, MeterLayout.TabWord(t), null, f.BodyStrong, f.Note, reserve);
                var (slot, right) = ThemeSkin.MenuParts(min, max);
                Icons.Draw(dl, TabIcons[(int)t], slot + new Vector2((S(16) - S(15)) / 2), S(15), on ? ink.Strong : ink.Muted);
                if (on) Icons.Draw(dl, Icon.Check, new Vector2(right - S(15), (min.Y + max.Y) / 2 - S(7.5f)), S(15), ink.Accent);
                else if (count != null) PlateArt.LineRight(dl, right, min.Y, count, f.Small, ink.Faint, (max.Y - min.Y) / S(1));
                if (!clicked) continue;
                if (!on)
                {
                    config.MeterTab = t;
                    config.Save();
                }
                ImGui.CloseCurrentPopup();
            }
            tabMenuH = ImGui.GetWindowHeight();
        }
        finally
        {
            Kit.EndMenu();
        }
    }

    /// <summary>
    /// The picker (300 wide), in the theme: tonight's fights the meter kept, newest first, the one shown
    /// ticked in the accent, the live one tagged Now, the best starred; the other duties' after; Copy as
    /// text; and at its foot Pace against (Best, Last, Off) and Compact. With a sample on, Stop the sample first.
    /// </summary>
    private void DrawPicker(IMeterSource m, float buttonLeft)
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(S(260), 0), new Vector2(S(PickerWidth), S(460)));
        PlaceMenu(buttonLeft, S(PickerWidth), pickerH > 0 ? pickerH : S(300));
        if (!BeginThemedMenu(Picker)) return;
        try
        {
            var w = S(PickerWidth) - S(11.2f);
            var dl = ImGui.GetWindowDrawList();
            var t = PlateTheme;
            var ink = NI;
            var f = NF;
            if (Preview != null)
            {
                var (stop, min, max) = ThemeSkin.MenuRow(dl, ink, "##pick-stop", w, "Stop the sample", "Its numbers are made up", f.BodyStrong, f.Note, 0);
                var (slot, _) = ThemeSkin.MenuParts(min, max);
                Icons.Draw(dl, Icon.X, slot + new Vector2((S(16) - S(15)) / 2), S(15), ink.Muted);
                if (stop)
                {
                    Preview = null;
                    ImGui.CloseCurrentPopup();
                }
                ThemeSkin.MenuRule(dl, ink, w);
            }
            var current = picked is { } id ? m.Get(id) : m.Current;
            var (here, others) = MeterView.Picker(m.Kept, current, current != null ? m.Best(current.Content) : null);
            if (here.Count > 0)
            {
                ThemeSkin.MenuEyebrow(dl, ink, w, $"{(DateTime.Now.Hour >= 17 ? "Tonight" : "Today")} · {here[0].Fight.Duty}", f.Head);
                foreach (var r in here) PickRow(dl, r, w);
            }
            if (others.Count > 0)
            {
                ThemeSkin.MenuEyebrow(dl, ink, w, "Other duties", f.Head);
                foreach (var r in others) PickRow(dl, r, w, r.Fight.Duty);
            }
            ThemeSkin.MenuRule(dl, ink, w);
            // Copy as text: a mini-parse of the fight shown, for chat (as ACT's).
            if (current != null)
            {
                var (copy, cmin, cmax) = ThemeSkin.MenuRow(dl, ink, "##pick-copy", w, "Copy as text", $"Pull {current.Pull}: each player's ENCDPS, share and deaths", f.BodyStrong, f.Note, 0);
                var (cslot, _) = ThemeSkin.MenuParts(cmin, cmax);
                Icons.Draw(dl, Icon.Copy, cslot + new Vector2((S(16) - S(15)) / 2), S(15), ink.Muted);
                if (copy)
                {
                    CopyParse(current);
                    ImGui.CloseCurrentPopup();
                }
                ThemeSkin.MenuRule(dl, ink, w);
            }
            var pos = ImGui.GetCursorScreenPos();
            var lineH = S(28);
            var gap = S(4);
            var h = S(5.6f) + lineH + gap + lineH + S(7.2f);
            var y = pos.Y + S(5.6f);
            PlateArt.Line(dl, new Vector2(pos.X + S(8.8f), y), "Pace against", f.Body, ink.Muted, 28);
            var sw = ThemeSkin.SegmentsWidth(PaceItems, f.BodyStrong);
            var pace = ThemeSkin.Segments(dl, t, ink, "##pick-pace", new Vector2(pos.X + w - S(8.8f) - sw, y), lineH, PaceItems, (int)config.MeterPace, f.BodyStrong);
            if (pace != (int)config.MeterPace)
            {
                config.MeterPace = (PaceAgainst)pace;
                config.Save();
            }
            // Compact, as Settings has it (the compact meter, in the same theme).
            y += lineH + gap;
            PlateArt.Line(dl, new Vector2(pos.X + S(8.8f), y), "Compact", f.Body, ink.Muted, 28);
            var size = Kit.SwitchSize;
            if (ThemeSkin.Switch(dl, ink, "##pick-compact", new Vector2(pos.X + w - S(8.8f) - size.X, y + (lineH - size.Y) / 2), config.MeterCompact))
            {
                config.MeterCompact = !config.MeterCompact;
                config.Save();
            }
            ImGui.Dummy(new Vector2(w, h));
            pickerH = ImGui.GetWindowHeight();
        }
        finally
        {
            Kit.EndMenu();
        }
    }

    /// <summary>A pull in the picker: "Pull 12" (the best's said), its duty when another, its tag (Now, Wipe, Clear) and length on the right; ticked in the accent when shown, starred when the best.</summary>
    private void PickRow(ImDrawListPtr dl, PickerRow r, float w, string? sub = null)
    {
        var t = PlateTheme;
        var ink = NI;
        var f = NF;
        var fight = r.Fight;
        var title = r.Best ? $"Pull {fight.Pull} · the best" : $"Pull {fight.Pull}";
        var tag = r.Live ? "Now" : MeterView.OutcomeWord(fight.Ended);
        var tagColor = r.Live ? t.Accent : fight.Ended == Core.Outcome.Clear ? (t.Light ? t.Good : ink.Strong) : t.Bad;
        var time = Format.Clock(fight.Duration);
        var tagW = tag != null ? ThemeSkin.TagWidth(t, tag, f.Small) : 0;
        var timeW = PlateArt.Width(time, f.Small);
        var reserve = tagW + S(8) + timeW + S(8);
        var (clicked, min, max) = ThemeSkin.MenuRow(dl, ink, "##pick" + fight.Id, w, title, sub, f.BodyStrong, f.Note, reserve);
        var (slot, right) = ThemeSkin.MenuParts(min, max);
        if (r.Shown) Icons.Draw(dl, Icon.Check, slot + new Vector2((S(16) - S(15)) / 2), S(15), ink.Accent);
        else if (r.Best) Icons.Draw(dl, Icon.Star, slot + new Vector2((S(16) - S(13)) / 2), S(13), ink.Accent);
        if (tag != null) ThemeSkin.Tag(dl, t, ink, new Vector2(right - tagW, min.Y), max.Y - min.Y, tag, f.Small, tagColor);
        PlateArt.LineRight(dl, right - (tag != null ? tagW + S(8) : 0), min.Y, time, f.Small, ink.Faint, (max.Y - min.Y) / S(1));
        if (clicked)
        {
            picked = r.Live ? null : fight.Id;
            unfolded = true;
            ImGui.CloseCurrentPopup();
        }
    }
}
