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
/// The meter over the fight (the mock-ups' "The meter"): small and calm, 330 wide, in the theme picked
/// (since 0.7.3 the normal meter too: MeterWindow.Normal.cs). The fight's clock with the dot that beats
/// while a fight is on, which pull it shows (the picker), the lock; five tabs with the accent under the
/// one you're on; pace on the Damage tab; a row a player on the theme's plate (the job's icon, the name,
/// the job, the number and its share, the bar in the theme's style and the role's colour), the limit
/// break on its own row, the party at the foot. Compact (since 0.7.0 the default: <see cref="PlateMeter"/>,
/// in MeterWindow.Plate.cs), the top eight on plates, the tabs and the picker in one menu in its head.
/// Locked, it has no title bar and can't be moved or sized; click-through, the mouse goes to the game.
/// Only the normal meter, unlocked, can be sized by hand (<see cref="MeterLayout.Resizable"/>): its
/// head's buttons are kept out of the reach of its edges and corners, and while the mouse is over one
/// it isn't resizable that frame (<see cref="ResizeGuard"/>), so a press there is the button's.
/// In a fight it's as tall as the party needs (at most the height you gave it); out of combat it folds
/// to one line (as it shrinks to the Boss before any fight, or when the meter has stopped). It stays
/// put at its anchor as it does: its top edge growing down, its foot growing up (its head at the foot
/// then, the rows reading up from it). Its numbers come from <see cref="IMeterSource"/> only.
/// </summary>
public sealed partial class MeterWindow : Window
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

    private static readonly string[] TabIds = { "##mtab0", "##mtab1", "##mtab2", "##mtab3", "##mtab4" };

    private static readonly Icon[] TabIcons = { Icon.Sword, Icon.Heart, Icon.Shield, Icon.Skull, Icon.List };

    /// <summary>How long the foot says the parse was copied.</summary>
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(2.5);

    private static readonly string[] PaceItems = { "Best", "Last", "Off" };

    private static readonly string[] SpotOrderOf = { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" };

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

    /// <summary>A height you gave the normal meter by its edge (CSS px; 0 none), kept once you let go.</summary>
    private float givenNow;

    /// <summary>The head's and foot's buttons kept clear of the resizing edges (the normal meter's).</summary>
    private readonly ResizeGuard guard = new();

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
    private (IReadOnlyList<MeterEvent>? Of, int Count, List<MeterEvent> Newest, List<string> Times, string Foot) feed =
        (null, -1, new List<MeterEvent>(), new List<string>(), string.Empty);

    /// <summary>The limit break gauge's bars, worked out again only when the gauge changes.</summary>
    private (LimitBreakGauge? Of, float[] Bars) gaugeBars = (null, Array.Empty<float>());

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

    /// <summary>A sample fight to look at the meter with (Settings → Meter, the empty meter's link, `/raidcodex meter preview`).</summary>
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

    /// <summary>The normal meter's sizes in its theme (<see cref="MeterSizes.For"/>; compact is <see cref="PlateMeter"/>'s own since 0.7.0).</summary>
    private MeterSizes Z => NormalLook().Z;

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
            Failed(ex, "Raid Codex's meter couldn't read its numbers");
        }
        try
        {
            Arrange();
        }
        catch (Exception ex)
        {
            Failed(ex, "Raid Codex's meter couldn't place itself");
        }
        // The background is see-through as set; the rows stay solid. Both meters draw their theme's own ground and frame (since 0.7.3 the normal one too).
        Theme.PushFrame(Vector4.Zero, PlateTheme.Win.Radius, Vector4.Zero);
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
            // Before a font is pushed: the resize grip is sized by the font at Begin.
            guard.Frame();
            Track();
            using (fonts.Push(Face.Sans, 13))
            {
                try
                {
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
            Failed(ex, "Raid Codex's meter couldn't draw");
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
        if (config.MeterCompact) PlateDecide(m);
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
        // Compact is never resized by hand; the normal meter isn't while the mouse is over its buttons (a press there is theirs).
        if (!MeterLayout.Resizable(config.MeterCompact, config.MeterLocked) || guard.Over()) Flags |= ImGuiWindowFlags.NoResize;
        var scale = ImGuiHelpers.GlobalScale;
        var chrome = config.MeterLocked ? 0 : titleH > 0 ? titleH : ImGui.GetFrameHeight();
        if (config.MeterCompact)
        {
            ArrangePlate(scale, chrome);
            return;
        }
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
            var given = config.MeterHeight;
            var needed = Z.Needed(PartyRows(), config.MeterPace != PaceAgainst.Off);
            var h = MeterLayout.FightHeight(needed, chrome / scale, given, Sizes.MinimumSize.Y, Sizes.MaximumSize.Y);
            Size = new Vector2(widthCss, h);
            SizeCondition = ImGuiCond.Always;
            height = h * scale;
            fightH = height;
        }
        else SizeCondition = ImGuiCond.FirstUseEver;
        PlaceAt(widthCss * scale, height);
    }

    /// <summary>Places the window from its anchor for this size (screen px), kept on the screen; not while held, nor before its height is known.</summary>
    private void PlaceAt(float width, float height)
    {
        if (!config.MeterAnchored || config.MeterAnchorFor != config.MeterGrows || held || float.IsNaN(height)) return;
        var vp = ImGui.GetMainViewport();
        var otherScreens = (ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0;
        var at = MeterLayout.Place(new Vector2(config.MeterAnchorX, config.MeterAnchorY), new Vector2(width, height), config.MeterGrows, vp.WorkPos,
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
            // The normal meter sized by its edge in a fight: that's the height you're giving it (compact can't be sized).
            if (!config.MeterCompact && view is View.Fight or View.Waiting && !float.IsNaN(fightH) && MathF.Abs(size.Y - fightH) > 1.5f) givenNow = size.Y / ImGuiHelpers.GlobalScale;
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
            config.MeterHeight = MathF.Round(givenNow);
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
        if (config.MeterCompact)
        {
            widthKnown = true;
            DrawPlate(top, w, bottom, m);
            return;
        }
        widthCss = w / ImGuiHelpers.GlobalScale;
        widthKnown = true;
        previewInFoot = false;
        if (shown != null) Words(shown);
        // The theme's ground and frame under it all (its sparkles as Decorations has them), as compact's.
        Configure(plate, config);
        plate.Ground(dl, top, new Vector2(top.X + w, bottom), true);
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
            // Readout's scanlines over everything, last.
            plate.Scanlines(dl, top, new Vector2(top.X + w, bottom));
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
        TabsRow(dl, top.X, ref stack, w);
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
    // Words worked out once (the normal meter's parts are drawn in MeterWindow.Normal.cs, in its theme).

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

    /// <summary>The gauge's bars now (none outside a party that fills one), worked out again only when it changes.</summary>
    private float[]? Bars(IMeterSource? m)
    {
        var g = m?.LimitBreak;
        if (g == null) return null;
        if (!Equals(gaugeBars.Of, g)) gaugeBars = (g, MeterText.Bars(g));
        return gaugeBars.Bars;
    }


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
        if (!ReferenceEquals(feed.Of, events) || feed.Count != events.Count)
        {
            var newest = MeterText.Newest(events, EventIds.Length);
            var times = new List<string>(newest.Count);
            foreach (var ev in newest) times.Add(Format.Clock(ev.At));
            var foot = events.Count == 0 ? "No events yet" : MeterText.Counts(events, 3);
            feed = (events, events.Count, newest, times, foot);
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

    private static int SpotOrder(string slot) => Array.IndexOf(SpotOrderOf, slot) is var i and >= 0 ? i : 99;

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

    /// <summary>/raidcodex meter unlock: the mouse back, the window movable again.</summary>
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
