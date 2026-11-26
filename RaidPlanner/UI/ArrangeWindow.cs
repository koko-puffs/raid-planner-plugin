using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Arrange windows (0.8.0; the main window's <c>StartArrange</c>, <c>/raidcodex arrange</c>): every window that's
/// turned on shows at once on the game's screen, unlocked, under a head with its name and a dashed outline
/// (<see cref="ArrangeMode"/>), to drag where you want; the one last pressed is picked (in ember) and its options
/// are in a small panel at the top right (<see cref="ArrangePanel"/>). This is the floating toolbar near the bottom
/// of the screen ("Arranging windows"), and what its buttons do: <b>Done</b> keeps where each is (the windows save
/// their places as they're moved) and locks each, <b>Cancel</b> puts every place, size, lock and option back as the
/// mode found them, <b>Reset places</b> moves each to its first place. Either way the samples the mode started stop.
/// It never takes focus as it appears, makes no sounds and ignores Escape (no keys at all: CLAUDE.md).
/// </summary>
public sealed class ArrangeWindow : Window
{
    private readonly Configuration config;
    private readonly Fonts fonts;
    private readonly MeterWindow meter;
    private readonly PlanWindow plan;
    private readonly CalloutWindow callouts;
    private readonly CooldownWindow cooldowns;
    private DateTime failedAt;

    /// <summary>Since 0.9.0: the ready check card and the death recap (set by the plugin; null, they don't take part).</summary>
    public OverlayWindow? ReadyCheck { get; set; }

    public OverlayWindow? DeathRecap { get; set; }

    private const string Title = "Arranging windows";
    private const string Line = "Drag them where you want. They lock when you’re done.";
    private const string NoneLine = "None of the windows is turned on: Settings → Features turns them on.";

    /// <summary>The toolbar's padding and gaps (CSS px, the mock-up's): 10 down, 16 at the left, 12 at the right, 10 between.</summary>
    private const float PadY = 10, PadLeft = 16, PadRight = 12, Gap = 10;

    /// <summary>Its foot's distance from the screen's bottom edge (CSS px).</summary>
    private const float FromBottom = 28;

    public ArrangeWindow(Configuration config, Fonts fonts, MeterWindow meter, PlanWindow plan, CalloutWindow callouts, CooldownWindow cooldowns)
        : base("Arranging windows###RaidPlannerArrange")
    {
        this.config = config;
        this.fonts = fonts;
        this.meter = meter;
        this.plan = plan;
        this.callouts = callouts;
        this.cooldowns = cooldowns;
        Panel = new ArrangePanel(config, fonts);
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        AllowPinning = false;
        AllowClickthrough = false;
        IsOpen = false;
    }

    /// <summary>The picked window's options (top right), shown while the mode is on.</summary>
    public ArrangePanel Panel { get; }

    /// <summary>Starts the meter's sample fight (set by the plugin): false when it couldn't (a real fight is on).</summary>
    public Func<bool>? StartSample { get; set; }

    /// <summary>Arrange windows has ended (Done or Cancel; set by the plugin: the main window comes back if it stepped aside).</summary>
    public Action? Ended { get; set; }

    private static ArrangeSession Session => ArrangeMode.Session;

    // ---------------------------------------------------------------------------------------------
    // Start, Done, Cancel, Reset places.

    /// <summary>
    /// Starts the mode (nothing when it's on): the windows turned on take part, their places kept for Cancel, each
    /// shown with its sample (the meter's sample fight unless a real fight is on, the callouts' and the tracker's
    /// samples; the plan window opened as it is, or empty).
    /// </summary>
    public void Start()
    {
        if (Session.On)
        {
            IsOpen = true;
            Panel.IsOpen = true;
            return;
        }
        var taking = ArrangeParts.Taking(config.ShowMeter, config.ShowPlanInDuties || plan.IsOpen, config.CalloutOverlay.Show, config.Cooldowns.Show,
            ReadyCheck != null && config.ReadyCheck.Show, DeathRecap != null && config.DeathRecap.Show);
        var places = new (ArrangePart, WindowPlace)[taking.Count];
        for (var i = 0; i < taking.Count; i++) places[i] = (taking[i], PlaceOf(taking[i]));
        Session.Start(places);
        foreach (var part in taking)
        {
            switch (part)
            {
                case ArrangePart.Meter:
                    if (meter.Preview == null && StartSample?.Invoke() == true) Session.StartedSample(part);
                    meter.IsOpen = true;
                    break;
                case ArrangePart.Plan:
                    if (!plan.IsOpen)
                    {
                        plan.IsOpen = true;
                        Session.StartedSample(part);
                    }
                    break;
                case ArrangePart.Callouts:
                case ArrangePart.Cooldowns:
                case ArrangePart.ReadyCheck:
                case ArrangePart.DeathRecap:
                    var w = Overlay(part).Window;
                    if (!w.Previewing)
                    {
                        w.Previewing = true;
                        Session.StartedSample(part);
                    }
                    w.IsOpen = true;
                    break;
            }
        }
        IsOpen = true;
        Panel.IsOpen = true;
    }

    /// <summary>Done: each window kept where it is now (saved as it moved) and locked; the mode ends.</summary>
    public void Done()
    {
        if (!Session.On) return;
        foreach (var part in Session.Parts)
        {
            switch (part)
            {
                case ArrangePart.Meter:
                    config.MeterLocked = true;
                    break;
                case ArrangePart.Plan:
                    config.PlanWindowLocked = true;
                    break;
                default:
                    Overlay(part).Settings().Locked = true;
                    break;
            }
        }
        End();
    }

    /// <summary>Cancel (and the plugin unloading mid-way): every window's place, size, lock and options as the mode found them; the mode ends.</summary>
    public void Cancel()
    {
        if (!Session.On) return;
        foreach (var part in Session.Parts)
        {
            if (Session.Before(part) is not { } was) continue;
            switch (part)
            {
                case ArrangePart.Meter:
                    config.MeterLocked = was.Locked;
                    config.MeterClickThrough = was.ClickThrough;
                    config.MeterGrows = was.Grows;
                    config.MeterBackground = was.Background;
                    config.MeterHeight = was.Height;
                    config.MeterAnchored = was.Anchored;
                    config.MeterAnchorX = was.AnchorX;
                    config.MeterAnchorY = was.AnchorY;
                    config.MeterAnchorFor = was.AnchorFor;
                    config.MeterAnchorVersion = was.AnchorVersion;
                    meter.PutWidth(was.Width);
                    // Never placed before the mode: back to its first place (ImGui's), as it would have gone.
                    if (!was.Anchored) meter.ResetPlace();
                    break;
                case ArrangePart.Plan:
                    config.PlanWindowLocked = was.Locked;
                    // Its size as drawn is in screen px; PutAt takes CSS px (since 0.9.0), so it isn't grown by the scale.
                    if (was.Anchored) plan.PutAt(new Vector2(was.AnchorX, was.AnchorY), new Vector2(was.Width, was.Height) / MathF.Max(0.01f, plan.DrawnScale));
                    break;
                default:
                    was.PutBack(Overlay(part).Settings());
                    break;
            }
        }
        End();
    }

    /// <summary>Reset places: each window taking part to its first place; the mode goes on.</summary>
    public void ResetPlaces()
    {
        foreach (var part in Session.Parts)
        {
            switch (part)
            {
                case ArrangePart.Meter:
                    meter.ResetPlace();
                    break;
                case ArrangePart.Plan:
                    plan.ResetPlace();
                    break;
                default:
                    Overlay(part).Window.ResetPlace();
                    break;
            }
        }
    }

    /// <summary>The mode ends: the samples it started stop (the plan window it opened closes), the settings are saved, its windows close.</summary>
    private void End()
    {
        foreach (var part in Session.End())
        {
            switch (part)
            {
                case ArrangePart.Meter:
                    meter.Preview = null;
                    break;
                case ArrangePart.Plan:
                    plan.IsOpen = false;
                    break;
                default:
                    Overlay(part).Window.Previewing = false;
                    break;
            }
        }
        config.Save();
        IsOpen = false;
        Panel.IsOpen = false;
        Ended?.Invoke();
    }

    /// <summary>A window's place and options now.</summary>
    private WindowPlace PlaceOf(ArrangePart part) => part switch
    {
        ArrangePart.Meter => new WindowPlace(config.MeterLocked, config.MeterClickThrough, config.MeterGrows, config.MeterBackground, meter.WidthCss,
            config.MeterHeight, config.MeterAnchored, config.MeterAnchorX, config.MeterAnchorY, config.MeterAnchorFor, config.MeterAnchorVersion),
        ArrangePart.Plan => WindowPlace.OfPlan(config.PlanWindowLocked, plan.Drawn?.Pos, plan.Drawn?.Size),
        _ => WindowPlace.Of(Overlay(part).Settings()),
    };

    /// <summary>An overlay's window and its settings.</summary>
    private (OverlayWindow Window, Func<OverlaySettings> Settings) Overlay(ArrangePart part) => part switch
    {
        ArrangePart.Callouts => (callouts, () => config.CalloutOverlay),
        ArrangePart.ReadyCheck when ReadyCheck != null => (ReadyCheck, () => config.ReadyCheck),
        ArrangePart.DeathRecap when DeathRecap != null => (DeathRecap, () => config.DeathRecap),
        _ => (cooldowns, () => config.Cooldowns),
    };

    // ---------------------------------------------------------------------------------------------
    // The toolbar.

    /// <summary>Its size (screen px), worked out before Begin from its words and buttons.</summary>
    private Vector2 Measure()
    {
        var none = Session.Parts.Count == 0;
        var text = MathF.Max(Txt.Width(Title, Face.SansSemibold, 13), Txt.Width(none ? NoneLine : Line, Face.Sans, 12));
        var buttons = Kit.ButtonWidth("Reset places", BtnSize.Md, Icon.Undo, Btn.Quiet) + Kit.ButtonWidth("Cancel") + Kit.ButtonWidth("Done", BtnSize.Md, Icon.Check, Btn.Tele);
        var w = S(PadLeft) + text + S(6 + Gap) + buttons + S(Gap) * 2 + S(PadRight);
        var h = S(PadY) * 2 + Kit.ButtonHeight();
        return new Vector2(MathF.Ceiling(w), MathF.Ceiling(h));
    }

    public override bool DrawConditions() => Session.On;

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings;
        try
        {
            // The plan window opened by the mode: where ImGui first put it, once it's been drawn (Cancel puts it back there).
            if (Session.Before(ArrangePart.Plan) is { Anchored: false } p && plan.Drawn is { } d)
                Session.Learnt(ArrangePart.Plan, WindowPlace.OfPlan(p.Locked, d.Pos, d.Size));
            var size = Measure();
            Size = Theme.UnitsOfScreen(size);
            SizeCondition = ImGuiCond.Always;
            var vp = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(ArrangeLayout.Toolbar(vp.WorkPos, vp.WorkPos + vp.WorkSize, size, S(FromBottom)), ImGuiCond.Always);
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
        // Raised near-solid, a white hairline at 20%, 14px corners.
        Theme.PushFrame(Palette.Raised, Palette.RadiusLg, Palette.Line3);
    }

    public override void PostDraw() => Theme.Pop();

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
                    DrawBar();
                }
                finally
                {
                    Frame.Unwind();
                }
            }
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
        finally
        {
            Theme.Pop();
        }
    }

    private void DrawBar()
    {
        // Its shadow (`0 18px 40px -10px #000`, as the kit fakes one: rings of black round it).
        var win = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        Kit.Rings(win + new Vector2(0, S(6)), win + size + new Vector2(0, S(6)), ImGui.GetStyle().WindowRounding, 8, 20, 0.3f, 0.14f);
        var dl = Kit.Dl;
        var none = Session.Parts.Count == 0;
        var bh = Kit.ButtonHeight();
        var y = win.Y + S(PadY);
        var x = win.X + S(PadLeft);
        // Its words: what's going on, and what to do.
        var textH = S(18) + S(2) + S(16);
        var ty = y + (bh - textH) / 2;
        Txt.Line(dl, new Vector2(x, ty), Title, Face.SansSemibold, 13, Palette.White, 18);
        Txt.Line(dl, new Vector2(x, ty + S(20)), none ? NoneLine : Line, Face.Sans, 12, Palette.Muted, 16);
        // The buttons from the right: Done, Cancel, Reset places.
        var right = win.X + size.X - S(PadRight);
        var doneW = Kit.ButtonWidth("Done", BtnSize.Md, Icon.Check, Btn.Tele);
        var cancelW = Kit.ButtonWidth("Cancel");
        var resetW = Kit.ButtonWidth("Reset places", BtnSize.Md, Icon.Undo, Btn.Quiet);
        var dx = right - doneW;
        var cx = dx - S(Gap) - cancelW;
        var rx = cx - S(Gap) - resetW;
        var reset = Kit.Button("##arrange-reset", new Vector2(rx, y), "Reset places", Btn.Quiet, BtnSize.Md, Icon.Undo, 0, none,
            "Each window back to where it first goes. You can still cancel.");
        var cancel = Kit.Button("##arrange-cancel", new Vector2(cx, y), "Cancel", Btn.Outline, BtnSize.Md, null, 0, false,
            "Every window back where it was, as it was");
        var done = Kit.Button("##arrange-done", new Vector2(dx, y), "Done", Btn.Tele, BtnSize.Md, Icon.Check, 0, false, "Keep them here, and lock them");
        ImGui.SetCursorScreenPos(win);
        ImGui.Dummy(size);
        // Acted on once drawn: Done and Cancel close this window.
        if (reset) ResetPlaces();
        else if (cancel) Cancel();
        else if (done) Done();
    }

    private void Failed(Exception ex)
    {
        if (DateTime.UtcNow - failedAt <= TimeSpan.FromSeconds(10)) return;
        failedAt = DateTime.UtcNow;
        Services.Log.Error(ex, "Raid Codex's Arrange windows couldn't draw");
    }
}

