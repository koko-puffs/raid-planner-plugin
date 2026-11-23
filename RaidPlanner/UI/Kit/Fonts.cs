using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Dalamud.Game;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace RaidPlanner.UI;

/// <summary>
/// The faces at the sizes the windows use, loaded through Dalamud's font atlas (so they follow
/// Dalamud's global scale and rebuild with it), times the plugin's own scale (since 0.8.1, Settings →
/// Windows → Size: <see cref="UiScale"/>; a new one builds a whole new set, drawn with once it's all
/// built, <see cref="Follow"/>). Every (face, size) is a font in the atlas (30 on
/// most clients, some 20,000 glyphs, a few million pixels at 100%), so the set is fixed, and a size
/// not here is drawn at the nearest one:
/// Geist 400 at 11, 12, 13, 14; 500 at 12, 13, 14; 600 at 7, 8, 10, 13, 14, 15;
/// Geist Mono 500 at 9, 10, 11, 12, 13; 600 at 10, 11, 12, 13, 15, 16, 18, 30;
/// Archivo Expanded ExtraBold at 13, 16, 17, 22 and 30 (the on-screen callouts' first step, since
/// 0.4.0: latin only, with no extra language's glyphs, so a callout in Chinese or Korean is drawn at 22).
///
/// Sizes are CSS sizes (the em). ImGui sizes a font by its line (ascent to descent), so each face is
/// loaded at its em times its line ratio, and 14px Geist here is as tall as 14px in the browser.
///
/// On a Japanese client the sans faces and the callout are Dalamud's own font from 11px up (it has
/// the kana and kanji that duty, ability and plan names need; the callout isn't put in capitals
/// there). It has no weights, so the faces of one size share one font (8 of them, not 17: each
/// carries thousands of kanji); the tiny spot labels (7, 8, 10) stay Geist. The mono keeps its face
/// (it shows numbers and the windows' own words; text with Japanese in it is drawn in the sans:
/// <see cref="Txt.For"/>).
///
/// The extra glyphs of Dalamud's language (Chinese, Korean: tens of thousands each) are added only to
/// the faces that draw a plan's own words (<see cref="PlanText"/>): on the global client everything
/// else the windows show is the game's (English, German, French, Japanese) or their own.
///
/// Since 0.7.0 the compact meter's theme brings its own faces, loaded only while it's picked
/// (<see cref="Want"/>, the others let go): Zen Maru Gothic 700 and 900 (Mochi) and Fraunces 500 and
/// 600 at opsz 14 (Grimoire), static latin and latin-ext instances made with fontTools (OFL); the
/// game's Axis, MiedingerMid and Jupiter (Native) through Dalamud's game fonts, nothing shipped; and
/// the sizes of Geist and Archivo a theme draws at that the windows don't (Neon's 9px capitals).
/// </summary>
public sealed class Fonts : IDisposable
{
    /// <summary>(hhea ascent − descent) / em, read from the font files: Geist and Geist Mono 1.3, Archivo 1.088.</summary>
    private const float GeistLine = 1.3f;

    private const float ArchivoLine = 1.088f;

    /// <summary>Zen Maru Gothic's line (1.448) and Fraunces' (1.233), read from the files as Geist's was.</summary>
    private const float RoundLine = 1.448f;

    private const float SerifLine = 1.233f;

    /// <summary>
    /// The faces a plan's own words are drawn in (written on the site, in any language): the callout
    /// (16, 22), its name, notes and the step on Home (Sans 13, SansSemibold 13), plan names in the
    /// plans menu (SansMedium 14) and Settings (Sans 12), board text (SansSemibold 13).
    /// </summary>
    public static readonly (Face Face, int Px)[] PlanText =
    {
        (Face.Callout, 16), (Face.Callout, 22), (Face.Sans, 12), (Face.Sans, 13), (Face.SansMedium, 14), (Face.SansSemibold, 13),
    };

