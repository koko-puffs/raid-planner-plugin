using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The meter opened up (the mock-ups' "Opened up" and "A death, and its recap"): the same four tabs
/// full size with icons and the deaths counted; the clock and where in the fight; which pull;
/// ENCDPS or DPS; then a table of every column, a row a player (the picked one under the reticle),
/// the limit break under them and the party at the foot, beside the picked player's numbers by
/// ability (DoT ticks their own row, est. when the game's tick was shared). On Deaths, the deaths and
/// the picked one's recap: its last hits and heals with the HP before each, the killing blow on a red wash.
/// Since 0.7.2 it's drawn in the compact meter's theme (<see cref="MeterTheme"/>), as the meter is: its
/// ground and frame, panels and rows on its plates, its faces (<see cref="DetailFonts"/>, columns as wide as
/// the words they show), its role colours and job icons, its bar for the abilities, its mark for the
/// picked row and the dead, its chips and accent for the tabs, the segments and the pull menu. Ready
/// Check is the window as it was.
/// </summary>
public sealed class MeterDetailWindow : Window
{
    private readonly Configuration config;
    private readonly Fonts fonts;
    private DateTime drawFailedAt;

    /// <summary>The theme's drawer: its ground, its bars, its marks (never pressed: the window's own controls are).</summary>
    private readonly PlateMeter plate = new() { Interactive = false };

    private (MeterThemeId Id, DetailFonts Fonts, ThemeInk Ink) look = (MeterThemeId.ReadyCheck, DetailFonts.ReadyCheck, ThemeInk.Of(MeterThemes.ReadyCheck));

    /// <summary>The meter's theme (the compact meter's since 0.7.2, the normal meter's too since 0.7.3).</summary>
    private MeterTheme T => MeterThemes.Of(config.MeterTheme);

    /// <summary>Ready Check: the window as it was (the kit's controls).</summary>
    private bool Rc => T.Id == MeterThemeId.ReadyCheck;

    private DetailFonts F => Look().Fonts;

    private ThemeInk I => Look().Ink;

    private (MeterThemeId Id, DetailFonts Fonts, ThemeInk Ink) Look()
    {
        var t = T;
        if (look.Id != t.Id) look = (t.Id, DetailFonts.For(t), ThemeInk.Of(t));
        return look;
    }

    private static readonly string[] Ranks = { "ENCDPS", "DPS" };

    private MeterTab tab = MeterTab.Damage;
    private int? fight;
    private int? player;
    private int? death;

    /// <summary>The numbers' source last drawn: when it changes (the meter's sample starts or ends) the picks made in the other are let go.</summary>
    private IMeterSource? lastSource;

    /// <summary>When the fight was copied as text (the button says so for a moment).</summary>
    private DateTime copiedAt;

    public MeterDetailWindow(Configuration config, Fonts fonts)
        : base("Raid Codex meter###RaidPlannerMeterDetail")
    {
        this.config = config;
        this.fonts = fonts;
        Size = Theme.Units(new Vector2(1180, 560));
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = Limits.Within(Vector2.Zero, UiScale.WasTotal);
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse;
    }

    /// <summary>Where the numbers come from (the small meter's own).</summary>
    public Func<IMeterSource?>? Source { get; set; }

    /// <summary>Back to the small meter.</summary>
    public Action? ShowSmall { get; set; }

    /// <summary>Opens on a tab, a fight (null: the current), a player and a death.</summary>
    public void Show(MeterTab on, int? fightId, int? partyId, int? deathIndex)
    {
        tab = MeterLayout.Known(on);
        fight = fightId;
        player = partyId;
        death = deathIndex;
        lastSource = Source?.Invoke();
        IsOpen = true;
        BringToFront();
    }

    /// <summary>At least as wide as the five tabs and the head's buttons side by side; the table and the abilities sit side by side from about 1100 (CSS px).</summary>
    private static readonly CssLimits Limits = new(new Vector2(980, 360), new Vector2(2400, 1600));

    /// <summary>Grows or shrinks it with the windows' size (Settings → Windows → Size).</summary>
    private readonly ScaleFollower follower = new();

    /// <summary>Its size as last drawn (screen px).</summary>
    private Vector2 drawnSize;

    /// <summary>Size was given for one frame (the windows' size moved): ImGui's again from the next.</summary>
    private bool sizedOnce;

    public override void PreDraw()
    {
        var vp = ImGui.GetMainViewport().Size;
        if (sizedOnce)
        {
            sizedOnce = false;
            SizeCondition = ImGuiCond.FirstUseEver;
        }
        if (follower.Resize(drawnSize, vp) is { } resized)
        {
            Size = resized;
            SizeCondition = ImGuiCond.Always;
            sizedOnce = true;
        }
        SizeConstraints = Limits.Within(vp, follower.Seen);
        // A theme draws its own ground and frame (as the compact meter does); Ready Check's is the kit's window.
        if (Rc) Theme.PushFrame();
        else Theme.PushFrame(Vector4.Zero, T.Win.Radius, Vector4.Zero);
    }

    public override void PostDraw() => Theme.Pop();

    public override void Draw()
    {
        drawnSize = ImGui.GetWindowSize();
        Kit.ResetFrame();
        Frame.ResetFrame();
        Theme.PushInside();
        try
        {
            MeterWindow.WantFonts(fonts, config.MeterTheme);
            using (fonts.Push(Face.Sans, 14))
            {
                try
                {
                    Frame.Window();
                    if (!Rc)
                    {
                        // The theme's ground and frame under the title bar (opaque here; its sparkles left out: lighter than the meter).
                        MeterWindow.Configure(plate, config);
                        plate.Background = 1f;
                        var top = ImGui.GetCursorScreenPos();
                        var win = ImGui.GetWindowPos();
                        plate.Ground(Kit.Dl, new Vector2(win.X, top.Y), win + ImGui.GetWindowSize(), false);
                    }
                    DrawInside();
                }
                finally
                {
                    Frame.Unwind();
                }
            }
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - drawFailedAt > TimeSpan.FromSeconds(10))
            {
                drawFailedAt = DateTime.UtcNow;
                Services.Log.Error(ex, "Raid Codex's meter couldn't draw");
            }
        }
        finally
        {
            Theme.Pop();
        }
    }

    private void DrawInside()
    {
        var m = Source?.Invoke();
        if (!ReferenceEquals(m, lastSource))
        {
            // A sample's ids mean nothing in the real numbers (nor the other way): back to the current fight.
            lastSource = m;
            fight = null;
            player = null;
            death = null;
        }
        var e = fight is { } id ? m?.Get(id) ?? m?.Current : m?.Current;
        var win = ImGui.GetWindowPos();
        var top = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth();
        var dl = Kit.Dl;
        var left = win.X + S(18);
        var inner = w - S(36);
        var y = top.Y + S(12);

        // The tabs, with icons and the deaths and events counted.
        var deaths = e != null ? MeterView.Deaths(e).Count : 0;
        var events = e != null && m != null ? m.Events(e.Id) : Array.Empty<MeterEvent>();
        var tabs = new[]
        {
            new TabSpec("Damage", Icon.Sword), new TabSpec("Healing", Icon.Heart), new TabSpec("Taken", Icon.Shield),
            new TabSpec("Deaths", Icon.Skull, deaths > 0 ? deaths.ToString(System.Globalization.CultureInfo.InvariantCulture) : null),
            new TabSpec("Events", Icon.List, events.Count > 0 ? events.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : null),
        };
        tab = (MeterTab)(Rc ? Kit.Tabs("##detail-tab", new Vector2(left, y), tabs, (int)tab, TabSize.Md) : ThemedTabs(dl, new Vector2(left, y), tabs, (int)tab, out _));
        var tabsWidth = Rc ? Kit.TabsWidth(tabs, TabSize.Md) : ThemedTabsWidth(tabs);

        // The right of the row: Small, ENCDPS or DPS, which pull, a rule, where in the fight, the clock.
        var cy = y + S(H) / 2;
        var x = left + inner;
        const string small = "Small";
        var sw = Rc ? Kit.ButtonWidth(small, BtnSize.Sm, Icon.PanelRight, Btn.Quiet) : ChipWidth(small, Icon.PanelRight);
        x -= sw;
        if (Rc ? Kit.Button("##detail-small", new Vector2(x, cy - S(HSm) / 2), small, Btn.Quiet, BtnSize.Sm, Icon.PanelRight, 0, false, "Back to the small meter")
                : Chip(dl, "##detail-small", new Vector2(x, cy - S(HSm) / 2), small, Icon.PanelRight, "Back to the small meter", sw))
        {
            IsOpen = false;
            ShowSmall?.Invoke();
        }
        // Copy as text: the fight shown as a mini-parse for chat (it says Copied for a moment).
        var copied = DateTime.UtcNow - copiedAt < TimeSpan.FromSeconds(2.5);
        x -= S(8) + S(HSm);
        var copyTip = copied ? "Copied to the clipboard" : "Copy as text: the fight, its length, each player's ENCDPS, share and deaths, a line each (for chat)";
        var copy = Rc
            ? Kit.IconButton("##detail-copy", new Vector2(x, cy - S(HSm) / 2), copied ? Icon.Check : Icon.Copy, copyTip, Btn.Quiet, BtnSize.Sm, e == null)
            : Chip(dl, "##detail-copy", new Vector2(x, cy - S(HSm) / 2), string.Empty, copied ? Icon.Check : Icon.Copy, copyTip, S(HSm), e == null);
        if (copy && e != null)
        {
            ImGui.SetClipboardText(MeterText.Parse(e, config.MeterJobsOnly));
            copiedAt = DateTime.UtcNow;
        }
        var rw = Rc ? Kit.SegmentedWidth(Ranks) : ThemedSegmentsWidth(Ranks);
        x -= S(12) + rw;
        var rank = Rc ? Kit.Segmented("##detail-rank", new Vector2(x, cy - S(HSm) / 2), Ranks, (int)config.MeterRank)
            : ThemedSegments(dl, "##detail-rank", new Vector2(x, cy - S(HSm) / 2), Ranks, (int)config.MeterRank);
        if (rank != (int)config.MeterRank)
        {
            config.MeterRank = (MeterRank)rank;
            config.Save();
        }
        if (e != null && m != null)
        {
            // The picker's button: its words, a gap, the chevron (Dropdown: the chevron never touches the words).
            var pick = e.Live ? $"Pull {e.Pull} · now" : $"Pull {e.Pull}";
            var pad = S(11.2f);
            var chev = S(15);
            var wordsW = Rc ? Txt.Width(pick, Face.SansMedium, 13) : PlateArt.Width(pick, F.BodyStrong);
            var pw = Dropdown.Width(wordsW, chev, pad, S(1));
            x -= S(12) + pw;
            var bp = new Vector2(x, cy - S(HSm) / 2);
            if (Rc)
            {
                if (Kit.Button("##detail-pick", bp, string.Empty, Btn.Outline, BtnSize.Sm, null, pw)) ImGui.OpenPopup("##detail-picker");
                Txt.Line(dl, new Vector2(x + pad, bp.Y), pick, Face.SansMedium, 13, Palette.White, HSm);
                Icons.Draw(dl, Icon.ChevronDown, new Vector2(x + Dropdown.ChevronAt(wordsW, pad, S(1)), cy - chev / 2), chev, Palette.White);
            }
            else
            {
                if (Chip(dl, "##detail-pick", bp, string.Empty, null, null, pw)) ImGui.OpenPopup("##detail-picker");
                PlateArt.Line(dl, new Vector2(x + pad, bp.Y), pick, F.BodyStrong, I.Strong, HSm);
                Icons.Draw(dl, Icon.ChevronDown, new Vector2(x + Dropdown.ChevronAt(wordsW, pad, S(1)), cy - chev / 2), chev, I.Muted);
            }
            DrawPicker(m, e);
            // A rule, then where in the fight and the clock, as they fit beside the tabs (the clock first).
            var tabsRight = left + tabsWidth + S(12);
            x -= S(12);
            dl.AddLine(new Vector2(x, cy - S(10)), new Vector2(x, cy + S(10)), Palette.U32(Rc ? Palette.Line2 : I.Rule), 1f);
            var clock = Format.Clock(e.Duration);
            var cw = PlateArt.Width(clock, F.Clock);
            var clockRoom = S(12) + cw + S(7) + S(6);
            if (MeterView.Where(e) is { } where)
            {
                var ww = PlateArt.Width(where, F.Body);
                if (x - S(12) - ww - clockRoom >= tabsRight)
                {
                    x -= S(12) + ww;
                    PlateArt.Line(dl, new Vector2(x, y), where, F.Body, I.Muted, H);
                }
            }
            if (x - clockRoom >= tabsRight)
            {
                x -= S(12) + cw;
                PlateArt.Line(dl, new Vector2(x, y), clock, F.Clock, Rc ? Palette.White : T.Clock, H);
                Kit.BeatDot(dl, new Vector2(x - S(7) - S(3), cy), e.Live ? (Rc ? Palette.Ember : T.Dot) : I.Faint, e.Live);
            }
        }
        y += S(H) + S(12);

        var footH = S(19.5f) * 2 + S(10) + S(14);
        var bottom = win.Y + ImGui.GetWindowHeight() - footH;
        if (e == null)
        {
            var c = new Vector2(win.X + w / 2, y + (bottom - y) / 2 - S(30));
            Boss.Empty(dl, c, 58, BossMood.Asleep, 96);
            if (Rc) Kit.Callout(dl, new Vector2(left, c.Y + S(58)), "No fight yet", 13, inner, 1, 1.1f, center: true);
            else
            {
                const string none = "No fight yet";
                PlateArt.Line(dl, new Vector2(left + (inner - PlateArt.Width(none, F.Title)) / 2, c.Y + S(58)), none, F.Title, I.Strong, 24);
            }
            return;
        }

        if (tab == MeterTab.Deaths)
        {
            // The deaths beside the picked one's recap (the recap has the more to say).
            var listW = MathF.Round(inner * 0.4f);
            var recapX = left + listW + S(18);
            DrawDeaths(dl, e, new Vector2(left, y), listW, bottom - y, new Vector2(recapX, y), left + inner - recapX);
        }
        else if (tab == MeterTab.Events)
        {
            // Each player's events counted beside the fight's events, oldest first.
            var leftW = MathF.Round(MathF.Min(MathF.Max(inner * 0.6f, S(600)), inner - S(18) - S(300)));
            DrawTable(dl, m!, e, new Vector2(left, y), leftW, bottom - y);
            var rightX = left + leftW + S(18);
            DrawEventLog(dl, e, events, new Vector2(rightX, y), left + inner - rightX, bottom - y);
        }
        else
        {
            // The table beside the picked player by ability when there's room for both (the table's
            // columns want about 700, the abilities' 400); narrower, the table alone, all its columns readable.
            var both = inner >= S(1060);
            var leftW = both ? MathF.Round(Math.Clamp(inner * 0.6f, S(640), inner - S(18) - S(400))) : inner;
            DrawTable(dl, m!, e, new Vector2(left, y), leftW, bottom - y);
            if (both)
            {
                var rightX = left + leftW + S(18);
                DrawAbilities(dl, m!, e, new Vector2(rightX, y), left + inner - rightX, bottom - y);
            }
        }

        // The footnote: what Crit, DH and DPS mean.
        var note = tab switch
        {
            MeterTab.Healing => "Healing is what the heals put back: the heal less the overheal. Shields aren't healing, so a scholar reads low here, as in ACT.",
            MeterTab.Taken => "Everything enemies and the floor did to each of you, bleeds and DoTs included: what the game took off your HP.",
            MeterTab.Deaths => "The wipe isn't anyone's death: the deaths in a wipe's last six seconds aren't counted against anyone. A raise is the dead member's HP coming back; who raised them is whose Raise was on them.",
            MeterTab.Events => "Casts are the GCDs used. Interrupts are the enemy casts stopped, credited to whoever's interrupt hit it in the second before (the game doesn't say who). Time dead leaves out the wipe.",
            _ => "Crit and DH are the share of hits. DPS is each player's damage over their own time, from their first hit to their last: it's higher than ENCDPS for anyone who stopped.",
        };
        Note(dl, new Vector2(left, bottom + S(10)), inner, note);
    }

    /// <summary>A note in small words, wrapped (Ready Check's the kit's flow; a theme's in its face, its muted ink).</summary>
    private void Note(ImDrawListPtr dl, Vector2 at, float width, string note)
    {
        if (Rc)
        {
            Txt.Flow(at, width, new[] { Run.Note(note) }, 19.5f, out _);
            return;
        }
        var f = F.Note;
        var key = (note, MathF.Round(width), f, Txt.Fonts.Generation);
        if (!noteLines.TryGetValue(key, out var lines))
        {
            if (noteLines.Count > 16) noteLines.Clear();
            // Wrapped a little short of the width: an outlined face's tracking is drawn on top of its advance.
            noteLines[key] = lines = Txt.Wrap(note, f.Face, f.Px, width - (f.Outline ? S(1) * 24 : 0));
        }
        var y = at.Y;
        foreach (var line in lines)
        {
            PlateArt.Line(dl, new Vector2(at.X, y), line, f, I.Muted, 19.5f);
            y += S(19.5f);
        }
    }

    /// <summary>The notes' lines, wrapped again only for other words, a width, a face or a font build.</summary>
    private readonly Dictionary<(string, float, ThemeFont, int), List<string>> noteLines = new();

    // ---------------------------------------------------------------------------------------------
    // A theme's controls (Ready Check's are the kit's).

    /// <summary>A tab's width in the theme: its icon, its word, its count.</summary>
    private float ThemedTabWidth(TabSpec tab) =>
        S(12) * 2 + S(Kit.IconPx + 7.2f) + PlateArt.Width(tab.Label, F.BodyStrong) + (tab.Count != null ? S(7.2f) + PlateArt.Width(tab.Count, F.Small) : 0);

    private float ThemedTabsWidth(IReadOnlyList<TabSpec> tabs)
    {
        var w = S(3.2f) * (tabs.Count - 1);
        foreach (var t in tabs) w += ThemedTabWidth(t);
        return w;
    }

    /// <summary>The tabs in the theme: its words, the one you're on in its strongest ink with its accent under it, pointed at on its hover wash.</summary>
    private int ThemedTabs(ImDrawListPtr dl, Vector2 at, IReadOnlyList<TabSpec> tabs, int selected, out float width)
    {
        var h = S(H);
        var x = at.X;
        var result = selected;
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            var w = ThemedTabWidth(tab);
            var p = new Vector2(x, at.Y);
            var (clicked, hovered, _) = Kit.Hit("##detail-tab" + i, p, new Vector2(w, h));
            if (clicked) result = i;
            var on = i == selected;
            if (hovered) dl.AddRectFilled(p, p + new Vector2(w, h), Palette.U32(I.Hover), S(I.RowRadius));
            var ink = on ? I.Strong : hovered ? I.Text : I.Muted;
            var tx = p.X + S(12);
            if (tab.Icon is { } ic) Icons.Draw(dl, ic, new Vector2(tx, p.Y + (h - S(Kit.IconPx)) / 2), S(Kit.IconPx), on ? I.Accent : ink, 1.75f);
            tx += S(Kit.IconPx + 7.2f);
            tx += PlateArt.Line(dl, new Vector2(tx, p.Y), tab.Label, F.BodyStrong, ink, H);
            if (tab.Count != null) PlateArt.Line(dl, new Vector2(tx + S(7.2f), p.Y), tab.Count, F.Small, I.Faint, H);
            if (on) dl.AddRectFilled(new Vector2(p.X + S(12), p.Y + h - S(2) - S(3)), new Vector2(p.X + w - S(12), p.Y + h - S(2)), Palette.U32(I.Accent), S(1.5f));
            x += w + S(3.2f);
        }
        width = x - at.X;
        return result;
    }

    /// <summary>A chip button's width in the theme: padding, an icon, its words.</summary>
    private float ChipWidth(string label, Icon? icon) =>
        label.Length == 0 ? S(HSm) : S(11.2f) * 2 + PlateArt.Width(label, F.BodyStrong) + (icon != null ? S(Kit.IconPx + 8) : 0);

    /// <summary>A button in the theme's chip (its hairline and corners; pointed at, its hover wash): an icon and words, or an icon alone, or nothing (the caller draws on it). Returns pressed.</summary>
    private bool Chip(ImDrawListPtr dl, string id, Vector2 pos, string label, Icon? icon, string? tip, float width, bool disabled = false)
    {
        var size = new Vector2(width, S(HSm));
        var (clicked, hovered, _) = Kit.Hit(id, pos, size, disabled);
        if (tip != null && Kit.Pointed(pos, pos + size)) Kit.Tooltip(tip);
        var r = S(MathF.Min(I.ChipRadius + 2, 10));
        if (hovered) dl.AddRectFilled(pos, pos + size, Palette.U32(I.Hover), r);
        dl.AddRect(pos + new Vector2(0.5f), pos + size - new Vector2(0.5f), Palette.U32(I.Chip), r, ImDrawFlags.None, 1f);
        var ink = disabled ? I.Faint : hovered ? I.Strong : I.Text;
        var content = (icon != null ? S(Kit.IconPx) : 0) + (icon != null && label.Length > 0 ? S(8) : 0) + (label.Length > 0 ? PlateArt.Width(label, F.BodyStrong) : 0);
        var x = pos.X + (width - content) / 2;
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(x, pos.Y + (size.Y - S(Kit.IconPx)) / 2), S(Kit.IconPx), ink);
            x += S(Kit.IconPx) + (label.Length > 0 ? S(8) : 0);
        }
        if (label.Length > 0) PlateArt.Line(dl, new Vector2(x, pos.Y), label, F.BodyStrong, ink, HSm);
        return clicked && !disabled;
    }

    private float ThemedSegmentsWidth(IReadOnlyList<string> items)
    {
        var widest = 0f;
        foreach (var s in items) widest = MathF.Max(widest, PlateArt.Width(s, F.BodyStrong));
        return S(3) * 2 + S(2) * (items.Count - 1) + items.Count * (widest + S(10) * 2) + 2;
    }

    /// <summary>Segments in the theme: a chip's hairline round them, the one picked on its accent's wash in its accent.</summary>
    private int ThemedSegments(ImDrawListPtr dl, string id, Vector2 pos, IReadOnlyList<string> items, int selected)
    {
        var h = S(HSm);
        var total = ThemedSegmentsWidth(items);
        var r = S(MathF.Min(I.ChipRadius + 2, 10));
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(total, h) - new Vector2(0.5f), Palette.U32(I.Chip), r, ImDrawFlags.None, 1f);
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
            if (on) dl.AddRectFilled(p, p + size, Palette.U32(Rgb.A(I.Accent, T.Light ? 0.16f : 0.2f)), MathF.Max(0, r - S(3)));
            else if (hovered) dl.AddRectFilled(p, p + size, Palette.U32(I.Hover), MathF.Max(0, r - S(3)));
            var tw = PlateArt.Width(items[i], F.BodyStrong);
            PlateArt.Line(dl, new Vector2(p.X + (size.X - tw) / 2, p.Y), items[i], F.BodyStrong, on ? I.Accent : hovered ? I.Text : I.Muted, size.Y / S(1));
            x += itemW + S(2);
        }
        return result;
    }

    /// <summary>A panel (`.xg`, `.ab`): Ready Check's slab, a theme's panel a shade off its ground, in its hairline.</summary>
    private void Panel(ImDrawListPtr dl, Vector2 at, float w, float h)
    {
        var r = S(I.PanelRadius);
        dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(I.Panel), r);
        dl.AddRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(I.PanelEdge), r, ImDrawFlags.None, 1f);
    }

    /// <summary>A row's ground in the theme: its plate (and its rule, Grimoire's), pointed at its hover wash; Ready Check's rows have none but the hover.</summary>
    private void RowGround(ImDrawListPtr dl, Vector2 min, Vector2 max, bool hovered)
    {
        var t = T;
        var rad = S(I.RowRadius);
        if (!Rc && t.Row.PlateAlpha > 0)
        {
            dl.AddRectFilled(min, max, Palette.U32(Rgb.A(t.Row.Plate, t.Row.PlateAlpha)), rad);
            if (t.Row.PlateEdge is { } pe) dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(pe), rad, ImDrawFlags.None, 1f);
        }
        if (!Rc && t.Row.Rule is { } rule) Kit.Rule(dl, min.X, max.X, max.Y, rule);
        if (hovered) dl.AddRectFilled(min, max, Palette.U32(I.Hover), rad);
    }

    /// <summary>The picked row: Ready Check's white wash under the reticle; a theme's mark for you (reverse video: its colour's wash and outline).</summary>
    private void Picked(ImDrawListPtr dl, string id, Vector2 min, Vector2 max, bool picked, bool hovered)
    {
        if (Rc)
        {
            if (picked) dl.AddRectFilled(min, max, Palette.U32(Palette.Rgba(255, 255, 255, 0.06f)), S(Palette.RadiusSm));
            Kit.Reticle(dl, id, min, max, picked || hovered);
            return;
        }
        if (!picked) return;
        var style = T.YouDrawn(config.MeterDecorations);
        var rad = S(I.RowRadius);
        if (style == YouStyle.Invert)
        {
            dl.AddRectFilled(min, max, Palette.U32(Rgb.A(T.You, 0.16f)), rad);
            dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(T.You), rad, ImDrawFlags.None, 1f);
            return;
        }
        // Mochi's heart is a ring here (no heart's place in a table row).
        plate.YouMark(dl, min, max, rad, max.Y - min.Y, style);
    }

    /// <summary>A player's mark at a size: Ready Check's spot badge (its centre); a theme's job icon in its socket (from its top-left).</summary>
    private void Badge(ImDrawListPtr dl, Vector2 center, float size, string? job, string? role, bool dead, string? slot)
    {
        if (Rc) PlateArt.JobBadge(dl, center, size, job, role, config.MeterRoleColours, dead, slot);
        else PlateArt.JobIcon(dl, center - new Vector2(S(size) / 2), size, job, role, T.RoleColor(role, config.MeterRoleColours), T.Icon, dead, T.DeadIcon);
    }

    /// <summary>A column head: Ready Check's spaced mono capitals (the kit's label); a theme's small face in capitals.</summary>
    private void HeadWords(ImDrawListPtr dl, float x, float lineTop, string text, ThemeFont f, float lineCss, bool right = false)
    {
        var w = HeadWidth(text, f);
        var at = right ? x - w : x;
        if (Rc) Kit.Label(dl, new Vector2(at, lineTop + (S(lineCss) - Txt.Natural(f.Face, f.Px)) / 2), text, f.Px, 0.08f, Palette.Faint);
        else PlateArt.Line(dl, new Vector2(at, lineTop), text, f, I.Faint, lineCss);
    }

    private float HeadWidth(string text, ThemeFont f) => Rc ? Txt.SpacedWidth(Txt.Caps(text), f.Face, f.Px, 0.08f) : PlateArt.Width(text, f);

    /// <summary>Measures as drawn: a head (capitals) as <see cref="HeadWords"/> draws it, else in the face.</summary>
    private float Measure(string text, ThemeFont f) => f.Caps ? HeadWidth(text, f) : PlateArt.Width(text, f);

    // ---------------------------------------------------------------------------------------------
    // The table.

    private sealed record Col(string Head, float Width, Func<MeterRow, string> Text, bool Strong = false, Func<MeterRow, bool>? Red = null);

    private Col[] Columns(MeterEncounter e) => tab switch
    {
        MeterTab.Healing => new Col[]
        {
            new("HPS", 62, r => Format.Thousands((long)Math.Round(r.Hps)), true),
            new("Healing", 80, r => Format.Thousands(r.Healing)),
            new("Over", 44, r => Format.PercentWhole(r.OverhealRate)),
            new("%", 42, r => Format.Share(r.HealingShare)),
            new("Crit", 44, r => Format.Share(r.HealCritRate)),
            new("Max heal", 68, r => Format.Thousands(r.MaxHeal)),
            new("Raises", 46, r => MeterText.Count(r.RaisesGiven)),
        },
        MeterTab.Taken => new Col[]
        {
            new("Taken", 80, r => Format.Thousands(r.Taken), true),
            new("A second", 62, r => Format.Thousands((long)Math.Round(r.TakenPerSecond))),
            new("%", 42, r => Format.Share(r.TakenShare)),
            new("Max hit", 68, r => Format.Thousands(r.MaxTaken)),
            new("Deaths", 40, r => r.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture), Red: r => r.Deaths > 0),
            new("Time dead", 62, r => MeterText.Dead(r.DeadSeconds), Red: r => r.DeadSeconds > 0),
        },
        MeterTab.Events => new Col[]
        {
            new("Casts", 46, r => MeterText.Count(r.Casts), true),
            new("Potions", 50, r => MeterText.Count(r.Potions)),
            new("Interrupts", 66, r => MeterText.Count(r.Interrupts)),
            new("Raises", 46, r => MeterText.Count(r.RaisesGiven)),
            new("Raised", 46, r => MeterText.Count(r.Raised)),
            new("Time dead", 62, r => MeterText.Dead(r.DeadSeconds), Red: r => r.DeadSeconds > 0),
        },
        _ => new Col[]
        {
            new("ENCDPS", 62, r => Format.Thousands((long)Math.Round(r.EncDps)), config.MeterRank == MeterRank.EncDps),
            new("DPS", 60, r => r.IsLimitBreak ? "–" : Format.Thousands((long)Math.Round(r.Dps)), config.MeterRank == MeterRank.Dps),
            new("Damage", 80, r => Format.Thousands(r.Damage)),
            new("%", 42, r => Format.Share(r.DamageShare)),
            new("Crit", 44, r => r.IsLimitBreak ? "–" : Format.Share(r.CritRate)),
            new("DH", 44, r => r.IsLimitBreak ? "–" : Format.Share(r.DirectHitRate)),
            new("Max hit", 68, r => Format.Thousands(r.MaxHit)),
            new("Deaths", 40, r => r.IsLimitBreak ? string.Empty : r.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture), Red: r => r.Deaths > 0),
        },
    };

    /// <summary>
    /// The table (`.xg`, a slab): heads in mono capitals, a 34px row a player (the spot, the name, the
    /// job in mono, the columns in mono, the main number white), the limit break, the party's total
    /// under a stronger rule. The picked row on a white wash under the reticle; a death's name muted with a skull.
    /// </summary>
    private void DrawTable(ImDrawListPtr dl, IMeterSource m, MeterEncounter e, Vector2 at, float w, float h)
    {
        Panel(dl, at, w, h);
        var (cols, list, cells, partyCells) = TableOf(e);
        var left = at.X + S(6) + S(12);
        var right = at.X + w - S(6) - S(12);
        var gap = S(10);
        // The columns from the right: each right-aligned at its edge, as wide as its widest words shown.
        var widths = ColumnWidths(cols, list, cells, partyCells);
        var edges = new float[cols.Length];
        var x = right;
        for (var i = cols.Length - 1; i >= 0; i--)
        {
            edges[i] = x;
            x -= widths[i] + gap;
        }
        var jobX = x - S(34) + S(10);
        var nameX = left + S(20) + gap;
        var y = at.Y + S(4);
        // Heads.
        HeadWords(dl, nameX, y, "Player", F.Head, 30);
        HeadWords(dl, jobX, y, "Job", F.Head, 30);
        for (var i = 0; i < cols.Length; i++) HeadWords(dl, edges[i], y, cols[i].Head, F.Head, 30, true);
        y += S(30);
        Kit.Rule(dl, at.X + S(6), at.X + w - S(6), y - 1, I.Rule);
        // The player picked, else (none yet, or not in this fight: another pull picked) the top one.
        if (list.Count > 0 && !list[0].IsLimitBreak && !list.Exists(r => r.PartyId == player && !r.IsLimitBreak)) player = list[0].PartyId;
        var rowH = S(34);
        for (var i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (y + rowH > at.Y + h - S(40)) break;
            var min = new Vector2(at.X + S(6), y);
            var max = new Vector2(at.X + w - S(6), y + rowH);
            var picked = r.PartyId == player && !r.IsLimitBreak;
            var (clicked, hovered, _) = Kit.Hit($"##xg{r.PartyId}{r.IsLimitBreak}", min, max - min, r.IsLimitBreak);
            if (clicked) player = r.PartyId;
            // A theme's rows on its plates, 2 apart; Ready Check's on the slab with hairlines between.
            var plateMin = Rc ? min : min + new Vector2(0, S(1));
            var plateMax = Rc ? max : max - new Vector2(0, S(1));
            if (Rc && !picked && !hovered && i > 0 && list[i - 1].PartyId != player) Kit.Rule(dl, min.X, max.X, y - 0.5f);
            RowGround(dl, plateMin, plateMax, hovered && !picked);
            Picked(dl, $"##xg-ret{r.PartyId}", plateMin, plateMax, picked, hovered);
            if (r.IsLimitBreak)
            {
                if (Rc) Kit.LimitMark(dl, new Vector2(left, y + (rowH - S(18)) / 2));
                else PlateArt.Glyph(dl, T.Id == MeterThemeId.Mochi ? PlateGlyph.Star : PlateGlyph.LimitBreak, new Vector2(left + S(3), y + (rowH - S(14)) / 2), S(14), T.Lb);
            }
            else Badge(dl, new Vector2(left + S(10), y + rowH / 2), 20, r.Job, r.Role, r.Dead, r.Slot);
            var name = r.IsLimitBreak ? "Limit break" : config.MeterJobsOnly ? r.Job : r.Name;
            var deathStyle = T.DeathDrawn(config.MeterDecorations);
            var nameRoom = jobX - S(10) - nameX - (r.Dead ? S(18) : 0);
            var nameText = PlateArt.Fit(name, F.Name, nameRoom);
            var nameInk = r.Dead ? I.Muted : I.Strong;
            if (r.Dead && !Rc && deathStyle == DeathStyle.Glitch)
            {
                PlateArt.Line(dl, new Vector2(nameX - 1, y), nameText, F.Name, Rgb.A(T.Death, PlateMeter.GlitchAlpha), 34);
                PlateArt.Line(dl, new Vector2(nameX + 1, y), nameText, F.Name, Rgb.Rgba(61, 236, 255, PlateMeter.GlitchAlpha), 34);
            }
            var nw = PlateArt.Line(dl, new Vector2(nameX, y), nameText, F.Name, nameInk, 34);
            if (r.Dead)
            {
                if (Rc) Icons.Draw(dl, Icon.Skull, new Vector2(nameX + nw + S(6), y + (rowH - S(12)) / 2), S(12), Palette.Danger);
                else
                {
                    if (deathStyle is DeathStyle.Dagger or DeathStyle.Tag)
                        dl.AddLine(new Vector2(nameX, y + rowH / 2 + S(0.5f)), new Vector2(nameX + nw, y + rowH / 2 + S(0.5f)), Palette.U32(T.Death), S(1.5f));
                    var mark = deathStyle == DeathStyle.Tag ? DeathStyle.Cross : deathStyle;
                    var gs = S(mark == DeathStyle.Broken ? 13 : mark == DeathStyle.Skull ? 12 : 11);
                    PlateMeter.DeathMark(dl, nameX + nw + S(6) + gs, y, rowH, mark, T.Death, F.Num);
                }
            }
            // The limit break's own action after its name, quieter (the mock-up's "LB2 at 0:58": the model has its biggest hit's).
            if (r.IsLimitBreak && r.MaxHitName.Length > 0 && nameRoom - nw - S(8) > S(30))
                PlateArt.Line(dl, new Vector2(nameX + nw + S(8), y), PlateArt.Fit(r.MaxHitName, F.Note, nameRoom - nw - S(8)), F.Note, I.Muted, 34);
            if (!r.IsLimitBreak && !config.MeterJobsOnly)
            {
                if (Rc) Kit.Label(dl, new Vector2(jobX, y + (rowH - Txt.Natural(Face.Mono, 10)) / 2), r.Job, 10, 0.06f, Palette.Faint);
                else PlateArt.Line(dl, new Vector2(jobX, y), r.Job, F.Head, I.Faint, 34);
            }
            for (var c = 0; c < cols.Length; c++)
            {
                var col = cols[c];
                var color = col.Red?.Invoke(r) == true ? I.Bad : col.Strong ? I.Strong : I.Text;
                PlateArt.LineRight(dl, edges[c], y, cells[i][c], col.Strong ? F.NumStrong : F.Num, color, 34);
            }
            y += rowH;
        }
        // The party at the foot.
        Kit.Rule(dl, at.X + S(6), at.X + w - S(6), y, I.StrongRule);
        PlateArt.Line(dl, new Vector2(nameX, y), "Party", F.Name, I.Strong, 34);
        for (var c = 0; c < cols.Length; c++)
            PlateArt.LineRight(dl, edges[c], y, partyCells[c], cols[c].Strong ? F.NumStrong : F.Num, I.Strong, 34);
    }

    /// <summary>The table's columns' widths (screen px), from the widest words each shows (head, cells, the party's) in the faces drawn; again for a new table, theme, scale or font build.</summary>
    private float[] ColumnWidths(Col[] cols, List<MeterRow> rows, string[][] cells, string[] party)
    {
        var key = (table.Tab, table.Rank, T.Id, S(1), Txt.Fonts.Generation);
        if (ReferenceEquals(columnWidthsOf, table.Of) && columnWidths.Key == key && columnWidths.Widths.Length == cols.Length) return columnWidths.Widths;
        var widths = new float[cols.Length];
        var fonts = new ThemeFont[cols.Length];
        var css = new float[cols.Length];
        var heads = new string[cols.Length];
        for (var c = 0; c < cols.Length; c++)
        {
            fonts[c] = cols[c].Strong ? F.NumStrong : F.Num;
            css[c] = cols[c].Width;
            heads[c] = cols[c].Head;
        }
        DetailColumns.Widths(widths, css, S(1), heads, F.Head, fonts, cells.Append(party), Measure);
        // Kept once the faces are built (measured in a stand-in before).
        if (Txt.Ready(F.Num.Face, F.Num.Px) && Txt.Ready(F.NumStrong.Face, F.NumStrong.Px))
        {
            columnWidths = (key, widths);
            columnWidthsOf = table.Of;
        }
        _ = rows;
        return widths;
    }

    private ((MeterTab, MeterRank, MeterThemeId, float, int) Key, float[] Widths) columnWidths = (default, Array.Empty<float>());

    /// <summary>The table the columns were measured for (compared by reference: an encounter is a record).</summary>
    private MeterEncounter? columnWidthsOf;

    private (MeterEncounter? Of, MeterTab Tab, MeterRank Rank, Col[] Cols, List<MeterRow> Rows, string[][] Cells, string[] Party) table =
        (null, MeterTab.Damage, MeterRank.EncDps, Array.Empty<Col>(), new List<MeterRow>(), Array.Empty<string[]>(), Array.Empty<string>());

    /// <summary>
    /// The table's columns, its rows in order (the limit break last) and every cell in words, worked out
    /// once a snapshot (a few times a second while a fight is on), not each frame. The party's row leaves
    /// out what doesn't add up (crit, DH, the biggest hit, DPS: as the mock-up leaves them).
    /// </summary>
    private (Col[] Cols, List<MeterRow> Rows, string[][] Cells, string[] Party) TableOf(MeterEncounter e)
    {
        var rank = config.MeterRank;
        if (!ReferenceEquals(table.Of, e) || table.Tab != tab || table.Rank != rank)
        {
            var cols = Columns(e);
            // The events' counts in spot order (there's no one number to rank them by); the rest biggest first.
            var rows = tab == MeterTab.Events ? MeterView.InSpotOrder(e.Rows) : MeterView.Ranked(e.Rows, tab, rank);
            if (tab == MeterTab.Damage && MeterView.LimitBreak(e.Rows) is { } lb) rows.Add(lb);
            var cells = new string[rows.Count][];
            for (var i = 0; i < rows.Count; i++)
            {
                cells[i] = new string[cols.Length];
                for (var c = 0; c < cols.Length; c++) cells[i][c] = cols[c].Text(rows[i]);
            }
            var deaths = MeterView.Deaths(e).Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var party = new string[cols.Length];
            for (var c = 0; c < cols.Length; c++)
                party[c] = cols[c].Head switch
                {
                    "Deaths" => deaths,
                    "%" => "100.0",
                    "DPS" or "Crit" or "DH" or "Max hit" or "Max heal" or "Over" => string.Empty,
                    _ => cols[c].Text(e.Party),
                };
            table = (e, tab, rank, cols, rows, cells, party);
        }
        return (table.Cols, table.Rows, table.Cells, table.Party);
    }

    /// <summary>
    /// By ability (`.ab`), for the picked player: the head (their spot, "Rook · DRG", how many abilities
    /// and hits, their total), then a 30px row an ability, biggest first (a DoT's ticks tagged, est. when
    /// shared), a slim bar each to the biggest; the rest summed as "9 more".
    /// </summary>
    private void DrawAbilities(ImDrawListPtr dl, IMeterSource m, MeterEncounter e, Vector2 at, float w, float h)
    {
        Panel(dl, at, w, h);
        var row = e.Rows.FirstOrDefault(r => r.PartyId == player && !r.IsLimitBreak);
        if (row == null) return;
        var abilities = m.Abilities(e.Id, row.PartyId, tab);
        var x = at.X + S(8) + S(10);
        var right = at.X + w - S(8) - S(10);
        var y = at.Y + S(12);
        Badge(dl, new Vector2(x + S(13), y + S(19)), 26, row.Job, row.Role, false, row.Slot);
        var tx = x + S(26) + S(12);
        var total = tab switch
        {
            MeterTab.Healing => row.Healing,
            MeterTab.Taken => row.Taken,
            _ => row.Damage,
        };
        var view = AbilitiesOf(abilities, total);
        var totalW = PlateArt.Width(view.Total, F.NumStrong);
        var title = config.MeterJobsOnly ? row.Job : $"{row.Name} · {row.Job}";
        PlateArt.Line(dl, new Vector2(tx, y), PlateArt.Fit(title, F.Title, right - totalW - S(12) - tx), F.Title, I.Strong, 15 * 1.25f);
        PlateArt.Line(dl, new Vector2(tx, y + S(15 * 1.25f)), PlateArt.Fit(view.Sub, F.Body, right - tx), F.Body, I.Muted, 19.5f);
        PlateArt.LineRight(dl, right, y, view.Total, F.NumStrong, I.Strong, 15 * 1.25f);
        y += S(38) + S(10);

        // Heads: Ability · Damage · % · Hits · Crit · DH · Max (no DH but for damage).
        var heads = tab switch
        {
            MeterTab.Healing => HealingHeads,
            MeterTab.Taken => TakenHeads,
            _ => DamageHeads,
        };
        // Each column as wide as its widest words shown (its head, its cells, the "more" line's).
        var widths = AbilityWidths(heads, tab == MeterTab.Damage ? DamageWidths : OtherWidths, view.Cells);
        Span<float> edges = stackalloc float[heads.Length];
        var ex = right;
        for (var i = heads.Length - 1; i >= 0; i--)
        {
            edges[i] = ex;
            ex -= widths[i] + S(8);
        }
        HeadWords(dl, x, y, tab == MeterTab.Taken ? "What hit" : "Ability", F.HeadSmall, 26);
        for (var i = 0; i < heads.Length; i++) HeadWords(dl, edges[i], y, heads[i], F.HeadSmall, 26, true);
        y += S(26);
        Kit.Rule(dl, at.X + S(8), at.X + w - S(8), y - 1, I.Rule);
        var rowH = S(30);
        // As many as fit; the rest summed on the last line ("9 more") when they don't all fit.
        var fits = (int)Math.Max(1, (at.Y + h - S(8) - y) / rowH);
        var shown = abilities.Count <= fits ? abilities.Count : Math.Max(0, fits - 1);
        for (var n = 0; n < shown; n++)
        {
            var a = abilities[n];
            var nameRoom = ex - x;
            var nx = x;
            nx += PlateArt.Line(dl, new Vector2(nx, y), PlateArt.Fit(a.Name, F.BodyStrong, nameRoom - (a.IsTick ? S(40) : 0) - (a.Estimated ? S(40) : 0)), F.BodyStrong, I.Strong, 30);
            if (a.IsTick) nx += S(6) + SmallTag(dl, new Vector2(nx + S(6), y + (rowH - S(16)) / 2), "DoT");
            if (a.Estimated) SmallTag(dl, new Vector2(nx + S(6), y + (rowH - S(16)) / 2), "est.");
            var cells = view.Cells[n];
            for (var i = 0; i < cells.Length && i < edges.Length; i++)
                PlateArt.LineRight(dl, edges[i], y, cells[i], F.Small, i == 0 ? I.Strong : I.Text, 30);
            // The bar under it, to the biggest: Ready Check's slim white one; a theme's own, in the player's role colour, under the words.
            var ba = new Vector2(at.X + S(8) + S(10), y + rowH - S(2) - S(2));
            var bb = new Vector2(at.X + w - S(8) - S(10), y + rowH - S(2));
            if (Rc)
            {
                dl.AddRectFilled(ba, bb, Palette.U32(Palette.Rgba(255, 255, 255, 0.04f)));
                if (view.Biggest > 0) dl.AddRectFilled(ba, new Vector2(ba.X + (bb.X - ba.X) * a.Amount / view.Biggest, bb.Y), Palette.U32(Palette.Rgba(255, 255, 255, 0.22f)));
            }
            else
            {
                var bh = S(4);
                var bt = new Vector2(ba.X, y + rowH - S(1) - bh);
                dl.AddRectFilled(bt, new Vector2(bb.X, bt.Y + bh), Palette.U32(Rgb.A(I.Text, 0.05f)), MathF.Min(bh / 2, S(I.RowRadius)));
                if (view.Biggest > 0) plate.BarOn(dl, bt, bb.X - ba.X, bh, a.Amount / (float)view.Biggest, row.Role, false);
            }
            y += rowH;
        }
        if (abilities.Count > shown)
        {
            long amount = 0;
            double share = 0;
            var hits = 0;
            for (var n = shown; n < abilities.Count; n++)
            {
                amount += abilities[n].Amount;
                share += abilities[n].Share;
                hits += abilities[n].Hits;
            }
            PlateArt.Line(dl, new Vector2(x, y), $"{abilities.Count - shown} more", F.BodyStrong, I.Muted, 30);
            PlateArt.LineRight(dl, edges[0], y, Format.Thousands(amount), F.Small, I.Muted, 30);
            PlateArt.LineRight(dl, edges[1], y, Format.Share(share), F.Small, I.Muted, 30);
            PlateArt.LineRight(dl, edges[2], y, hits.ToString(System.Globalization.CultureInfo.InvariantCulture), F.Small, I.Muted, 30);
        }
        if (abilities.Count == 0) PlateArt.Line(dl, new Vector2(x, y), "Nothing yet.", F.Body, I.Muted, 30);
    }

    /// <summary>A small tag ("DoT", "est."): Ready Check's the kit's; a theme's its chip. Returns its width.</summary>
    private float SmallTag(ImDrawListPtr dl, Vector2 pos, string word)
    {
        if (Rc) return Kit.Tag(dl, pos, word, TagKind.Plain, 16);
        var w = PlateArt.Width(word, F.Small) + S(10);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, S(16)) - new Vector2(0.5f), Palette.U32(I.Chip), S(I.ChipRadius), ImDrawFlags.None, 1f);
        PlateArt.Line(dl, new Vector2(pos.X + S(5), pos.Y), word, F.Small, I.Muted, 16);
        return w;
    }

    /// <summary>The abilities' columns' widths (screen px): each at least its CSS width, its head and its widest cell, measured as drawn; again for new cells, a theme, a scale or a font build.</summary>
    private float[] AbilityWidths(string[] heads, float[] css, string[][] cells)
    {
        var key = (cells, T.Id, S(1), Txt.Fonts.Generation);
        if (ReferenceEquals(abilityWidths.Key.Cells, cells) && abilityWidths.Key == key && abilityWidths.Widths.Length == heads.Length) return abilityWidths.Widths;
        var widths = new float[heads.Length];
        var fonts = new ThemeFont[heads.Length];
        for (var i = 0; i < fonts.Length; i++) fonts[i] = F.Small;
        DetailColumns.Widths(widths, css, S(1), heads, F.HeadSmall, fonts, cells, Measure);
        if (Txt.Ready(F.Small.Face, F.Small.Px)) abilityWidths = (key, widths);
        return widths;
    }

    private ((string[][]? Cells, MeterThemeId, float, int) Key, float[] Widths) abilityWidths = (default, Array.Empty<float>());

    private static readonly string[] DamageHeads = { "Damage", "%", "Hits", "Crit", "DH", "Max" };
    private static readonly string[] HealingHeads = { "Healing", "%", "Hits", "Crit", "Max" };
    private static readonly string[] TakenHeads = { "Taken", "%", "Hits", "Crit", "Max" };
    private static readonly float[] DamageWidths = { 64, 36, 30, 36, 36, 56 };
    private static readonly float[] OtherWidths = { 64, 36, 30, 36, 56 };

    private (IReadOnlyList<AbilityRow>? Of, MeterTab Tab, long Amount, string[][] Cells, long Biggest, string Sub, string Total) abilityView =
        (null, MeterTab.Damage, 0, Array.Empty<string[]>(), 0, string.Empty, string.Empty);

    /// <summary>
    /// The picked player's abilities in words (each line's cells, how many and how big, the total):
    /// worked out again only when the source gives a new list (it keeps one, a few times a second
    /// while a fight is on) or the tab changes, not each frame.
    /// </summary>
    private (string[][] Cells, long Biggest, string Sub, string Total) AbilitiesOf(IReadOnlyList<AbilityRow> abilities, long total)
    {
        if (!ReferenceEquals(abilityView.Of, abilities) || abilityView.Tab != tab || abilityView.Amount != total)
        {
            var cells = new string[abilities.Count][];
            long biggest = 0;
            var hits = 0;
            for (var i = 0; i < abilities.Count; i++)
            {
                var a = abilities[i];
                biggest = Math.Max(biggest, a.Amount);
                hits += a.Hits;
                var count = a.Hits.ToString(System.Globalization.CultureInfo.InvariantCulture);
                cells[i] = tab == MeterTab.Damage
                    ? new[] { Format.Thousands(a.Amount), Format.Share(a.Share), count, Rate(a.CritRate), Rate(a.DirectHitRate), Format.Thousands(a.Max) }
                    : new[] { Format.Thousands(a.Amount), Format.Share(a.Share), count, Rate(a.CritRate), Format.Thousands(a.Max) };
            }
            var by = tab == MeterTab.Taken ? "By what hit them" : "By ability";
            var sub = $"{by} · {CountOf(abilities.Count, "ability", "abilities")}, {CountOf(hits, "hit", "hits")}";
            abilityView = (abilities, tab, total, cells, biggest, sub, Format.Thousands(total));
        }
        return (abilityView.Cells, abilityView.Biggest, abilityView.Sub, abilityView.Total);
    }

    private static string Rate(double? r) => r is { } v ? Format.Share(v) : "–";

    private static string CountOf(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{Format.Thousands(n)} {many}";

    // ---------------------------------------------------------------------------------------------
    // The events.

    /// <summary>
    /// The fight's events (`.ab`'s slab), oldest first, scrolling: a 34px row each, the time in mono, the
    /// kind's mark in its colour, the member's spot, what happened, its kind in faint mono capitals on
    /// the right; a death's row in its red. Pressed, a death opens its recap (Deaths) and a member's
    /// event picks them in the table. Only the rows in view are drawn.
    /// </summary>
    private void DrawEventLog(ImDrawListPtr dl, MeterEncounter e, IReadOnlyList<MeterEvent> events, Vector2 at, float w, float h)
    {
        var r = S(I.PanelRadius);
        dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(I.Panel), r);
        var x = at.X + S(18);
        var y = at.Y + S(12);
        PlateArt.Line(dl, new Vector2(x, y), "Events", F.Title, I.Strong, 15 * 1.25f);
        var sub = events.Count == 0 ? "None yet: deaths, raises, interrupts, potions, limit breaks and tank swaps show here" : MeterText.Counts(events, 4);
        PlateArt.Line(dl, new Vector2(x, y + S(15 * 1.25f)), PlateArt.Fit(sub, F.Body, w - S(36)), F.Body, I.Muted, 19.5f);
        var listTop = at.Y + S(12) + S(38) + S(10);
        Kit.Rule(dl, at.X + S(8), at.X + w - S(8), listTop - 1, I.Rule);
        var listH = at.Y + h - S(4) - listTop;
        if (listH > S(20) && events.Count > 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(at.X + 1, listTop));
            var open = ImGui.BeginChild("##detail-events", new Vector2(w - 2, listH), false, ImGuiWindowFlags.None);
            Frame.Open.Push(false);
            try
            {
                if (open) EventRows(e, events, w - 2, listH);
            }
            finally
            {
                if (Frame.Open.Count > 0) Frame.Open.Pop();
                ImGui.EndChild();
            }
        }
        dl.AddRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(I.PanelEdge), r, ImDrawFlags.None, 1f);
    }

    /// <summary>An event's mark's colour: Ready Check's (the meter's); a theme's bad, good, accent or muted.</summary>
    private Vector4 Tone(EventTone tone) => Rc ? MeterWindow.ToneColor(tone) : tone switch
    {
        EventTone.Bad => T.Bad,
        EventTone.Good => T.Good,
        EventTone.Ember => T.Accent,
        _ => I.Muted,
    };

    /// <summary>The event log's row ids, made once each.</summary>
    private readonly List<string> eventIds = new();

    /// <summary>The event log's times in words, made again only for a new list of events.</summary>
    private (IReadOnlyList<MeterEvent>? Of, int Count, string[] Times) eventTimes = (null, 0, Array.Empty<string>());

    private void EventRows(MeterEncounter e, IReadOnlyList<MeterEvent> events, float w, float viewH)
    {
        if (!ReferenceEquals(eventTimes.Of, events) || eventTimes.Count != events.Count)
        {
            var times = new string[events.Count];
            for (var i = 0; i < times.Length; i++) times[i] = Format.Clock(events[i].At);
            eventTimes = (events, events.Count, times);
        }
        while (eventIds.Count < events.Count) eventIds.Add("##dev" + eventIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var dl = ImGui.GetWindowDrawList();
        var rowH = S(34);
        var start = ImGui.GetCursorScreenPos() + new Vector2(S(6), S(2));
        var rw = ImGui.GetContentRegionAvail().X - S(12);
        var scroll = ImGui.GetScrollY();
        var first = Math.Max(0, (int)(scroll / rowH) - 1);
        var last = Math.Min(events.Count, first + (int)(viewH / rowH) + 3);
        for (var i = first; i < last; i++)
        {
            var ev = events[i];
            var min = start + new Vector2(0, rowH * i);
            var max = min + new Vector2(rw, rowH);
            var picked = ev.PartyId != 0 && ev.PartyId == player;
            var (clicked, hovered, _) = Kit.Hit(eventIds[i], min, max - min);
            if (Rc && (picked || hovered)) dl.AddRectFilled(min, max, Palette.U32(picked ? Palette.Rgba(255, 255, 255, 0.05f) : Palette.Hover), S(Palette.RadiusSm));
            else if (!Rc)
            {
                var pm = min + new Vector2(0, S(1));
                var px = max - new Vector2(0, S(1));
                RowGround(dl, pm, px, hovered && !picked);
                if (picked) Picked(dl, string.Empty, pm, px, true, false);
            }
            if (clicked)
            {
                if (ev.PartyId > 0) player = ev.PartyId;
                if (ev.Kind == MeterEventKind.Death)
                    for (var d = 0; d < e.Deaths.Count; d++)
                        if (e.Deaths[d].PartyId == ev.PartyId && Math.Abs(e.Deaths[d].At - ev.At) < 1)
                        {
                            death = d;
                            tab = MeterTab.Deaths;
                        }
            }
            var x = min.X + S(10);
            // The time as wide as it's drawn (at least 40), so a wide face's "12:34" doesn't reach the mark.
            var timeW = MathF.Max(S(40), PlateArt.Width(eventTimes.Times[i], F.Small));
            PlateArt.LineRight(dl, x + timeW, min.Y, eventTimes.Times[i], F.Small, I.Muted, 34);
            x += timeW + S(12);
            Icons.Draw(dl, MeterWindow.EventIcon(ev.Kind), new Vector2(x, min.Y + (rowH - S(15)) / 2), S(15), Tone(MeterText.Tone(ev.Kind)));
            x += S(15) + S(10);
            if (ev.Slot.Length > 0)
            {
                var who = e.Rows.FirstOrDefault(r => r.PartyId == ev.PartyId && !r.IsLimitBreak);
                Badge(dl, new Vector2(x + S(9), min.Y + rowH / 2), 18, who?.Job, who?.Role ?? Format.RoleOfSlot(ev.Slot), false, ev.Slot);
                x += S(18) + S(8);
            }
            var kindWord = MeterText.KindWord(ev.Kind);
            var kw = HeadWidth(kindWord, F.HeadSmall);
            HeadWords(dl, max.X - S(10), min.Y, kindWord, F.HeadSmall, 34, true);
            var room = max.X - S(10) - kw - S(12) - x;
            if (room > S(20))
                PlateArt.Line(dl, new Vector2(x, min.Y), PlateArt.Fit(ev.Text, F.Body, room), F.Body, ev.Kind == MeterEventKind.Death ? (Rc ? Palette.DangerName : T.Bad) : I.Text, 34);
            if (hovered && PlateArt.Width(ev.Text, F.Body) > room) Kit.Tooltip(ev.Text);
        }
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(rw, rowH * events.Count + S(4)));
    }

    // ---------------------------------------------------------------------------------------------
    // Deaths and the recap.

    /// <summary>Where a death is in the fight's own list (the index the small meter opens a recap by).</summary>
    private static int IndexIn(IReadOnlyList<DeathEntry> all, DeathEntry d)
    {
        for (var i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], d))
                return i;
        return -1;
    }

    private void DrawDeaths(ImDrawListPtr dl, MeterEncounter e, Vector2 at, float w, float h, Vector2 recapAt, float recapW)
    {
        Panel(dl, at, w, h);
        var all = e.Deaths.OrderBy(d => d.At).ToList();
        if (death == null || death >= e.Deaths.Count) death = all.Count > 0 ? IndexIn(e.Deaths, all.FirstOrDefault(d => !d.PartOfWipe) ?? all[0]) : null;
        var y = at.Y + S(6);
        var rowH = S(46);
        foreach (var d in all)
        {
            if (y + rowH > at.Y + h - S(6)) break;
            var index = IndexIn(e.Deaths, d);
            var min = new Vector2(at.X + S(6), y);
            var max = new Vector2(at.X + w - S(6), y + rowH);
            var picked = index == death;
            var (clicked, hovered, _) = Kit.Hit($"##dd{index}", min, max - min);
            if (clicked) death = index;
            if (Rc && picked) dl.AddRectFilled(min, max, Palette.U32(Palette.Rgba(255, 255, 255, 0.06f)), S(Palette.RadiusSm));
            else if (Rc && hovered) dl.AddRectFilled(min, max, Palette.U32(Palette.Hover), S(Palette.RadiusSm));
            var plateMin = Rc ? min : min + new Vector2(0, S(1));
            var plateMax = Rc ? max : max - new Vector2(0, S(1));
            if (!Rc) RowGround(dl, plateMin, plateMax, hovered && !picked);
            Picked(dl, $"##dd-ret{index}", plateMin, plateMax, picked, hovered);
            var x = min.X + S(12);
            var dr = e.Rows.FirstOrDefault(r => r.PartyId == d.PartyId && !r.IsLimitBreak);
            Badge(dl, new Vector2(x + S(10), y + rowH / 2), 20, dr?.Job, dr?.Role, false, d.Slot);
            x += S(20) + S(10);
            var name = config.MeterJobsOnly ? d.Slot : d.Name;
            var color = d.PartOfWipe ? I.Muted : I.Strong;
            var time = Format.Clock(d.At);
            var timeW = PlateArt.Width(time, F.SmallStrong);
            PlateArt.Line(dl, new Vector2(x, y + S(6)), PlateArt.Fit(name, F.Name, MathF.Min(w * 0.4f, max.X - S(12) - timeW - S(12) - x)), F.Name, color, 18);
            var sub = d.PartOfWipe ? $"The wipe · {d.KilledBy}" : MeterText.DeathLine(d);
            PlateArt.Line(dl, new Vector2(x, y + S(24)), PlateArt.Fit(sub, F.Note, max.X - S(12) - timeW - S(12) - x), F.Note, I.Muted, 16);
            PlateArt.LineRight(dl, max.X - S(12), y, time, F.SmallStrong, d.PartOfWipe ? I.Muted : I.Bad, 46);
            y += rowH;
        }
        if (all.Count == 0) PlateArt.Line(dl, new Vector2(at.X + S(18), y), "Nobody died.", F.Body, I.Muted, 30);

        // The recap.
        Panel(dl, recapAt, recapW, h);
        if (death is { } di && di < e.Deaths.Count) Recap(dl, e, e.Deaths[di], recapAt, recapW, h);
    }

    /// <summary>
    /// A death's recap (`.rc`): "Odette died at 2:51" as a small callout, the killing blow and how far
    /// over; then its last hits and heals, oldest first: the time to a tenth, what it was (and from whom,
    /// what made it worse), its amount (a hit red, a heal green), the HP before it as a bar against their
    /// max HP (<see cref="MeterView.RecapMax(DeathEntry)"/>); the killing blow on a red wash (<see cref="MeterView.KillingLine"/>).
    /// </summary>
    private void Recap(ImDrawListPtr dl, MeterEncounter e, DeathEntry d, Vector2 at, float w, float h)
    {
        var x = at.X + S(18);
        var inner = w - S(36);
        var y = at.Y + S(14);
        var dr = e.Rows.FirstOrDefault(r => r.PartyId == d.PartyId && !r.IsLimitBreak);
        Badge(dl, new Vector2(x + S(13), y + S(18)), 26, dr?.Job, dr?.Role, false, d.Slot);
        var tx = x + S(26) + S(12);
        var who = config.MeterJobsOnly ? d.Slot : d.Name;
        var headline = $"{who} died at {Format.Clock(d.At)}";
        float th;
        if (Rc) th = Kit.Callout(dl, new Vector2(tx, y), headline, 17, inner - S(38), 1, 1.05f);
        else
        {
            // A theme's title, a size up, in its death colour.
            var big = F.Title with { Px = F.Title.Px + 2 };
            PlateArt.Line(dl, new Vector2(tx, y), PlateArt.Fit(headline, big, inner - S(38)), big, T.Death, 22);
            th = S(22);
        }
        PlateArt.Line(dl, new Vector2(tx, y + th + S(4)), PlateArt.Fit($"{d.KilledBy} · {Format.Thousands(d.Amount)} · {Format.Thousands(d.Overkill)} over what was left", F.Body, inner - S(38)),
            F.Body, I.Muted, 19.5f);
        var headH = th + S(4) + S(19.5f);
        // Since 0.4.0: Weakness on as they died, and the raise (when, by whom, how long they were down).
        if (MeterText.AfterDeath(d) is { } after)
        {
            PlateArt.Line(dl, new Vector2(tx, y + headH), PlateArt.Fit(after, F.BodyStrong, inner - S(38)), F.BodyStrong, d.RaisedAt != null ? I.Good : I.Muted, 19.5f);
            headH += S(19.5f);
        }
        y += MathF.Max(S(38), headH) + S(10);

        // Heads.
        var left = at.X + S(6) + S(14);
        var right = at.X + w - S(6) - S(14);
        var hpW = S(120);
        var amountR = right - hpW - S(12);
        // The time's column as wide as "2:51.4" is drawn (at least 50), so a wide face's doesn't reach what.
        var whatX = left + MathF.Max(S(50), PlateArt.Width("88:88.8", F.Small)) + S(12);
        HeadWords(dl, left, y, "Time", F.HeadSmall, 26);
        HeadWords(dl, whatX, y, "What", F.HeadSmall, 26);
        HeadWords(dl, amountR, y, "Amount", F.HeadSmall, 26, true);
        HeadWords(dl, right, y, "HP before", F.HeadSmall, 26, true);
        y += S(26);
        var max = MeterView.RecapMax(d);
        var killing = MeterView.KillingLine(d);
        var rowH = S(40);
        for (var i = 0; i < d.Recap.Count; i++)
        {
            var l = d.Recap[i];
            if (y + rowH > at.Y + h - S(30)) break;
            var kill = i == killing;
            var min = new Vector2(at.X + S(6), y);
            var rmax = new Vector2(at.X + w - S(6), y + rowH);
            if (kill) dl.AddRectFilled(min, rmax, Palette.U32(I.BadWash), S(Rc ? Palette.RadiusSm : I.RowRadius));
            else if (i > 0) Kit.Rule(dl, min.X, rmax.X, y - 0.5f, I.Rule);
            PlateArt.Line(dl, new Vector2(left, y), Format.ClockTenths(l.At), F.Small, kill ? I.Bad : I.Text, 40);
            var amount = l.Amount != 0 ? Format.Signed(l.Amount) : string.Empty;
            var amountW = amount.Length > 0 ? PlateArt.Width(amount, F.SmallStrong) : 0;
            var whatRoom = amountR - MathF.Max(S(80), amountW + S(12)) - whatX;
            var whatTop = y + (rowH - S(13 * 1.2f) - S(2) - S(11 * 1.3f)) / 2;
            PlateArt.Line(dl, new Vector2(whatX, whatTop), PlateArt.Fit(l.What, F.BodyStrong, whatRoom), F.BodyStrong, I.Strong, 13 * 1.2f);
            if (l.Note.Length > 0)
                PlateArt.Line(dl, new Vector2(whatX, whatTop + S(13 * 1.2f) + S(2)), PlateArt.Fit(l.Note, F.Note, whatRoom), F.Note, I.Muted, 11 * 1.3f);
            if (amount.Length > 0)
                PlateArt.LineRight(dl, amountR, y, amount, F.SmallStrong, l.Amount < 0 ? (Rc ? Palette.DangerSoft : T.Bad) : I.Good, 40);
            // The HP before, and its bar: low in warn, out in danger.
            var bx = right - hpW;
            var vy = y + (rowH - S(11) - S(4) - S(4)) / 2;
            PlateArt.LineRight(dl, right, vy, Format.Thousands(l.HpBefore), F.Tiny, I.Muted, 11);
            var barY = vy + S(11) + S(4);
            dl.AddRectFilled(new Vector2(bx, barY), new Vector2(right, barY + S(4)), Palette.U32(Rgb.A(I.Text, 0.08f)), S(2));
            var f = max > 0 ? Math.Clamp(l.HpBefore / (float)max, 0, 1) : 0;
            var col = l.HpBefore <= 0 ? I.Bad : f < 0.3f ? I.Warn : Rgb.A(I.Text, 0.6f);
            if (f > 0) dl.AddRectFilled(new Vector2(bx, barY), new Vector2(bx + hpW * f, barY + S(4)), Palette.U32(col), S(2));
            y += rowH;
        }
        if (d.Recap.Count == 0) PlateArt.Line(dl, new Vector2(left, y), "Nothing was kept of what hit them.", F.Body, I.Muted, 30);
        Note(dl, new Vector2(at.X + S(18), at.Y + h - S(30)), w - S(36), "The last ten hits and heals are kept; these are the ones before the death.");
    }

    /// <summary>Which pull, in the opened-up window: the same picker as the small meter's, without pace.</summary>
    private void DrawPicker(IMeterSource m, MeterEncounter shown)
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(S(260), 0), new Vector2(S(300), S(460)));
        // A theme's menu on its panel in its hairline (its rows below are its own); Ready Check's the kit's.
        var open = Rc ? Kit.BeginMenu("##detail-picker") : Kit.BeginMenu("##detail-picker", Rgb.A(I.Panel, 1), I.Chip, MathF.Min(T.Win.Radius, 12));
        if (!open) return;
        try
        {
            var w = S(300) - S(11.2f);
            var dl = ImGui.GetWindowDrawList();
            var (here, others) = MeterView.Picker(m.Kept, shown, m.Best(shown.Content));
            var eyebrow = here.Count > 0 ? $"{(DateTime.Now.Hour >= 17 ? "Tonight" : "Today")} · {here[0].Fight.Duty}" : null;
            if (!Rc)
            {
                ThemedPicker(dl, w, here.Concat(others), eyebrow, shown);
                return;
            }
            if (eyebrow != null) Kit.MenuEyebrow(eyebrow, w);
            foreach (var r in here.Concat(others))
            {
                var f = r.Fight;
                var title = r.Best ? $"Pull {f.Pull} · the best" : $"Pull {f.Pull}";
                var (clicked, _, min, max) = Kit.MenuRow("##dpick" + f.Id, w, title, f.Content == shown.Content ? null : f.Duty, true, 16, S(60));
                var (slot, right) = Kit.MenuRowParts(min, max, true, 16);
                if (r.Shown) Kit.Tick(dl, slot + new Vector2(S(8)), 16);
                else if (r.Best) Icons.Draw(dl, Icon.Star, slot + new Vector2((S(16) - S(13)) / 2), S(13), Palette.White);
                Txt.LineRight(dl, right, min.Y, Format.Clock(f.Duration), Face.Mono, 12, Palette.Faint, (max.Y - min.Y) / S(1));
                if (clicked)
                {
                    fight = r.Live ? null : f.Id;
                    death = null;
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        finally
        {
            Kit.EndMenu();
        }
    }

    /// <summary>The pull menu's rows in the theme: its eyebrow in its heads' capitals, each pull in its words (the one shown ticked in its accent, the best starred), its length in its small numbers.</summary>
    private void ThemedPicker(ImDrawListPtr dl, float w, IEnumerable<PickerRow> rows, string? eyebrow, MeterEncounter shown)
    {
        var at = ImGui.GetCursorScreenPos();
        var y = at.Y;
        if (eyebrow != null)
        {
            PlateArt.Line(dl, new Vector2(at.X + S(8.8f), y), PlateArt.Fit(eyebrow, F.HeadSmall, w - S(17.6f)), F.HeadSmall, I.Faint, 26);
            y += S(26);
        }
        var rh = S(34);
        foreach (var r in rows)
        {
            var f = r.Fight;
            var min = new Vector2(at.X, y);
            var max = new Vector2(at.X + w, y + rh);
            var (clicked, hovered, _) = Kit.Hit("##dpick" + f.Id, min, max - min);
            if (hovered) dl.AddRectFilled(min, max, Palette.U32(I.Hover), S(I.RowRadius));
            var x = min.X + S(8.8f);
            if (r.Shown) Icons.Draw(dl, Icon.Check, new Vector2(x, y + (rh - S(15)) / 2), S(15), I.Accent);
            else if (r.Best) Icons.Draw(dl, Icon.Star, new Vector2(x + S(1), y + (rh - S(13)) / 2), S(13), I.Accent);
            x += S(16) + S(8);
            var length = Format.Clock(f.Duration);
            var lw = PlateArt.LineRight(dl, max.X - S(8.8f), y, length, F.Small, I.Faint, 34);
            var title = r.Best ? $"Pull {f.Pull} · the best" : $"Pull {f.Pull}";
            var room = max.X - S(8.8f) - lw - S(12) - x;
            var tw = PlateArt.Line(dl, new Vector2(x, y), PlateArt.Fit(title, F.BodyStrong, room), F.BodyStrong, I.Strong, 34);
            if (f.Content != shown.Content && room - tw > S(30))
                PlateArt.Line(dl, new Vector2(x + tw + S(8), y), PlateArt.Fit(f.Duty, F.Note, room - tw - S(8)), F.Note, I.Muted, 34);
            if (clicked)
            {
                fight = r.Live ? null : f.Id;
                death = null;
                ImGui.CloseCurrentPopup();
            }
            y += rh;
        }
        ImGui.SetCursorScreenPos(at);
        ImGui.Dummy(new Vector2(w, MathF.Max(1, y - at.Y)));
    }
}
