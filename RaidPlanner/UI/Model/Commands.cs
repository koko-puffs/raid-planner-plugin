using System;

namespace RaidPlanner.UI.Model;

/// <summary>/raidcodex's words, read the same however they're typed (worked out away from the game so it's tested).</summary>
public static class CommandWords
{
    /// <summary>The command (0.5.1; the plugin was Raid Planner before it was Raid Codex).</summary>
    public const string Main = "/raidcodex";

    /// <summary>The command before 0.5.1: it still works, the same as <see cref="Main"/>.</summary>
    public const string Old = "/raidplan";

    /// <summary>Every command the plugin answers, the main one first; each does the same.</summary>
    public static readonly string[] Names = [Main, Old];

    /// <summary>What Dalamud's command list says for the main command.</summary>
    public const string MainHelp =
        "Open Raid Codex. /raidcodex plan shows your plan, /raidcodex next and /raidcodex prev move its step; "
        + "/raidcodex meter shows or hides the meter, /raidcodex meter unlock gives it the mouse back, /raidcodex meter preview shows a sample fight in it; "
        + "/raidcodex cooldowns shows or hides the party cooldown tracker, /raidcodex callouts the on-screen callouts (each with unlock, preview and settings after it); "
        + "/raidcodex send last sends the newest pull; /raidcodex stop ends the pull being recorded (and keeps it); /raidcodex settings opens Settings. "
        + "/raidplan still works.";

    /// <summary>What it says for the old one.</summary>
    public const string OldHelp = "The same as /raidcodex (Raid Codex was Raid Planner): /raidplan still works.";

    /// <summary>Dalamud's help line for one of <see cref="Names"/>.</summary>
    public static string HelpFor(string command) => command == Old ? OldHelp : MainHelp;

    /// <summary>
    /// What follows /raidcodex as the plugin matches it: in lower case, one space between words (tabs and
    /// runs of spaces are one), and the tracker's and the callouts' other names spelled as the plugin
    /// knows them ("cds" and "cooldown" are "cooldowns", "callout" is "callouts"), so
    /// <c>/raidcodex  CDS   unlock</c> is <c>cooldowns unlock</c>.
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
