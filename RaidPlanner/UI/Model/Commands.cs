using System;

namespace RaidPlanner.UI.Model;

/// <summary>/raidcodex's words, read the same however they're typed (worked out away from the game so it's tested).</summary>
public static class CommandWords
{
    /// <summary>The command (0.5.1; the plugin was Raid Planner before it was Raid Codex).</summary>
    public const string Main = "/raidcodex";

    /// <summary>The short command (0.7.4): every /raidcodex command works with /rc in its place.</summary>
    public const string Short = "/rc";

    /// <summary>The command before 0.5.1: it still works, the same as <see cref="Main"/>.</summary>
    public const string Old = "/raidplan";

    /// <summary>Every command the plugin answers, the main one first; each does the same.</summary>
    public static readonly string[] Names = [Main, Short, Old];

    /// <summary>What Dalamud's command list says for the main command: the three to know, the rest in /raidcodex help.</summary>
    public const string MainHelp =
        "Open Raid Codex. /raidcodex settings opens Settings; /raidcodex help lists every command. /rc works the same as /raidcodex.";

    /// <summary>What it says for the short one and the old one (neither is listed: Dalamud's list shows the main one only).</summary>
    public const string ShortHelp = "The same as /raidcodex: /rc settings, /rc help.";

    public const string OldHelp = "The same as /raidcodex (Raid Codex was Raid Planner): /raidplan still works.";

    /// <summary>Dalamud's help line for one of <see cref="Names"/>.</summary>
    public static string HelpFor(string command) => command == Old ? OldHelp : command == Short ? ShortHelp : MainHelp;

    /// <summary>Whether Dalamud's command list shows it: the main command only, so the list stays short.</summary>
    public static bool Listed(string command) => command == Main;

    /// <summary>
    /// What /raidcodex help writes in chat, a line each: what follows /raidcodex, and what it does. Each
    /// works after /rc too.
    /// </summary>
    public static readonly (string Words, string Does)[] Help =
    [
        ("", "opens or closes the window"),
        ("settings", "opens Settings"),
        ("stop", "ends the pull being recorded, and keeps it"),
        ("send last", "sends the newest pull to Raid Codex"),
        ("plan", "shows or hides the plan window"),
        ("next / prev", "moves the plan a step on or back"),
        ("meter", "shows or hides the meter"),
        ("meter unlock", "gives the meter the mouse back"),
        ("meter big", "opens the meter up"),
        ("meter preview", "shows a sample fight in the meter (again to stop)"),
        ("meter settings", "opens Settings on the meter"),
        ("cooldowns", "shows or hides the party cooldown tracker"),
        ("callouts", "shows or hides the on-screen callouts"),
        ("cooldowns / callouts unlock, preview, settings", "gives it the mouse back, shows its sample, opens its settings"),
        ("arrange", "shows every window that's on, to drag where you want (Done locks them)"),
        ("help", "this list"),
    ];

    /// <summary>The help as the chat lines it's written as: a head, then a line per command.</summary>
    public static string[] HelpLines()
    {
        var lines = new string[Help.Length + 1];
        lines[0] = "Commands (each works with /rc in place of /raidcodex):";
        for (var i = 0; i < Help.Length; i++)
        {
            var (words, does) = Help[i];
            lines[i + 1] = $"{Main}{(words.Length > 0 ? " " + words : string.Empty)}: {does}";
        }
        return lines;
    }

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
