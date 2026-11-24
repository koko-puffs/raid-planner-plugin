using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>What the person did to the compact meter this frame (it draws; its owner acts).</summary>
internal struct PlateInput
{
    /// <summary>The head's menu button ("DPS ⌄") pressed; its left edge, for the menu to open off.</summary>
    public bool Menu;

    public float MenuLeft;

    public bool Lock;

    /// <summary>A row pressed (its member), or −1.</summary>
    public int Pressed;

    /// <summary>A row pointed at (its member), or −1, and the row's rectangle (for the details panel).</summary>
    public int Pointed;

    public Vector2 PointedMin;

    public Vector2 PointedMax;

    /// <summary>A death or an event pressed (Deaths, Events): its index in the list drawn, or −1.</summary>
    public int Item;

    public bool Preview;

    /// <summary>The one line's chevron pressed: open it again.</summary>
    public bool Open;

    /// <summary>The empty meter's "See a sample" pressed.</summary>
    public bool Sample;
}

/// <summary>
/// The compact meter (since 0.7.0; round four's variant A, "Plate"), in any of the six themes
/// (<see cref="MeterTheme"/>): one head (the clock with its dot, the phase in a chip, the limit
/// break's pips, the tab menu, the lock), the top eight on plates (the bar in the role's colour, the
/// job icon, the name, three-figure numbers in fixed columns; you marked, and pinned at the foot with
/// your rank when you're below the last row shown; a death's mark and how long they've been down), the
/// limit break's row, the foot (the party and pace); the one-line collapsed meter; the details panel.
/// Its words are made once a snapshot (<see cref="Update"/>), the rows slide and the bars ease
/// between snapshots, and ranks swap only once a lead has held a second (<see cref="RankHold"/>).
/// Drawn by the meter's window and by Settings' preview, from the same fight model.
/// </summary>
internal sealed class PlateMeter
{
    /// <summary>A row as drawn: its member, its words, its bar's length (to the top's), its rank, pinned, how long they've been down.</summary>
    public sealed class Line
    {
        public MeterRow Row = null!;
        public string Value = string.Empty;
        public string Share = string.Empty;
        public string RankText = string.Empty;
        public string PinnedRank = string.Empty;
        public string? DeadFor;
        public string Id = string.Empty;
        public float Target;

        /// <summary>Healing's striped part after the bar (since 0.9.0: the barriers it put on that soaked damage), 0–1 of the track.</summary>
        public float Striped;

        public int Rank;
    }

    /// <summary>A death on the Deaths tab, or an event on Events, as drawn.</summary>
    public sealed class Item
    {
        public string Time = string.Empty;
        public string Text = string.Empty;
        public string Name = string.Empty;
        public string? Job;
        public string? Role;
        public MeterEventKind Kind;
        public int PartyId;
        public string Id = string.Empty;
    }

    // What it's drawn with (set by its owner each frame).
    public MeterTheme T = MeterThemes.ReadyCheck;
    public bool Decorations = true;
    public MeterRoleColours RoleSet;
    public bool Whole;
    public bool JobsOnly;

    /// <summary>Where its head sits and the rows' order (since 0.7.4; which way it grows is its window's alone).</summary>
    public MeterArrange Arrange = MeterArrange.Default;

    /// <summary>The head at the bottom edge: everything else the other way up from it (the foot at the top).</summary>
    private bool Up => Arrange.HeadAtBottom;
    public float Background = 0.88f;
    public bool Locked;
    public bool ClickThrough;
    public bool Interactive = true;
    public bool PreviewOn;

    /// <summary>What the foot says on the right instead of pace ("copied", "click-through"), or null.</summary>
    public string? FootNote;

    /// <summary>The limit break's pips (bars full, the one filling), or null: none in the head.</summary>
    public float[]? Pips;

    /// <summary>Pace's words ("+3.4%") and whether ahead, or null.</summary>
    public string? Pace;

    public bool PaceAhead;

    public string? PaceTip;

    // The fight's words, made once a snapshot.
    private MeterEncounter? of;
    private MeterTab tabOf;
    private MeterRank rankOf;
    private bool wholeOf;
    private int fightId = -1;
    private readonly RankHold hold = new();
    private readonly List<Line> pool = new();
    private readonly List<Line> lines = new();
    private readonly List<int> ids = new();
    private readonly List<double> values = new();
    private readonly List<PlateSlot> slots = new();
    private readonly Dictionary<int, Line> byId = new();
    private Line? lb;
    private string clock = "0:00";
    private (string Chip, string Short, string Term, string Long)? phase;
    private string partyText = string.Empty;
    private string? partyUnit;
    private string keptAs = string.Empty;
    private string youRank = string.Empty;
    private string youValue = string.Empty;
    private string deaths = string.Empty;
    private int deathCount;
    private string tabWord = "DPS";
    private string title = "Damage";
    private bool live;
    private Core.Outcome? ended;
    private readonly List<Item> items = new();
    private readonly List<Item> itemPool = new();
    private string itemsFoot = string.Empty;

    /// <summary>Counts the rows' words made (<see cref="Update"/>): the widest number is measured again for new ones.</summary>
    private int linesVersion;

    // The number columns as drawn (screen px; PlateColumns): measured in the faces actually drawn, again
    // for another theme, scale, Whole or font build (kept only once the faces are built: before, a stand-in is measured).
    private (MeterThemeId Id, bool Whole, float Scale, int Fonts) columnsOf = (MeterThemeId.ReadyCheck, false, -1, -1);
    private int columnsVersion;
    private float valueCol;
    private float shareCol;

    /// <summary>The widest number among the rows' (a whole number past the samples: Taken's millions), for their words and the columns measured.</summary>
    private (int Lines, int Columns, float Width) widest = (-1, -1, 0);
    private IReadOnlyList<MeterEvent>? eventsOf;
    private int eventsCount = -1;

    // Motion between snapshots: each row's place (in rows) and bar, eased; deaths seen (for the flash).
    private readonly Dictionary<int, float> shownSlot = new();
    private readonly Dictionary<int, float> shownBar = new();
    private readonly Dictionary<int, double> diedSeen = new();
    private readonly Dictionary<int, bool> wasDead = new();

    /// <summary>The tab shown.</summary>
    public MeterTab Tab => tabOf;

    /// <summary>The rows in the order shown (held), for the details panel's rank.</summary>
    public IReadOnlyList<Line> Lines => lines;

    public int DeathCount => deathCount;

    private static readonly string[] RankTexts = MakeRanks();

    private static string[] MakeRanks()
    {
        var r = new string[25];
        for (var i = 0; i < r.Length; i++) r[i] = i.ToString("00", CultureInfo.InvariantCulture);
        return r;
    }

    private static readonly string[] PinnedTexts = MakePinned();

    private static string[] MakePinned()
    {
        var r = new string[25];
        for (var i = 0; i < r.Length; i++) r[i] = "#" + i.ToString(CultureInfo.InvariantCulture);
        return r;
    }

    // ---------------------------------------------------------------------------------------------
    // The words, once a snapshot.

    /// <summary>
    /// Works out the rows and words for a fight and tab, once a snapshot (again only when the source
    /// gives a new one, or the tab, the number or whole numbers change). <paramref name="now"/> is
    /// seconds on a steady clock: a lead must hold for a second of it before two rows swap.
    /// </summary>
    public void Update(MeterEncounter e, IMeterSource? m, MeterTab tab, MeterRank rank, bool whole, double now)
    {
        if (tab == MeterTab.Events && m != null)
        {
            var evs = m.Events(e.Id);
            if (!ReferenceEquals(evs, eventsOf) || evs.Count != eventsCount) of = null;
        }
        if (ReferenceEquals(of, e) && tabOf == tab && rankOf == rank && wholeOf == whole) return;
        var again = e.Id != fightId || tab != tabOf || rank != rankOf;
        of = e;
        tabOf = tab;
        rankOf = rank;
        wholeOf = whole;
        live = e.Live;
        ended = e.Ended;
        if (again)
        {
            hold.Reset();
            shownSlot.Clear();
            shownBar.Clear();
            if (e.Id != fightId)
            {
                diedSeen.Clear();
                wasDead.Clear();
            }
        }
        fightId = e.Id;
        clock = Format.Clock(e.Duration);
        phase = PlateWords.Phase(e.Phase, e.Duration - e.PhaseAt);
        tabWord = PlateWords.TabWord(tab);
        title = MeterLayout.TabWord(tab);
        keptAs = "kept as pull " + e.Pull.ToString(CultureInfo.InvariantCulture);

        // The rows, ranked for the tab (the bars and numbers' tab: Deaths and Events rank by damage, for the one line).
        var numbers = tab is MeterTab.Deaths or MeterTab.Events ? MeterTab.Damage : tab;
        var ranked = MeterView.Ranked(e.Rows, numbers, rank);
        var top = ranked.Count > 0 ? MeterView.Value(ranked[0], numbers, rank) : 0;
        ids.Clear();
        values.Clear();
        byId.Clear();
        foreach (var r in ranked)
        {
            ids.Add(r.PartyId);
            values.Add(MeterView.Value(r, numbers, rank));
        }
        // Live, a lead holds a second before the rows swap; a kept fight is in its true order.
        IReadOnlyList<int> order = e.Live ? hold.Update(ids, values, now) : ids;
        lines.Clear();
        for (var i = 0; i < order.Count; i++)
        {
            MeterRow? row = null;
            foreach (var r in ranked)
                if (r.PartyId == order[i])
                {
                    row = r;
                    break;
                }
            if (row == null) continue;
            var l = i < pool.Count ? pool[i] : Add(pool);
            l.Row = row;
            var v = MeterView.Value(row, numbers, rank);
            l.Value = MeterNumber.Text(v, whole);
            l.Share = MeterView.Second(row, numbers);
            l.Target = top > 0 ? (float)Math.Clamp(v / top, 0, 1) : 0;
            l.Striped = 0;
            if (numbers == MeterTab.Healing)
            {
                // Healing that landed, then (striped) the barriers that soaked damage, against the most of the two together.
                var healTop = MeterView.HealingTop(ranked);
                l.Target = MeterView.HealBar(row, healTop);
                l.Striped = MeterView.ShieldBar(row, healTop);
            }
            l.Rank = i + 1;
            l.RankText = RankTexts[Math.Min(i + 1, RankTexts.Length - 1)];
            l.PinnedRank = PinnedTexts[Math.Min(i + 1, PinnedTexts.Length - 1)];
            l.Id = "##pm" + row.PartyId.ToString(CultureInfo.InvariantCulture);
            l.DeadFor = row.Dead && row.DiedAt is { } d ? PlateWords.DeadFor(d, e.Duration) : null;
            if (row.Dead && e.Live && !wasDead.GetValueOrDefault(row.PartyId)) diedSeen[row.PartyId] = now;
            wasDead[row.PartyId] = row.Dead;
            lines.Add(l);
            byId[row.PartyId] = l;
        }
        lb = null;
        if (numbers == MeterTab.Damage && MeterView.LimitBreak(e.Rows) is { } lbr)
        {
            lb = pool.Count > order.Count ? pool[order.Count] : Add(pool);
            lb.Row = lbr;
            lb.Value = MeterNumber.Text(lbr.EncDps, whole);
            lb.Share = Format.Share(lbr.DamageShare);
            lb.Target = top > 0 ? (float)Math.Clamp(lbr.EncDps / top, 0, 1) : 0;
            lb.Id = "##pm-lb";
        }
        var (pv, unit) = numbers switch
        {
            MeterTab.Healing => (e.Party.Hps, "HPS"),
            MeterTab.Taken => (e.Party.Taken, (string?)null),
            _ => (rank == MeterRank.Dps ? e.Party.Dps : e.Party.EncDps, (string?)null),
        };
        partyText = MeterNumber.Text(pv, whole);
        partyUnit = unit;
        linesVersion++;
        youRank = string.Empty;
        youValue = string.Empty;
        foreach (var l in lines)
            if (l.Row.IsYou)
            {
                youRank = l.PinnedRank;
                youValue = l.Value;
            }
        deathCount = 0;
        foreach (var d in e.Deaths)
            if (!d.PartOfWipe)
                deathCount++;
        deaths = deathCount.ToString(CultureInfo.InvariantCulture);

        // Deaths and Events: their lines.
        items.Clear();
        if (tab == MeterTab.Deaths)
        {
            var list = MeterView.Deaths(e);
            for (var i = 0; i < list.Count; i++)
            {
                var d = list[i];
                var it = i < itemPool.Count ? itemPool[i] : Add(itemPool);
                var r = RowOf(e, d.PartyId);
                it.Time = Format.Clock(d.At);
                it.Text = MeterText.DeathLine(d);
                it.Name = d.Name;
                it.Job = r?.Job;
                it.Role = r?.Role;
                it.Kind = MeterEventKind.Death;
                it.PartyId = d.PartyId;
                it.Id = "##pd" + i.ToString(CultureInfo.InvariantCulture);
                items.Add(it);
            }
            itemsFoot = deathCount == 1 ? "1 death" : deaths + " deaths";
        }
        else if (tab == MeterTab.Events && m != null)
        {
            var evs = m.Events(e.Id);
            eventsOf = evs;
            eventsCount = evs.Count;
            var newest = MeterText.Newest(evs, 24);
            for (var i = 0; i < newest.Count; i++)
            {
                var ev = newest[i];
                var it = i < itemPool.Count ? itemPool[i] : Add(itemPool);
                var r = ev.PartyId > 0 ? RowOf(e, ev.PartyId) : null;
                it.Time = Format.Clock(ev.At);
                it.Text = ev.Text;
                it.Name = r?.Name ?? string.Empty;
                it.Job = r?.Job;
                it.Role = r?.Role;
                it.Kind = ev.Kind;
                it.PartyId = ev.PartyId;
                it.Id = "##pe" + i.ToString(CultureInfo.InvariantCulture);
                items.Add(it);
            }
            itemsFoot = evs.Count == 0 ? "No events yet" : MeterText.Counts(evs, 2);
        }
    }

