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
/// The meter over the fight (the mock-ups' "The meter"): small and calm, 330 wide. The fight's clock
/// in mono with the ember dot that beats while a fight is on, which pull it shows (the picker), the
/// lock; four tabs with the ember cast bar under the one you're on; pace on the Damage tab; a 25px
/// row a player (the spot in its role's colour, the name, the job in mono, the number and its share,
/// a slim bar to the top player's: yours ember), the limit break on its own row, the party at the
/// foot. Locked, it has no title bar and can't be moved or sized; click-through, the mouse goes to
/// the game. Out of combat it folds to one line. Its numbers come from <see cref="IMeterSource"/> only.
/// </summary>
public sealed class MeterWindow : Window
{
    private const string Picker = "##rp-meter-picker";

    private readonly Configuration config;
    private readonly Fonts fonts;
    private DateTime drawFailedAt;

    /// <summary>The fight picked to show (null: the current one, live or the last).</summary>
    private int? picked;

    /// <summary>Opened again by hand while folded (until the next fight ends).</summary>
    private bool unfolded;

    private bool wasInEncounter;

    public MeterWindow(Configuration config, Fonts fonts)
        : base("Meter###RaidPlannerMeter")
    {
        this.config = config;
        this.fonts = fonts;
        Size = new Vector2(330, 382);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(260, 60), MaximumSize = new Vector2(720, 1400) };
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        IsOpen = config.ShowMeter;
    }

    /// <summary>Where the numbers come from (the plugin's meter, set by the plugin); null before there is one.</summary>
    public Func<IMeterSource?>? Source { get; set; }

    /// <summary>A made-up fight to look at the windows with (`/raidplan meter preview`).</summary>
    public IMeterSource? Preview { get; set; }

    /// <summary>In a duty (the meter shows only there unless Everywhere).</summary>
    public Func<bool>? InDuty { get; set; }

    /// <summary>Opens the opened-up window on a tab, a fight and a player (or a death).</summary>
    public Action<MeterTab, int?, int?, int?>? OpenDetail { get; set; }

    /// <summary>Opens Settings on the Meter tab.</summary>
    public Action? OpenSettings { get; set; }

    private IMeterSource? Meter => Preview ?? Source?.Invoke();

    /// <summary>Shown when wanted, in a duty (or everywhere), and not hidden out of combat.</summary>
    public override bool DrawConditions()
    {
        if (Preview != null) return true;
        if (!config.ShowMeter) return false;
        if (!config.MeterEverywhere && InDuty?.Invoke() != true) return false;
        var m = Source?.Invoke();
        if (config.MeterHideOutOfCombat && m is { InEncounter: false, Waiting: false }) return false;
        return true;
    }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing;
        if (config.MeterLocked)
        {
            // Locked: no title bar, no moving or sizing; click-through, the mouse goes to the game.
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus;
            if (config.MeterClickThrough) Flags |= ImGuiWindowFlags.NoInputs;
        }
        // The background is see-through as set; the rows stay solid.
        Theme.PushFrame(Palette.Rgba(13, 13, 16, Math.Clamp(config.MeterBackground, 0.2f, 1f)), Palette.RadiusMd);
    }

    public override void PostDraw() => Theme.Pop();

    public override void OnClose()
    {
        // The window's × is Settings' "Show the meter".
        if (config.ShowMeter && Preview == null)
        {
            config.ShowMeter = false;
            config.Save();
        }
        Preview = null;
    }

    public override void Draw()
    {
        Kit.ResetFrame();
        Frame.ResetFrame();
        Theme.PushInside();
        try
        {
            using (fonts.Push(Face.Sans, 13))
            {
                try
                {
                    Frame.Window(!config.MeterLocked);
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
                Services.Log.Error(ex, "Raid Planner's meter couldn't draw");
            }
        }
        finally
        {
            Theme.Pop();
        }
    }

    private void DrawInside()
    {
        var m = Meter;
        var win = ImGui.GetWindowPos();
        var top = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth();
        var dl = Kit.Dl;
        Kit.PushBox(win.X, w);
        try
        {
            var live = m?.InEncounter == true;
            if (wasInEncounter && !live) unfolded = false;
            if (live) unfolded = false;
            wasInEncounter = live;
            var shown = picked is { } id ? m?.Get(id) ?? m?.Current : m?.Current;
            if (picked != null && shown?.Id != picked) picked = null;

            // Before any fight: the Boss asleep and one line.
            if (m == null || (shown == null && !m.Waiting))
            {
                var y0 = Header(dl, top, w, "0:00", false, null, null, null, null);
                y0 = TabsRow(dl, top.X, y0, w);
                var c = new Vector2(top.X + w / 2, y0 + S(16) + S(20));
                Boss.Empty(dl, c, 40, BossMood.Asleep);
                var ty = c.Y + S(20) + S(7.2f);
                ty += Kit.Callout(dl, new Vector2(top.X, ty), "No fight yet", 13, w, 1, 1.1f, center: true);
                var line = m == null && Source?.Invoke() == null ? "The meter starts with the plugin's reader." : "It starts on the first hit.";
                Txt.Flow(new Vector2(top.X + S(14), ty + S(7.2f)), w - S(28), new[] { Run.Note(line) }, 19.5f, out _, center: true);
                return;
            }

            // Between pulls it folds to one line: the last pull's length and the party's number; a press opens it.
            if (!live && !m.Waiting && !unfolded && picked == null && shown != null && Preview == null)
            {
                Folded(dl, top, w, shown);
                return;
            }

            if (m.Waiting && picked == null)
            {
                DrawWaiting(dl, top, w, shown);
                return;
            }

            var e = shown!;
            var ended = !e.Live;
            var tag = MeterView.OutcomeWord(e.Ended);
            var y = Header(dl, top, w, Format.Clock(e.Duration), e.Live, tag, e.Ended == Core.Outcome.Clear ? TagKind.Clear : TagKind.Wipe, $"Pull {e.Pull}", m);
            y = TabsRow(dl, top.X, y, w);
            var tab = config.MeterTab;
            if (tab == MeterTab.Deaths)
            {
                DrawDeaths(dl, top.X, y, w, e, live && !ended);
                return;
            }
            if (tab == MeterTab.Damage && config.MeterPace != PaceAgainst.Off && m.Pace(e.Id, config.MeterPace) is { } pace)
                y = PaceRow(dl, top.X, y, w, pace);
            y = Columns(dl, top.X, y, w, tab);
            var footH = S(28);
            var bottom = win.Y + ImGui.GetWindowHeight() - footH;
            y = Rows(dl, top.X, y, w, e, tab, bottom);
            Footer(dl, top.X, bottom, w, e, tab, ended);
        }
        finally
        {
            Kit.PopBox();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The head: the clock, which pull, the lock.

    /// <summary>The head (`.mt-h`, 36 tall): the clock (mono 600 15px, its dot), a tag, the picker, the lock. Returns the y under it.</summary>
    private float Header(ImDrawListPtr dl, Vector2 top, float w, string clock, bool beating, string? tag, TagKind? tagKind, string? pick, IMeterSource? m,
        string? waitingLine = null)
    {
        var h = S(36);
        var x = top.X + S(12);
        var cy = top.Y + h / 2;
        Kit.BeatDot(dl, new Vector2(x + S(3), cy), beating ? Palette.Ember : Palette.Faint, beating);
        x += S(6 + 7);
        x += Txt.Line(dl, new Vector2(x, top.Y), clock, Face.MonoSemibold, 15, Palette.White, 36) + S(8);
        if (Preview != null) x += Kit.Tag(dl, new Vector2(x, cy - S(9)), "Preview", TagKind.Plain, 18) + S(8);
        if (tag != null && tagKind is { } k) x += Kit.Tag(dl, new Vector2(x, cy - S(9)), tag, k, 18) + S(8);
        if (waitingLine != null) Txt.Line(dl, new Vector2(x, top.Y), waitingLine, Face.Sans, 13, Palette.Muted, 36);
        var right = top.X + w - S(6);
        // The lock (`.mt-ib`): ember-washed when locked, a pointer when click-through, quiet when open.
        var lx = right - S(24);
        var locked = config.MeterLocked;
        var icon = !locked ? Icon.LockOpen : config.MeterClickThrough ? Icon.MousePointer : Icon.Lock;
        if (SmallButton("##meter-lock", new Vector2(lx, cy - S(12)), icon, locked, locked ? "Locked where it is. Press to move and size it again." : "Lock it where it is"))
        {
            config.MeterLocked = !locked;
            config.Save();
        }
        if (pick != null && m != null)
        {
            // The picker (`.mt-pick`): 24 tall, 6px corners, a hairline; "Pull 12 ⌄".
            var pw = S(8) + Txt.Width(pick, Face.SansMedium, 12) + S(4) + S(13) + S(4);
            var pp = new Vector2(lx - S(8) - pw, cy - S(12));
            var (clicked, hovered, _) = Kit.Hit("##meter-pick", pp, new Vector2(pw, S(24)));
            if (hovered) dl.AddRectFilled(pp, pp + new Vector2(pw, S(24)), Palette.U32(Palette.Hover), S(6));
            dl.AddRect(pp + new Vector2(0.5f), pp + new Vector2(pw, S(24)) - new Vector2(0.5f), Palette.U32(Palette.Line2), S(6), ImDrawFlags.None, 1f);
            var tx = pp.X + S(8);
            tx += Txt.Line(dl, new Vector2(tx, pp.Y), pick, Face.SansMedium, 12, Palette.Text, 24) + S(4);
            Icons.Draw(dl, Icon.ChevronDown, new Vector2(tx, pp.Y + (S(24) - S(13)) / 2), S(13), Palette.Faint);
            if (clicked) ImGui.OpenPopup(Picker);
            DrawPicker(m);
        }
        return top.Y + h;
    }

    /// <summary>A 24px square icon button (`.mt-ib`): muted, ember-washed when on.</summary>
    private static bool SmallButton(string id, Vector2 pos, Icon icon, bool on, string tip)
    {
        var s = S(24);
        var (clicked, hovered, _) = Kit.Hit(id, pos, new Vector2(s));
        var dl = Kit.Dl;
        if (on) dl.AddRectFilled(pos, pos + new Vector2(s), Palette.U32(Palette.EmberWash), S(6));
        else if (hovered) dl.AddRectFilled(pos, pos + new Vector2(s), Palette.U32(Palette.Hover), S(6));
        Icons.Draw(dl, icon, pos + new Vector2((s - S(14)) / 2), S(14), on ? Palette.Ember : hovered ? Palette.White : Palette.Muted);
        if (hovered) Kit.Tooltip(tip);
        Kit.FocusRing(pos, pos + new Vector2(s), S(6));
        return clicked;
    }

    /// <summary>The four tabs (`.mt-tabs`, 27 tall, 12px at 550), a hairline under them. Returns the y under them.</summary>
    private float TabsRow(ImDrawListPtr dl, float left, float y, float w)
    {
        var tabs = new[] { new TabSpec("Damage"), new TabSpec("Healing"), new TabSpec("Taken"), new TabSpec("Deaths") };
        var t = (MeterTab)Kit.Tabs("##meter-tab", new Vector2(left + S(6), y), tabs, (int)config.MeterTab, TabSize.Meter, true, w, left);
        if (t != config.MeterTab)
        {
            config.MeterTab = t;
            config.Save();
        }
        return y + S(27);
    }

    /// <summary>Pace (`.mt-pace`, 26 tall): the phase and how far into it, the bar (this pull ember, the other's a white tick), "on pull 4 +3.4%".</summary>
    private static float PaceRow(ImDrawListPtr dl, float left, float y, float w, PaceLine pace)
    {
        var h = S(26);
        var x = left + S(12);
        var label = $"Pace · {pace.Phase} +{Format.Clock(pace.PhaseSeconds)}";
        var lw = Kit.Label(dl, new Vector2(x, y + (h - Txt.Natural(Face.Mono, 9)) / 2), label, 9, 0.09f, Palette.Faint);
        var ahead = Format.SignedPercent(pace.Ahead);
        var aw = Txt.Width(ahead, Face.MonoSemibold, 12);
        var on = $"on pull {pace.AgainstPull} ";
        var ow = Txt.Width(on, Face.Sans, 11);
        var rx = left + w - S(12);
        Txt.Line(dl, new Vector2(rx - aw, y), ahead, Face.MonoSemibold, 12, Palette.White, 26);
        Txt.Line(dl, new Vector2(rx - aw - ow, y), on, Face.Sans, 11, Palette.Muted, 26);
        var bx = x + lw + S(8);
        var bw = rx - aw - ow - S(8) - bx;
        if (bw > S(20))
        {
            var (fill, mark) = MeterView.PaceBar(pace);
            var by = y + h / 2 - S(1.5f);
            dl.AddRectFilled(new Vector2(bx, by), new Vector2(bx + bw, by + S(3)), Palette.U32(Palette.Rgba(255, 255, 255, 0.08f)), S(2));
            if (fill > 0) Kit.CastGradient(dl, new Vector2(bx, by), new Vector2(bx + bw * fill, by + S(3)));
            var mx = bx + bw * mark;
            dl.AddRectFilled(new Vector2(mx - S(0.75f), by - S(3)), new Vector2(mx + S(0.75f), by + S(6)), Palette.U32(Palette.White));
        }
        Kit.Rule(dl, left, left + w, y + h - 1);
        return y + h;
    }

    /// <summary>The columns' heads (`.mt-cols`, 20 tall, mono 9px capitals): the number and its second, right-aligned.</summary>
    private float Columns(ImDrawListPtr dl, float left, float y, float w, MeterTab tab)
    {
        var (a, b) = MeterView.Heads(tab, config.MeterRank);
        var (_, _, _, valueR, secondR) = Grid(left, w);
        var ly = y + S(20) - S(2) - Txt.Natural(Face.Mono, 9);
        var aw = Txt.SpacedWidth(Txt.Caps(a), Face.Mono, 9, 0.08f);
        var bw = Txt.SpacedWidth(Txt.Caps(b), Face.Mono, 9, 0.08f);
        Kit.Label(dl, new Vector2(valueR - aw, ly), a, 9, 0.08f, Palette.Faint);
        Kit.Label(dl, new Vector2(secondR - bw, ly), b, 9, 0.08f, Palette.Faint);
        return y + S(20);
    }

    /// <summary>The row's grid (`18px 1fr 30px 66px 40px`, 8 apart, 12 in): the spot's left, the name's left, the job's left, the number's right, the second's right.</summary>
    private static (float Spot, float Name, float Job, float ValueRight, float SecondRight) Grid(float left, float w)
    {
        var x0 = left + S(12);
        var secondR = left + w - S(12);
        var valueR = secondR - S(40) - S(8);
        var job = valueR - S(66) - S(8) - S(30);
        return (x0, x0 + S(18) + S(8), job, valueR, secondR);
    }

    /// <summary>The rows (25 tall), biggest first, then the limit break's under a hairline. Rows don't grow: past the room they're cut.</summary>
    private float Rows(ImDrawListPtr dl, float left, float y, float w, MeterEncounter e, MeterTab tab, float bottom)
    {
        var rank = config.MeterRank;
        var rows = MeterView.Ranked(e.Rows, tab, rank);
        var top = rows.Count > 0 ? MeterView.Value(rows[0], tab, rank) : 0;
        var (spotX, nameX, jobX, valueR, secondR) = Grid(left, w);
        var rowH = S(25);
        dl.PushClipRect(new Vector2(left, y), new Vector2(left + w, bottom), true);
        try
        {
            foreach (var r in rows)
            {
                if (y + rowH > bottom + 1) break;
                var min = new Vector2(left, y);
                var max = new Vector2(left + w, y + rowH);
                var (clicked, hovered, _) = Kit.Hit($"##mr{r.PartyId}", min, max - min);
                if (hovered) dl.AddRectFilled(min, max, Palette.U32(Palette.Hover));
                if (clicked) OpenDetail?.Invoke(tab, e.Id, r.PartyId, null);
                Kit.Spot(dl, new Vector2(spotX + S(9), y + rowH / 2), 18, r.Slot, Palette.RoleColor(r.Role), 1.5f);
                var nameText = config.MeterJobsOnly ? r.Job : r.Name;
                var dim = r.Dead;
                var nameRoom = (config.MeterJobsOnly ? valueR - S(66) : jobX - S(8)) - nameX - (dim ? S(17) : 0);
                var nw = Txt.Line(dl, new Vector2(nameX, y), Txt.Fit(nameText, Face.SansMedium, 13, nameRoom), Face.SansMedium, 13, dim ? Palette.Muted : Palette.White, 25);
                if (dim) Icons.Draw(dl, Icon.Skull, new Vector2(nameX + nw + S(5), y + (rowH - S(12)) / 2), S(12), Palette.Danger);
                if (!config.MeterJobsOnly) Kit.Label(dl, new Vector2(jobX, y + (rowH - Txt.Natural(Face.Mono, 10)) / 2), r.Job, 10, 0.06f, Palette.Faint);
                Txt.LineRight(dl, valueR, y, MeterView.ValueText(r, tab, rank, true), Face.MonoSemibold, 13, dim ? Palette.Muted : Palette.White, 25);
                Txt.LineRight(dl, secondR, y, MeterView.Second(r, tab), Face.Mono, 11, Palette.Muted, 25);
                Bar(dl, left, w, y + rowH, MeterView.Bar(r, top, tab, rank), r.IsYou, dim);
                y += rowH;
            }
            if (tab == MeterTab.Damage && MeterView.LimitBreak(e.Rows) is { } lb && y + S(27) <= bottom + 1)
            {
                Kit.Rule(dl, left, left + w, y);
                var h = S(27);
                Kit.LimitMark(dl, new Vector2(spotX, y + (h - S(18)) / 2));
                Txt.Line(dl, new Vector2(nameX, y), "Limit break", Face.SansMedium, 13, Palette.White, 27);
                Txt.LineRight(dl, valueR, y, Format.Thousands((long)Math.Round(lb.EncDps)), Face.MonoSemibold, 13, Palette.White, 27);
                Txt.LineRight(dl, secondR, y, Format.Share(lb.DamageShare), Face.Mono, 11, Palette.Muted, 27);
                Bar(dl, left, w, y + h, top > 0 ? (float)Math.Clamp(lb.EncDps / top, 0, 1) : 0, false, false);
                y += h;
            }
        }
        finally
        {
            dl.PopClipRect();
        }
        return y;
    }

    /// <summary>A row's bar (`.mb`): a slim cast bar to the top player's, 38 in from the left; grey, yours ember, a death's dimmer.</summary>
    private static void Bar(ImDrawListPtr dl, float left, float w, float bottom, float fraction, bool you, bool dead)
    {
        var a = new Vector2(left + S(38), bottom - S(1) - S(2));
        var b = new Vector2(left + w - S(12), bottom - S(1));
        dl.AddRectFilled(a, b, Palette.U32(Palette.Rgba(255, 255, 255, 0.05f)), S(1));
        var fw = (b.X - a.X) * fraction;
        if (fw < 1) return;
        if (you) Kit.CastGradient(dl, a, new Vector2(a.X + fw, b.Y));
        else dl.AddRectFilled(a, new Vector2(a.X + fw, b.Y), Palette.U32(Palette.Rgba(255, 255, 255, dead ? 0.16f : 0.3f)), S(1));
    }

    /// <summary>The foot (`.mt-f`, 28 tall, mono 11px): the party's number, and what's worth saying on the right.</summary>
    private void Footer(ImDrawListPtr dl, float left, float y, float w, MeterEncounter e, MeterTab tab, bool ended)
    {
        Kit.Rule(dl, left, left + w, y);
        var (value, unit) = MeterView.Party(e, tab, config.MeterRank);
        var x = left + S(12);
        x += Txt.Line(dl, new Vector2(x, y), "Party · ", Face.Mono, 11, Palette.Muted, 28);
        x += Txt.Line(dl, new Vector2(x, y), value, Face.MonoSemibold, 11, Palette.White, 28);
        if (unit != null) Txt.Line(dl, new Vector2(x, y), " " + unit, Face.Mono, 11, Palette.Muted, 28);
        string? right = config.MeterLocked && config.MeterClickThrough ? "click-through"
            : ended ? $"kept as pull {e.Pull}"
            : tab switch
            {
                MeterTab.Healing => "shields not counted",
                MeterTab.Taken => "incl. bleeds",
                _ => MeterView.Where(e) ?? (config.MeterRank == MeterRank.Dps ? "DPS" : "ENCDPS"),
            };
        Txt.LineRight(dl, left + w - S(12), y, right, Face.Mono, 11, Palette.Muted, 28);
    }

    // ---------------------------------------------------------------------------------------------
    // Deaths, waiting, folded.

    /// <summary>The Deaths tab: each death in red time, its spot and name, the killing blow and how big; pressed, its recap opens.</summary>
    private void DrawDeaths(ImDrawListPtr dl, float left, float y, float w, MeterEncounter e, bool live)
    {
        var deaths = MeterView.Deaths(e);
        foreach (var d in deaths)
        {
            var nameH = Txt.Natural(Face.SansMedium, 13);
            var subH = Txt.Natural(Face.Sans, 11);
            var h = S(6) * 2 + nameH + S(2) + subH;
            var min = new Vector2(left, y);
            var max = new Vector2(left + w, y + h);
            var (clicked, hovered, _) = Kit.Hit($"##md{d.PartyId}{d.At}", min, max - min);
            if (hovered) dl.AddRectFilled(min, max, Palette.U32(Palette.Hover));
            Kit.Reticle(dl, $"##md-ret{d.PartyId}{d.At}", min + new Vector2(S(6), S(1)), max - new Vector2(S(6), S(1)), hovered);
            if (clicked) OpenDetail?.Invoke(MeterTab.Deaths, e.Id, d.PartyId, e.Deaths.ToList().IndexOf(d));
            var x = left + S(12);
            var role = e.Rows.FirstOrDefault(r => r.PartyId == d.PartyId)?.Role;
            Kit.Spot(dl, new Vector2(x + S(9), y + S(6) + nameH / 2), 18, d.Slot, Palette.RoleColor(role), 1.5f);
            var nx = x + S(18) + S(8);
            var job = e.Rows.FirstOrDefault(r => r.PartyId == d.PartyId)?.Job ?? string.Empty;
            var name = config.MeterJobsOnly ? job : d.Name;
            var nw = Txt.Line(dl, new Vector2(nx, y + S(6)), Txt.Fit(name, Face.SansMedium, 13, w - S(100)), Face.SansMedium, 13, Palette.White);
            if (!config.MeterJobsOnly) Kit.Label(dl, new Vector2(nx + nw + S(5), y + S(6) + (nameH - Txt.Natural(Face.Mono, 10)) / 2), job, 10, 0.06f, Palette.Faint);
            Txt.LineRight(dl, left + w - S(12), y + S(6), Format.Clock(d.At), Face.MonoSemibold, 12, Palette.Danger, nameH / S(1));
            var sub = $"{d.KilledBy} · {Format.Thousands(d.Amount)}";
            Txt.Draw(dl, new Vector2(nx, y + S(6) + nameH + S(2)), Txt.Fit(sub, Face.Sans, 11, left + w - S(12) - nx), Face.Sans, 11, Palette.Muted);
            y += h;
        }
        var wipe = e.Deaths.Count(d => d.PartOfWipe);
        if (live)
        {
            var alive = e.Rows.Count(r => !r.IsLimitBreak && !r.Dead);
            var c = new Vector2(left + w / 2, y + S(14));
            var t1 = $"{alive} alive";
            Txt.Draw(dl, new Vector2(c.X - Txt.Width(t1, Face.Sans, 13) / 2, c.Y), t1, Face.Sans, 13, Palette.Muted);
            var t2 = deaths.Count > 0 ? "A death's recap opens from its row" : "No deaths yet";
            Txt.Draw(dl, new Vector2(c.X - Txt.Width(t2, Face.Sans, 12) / 2, c.Y + S(19.5f) + S(7.2f)), t2, Face.Sans, 12, Palette.Faint);
            return;
        }
        if (deaths.Count == 0)
        {
            var t = wipe > 0 ? "No deaths but the wipe" : "Nobody died";
            Txt.Draw(dl, new Vector2(left + (w - Txt.Width(t, Face.Sans, 13)) / 2, y + S(14)), t, Face.Sans, 13, Palette.Muted);
        }
        var fy = ImGui.GetWindowPos().Y + ImGui.GetWindowHeight() - S(28);
        Kit.Rule(dl, left, left + w, fy);
        var left2 = deaths.Count == 1 ? "1 death" : $"{deaths.Count} deaths";
        if (wipe > 0) left2 += " · the wipe";
        Txt.Line(dl, new Vector2(left + S(12), fy), left2, Face.Mono, 11, Palette.Muted, 28);
        Txt.LineRight(dl, left + w - S(12), fy, $"pull {e.Pull}", Face.Mono, 11, Palette.Muted, 28);
    }

    /// <summary>Waiting for the first hit: the clock at 0:00 with a hollow dot, the party listed with nothing yet.</summary>
    private void DrawWaiting(ImDrawListPtr dl, Vector2 top, float w, MeterEncounter? last)
    {
        var y = Header(dl, top, w, "0:00", false, null, null, null, null, "Waiting for the first hit");
        y = TabsRow(dl, top.X, y, w);
        y = Columns(dl, top.X, y, w, MeterTab.Damage);
        var (spotX, nameX, jobX, valueR, _) = Grid(top.X, w);
        var rows = last?.Rows.Where(r => !r.IsLimitBreak).OrderBy(r => SpotOrder(r.Slot)).ToList() ?? new List<MeterRow>();
        var rowH = S(25);
        foreach (var r in rows)
        {
            Kit.Spot(dl, new Vector2(spotX + S(9), y + rowH / 2), 18, r.Slot, Palette.RoleColor(r.Role), 1.5f);
            Txt.Line(dl, new Vector2(nameX, y), Txt.Fit(config.MeterJobsOnly ? r.Job : r.Name, Face.SansMedium, 13, jobX - S(8) - nameX), Face.SansMedium, 13, Palette.White, 25);
            if (!config.MeterJobsOnly) Kit.Label(dl, new Vector2(jobX, y + (rowH - Txt.Natural(Face.Mono, 10)) / 2), r.Job, 10, 0.06f, Palette.Faint);
            Txt.LineRight(dl, valueR, y, "–", Face.MonoSemibold, 13, Palette.White, 25);
            Bar(dl, top.X, w, y + rowH, 0, false, false);
            y += rowH;
        }
        var fy = ImGui.GetWindowPos().Y + ImGui.GetWindowHeight() - S(28);
        Kit.Rule(dl, top.X, top.X + w, fy);
        if (last != null)
        {
            Txt.Line(dl, new Vector2(top.X + S(12), fy), Txt.Fit(last.Duty, Face.Mono, 11, w * 0.6f), Face.Mono, 11, Palette.Muted, 28);
            Txt.LineRight(dl, top.X + w - S(12), fy, $"pull {last.Pull + 1}", Face.Mono, 11, Palette.Muted, 28);
        }
    }

    private static int SpotOrder(string slot) => Array.IndexOf(new[] { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" }, slot) is var i and >= 0 ? i : 99;

    /// <summary>Folded between pulls: one line, the last pull's length under its tag, "Pull 12 · party 163,188", and a press opens it.</summary>
    private void Folded(ImDrawListPtr dl, Vector2 top, float w, MeterEncounter e)
    {
        var h = S(36);
        var x = top.X + S(12);
        var cy = top.Y + h / 2;
        Kit.BeatDot(dl, new Vector2(x + S(3), cy), Palette.Faint, false);
        x += S(13);
        x += Txt.Line(dl, new Vector2(x, top.Y), Format.Clock(e.Duration), Face.MonoSemibold, 15, Palette.White, 36) + S(8);
        if (MeterView.OutcomeWord(e.Ended) is { } t) x += Kit.Tag(dl, new Vector2(x, cy - S(9)), t, e.Ended == Core.Outcome.Clear ? TagKind.Clear : TagKind.Wipe, 18) + S(8);
        var right = top.X + w - S(6);
        if (SmallButton("##meter-open", new Vector2(right - S(24), cy - S(12)), Icon.ChevronDown, false, "Open it")) unfolded = true;
        var (value, _) = MeterView.Party(e, MeterTab.Damage, config.MeterRank);
        var vw = Txt.Width(value, Face.MonoSemibold, 13);
        var rx = right - S(24) - S(8);
        Txt.Line(dl, new Vector2(rx - vw, top.Y), value, Face.MonoSemibold, 13, Palette.White, 36);
        var label = $"Pull {e.Pull} · party ";
        var lw = Txt.Width(label, Face.Sans, 13);
        if (rx - vw - lw > x) Txt.Line(dl, new Vector2(rx - vw - lw, top.Y), label, Face.Sans, 13, Palette.Muted, 36);
    }

    // ---------------------------------------------------------------------------------------------
    // The picker.

    /// <summary>
    /// The picker (`.menu`, 300 wide): tonight's fights the meter kept, newest first, the one shown
    /// ticked, the live one tagged Now, the best starred; the other duties' after; and Pace against
    /// (Best, Last, Off) at its foot.
    /// </summary>
    private void DrawPicker(IMeterSource m)
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(S(260), 0), new Vector2(S(300), S(460)));
        if (!Kit.BeginMenu(Picker)) return;
        try
        {
            var w = S(300) - S(11.2f);
            var dl = ImGui.GetWindowDrawList();
            var shown = picked is { } id ? m.Get(id) : m.Current;
            var (here, others) = MeterView.Picker(m.Kept, shown, shown != null ? m.Best(shown.Content) : null);
            if (here.Count > 0)
            {
                Kit.MenuEyebrow($"{(DateTime.Now.Hour >= 17 ? "Tonight" : "Today")} · {here[0].Fight.Duty}", w);
                foreach (var r in here) PickRow(dl, r, w);
            }
            if (others.Count > 0)
            {
                Kit.MenuEyebrow("Other duties", w);
                foreach (var r in others) PickRow(dl, r, w, r.Fight.Duty);
            }
            Kit.MenuRule(w);
            var pos = ImGui.GetCursorScreenPos();
            var h = S(28) + S(5.6f) + S(7.2f);
            Txt.Line(dl, new Vector2(pos.X + S(8.8f), pos.Y + S(5.6f)), "Pace against", Face.Sans, 13, Palette.Muted, 28);
            var items = new[] { "Best", "Last", "Off" };
            var sw = Kit.SegmentedWidth(items);
            var pace = Kit.Segmented("##pick-pace", new Vector2(pos.X + w - S(8.8f) - sw, pos.Y + S(5.6f)), items, (int)config.MeterPace, true, false, S(28));
            if (pace != (int)config.MeterPace)
            {
                config.MeterPace = (PaceAgainst)pace;
                config.Save();
            }
            ImGui.Dummy(new Vector2(w, h));
        }
        finally
        {
            Kit.EndMenu();
        }
    }

    private void PickRow(ImDrawListPtr dl, PickerRow r, float w, string? sub = null)
    {
        var f = r.Fight;
        var title = r.Best ? $"Pull {f.Pull} · the best" : $"Pull {f.Pull}";
        var tag = r.Live ? "Now" : MeterView.OutcomeWord(f.Ended);
        var tagKind = r.Live ? TagKind.Ember : f.Ended == Core.Outcome.Clear ? TagKind.Clear : TagKind.Wipe;
        var time = Format.Clock(f.Duration);
        var tagW = tag != null ? Kit.TagWidth(tag, tagKind) : 0;
        var timeW = Txt.Width(time, Face.Mono, 12);
        var reserve = tagW + S(8) + timeW + S(8);
        var (clicked, _, min, max) = Kit.MenuRow("##pick" + f.Id, w, title, sub, true, 16, reserve);
        var (slot, right) = Kit.MenuRowParts(min, max, true, 16);
        if (r.Shown) Kit.Tick(dl, slot + new Vector2(S(8)), 16);
        else if (r.Best) Icons.Draw(dl, Icon.Star, slot + new Vector2((S(16) - S(13)) / 2), S(13), Palette.White);
        var cy = (min.Y + max.Y) / 2;
        if (tag != null) Kit.Tag(dl, new Vector2(right - tagW, cy - S(9)), tag, tagKind, 18);
        Txt.LineRight(dl, right - (tag != null ? tagW + S(8) : 0), min.Y, time, Face.Mono, 12, Palette.Faint, (max.Y - min.Y) / S(1));
        if (clicked)
        {
            picked = r.Live ? null : f.Id;
            unfolded = true;
            ImGui.CloseCurrentPopup();
        }
    }

    /// <summary>/raidplan meter unlock: the mouse back, the window movable again.</summary>
    public void Unlock()
    {
        config.MeterClickThrough = false;
        config.MeterLocked = false;
        config.Save();
    }
}
