using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
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
/// foot. Compact, 20px rows with the bar as the row's background, and the tabs in a menu by the lock.
/// Locked, it has no title bar and can't be moved or sized; click-through, the mouse goes to the game.
/// In a fight it's as tall as the party needs (at most the height you gave it); out of combat it folds
/// to one line (as it shrinks to the Boss before any fight, or when the meter has stopped). It stays
/// put at its anchor as it does: its top edge growing down, its foot growing up (its head at the foot
/// then, the rows reading up from it). Its numbers come from <see cref="IMeterSource"/> only.
/// </summary>
public sealed class MeterWindow : Window
{
    private const string Picker = "##rp-meter-picker";
    private const string TabMenu = "##rp-meter-tabs";

    /// <summary>A menu opens 6px off the head and 14px in from the meter's side (the mock-up's picker).</summary>
    private const float MenuGap = 6;

    private const float MenuInset = 14;

    private const float PickerWidth = 300;
    private const float TabMenuWidth = 200;

    /// <summary>The height it first opens to before it knows its party (CSS px).</summary>
    private const float OpenHeight = 382;

    private static readonly WindowSizeConstraints Sizes = new() { MinimumSize = new Vector2(260, 60), MaximumSize = new Vector2(720, 1400) };

    /// <summary>Folded or empty it's as tall as what it shows (a line, or the Boss and its words).</summary>
    private static readonly WindowSizeConstraints FitSizes = new() { MinimumSize = new Vector2(260, 0), MaximumSize = new Vector2(720, 1400) };

    private static readonly TabSpec[] TabSpecs = { new("Damage"), new("Healing"), new("Taken"), new("Deaths"), new("Events") };

    private static readonly string[] TabIds = { "##mtab0", "##mtab1", "##mtab2", "##mtab3", "##mtab4" };

    private static readonly Icon[] TabIcons = { Icon.Sword, Icon.Heart, Icon.Shield, Icon.Skull, Icon.List };

    /// <summary>How long the foot says the parse was copied.</summary>
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(2.5);

    private static readonly string[] PaceItems = { "Best", "Last", "Off" };

    private static readonly string[] SpotOrderOf = { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" };

    private static readonly Run[] OneRun = new Run[1];

    /// <summary>What the window shows this frame (decided once, before ImGui.Begin, since it sizes the window).</summary>
    private enum View
    {
        /// <summary>No meter, or no fight yet: the Boss asleep.</summary>
        Nothing,

        /// <summary>The meter stopped itself (<see cref="IMeterSource.Problem"/>): the Boss awake, and why.</summary>
        Stopped,

        /// <summary>Between pulls: one line.</summary>
        Folded,

        /// <summary>In combat before the first hit: the party with dashes.</summary>
        Waiting,

        /// <summary>A fight, live or kept.</summary>
        Fight,
    }

    private readonly Configuration config;
    private readonly Fonts fonts;
    private DateTime drawFailedAt;

    /// <summary>The fight picked to show (null: the current one, live or the last).</summary>
    private int? picked;

    /// <summary>Opened again by hand while folded (until the next fight ends).</summary>
    private bool unfolded;

    private bool wasInEncounter;

    /// <summary>The numbers' source last frame: when it changes (a sample starts or ends) nothing picked from the other is kept.</summary>
    private IMeterSource? lastSource;

    private View view;
    private MeterEncounter? shown;

    /// <summary>The window's width last drawn (CSS px), kept as it folds and opens.</summary>
    private float widthCss = 330;

    /// <summary>It has been drawn once (at the size Dalamud kept for it), so its width is known and it can fold.</summary>
    private bool widthKnown;

    // Where it stays put (its anchor, in the settings) and how it's held.

    /// <summary>The left button went down on the meter (its title bar, an edge, anywhere in it) and isn't up yet.</summary>
    private bool heldOnMe;

    /// <summary>You're moving or sizing it (unlocked, held): it isn't placed or sized for you until you let go, and its anchor follows it.</summary>
    private bool held;

    /// <summary>The title bar's height when it shows (screen px), measured once it has; until then ImGui's frame height.</summary>
    private float titleH;

    /// <summary>How tall the content drawn last frame was (screen px): ImGui fits a folded or empty meter to it this frame.</summary>
    private float contentH;

    /// <summary>The height asked for in a fight last (screen px; NaN before any): a different one while held is one you gave it.</summary>
    private float fightH = float.NaN;

    /// <summary>A height you gave it by its edge (CSS px; 0 none), kept once you let go.</summary>
    private float givenNow;

    /// <summary>The anchor moved with you while held: saved once you let go.</summary>
    private bool anchorMoved;

    private Vector2 lastPos;
    private Vector2 lastSize;
    private bool drawnOnce;

    /// <summary>The head's band this frame (screen px), for the menus to open off it.</summary>
    private float headTop;

    private float headBottom;

    /// <summary>The menus' heights when last open (screen px), to choose which way they open.</summary>
    private float pickerH;

    private float tabMenuH;

    /// <summary>The Preview tag didn't fit in the head this frame: the foot shows it.</summary>
    private bool previewInFoot;

    /// <summary>The rows as drawn, worked out once a snapshot (the source makes a new one a few times a second while a fight is on).</summary>
    private (MeterEncounter? Of, MeterTab Tab, MeterRank Rank, List<MeterLine> Rows, MeterLine? LimitBreak, (string Value, string? Unit) Party) lines =
        (null, MeterTab.Damage, MeterRank.EncDps, new List<MeterLine>(), null, (string.Empty, null));

    /// <summary>The shown fight's words (its clock, which pull, its deaths…), worked out once a snapshot so a frame makes no strings.</summary>
    private readonly Said said = new();

    /// <summary>Pace's words, made again only when what they say changes.</summary>
    private (int Pull, string Phase, int Seconds, int Ahead, string Label, string AheadText, string On) paceWords = (-1, string.Empty, -1, 0, string.Empty, string.Empty, string.Empty);

    /// <summary>The Deaths tab's lines with their counts, made again only when the counts change.</summary>
    private (int Alive, int Cut, string Text) aliveWords = (-1, -1, string.Empty);

    private (int Count, int Cut, bool Wipe, string Text) deathFoot = (-1, -1, false, string.Empty);

    /// <summary>Why it stopped, in the empty meter's words, made again only for another reason.</summary>
    private (string? Of, string Text) stoppedWords = (null, string.Empty);

    /// <summary>When the shown fight was copied as text (the foot says so for a moment).</summary>
    private DateTime copiedAt;

    /// <summary>The Events tab's feed (newest first, each time in words, the foot's counts), made again only for a new list from the source.</summary>
    private (IReadOnlyList<MeterEvent>? Of, int Count, bool Compact, List<MeterEvent> Newest, List<string> Times, string Foot) feed =
        (null, -1, false, new List<MeterEvent>(), new List<string>(), string.Empty);

    /// <summary>The limit break gauge's bars, worked out again only when the gauge changes.</summary>
    private (LimitBreakGauge? Of, float[] Bars) gaugeBars = (null, Array.Empty<float>());

    /// <summary>Column heads in capitals.</summary>
    private static readonly Dictionary<string, string> CapsOf = new();

    public MeterWindow(Configuration config, Fonts fonts)
        : base("Meter###RaidPlannerMeter")
    {
        this.config = config;
        this.fonts = fonts;
        Size = new Vector2(330, OpenHeight);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = Sizes;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        IsOpen = config.ShowMeter;
    }

    /// <summary>Where the numbers come from (the plugin's meter, set by the plugin); null before there is one.</summary>
    public Func<IMeterSource?>? Source { get; set; }

    /// <summary>A sample fight to look at the meter with (Settings → Meter, the empty meter's link, `/raidplan meter preview`).</summary>
    public IMeterSource? Preview { get; set; }

    /// <summary>Starts the sample (unless a real fight is on: it would end at once); false when it couldn't.</summary>
    public Func<bool>? StartSample { get; set; }

    /// <summary>In a duty (the meter shows only there unless Everywhere).</summary>
    public Func<bool>? InDuty { get; set; }

    /// <summary>Opens the opened-up window on a tab, a fight and a player (or a death).</summary>
    public Action<MeterTab, int?, int?, int?>? OpenDetail { get; set; }

    /// <summary>Opens Settings on the Meter tab.</summary>
    public Action? OpenSettings { get; set; }

    private IMeterSource? Meter => Preview ?? Source?.Invoke();

    private MeterSizes Z => MeterSizes.Of(config.MeterCompact);

    /// <summary>It grows up: anchored at its foot, its head at the foot.</summary>
    private bool Up => config.MeterGrows == MeterGrows.Up;

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
        try
        {
            Decide();
        }
        catch (Exception ex)
        {
            // Outside the window's own catch (before ImGui.Begin): a fault here would stop every window of the plugin drawing.
            view = View.Nothing;
            shown = null;
            Failed(ex, "Raid Planner's meter couldn't read its numbers");
        }
        try
        {
            Arrange();
        }
        catch (Exception ex)
        {
            Failed(ex, "Raid Planner's meter couldn't place itself");
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
            Track();
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
            Failed(ex, "Raid Planner's meter couldn't draw");
        }
        finally
        {
            Theme.Pop();
        }
    }

    private void Failed(Exception ex, string what)
    {
        if (DateTime.UtcNow - drawFailedAt <= TimeSpan.FromSeconds(10)) return;
        drawFailedAt = DateTime.UtcNow;
        Services.Log.Error(ex, what);
    }