    private static T Add<T>(List<T> list)
        where T : new()
    {
        var t = new T();
        list.Add(t);
        return t;
    }

    private static MeterRow? RowOf(MeterEncounter e, int partyId)
    {
        foreach (var r in e.Rows)
            if (r.PartyId == partyId && !r.IsLimitBreak)
                return r;
        return null;
    }

    /// <summary>The death or event drawn at <paramref name="index"/> (Deaths, Events), or null.</summary>
    public Item? ItemAt(int index) => index >= 0 && index < items.Count ? items[index] : null;

    /// <summary>The row of a member, or null.</summary>
    public Line? LineOf(int partyId) => byId.TryGetValue(partyId, out var l) ? l : null;

    // ---------------------------------------------------------------------------------------------
    // The number columns.

    /// <summary>The number and share columns' widths (screen px) for the theme, the scale and Whole, measured once a font build.</summary>
    private void Columns()
    {
        var t = T;
        var key = (t.Id, Whole, S(1), Txt.Fonts.Generation);
        if (key == columnsOf) return;
        valueCol = PlateColumns.Value(t, Whole, S(1), PlateArt.Width);
        shareCol = PlateColumns.Share(t, S(1), PlateArt.Width);
        columnsVersion++;
        if (Txt.Ready(t.Font.Num.Face, t.Font.Num.Px) && Txt.Ready(t.Font.Small.Face, t.Font.Small.Px)) columnsOf = key;
    }

    /// <summary>The number column (screen px): the measured samples', or the widest of the rows' own numbers when one is wider.</summary>
    private float ValueColumn()
    {
        Columns();
        if (widest.Lines != linesVersion || widest.Columns != columnsVersion)
        {
            var w = 0f;
            foreach (var l in lines) w = MathF.Max(w, PlateArt.Width(l.Value, T.Font.Num));
            widest = (linesVersion, columnsVersion, w);
        }
        return MathF.Max(valueCol, widest.Width);
    }

    /// <summary>The share column (screen px), as measured.</summary>
    private float ShareColumn()
    {
        Columns();
        return shareCol;
    }

    // ---------------------------------------------------------------------------------------------
    // Sizes.

    /// <summary>How tall the fight's meter is for its rows now (CSS px): at most eight and the limit break, as <see cref="PlateSizes.Needed"/>.</summary>
    public float Needed(int party, bool waiting = false)
    {
        if (tabOf is MeterTab.Deaths or MeterTab.Events && !waiting)
        {
            var n = Math.Clamp(tabOf == MeterTab.Deaths ? Math.Max(items.Count, 1) : Math.Max(Math.Min(items.Count, PlateRows.Max), 1), 1, PlateRows.Max);
            return PlateSizes.Needed(T, Math.Max(n, Math.Min(party, PlateRows.Max)), false);
        }
        return PlateSizes.Needed(T, Math.Min(party, PlateRows.Max), lb != null && !waiting);
    }

    // ---------------------------------------------------------------------------------------------
    // Drawing: the window's ground and frame.

    private Vector4 Role(string? role) => T.RoleColor(role, RoleSet);

    /// <summary>The theme's ground and frame over a box (the meter's window, the one line, the details panel).</summary>
    public void Ground(ImDrawListPtr dl, Vector2 min, Vector2 max, bool sparkles = true)
    {
        var w = T.Win;
        var a = T.Alpha(Background);
        var r = S(w.Radius);
        dl.PushClipRectFullScreen();
        try
        {
            if (w.Drop is { } drop)
                dl.AddRectFilled(min + S(1) * w.DropOffset, max + S(1) * w.DropOffset, Palette.U32(drop), r);
            for (var i = w.Rings.Count - 1; i >= 0; i--)
            {
                var ring = w.Rings[i];
                var o = S(ring.Px) + S(w.EdgeWidth);
                dl.AddRect(min - new Vector2(o - S(1)), max + new Vector2(o - S(1)), Palette.U32(ring.Color), r + o, ImDrawFlags.None, S(ring.Px > 2 ? 3 : 2));
            }
            if (w.GradTop is { } gt && w.GradBottom is { } gb) PlateArt.GradientRect(dl, min, max, Rgb.A(gt, a), Rgb.A(gb, a), r);
            else dl.AddRectFilled(min, max, Palette.U32(Rgb.A(w.Bg, a)), r);
            // The edge (and an outer line round a sticker's white edge).
            var ew = MathF.Max(1, S(w.EdgeWidth));
            if (w.Outer is { } outer) dl.AddRect(min - new Vector2(ew + 0.5f), max + new Vector2(ew + 0.5f), Palette.U32(outer), r + ew, ImDrawFlags.None, 1f);
            dl.AddRect(min - new Vector2(ew / 2 - 0.5f), max + new Vector2(ew / 2 - 0.5f), Palette.U32(w.Edge), r + ew / 2, ImDrawFlags.None, ew);
            if (w.Inner is { } inner)
            {
                var ins = S(w.InnerInset);
                dl.AddRect(min + new Vector2(ins + 0.5f), max - new Vector2(ins + 0.5f), Palette.U32(inner), MathF.Max(0, r - ins), ImDrawFlags.None, 1f);
                if (T.Id == MeterThemeId.Native)
                    dl.AddLine(new Vector2(min.X + S(3), min.Y + S(2) + 0.5f), new Vector2(max.X - S(3), min.Y + S(2) + 0.5f), Palette.U32(Rgb.Rgba(255, 255, 255, 0.14f)), 1f);
            }
            var orn = T.Drawn(Decorations);
            if ((orn & Ornaments.Pips) != 0)
            {
                // Grimoire's gilt pips at the frame's corners.
                var p = S(4);
                for (var k = 0; k < 4; k++)
                {
                    var c = new Vector2(k % 2 == 0 ? min.X + p : max.X - p, k < 2 ? min.Y + p : max.Y - p);
                    var s = S(6.2f);
                    PlateArt.Glyph(dl, PlateGlyph.Diamond, c - new Vector2(s / 2 + S(1)), s + S(2), Rgb.Hex(0x4a3020));
                    PlateArt.Glyph(dl, PlateGlyph.Diamond, c - new Vector2(s / 2), s, Rgb.Hex(0xb08d4f));
                }
            }
            if ((orn & Ornaments.Sparkles) != 0 && sparkles)
            {
                PlateArt.Glyph(dl, PlateGlyph.Star, new Vector2(min.X - S(9), min.Y - S(8)), S(15), Rgb.Hex(0xffc94d));
                PlateArt.Glyph(dl, PlateGlyph.Star, new Vector2(min.X + S(3), min.Y - S(11)), S(8), Rgb.Hex(0xff9ec7));
                PlateArt.Glyph(dl, PlateGlyph.Star, new Vector2(max.X - S(4), max.Y - S(3)), S(12), Rgb.Hex(0x9fe6cf));
            }
        }
        finally
        {
            dl.PopClipRect();
        }
    }

    /// <summary>Readout's scanlines over the box (every third pixel a dark line), drawn last.</summary>
    public void Scanlines(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        if ((T.Drawn(Decorations) & Ornaments.Scanlines) == 0) return;
        var col = Palette.U32(Rgb.Rgba(0, 0, 0, 0.28f));
        var step = MathF.Max(3, S(3));
        for (var y = min.Y + 1 + step - 1; y < max.Y - 1; y += step) dl.AddLine(new Vector2(min.X + 1, MathF.Floor(y) + 0.5f), new Vector2(max.X - 1, MathF.Floor(y) + 0.5f), col, 1f);
    }

    // ---------------------------------------------------------------------------------------------
    // The fight.

