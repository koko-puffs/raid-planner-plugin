using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// A small window over the fight as the meter is (the cooldown tracker, the on-screen callouts): its
/// background as see-through as its settings say, as tall as what it shows, kept at its anchor as it
/// grows (its top edge growing down, its foot growing up: <see cref="MeterLayout"/>), lockable (no title
/// bar, no moving or sizing) and click-through when locked. You give it its width; its height is its
/// own. While you hold it (unlocked) it's neither placed nor sized, and its width and anchor are kept
/// once you let go. Its own buttons (the Preview tag) are kept out of the reach of its resizing edges
/// and corners, and while the mouse is over one it isn't resizable that frame (<see cref="ResizeGuard"/>).
/// It never takes focus, makes no sounds and ignores Escape. Drawing catches what goes wrong and logs
/// it now and then. While Arrange windows has it (<see cref="ArrangeMode"/>) it's unlocked whatever its
/// setting says, its arranging head in place of the title bar and a dashed outline round it.
/// </summary>
public abstract class OverlayWindow : Window
{
    protected readonly Configuration Config;
    protected readonly Fonts Fonts;
    private readonly Func<OverlaySettings> settings;
    private readonly float firstWidth;
    private readonly Vector2 firstAt;
    private DateTime failedAt;

    private bool heldOnMe;
    private bool held;
    private float titleH;

    /// <summary>Its chrome this frame (screen px): the title bar unlocked, the arranging head, none locked.</summary>
    private float chromeNow;

    private Vector2 lastPos;
    private Vector2 lastSize;
    private bool drawnOnce;
    private bool dirty;

    /// <summary>Its buttons kept clear of the resizing edges.</summary>
    private readonly ResizeGuard guard = new();

    /// <summary>The content's height asked for this frame (screen px), worked out before Begin.</summary>
    private float contentH;

    private static readonly CssLimits Sizes = new(new Vector2(180, 20), new Vector2(1200, 1400));

    /// <summary>Which window it is in Arrange windows.</summary>
    private readonly ArrangePart part;

    /// <summary>Reset places was pressed (Arrange windows): put at its first place before it's next drawn.</summary>
    private bool resetWanted;

    /// <param name="firstWidth">Its width until you give it one (CSS px).</param>
    /// <param name="firstAt">Where it goes before you move it: across the screen and down it (0–1).</param>
    protected OverlayWindow(string name, Configuration config, Fonts fonts, Func<OverlaySettings> settings, float firstWidth, Vector2 firstAt, ArrangePart part)
        : base(name)
    {
        this.part = part;
        Config = config;
        Fonts = fonts;
        this.settings = settings;
        this.firstWidth = firstWidth;
        this.firstAt = firstAt;
        SizeConstraints = Sizes.Now;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        IsOpen = settings().Show;
    }

    /// <summary>It's wanted open: its setting's on or a sample is shown (the plugin keeps <see cref="Window.IsOpen"/> to this each frame).</summary>
    public bool WantedOpen => Settings.Show || Previewing;

    protected OverlaySettings Settings => settings();

    /// <summary>Arrange windows is on and this window takes part: shown unlocked with its arranging head and outline (<see cref="ArrangeMode"/>).</summary>
    protected bool Arranging => ArrangeMode.Shows(part);

    /// <summary>Locked as its setting says, except while it's arranged.</summary>
    private bool Locked => Settings.Locked && !Arranging;

    /// <summary>Reset places (Arrange windows): back to where it first goes (<see cref="OverlayLayout.FirstAnchor"/>) at its next frame.</summary>
    public void ResetPlace() => resetWanted = true;

    /// <summary>A sample is shown (under a Preview tag) instead of the real thing.</summary>
    public bool Previewing { get; set; }

    /// <summary>Its width now (CSS px).</summary>
    protected float WidthCss => Settings.Width > 0 ? Settings.Width : firstWidth;

    /// <summary>Whether there's something to show this frame (asked before Begin).</summary>
    protected abstract bool Wanted();

