using System;
using System.Globalization;

namespace RaidPlanner.UI.Model;

/// <summary>
/// How big the plugin's windows are drawn (since 0.8.1; Settings → Windows → Size): every window, its words, its
/// icons and its spacing together, on top of Dalamud's own scale (its Global Font Scale). Fit to my screen (the
/// default) makes them bigger on a screen taller than 1440 px, as tall as the screen is over 1440 (150% on 4K, 200%
/// at 2880), unless Dalamud's own scale already is that big; else a size you pick, 80% to 200% of Dalamud's. Pure,
/// so it's tested.
/// </summary>
public static class WindowSize
{
    /// <summary><see cref="Configuration.WindowSize"/> for Fit to my screen.</summary>
    public const int Fit = 0;

    /// <summary>The sizes you can pick (%), in steps of <see cref="Step"/>.</summary>
    public const int Smallest = 80;

    public const int Largest = 200;
    public const int Step = 10;

    /// <summary>The screen height (px) the windows are drawn at their own size on; taller screens get bigger ones.</summary>
    public const float Reference = 1440;

    /// <summary>Fit to my screen grows them up to this (on a screen 2880 px tall or taller).</summary>
    public const float FitMost = 2;

    /// <summary>
    /// The plugin's own scale (on top of Dalamud's): the size picked, or, fitting the screen, what makes the windows as
    /// big as the screen wants (<see cref="ForScreen"/>) where Dalamud's scale doesn't already. A size not in
    /// steps of 5% is rounded to one, so fonts are built at a few sizes, not a new one for every pixel of a screen.
    /// </summary>
    /// <param name="setting"><see cref="Configuration.WindowSize"/>: <see cref="Fit"/>, else the size picked (%).</param>
    /// <param name="screenHeight">The game's screen height (px); 0 or less: not known (fitting, the plugin's own size).</param>
    /// <param name="dalamudScale">Dalamud's global scale.</param>
    public static float Factor(int setting, float screenHeight, float dalamudScale)
    {
        if (setting != Fit) return Clamp(setting) / 100f;
        if (dalamudScale <= 0 || float.IsNaN(dalamudScale)) dalamudScale = 1;
        var wanted = ForScreen(screenHeight) / dalamudScale;
        return MathF.Max(1, MathF.Round(wanted * 20) / 20);
    }

    /// <summary>How big a screen this tall wants the windows, over all (1 up to 1440 px, then its height over 1440, at most 2), in steps of 10%.</summary>
    public static float ForScreen(float screenHeight)
    {
        if (!(screenHeight > Reference)) return 1;
        var f = MathF.Min(FitMost, screenHeight / Reference);
        return MathF.Round(f * 10) / 10;
    }

    /// <summary>A size picked (%), kept to <see cref="Smallest"/>–<see cref="Largest"/> in steps of <see cref="Step"/>.</summary>
    public static int Clamp(int percent)
    {
        var p = Math.Clamp(percent, Smallest, Largest);
        return (int)MathF.Round(p / (float)Step) * Step;
    }

    /// <summary>The size picked one step up (+1) or down (−1); fitting, the step starts from the size fitting gives now.</summary>
    public static int Stepped(int setting, int by, float factorNow)
    {
        var from = setting == Fit ? (int)MathF.Round(factorNow * 100) : setting;
        return Clamp((int)(MathF.Round(from / (float)Step) * Step) + by * Step);
    }

    /// <summary>A size as Settings shows it: "150%".</summary>
    public static string Words(float factor) => ((int)MathF.Round(factor * 100)).ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// /raidcodex size's words: "fit" (or "auto") fits the screen; a number (with or without %) is the size picked,
    /// kept to 80–200 in steps of 10. Null: not a size.
    /// </summary>
    public static int? Parse(string? words)
    {
        if (string.IsNullOrWhiteSpace(words)) return null;
        var w = words.Trim().ToLowerInvariant();
        if (w is "fit" or "auto" or "screen") return Fit;
        if (w.EndsWith('%')) w = w[..^1].TrimEnd();
        return int.TryParse(w, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p > 0 ? Clamp(p) : null;
    }
}