    /// <summary>
    /// The fight's meter in a box <paramref name="w"/> by <paramref name="h"/> (the window's content):
    /// as many rows as fit (at most eight; you pinned last when you're below them), the head at the
    /// edge <see cref="Arrange"/> puts it at (at the bottom, everything else the other way up from it), the
    /// rows in its order (the top player at the top or at the bottom of the list; you pinned at the end away from them).
    /// </summary>
    public PlateInput DrawFight(ImDrawListPtr dl, Vector2 min, float w, float h, bool waiting)
    {
        var input = new PlateInput { Pressed = -1, Pointed = -1, Item = -1 };
        var t = T;
        var max = min + new Vector2(w, h);
        Ground(dl, min, max);
        var pad = S(t.Win.Pad);
        var x0 = min.X + pad;
        var iw = w - pad * 2;
        var headH = S(t.Win.HeadH);
        // Mirrored with the head at the bottom: a band at y (down from the head's edge) is drawn at its mirror.
        float Y(float y, float hh) => Up ? max.Y - y - hh : min.Y + y;

        if (t.HeadPlates) Plate(dl, new Vector2(x0, Y(pad - S(1), headH - pad + S(1))), iw, headH - pad + S(1), t.Row.Plate, t.Row.PlateAlpha);
        Head(dl, min, w, Y(0, headH), waiting, ref input);

        var y = headH + S(t.Win.HeadGap);
        var step = S(PlateSizes.RowStep(t));
        var rh = S(t.Row.H);
        var lbOn = lb != null && !waiting && tabOf != MeterTab.Deaths && tabOf != MeterTab.Events;
        var footFrom = 0f;
        if (tabOf is MeterTab.Deaths or MeterTab.Events && !waiting)
        {
            var room = PlateSizes.RowsIn(t, h / S(1), false);
            var n = Math.Min(items.Count, room);
            // The first death or the newest event where the top player would be (Order).
            for (var i = 0; i < n; i++) ItemRow(dl, items[i], x0, Y(y + Arrange.FromHead(i, n) * step, rh), iw, rh, i, ref input);
            y += n * step;
            if (items.Count == 0)
            {
                var none = tabOf == MeterTab.Deaths ? (ended == Core.Outcome.Wipe ? "No deaths but the wipe" : "Nobody died") : "Deaths, raises, interrupts and potions show here.";
                var nt = PlateArt.Fit(none, t.Font.Label, iw - S(12));
                PlateArt.Line(dl, new Vector2(x0 + (iw - PlateArt.Width(nt, t.Font.Label)) / 2, Y(y, rh)), nt, t.Font.Label, t.Row.Muted, t.Row.H);
                y += step;
            }
            footFrom = y + S(1);
            y += S(1);
            if (t.FootPlate) Plate(dl, new Vector2(x0, Y(footFrom, S(19))), iw, S(19), t.Row.Plate, t.Row.PlateAlpha);
            FootLine(dl, x0, iw, Y(y, S(18)), itemsFoot, null, ref input);
            Scanlines(dl, min, max);
            return input;
        }

        // The rows: as many as fit, you pinned last when below them.
        var you = -1;
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Row.IsYou)
                you = i;
        var fit = PlateSizes.RowsIn(t, h / S(1), lbOn);
        PlateRows.Select(lines.Count, you, fit, slots);
        if (slots.Count > 0 && slots[^1].Pinned && PlateSizes.Needed(t, slots.Count, lbOn, true) > h / S(1) + 0.5f && fit > 1)
            PlateRows.Select(lines.Count, you, fit - 1, slots);
        var rowsTop = y;
        // Each row's place from the head's side, as Order has it; you pinned at the end away from the top player,
        // the dashed rule between you and the rest (the rows past it moved by its room).
        var count = slots.Count;
        var pinned = count > 0 && slots[^1].Pinned;
        var boundary = Arrange.PinBoundary(count);
        var dt = ImGui.GetIO().DeltaTime;
        var still = ReducedMotion;
        var clipA = Up ? Y(rowsTop + slots.Count * step + S(PlateSizes.PinRule), 0) : Y(rowsTop, 0);
        var clipB = Up ? Y(rowsTop, 0) : Y(rowsTop + slots.Count * step + S(PlateSizes.PinRule), 0);
        dl.PushClipRect(new Vector2(min.X, MathF.Min(clipA, clipB) - S(4)), new Vector2(max.X, MathF.Max(clipA, clipB) + S(4)), true);
        try
        {
            for (var k = 0; k < slots.Count; k++)
            {
                var slot = slots[k];
                var l = lines[slot.Index];
                var id = l.Row.PartyId;
                // The rows slide to their places (160ms a place) and the bars ease (250ms).
                var place = Arrange.FromHead(k, count);
                var target = (float)place;
                var at = slot.Pinned || still || !shownSlot.TryGetValue(id, out var s0) ? target : Toward(s0, target, MathF.Max(1, MathF.Abs(target - s0)) / 0.16f * dt);
                shownSlot[id] = at;
                var bar = still || !shownBar.TryGetValue(id, out var b0) ? l.Target : Toward(b0, l.Target, MathF.Max(0.05f, MathF.Abs(l.Target - b0)) / 0.25f * dt);
                shownBar[id] = bar;
                var ry = rowsTop + at * step + (pinned && place >= boundary ? S(PlateSizes.PinRule) : 0);
                if (slot.Pinned)
                {
                    PlateArt.Dashed(dl, x0 + S(8), x0 + iw - S(8), Y(rowsTop + boundary * step + S(1), 1), t.Dashed);
                    ry = rowsTop + place * step + (place >= boundary ? S(PlateSizes.PinRule) : 0);
                }
                Row(dl, l, x0, Y(ry, rh), iw, rh, waiting ? 0 : bar, slot.Pinned, waiting, ref input);
            }
        }
        finally
        {
            dl.PopClipRect();
        }
        y = rowsTop + slots.Count * step + (slots.Count > 0 && slots[^1].Pinned ? S(PlateSizes.PinRule) : 0);
        footFrom = y + S(1);
        // The foot's plate (under the limit break's row and the party's line), first.
        if (t.FootPlate)
        {
            var ph = (lbOn ? S(PlateSizes.LimitBreak(t)) : 0) + S(PlateSizes.Foot);
            Plate(dl, new Vector2(x0, Y(footFrom, ph)), iw, ph, t.Row.Plate, t.Row.PlateAlpha);
        }
        if (lbOn && lb != null)
        {
            y += S(t.Id == MeterThemeId.Readout ? 1 : 2);
            Kit.Rule(dl, x0 + S(4), x0 + iw - S(4), Y(y, 1), t.Divider);
            y += S(3);
            var lh = S(PlateSizes.LimitBreakRow(t));
            LimitBreakRow(dl, x0, Y(y, lh), iw, lh);
            y += lh;
        }
        y += S(1);
        var fh = S(18);
        var party = waiting ? string.Empty : partyText;
        FootLine(dl, x0, iw, Y(y, fh), party, partyUnit, ref input, !waiting);
        Scanlines(dl, min, max);
        return input;
    }

    private static float Toward(float v, float target, float step) => v < target ? MathF.Min(target, v + step) : MathF.Max(target, v - step);

    private void Plate(ImDrawListPtr dl, Vector2 at, float w, float h, Vector4 color, float alpha)
    {
        if (alpha <= 0 || h < S(4)) return;
        dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Rgb.A(color, alpha * color.W)), S(T.Row.Radius));
    }

    // ---------------------------------------------------------------------------------------------
    // The head.

    /// <summary>
    /// The head's band in the theme (the compact meter's and, since 0.7.3, the normal meter's): Native's
    /// title band and gilt rule, Neon's horizon, Grimoire's two rules; on the side away from the rows
    /// (<paramref name="up"/>: the head at the foot). Nothing for the others (their head sits on a plate or on the ground).
    /// </summary>
    public void HeadBand(ImDrawListPtr dl, float left, float right, float top, float h, bool up)
    {
        var t = T;
        var w = right - left;
        var orn = t.Drawn(Decorations);
        if (t.Id == MeterThemeId.Native)
        {
            if ((orn & Ornaments.TitleBand) != 0)
                // Inset 2 from the window's edge it's against (the top, or the bottom with the head there), flush with the rows' side.
                PlateArt.GradientRect(dl, new Vector2(left + S(2), up ? top : top + S(2)), new Vector2(right - S(2), up ? top + h - S(2) : top + h), Rgb.Rgba(78, 78, 88, 0.9f),
                    Rgb.Rgba(40, 40, 46, 0.9f), S(3));
            Kit.Rule(dl, left + S(4), right - S(4), up ? top - S(1) : top + h, Rgb.Rgba(214, 184, 120, 0.5f));
        }
        else if (t.Id == MeterThemeId.Neon && (orn & Ornaments.Horizon) != 0)
        {
            var a = Palette.U32(Rgb.Rgba(255, 79, 216, 0));
            var b = Palette.U32(Rgb.Rgba(255, 79, 216, 0.16f));
            dl.AddRectFilledMultiColor(new Vector2(left + 1, up ? top : top + 1), new Vector2(right - 1, up ? top + h - 1 : top + h), up ? b : a, up ? b : a, up ? a : b, up ? a : b);
            var line = Palette.U32(Rgb.Rgba(255, 79, 216, 0.35f));
            var gy = up ? top + S(7) : top + h - S(7);
            for (var i = 0; i < 9; i++)
            {
                var gx = left + S(8) + i * (w - S(16)) / 8;
                var lean = (i - 4) * S(1.75f);
                dl.AddLine(new Vector2(gx, gy), new Vector2(gx + lean, up ? top : top + h), line, 1f);
            }
            dl.AddLine(new Vector2(left + S(4), gy + 0.5f), new Vector2(right - S(4), gy + 0.5f), line, 1f);
            var g2 = up ? top + S(3) : top + h - S(3);
            dl.AddLine(new Vector2(left + S(4), g2 + 0.5f), new Vector2(right - S(4), g2 + 0.5f), Palette.U32(Rgb.Rgba(255, 79, 216, 0.28f)), 1f);
            Kit.Rule(dl, left + S(4), right - S(4), up ? top : top + h, Rgb.Rgba(255, 79, 216, 0.7f));
        }
        else if (t.Id == MeterThemeId.Grimoire)
        {
            var r1 = up ? top + S(1) : top + h - S(1);
            var r2 = up ? top - S(1) : top + h + S(1);
            Kit.Rule(dl, left + S(10), right - S(10), r1, Rgb.Rgba(90, 60, 30, 0.45f));
            Kit.Rule(dl, left + S(10), right - S(10), r2, Rgb.Rgba(90, 60, 30, 0.25f));
        }

    }

    private void Head(ImDrawListPtr dl, Vector2 min, float w, float top, bool waiting, ref PlateInput input)
    {
        var t = T;
        var h = S(t.Win.HeadH);
        var hCss = t.Win.HeadH;
        var orn = t.Drawn(Decorations);
        var left = min.X;
        var right = min.X + w;
        var cy = top + h / 2;
        // The bands behind it.
        HeadBand(dl, left, right, top, h, Up);

        var padL = S(t.Id == MeterThemeId.Grimoire ? 12 : t.Id == MeterThemeId.Native ? 9 : 8);
        var padR = S(t.Id == MeterThemeId.Grimoire ? 12 : 7);
        // The right: the lock, the menu ("DPS ⌄"), the pips.
        var rx = right - padR;
        var lockS = S(12);
        var lockMin = new Vector2(rx - lockS, cy - lockS / 2);
        var lockIcon = !Locked ? Icon.LockOpen : ClickThrough ? Icon.MousePointer : Icon.Lock;
        var lockHot = false;
        if (Interactive)
        {
            var (clicked, hovered, _) = Kit.Hit("##pm-lock", lockMin - new Vector2(S(4)), new Vector2(lockS + S(8)));
            lockHot = hovered;
            if (hovered) Kit.Tooltip(Locked ? "Locked where it is. Press to move and size it again." : "Lock it where it is");
            input.Lock = clicked;
        }
        Icons.Draw(dl, lockIcon, lockMin, lockS, Locked && t.Id == MeterThemeId.ReadyCheck ? t.Accent : lockHot ? t.Row.Text : Rgb.A(t.Row.Muted, 0.9f));
        rx -= lockS + S(7);
        var chev = S(11);
        var ww = PlateArt.Width(tabWord, t.Font.Label);
        // The words, a 6px gap (Dropdown.Gap), the chevron.
        var mw = Dropdown.Width(ww, chev, 0, S(1));
        var mMin = new Vector2(rx - mw, top);
        if (Interactive)
        {
            var (clicked, hovered, _) = Kit.Hit("##pm-menu", mMin - new Vector2(S(4), 0), new Vector2(mw + S(8), h));
            if (hovered) dl.AddRectFilled(mMin - new Vector2(S(4), -S(3)), new Vector2(rx + S(4), top + h - S(3)), Palette.U32(t.HoverWash), S(4));
            input.Menu = clicked;
            input.MenuLeft = mMin.X - S(4);
        }
        PlateArt.Line(dl, mMin, tabWord, t.Font.Label, t.Row.Text, hCss);
        Icons.Draw(dl, Icon.ChevronDown, new Vector2(rx - chev, cy - chev / 2), chev, t.Row.Muted);
        rx -= mw + S(7);
        if (!waiting && live && Pips is { Length: > 0 } pips)
        {
            var pw = S(8);
            var gap = S(2);
            var pipsW = pips.Length * pw + (pips.Length - 1) * gap;
            DrawPips(dl, new Vector2(rx - pipsW, cy), pips, 8, 4);
            if (Interactive && Kit.Pointed(new Vector2(rx - pipsW, top), new Vector2(rx, top + h)) && PipsTip != null) Kit.Tooltip(PipsTip);
            rx -= pipsW + S(7);
        }
        // The left: as far as it fits before them.
        var x = left + padL;
        var room = rx - S(4);
        var gapL = S(6);
        void Dot(ref float xx)
        {
            var c = new Vector2(xx + S(3), cy);
            if (live) Kit.BeatDot(dl, c, t.Dot, true);
            else if (waiting) dl.AddCircle(c, S(3) - S(0.75f), Palette.U32(t.Row.Faint), 16, S(1.5f));
            else return;
            xx += S(6) + gapL;
        }
        switch (t.Id)
        {
            case MeterThemeId.Mochi:
            {
                var clk = waiting ? "0:00" : clock;
                var cw = PlateArt.Width(clk, t.Font.Head);
                var pillW = S(7) + S(11) + S(5) + cw + S(9);
                var pill = new Vector2(x, cy - S(10));
                dl.AddRectFilled(pill, pill + new Vector2(pillW, S(20)), Palette.U32(Rgb.Hex(0xffdcea)), S(10));
                PlateArt.Glyph(dl, PlateGlyph.Heart, new Vector2(x + S(7), cy - S(5.5f)), S(11), live ? Rgb.Hex(0xff4f9a) : Rgb.Hex(0xd9a7bd));
                PlateArt.Line(dl, new Vector2(x + S(7) + S(11) + S(5), top), clk, t.Font.Head, t.Clock, hCss);
                x += pillW + gapL;
                break;
            }
            case MeterThemeId.Native:
            {
                var d = S(7);
                PlateArt.Glyph(dl, PlateGlyph.Diamond, new Vector2(x - S(1), cy - d / 2 - S(1)), d + S(2), Rgb.Hex(0x000000));
                PlateArt.Glyph(dl, PlateGlyph.Diamond, new Vector2(x, cy - d / 2), d, Rgb.Hex(0xd6b878));
                x += d + gapL;
                x += PlateArt.Line(dl, new Vector2(x, top), PlateArt.Fit(title, t.Font.Head, room - x), t.Font.Head, Rgb.Hex(0xefe3c4), hCss, Rgb.Rgba(0, 0, 0, 0.8f)) + gapL;
                break;
            }
            case MeterThemeId.Readout:
                x += PlateArt.Line(dl, new Vector2(x, top), ">", t.Font.Head, t.Accent, hCss) + gapL;
                x += PlateArt.Line(dl, new Vector2(x, top), waiting ? "0:00" : clock, t.Font.Head, t.Clock, hCss) + S(2);
                if (live) dl.AddRectFilled(new Vector2(x, cy - S(5.5f)), new Vector2(x + S(6), cy + S(5.5f)), Palette.U32(t.Accent));
                x += S(6) + gapL;
                break;
            case MeterThemeId.Grimoire:
            {
                var rubric = (orn & Ornaments.Rubric) != 0 && title.Length > 1;
                if (rubric)
                {
                    var cap = new ThemeFont(Face.SerifSemibold, 19);
                    x += PlateArt.Line(dl, new Vector2(x, top - S(1.5f)), title[..1], cap, Rgb.Hex(0x9b1c1c), hCss) + S(1);
                    x += PlateArt.Line(dl, new Vector2(x, top), Rest(title), t.Font.Head, Rgb.Hex(0x3b2412), hCss) + gapL;
                }
                else x += PlateArt.Line(dl, new Vector2(x, top), title, t.Font.Head, Rgb.Hex(0x3b2412), hCss) + gapL;
                break;
            }
            default:
                Dot(ref x);
                x += PlateArt.Line(dl, new Vector2(x, top), waiting ? "0:00" : clock, t.Font.Head, t.Clock, hCss,
                    t.Id == MeterThemeId.Neon ? Rgb.Rgba(61, 236, 255, 0.35f) : null) + gapL;
                break;
        }
        // The outcome's tag.
        if (!live && !waiting && ended is { } o && x < room)
        {
            var word = o == Core.Outcome.Clear ? "Clear" : "Wipe";
            var c = o == Core.Outcome.Clear ? (t.Light ? t.Good : t.Row.Text) : t.Bad;
            var tag = o == Core.Outcome.Clear ? "[CLEAR]" : "[WIPE]";
            if (t.Id == MeterThemeId.Readout)
            {
                if (x + PlateArt.Width(tag, t.Font.Small) <= room) x += PlateArt.Line(dl, new Vector2(x, top), tag, t.Font.Small, c, hCss) + gapL;
            }
            else x += Chip(dl, new Vector2(x, cy), word, c, room - x) + gapL;
        }
        // The clock after the title (Native, Grimoire), when it fits whole before the buttons.
        var after = waiting ? "0:00" : clock;
        if (t.Id is MeterThemeId.Native or MeterThemeId.Grimoire && x + PlateArt.Width(after, t.Font.Num) <= room)
            x += PlateArt.Line(dl, new Vector2(x, top), after, t.Font.Num, t.Clock, hCss) + gapL;
        // Where in the fight, or waiting.
        if (waiting)
        {
            var wt = t.Id == MeterThemeId.Readout ? "waiting…" : "Waiting for the first hit";
            if (room - x > S(20)) PlateArt.Line(dl, new Vector2(x, top), PlateArt.Fit(wt, t.Font.Label, room - x), t.Font.Label, t.Row.Muted, hCss);
        }
        else if (phase is { } ph && x < room)
        {
            switch (t.Id)
            {
                case MeterThemeId.Native:
                    if (live && PlateArt.Width(ph.Long, t.Font.Small) <= room - x) PlateArt.Line(dl, new Vector2(x, top), ph.Long, t.Font.Small, Rgb.Hex(0xd9cfb8), hCss);
                    break;
                case MeterThemeId.Readout:
                    if (live && PlateArt.Width(ph.Term, t.Font.Small) <= room - x) PlateArt.Line(dl, new Vector2(x, top), ph.Term, t.Font.Small, t.Row.Muted, hCss);
                    break;
                case MeterThemeId.Grimoire:
                    if (PlateArt.Width(ph.Short, t.Font.Small) <= room - x) PlateArt.Line(dl, new Vector2(x, top), ph.Short, t.Font.Small, t.Row.Muted, hCss);
                    break;
                default:
                    Chip(dl, new Vector2(x, cy), live ? ph.Chip : "in " + ph.Short, t.Row.Muted, room - x);
                    break;
            }
        }
    }

    private static string Rest(string word) => RestOf.TryGetValue(word, out var r) ? r : RestOf[word] = word[1..];

    private static readonly Dictionary<string, string> RestOf = new();

    /// <summary>The limit break's tooltip for the pips.</summary>
    public string? PipsTip;

    /// <summary>A chip: the words in the small face in a hairline (Mochi's filled) box, 16 tall; returns its width (0 when it doesn't fit).</summary>
    private float Chip(ImDrawListPtr dl, Vector2 leftMid, string text, Vector4 color, float room)
    {
        var t = T;
        var tw = PlateArt.Width(text, t.Font.Small);
        var w = tw + S(10);
        if (w > room) return 0;
        var a = new Vector2(leftMid.X, leftMid.Y - S(8));
        var b = a + new Vector2(w, S(16));
        if (t.ChipFill is { } fill) dl.AddRectFilled(a, b, Palette.U32(fill), S(t.ChipRadius));
        else dl.AddRect(a + new Vector2(0.5f), b - new Vector2(0.5f), Palette.U32(t.ChipLine), S(t.ChipRadius), ImDrawFlags.None, 1f);
        PlateArt.Line(dl, new Vector2(a.X + S(5), a.Y), text, t.Font.Small, color, 16);
        return w;
    }

    /// <summary>The limit break's pips: a bar each, full ones the theme's colour, the one filling to its share over a track.</summary>
    public void DrawPips(ImDrawListPtr dl, Vector2 leftMid, float[] pips, float wCss, float hCss)
    {
        var t = T;
        var pw = S(wCss);
        var ph = S(hCss);
        var r = t.Id == MeterThemeId.Readout ? 0 : S(1);
        var x = leftMid.X;
        var track = Palette.U32(Rgb.A(t.Row.Muted, 0.22f));
        foreach (var f in pips)
        {
            var a = new Vector2(x, leftMid.Y - ph / 2);
            var b = a + new Vector2(pw, ph);
            if (f >= 1) dl.AddRectFilled(a, b, Palette.U32(t.Lb), r);
            else
            {
                dl.AddRectFilled(a, b, track, r);
                if (f > 0) dl.AddRectFilled(a, new Vector2(a.X + pw * f, b.Y), Palette.U32(Rgb.A(t.Lb, 0.55f)));
            }
            x += pw + S(2);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // A row.

    private void Row(ImDrawListPtr dl, Line l, float x0, float y, float iw, float rh, float bar, bool pinned, bool waiting, ref PlateInput input)
    {
        var t = T;
        var r = l.Row;
        var min = new Vector2(x0, y);
        var max = new Vector2(x0 + iw, y + rh);
        var rad = S(t.Row.Radius);
        var dead = r.Dead && !waiting;
        var you = r.IsYou;
        var youStyle = t.YouDrawn(Decorations);
        var inv = you && youStyle == YouStyle.Invert;
        var hovered = false;
        if (Interactive)
        {
            var (clicked, hov, _) = Kit.Hit(l.Id, min, max - min);
            hovered = hov;
            if (clicked) input.Pressed = r.PartyId;
            if (hov)
            {
                input.Pointed = r.PartyId;
                input.PointedMin = min;
                input.PointedMax = max;
            }
        }
        // The plate, reverse video, a fresh death's flash, pointed at.
        if (t.Row.PlateAlpha > 0)
        {
            dl.AddRectFilled(min, max, Palette.U32(Rgb.A(t.Row.Plate, t.Row.PlateAlpha)), rad);
            if (t.Row.PlateEdge is { } pe) dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(pe), rad, ImDrawFlags.None, 1f);
        }
        if (inv) dl.AddRectFilled(min - new Vector2(S(2), 0), max + new Vector2(S(2), 0), Palette.U32(t.You));
        if (dead && diedSeen.TryGetValue(r.PartyId, out var seen))
        {
            var since = (float)(Now() - seen);
            if (since is >= 0 and < 2) dl.AddRectFilled(min, max, Palette.U32(Rgb.A(t.Death, 0.28f * (1 - since / 2))), rad);
        }
        if (hovered) dl.AddRectFilled(min, max, Palette.U32(t.HoverWash), rad);
        // The right's parts: Mochi keeps the heart's place on every row (so the shares line up), the share column, the number's.
        var gap = S(t.Id == MeterThemeId.Readout ? 5 : 6);
        var padR = S(t.Id == MeterThemeId.Mochi ? 7 : 5);
        var heartSlot = youStyle == YouStyle.Heart ? S(10) + S(2) : 0;
        var shareW = ShareColumn();
        var valW = ValueColumn();
        // The bar, on its track: from the row's left to just before the share (and, a hard-ended bar, the number), so it never runs under them.
        if (t.Bar.Style != BarStyle.Segments && !inv)
        {
            var track = PlateColumns.Track(iw, padR, heartSlot, shareW, S(1), t.Bar.BeforeNumber ? valW : 0, gap);
            Bar(dl, min, track, rh, bar, r.Role, dead);
            // Healing's shields absorbed, striped after the bar (as it eases, the stripes keep to its end).
            if (!waiting && l.Striped > 0) StripesOn(dl, min, track, rh, bar, l.Striped, r.Role, dead);
        }
        if (t.Row.Rule is { } rule) Kit.Rule(dl, min.X, max.X, max.Y, rule);
        // You.
        if (you) YouMark(dl, min, max, rad, rh, youStyle);
        // The words: the rank (Readout), the icon, the name; from the right the share, the number, the cells, the death's mark.
        var padIn = S(t.Id == MeterThemeId.Mochi ? 3 : you && youStyle == YouStyle.Notch ? 5 : you && youStyle == YouStyle.Ribbon ? PastRibbon : 3);
        var lineCss = rh / S(1);
        var ink = Rgb.Hex(0x031005);
        var x = min.X + padIn;
        if (t.Id == MeterThemeId.Readout)
        {
            PlateArt.Line(dl, new Vector2(x, y), inv ? ">" : l.RankText, t.Font.Small, inv ? ink : t.Row.Faint, lineCss);
            x += S(14) + gap;
            var letter = r.Role == "tank" ? "T" : r.Role == "healer" ? "H" : "D";
            PlateArt.Line(dl, new Vector2(x, y), letter, new ThemeFont(Face.MonoSemibold, t.Font.Name.Px), inv ? ink : dead ? t.DeadIcon : Role(r.Role), lineCss);
            x += S(9) + gap;
        }
        else
        {
            var isz = t.Icon?.Size ?? 16;
            PlateArt.JobIcon(dl, new Vector2(x, y + (rh - S(isz)) / 2), isz, r.Job, r.Role, Role(r.Role), t.Icon, dead, t.DeadIcon);
            x += S(isz) + gap;
        }
        var rx = max.X - padR;
        if (heartSlot > 0)
        {
            if (you) PlateArt.Glyph(dl, PlateGlyph.Heart, new Vector2(rx - S(10), y + (rh - S(10)) / 2), S(10), t.You);
            rx -= heartSlot;
        }
        if (!waiting) PlateArt.LineRight(dl, rx, y, l.Share, t.Font.Small, inv ? ink : t.Row.Muted, lineCss);
        rx -= shareW + gap;
        PlateArt.LineRight(dl, rx, y, waiting ? "–" : l.Value, t.Font.Num, inv ? ink : dead ? t.Row.Muted : t.Row.Num, lineCss);
        rx -= valW + gap;
        var deathStyle = t.DeathDrawn(Decorations);
        if (t.Bar.Style == BarStyle.Segments)
        {
            var sw = S(t.Bar.SegWidth);
            if (dead && deathStyle == DeathStyle.Tag && r.DiedAt is { } at)
                PlateArt.Line(dl, new Vector2(rx - sw, y), DeadTag(at), t.Font.Small, t.Death, lineCss);
            else if (!waiting) Segments(dl, new Vector2(rx - sw, y + (rh - S(t.Bar.SegH)) / 2), bar, r.Role, dead, inv);
            rx -= sw + gap;
        }
        else if (dead)
        {
            var dc = t.Death;
            if (l.DeadFor != null)
            {
                rx -= PlateArt.LineRight(dl, rx, y, l.DeadFor, t.Font.Small, dc, lineCss) + S(4);
            }
            var gs = DeathMark(dl, rx, y, rh, deathStyle, dc, t.Font.Num);
            rx -= gs + gap;
        }
        // The name (or the job alone), cut to fit; yours a weight heavier; struck through or glitched when dead.
        var nameFont = you && t.Id != MeterThemeId.Readout ? t.Font.Name with { Face = MeterThemes.Heavier(t.Font.Name.Face) } : t.Font.Name;
        var nameText = JobsOnly ? r.Job : r.Name;
        var room = rx - x - (pinned ? PlateArt.Width(l.PinnedRank, t.Font.Small) + S(5) : 0);
        var name = PlateArt.Fit(nameText, nameFont, MathF.Max(S(10), room));
        var nameColor = inv ? ink : dead ? t.Row.Muted : t.Row.Text;
        float nw;
        if (dead && deathStyle == DeathStyle.Glitch)
        {
            // The two offset copies faint, so the name itself still reads.
            PlateArt.Line(dl, new Vector2(x - 1, y), name, nameFont, Rgb.A(t.Death, GlitchAlpha), lineCss);
            PlateArt.Line(dl, new Vector2(x + 1, y), name, nameFont, Rgb.Rgba(61, 236, 255, GlitchAlpha), lineCss);
            nw = PlateArt.Line(dl, new Vector2(x, y), name, nameFont, nameColor, lineCss);
        }
        else nw = PlateArt.Line(dl, new Vector2(x, y), name, nameFont, nameColor, lineCss);
        if (dead && deathStyle is DeathStyle.Dagger or DeathStyle.Tag)
        {
            var sy = y + rh / 2 + S(0.5f);
            dl.AddLine(new Vector2(x, sy), new Vector2(x + nw, sy), Palette.U32(t.Death), S(1.5f));
        }
        if (pinned) PlateArt.Line(dl, new Vector2(x + nw + S(5), y), l.PinnedRank, t.Font.Small, inv ? ink : t.Row.Muted, lineCss);
    }

    /// <summary>A row's mark for you in a style (the meter's rows; the opened-up window's picked row): an outline, a notch, a heart's ring, a ribbon. Reverse video (Readout) is the row's own fill: not drawn here.</summary>
    public void YouMark(ImDrawListPtr dl, Vector2 min, Vector2 max, float rad, float rh, YouStyle style)
    {
        var t = T;
        switch (style)
        {
            case YouStyle.Notch:
                dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Rgb.A(t.You, 0.75f)), rad, ImDrawFlags.None, 1f);
                dl.AddRectFilled(min, new Vector2(min.X + S(3), max.Y), Palette.U32(t.You), rad, ImDrawFlags.RoundCornersLeft);
                break;
            case YouStyle.Heart:
                dl.AddRect(min - new Vector2(S(1) - 1), max + new Vector2(S(1) - 1), Palette.U32(t.You), rad + S(1), ImDrawFlags.None, S(2));
                break;
            case YouStyle.Gold:
                dl.AddRect(min - new Vector2(0.5f), max + new Vector2(0.5f), Palette.U32(Rgb.Rgba(0, 0, 0, 0.6f)), rad + 1, ImDrawFlags.None, 1f);
                dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(t.You), rad, ImDrawFlags.None, 1f);
                break;
            case YouStyle.White:
                dl.AddRect(min - new Vector2(S(2.5f)), max + new Vector2(S(2.5f)), Palette.U32(Rgb.Rgba(255, 255, 255, 0.1f)), rad + S(3), ImDrawFlags.None, S(2));
                dl.AddRect(min - new Vector2(0.5f), max + new Vector2(0.5f), Palette.U32(Rgb.Rgba(255, 255, 255, 0.28f)), rad + 1, ImDrawFlags.None, 1f);
                dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Vector4.One), rad, ImDrawFlags.None, 1f);
                break;
            case YouStyle.Ribbon:
                // The bookmark, 6 wide (its strip is the middle half of its box), from 2 in: the job icon starts clear of it.
                PlateArt.Glyph(dl, PlateGlyph.Ribbon, new Vector2(min.X + S(RibbonLeft) - S(RibbonWidth) / 2, min.Y - S(3)), new Vector2(S(RibbonWidth * 2), rh + S(7)), t.You);
                break;
        }
    }

    /// <summary>How far in a row's words start past a ribbon (CSS px).</summary>
    public const float PastRibbon = RibbonLeft + RibbonWidth + 6;

    /// <summary>A death's mark in a style, its right edge at <paramref name="right"/>, on a row <paramref name="rh"/> tall from <paramref name="y"/>: a skull, a broken heart, a dagger, a cross. Returns its width.</summary>
    public static float DeathMark(ImDrawListPtr dl, float right, float y, float rh, DeathStyle style, Vector4 color, ThemeFont dagger)
    {
        var gs = S(style == DeathStyle.Broken ? 13 : style == DeathStyle.Skull ? 12 : 11);
        var gy = y + (rh - gs) / 2;
        switch (style)
        {
            case DeathStyle.Skull:
                Icons.Draw(dl, Icon.Skull, new Vector2(right - gs, gy), gs, color, 2.2f);
                break;
            case DeathStyle.Broken:
                PlateArt.Glyph(dl, PlateGlyph.BrokenHeart, new Vector2(right - gs, gy), gs, color);
                break;
            case DeathStyle.Dagger:
                return PlateArt.LineRight(dl, right, y, "†", dagger, color, rh / S(1));
            default:
                Icons.Draw(dl, Icon.X, new Vector2(right - gs, gy), gs, color, 3.2f);
                break;
        }
        return gs;
    }

    /// <summary>A bar in the theme's style along a track (the opened-up window's), as a row's.</summary>
    public void BarOn(ImDrawListPtr dl, Vector2 min, float track, float h, float f, string? role, bool dead)
    {
        if (T.Bar.Style != BarStyle.Segments)
        {
            Bar(dl, min, track, h, f, role ?? "dps", dead);
            return;
        }
        // Readout's cells along the track.
        var b = T.Bar;
        var step = S(b.Cell + b.CellGap);
        var n = Math.Max(1, (int)MathF.Floor(track / step));
        var on = (int)MathF.Round(n * Math.Clamp(f, 0, 1));
        var lit = Palette.U32(dead ? T.DeadBar : Role(role));
        var dim = Palette.U32(Rgb.Rgba(196, 245, 205, 0.08f));
        for (var i = 0; i < n; i++)
        {
            var a = new Vector2(MathF.Round(min.X + i * step), MathF.Round(min.Y));
            dl.AddRectFilled(a, a + new Vector2(S(b.Cell), h), i < on ? lit : dim);
        }
    }

    /// <summary>
    /// The striped part after a bar (since 0.9.0, the Healing tab's shields absorbed): from <paramref name="from"/> of the track for
    /// <paramref name="length"/> of it, a faint wash of the role's colour with its diagonal stripes; Readout's cells half lit.
    /// </summary>
    public void StripesOn(ImDrawListPtr dl, Vector2 min, float track, float h, float from, float length, string? role, bool dead)
    {
        if (length <= 0 || track <= 0) return;
        var rc = dead ? T.DeadBar : Role(role);
        if (T.Bar.Style == BarStyle.Segments)
        {
            var b = T.Bar;
            var step = S(b.Cell + b.CellGap);
            var n = Math.Max(1, (int)MathF.Floor(track / step));
            var a0 = (int)MathF.Round(n * Math.Clamp(from, 0, 1));
            var a1 = (int)MathF.Round(n * Math.Clamp(from + length, 0, 1));
            for (var i = a0; i < a1; i++)
            {
                var a = new Vector2(MathF.Round(min.X + i * step), MathF.Round(min.Y));
                dl.AddRectFilled(a, a + new Vector2(S(b.Cell), h), Palette.U32(Rgb.A(rc, 0.45f)));
            }
            return;
        }
        var x0 = MathF.Round(min.X + track * Math.Clamp(from, 0, 1));
        var x1 = MathF.Round(min.X + track * Math.Clamp(from + length, 0, 1));
        if (x1 - x0 < 1) return;
        Stripes(dl, new Vector2(x0, min.Y), new Vector2(x1, min.Y + h), rc, dead);
    }

    /// <summary>Diagonal stripes in a box (the shields' part of a bar, the legend's swatch): a faint wash of the colour and its lines.</summary>
    public static void Stripes(ImDrawListPtr dl, Vector2 a, Vector2 b, Vector4 color, bool dead = false)
    {
        dl.AddRectFilled(a, b, Palette.U32(Rgb.A(color, dead ? 0.05f : 0.12f)));
        dl.PushClipRect(a, b, true);
        var h = b.Y - a.Y;
        var gap = MathF.Max(3, S(5));
        var line = Palette.U32(Rgb.A(color, dead ? 0.2f : 0.55f));
        for (var x = a.X - h; x < b.X; x += gap) dl.AddLine(new Vector2(x, b.Y), new Vector2(x + h, a.Y), line, MathF.Max(1, S(1.5f)));
        dl.PopClipRect();
    }

    /// <summary>Grimoire's ribbon on your row: its strip's left edge and width (CSS px); the job icon starts 6 past it.</summary>
    private const float RibbonLeft = 2;

    private const float RibbonWidth = 6;

    /// <summary>Neon's glitch: how strong the dead name's two offset copies are.</summary>
    internal const float GlitchAlpha = 0.35f;

    private readonly Dictionary<int, string> deadTags = new();

    private string DeadTag(double at)
    {
        var k = (int)Math.Floor(at);
        if (deadTags.TryGetValue(k, out var s)) return s;
        if (deadTags.Count > 64) deadTags.Clear();
        return deadTags[k] = "DEAD " + Format.Clock(at);
    }

    /// <summary>A steady clock in seconds (the flash of a fresh death).</summary>
    public Func<double> Now { get; set; } = () => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    /// <summary>A row's bar in the theme's style, from the row's left to its share of the top's along its track (<see cref="PlateColumns.Track"/>, ending before the share).</summary>
    private void Bar(ImDrawListPtr dl, Vector2 min, float iw, float rh, float f, string role, bool dead)
    {
        var t = T;
        var b = t.Bar;
        var fw = MathF.Round(iw * Math.Clamp(f, 0, 1));
        if (fw < 1) return;
        var rc = dead ? t.DeadBar : Role(role);
        var rad = S(t.Row.Radius);
        switch (b.Style)
        {
            case BarStyle.Fill:
            {
                var flags = fw >= iw - 0.5f ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersLeft;
                dl.AddRectFilled(min, new Vector2(min.X + fw, min.Y + rh), Palette.U32(Rgb.A(rc, dead ? 0.12f : b.Alpha)), rad, flags);
                var e = S(b.Edge);
                dl.AddRectFilled(new Vector2(min.X + MathF.Min(rad / 2, fw), min.Y + rh - e), new Vector2(min.X + fw - (flags == ImDrawFlags.RoundCornersAll ? rad / 2 : 0), min.Y + rh),
                    Palette.U32(dead ? Rgb.Rgba(255, 255, 255, 0.18f) : rc));
                break;
            }
            case BarStyle.Pill:
                dl.AddRectFilled(min, new Vector2(min.X + MathF.Max(rh, fw), min.Y + rh), Palette.U32(Rgb.Over(rc, dead ? 0.5f : b.Alpha, Vector4.One)), rh / 2);
                break;
            case BarStyle.Hp:
            {
                var top = dead ? Rgb.Hex(0x3a3a3e) : rc;
                var bottom = dead ? Rgb.Hex(0x222226) : Rgb.Mix(rc, new Vector4(0, 0, 0, 1), 0.45f);
                var a = new Vector2(min.X + 1, min.Y + 1);
                var z = new Vector2(min.X + MathF.Max(1, fw - 1), min.Y + rh - 1);
                var ct = Palette.U32(Rgb.A(top, b.Alpha));
                var cb = Palette.U32(Rgb.A(bottom, b.Alpha));
                dl.AddRectFilledMultiColor(a, z, ct, ct, cb, cb);
                dl.AddLine(new Vector2(a.X, a.Y + 0.5f), new Vector2(z.X, a.Y + 0.5f), Palette.U32(Rgb.Rgba(255, 255, 255, dead ? 0.05f : 0.3f)), 1f);
                break;
            }
            case BarStyle.Glow:
            {
                var c0 = Palette.U32(Rgb.A(rc, dead ? 0.05f : b.Alpha0));
                var c1 = Palette.U32(Rgb.A(rc, dead ? 0.12f : b.Alpha));
                var z = new Vector2(min.X + fw, min.Y + rh);
                dl.AddRectFilledMultiColor(min, z, c0, c1, c1, c0);
                dl.AddLine(new Vector2(min.X, min.Y + 0.5f), new Vector2(z.X, min.Y + 0.5f), Palette.U32(Rgb.A(rc, dead ? 0.3f : 1)), 1f);
                if (!dead)
                {
                    var capA = new Vector2(z.X - S(2), min.Y);
                    dl.AddRect(capA - new Vector2(S(2.5f)), z + new Vector2(S(2.5f)), Palette.U32(Rgb.A(rc, 0.12f)), 0, ImDrawFlags.None, S(2));
                    dl.AddRect(capA - new Vector2(0.5f), z + new Vector2(0.5f), Palette.U32(Rgb.A(rc, 0.35f)), 0, ImDrawFlags.None, 1f);
                    dl.AddRectFilled(capA, z, Palette.U32(rc));
                }
                break;
            }
            case BarStyle.Underline:
            {
                var e = S(b.Edge);
                var lineTop = min.Y + rh - e - S(1);
                dl.AddRectFilled(min, new Vector2(min.X + fw, lineTop), Palette.U32(Rgb.A(rc, dead ? 0.04f : b.Wash)));
                dl.AddRectFilled(new Vector2(min.X, lineTop), new Vector2(min.X + fw, lineTop + e), Palette.U32(dead ? Rgb.Rgba(90, 60, 30, 0.3f) : rc));
                break;
            }
        }
    }

    /// <summary>Readout's bar: a run of lit cells, the rest dim.</summary>
    private void Segments(ImDrawListPtr dl, Vector2 at, float f, string role, bool dead, bool inv)
    {
        var b = T.Bar;
        var n = (int)MathF.Floor(b.SegWidth / (b.Cell + b.CellGap));
        var on = (int)MathF.Round(n * Math.Clamp(f, 0, 1));
        var lit = Palette.U32(inv ? Rgb.Hex(0x031005) : dead ? T.DeadBar : Role(role));
        var dim = Palette.U32(inv ? Rgb.Rgba(3, 16, 5, 0.18f) : Rgb.Rgba(196, 245, 205, 0.08f));
        var cw = S(b.Cell);
        var step = S(b.Cell + b.CellGap);
        var h = S(b.SegH);
        for (var i = 0; i < n; i++)
        {
            var a = new Vector2(MathF.Round(at.X + i * step), MathF.Round(at.Y));
            dl.AddRectFilled(a, a + new Vector2(cw, h), i < on ? lit : dim);
        }
    }

    private void LimitBreakRow(ImDrawListPtr dl, float x0, float y, float iw, float lh)
    {
        var t = T;
        var l = lb!;
        var lineCss = lh / S(1);
        var x = x0 + S(3);
        var gs = S(12);
        PlateArt.Glyph(dl, t.Id == MeterThemeId.Mochi ? PlateGlyph.Star : PlateGlyph.LimitBreak, new Vector2(x + (S(16) - gs) / 2, y + (lh - gs) / 2), gs, t.Lb);
        x += S(16) + S(6);
        // Its columns line up with the rows' (Mochi's heart's place kept as theirs).
        var rx = x0 + iw - S(t.Id == MeterThemeId.Mochi ? 7 : 5) - (t.YouDrawn(Decorations) == YouStyle.Heart ? S(10) + S(2) : 0);
        PlateArt.LineRight(dl, rx, y, l.Share, t.Font.Small, t.Row.Muted, lineCss);
        rx -= ShareColumn() + S(6);
        var num = t.Font.Num with { Px = t.Font.Num.Px - 1 };
        PlateArt.LineRight(dl, rx, y, l.Value, num, t.Row.Text, lineCss);
        rx -= MathF.Max(ValueColumn(), PlateArt.Width(l.Value, num)) + S(6);
        var label = t.Id == MeterThemeId.Readout ? "LB" : "Limit break";
        PlateArt.Line(dl, new Vector2(x, y), PlateArt.Fit(label, t.Font.Label, rx - x), t.Font.Label, t.Row.Muted, lineCss);
    }

    /// <summary>The foot: "Party 165k" (Readout's in lower case: Geist Mono has no Σ) and on the right pace, what's kept, or a note; the Preview tag while the sample's on.</summary>
    private void FootLine(ImDrawListPtr dl, float x0, float iw, float y, string value, string? unit, ref PlateInput input, bool rightSide = true)
    {
        var t = T;
        var f = t.Font.Small;
        var x = x0 + S(3);
        var rx = x0 + iw - S(3);
        // The right first (the Preview tag is pressed; pace changes), then the left in the room it leaves.
        if (rightSide) rx -= FootRight(dl, rx, y, ref input) is var rw and > 0 ? rw + S(8) : 0;
        if (value.Length == 0) return;
        var room = rx - x;
        if (tabOf is MeterTab.Deaths or MeterTab.Events)
        {
            PlateArt.Line(dl, new Vector2(x, y), PlateArt.Fit(value, f, MathF.Min(iw * 0.62f, room)), f, t.Row.Muted, 18);
            return;
        }
        var label = t.Id == MeterThemeId.Readout ? "party " : "Party ";
        var unitText = unit == null ? null : unit == "HPS" ? " HPS" : unit;
        var vw = PlateArt.Width(value, f);
        var lw = PlateArt.Width(label, f);
        var uw = unitText != null ? PlateArt.Width(unitText, f) : 0;
        // What doesn't fit goes: the unit, then the word; the number stays.
        if (lw + vw + uw > room) unitText = null;
        if (lw + vw > room) lw = 0;
        if (lw > 0) x += PlateArt.Line(dl, new Vector2(x, y), label, f, t.Row.Muted, 18);
        x += PlateArt.Line(dl, new Vector2(x, y), value, f, t.Row.Text, 18);
        if (unitText != null) PlateArt.Line(dl, new Vector2(x, y), unitText, f, t.Row.Muted, 18);
    }

    /// <summary>The foot's right, from <paramref name="rx"/>: the Preview tag, a note, what it's kept as, or pace; returns its width (0: nothing).</summary>
    private float FootRight(ImDrawListPtr dl, float rx, float y, ref PlateInput input)
    {
        var t = T;
        var f = t.Font.Small;
        if (PreviewOn)
        {
            const string word = "Preview";
            var w = PlateArt.Width(word, f) + S(10);
            var a = new Vector2(rx - w, y + S(1));
            if (Interactive)
            {
                var (clicked, hovered, _) = Kit.Hit("##pm-preview", a, new Vector2(w, S(16)));
                if (hovered) Kit.Tooltip("A sample fight with made-up numbers. Press to stop it; a real fight stops it too.");
                input.Preview = clicked;
            }
            dl.AddRect(a + new Vector2(0.5f), a + new Vector2(w, S(16)) - new Vector2(0.5f), Palette.U32(t.ChipLine), S(t.ChipRadius), ImDrawFlags.None, 1f);
            PlateArt.Line(dl, new Vector2(a.X + S(5), a.Y), word, f, t.Accent, 16);
            return w;
        }
        if (FootNote != null) return PlateArt.LineRight(dl, rx, y, FootNote, f, t.Row.Muted, 18);
        if (!live && tabOf is not (MeterTab.Deaths or MeterTab.Events)) return PlateArt.LineRight(dl, rx, y, keptAs, f, t.Row.Muted, 18);
        if (Pace != null && tabOf == MeterTab.Damage)
        {
            var pw = PlateArt.LineRight(dl, rx, y, Pace, f, PaceAhead ? t.Good : t.Bad, 18);
            var lw = PlateArt.LineRight(dl, rx - pw, y, "pace ", f, t.Row.Muted, 18);
            if (Interactive && PaceTip != null && Kit.Pointed(new Vector2(rx - pw - lw, y), new Vector2(rx, y + S(18)))) Kit.Tooltip(PaceTip);
            return pw + lw;
        }
        return 0;
    }

    /// <summary>A death (Deaths) or an event (Events) on its plate: the time, the mark, the job icon, the words.</summary>
    private void ItemRow(ImDrawListPtr dl, Item it, float x0, float y, float iw, float rh, int index, ref PlateInput input)
    {
        var t = T;
        var min = new Vector2(x0, y);
        var max = new Vector2(x0 + iw, y + rh);
        var rad = S(t.Row.Radius);
        var hovered = false;
        if (Interactive)
        {
            var (clicked, hov, _) = Kit.Hit(it.Id, min, max - min);
            hovered = hov;
            if (clicked) input.Item = index;
            if (hov) Kit.Tooltip(it.Kind == MeterEventKind.Death && tabOf == MeterTab.Deaths ? $"{it.Name} died at {it.Time}: {it.Text}" : $"{MeterText.KindWord(it.Kind)} at {it.Time}: {it.Text}");
        }
        if (t.Row.PlateAlpha > 0) dl.AddRectFilled(min, max, Palette.U32(Rgb.A(t.Row.Plate, t.Row.PlateAlpha)), rad);
        if (hovered) dl.AddRectFilled(min, max, Palette.U32(t.HoverWash), rad);
        if (t.Row.Rule is { } rule) Kit.Rule(dl, min.X, max.X, max.Y, rule);
        var lineCss = rh / S(1);
        var x = min.X + S(4);
        var rx = max.X - S(5);
        var bad = it.Kind == MeterEventKind.Death;
        if (tabOf == MeterTab.Deaths)
        {
            PlateArt.JobIcon(dl, new Vector2(x, y + (rh - S(14)) / 2), 14, it.Job, it.Role, Role(it.Role), t.Icon, false, t.DeadIcon);
            x += S(14) + S(6);
            rx -= PlateArt.LineRight(dl, rx, y, it.Time, t.Font.Small, t.Death, lineCss) + S(8);
            var who = JobsOnly ? it.Job ?? it.Name : it.Name;
            var nw = PlateArt.Line(dl, new Vector2(x, y), PlateArt.Fit(who, t.Font.Name, MathF.Max(S(20), (rx - x) * 0.5f)), t.Font.Name, t.Row.Text, lineCss);
            x += nw + S(6);
            if (rx - x > S(20)) PlateArt.Line(dl, new Vector2(x, y), PlateArt.Fit(it.Text, t.Font.Label, rx - x), t.Font.Label, t.Row.Muted, lineCss);
            return;
        }
        x += PlateArt.LineRight(dl, x + S(28), y, it.Time, t.Font.Small, t.Row.Faint, lineCss) > 0 ? S(28) + S(6) : 0;
        var mark = S(12);
        var tone = MeterText.Tone(it.Kind) switch
        {
            EventTone.Bad => t.Bad,
            EventTone.Good => t.Good,
            EventTone.Ember => t.Accent,
            _ => t.Row.Muted,
        };
        Icons.Draw(dl, MeterWindow.EventIcon(it.Kind), new Vector2(x, y + (rh - mark) / 2), mark, tone);
        x += mark + S(6);
        if (it.Job != null)
        {
            PlateArt.JobIcon(dl, new Vector2(x, y + (rh - S(14)) / 2), 14, it.Job, it.Role, Role(it.Role), t.Icon, false, t.DeadIcon);
            x += S(14) + S(5);
        }
        if (rx - x > S(16)) PlateArt.Line(dl, new Vector2(x, y), PlateArt.Fit(it.Text, t.Font.Label, rx - x), t.Font.Label, bad ? t.Death : t.Row.Text, lineCss);
    }

    // ---------------------------------------------------------------------------------------------
    // One line: collapsed by hand, between pulls, before any fight.

    /// <summary>The height of the one line (CSS px).</summary>
    public float LineHeight => T.Win.CollapsedH;

    /// <summary>
    /// The one line (round four's collapsed meter): the clock with its dot (the tag when it's over),
    /// the phase, then you and your rank and number, the deaths, the limit break's pips, and a
    /// chevron that opens it. With no fight, <paramref name="empty"/> in its place (and See a sample).
    /// </summary>
    public PlateInput DrawLine(ImDrawListPtr dl, Vector2 min, float w, string? empty = null, bool sample = false, string? why = null)
    {
        var input = new PlateInput { Pressed = -1, Pointed = -1, Item = -1 };
        var t = T;
        var h = S(t.Win.CollapsedH);
        var max = min + new Vector2(w, h);
        Ground(dl, min, max, false);
        var hCss = t.Win.CollapsedH;
        if (t.HeadPlates || t.Id == MeterThemeId.Native)
            dl.AddRectFilled(min + new Vector2(S(3)), max - new Vector2(S(3)), Palette.U32(Rgb.A(t.Row.Plate, t.Row.PlateAlpha)), S(t.Row.Radius));
        if ((t.Drawn(Decorations) & Ornaments.Sparkles) != 0)
        {
            dl.PushClipRectFullScreen();
            PlateArt.Glyph(dl, PlateGlyph.Star, new Vector2(max.X - S(6), min.Y - S(8)), S(13), Rgb.Hex(0xffc94d));
            dl.PopClipRect();
        }
        var cy = min.Y + h / 2;
        var x = min.X + S(t.Id == MeterThemeId.Grimoire ? 12 : 9);
        var rx = max.X - S(t.Id == MeterThemeId.Neon ? 9 : 8);
        var gap = S(t.Id == MeterThemeId.Neon ? 5 : 7);
        // The chevron at the right: opens it.
        var chev = S(11);
        if (Interactive)
        {
            var (clicked, hovered, _) = Kit.Hit("##pm-open", new Vector2(rx - chev - S(6), min.Y), new Vector2(chev + S(12), h));
            if (hovered) Kit.Tooltip(empty != null ? "The meter's settings" : "Open it");
            input.Open = clicked;
        }
        Icons.Draw(dl, Icon.ChevronDown, new Vector2(rx - chev, cy - chev / 2), chev, t.Row.Muted);
        rx -= chev + gap;
        if (empty != null)
        {
            // No fight yet, or stopped: the words, and the sample's link.
            dl.AddCircle(new Vector2(x + S(3), cy), S(2.25f), Palette.U32(t.Row.Faint), 16, S(1.5f));
            x += S(6) + gap;
            if (sample)
            {
                const string see = "See a sample";
                var sw = PlateArt.Width(see, t.Font.Label);
                if (Interactive)
                {
                    var (clicked, hovered, _) = Kit.Hit("##pm-sample", new Vector2(rx - sw, min.Y), new Vector2(sw, h));
                    if (hovered) Kit.Tooltip("A made-up pull, to see the meter mid-fight. A real fight ends it.");
                    input.Sample = clicked;
                }
                PlateArt.Line(dl, new Vector2(rx - sw, min.Y), see, t.Font.Label, t.Link, hCss);
                rx -= sw + gap;
            }
            var ew = PlateArt.Line(dl, new Vector2(x, min.Y), PlateArt.Fit(empty, t.Font.Label, rx - x), t.Font.Label, why != null ? t.Bad : t.Row.Muted, hCss);
            if (why != null && Interactive && Kit.Pointed(new Vector2(x, min.Y), new Vector2(x + ew, max.Y))) Kit.Tooltip(why);
            Scanlines(dl, min, max);
            return input;
        }
        // The left: the clock.
        var clockFont = t.Id is MeterThemeId.Native or MeterThemeId.Grimoire ? t.Font.Num : t.Font.Head;
        if (t.Id == MeterThemeId.Mochi)
        {
            var cw = PlateArt.Width(clock, t.Font.Head);
            var pw = S(6) + S(10) + S(5) + cw + S(8);
            dl.AddRectFilled(new Vector2(x, cy - S(10)), new Vector2(x + pw, cy + S(10)), Palette.U32(Rgb.Hex(0xffdcea)), S(10));
            PlateArt.Glyph(dl, PlateGlyph.Heart, new Vector2(x + S(6), cy - S(5)), S(10), live ? Rgb.Hex(0xff4f9a) : Rgb.Hex(0xd9a7bd));
            PlateArt.Line(dl, new Vector2(x + S(21), min.Y), clock, t.Font.Head, t.Clock, hCss);
            x += pw + gap;
        }
        else if (t.Id == MeterThemeId.Readout)
        {
            x += PlateArt.Line(dl, new Vector2(x, min.Y), "> ", t.Font.Head, t.Accent, hCss);
            x += PlateArt.Line(dl, new Vector2(x, min.Y), clock, t.Font.Head, t.Accent, hCss) + gap;
        }
        else
        {
            if (live) Kit.BeatDot(dl, new Vector2(x + S(3), cy), t.Dot, true);
            else dl.AddCircleFilled(new Vector2(x + S(3), cy), S(3), Palette.U32(t.Row.Faint), 12);
            x += S(6) + gap;
            x += PlateArt.Line(dl, new Vector2(x, min.Y), clock, clockFont, t.Clock, hCss, t.Id == MeterThemeId.Neon ? Rgb.Rgba(61, 236, 255, 0.35f) : null) + gap;
        }
        if (!live && ended is { } o)
        {
            var word = t.Id == MeterThemeId.Readout ? (o == Core.Outcome.Clear ? "[CLEAR]" : "[WIPE]") : o == Core.Outcome.Clear ? "Clear" : "Wipe";
            x += PlateArt.Line(dl, new Vector2(x, min.Y), word, t.Font.Small, o == Core.Outcome.Clear ? (t.Light ? t.Good : t.Row.Text) : t.Bad, hCss) + gap;
        }
        else if (phase is { } ph) x += PlateArt.Line(dl, new Vector2(x, min.Y), ph.Short, t.Font.Small, t.Row.Muted, hCss) + gap;
        // The right, from the chevron: the pips, the deaths, you.
        if (live && Pips is { Length: > 0 } pips)
        {
            var pw = pips.Length * S(6) + (pips.Length - 1) * S(2);
            if (rx - pw > x)
            {
                DrawPips(dl, new Vector2(rx - pw, cy), pips, 6, 4);
                rx -= pw + gap;
            }
        }
        var deathsW = deathCount > 0 ? PlateArt.Width(deaths, t.Font.Small) + (t.Id == MeterThemeId.Readout ? PlateArt.Width("x", t.Font.Small) : S(3) + S(11)) : 0;
        if (deathCount > 0 && rx - deathsW - gap > x)
        {
            if (t.Id == MeterThemeId.Readout) rx -= PlateArt.LineRight(dl, rx, min.Y, "x" + deaths, t.Font.Small, t.Death, hCss) + gap;
            else
            {
                rx -= PlateArt.LineRight(dl, rx, min.Y, deaths, t.Font.Small, t.Death, hCss) + S(3);
                var gs = S(11);
                if (t.DeathDrawn(Decorations) == DeathStyle.Broken) PlateArt.Glyph(dl, PlateGlyph.BrokenHeart, new Vector2(rx - gs, cy - gs / 2), gs, t.Death);
                else if (t.DeathDrawn(Decorations) is DeathStyle.Skull or DeathStyle.Dagger) Icons.Draw(dl, Icon.Skull, new Vector2(rx - gs, cy - gs / 2), gs, t.Death, 2.2f);
                else Icons.Draw(dl, Icon.X, new Vector2(rx - gs, cy - gs / 2), gs, t.Death, 3.2f);
                rx -= gs + gap;
            }
        }
        if (youRank.Length > 0)
        {
            var vw = PlateArt.Width(youValue, t.Font.Num);
            var label = t.Id == MeterThemeId.Readout ? "you " : "You ";
            var lw = PlateArt.Width(label, t.Font.Label) + PlateArt.Width(youRank, t.Font.Label);
            var total = (t.Id == MeterThemeId.Readout ? 0 : S(3) + S(4)) + lw + S(4) + vw;
            if (rx - total > x)
            {
                var ux = rx - total;
                if (t.Id != MeterThemeId.Readout)
                {
                    dl.AddRectFilled(new Vector2(ux, cy - S(6)), new Vector2(ux + S(3), cy + S(6)), Palette.U32(t.You), S(1));
                    ux += S(3) + S(4);
                }
                ux += PlateArt.Line(dl, new Vector2(ux, min.Y), label, t.Font.Label, t.Row.Muted, hCss);
                ux += PlateArt.Line(dl, new Vector2(ux, min.Y), youRank, t.Font.Label, t.Row.Muted, hCss) + S(4);
                PlateArt.Line(dl, new Vector2(ux, min.Y), youValue, t.Font.Num, t.Id == MeterThemeId.Readout ? t.Accent : t.Row.Num, hCss);
            }
        }
        Scanlines(dl, min, max);
        return input;
    }

    // ---------------------------------------------------------------------------------------------
    // The details panel (pointed at a row).

    /// <summary>A member's details as the panel shows them, made once a snapshot for the member pointed at.</summary>
    public sealed class Details
    {
        public MeterEncounter? Of;
        public int PartyId = -1;
        public MeterTab Tab;
        public MeterRow Row = null!;
        public string Name = string.Empty;
        public string Sub = string.Empty;
        public readonly string[] Labels = new string[6];
        public readonly string[] Values = new string[6];
        public readonly List<(string Name, string Pct, string Value, float Bar)> Abilities = new();
        public string Foot = string.Empty;
        public string Link = string.Empty;
    }

    private readonly Details details = new();

    /// <summary>The panel's words for a member (again only for a new snapshot, member or tab).</summary>
    public Details? DetailsFor(MeterEncounter e, IMeterSource m, int partyId)
    {
        var tab = tabOf is MeterTab.Deaths or MeterTab.Events ? MeterTab.Damage : tabOf;
        if (ReferenceEquals(details.Of, e) && details.PartyId == partyId && details.Tab == tab) return details;
        var l = LineOf(partyId);
        if (l == null) return null;
        var r = l.Row;
        var d = details;
        d.Of = e;
        d.PartyId = partyId;
        d.Tab = tab;
        d.Row = r;
        d.Name = JobsOnly ? r.Job : r.Name;
        var where = phase is { } ph ? (T.Id == MeterThemeId.Readout ? " · " + ph.Short : " · in " + ph.Short) : string.Empty;
        // The job, and the spot (the meter's rows show the job's icon in its place).
        d.Sub = (JobsOnly ? r.Slot : r.Job + " · " + r.Slot) + " · #" + l.Rank.ToString(CultureInfo.InvariantCulture) + " of " + lines.Count.ToString(CultureInfo.InvariantCulture) + where;
        static string N(double v) => Format.Thousands((long)Math.Round(v));
        void Set(int i, string label, string value)
        {
            d.Labels[i] = label;
            d.Values[i] = value;
        }
        var dps = rankOf == MeterRank.Dps;
        switch (tab)
        {
            case MeterTab.Healing:
                Set(0, "HPS", N(r.Hps));
                Set(1, "Overheal", Format.PercentWhole(r.OverhealRate));
                Set(2, "Share", Format.Share(r.HealingShare) + "%");
                Set(3, "Healing", MeterNumber.Short(r.Healing));
                // Since 0.9.0: what their barriers soaked (an estimate; the foot says so), in crit's place.
                Set(4, "Shields est.", MeterNumber.Short(r.Absorbed));
                Set(5, "Max heal", MeterNumber.Short(r.MaxHeal));
                break;
            case MeterTab.Taken:
                Set(0, "Taken", MeterNumber.Short(r.Taken));
                Set(1, "A second", N(r.TakenPerSecond));
                Set(2, "Share", Format.Share(r.TakenShare) + "%");
                Set(3, "Max hit", MeterNumber.Short(r.MaxTaken));
                Set(4, "Deaths", MeterText.Count(r.Deaths));
                Set(5, "Time dead", MeterText.Dead(r.DeadSeconds));
                break;
            default:
                Set(0, dps ? "DPS" : "ENCDPS", N(dps ? r.Dps : r.EncDps));
                Set(1, dps ? "ENCDPS" : "DPS", N(dps ? r.EncDps : r.Dps));
                Set(2, "Share", Format.Share(r.DamageShare) + "%");
                Set(3, "Damage", MeterNumber.Short(r.Damage));
                Set(4, "Crit · DH", Math.Round(r.CritRate * 100).ToString("0", CultureInfo.InvariantCulture) + " · " + Math.Round(r.DirectHitRate * 100).ToString("0", CultureInfo.InvariantCulture));
                Set(5, "Max hit", MeterNumber.Short(r.MaxHit));
                break;
        }
        d.Abilities.Clear();
        var abilities = m.Abilities(e.Id, partyId, tab);
        long top = 0;
        foreach (var a in abilities) top = Math.Max(top, a.Amount);
        for (var i = 0; i < abilities.Count && d.Abilities.Count < 4; i++)
        {
            var a = abilities[i];
            d.Abilities.Add((a.IsTick ? a.Name + " (DoT)" : a.Name, Format.Share(a.Share) + "%", MeterNumber.Short(a.Amount), top > 0 ? (float)(a.Amount / (double)top) : 0));
        }
        var readout = T.Id == MeterThemeId.Readout;
        d.Foot = tab == MeterTab.Healing
            ? readout ? "shields: estimated, not healing" : "Shields absorbed are an estimate"
            : readout
            ? $"deaths {r.Deaths} · gcd {r.Casts} · pot {r.Potions}"
            : $"{Words.Count(r.Deaths, "death")} · {r.Casts.ToString(CultureInfo.InvariantCulture)} GCDs · {Words.Count(r.Potions, "potion")}";
        d.Link = readout ? "[click] open full table" : "Click: the full table and recap";
        return d;
    }

    /// <summary>The panel's size (CSS px).</summary>
    public static Vector2 DetailsSize(Details d) => new(236, 10 + 32 + 8 + 70 + 7 + 15 + 18 * Math.Max(1, d.Abilities.Count) + 4 + 16 + 22);

    /// <summary>The details panel at <paramref name="min"/> (its own ground and frame, in the theme).</summary>
    public void DrawDetails(ImDrawListPtr dl, Vector2 min, Details d)
    {
        var t = T;
        var size = DetailsSize(d) * S(1);
        var max = min + size;
        Ground(dl, min, max, false);
        var light = t.Light;
        var line = light ? Rgb.Rgba(90, 50, 60, 0.16f) : t.Id == MeterThemeId.Readout ? Rgb.Hex(0x2c4a35) : Rgb.Rgba(255, 255, 255, 0.09f);
        var x = min.X + S(10);
        var right = max.X - S(10);
        var y = min.Y + S(10);
        var r = d.Row;
        if (t.Id == MeterThemeId.Readout)
            PlateArt.Line(dl, new Vector2(x, y), r.Role == "tank" ? "T" : r.Role == "healer" ? "H" : "D", new ThemeFont(Face.MonoSemibold, 13), Role(r.Role), 20);
        else PlateArt.JobIcon(dl, new Vector2(x, y), 20, r.Job, r.Role, Role(r.Role), t.Icon, r.Dead, t.DeadIcon);
        var tx = x + S(28);
        var nameFont = t.Font.Name with { Face = MeterThemes.Heavier(t.Font.Name.Face), Px = t.Font.Name.Px + 1 };
        PlateArt.Line(dl, new Vector2(tx, y - S(2)), PlateArt.Fit(d.Name, nameFont, right - tx), nameFont, t.Row.Text, 16);
        PlateArt.Line(dl, new Vector2(tx, y + S(14)), PlateArt.Fit(d.Sub, t.Font.Small, right - tx), t.Font.Small, t.Row.Muted, 14);
        if ((t.Drawn(Decorations) & Ornaments.Sparkles) != 0) PlateArt.Glyph(dl, PlateGlyph.Star, new Vector2(right - S(14), y), S(14), Rgb.Hex(0xffc94d));
        y += S(32);
        Kit.Rule(dl, x, right, y, line);
        y += S(8);
        var colW = (right - x - S(16)) / 3;
        var labelFont = t.Id is MeterThemeId.Readout or MeterThemeId.Neon ? t.Font.Small : t.Font.Small with { Px = 9, Caps = true, Cells = 0 };
        for (var i = 0; i < 6; i++)
        {
            var cx = x + (i % 3) * (colW + S(8));
            var cyy = y + (i / 3) * S(35);
            PlateArt.Line(dl, new Vector2(cx, cyy), d.Labels[i], labelFont, t.Row.Faint, 12);
            PlateArt.Line(dl, new Vector2(cx, cyy + S(13)), d.Values[i], t.Font.Num, t.Row.Num, 16);
        }
        y += S(70);
        Kit.Rule(dl, x, right, y, line);
        y += S(7);
        PlateArt.Line(dl, new Vector2(x, y), t.Id == MeterThemeId.Readout ? "# top abilities" : "Top abilities", t.Font.Small, t.Row.Faint, 13);
        y += S(15);
        var rc = Role(r.Role);
        var aw = right - x;
        if (d.Abilities.Count == 0) PlateArt.Line(dl, new Vector2(x, y), "None yet", t.Font.Label, t.Row.Muted, 17);
        foreach (var (name, pct, value, bar) in d.Abilities)
        {
            if (t.Id != MeterThemeId.Readout)
                dl.AddRectFilled(new Vector2(x, y + S(14)), new Vector2(x + aw * bar, y + S(16)), Palette.U32(Rgb.A(rc, light ? 0.8f : 0.85f)));
            var vw = PlateArt.LineRight(dl, right, y, value, t.Font.Small, t.Row.Text, 14);
            var pw = PlateArt.LineRight(dl, right - MathF.Max(vw, S(44)) - S(6), y, pct, t.Font.Small, t.Row.Muted, 14);
            var room = right - MathF.Max(vw, S(44)) - S(12) - pw - x;
            PlateArt.Line(dl, new Vector2(x, y), PlateArt.Fit(name, t.Font.Label, room), t.Font.Label, t.Row.Text, 14);
            y += S(18);
        }
        if (d.Abilities.Count == 0) y += S(18);
        y += S(4);
        PlateArt.Line(dl, new Vector2(x, y), PlateArt.Fit(d.Foot, t.Font.Small, right - x), t.Font.Small, t.Row.Muted, 14);
        y += S(16);
        PlateArt.Line(dl, new Vector2(x, y), d.Link, t.Font.Small, t.Link, 14);
        Scanlines(dl, min, max);
    }
}
