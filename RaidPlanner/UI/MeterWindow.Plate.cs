using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The meter compact (since 0.7.0 the default; round four's "Plate", <see cref="PlateMeter"/>): sized
/// for its rows (at most eight and the limit break) in its theme, its width its theme's, never by hand
/// (its window can't be resized: a size given it would only snap back), one line when folded or
/// collapsed, its one menu ("DPS ⌄": the tabs, the pulls, Copy as text, Pace against, Compact, One
/// line), the details panel beside it when a row is pointed at.
/// </summary>
public sealed partial class MeterWindow
{
    private const string PlateMenu = "##rp-meter-plate-menu";

    private const float PlateMenuWidth = 300;

    /// <summary>Compact can be narrower than the normal meter (its themes are 248 to 268 wide).</summary>
    private static readonly WindowSizeConstraints PlateSizesLimits = new() { MinimumSize = new Vector2(200, 20), MaximumSize = new Vector2(720, 1400) };

    private readonly PlateMeter plate = new();

    private float plateMenuH;

    /// <summary>The theme whose fonts were asked for last.</summary>
    private static MeterThemeId? fontsFor;

    private (string? Of, string Tip) paceTip = (null, string.Empty);

    /// <summary>The limit break's words for its pips, made again only when the gauge changes.</summary>
    private (LimitBreakGauge? Of, string Tip) gaugeTip = (null, string.Empty);

    /// <summary>The compact meter's theme.</summary>
    private MeterTheme PlateTheme => MeterThemes.Of(config.MeterTheme);

    /// <summary>The compact meter shows one line: collapsed by hand, folded between pulls, or nothing to show.</summary>
    private bool PlateOneLine => config.MeterCompact && (config.MeterCollapsed || view is View.Folded or View.Nothing or View.Stopped);

    /// <summary>A theme's faces loaded (and the last theme's let go) when the theme picked changes: the meter and Settings' preview ask.</summary>
    internal static void WantFonts(Fonts fonts, MeterThemeId id)
    {
        if (fontsFor == id) return;
        fontsFor = id;
        // The compact meter's faces, the opened-up window's and the normal meter's in the theme (DetailFonts).
        var t = MeterThemes.Of(id);
        fonts.Want(System.Linq.Enumerable.Concat(System.Linq.Enumerable.Concat(MeterThemes.FacesOf(t), DetailFonts.For(t).Faces()), DetailFonts.ForNormal(t).Faces()));
    }

    /// <summary>The compact meter's settings onto its drawer (each frame), and the shown fight's words (once a snapshot).</summary>
    private void PlateDecide(IMeterSource? m)
    {
        WantFonts(fonts, config.MeterTheme);
        Configure(plate, config);
        plate.Locked = Locked;
        plate.ClickThrough = config.MeterClickThrough;
        plate.PreviewOn = Preview != null;
        if (shown != null) plate.Update(shown, m, MeterLayout.Known(config.MeterTab), config.MeterRank, config.MeterWholeNumbers, Now());
    }

    /// <summary>A plate drawer set as the settings say (the meter's, and Settings' preview).</summary>
    internal static void Configure(PlateMeter p, Configuration config)
    {
        p.T = MeterThemes.Of(config.MeterTheme);
        p.Decorations = config.MeterDecorations;
        p.RoleSet = config.MeterRoleColours;
        p.Whole = config.MeterWholeNumbers;
        p.JobsOnly = config.MeterJobsOnly;
        p.Background = Math.Clamp(config.MeterBackground, 0.2f, 1f);
        // Where its head sits and the rows' order (since 0.7.4 apart from which way it grows; Settings' preview follows them too).
        p.Arrange = config.MeterArrangeNow();
    }

    private static double Now() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    /// <summary>
    /// Before ImGui.Begin, compact (never resizable: <see cref="MeterLayout.Resizable"/>): its width
    /// its theme's, its height one line or what its rows need (at most eight and the limit break), and
    /// its place from its anchor (not while you move it). Sizes given by hand before 0.7.0 are ignored.
    /// </summary>
    private void ArrangePlate(float scale, float chrome)
    {
        var t = PlateTheme;
        var width = t.Win.Width;
        SizeConstraints = PlateSizesLimits;
        float h;
        if (PlateOneLine)
        {
            holdH = 0;
            h = plate.LineHeight + chrome / scale;
        }
        else
        {
            var needed = plate.Needed(PartyRows(), view == View.Waiting);
            // Held while the mouse is on it (MeterLayout.Hold): a tab picked or a row come doesn't move its head under the mouse.
            h = HoldHeight(MeterLayout.FightHeight(needed, chrome / scale, 0, 20, Sizes.MaximumSize.Y));
        }
        Size = new Vector2(width, h);
        SizeCondition = ImGuiCond.Always;
        fightH = h * scale;
        PlaceAt(width * scale, h * scale);
    }

    /// <summary>The compact meter, inside the window: one line, or the fight; then what was pressed, the menu and the details panel.</summary>
    private void DrawPlate(Vector2 top, float w, float bottom, IMeterSource? m)
    {
        var dl = Kit.Dl;
        var t = PlateTheme;
        var e = shown;
        plate.FootNote = DateTime.UtcNow - copiedAt < CopiedFor ? "copied"
            : Locked && config.MeterClickThrough ? "click-through"
            : null;
        plate.Pips = config.MeterGauge != GaugePlace.Off ? Bars(m) : null;
        plate.PipsTip = null;
        if (m?.LimitBreak is { } g && plate.Pips != null)
        {
            if (!Equals(gaugeTip.Of, g)) gaugeTip = (g, MeterText.GaugeTip(g));
            plate.PipsTip = gaugeTip.Tip;
        }
        plate.Pace = null;
        plate.PaceTip = null;
        if (e != null && m != null && e.Live && config.MeterPace != PaceAgainst.Off && m.Pace(e.Id, config.MeterPace) is { } pace)
        {
            var (label, ahead, on) = PaceWords(pace);
            plate.Pace = ahead;
            plate.PaceAhead = pace.Ahead >= 0;
            if (!ReferenceEquals(paceTip.Of, ahead)) paceTip = (ahead, label + " · " + on + ahead);
            plate.PaceTip = paceTip.Tip;
        }
        var h = bottom - top.Y;
        headTop = HeadBottom ? bottom - S(t.Win.HeadH) : top.Y;
        headBottom = HeadBottom ? bottom : top.Y + S(t.Win.HeadH);
        PlateInput input;
        var oneLine = PlateOneLine;
        if (oneLine)
        {
            headTop = top.Y;
            headBottom = top.Y + S(plate.LineHeight);
            if (view is View.Nothing or View.Stopped || e == null)
            {
                var stopped = view == View.Stopped && m?.Problem != null;
                input = plate.DrawLine(dl, top, w, stopped ? "The meter has stopped" : m == null ? "The meter starts with the reader" : "No fight yet",
                    !stopped && Preview == null && StartSample != null, stopped ? StoppedLine(m!.Problem!) : null);
                if (input.Open) OpenSettings?.Invoke();
                if (input.Sample) StartSample?.Invoke();
            }
            else
            {
                input = plate.DrawLine(dl, top, w);
                if (input.Open)
                {
                    if (config.MeterCollapsed)
                    {
                        config.MeterCollapsed = false;
                        config.Save();
                    }
                    else unfolded = true;
                }
            }
            contentH = S(plate.LineHeight);
        }
        else
        {
            input = plate.DrawFight(dl, top, w, h, view == View.Waiting);
            contentH = MathF.Max(1, h);
        }
        ImGui.SetCursorScreenPos(top);
        ImGui.Dummy(new Vector2(w, MathF.Max(1, contentH)));

        if (input.Lock)
        {
            config.MeterLocked = !Locked;
            config.Save();
        }
        if (input.Preview && !Arranging) Preview = null;
        if (input.Menu) ImGui.OpenPopup(PlateMenu);
        if (!oneLine) DrawPlateMenu(m, input.MenuLeft, e != null);
        if (e == null) return;
        if (input.Pressed >= 0) OpenDetail?.Invoke(plate.Tab, e.Id, input.Pressed, null);
        if (input.Item >= 0 && plate.ItemAt(input.Item) is { } item)
        {
            if (plate.Tab == MeterTab.Deaths || item.Kind == MeterEventKind.Death)
            {
                var death = -1;
                for (var i = 0; i < e.Deaths.Count && death < 0; i++)
                    if (e.Deaths[i].PartyId == item.PartyId && Format.Clock(e.Deaths[i].At) == item.Time)
                        death = i;
                OpenDetail?.Invoke(MeterTab.Deaths, e.Id, item.PartyId, death >= 0 ? death : null);
            }
            else OpenDetail?.Invoke(MeterTab.Events, e.Id, item.PartyId > 0 ? item.PartyId : null, null);
        }
        if (input.Pointed >= 0 && m != null && !ImGui.IsPopupOpen(PlateMenu)) ShowDetails(e, m, input.Pointed, input.PointedMin, input.PointedMax);
    }

    /// <summary>
    /// The details panel beside the meter, on the side with room (the meter's own theme), level with
    /// the row pointed at and kept on the screen: in a tooltip, so it's over everything and needs no window of its own.
    /// </summary>
    private void ShowDetails(MeterEncounter e, IMeterSource m, int partyId, Vector2 rowMin, Vector2 rowMax)
    {
        if (plate.DetailsFor(e, m, partyId) is not { } d) return;
        var size = PlateMeter.DetailsSize(d) * S(1);
        var win = ImGui.GetWindowPos();
        var ww = ImGui.GetWindowWidth();
        var vp = ImGui.GetMainViewport();
        var gap = S(8);
        var x = win.X + ww + gap + size.X <= vp.WorkPos.X + vp.WorkSize.X ? win.X + ww + gap : win.X - gap - size.X;
        var y = Math.Clamp(rowMin.Y - S(10), vp.WorkPos.Y, MathF.Max(vp.WorkPos.Y, vp.WorkPos.Y + vp.WorkSize.Y - size.Y));
        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Vector4.Zero);
        ImGui.BeginTooltip();
        try
        {
            var at = ImGui.GetCursorScreenPos();
            plate.DrawDetails(ImGui.GetWindowDrawList(), at, d);
            ImGui.Dummy(size);
        }
        finally
        {
            ImGui.EndTooltip();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
        }
        _ = rowMax;
    }

    private static readonly string[] PlateTabIds = { "##ptab0", "##ptab1", "##ptab2", "##ptab3", "##ptab4" };

    /// <summary>
    /// The compact meter's one menu (`.menu`, 300 wide), off its head: the five tabs (the one you're on
    /// ticked, the deaths and events counted), the fights kept (as the picker's), Copy as text, and at its
    /// foot Pace against, Compact and One line. With a sample on, Stop the sample first.
    /// </summary>
    private void DrawPlateMenu(IMeterSource? m, float buttonLeft, bool hasFight)
    {
        var mw = S(PlateMenuWidth);
        ImGui.SetNextWindowSizeConstraints(new Vector2(mw, 0), new Vector2(mw, S(640)));
        PlaceMenu(buttonLeft, mw, plateMenuH > 0 ? plateMenuH : S(480));
        if (!Kit.BeginMenu(PlateMenu)) return;
        try
        {
            var w = mw - S(11.2f);
            var dl = ImGui.GetWindowDrawList();
            if (Preview != null)
            {
                var (stop, _, smin, smax) = Kit.MenuRow("##pmenu-stop", w, "Stop the sample", "Its numbers are made up", true, 16);
                var (sslot, _) = Kit.MenuRowParts(smin, smax, true, 16);
                Icons.Draw(dl, Icon.X, sslot + new Vector2((S(16) - S(15)) / 2), S(15), Palette.Muted);
                if (stop)
                {
                    Preview = null;
                    ImGui.CloseCurrentPopup();
                }
                Kit.MenuRule(w);
            }
            foreach (var tab in MeterLayout.TabOrder)
            {
                var on = tab == MeterLayout.Known(config.MeterTab);
                var count = !hasFight ? null : tab == MeterTab.Deaths ? said.DeathCount : tab == MeterTab.Events ? EventCount() : null;
                var reserve = on ? S(16) + S(8) : count != null ? Txt.Width(count, Face.Mono, 11) + S(8) : 0;
                var (clicked, _, min, max) = Kit.MenuRow(PlateTabIds[(int)tab], w, MeterLayout.TabWord(tab), null, true, 16, reserve);
                var (slot, right) = Kit.MenuRowParts(min, max, true, 16);
                Icons.Draw(dl, TabIcons[(int)tab], slot + new Vector2((S(16) - S(15)) / 2), S(15), on ? Palette.White : Palette.Muted);
                var cy = (min.Y + max.Y) / 2;
                if (on) Kit.Tick(dl, new Vector2(right - S(8), cy), 16);
                else if (count != null) Txt.LineRight(dl, right, min.Y, count, Face.Mono, 11, Palette.Faint, (max.Y - min.Y) / S(1));
                if (!clicked) continue;
                if (!on)
                {
                    config.MeterTab = tab;
                    config.Save();
                }
                ImGui.CloseCurrentPopup();
            }
            if (m != null)
            {
                var current = picked is { } id ? m.Get(id) : m.Current;
                var (here, others) = MeterView.Picker(m.Kept, current, current != null ? m.Best(current.Content) : null);
                if (here.Count > 0)
                {
                    Kit.MenuRule(w);
                    Kit.MenuEyebrow($"{(DateTime.Now.Hour >= 17 ? "Tonight" : "Today")} · {here[0].Fight.Duty}", w);
                    foreach (var r in here) PickRow(dl, r, w);
                }
                if (others.Count > 0)
                {
                    Kit.MenuEyebrow("Other duties", w);
                    foreach (var r in others) PickRow(dl, r, w, r.Fight.Duty);
                }
                if (current != null)
                {
                    Kit.MenuRule(w);
                    var (copy, _, cmin, cmax) = Kit.MenuRow("##pmenu-copy", w, "Copy as text", $"Pull {current.Pull}: each player's ENCDPS, share and deaths", true, 16);
                    var (cslot, _) = Kit.MenuRowParts(cmin, cmax, true, 16);
                    Icons.Draw(dl, Icon.Copy, cslot + new Vector2((S(16) - S(15)) / 2), S(15), Palette.Muted);
                    if (copy)
                    {
                        CopyParse(current);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
            Kit.MenuRule(w);
            var pos = ImGui.GetCursorScreenPos();
            var lineH = S(28);
            var gap = S(4);
            var y = pos.Y + S(5.6f);
            Txt.Line(dl, new Vector2(pos.X + S(8.8f), y), "Pace against", Face.Sans, 13, Palette.Muted, 28);
            var sw = Kit.SegmentedWidth(PaceItems);
            var pace = Kit.Segmented("##pmenu-pace", new Vector2(pos.X + w - S(8.8f) - sw, y), PaceItems, (int)config.MeterPace, true, false, lineH);
            if (pace != (int)config.MeterPace)
            {
                config.MeterPace = (PaceAgainst)pace;
                config.Save();
            }
            var size = Kit.SwitchSize;
            y += lineH + gap;
            Txt.Line(dl, new Vector2(pos.X + S(8.8f), y), "Compact", Face.Sans, 13, Palette.Muted, 28);
            if (Kit.Switch("##pmenu-compact", new Vector2(pos.X + w - S(8.8f) - size.X, y + (lineH - size.Y) / 2), config.MeterCompact))
            {
                config.MeterCompact = !config.MeterCompact;
                config.Save();
                ImGui.CloseCurrentPopup();
            }
            y += lineH + gap;
            Txt.Line(dl, new Vector2(pos.X + S(8.8f), y), "One line", Face.Sans, 13, Palette.Muted, 28);
            if (Kit.Pointed(new Vector2(pos.X, y), new Vector2(pos.X + w - size.X - S(20), y + lineH)))
                Kit.Tooltip("The meter folded to one line: the clock, the phase, you and your rank, deaths and the limit break. Its chevron opens it again.");
            if (Kit.Switch("##pmenu-line", new Vector2(pos.X + w - S(8.8f) - size.X, y + (lineH - size.Y) / 2), config.MeterCollapsed))
            {
                config.MeterCollapsed = !config.MeterCollapsed;
                config.Save();
                ImGui.CloseCurrentPopup();
            }
            ImGui.Dummy(new Vector2(w, S(5.6f) + (lineH + gap) * 3 - gap + S(7.2f)));
            plateMenuH = ImGui.GetWindowHeight();
        }
        finally
        {
            Kit.EndMenu();
        }
    }
}