    /// <summary>What to show this frame, and the fight shown.</summary>
    private void Decide()
    {
        var m = Meter;
        if (!ReferenceEquals(m, lastSource))
        {
            // A sample started or ended: what was picked or opened belongs to the other, and their ids mean nothing here.
            lastSource = m;
            picked = null;
            unfolded = false;
            wasInEncounter = false;
        }
        var live = m?.InEncounter == true;
        if (wasInEncounter && !live) unfolded = false;
        if (live) unfolded = false;
        // A new pull shows itself, whatever was picked to look at before it.
        if (live && !wasInEncounter) picked = null;
        wasInEncounter = live;
        shown = picked is { } id ? m?.Get(id) ?? m?.Current : m?.Current;
        if (picked != null && shown?.Id != picked) picked = null;
        view = m == null ? View.Nothing
            // Stopped, it says so (a kept fight can still be picked to look at).
            : m.Problem != null && !live && picked == null ? View.Stopped
            : shown == null && !m.Waiting ? View.Nothing
            // Between pulls it folds to one line: the last pull's length and the party's number; a press opens it.
            : !live && !m.Waiting && !unfolded && picked == null && Preview == null ? View.Folded
            : m.Waiting && picked == null ? View.Waiting
            : View.Fight;
    }

    // ---------------------------------------------------------------------------------------------
    // Its size and place.

    /// <summary>
    /// Before ImGui.Begin: its flags, its height (folded or empty, what it shows; in a fight what the
    /// party needs, at most the height you gave it) and its place, from its anchor (its top edge kept
    /// growing down, its foot growing up), kept on the screen. Not while you're moving or sizing it.
    /// </summary>
    private void Arrange()
    {
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) heldOnMe = false;
        held = heldOnMe && !config.MeterLocked;
        if (!held) Commit();
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing;
        if (config.MeterLocked)
        {
            // Locked: no title bar, no moving or sizing; click-through, the mouse goes to the game.
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus;
            if (config.MeterClickThrough) Flags |= ImGuiWindowFlags.NoInputs;
        }
        var scale = ImGuiHelpers.GlobalScale;
        var chrome = config.MeterLocked ? 0 : titleH > 0 ? titleH : ImGui.GetFrameHeight();
        // Folded (or empty) it's as tall as what it shows, as wide as it was (ImGui fits the height to what
        // was drawn; the width is set, so it's kept: known once it's been drawn).
        var fits = widthKnown && view is View.Nothing or View.Stopped or View.Folded;
        SizeConstraints = fits ? FitSizes : Sizes;
        var height = float.NaN;
        if (fits)
        {
            Flags |= ImGuiWindowFlags.AlwaysAutoResize;
            Size = new Vector2(widthCss, 0);
            SizeCondition = ImGuiCond.Always;
            if (contentH > 0) height = contentH + chrome;
        }
        else if (widthKnown && !held)
        {
            // In a fight: as tall as the party needs (a bigger party, a taller meter), at most the height you gave it.
            var given = config.MeterCompact ? config.MeterCompactHeight : config.MeterHeight;
            var needed = Z.Needed(PartyRows(), config.MeterPace != PaceAgainst.Off);
            var h = MeterLayout.FightHeight(needed, chrome / scale, given, Sizes.MinimumSize.Y, Sizes.MaximumSize.Y);
            Size = new Vector2(widthCss, h);
            SizeCondition = ImGuiCond.Always;
            height = h * scale;
            fightH = height;
        }
        else SizeCondition = ImGuiCond.FirstUseEver;
        if (!config.MeterAnchored || config.MeterAnchorFor != config.MeterGrows || held || float.IsNaN(height)) return;
        var vp = ImGui.GetMainViewport();
        var otherScreens = (ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0;
        var at = MeterLayout.Place(new Vector2(config.MeterAnchorX, config.MeterAnchorY), new Vector2(widthCss * scale, height), config.MeterGrows, vp.WorkPos,
            vp.WorkPos + vp.WorkSize, otherScreens);
        ImGui.SetNextWindowPos(at, ImGuiCond.Always);
    }

    /// <summary>How many party rows the shown fight has (the limit break's apart): what its height is worked out for.</summary>
    private int PartyRows()
    {
        if (shown == null) return 0;
        var n = 0;
        var rows = shown.Rows;
        for (var i = 0; i < rows.Count; i++)
            if (!rows[i].IsLimitBreak)
                n++;
        return n;
    }