/// <summary>
/// Arrange windows' options panel (the mock-up's, at the top right, 250 wide): the picked window's name and its
/// few options: <b>Grows</b> (Down, Up), <b>Click-through</b> (once locked) and <b>Background</b> (a slider), the
/// meter's (<see cref="Configuration.MeterGrows"/>, <see cref="Configuration.MeterClickThrough"/>,
/// <see cref="Configuration.MeterBackground"/>) or the overlay's own (<see cref="OverlaySettings"/>), as Settings has
/// them. The plan window has none of them: it says it's sized by its edges.
/// </summary>
public sealed class ArrangePanel : Window
{
    private readonly Configuration config;
    private readonly Fonts fonts;
    private DateTime failedAt;
    private bool backgroundDirty;

    private static readonly string[] GrowsItems = ["Down", "Up"];

    /// <summary>Its width (CSS px), its padding (12 down, 14 in), a row's height (38) and the slider's (24).</summary>
    private const float WidthCss = 250, PadY = 12, PadX = 14, RowCss = 38, SliderCss = 24;

    private const float FromEdge = 24;

    public ArrangePanel(Configuration config, Fonts fonts)
        : base("Arrange options###RaidPlannerArrangeOptions")
    {
        this.config = config;
        this.fonts = fonts;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        AllowPinning = false;
        AllowClickthrough = false;
        IsOpen = false;
    }

