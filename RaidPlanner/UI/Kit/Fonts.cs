using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game;
using Dalamud.Interface.ManagedFontAtlas;

namespace RaidPlanner.UI;

/// <summary>Ready Check's three voices, by weight: the read (Geist), the count (Geist Mono), the callout (Archivo).</summary>
public enum Face
{
    /// <summary>Geist 400: what you read.</summary>
    Sans,

    /// <summary>Geist 500 (the site's 550): buttons, names in lists, tabs, a setting's name.</summary>
    SansMedium,

    /// <summary>Geist 600 (and the site's 650 and 700): small headings, the telegraph's words, a cast's name, a spot's label.</summary>
    SansSemibold,

    /// <summary>Geist Mono 500: eyebrows, tags, times and counts that line up.</summary>
    Mono,

    /// <summary>Geist Mono 600: the clock, values, the meter's numbers.</summary>
    MonoSemibold,

    /// <summary>Archivo Expanded ExtraBold (wdth 125, 800), in capitals: the callout, what a raid lead says.</summary>
    Callout,
}

/// <summary>
/// The faces at the sizes the windows use, loaded through Dalamud's font atlas (so they follow
/// Dalamud's global scale and rebuild with it). Every (face, size) is a font in the atlas (30 on
/// most clients, some 20,000 glyphs, a few million pixels at 100%), so the set is fixed, and a size
/// not here is drawn at the nearest one:
/// Geist 400 at 11, 12, 13, 14; 500 at 12, 13, 14; 600 at 7, 8, 10, 13, 14, 15;
/// Geist Mono 500 at 9, 10, 11, 12, 13; 600 at 10, 11, 12, 13, 15, 16, 18, 30;
/// Archivo Expanded ExtraBold at 13, 16, 17, 22.
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
/// </summary>
public sealed class Fonts : IDisposable
{
    /// <summary>(hhea ascent − descent) / em, read from the font files: Geist and Geist Mono 1.3, Archivo 1.088.</summary>
    private const float GeistLine = 1.3f;

    private const float ArchivoLine = 1.088f;

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
        (Face.Callout, 13), (Face.Callout, 16), (Face.Callout, 17), (Face.Callout, 22),
    };

    /// <summary>Each (face, size) wanted, to its font; faces built alike (a Japanese client's) share one.</summary>
    private readonly Dictionary<(Face, int), IFontHandle> handles = new();

    /// <summary>Each font built, once.</summary>
    private readonly List<IFontHandle> built = new();

    /// <summary>A size asked for that isn't loaded, to the nearest that is (worked out once).</summary>
    private readonly Dictionary<(Face, int), IFontHandle> nearest = new();

    /// <summary>The client is Japanese: callouts and sans words are Dalamud's font, and callouts aren't put in capitals.</summary>
    public bool Japanese { get; }

    public Fonts()
    {
        var atlas = Services.PluginInterface.UiBuilder.FontAtlas;
        Japanese = Services.ClientState.ClientLanguage == ClientLanguage.Japanese;
        var alike = new Dictionary<(string File, float Size, bool Languages), IFontHandle>();
        foreach (var (face, px) in Wanted)
        {
            var (file, ratio) = face switch
            {
                Face.Sans => ("Geist-Regular.ttf", GeistLine),
                Face.SansMedium => ("Geist-Medium.ttf", GeistLine),
                Face.SansSemibold => ("Geist-SemiBold.ttf", GeistLine),
                Face.Mono => ("GeistMono-Medium.ttf", GeistLine),
                Face.MonoSemibold => ("GeistMono-SemiBold.ttf", GeistLine),
                _ => ("ArchivoExpanded-ExtraBold.ttf", ArchivoLine),
            };
            var mono = face is Face.Mono or Face.MonoSemibold;
            var dalamudFont = Japanese && !mono && px >= 11;
            // Dalamud's font is drawn at Geist's line for its size, so it sits in the same line boxes.
            var size = dalamudFont ? px * GeistLine : px * ratio;
            // Dalamud's font brings its language's glyphs itself; Geist and Archivo get them only where a plan's words go.
            var languages = !dalamudFont && Array.IndexOf(PlanText, (face, px)) >= 0;
            var key = (dalamudFont ? "Dalamud" : file, MathF.Round(size, 2), languages);
            if (!alike.TryGetValue(key, out var handle))
            {
                handle = Make(atlas, file, size, dalamudFont, languages);
                alike[key] = handle;
                built.Add(handle);
            }
            handles[(face, px)] = handle;
        }
    }

    /// <summary>Pushes a face at a CSS size (the nearest loaded size when that one isn't).</summary>
    public IDisposable Push(Face face, int px) => Handle(face, px).Push();

    public IFontHandle Handle(Face face, int px)
    {
        if (handles.TryGetValue((face, px), out var h)) return h;
        if (nearest.TryGetValue((face, px), out h)) return h;
        var best = handles.Keys.Where(k => k.Item1 == face).OrderBy(k => Math.Abs(k.Item2 - px)).FirstOrDefault();
        h = handles.TryGetValue(best, out var found) ? found : handles[(Face.Sans, 14)];
        nearest[(face, px)] = h;
        return h;
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

    public void Dispose()
    {
        // Each built font once (faces built alike share one).
        foreach (var h in built) h.Dispose();
        built.Clear();
        handles.Clear();
        nearest.Clear();
    }
}
