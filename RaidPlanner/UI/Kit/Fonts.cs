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
/// Dalamud's global scale and rebuild with it). Every (face, size) is its own font in the atlas,
/// so the set is fixed, and a size not here is drawn at the nearest one:
/// Geist 400 at 11, 12, 13, 14; 500 at 12, 13, 14; 600 at 7, 8, 10, 13, 14, 15;
/// Geist Mono 500 at 9, 10, 11, 12, 13; 600 at 10, 11, 12, 13, 15, 16, 18, 30;
/// Archivo Expanded ExtraBold at 13, 16, 17, 22.
///
/// Sizes are CSS sizes (the em). ImGui sizes a font by its line (ascent to descent), so each face is
/// loaded at its em times its line ratio, and 14px Geist here is as tall as 14px in the browser.
///
/// On a Japanese client the sans faces and the callout are Dalamud's own font (it has the kana and
/// kanji that duty, ability and plan names need; the callout isn't put in capitals there); the
/// mono keeps its face (it only shows numbers and the windows' own words).
/// </summary>
public sealed class Fonts : IDisposable
{
    /// <summary>(hhea ascent − descent) / em, read from the font files: Geist and Geist Mono 1.3, Archivo 1.088.</summary>
    private const float GeistLine = 1.3f;

    private const float ArchivoLine = 1.088f;

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

    private readonly Dictionary<(Face, int), IFontHandle> handles = new();

    /// <summary>The client is Japanese: callouts and sans words are Dalamud's font, and callouts aren't put in capitals.</summary>
    public bool Japanese { get; }

    public Fonts()
    {
        var atlas = Services.PluginInterface.UiBuilder.FontAtlas;
        Japanese = Services.ClientState.ClientLanguage == ClientLanguage.Japanese;
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
            var dalamudFont = Japanese && !mono;
            // Dalamud's font is drawn at Geist's line for its size, so it sits in the same line boxes.
            var size = dalamudFont ? px * GeistLine : px * ratio;
            // The mono only shows times, counts and the windows' own words: no other language's letters,
            // and the tiny spot labels need none either.
            handles[(face, px)] = Make(atlas, file, size, dalamudFont, !mono && px >= 11);
        }
    }

    /// <summary>Pushes a face at a CSS size (the nearest loaded size when that one isn't).</summary>
    public IDisposable Push(Face face, int px) => Handle(face, px).Push();

    public IFontHandle Handle(Face face, int px)
    {
        if (handles.TryGetValue((face, px), out var h)) return h;
        var best = handles.Keys.Where(k => k.Item1 == face).OrderBy(k => Math.Abs(k.Item2 - px)).FirstOrDefault();
        return handles.TryGetValue(best, out h) ? h : handles[(Face.Sans, 14)];
    }

    private static IFontHandle Make(IFontAtlas atlas, string file, float sizePx, bool dalamudFont, bool languages) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var font = dalamudFont
                ? tk.AddDalamudDefaultFont(sizePx, null)
                : tk.AddFontFromMemory(Read(file), new SafeFontConfig { SizePx = sizePx }, file);
            // The game's own symbols, and whatever Dalamud's language needs (ability names in any client's language).
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
        foreach (var h in handles.Values) h.Dispose();
        handles.Clear();
    }
}