    public static readonly (Face Face, int Px)[] Wanted =
    {
        (Face.Sans, 11), (Face.Sans, 12), (Face.Sans, 13), (Face.Sans, 14),
        (Face.SansMedium, 12), (Face.SansMedium, 13), (Face.SansMedium, 14),
        (Face.SansSemibold, 7), (Face.SansSemibold, 8), (Face.SansSemibold, 10), (Face.SansSemibold, 13), (Face.SansSemibold, 14), (Face.SansSemibold, 15),
        (Face.Mono, 9), (Face.Mono, 10), (Face.Mono, 11), (Face.Mono, 12), (Face.Mono, 13),
        (Face.MonoSemibold, 10), (Face.MonoSemibold, 11), (Face.MonoSemibold, 12), (Face.MonoSemibold, 13), (Face.MonoSemibold, 15), (Face.MonoSemibold, 16),
        (Face.MonoSemibold, 18), (Face.MonoSemibold, 30),
        (Face.Callout, 13), (Face.Callout, 16), (Face.Callout, 17), (Face.Callout, 22), (Face.Callout, 30),
    };

    /// <summary>The client is Japanese: callouts and sans words are Dalamud's font, and callouts aren't put in capitals.</summary>
    public bool Japanese { get; }

    private readonly IFontAtlas atlas;

    /// <summary>The fonts drawn with, at the plugin's scale now (<see cref="UiScale.Factor"/>).</summary>
    private FontSet current;

    /// <summary>
    /// The fonts being built at a new scale (Settings → Windows → Size, or a new screen): drawn with once every one of
    /// them is built, so the windows never show half their words at one size and half at the other.
    /// </summary>
    private FontSet? pending;

    /// <summary>The scale asked for and since when (seconds, ImGui's clock): a new build waits until it's held still.</summary>
    private (float Factor, double Since) asked = (-1, 0);

    /// <summary>How long a new scale is held before the fonts are built at it (a stepper pressed a few times builds once).</summary>
    private const double Settle = 0.4;

    /// <summary>The faces a theme draws in that the windows don't (<see cref="Want"/>).</summary>
    private HashSet<(Face, int)> themedWanted = new();

    private int generation;

    /// <summary>
    /// Counts the atlas's builds (and a theme's faces changing, and the fonts' scale moving): what was measured in a
    /// font before (the compact meter's columns) is measured again when it moves. Read from the draw thread; a build
    /// may step on another.
    /// </summary>
    public int Generation => Volatile.Read(ref generation);

    /// <param name="factor">The plugin's own scale to build the fonts at first (<see cref="UiScale.Factor"/>).</param>
    public Fonts(float factor)
    {
        atlas = Services.PluginInterface.UiBuilder.FontAtlas;
        atlas.BuildStepChange += OnBuildStep;
        Japanese = Services.ClientState.ClientLanguage == ClientLanguage.Japanese;
        current = new FontSet(atlas, factor, Japanese, themedWanted);
    }

    /// <summary>
    /// Each frame, before the windows draw: the scale the windows want (<see cref="UiScale.Wanted"/>). Once it's held
    /// still a moment and differs from the fonts', they're built again at it; once they're all built they're drawn
    /// with, the old ones let go, and <see cref="UiScale.Factor"/> moves with them (so the windows' sizes and their
    /// words move together, on one frame).
    /// </summary>
    public void Follow(float wanted, double now)
    {
        if (MathF.Abs(wanted - asked.Factor) > 0.001f) asked = (wanted, now);
        var target = pending?.Factor ?? current.Factor;
        if (MathF.Abs(asked.Factor - target) > 0.001f && now - asked.Since >= Settle)
        {
            // Back to the size drawn now (stepped there and back): nothing to build.
            pending?.Dispose();
            pending = MathF.Abs(asked.Factor - current.Factor) > 0.001f ? new FontSet(atlas, asked.Factor, Japanese, themedWanted) : null;
        }
        if (pending is not { Ready: true } done) return;
        current.Dispose();
        current = done;
        pending = null;
        Interlocked.Increment(ref generation);
        UiScale.Moved(current.Factor);
    }