    /// <summary>How tall its content is this frame (CSS px) at its width (asked before Begin, after <see cref="Wanted"/>).</summary>
    protected abstract float Measure(float widthCss);

    /// <summary>Its content, in the window's draw list, from its top-left, this wide and tall (screen px).</summary>
    protected abstract void DrawContent(ImDrawListPtr dl, Vector2 top, float w, float h);

    /// <summary>What it says in the log when drawing went wrong.</summary>
    protected abstract string What { get; }

    public sealed override bool DrawConditions()
    {
        try
        {
            return Wanted();
        }
        catch (Exception ex)
        {
            Failed(ex);
            return false;
        }
    }

    public sealed override void PreDraw()
    {
        try
        {
            Arrange();
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
        Theme.PushFrame(Palette.Rgba(13, 13, 16, Math.Clamp(Settings.Background, 0, 1)), Palette.RadiusMd);
    }

    public sealed override void PostDraw() => Theme.Pop();

    public override void OnClose()
    {
        // Its × is its setting's Show (a sample just stops).
        if (Previewing)
        {
            Previewing = false;
            return;
        }
        if (!Settings.Show) return;
        Settings.Show = false;
        Config.Save();
    }

    /// <summary>Before Begin: its flags, its height (its content's), its width (yours) and its place from its anchor.</summary>
    private void Arrange()
    {
        var s = Settings;
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) heldOnMe = false;
        held = heldOnMe && !Locked;
        if (!held && dirty)
        {
            dirty = false;
            Config.Save();
        }
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav;
        // Arranged: its own head in place of the title bar (and no ×), movable and sizable.
        if (Arranging) Flags |= ImGuiWindowFlags.NoTitleBar;
        if (Locked)
        {
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus;
            if (s.ClickThrough) Flags |= ImGuiWindowFlags.NoInputs;
        }
        // Not resizable while the mouse is over one of its buttons: a press there is theirs.
        else if (guard.Over()) Flags |= ImGuiWindowFlags.NoResize;
        var scale = Theme.Scale;
        SizeConstraints = Sizes.Now;
        var titleBar = titleH > 0 ? titleH : ImGui.GetFrameHeight();
        var chrome = Arranging ? ArrangeMode.HeadH : Locked ? 0 : titleBar;
        chromeNow = chrome;
        // An anchor saved before it was its content's: read once with the chrome it's shown with (none locked, the title
        // bar unlocked), so it stays exactly where it is.
        if (s.AnchorVersion < WindowAnchor.Current)
        {
            s.SetAnchor(s.Anchor().Migrated(s.Locked ? 0 : titleBar));
            Config.Save();
        }
        contentH = MathF.Max(S(8), S(Measure(WidthCss)));
        if (held)
        {
            SizeCondition = ImGuiCond.FirstUseEver;
            return;
        }
        var height = contentH + chrome;
        Size = Theme.Units(new Vector2(WidthCss, height / scale));
        SizeCondition = ImGuiCond.Always;
        var vp = ImGui.GetMainViewport();
        if (resetWanted)
        {
            // Reset places: its first anchor for its size now, as the corner its Grows keeps.
            resetWanted = false;
            var first = OverlayLayout.FirstAnchor(vp.WorkPos, vp.WorkPos + vp.WorkSize, WidthCss * scale, height, firstAt.X, firstAt.Y, s.Grows, chrome);
            SetAnchor(s, first);
            Config.Save();
        }
        if (!s.Anchored || s.AnchorFor != s.Grows)
        {
            if (!s.Anchored)
            {
                // Never placed: where it goes at first (then its anchor is read from where it is).
                var first = OverlayLayout.FirstAnchor(vp.WorkPos, vp.WorkPos + vp.WorkSize, WidthCss * scale, height, firstAt.X, firstAt.Y, MeterGrows.Down);
                ImGui.SetNextWindowPos(first, ImGuiCond.Always);
            }
            return;
        }
        var otherScreens = (ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0;
        if (s.Anchor().Place(s.Grows, new Vector2(WidthCss * scale, height), chrome, vp.WorkPos, vp.WorkPos + vp.WorkSize, otherScreens, held) is { } at)
            ImGui.SetNextWindowPos(at, ImGuiCond.Always);
    }

    /// <summary>Inside: the title bar's height, whether you've taken hold of it, its anchor and width following you.</summary>
    private void Track()
    {
        var s = Settings;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (!Locked)
        {
            var t = ImGui.GetCursorStartPos().Y;
            if (t > 0) titleH = t;
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            heldOnMe = !Locked && ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByPopup);
        // Locked (or not held) it's kept as it is: where it was kept on the screen this frame is never read back.
        var (anchor, change) = s.Anchor().Track(s.Grows, pos, size, chromeNow, held && drawnOnce && (pos != lastPos || size != lastSize));
        if (change is AnchorChange.First or AnchorChange.Turned)
        {
            s.SetAnchor(anchor);
            Config.Save();
        }
        else if (change == AnchorChange.Moved)
        {
            s.SetAnchor(anchor);
            if (MathF.Abs(size.X - lastSize.X) > 0.5f) s.Width = MathF.Round(size.X / Theme.Scale);
            dirty = true;
        }
        lastPos = pos;
        lastSize = size;
        drawnOnce = true;
    }

    private static void SetAnchor(OverlaySettings s, Vector2 at) => s.SetAnchor(new WindowAnchor(at, true, s.Grows, WindowAnchor.Current));

    public sealed override void Draw()
    {
        Kit.ResetFrame();
        Frame.ResetFrame();
        Theme.PushInside();
        try
        {
            // Before a font is pushed: the resize grip is sized by the font at Begin.
            guard.Frame();
            Track();
            using (Fonts.Push(Face.Sans, 13))
            {
                try
                {
                    var arranging = Arranging;
                    if (arranging) ArrangeMode.Head(part);
                    Frame.Window(!Locked);
                    var top = ImGui.GetCursorScreenPos();
                    var w = ImGui.GetWindowWidth();
                    var bottom = ImGui.GetWindowPos().Y + ImGui.GetWindowHeight();
                    Kit.PushBox(ImGui.GetWindowPos().X, w);
                    try
                    {
                        DrawContent(Kit.Dl, top, w, MathF.Max(1, bottom - top.Y));
                    }
                    finally
                    {
                        Kit.PopBox();
                    }
                    ImGui.SetCursorScreenPos(top);
                    ImGui.Dummy(new Vector2(w, MathF.Max(1, bottom - top.Y)));
                    if (arranging) ArrangeMode.Outline(part);
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

    protected void Failed(Exception ex)
    {
        if (DateTime.UtcNow - failedAt <= TimeSpan.FromSeconds(10)) return;
        failedAt = DateTime.UtcNow;
        Services.Log.Error(ex, What);
    }

    /// <summary>/raidcodex … unlock: the mouse back, movable again.</summary>
    public void Unlock()
    {
        Settings.ClickThrough = false;
        Settings.Locked = false;
        Config.Save();
    }

    /// <summary>The Preview tag (`.tag`), and pressed, the sample stops; moved in out of the resizing edges' reach. Returns its width.</summary>
    protected float PreviewTag(ImDrawListPtr dl, Vector2 pos, string id)
    {
        var w = Kit.TagWidth("Preview", TagKind.Plain);
        pos.X -= guard.ShiftLeft(pos, pos + new Vector2(w, S(18)));
        guard.Keep(pos, pos + new Vector2(w, S(18)));
        // While it's arranged the sample stays (Done or Cancel stops it): the tag isn't pressable.
        var (clicked, hovered, _) = Kit.Hit(id, pos, new Vector2(w, S(18)), Arranging);
        Kit.Tag(dl, pos, "Preview", TagKind.Plain, 18);
        if (hovered) Kit.Tooltip("A sample with made-up numbers. Press to stop it.");
        if (clicked) Previewing = false;
        return w;
    }
}