    /// <summary>
    /// Inside the window, each frame: the title bar's height, whether you've taken hold of it, and its
    /// anchor: read from where it is the first time (one saved by an older version stays where it was),
    /// moved to the other corner when Grows changes, following it while you move or size it.
    /// </summary>
    private void Track()
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (!config.MeterLocked)
        {
            var t = ImGui.GetCursorStartPos().Y;
            if (t > 0) titleH = t;
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            heldOnMe = !config.MeterLocked && ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByPopup);
        if (!config.MeterAnchored || config.MeterAnchorFor != config.MeterGrows)
        {
            // 0.3.0 kept the height it first opened to (382) by itself, not only one you gave it: that one isn't yours.
            if (!config.MeterAnchored && MathF.Abs(config.MeterHeight - OpenHeight) < 0.5f) config.MeterHeight = 0;
            SetAnchor(MeterLayout.Corner(pos, size, config.MeterGrows));
            config.Save();
        }
        else if (held && drawnOnce && (pos != lastPos || size != lastSize))
        {
            SetAnchor(MeterLayout.Corner(pos, size, config.MeterGrows));
            anchorMoved = true;
            // Sized by its edge in a fight: that's the height you're giving it.
            if (view is View.Fight or View.Waiting && !float.IsNaN(fightH) && MathF.Abs(size.Y - fightH) > 1.5f) givenNow = size.Y / ImGuiHelpers.GlobalScale;
        }
        lastPos = pos;
        lastSize = size;
        drawnOnce = true;
    }

    private void SetAnchor(Vector2 at)
    {
        config.MeterAnchorX = at.X;
        config.MeterAnchorY = at.Y;
        config.MeterAnchored = true;
        config.MeterAnchorFor = config.MeterGrows;
    }

    /// <summary>Once you let go: the height you gave it and where you put it, saved once (not every frame of the drag).</summary>
    private void Commit()
    {
        if (givenNow > 0)
        {
            if (config.MeterCompact) config.MeterCompactHeight = MathF.Round(givenNow);
            else config.MeterHeight = MathF.Round(givenNow);
            givenNow = 0;
            anchorMoved = true;
        }
        if (!anchorMoved) return;
        anchorMoved = false;
        config.Save();
    }

    // ---------------------------------------------------------------------------------------------
    // What it shows.

    private void DrawInside()
    {
        var m = Meter;
        var win = ImGui.GetWindowPos();
        var top = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth();
        var bottom = win.Y + ImGui.GetWindowHeight();
        var dl = Kit.Dl;
        widthCss = w / ImGuiHelpers.GlobalScale;
        widthKnown = true;
        previewInFoot = false;
        if (shown != null) Words(shown);
        Kit.PushBox(win.X, w);
        try
        {
            switch (view)
            {
                case View.Nothing:
                case View.Stopped:
                    contentH = DrawEmpty(dl, top, w, m);
                    Fit(top, w, contentH);
                    return;
                case View.Folded when shown != null:
                    contentH = S(Z.Head);
                    Folded(dl, top, w, shown);
                    Fit(top, w, contentH);
                    return;
                case View.Waiting when m != null:
                    DrawWaiting(dl, top, w, bottom, shown, m);
                    break;
                case View.Fight when m != null && shown != null:
                    DrawFight(dl, top, w, bottom, shown, m);
                    break;
            }
            // In a fight the content is the window's own height: so it's what ImGui fits it to if it folds next frame.
            contentH = MathF.Max(1, bottom - top.Y);
            Fit(top, w, contentH);
        }
        finally
        {
            Kit.PopBox();
        }
    }

    /// <summary>What ImGui sizes the window by: its content, edge to edge, this tall.</summary>
    private static void Fit(Vector2 top, float w, float h)
    {
        ImGui.SetCursorScreenPos(top);
        ImGui.Dummy(new Vector2(w, MathF.Max(1, h)));
    }

    /// <summary>
    /// A fight, from the head's edge: the head, the tabs (not compact), pace on the Damage tab, the
    /// columns' heads, the rows (the top player nearest the head), the limit break after them, and the
    /// party's line at the far edge. Growing up it's the same bands from the foot.
    /// </summary>
    private void DrawFight(ImDrawListPtr dl, Vector2 top, float w, float bottom, MeterEncounter e, IMeterSource m)
    {
        var z = Z;
        var ended = !e.Live;
        var stack = new MeterStack(top.Y, bottom, Up);
        Header(dl, top.X, ref stack, w, said.Clock, e.Live, MeterView.OutcomeWord(e.Ended), e.Ended == Core.Outcome.Clear ? TagKind.Clear : TagKind.Wipe,
            said.Pick, m, null, true);
        if (!z.IsCompact) TabsRow(dl, top.X, ref stack, w);
        var tab = MeterLayout.Known(config.MeterTab);
        if (tab == MeterTab.Deaths)
        {
            DrawDeaths(dl, top.X, w, stack, e, m.InEncounter && !ended);
            return;
        }
        if (tab == MeterTab.Events)
        {
            DrawEvents(dl, top.X, w, stack, e, m);
            return;
        }
        if (tab == MeterTab.Damage && config.MeterPace != PaceAgainst.Off && m.Pace(e.Id, config.MeterPace) is { } pace)
            PaceRow(dl, top.X, ref stack, w, pace);
        Columns(dl, top.X, ref stack, w, tab);
        var fy = stack.Far(S(z.Foot));
        Rows(dl, top.X, w, e, tab, stack);
        Footer(dl, top.X, fy, w, e, tab, ended, stack);
    }

    /// <summary>The rows as drawn for the shown fight and tab (worked out again only for a new snapshot, tab or number).</summary>
    private (List<MeterLine> Rows, MeterLine? LimitBreak, (string Value, string? Unit) Party) LinesOf(MeterEncounter e, MeterTab tab, MeterRank rank)
    {
        if (!ReferenceEquals(lines.Of, e) || lines.Tab != tab || lines.Rank != rank)
        {
            var (rows, lb) = MeterView.Lines(e, tab, rank);
            lines = (e, tab, rank, rows, lb, MeterView.Party(e, tab, rank));
        }
        return (lines.Rows, lines.LimitBreak, lines.Party);
    }

    // ---------------------------------------------------------------------------------------------
    // The head: the clock, which pull, the tab menu, the lock.

    /// <summary>
    /// The head (`.mt-h`, 36 tall; 30 compact) at the anchored edge: on the right the lock, compact's
    /// tab menu ("Damage ⌄") and the picker ("Pull 12 ⌄"); on the left the clock (mono 600) with its
    /// dot, then the Preview tag and the outcome's tag, or the waiting line, as far as they fit before
    /// the buttons. Compact (no tab row to do it) has a hairline on its far side.
    /// </summary>
    private void Header(ImDrawListPtr dl, float left, ref MeterStack stack, float w, string clock, bool beating, string? tag, TagKind tagKind, string? pick,
        IMeterSource? m, string? waitingLine, bool hasFight)
    {
        var z = Z;
        var h = S(z.Head);
        var y = stack.Near(h);
        if (z.IsCompact) Kit.Rule(dl, left, left + w, stack.Away(y, h));
        headTop = y;
        headBottom = y + h;
        var cy = y + h / 2;
        var right = left + w - S(6);
        // The lock (`.mt-ib`): ember-washed when locked, a pointer when click-through, quiet when open.
        var lx = right - S(24);
        var locked = config.MeterLocked;
        var icon = !locked ? Icon.LockOpen : config.MeterClickThrough ? Icon.MousePointer : Icon.Lock;
        if (SmallButton("##meter-lock", new Vector2(lx, cy - S(12)), icon, locked, locked ? "Locked where it is. Press to move and size it again." : "Lock it where it is"))
        {
            config.MeterLocked = !locked;
            config.Save();
        }
        var edge = lx;
        if (z.IsCompact)
        {
            // Compact: the tabs as one menu by the lock, its button the tab's word.
            var word = MeterLayout.TabWord(config.MeterTab);
            var tw = DropWidth(word);
            var tx = edge - S(6) - tw;
            if (DropButton("##meter-tabs", new Vector2(tx, cy - S(12)), tw, word)) ImGui.OpenPopup(TabMenu);
            DrawTabMenu(tx, hasFight);
            edge = tx;
        }
        if (pick != null && m != null)
        {
            // The picker (`.mt-pick`): 24 tall, 6px corners, a hairline; "Pull 12 ⌄".
            var pw = DropWidth(pick);
            var px = edge - S(z.IsCompact ? 6 : 8) - pw;
            if (DropButton("##meter-pick", new Vector2(px, cy - S(12)), pw, pick)) ImGui.OpenPopup(Picker);
            DrawPicker(m, px);
            edge = px;
        }
        // The left: the dot and the clock, then the tags while they fit before the buttons.
        var x = left + S(12);
        Kit.BeatDot(dl, new Vector2(x + S(3), cy), beating ? Palette.Ember : Palette.Faint, beating);
        x += S(6 + 7);
        x += Txt.Line(dl, new Vector2(x, y), clock, Face.MonoSemibold, z.ClockPx, Palette.White, z.Head) + S(8);
        var room = edge - S(8);
        if (Preview != null)
        {
            // The sample's mark; pressed, it stops the sample. Where it doesn't fit, the foot shows it.
            var pw = Kit.TagWidth("Preview", TagKind.Plain);
            if (x + pw <= room)
            {
                PreviewTag(dl, new Vector2(x, cy - S(9)));
                x += pw + S(8);
            }
            else previewInFoot = true;
        }
        if (tag != null)
        {
            var tw = Kit.TagWidth(tag, tagKind);
            if (x + tw <= room)
            {
                Kit.Tag(dl, new Vector2(x, cy - S(9)), tag, tagKind, 18);
                x += tw + S(8);
            }
        }
        // The limit break gauge, when it's wanted in the head, as far as it fits before the buttons.
        if (config.MeterGauge == GaugePlace.Head && beating && Bars(m) is { Length: > 0 } bars)
        {
            var gw = GaugeWidth(bars.Length);
            if (x + gw <= room)
            {
                DrawGauge(dl, new Vector2(x, cy), gw, bars, z.IsCompact ? 4 : 5);
                if (Kit.Pointed(new Vector2(x, y), new Vector2(x + gw, y + h)) && m?.LimitBreak is { } g) Kit.Tooltip(MeterText.GaugeTip(g));
                x += gw + S(8);
            }
        }
        // "Waiting for the first hit", as much as fits before the buttons.
        var px2 = z.IsCompact ? 12 : 13;
        if (waitingLine != null && room - x > S(20))
            Txt.Line(dl, new Vector2(x, y), Txt.Fit(waitingLine, Face.Sans, px2, room - x), Face.Sans, px2, Palette.Muted, z.Head);
    }

    /// <summary>The Preview tag: the sample's mark. Pressed, the sample stops.</summary>
    private void PreviewTag(ImDrawListPtr dl, Vector2 pos)
    {
        var w = Kit.TagWidth("Preview", TagKind.Plain);
        var (clicked, hovered, _) = Kit.Hit("##meter-preview", pos, new Vector2(w, S(18)));
        Kit.Tag(dl, pos, "Preview", TagKind.Plain, 18);
        if (hovered) Kit.Tooltip("A sample fight with made-up numbers. Press to stop it; a real fight stops it too.");
        if (clicked) Preview = null;
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

    /// <summary>A dropdown's button's width (`.mt-pick`): 8 in, the words (550 12px), 4, the chevron (13), 4.</summary>
    private static float DropWidth(string label) => S(8) + Txt.Width(label, Face.SansMedium, 12) + S(4) + S(13) + S(4);

    /// <summary>A dropdown's button (`.mt-pick`, the picker's and compact's tab menu): 24 tall, 6px corners, a hairline, its words and a chevron.</summary>
    private static bool DropButton(string id, Vector2 pos, float width, string label)
    {
        var dl = Kit.Dl;
        var size = new Vector2(width, S(24));
        var (clicked, hovered, _) = Kit.Hit(id, pos, size);
        if (hovered) dl.AddRectFilled(pos, pos + size, Palette.U32(Palette.Hover), S(6));
        dl.AddRect(pos + new Vector2(0.5f), pos + size - new Vector2(0.5f), Palette.U32(Palette.Line2), S(6), ImDrawFlags.None, 1f);
        var tx = pos.X + S(8);
        tx += Txt.Line(dl, new Vector2(tx, pos.Y), label, Face.SansMedium, 12, Palette.Text, 24) + S(4);
        Icons.Draw(dl, Icon.ChevronDown, new Vector2(tx, pos.Y + (size.Y - S(13)) / 2), S(13), Palette.Faint);
        Kit.FocusRing(pos, pos + size, S(6));
        return clicked;
    }

    /// <summary>The four tabs (`.mt-tabs`, 27 tall, 12px at 550) next to the head, a hairline on their far side.</summary>
    private void TabsRow(ImDrawListPtr dl, float left, ref MeterStack stack, float w)
    {
        var h = S(Z.Tabs);
        var y = stack.Near(h);
        var current = MeterLayout.Known(config.MeterTab);
        // The five tabs where they fit; a narrower meter has compact's menu in their place.
        if (Kit.TabsWidth(TabSpecs, TabSize.Meter) + S(12) > w)
        {
            var word = MeterLayout.TabWord(current);
            var tx = left + S(Z.Pad);
            if (DropButton("##meter-tabs-row", new Vector2(tx, y + (h - S(24)) / 2), DropWidth(word), word)) ImGui.OpenPopup(TabMenu);
            headTop = y;
            headBottom = y + h;
            DrawTabMenu(tx, shown != null);
            Kit.Rule(dl, left, left + w, stack.Away(y, h));
            return;
        }
        var t = (MeterTab)Kit.Tabs("##meter-tab", new Vector2(left + S(6), y), TabSpecs, (int)current, TabSize.Meter);
        Kit.Rule(dl, left, left + w, stack.Away(y, h));
        if (t == config.MeterTab) return;
        config.MeterTab = t;
        config.Save();
    }

    /// <summary>Pace (`.mt-pace`, 26 tall; 22 compact): the phase and how far into it, the bar (this pull ember, the other's a white tick), "on pull 4 +3.4%".</summary>
    private void PaceRow(ImDrawListPtr dl, float left, ref MeterStack stack, float w, PaceLine pace)
    {
        var z = Z;
        var h = S(z.Pace);
        var y = stack.Near(h);
        var (label, ahead, on) = PaceWords(pace);
        var x = left + S(z.Pad);
        var lw = Txt.Spaced(dl, new Vector2(x, y + (h - Txt.Natural(Face.Mono, 9)) / 2), label, Face.Mono, 9, Palette.Faint, 0.09f);
        var aheadPx = z.IsCompact ? 11 : 12;
        var aw = Txt.Width(ahead, Face.MonoSemibold, aheadPx);
        var ow = Txt.Width(on, Face.Sans, 11);
        var rx = left + w - S(z.Pad);
        Txt.Line(dl, new Vector2(rx - aw, y), ahead, Face.MonoSemibold, aheadPx, Palette.White, z.Pace);
        Txt.Line(dl, new Vector2(rx - aw - ow, y), on, Face.Sans, 11, Palette.Muted, z.Pace);
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
        Kit.Rule(dl, left, left + w, stack.Away(y, h));
    }

    /// <summary>Pace's words ("PACE · P2 +0:41" in capitals, "+3.4%", "on pull 4 "), made again only when what they say changes.</summary>
    private (string Label, string Ahead, string On) PaceWords(PaceLine pace)
    {
        var seconds = (int)Math.Floor(Math.Max(0, pace.PhaseSeconds));
        var ahead = (int)Math.Round(pace.Ahead * 1000);
        if (paceWords.Pull != pace.AgainstPull || paceWords.Phase != pace.Phase || paceWords.Seconds != seconds || paceWords.Ahead != ahead)
        {
            var label = Txt.Caps($"Pace · {pace.Phase} +{Format.Clock(seconds)}");
            paceWords = (pace.AgainstPull, pace.Phase, seconds, ahead, label, Format.SignedPercent(pace.Ahead),
                "on pull " + pace.AgainstPull.ToString(CultureInfo.InvariantCulture) + " ");
        }
        return (paceWords.Label, paceWords.AheadText, paceWords.On);
    }

    /// <summary>The columns' heads (`.mt-cols`, 20 tall; 16 compact; mono 9px capitals) next to the rows, right-aligned to their columns.</summary>
    private void Columns(ImDrawListPtr dl, float left, ref MeterStack stack, float w, MeterTab tab)
    {
        var z = Z;
        var h = S(z.Cols);
        var y = stack.Near(h);
        var (a, b) = MeterView.Heads(tab, config.MeterRank);
        a = Caps(a);
        b = Caps(b);
        var g = Grid(left, w, z);
        var lh = Txt.Natural(Face.Mono, 9);
        // Against the rows: at the band's foot when they're under it, at its top when they're above (growing up).
        var ly = stack.Up ? y + S(2) : y + h - S(2) - lh;
        Txt.Spaced(dl, new Vector2(g.ValueRight - Txt.SpacedWidth(a, Face.Mono, 9, 0.08f), ly), a, Face.Mono, 9, Palette.Faint, 0.08f);
        Txt.Spaced(dl, new Vector2(g.SecondRight - Txt.SpacedWidth(b, Face.Mono, 9, 0.08f), ly), b, Face.Mono, 9, Palette.Faint, 0.08f);
    }

    private static string Caps(string text)
    {
        if (CapsOf.TryGetValue(text, out var caps)) return caps;
        return CapsOf[text] = Txt.Caps(text);
    }

    /// <summary>
    /// The row's grid (`18px 1fr 30px 66px 40px`, 8 apart, 12 in; compact `16px 1fr 26px 66px 34px`, 6
    /// apart, 10 in): the spot's left, the name's left, the job's left, the number's right, the second's right.
    /// </summary>
    private static (float Spot, float Name, float Job, float ValueRight, float SecondRight) Grid(float left, float w, MeterSizes z)
    {
        var x0 = left + S(z.Pad);
        var secondR = left + w - S(z.Pad);
        var valueR = secondR - S(z.SecondCol) - S(z.Gap);
        var job = valueR - S(z.ValueCol) - S(z.Gap) - S(z.JobCol);
        return (x0, x0 + S(z.Spot) + S(z.Gap), job, valueR, secondR);
    }

    /// <summary>The rows (25 tall; 20 compact), biggest first from the head's side, then the limit break's after a hairline. Past the room they're cut.</summary>
    private void Rows(ImDrawListPtr dl, float left, float w, MeterEncounter e, MeterTab tab, MeterStack stack)
    {
        var z = Z;
        var (rows, lb, _) = LinesOf(e, tab, config.MeterRank);
        var g = Grid(left, w, z);
        var rowH = S(z.Row);
        var lbH = S(z.LimitBreak);
        var n = Math.Min(rows.Count, stack.Fits(rowH));
        var a = MathF.Min(stack.NearEdge, stack.FarEdge);
        var b = MathF.Max(stack.NearEdge, stack.FarEdge);
        dl.PushClipRect(new Vector2(left, a), new Vector2(left + w, b), true);
        try
        {
            for (var i = 0; i < n; i++) Row(dl, rows[i], left, stack.Row(i, rowH), w, g, z, e, tab);
            if (lb != null && stack.Room - n * rowH + 1 >= lbH)
            {
                var y = stack.Up ? stack.NearEdge - n * rowH - lbH : stack.NearEdge + n * rowH;
                Kit.Rule(dl, left, left + w, stack.Toward(y, lbH));
                if (z.IsCompact) BarBehind(dl, left, w, y, lbH, lb.Bar, false, false);
                Kit.LimitMark(dl, new Vector2(g.Spot, y + (lbH - S(z.Spot)) / 2), z.Spot);
                Txt.Line(dl, new Vector2(g.Name, y), "Limit break", Face.SansMedium, z.NamePx, Palette.White, z.LimitBreak);
                Txt.LineRight(dl, g.ValueRight, y, lb.Value, Face.MonoSemibold, z.ValuePx, Palette.White, z.LimitBreak);
                Txt.LineRight(dl, g.SecondRight, y, lb.Second, Face.Mono, z.SecondPx, Palette.Muted, z.LimitBreak);
                if (!z.IsCompact) Bar(dl, left, w, y + lbH, lb.Bar, false, false);
            }
        }
        finally
        {
            dl.PopClipRect();
        }
    }

    /// <summary>A player's row: the spot, the name (muted with a red skull once dead), the job, the number, its second column, the bar. Pressed, the meter opens up on them.</summary>
    private void Row(ImDrawListPtr dl, MeterLine line, float left, float y, float w, (float Spot, float Name, float Job, float ValueRight, float SecondRight) g,
        MeterSizes z, MeterEncounter e, MeterTab tab)
    {
        var r = line.Row;
        var rowH = S(z.Row);
        var min = new Vector2(left, y);
        var max = new Vector2(left + w, y + rowH);
        var (clicked, hovered, _) = Kit.Hit(line.Id, min, max - min);
        var dim = r.Dead;
        if (z.IsCompact) BarBehind(dl, left, w, y, rowH, line.Bar, r.IsYou, dim);
        if (hovered) dl.AddRectFilled(min, max, Palette.U32(Palette.Hover));
        if (clicked) OpenDetail?.Invoke(tab, e.Id, r.PartyId, null);
        Kit.Spot(dl, new Vector2(g.Spot + S(z.Spot) / 2, y + rowH / 2), z.Spot, r.Slot, Palette.RoleColor(r.Role), 1.5f);
        var skull = z.IsCompact ? 11 : 12;
        var nameText = config.MeterJobsOnly ? r.Job : r.Name;
        var nameRoom = (config.MeterJobsOnly ? g.ValueRight - S(z.ValueCol) : g.Job - S(z.Gap)) - g.Name - (dim ? S(5 + skull) : 0);
        var nw = Txt.Line(dl, new Vector2(g.Name, y), Txt.Fit(nameText, Face.SansMedium, z.NamePx, nameRoom), Face.SansMedium, z.NamePx,
            dim ? Palette.Muted : Palette.White, z.Row);
        if (dim) Icons.Draw(dl, Icon.Skull, new Vector2(g.Name + nw + S(5), y + (rowH - S(skull)) / 2), S(skull), Palette.Danger);
        if (!config.MeterJobsOnly) Txt.Spaced(dl, new Vector2(g.Job, y + (rowH - Txt.Natural(Face.Mono, z.JobPx)) / 2), Caps(r.Job), Face.Mono, z.JobPx, Palette.Faint, 0.06f);
        Txt.LineRight(dl, g.ValueRight, y, line.Value, Face.MonoSemibold, z.ValuePx, dim ? Palette.Muted : Palette.White, z.Row);
        Txt.LineRight(dl, g.SecondRight, y, line.Second, Face.Mono, z.SecondPx, Palette.Muted, z.Row);
        if (!z.IsCompact) Bar(dl, left, w, y + rowH, line.Bar, r.IsYou, dim);
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

    /// <summary>
    /// Compact's bar: the row's own background to the top player's (6px in at each side, a pixel
    /// short of the row's edges so rows don't run together), white at 5.5% (a death's at 3%, yours
    /// ember-washed), with the cast bar's line a pixel thick along its foot (grey, yours the ember gradient).
    /// </summary>
    private static void BarBehind(ImDrawListPtr dl, float left, float w, float y, float rowH, float fraction, bool you, bool dead)
    {
        var x = left + S(6);
        var fw = (w - S(12)) * Math.Clamp(fraction, 0, 1);
        if (fw < 1) return;
        var a = new Vector2(x, y + 1);
        var b = new Vector2(x + fw, y + rowH - 1);
        dl.AddRectFilled(a, b, Palette.U32(you ? Palette.EmberWash : Palette.Rgba(255, 255, 255, dead ? 0.03f : 0.055f)));
        var la = new Vector2(x, b.Y - 1);
        if (you) Kit.CastGradient(dl, la, b);
        else dl.AddRectFilled(la, b, Palette.U32(Palette.Rgba(255, 255, 255, dead ? 0.12f : 0.24f)));
    }

    /// <summary>The foot (`.mt-f`, 28 tall; 22 compact; mono 11px, 10 compact) at the far edge: the party's number, and what's worth saying on the right.</summary>
    private void Footer(ImDrawListPtr dl, float left, float y, float w, MeterEncounter e, MeterTab tab, bool ended, MeterStack stack)
    {
        var z = Z;
        var h = S(z.Foot);
        Kit.Rule(dl, left, left + w, stack.Toward(y, h));
        var (value, unit) = LinesOf(e, tab, config.MeterRank).Party;
        var x = left + S(z.Pad);
        x += Txt.Line(dl, new Vector2(x, y), "Party · ", Face.Mono, z.FootPx, Palette.Muted, z.Foot);
        x += Txt.Line(dl, new Vector2(x, y), value, Face.MonoSemibold, z.FootPx, Palette.White, z.Foot);
        // The unit after the number ("HPS"), and the gauge only after it.
        if (unit != null) x += Txt.Width(" ", Face.Mono, z.FootPx) + Txt.Line(dl, new Vector2(x + Txt.Width(" ", Face.Mono, z.FootPx), y), unit, Face.Mono, z.FootPx, Palette.Muted, z.Foot);
        var rx = left + w - S(z.Pad);
        if (previewInFoot)
        {
            PreviewTag(dl, new Vector2(rx - Kit.TagWidth("Preview", TagKind.Plain), y + (h - S(18)) / 2));
            return;
        }
        var right = DateTime.UtcNow - copiedAt < CopiedFor ? "copied to the clipboard"
            : config.MeterLocked && config.MeterClickThrough ? "click-through"
            : ended ? said.KeptAs
            : tab switch
            {
                MeterTab.Healing => "shields not counted",
                MeterTab.Taken => "incl. bleeds",
                _ => said.Where ?? (config.MeterRank == MeterRank.Dps ? "DPS" : "ENCDPS"),
            };
        var rw = Txt.LineRight(dl, rx, y, right, Face.Mono, z.FootPx, Palette.Muted, z.Foot);
        FootGauge(dl, x + S(12), rx - rw - S(10), y, h, !ended);
    }

    /// <summary>The limit break gauge in the foot (when it's wanted there and the fight is live), right-aligned before what the foot says on the right, when there's room.</summary>
    private void FootGauge(ImDrawListPtr dl, float from, float to, float y, float h, bool live)
    {
        if (config.MeterGauge != GaugePlace.Foot || !live || Bars(Meter) is not { Length: > 0 } bars) return;
        var gw = GaugeWidth(bars.Length);
        if (to - gw < from) return;
        var gx = to - gw;
        DrawGauge(dl, new Vector2(gx, y + h / 2), gw, bars, Z.IsCompact ? 4 : 5);
        if (Kit.Pointed(new Vector2(gx, y), new Vector2(to, y + h)) && Meter?.LimitBreak is { } g) Kit.Tooltip(MeterText.GaugeTip(g));
    }

    /// <summary>The gauge's bars now (none outside a party that fills one), worked out again only when it changes.</summary>
    private float[]? Bars(IMeterSource? m)
    {
        var g = m?.LimitBreak;
        if (g == null) return null;
        if (!Equals(gaugeBars.Of, g)) gaugeBars = (g, MeterText.Bars(g));
        return gaugeBars.Bars;
    }

    /// <summary>The gauge's width: "LB" in mono and a 16px bar each, 3 apart.</summary>
    private static float GaugeWidth(int bars) => Txt.Width("LB", Face.Mono, 9) + S(5) + bars * S(16) + Math.Max(0, bars - 1) * S(3);

    /// <summary>
    /// The limit break gauge (`.cast-bar`s side by side): "LB" in faint mono, then a bar each the party
    /// has, a full one the ember gradient, the one filling to its share, the rest a track; on its line's
    /// middle at <paramref name="leftMid"/>, at most <paramref name="width"/> wide. Shared with the cooldown tracker.
    /// </summary>
    public static void DrawGauge(ImDrawListPtr dl, Vector2 leftMid, float width, float[] bars, float height)
    {
        if (bars.Length == 0) return;
        var label = Txt.Width("LB", Face.Mono, 9);
        var gap = S(3);
        var bw = MathF.Min(S(16), (width - label - S(5) - gap * (bars.Length - 1)) / bars.Length);
        if (bw < S(5)) return;
        Txt.Draw(dl, new Vector2(leftMid.X, leftMid.Y - Txt.Natural(Face.Mono, 9) / 2), "LB", Face.Mono, 9, Palette.Faint);
        var x = leftMid.X + label + S(5);
        var h = S(height);
        var top = leftMid.Y - h / 2;
        foreach (var f in bars)
        {
            var a = new Vector2(x, top);
            var b = new Vector2(x + bw, top + h);
            dl.AddRectFilled(a, b, Palette.U32(Palette.Track), S(1.5f));
            if (f >= 1)
            {
                Kit.CastGradient(dl, a, b);
                dl.AddRect(a - new Vector2(1), b + new Vector2(1), Palette.U32(Palette.WithAlpha(Palette.EmberHi, 0.25f)), S(2), ImDrawFlags.None, 1f);
            }
            else if (f > 0) dl.AddRectFilled(a, new Vector2(x + bw * f, b.Y), Palette.U32(Palette.EmberDeep));
            x += bw + gap;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Deaths, waiting, folded, nothing yet.

    /// <summary>
    /// The Deaths tab: each death in red time, its spot and name, the killing blow and how big (compact:
    /// on one 20px line); pressed, its recap opens. The first nearest the head. Past the room (the foot,
    /// or the alive count after them) they're cut, and said.
    /// </summary>
    private void DrawDeaths(ImDrawListPtr dl, float left, float w, MeterStack stack, MeterEncounter e, bool live)
    {
        var z = Z;
        var deaths = said.Deaths;
        var footY = live ? 0 : stack.Far(S(z.Foot));
        var nameH = Txt.Natural(Face.SansMedium, 13);
        var subH = Txt.Natural(Face.Sans, 11);
        var rowH = z.IsCompact ? S(z.DeathRow) : S(6) * 2 + nameH + S(2) + subH;
        // Live, "7 alive" and its line sit after the rows (14 off them, 46 for the two; compact, one 22px line).
        var aliveH = !live ? 0 : z.IsCompact ? S(22) : S(14) + S(19.5f) + S(7.2f) + S(16) + S(6);
        var fit = Math.Max(0, (int)MathF.Floor((stack.Room - aliveH + 1) / rowH));
        var n = Math.Min(deaths.Count, fit);
        var cut = deaths.Count - n;
        var a = MathF.Min(stack.NearEdge, stack.FarEdge);
        var b = MathF.Max(stack.NearEdge, stack.FarEdge);
        dl.PushClipRect(new Vector2(left, a), new Vector2(left + w, MathF.Max(a, b)), true);
        try
        {
            for (var i = 0; i < n; i++)
            {
                var y = stack.Row(i, rowH);
                if (z.IsCompact) DeathLine(dl, left, y, w, rowH, e, i);
                else DeathRow(dl, left, y, w, rowH, nameH, e, i);
            }
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
            if (z.IsCompact)
            {
                Txt.Line(dl, new Vector2(left + (w - Txt.Width(t1, Face.Sans, 12)) / 2, by), t1, Face.Sans, 12, Palette.Muted, 22);
                return;
            }
            var c = new Vector2(left + w / 2, by + S(14));
            Txt.Draw(dl, new Vector2(c.X - Txt.Width(t1, Face.Sans, 13) / 2, c.Y), t1, Face.Sans, 13, Palette.Muted);
            var t2 = deaths.Count > 0 ? "A death's recap opens from its row" : "No deaths yet";
            Txt.Draw(dl, new Vector2(c.X - Txt.Width(t2, Face.Sans, 12) / 2, c.Y + S(19.5f) + S(7.2f)), t2, Face.Sans, 12, Palette.Faint);
            return;
        }
        if (deaths.Count == 0)
        {
            var t = said.Wipe > 0 ? "No deaths but the wipe" : "Nobody died";
            var px = z.IsCompact ? 12 : 13;
            var ty = stack.Up ? listEnd - S(14) - Txt.Natural(Face.Sans, px) : listEnd + S(14);
            Txt.Draw(dl, new Vector2(left + (w - Txt.Width(t, Face.Sans, px)) / 2, ty), t, Face.Sans, px, Palette.Muted);
        }
        var fh = S(z.Foot);
        Kit.Rule(dl, left, left + w, stack.Toward(footY, fh));
        Txt.Line(dl, new Vector2(left + S(z.Pad), footY), DeathFoot(deaths.Count, cut, said.Wipe > 0), Face.Mono, z.FootPx, Palette.Muted, z.Foot);
        if (previewInFoot) PreviewTag(dl, new Vector2(left + w - S(z.Pad) - Kit.TagWidth("Preview", TagKind.Plain), footY + (fh - S(18)) / 2));
        else Txt.LineRight(dl, left + w - S(z.Pad), footY, said.PullWord, Face.Mono, z.FootPx, Palette.Muted, z.Foot);
    }

    /// <summary>A death (`.mdr`): the spot and name, the job in mono, its time in red on the right, what killed them and how big under it.</summary>
    private void DeathRow(ImDrawListPtr dl, float left, float y, float w, float h, float nameH, MeterEncounter e, int i)
    {
        var d = said.Deaths[i];
        var min = new Vector2(left, y);
        var max = new Vector2(left + w, y + h);
        var (clicked, hovered, _) = Kit.Hit(said.DeathIds[i], min, max - min);
        if (hovered) dl.AddRectFilled(min, max, Palette.U32(Palette.Hover));
        Kit.Reticle(dl, said.DeathRets[i], min + new Vector2(S(6), S(1)), max - new Vector2(S(6), S(1)), hovered);
        if (clicked) OpenDetail?.Invoke(MeterTab.Deaths, e.Id, d.PartyId, IndexIn(e.Deaths, d));
        var x = left + S(12);
        var row = RowOf(e, d.PartyId);
        Kit.Spot(dl, new Vector2(x + S(9), y + S(6) + nameH / 2), 18, d.Slot, Palette.RoleColor(row?.Role), 1.5f);
        var nx = x + S(18) + S(8);
        var job = row?.Job ?? string.Empty;
        var name = config.MeterJobsOnly ? job : d.Name;
        var nw = Txt.Line(dl, new Vector2(nx, y + S(6)), Txt.Fit(name, Face.SansMedium, 13, w - S(100)), Face.SansMedium, 13, Palette.White);
        if (!config.MeterJobsOnly) Txt.Spaced(dl, new Vector2(nx + nw + S(5), y + S(6) + (nameH - Txt.Natural(Face.Mono, 10)) / 2), Caps(job), Face.Mono, 10, Palette.Faint, 0.06f);
        Txt.LineRight(dl, left + w - S(12), y + S(6), said.DeathTimes[i], Face.MonoSemibold, 12, Palette.Danger, nameH / S(1));
        Txt.Draw(dl, new Vector2(nx, y + S(6) + nameH + S(2)), Txt.Fit(said.DeathLines[i], Face.Sans, 11, left + w - S(12) - nx), Face.Sans, 11, Palette.Muted);
    }

    /// <summary>Compact's death, one 20px line: the spot, the name, the job, what killed them and how big (muted, cut to fit), its time in red.</summary>
    private void DeathLine(ImDrawListPtr dl, float left, float y, float w, float h, MeterEncounter e, int i)
    {
        var z = Z;
        var d = said.Deaths[i];
        var min = new Vector2(left, y);
        var max = new Vector2(left + w, y + h);
        var (clicked, hovered, _) = Kit.Hit(said.DeathIds[i], min, max - min);
        if (hovered) dl.AddRectFilled(min, max, Palette.U32(Palette.Hover));
        if (clicked) OpenDetail?.Invoke(MeterTab.Deaths, e.Id, d.PartyId, IndexIn(e.Deaths, d));
        var row = RowOf(e, d.PartyId);
        var x = left + S(z.Pad);
        Kit.Spot(dl, new Vector2(x + S(z.Spot) / 2, y + h / 2), z.Spot, d.Slot, Palette.RoleColor(row?.Role), 1.5f);
        x += S(z.Spot) + S(z.Gap);
        var right = left + w - S(z.Pad);
        var tw = Txt.LineRight(dl, right, y, said.DeathTimes[i], Face.MonoSemibold, 11, Palette.Danger, z.DeathRow);
        var room = right - tw - S(8);
        var job = row?.Job ?? string.Empty;
        var name = config.MeterJobsOnly ? job : d.Name;
        x += Txt.Line(dl, new Vector2(x, y), Txt.Fit(name, Face.SansMedium, z.NamePx, MathF.Max(S(20), (room - x) * 0.55f)), Face.SansMedium, z.NamePx, Palette.White, z.DeathRow);
        if (!config.MeterJobsOnly && x + S(30) < room)
        {
            x += S(5);
            x += Txt.Spaced(dl, new Vector2(x, y + (h - Txt.Natural(Face.Mono, z.JobPx)) / 2), Caps(job), Face.Mono, z.JobPx, Palette.Faint, 0.06f);
        }
        x += S(8);
        if (room - x > S(24)) Txt.Line(dl, new Vector2(x, y), Txt.Fit(said.DeathLines[i], Face.Sans, 11, room - x), Face.Sans, 11, Palette.Muted, z.DeathRow);
    }

    /// <summary>
    /// The Events tab (0.4.0): the fight's events as a feed, the newest nearest the head: the time in
    /// mono, the kind's mark in its colour (a death red, a raise or an interrupt green, the limit break
    /// and potions ember), the member's spot and what happened, cut to fit. Pressed, the meter opens up
    /// on Events (a death, on its recap). Past the room the oldest are cut; the foot counts them by kind.
    /// </summary>
    private void DrawEvents(ImDrawListPtr dl, float left, float w, MeterStack stack, MeterEncounter e, IMeterSource m)
    {
        var z = Z;
        var f = Feed(m.Events(e.Id));
        var fh = S(z.Foot);
        var footY = stack.Far(fh);
        var rowH = S(z.IsCompact ? 20 : 23);
        var n = Math.Min(f.Newest.Count, stack.Fits(rowH));
        var a = MathF.Min(stack.NearEdge, stack.FarEdge);
        var b = MathF.Max(stack.NearEdge, stack.FarEdge);
        dl.PushClipRect(new Vector2(left, a), new Vector2(left + w, MathF.Max(a, b)), true);
        try
        {
            for (var i = 0; i < n; i++) EventRow(dl, f.Newest[i], f.Times[i], left, stack.Row(i, rowH), w, rowH, e, i);
            if (f.Newest.Count == 0)
            {
                const string none = "Deaths, raises, interrupts, potions and limit breaks show here.";
                var px = z.IsCompact ? 11 : 12;
                var ty = stack.Up ? stack.NearEdge - S(14) - Txt.Natural(Face.Sans, px) : stack.NearEdge + S(14);
                var t = Txt.Fit(none, Face.Sans, px, w - S(24));
                Txt.Draw(dl, new Vector2(left + (w - Txt.Width(t, Face.Sans, px)) / 2, ty), t, Face.Sans, px, Palette.Muted);
            }
        }
        finally
        {
            dl.PopClipRect();
        }
        Kit.Rule(dl, left, left + w, stack.Toward(footY, fh));
        var lw = Txt.Line(dl, new Vector2(left + S(z.Pad), footY), Txt.Fit(f.Foot, Face.Mono, z.FootPx, w * 0.62f), Face.Mono, z.FootPx, Palette.Muted, z.Foot);
        var rx = left + w - S(z.Pad);
        if (previewInFoot) PreviewTag(dl, new Vector2(rx - Kit.TagWidth("Preview", TagKind.Plain), footY + (fh - S(18)) / 2));
        else
        {
            var rw = Txt.LineRight(dl, rx, footY, DateTime.UtcNow - copiedAt < CopiedFor ? "copied" : said.PullWord, Face.Mono, z.FootPx, Palette.Muted, z.Foot);
            FootGauge(dl, left + S(z.Pad) + lw + S(12), rx - rw - S(10), footY, fh, e.Live);
        }
    }

    /// <summary>An event's row: its time, its mark, the member's spot, what happened. Pressed, the meter opens up on it.</summary>
    private void EventRow(ImDrawListPtr dl, MeterEvent ev, string time, float left, float y, float w, float h, MeterEncounter e, int i)
    {
        var z = Z;
        var min = new Vector2(left, y);
        var max = new Vector2(left + w, y + h);
        var (clicked, hovered, _) = Kit.Hit(EventIds[i % EventIds.Length], min, max - min);
        if (hovered)
        {
            dl.AddRectFilled(min, max, Palette.U32(Palette.Hover));
            Kit.Tooltip($"{MeterText.KindWord(ev.Kind)} at {time}: {ev.Text}");
        }
        if (clicked)
        {
            var death = ev.Kind == MeterEventKind.Death ? DeathAt(e, ev) : -1;
            if (death >= 0) OpenDetail?.Invoke(MeterTab.Deaths, e.Id, ev.PartyId, death);
            else OpenDetail?.Invoke(MeterTab.Events, e.Id, ev.PartyId > 0 ? ev.PartyId : null, null);
        }
        var lineCss = h / S(1);
        var x = left + S(z.Pad);
        var tpx = z.IsCompact ? 10 : 11;
        Txt.LineRight(dl, x + S(30), y, time, Face.Mono, tpx, Palette.Faint, lineCss);
        x += S(30) + S(8);
        var mark = S(z.IsCompact ? 12 : 13);
        Icons.Draw(dl, EventIcon(ev.Kind), new Vector2(x, y + (h - mark) / 2), mark, ToneColor(MeterText.Tone(ev.Kind)));
        x += mark + S(7);
        if (ev.Slot.Length > 0)
        {
            var spot = z.Spot - 2;
            Kit.Spot(dl, new Vector2(x + S(spot) / 2, y + h / 2), spot, ev.Slot, Palette.SpotColor(ev.Slot), 1.5f);
            x += S(spot) + S(6);
        }
        var px = z.IsCompact ? 11 : 12;
        var room = left + w - S(z.Pad) - x;
        if (room > S(16)) Txt.Line(dl, new Vector2(x, y), Txt.Fit(ev.Text, Face.Sans, px, room), Face.Sans, px, ev.Kind == MeterEventKind.Death ? Palette.DangerName : Palette.Text, lineCss);
    }

    private static readonly string[] EventIds = MakeIds("##mev", 64);

    private static string[] MakeIds(string prefix, int n)
    {
        var ids = new string[n];
        for (var i = 0; i < n; i++) ids[i] = prefix + i.ToString(CultureInfo.InvariantCulture);
        return ids;
    }

    /// <summary>An event's mark (Lucide): a skull, a heart, a stop, a warning, a bolt, a shield, a flask, sparkles, two arrows.</summary>
    public static Icon EventIcon(MeterEventKind kind) => kind switch
    {
        MeterEventKind.Death => Icon.Skull,
        MeterEventKind.Raise => Icon.Heart,
        MeterEventKind.Interrupt => Icon.CircleX,
        MeterEventKind.CastWentOff => Icon.AlertTriangle,
        MeterEventKind.Stun => Icon.Zap,
        MeterEventKind.Resisted => Icon.Shield,
        MeterEventKind.Potion => Icon.Flask,
        MeterEventKind.LimitBreak => Icon.Sparkles,
        MeterEventKind.TankSwap => Icon.ArrowLeftRight,
        MeterEventKind.AggroLost => Icon.Alert,
        _ => Icon.Info,
    };

    /// <summary>An event's mark's colour: red, green, ember or muted.</summary>
    public static Vector4 ToneColor(EventTone tone) => tone switch
    {
        EventTone.Bad => Palette.Danger,
        EventTone.Good => Palette.Ready,
        EventTone.Ember => Palette.Ember,
        _ => Palette.Muted,
    };

    /// <summary>The death an event is about (its index in the fight's list), or −1.</summary>
    private static int DeathAt(MeterEncounter e, MeterEvent ev)
    {
        for (var i = 0; i < e.Deaths.Count; i++)
            if (e.Deaths[i].PartyId == ev.PartyId && Math.Abs(e.Deaths[i].At - ev.At) < 1)
                return i;
        return -1;
    }

    /// <summary>The feed for a list of events: made again only when the source gives a new list (or it grew).</summary>
    private (List<MeterEvent> Newest, List<string> Times, string Foot) Feed(IReadOnlyList<MeterEvent> events)
    {
        var compact = Z.IsCompact;
        if (!ReferenceEquals(feed.Of, events) || feed.Count != events.Count || feed.Compact != compact)
        {
            var newest = MeterText.Newest(events, EventIds.Length);
            var times = new List<string>(newest.Count);
            foreach (var ev in newest) times.Add(Format.Clock(ev.At));
            var foot = events.Count == 0 ? "No events yet" : MeterText.Counts(events, compact ? 2 : 3);
            feed = (events, events.Count, compact, newest, times, foot);
        }
        return (feed.Newest, feed.Times, feed.Foot);
    }

    /// <summary>The Events tab's count in compact's menu (the shown fight's), or null.</summary>
    private string? EventCount()
    {
        if (shown == null || Meter is not { } m) return null;
        var n = m.Events(shown.Id).Count;
        return n > 0 ? n.ToString(CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Copy as text: the fight shown, as a mini-parse (<see cref="MeterText.Parse"/>), to the clipboard; the foot says so for a moment.</summary>
    private void CopyParse(MeterEncounter e)
    {
        ImGui.SetClipboardText(MeterText.Parse(e, config.MeterJobsOnly));
        copiedAt = DateTime.UtcNow;
    }

    private string StoppedLine(string problem)
    {
        if (!ReferenceEquals(stoppedWords.Of, problem)) stoppedWords = (problem, MeterView.StoppedLine(problem));
        return stoppedWords.Text;
    }

    /// <summary>"7 alive", "7 alive · 3 more in the big window".</summary>
    private string AliveLine(int alive, int cut)
    {
        if (aliveWords.Alive != alive || aliveWords.Cut != cut)
        {
            var a = alive.ToString(CultureInfo.InvariantCulture) + " alive";
            aliveWords = (alive, cut, cut > 0 ? a + " · " + cut.ToString(CultureInfo.InvariantCulture) + " more in the big window" : a);
        }
        return aliveWords.Text;
    }

    /// <summary>"2 deaths · 3 not shown · the wipe".</summary>
    private string DeathFoot(int count, int cut, bool wipe)
    {
        if (deathFoot.Count != count || deathFoot.Cut != cut || deathFoot.Wipe != wipe)
        {
            var t = count == 1 ? "1 death" : count.ToString(CultureInfo.InvariantCulture) + " deaths";
            if (cut > 0) t += " · " + cut.ToString(CultureInfo.InvariantCulture) + " not shown";
            if (wipe) t += " · the wipe";
            deathFoot = (count, cut, wipe, t);
        }
        return deathFoot.Text;
    }

    /// <summary>A party member's row in a fight (for a death's spot colour and job), or null.</summary>
    private static MeterRow? RowOf(MeterEncounter e, int partyId)
    {
        var rows = e.Rows;
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].PartyId == partyId && !rows[i].IsLimitBreak)
                return rows[i];
        return null;
    }

    /// <summary>Where a death is in the fight's own list (the opened-up window picks it by that).</summary>
    private static int IndexIn(IReadOnlyList<DeathEntry> all, DeathEntry d)
    {
        for (var i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], d))
                return i;
        return -1;
    }

    /// <summary>Waiting for the first hit: the clock at 0:00 with a hollow dot, the party listed (in spot order) with nothing yet.</summary>
    private void DrawWaiting(ImDrawListPtr dl, Vector2 top, float w, float bottom, MeterEncounter? last, IMeterSource m)
    {
        var z = Z;
        var stack = new MeterStack(top.Y, bottom, Up);
        Header(dl, top.X, ref stack, w, "0:00", false, null, TagKind.Wipe, null, m, "Waiting for the first hit", false);
        if (!z.IsCompact) TabsRow(dl, top.X, ref stack, w);
        Columns(dl, top.X, ref stack, w, MeterTab.Damage);
        var fy = stack.Far(S(z.Foot));
        var g = Grid(top.X, w, z);
        var rowH = S(z.Row);
        var rows = last != null ? said.Spots : null;
        var n = rows == null ? 0 : Math.Min(rows.Count, stack.Fits(rowH));
        for (var i = 0; i < n; i++)
        {
            var r = rows![i];
            var y = stack.Row(i, rowH);
            Kit.Spot(dl, new Vector2(g.Spot + S(z.Spot) / 2, y + rowH / 2), z.Spot, r.Slot, Palette.RoleColor(r.Role), 1.5f);
            Txt.Line(dl, new Vector2(g.Name, y), Txt.Fit(config.MeterJobsOnly ? r.Job : r.Name, Face.SansMedium, z.NamePx, g.Job - S(z.Gap) - g.Name), Face.SansMedium, z.NamePx,
                Palette.White, z.Row);
            if (!config.MeterJobsOnly) Txt.Spaced(dl, new Vector2(g.Job, y + (rowH - Txt.Natural(Face.Mono, z.JobPx)) / 2), Caps(r.Job), Face.Mono, z.JobPx, Palette.Faint, 0.06f);
            Txt.LineRight(dl, g.ValueRight, y, "–", Face.MonoSemibold, z.ValuePx, Palette.White, z.Row);
            if (!z.IsCompact) Bar(dl, top.X, w, y + rowH, 0, false, false);
        }
        Kit.Rule(dl, top.X, top.X + w, stack.Toward(fy, S(z.Foot)));
        if (last == null) return;
        Txt.Line(dl, new Vector2(top.X + S(z.Pad), fy), Txt.Fit(last.Duty, Face.Mono, z.FootPx, w * 0.6f), Face.Mono, z.FootPx, Palette.Muted, z.Foot);
        Txt.LineRight(dl, top.X + w - S(z.Pad), fy, said.NextPull, Face.Mono, z.FootPx, Palette.Muted, z.Foot);
    }

    private static int SpotOrder(string slot) => Array.IndexOf(SpotOrderOf, slot) is var i and >= 0 ? i : 99;

    /// <summary>
    /// Before any fight, the Boss asleep, "No fight yet", one line and a quiet link to a sample fight;
    /// stopped (<see cref="IMeterSource.Problem"/>), the Boss awake, "The meter has stopped" and why.
    /// The head and the tabs on its anchored side, the Boss and its words beyond them. Returns how tall
    /// it is (the window is that tall).
    /// </summary>
    private float DrawEmpty(ImDrawListPtr dl, Vector2 top, float w, IMeterSource? m)
    {
        var z = Z;
        var stopped = view == View.Stopped && m?.Problem != null;
        // Stopped, a kept fight can still be picked to look at.
        var pick = stopped && shown != null && m!.Kept.Count > 0 ? said.Pick : null;
        var line = stopped ? StoppedLine(m!.Problem!)
            : m == null ? "The meter starts with the plugin's reader."
            : "It starts on the first hit.";
        var title = stopped ? "The meter has stopped" : "No fight yet";
        var sample = !stopped && Preview == null && StartSample != null;
        OneRun[0] = Run.Note(line);
        var calloutH = Kit.CalloutHeight(title, 13, w, 1, 1.1f);
        var lineH = Txt.Flow(Vector2.Zero, w - S(28), OneRun, 19.5f, out _, center: true, draw: false);
        var linkH = sample ? S(6) + Txt.Natural(Face.SansMedium, 12) + S(4) : 0;
        var messageH = S(16) + S(40) + S(7.2f) + calloutH + S(7.2f) + lineH + linkH + S(18);
        var total = S(z.Head) + S(z.Tabs) + messageH;
        var stack = new MeterStack(top.Y, top.Y + total, Up);
        Header(dl, top.X, ref stack, w, "0:00", false, null, TagKind.Wipe, pick, m, null, pick != null);
        if (!z.IsCompact) TabsRow(dl, top.X, ref stack, w);
        // The message fills what's left, whichever side the head is on.
        var y = stack.Up ? stack.FarEdge : stack.NearEdge;
        var c = new Vector2(top.X + w / 2, y + S(16) + S(20));
        Boss.Empty(dl, c, 40, stopped ? BossMood.Awake : BossMood.Asleep);
        var ty = c.Y + S(20) + S(7.2f);
        ty += Kit.Callout(dl, new Vector2(top.X, ty), title, 13, w, 1, 1.1f, center: true);
        ty += S(7.2f);
        ty += Txt.Flow(new Vector2(top.X + S(14), ty), w - S(28), OneRun, 19.5f, out _, center: true);
        if (sample)
        {
            // "See a sample": the meter mid-fight, with made-up numbers.
            const string see = "See a sample";
            var lw = Kit.LinkWidth(see, null, 12);
            if (Kit.Link("##meter-sample", new Vector2(top.X + (w - lw) / 2, ty + S(6)), see, null, 12, "A made-up pull, to see the meter mid-fight. A real fight ends it."))
                StartSample?.Invoke();
        }
        return total;
    }

    /// <summary>Folded between pulls: one line, the last pull's length under its tag, "Pull 12 · party 163,188", and a press opens it.</summary>
    private void Folded(ImDrawListPtr dl, Vector2 top, float w, MeterEncounter e)
    {
        var z = Z;
        var h = S(z.Head);
        var x = top.X + S(12);
        var cy = top.Y + h / 2;
        Kit.BeatDot(dl, new Vector2(x + S(3), cy), Palette.Faint, false);
        x += S(13);
        x += Txt.Line(dl, new Vector2(x, top.Y), said.Clock, Face.MonoSemibold, z.ClockPx, Palette.White, z.Head) + S(8);
        if (MeterView.OutcomeWord(e.Ended) is { } t) x += Kit.Tag(dl, new Vector2(x, cy - S(9)), t, e.Ended == Core.Outcome.Clear ? TagKind.Clear : TagKind.Wipe, 18) + S(8);
        var right = top.X + w - S(6);
        if (SmallButton("##meter-open", new Vector2(right - S(24), cy - S(12)), Icon.ChevronDown, false, "Open it")) unfolded = true;
        var (value, _) = LinesOf(e, MeterTab.Damage, config.MeterRank).Party;
        var px = z.IsCompact ? 12 : 13;
        var vw = Txt.Width(value, Face.MonoSemibold, px);
        var rx = right - S(24) - S(8);
        Txt.Line(dl, new Vector2(rx - vw, top.Y), value, Face.MonoSemibold, px, Palette.White, z.Head);
        var lw = Txt.Width(said.FoldLabel, Face.Sans, px);
        if (rx - vw - lw > x) Txt.Line(dl, new Vector2(rx - vw - lw, top.Y), said.FoldLabel, Face.Sans, px, Palette.Muted, z.Head);
    }

    // ---------------------------------------------------------------------------------------------
    // The menus: the picker, compact's tabs.

    /// <summary>
    /// Before a menu that opens from the head: where it goes. Off the head by 6px, away from the edge
    /// the meter is anchored at (down from a head on top, up from one at the foot; the other way when
    /// there isn't room that way), its left edge by its button's but inside the meter's sides and the screen.
    /// </summary>
    private void PlaceMenu(float buttonLeft, float menuW, float menuH)
    {
        var win = ImGui.GetWindowPos();
        var vp = ImGui.GetMainViewport();
        var screenTop = vp.WorkPos.Y;
        var screenBottom = vp.WorkPos.Y + vp.WorkSize.Y;
        var up = MeterLayout.MenuUp(Up, headTop - screenTop, screenBottom - headBottom, menuH + S(MenuGap));
        var x = MeterLayout.MenuLeft(buttonLeft, win.X, win.X + ImGui.GetWindowWidth(), menuW, S(MenuInset), vp.WorkPos.X, vp.WorkPos.X + vp.WorkSize.X);
        ImGui.SetNextWindowPos(new Vector2(x, up ? headTop - S(MenuGap) : headBottom + S(MenuGap)), ImGuiCond.Always, new Vector2(0, up ? 1 : 0));
    }

    /// <summary>
    /// Compact's tab menu (`.menu`, 200 wide): Damage, Healing, Taken, Deaths, each with the opened-up
    /// window's icon, the one you're on ticked, the deaths counted.
    /// </summary>
    private void DrawTabMenu(float buttonLeft, bool hasFight)
    {
        var mw = S(TabMenuWidth);
        ImGui.SetNextWindowSizeConstraints(new Vector2(mw, 0), new Vector2(mw, S(400)));
        PlaceMenu(buttonLeft, mw, tabMenuH > 0 ? tabMenuH : S(MeterLayout.TabOrder.Length * 34 + 11.2f));
        if (!Kit.BeginMenu(TabMenu)) return;
        try
        {
            var w = mw - S(11.2f);
            var dl = ImGui.GetWindowDrawList();
            foreach (var t in MeterLayout.TabOrder)
            {
                var on = t == config.MeterTab;
                var count = !hasFight ? null : t == MeterTab.Deaths ? said.DeathCount : t == MeterTab.Events ? EventCount() : null;
                var reserve = on ? S(16) + S(8) : count != null ? Txt.Width(count, Face.Mono, 11) + S(8) : 0;
                var (clicked, _, min, max) = Kit.MenuRow(TabIds[(int)t], w, MeterLayout.TabWord(t), null, true, 16, reserve);
                var (slot, right) = Kit.MenuRowParts(min, max, true, 16);
                Icons.Draw(dl, TabIcons[(int)t], slot + new Vector2((S(16) - S(15)) / 2), S(15), on ? Palette.White : Palette.Muted);
                var cy = (min.Y + max.Y) / 2;
                if (on) Kit.Tick(dl, new Vector2(right - S(8), cy), 16);
                else if (count != null) Txt.LineRight(dl, right, min.Y, count, Face.Mono, 11, Palette.Faint, (max.Y - min.Y) / S(1));
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
    /// The picker (`.menu`, 300 wide): tonight's fights the meter kept, newest first, the one shown
    /// ticked, the live one tagged Now, the best starred; the other duties' after; and at its foot Pace
    /// against (Best, Last, Off) and Compact. With a sample on, Stop the sample first.
    /// </summary>
    private void DrawPicker(IMeterSource m, float buttonLeft)
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(S(260), 0), new Vector2(S(PickerWidth), S(460)));
        PlaceMenu(buttonLeft, S(PickerWidth), pickerH > 0 ? pickerH : S(300));
        if (!Kit.BeginMenu(Picker)) return;
        try
        {
            var w = S(PickerWidth) - S(11.2f);
            var dl = ImGui.GetWindowDrawList();
            if (Preview != null)
            {
                var (stop, _, min, max) = Kit.MenuRow("##pick-stop", w, "Stop the sample", "Its numbers are made up", true, 16);
                var (slot, _) = Kit.MenuRowParts(min, max, true, 16);
                Icons.Draw(dl, Icon.X, slot + new Vector2((S(16) - S(15)) / 2), S(15), Palette.Muted);
                if (stop)
                {
                    Preview = null;
                    ImGui.CloseCurrentPopup();
                }
                Kit.MenuRule(w);
            }
            var current = picked is { } id ? m.Get(id) : m.Current;
            var (here, others) = MeterView.Picker(m.Kept, current, current != null ? m.Best(current.Content) : null);
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
            // Copy as text: a mini-parse of the fight shown, for chat (as ACT's).
            if (current != null)
            {
                var (copy, _, cmin, cmax) = Kit.MenuRow("##pick-copy", w, "Copy as text", $"Pull {current.Pull}: each player's ENCDPS, share and deaths", true, 16);
                var (cslot, _) = Kit.MenuRowParts(cmin, cmax, true, 16);
                Icons.Draw(dl, Icon.Copy, cslot + new Vector2((S(16) - S(15)) / 2), S(15), Palette.Muted);
                if (copy)
                {
                    CopyParse(current);
                    ImGui.CloseCurrentPopup();
                }
                Kit.MenuRule(w);
            }
            var pos = ImGui.GetCursorScreenPos();
            var lineH = S(28);
            var gap = S(4);
            var h = S(5.6f) + lineH + gap + lineH + S(7.2f);
            var y = pos.Y + S(5.6f);
            Txt.Line(dl, new Vector2(pos.X + S(8.8f), y), "Pace against", Face.Sans, 13, Palette.Muted, 28);
            var sw = Kit.SegmentedWidth(PaceItems);
            var pace = Kit.Segmented("##pick-pace", new Vector2(pos.X + w - S(8.8f) - sw, y), PaceItems, (int)config.MeterPace, true, false, lineH);
            if (pace != (int)config.MeterPace)
            {
                config.MeterPace = (PaceAgainst)pace;
                config.Save();
            }
            // Compact, as Settings has it: smaller rows and the tabs in a menu.
            y += lineH + gap;
            Txt.Line(dl, new Vector2(pos.X + S(8.8f), y), "Compact", Face.Sans, 13, Palette.Muted, 28);
            var size = Kit.SwitchSize;
            if (Kit.Switch("##pick-compact", new Vector2(pos.X + w - S(8.8f) - size.X, y + (lineH - size.Y) / 2), config.MeterCompact))
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

    // ---------------------------------------------------------------------------------------------
    // The shown fight's words.

    /// <summary>Makes the shown fight's words again when it's a new snapshot.</summary>
    private void Words(MeterEncounter e)
    {
        if (ReferenceEquals(said.Of, e)) return;
        said.Fill(e);
    }

    /// <summary>The shown fight's words and lists, made once a snapshot (a few times a second in a fight), not each frame.</summary>
    private sealed class Said
    {
        public MeterEncounter? Of;
        public string Clock = string.Empty;
        public string Pick = string.Empty;
        public string PullWord = string.Empty;
        public string KeptAs = string.Empty;
        public string NextPull = string.Empty;
        public string FoldLabel = string.Empty;
        public string? Where;
        public string? DeathCount;
        public int Alive;
        public int Wipe;
        public readonly List<DeathEntry> Deaths = new();
        public readonly List<string> DeathTimes = new();
        public readonly List<string> DeathLines = new();
        public readonly List<string> DeathIds = new();
        public readonly List<string> DeathRets = new();
        public readonly List<MeterRow> Spots = new();

        public void Fill(MeterEncounter e)
        {
            Of = e;
            var pull = e.Pull.ToString(CultureInfo.InvariantCulture);
            Clock = Format.Clock(e.Duration);
            Pick = "Pull " + pull;
            PullWord = "pull " + pull;
            KeptAs = "kept as pull " + pull;
            NextPull = "pull " + (e.Pull + 1).ToString(CultureInfo.InvariantCulture);
            FoldLabel = "Pull " + pull + " · party ";
            Where = MeterView.Where(e);
            Deaths.Clear();
            Deaths.AddRange(MeterView.Deaths(e));
            DeathTimes.Clear();
            DeathLines.Clear();
            DeathIds.Clear();
            DeathRets.Clear();
            for (var i = 0; i < Deaths.Count; i++)
            {
                var d = Deaths[i];
                var id = d.PartyId.ToString(CultureInfo.InvariantCulture) + "-" + i.ToString(CultureInfo.InvariantCulture);
                DeathTimes.Add(Format.Clock(d.At));
                DeathLines.Add(MeterText.DeathLine(d));
                DeathIds.Add("##md" + id);
                DeathRets.Add("##md-ret" + id);
            }
            DeathCount = Deaths.Count > 0 ? Deaths.Count.ToString(CultureInfo.InvariantCulture) : null;
            Alive = 0;
            Spots.Clear();
            foreach (var r in e.Rows)
            {
                if (r.IsLimitBreak) continue;
                if (!r.Dead) Alive++;
                Spots.Add(r);
            }
            // In spot order, MT to R2 (kept in the order they came where the spot isn't one of the eight's).
            for (var i = 1; i < Spots.Count; i++)
            {
                var r = Spots[i];
                var k = SpotOrder(r.Slot);
                var j = i - 1;
                for (; j >= 0 && SpotOrder(Spots[j].Slot) > k; j--) Spots[j + 1] = Spots[j];
                Spots[j + 1] = r;
            }
            Wipe = 0;
            foreach (var d in e.Deaths)
                if (d.PartOfWipe)
                    Wipe++;
        }
    }
}