    /// <summary>
    /// The faces a theme draws in, loaded (those the windows don't load already), and the faces of the
    /// theme before let go: a theme's fonts are in the atlas only while it's picked. Cheap when nothing changes.
    /// </summary>
    public void Want(IEnumerable<(Face Face, int Px)> faces)
    {
        var wanted = new HashSet<(Face, int)>();
        foreach (var f in faces)
            if (!current.Handles.ContainsKey(f))
                wanted.Add(f);
        if (wanted.SetEquals(themedWanted)) return;
        themedWanted = wanted;
        current.Theme(wanted);
        pending?.Theme(wanted);
        Interlocked.Increment(ref generation);
    }

    /// <summary>Pushes a face at a CSS size (the nearest loaded size when that one isn't).</summary>
    public IDisposable Push(Face face, int px) => Handle(face, px).Push();

    public IFontHandle Handle(Face face, int px) => current.Handle(face, px);

    /// <summary>A face's file and its line's ratio to its size.</summary>
    private static (string File, float Ratio) FileOf(Face face) => face switch
    {
        Face.Sans => ("Geist-Regular.ttf", GeistLine),
        Face.SansMedium => ("Geist-Medium.ttf", GeistLine),
        Face.SansSemibold => ("Geist-SemiBold.ttf", GeistLine),
        Face.Mono => ("GeistMono-Medium.ttf", GeistLine),
        Face.MonoSemibold => ("GeistMono-SemiBold.ttf", GeistLine),
        Face.Round => ("ZenMaruGothic-Bold.ttf", RoundLine),
        Face.RoundBlack => ("ZenMaruGothic-Black.ttf", RoundLine),
        Face.Serif => ("Fraunces14-Medium.ttf", SerifLine),
        Face.SerifSemibold => ("Fraunces14-SemiBold.ttf", SerifLine),
        _ => ("ArchivoExpanded-ExtraBold.ttf", ArchivoLine),
    };

    /// <summary>
    /// Every font the windows draw in, at one scale (the plugin's own, on top of Dalamud's, which the atlas applies
    /// itself): the faces at the sizes wanted, a theme's while it's picked, and the nearest loaded for a size that isn't.
    /// </summary>
    private sealed class FontSet : IDisposable
    {
        public readonly float Factor;

        /// <summary>Each (face, size) wanted, to its font; faces built alike (a Japanese client's) share one.</summary>
        public readonly Dictionary<(Face, int), IFontHandle> Handles = new();

        /// <summary>Each font built, once.</summary>
        private readonly List<IFontHandle> built = new();

        /// <summary>A theme's faces built while it's picked, each (face, size) to its font.</summary>
        private readonly Dictionary<(Face, int), IFontHandle> themed = new();

        /// <summary>A size asked for that isn't loaded, to the nearest that is (worked out once).</summary>
        private readonly Dictionary<(Face, int), IFontHandle> nearest = new();

        private readonly IFontAtlas atlas;

        public FontSet(IFontAtlas atlas, float factor, bool japanese, IEnumerable<(Face, int)> themedFaces)
        {
            this.atlas = atlas;
            Factor = factor;
            var alike = new Dictionary<(string File, float Size, bool Languages), IFontHandle>();
            foreach (var (face, px) in Wanted)
            {
                var (file, ratio) = FileOf(face);
                var mono = face is Face.Mono or Face.MonoSemibold;
                var dalamudFont = japanese && !mono && px >= 11;
                // Dalamud's font is drawn at Geist's line for its size, so it sits in the same line boxes.
                var size = (dalamudFont ? px * GeistLine : px * ratio) * factor;
                // Dalamud's font brings its language's glyphs itself; Geist and Archivo get them only where a plan's words go.
                var languages = !dalamudFont && Array.IndexOf(PlanText, (face, px)) >= 0;
                var key = (dalamudFont ? "Dalamud" : file, MathF.Round(size, 2), languages);
                if (!alike.TryGetValue(key, out var handle))
                {
                    handle = Make(atlas, file, size, dalamudFont, languages);
                    alike[key] = handle;
                    built.Add(handle);
                }
                Handles[(face, px)] = handle;
            }
            Theme(themedFaces);
        }

        /// <summary>Every font built (or failed: one that can't be is drawn as Dalamud's, not waited for for ever).</summary>
        public bool Ready
        {
            get
            {
                foreach (var h in built)
                    if (!h.Available && h.LoadException == null)
                        return false;
                foreach (var h in themed.Values)
                    if (!h.Available && h.LoadException == null)
                        return false;
                return true;
            }
        }

