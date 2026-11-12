using System;

namespace RaidPlanner.UI.Model;

/// <summary>/raidplan's words, read the same however they're typed (worked out away from the game so it's tested).</summary>
public static class CommandWords
{
    /// <summary>
    /// What follows /raidplan as the plugin matches it: in lower case, one space between words (tabs and
    /// runs of spaces are one), and the tracker's and the callouts' other names spelled as the plugin
    /// knows them ("cds" and "cooldown" are "cooldowns", "callout" is "callouts"), so
    /// <c>/raidplan  CDS   unlock</c> is <c>cooldowns unlock</c>.
    /// </summary>
    public static string Normalize(string? args)
    {
        if (string.IsNullOrWhiteSpace(args)) return string.Empty;
        var words = args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++) words[i] = words[i].ToLowerInvariant();
        words[0] = words[0] switch
        {
            "cds" or "cooldown" => "cooldowns",
            "callout" => "callouts",
            _ => words[0],
        };
        return string.Join(' ', words);
    }
}
