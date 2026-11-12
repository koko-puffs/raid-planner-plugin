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
/// once you let go. It never takes focus, makes no sounds and ignores Escape. Drawing catches what
/// goes wrong and logs it now and then.
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
    private Vector2 lastPos;
    private Vector2 lastSize;
    private bool drawnOnce;
    private bool dirty;

    /// <summary>The content's height asked for this frame (screen px), worked out before Begin.</summary>
    private float contentH;

    private static readonly WindowSizeConstraints Sizes = new() { MinimumSize = new Vector2(180, 20), MaximumSize = new Vector2(1200, 1400) };

    /// <param name="firstWidth">Its width until you give it one (CSS px).</param>
    /// <param name="firstAt">Where it goes before you move it: across the screen and down it (0–1).</param>
    protected OverlayWindow(string name, Configuration config, Fonts fonts, Func<OverlaySettings> settings, float firstWidth, Vector2 firstAt)
        : base(name)
    {
        Config = config;
        Fonts = fonts;
        this.settings = settings;
        this.firstWidth = firstWidth;
        this.firstAt = firstAt;
        SizeConstraints = Sizes;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        IsOpen = settings().Show;
    }

    /// <summary>It's wanted open: its setting's on or a sample is shown (the plugin keeps <see cref="Window.IsOpen"/> to this each frame).</summary>
    public bool WantedOpen => Settings.Show || Previewing;

    protected OverlaySettings Settings => settings();

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
        held = heldOnMe && !s.Locked;
        if (!held && dirty)
        {
            dirty = false;
            Config.Save();
        }
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav;
        if (s.Locked)
        {
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus;
            if (s.ClickThrough) Flags |= ImGuiWindowFlags.NoInputs;
        }
        var scale = ImGuiHelpers.GlobalScale;
        var chrome = s.Locked ? 0 : titleH > 0 ? titleH : ImGui.GetFrameHeight();
        contentH = MathF.Max(S(8), S(Measure(WidthCss)));
        if (held)
        {
            SizeCondition = ImGuiCond.FirstUseEver;
            return;
        }
        var height = contentH + chrome;
        Size = new Vector2(WidthCss, height / scale);
        SizeCondition = ImGuiCond.Always;
        var vp = ImGui.GetMainViewport();
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
        var at = MeterLayout.Place(new Vector2(s.AnchorX, s.AnchorY), new Vector2(WidthCss * scale, height), s.Grows, vp.WorkPos, vp.WorkPos + vp.WorkSize, otherScreens);
        ImGui.SetNextWindowPos(at, ImGuiCond.Always);
    }

    /// <summary>Inside: the title bar's height, whether you've taken hold of it, its anchor and width following you.</summary>
    private void Track()
    {
        var s = Settings;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (!s.Locked)
        {
            var t = ImGui.GetCursorStartPos().Y;
            if (t > 0) titleH = t;
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            heldOnMe = !s.Locked && ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByPopup);
        if (!s.Anchored || s.AnchorFor != s.Grows)
        {
            SetAnchor(s, MeterLayout.Corner(pos, size, s.Grows));
            Config.Save();
        }
        else if (held && drawnOnce && (pos != lastPos || size != lastSize))
        {
            SetAnchor(s, MeterLayout.Corner(pos, size, s.Grows));
            if (MathF.Abs(size.X - lastSize.X) > 0.5f) s.Width = MathF.Round(size.X / ImGuiHelpers.GlobalScale);
            dirty = true;
        }
        lastPos = pos;
        lastSize = size;
        drawnOnce = true;
    }

    private static void SetAnchor(OverlaySettings s, Vector2 at)
    {
        s.AnchorX = at.X;
        s.AnchorY = at.Y;
        s.Anchored = true;
        s.AnchorFor = s.Grows;
    }

    public sealed override void Draw()
    {
        Kit.ResetFrame();
        Frame.ResetFrame();
        Theme.PushInside();
        try
        {
            Track();
            using (Fonts.Push(Face.Sans, 13))
            {
                try
                {
                    Frame.Window(!Settings.Locked);
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

    /// <summary>/raidplan … unlock: the mouse back, movable again.</summary>
    public void Unlock()
    {
        Settings.ClickThrough = false;
        Settings.Locked = false;
        Config.Save();
    }

    /// <summary>The Preview tag (`.tag`), and pressed, the sample stops. Returns its width.</summary>
    protected float PreviewTag(ImDrawListPtr dl, Vector2 pos, string id)
    {
        var w = Kit.TagWidth("Preview", TagKind.Plain);
        var (clicked, hovered, _) = Kit.Hit(id, pos, new Vector2(w, S(18)));
        Kit.Tag(dl, pos, "Preview", TagKind.Plain, 18);
        if (hovered) Kit.Tooltip("A sample with made-up numbers. Press to stop it.");
        if (clicked) Previewing = false;
        return w;
    }
}