        /// <summary>A theme's faces built (those not already), the ones no longer wanted let go.</summary>
        public void Theme(IEnumerable<(Face, int)> faces)
        {
            var wanted = new HashSet<(Face, int)>(faces);
            foreach (var key in themed.Keys.ToList())
            {
                if (wanted.Contains(key)) continue;
                themed[key].Dispose();
                themed.Remove(key);
            }
            foreach (var (face, px) in wanted)
                if (!themed.ContainsKey((face, px)))
                    themed[(face, px)] = MakeThemed(face, px);
            nearest.Clear();
        }

        /// <summary>A theme's face: the game's through Dalamud, else a shipped file at its line's size.</summary>
        private IFontHandle MakeThemed(Face face, int px)
        {
            var family = face switch
            {
                Face.GameAxis => GameFontFamily.Axis,
                Face.GameNumbers => GameFontFamily.MiedingerMid,
                Face.GameTitle => GameFontFamily.Jupiter,
                _ => GameFontFamily.Undefined,
            };
            // The game's fonts at Geist's line for the size, so they sit in the same line boxes.
            if (family != GameFontFamily.Undefined) return atlas.NewGameFontHandle(new GameFontStyle(family, px * GeistLine * Factor));
            var (file, ratio) = FileOf(face);
            return Make(atlas, file, px * ratio * Factor, false, false);
        }

        public IFontHandle Handle(Face face, int px)
        {
            if (Handles.TryGetValue((face, px), out var h)) return h;
            if (themed.TryGetValue((face, px), out h)) return h;
            if (nearest.TryGetValue((face, px), out h)) return h;
            // A theme's face not loaded (its theme isn't picked): the windows' own nearest in spirit.
            var stand = face switch
            {
                Face.Round or Face.Serif or Face.GameAxis or Face.GameTitle => Face.SansMedium,
                Face.RoundBlack or Face.SerifSemibold => Face.SansSemibold,
                Face.GameNumbers => Face.MonoSemibold,
                _ => face,
            };
            var best = Handles.Keys.Where(k => k.Item1 == stand).OrderBy(k => Math.Abs(k.Item2 - px)).FirstOrDefault();
            h = Handles.TryGetValue(best, out var found) ? found : Handles[(Face.Sans, 14)];
            nearest[(face, px)] = h;
            return h;
        }

        public void Dispose()
        {
            // Each built font once (faces built alike share one).
            foreach (var h in built) h.Dispose();
            built.Clear();
            foreach (var h in themed.Values) h.Dispose();
            themed.Clear();
            Handles.Clear();
            nearest.Clear();
        }
    }

    private static IFontHandle Make(IFontAtlas atlas, string file, float sizePx, bool dalamudFont, bool languages) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            // Dalamud's font comes with its language's glyphs already (AddDalamudDefaultFont attaches them).
            var font = dalamudFont
                ? tk.AddDalamudDefaultFont(sizePx, null)
                : tk.AddFontFromMemory(Read(file), new SafeFontConfig { SizePx = sizePx }, file);
            // The game's own symbols (a glyph the font has already is kept), and the language Dalamud is
            // set to where a plan's own words are drawn.
            tk.AddGameSymbol(new SafeFontConfig { SizePx = sizePx, MergeFont = font });
            if (languages) tk.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx = sizePx, MergeFont = font });
        }));

    private static readonly Dictionary<string, byte[]> Files = new();

    private static byte[] Read(string file)
    {
        lock (Files)
        {
            if (Files.TryGetValue(file, out var cached)) return cached;
            var asm = typeof(Fonts).Assembly;
            var name = asm.GetManifestResourceNames().First(n => n.EndsWith(file, StringComparison.Ordinal));
            using var s = asm.GetManifestResourceStream(name)!;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return Files[file] = ms.ToArray();
        }
    }

    private void OnBuildStep(IFontAtlasBuildToolkit toolkit) => Interlocked.Increment(ref generation);

    public void Dispose()
    {
        atlas.BuildStepChange -= OnBuildStep;
        pending?.Dispose();
        pending = null;
        current.Dispose();
    }
}