    private static ArrangePart? Picked => ArrangeMode.On ? ArrangeMode.Session.Picked : null;

    /// <summary>Its height (CSS px) for the window picked.</summary>
    private static float HeightCss(ArrangePart part) =>
        PadY + 18 + 2 + 16 + (ArrangeParts.HasOptions(part) ? 6 + RowCss * 3 + SliderCss : 0) + PadY;

    public override bool DrawConditions() => Picked != null;

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings;
        if (Picked is { } part)
        {
            Size = Theme.Units(new Vector2(WidthCss, HeightCss(part)));
            SizeCondition = ImGuiCond.Always;
            var vp = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(ArrangeLayout.Panel(vp.WorkPos, vp.WorkPos + vp.WorkSize, S(WidthCss), S(FromEdge)), ImGuiCond.Always);
        }
        // Raised near-solid, a white hairline at 12%, 12px corners.
        Theme.PushFrame(Palette.Raised, 12, Palette.Line2);
    }

    public override void PostDraw() => Theme.Pop();

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
                    if (Picked is { } part) DrawOptions(part);
                }
                finally
                {
                    Frame.Unwind();
                }
            }
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - failedAt > TimeSpan.FromSeconds(10))
            {
                failedAt = DateTime.UtcNow;
                Services.Log.Error(ex, "Raid Codex's Arrange windows couldn't draw its options");
            }
        }
        finally
        {
            Theme.Pop();
        }
    }

    private void DrawOptions(ArrangePart part)
    {
        var win = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = Kit.Dl;
        var left = win.X + S(PadX);
        var w = size.X - S(PadX) * 2;
        var right = left + w;
        var y = win.Y + S(PadY);
        Txt.Line(dl, new Vector2(left, y), ArrangeParts.Label(part), Face.SansSemibold, 13, Palette.White, 18);
        y += S(18 + 2);
        var has = ArrangeParts.HasOptions(part);
        Txt.Line(dl, new Vector2(left, y), has ? "Picked: what it does once it’s locked" : "Picked: drag its edges to size it", Face.Sans, 12, Palette.Faint, 16);
        y += S(16);
        if (has)
        {
            y += S(6);
            var meter = part == ArrangePart.Meter;
            var o = meter ? null : part switch
            {
                ArrangePart.Callouts => config.CalloutOverlay,
                ArrangePart.ReadyCheck => config.ReadyCheck,
                ArrangePart.DeathRecap => config.DeathRecap,
                _ => config.Cooldowns,
            };

            // Grows: which edge stays put as it grows.
            Kit.Rule(dl, left, right, y);
            Txt.Line(dl, new Vector2(left, y), "Grows", Face.SansMedium, 13, Palette.Text, RowCss);
            var grows = (int)(meter ? config.MeterGrows : o!.Grows);
            var segW = Kit.SegmentedWidth(GrowsItems);
            var picked = Kit.Segmented("##arrange-grows", new Vector2(right - segW, y + (S(RowCss) - S(HSm)) / 2), GrowsItems, grows);
            if (Kit.Pointed(new Vector2(left, y), new Vector2(right - segW - S(8), y + S(RowCss))))
                Kit.Tooltip("Down: its top edge stays where you put it and it grows downwards. Up: its foot stays, and it grows upwards.");
            if (picked != grows)
            {
                if (meter) config.MeterGrows = (MeterGrows)picked;
                else o!.Grows = (MeterGrows)picked;
                config.Save();
            }
            y += S(RowCss);

            // Click-through (when locked): the mouse goes through it to the game.
            Kit.Rule(dl, left, right, y);
            Txt.Line(dl, new Vector2(left, y), "Click-through", Face.SansMedium, 13, Palette.Text, RowCss);
            var through = meter ? config.MeterClickThrough : o!.ClickThrough;
            var sw = Kit.SwitchSize;
            if (Kit.Switch("##arrange-through", new Vector2(right - sw.X, y + (S(RowCss) - sw.Y) / 2), through))
            {
                if (meter) config.MeterClickThrough = !through;
                else o!.ClickThrough = !through;
                config.Save();
            }
            if (Kit.Pointed(new Vector2(left, y), new Vector2(right, y + S(RowCss))))
                Kit.Tooltip("Once it's locked (Done locks it), the mouse goes through it to the game. Its command with unlock gives the mouse back.");
            y += S(RowCss);

            // Background: how solid its ground is (the meter keeps 20% at least, as Settings does).
            Kit.Rule(dl, left, right, y);
            Txt.Line(dl, new Vector2(left, y), "Background", Face.SansMedium, 13, Palette.Text, RowCss);
            var min = meter ? 0.2f : 0f;
            var bg = Math.Clamp(meter ? config.MeterBackground : o!.Background, min, 1f);
            Txt.LineRight(dl, right, y, Format.PercentWhole(bg), Face.MonoSemibold, 12, Palette.White, RowCss);
            y += S(RowCss) - S(6);
            var nb = Math.Clamp(Kit.Slider("##arrange-bg", new Vector2(left + S(2), y), w - S(4), bg), min, 1f);
            if (MathF.Abs(nb - bg) > 0.001f)
            {
                if (meter) config.MeterBackground = nb;
                else o!.Background = nb;
                backgroundDirty = true;
            }
            y += S(SliderCss);
        }
        // Saved once the knob is let go, not on every frame it's dragged.
        if (backgroundDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            backgroundDirty = false;
            config.Save();
        }
        ImGui.SetCursorScreenPos(win);
        ImGui.Dummy(new Vector2(size.X, MathF.Max(1, y - win.Y)));
    }
}
