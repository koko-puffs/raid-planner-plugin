using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RaidPlanner.Core;

/// <summary>
/// The window's words for times, pulls and counts, worked out away from the game so they're
/// tested. English whatever the PC's language (the theme's fonts, and the site, are).
/// </summary>
public static class Words
{
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    /// <summary>A length of time as a fight's clock: "0:07", "6:42", "1:02:03".</summary>
    public static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var s = (long)Math.Floor(seconds);
        var h = s / 3600;
        var m = s / 60 % 60;
        var sec = s % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
    }

    /// <summary>When something happened: "today 21:04", "yesterday 21:04", "Mon 21:04" this week, "2 Nov 21:04", "2 Nov 2025" a year ago.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var day = local.Date;
        var time = local.ToString("HH:mm", En);
        if (day == today) return $"today {time}";
        if (day == today.AddDays(-1)) return $"yesterday {time}";
        if (day > today.AddDays(-7) && day < today) return $"{local.ToString("ddd", En)} {time}";
        if (day.Year == today.Year) return $"{local.ToString("d MMM", En)} {time}";
        return local.ToString("d MMM yyyy", En);
    }

    public static string OutcomeWord(Outcome o) => o switch
    {
        Outcome.Clear => "Cleared",
        Outcome.Wipe => "Wiped",
        _ => "Ended",
    };

    /// <summary>The outcome as the file says it.</summary>
    public static string OutcomeKey(Outcome o) => o switch
    {
        Outcome.Clear => "clear",
        Outcome.Wipe => "wipe",
        _ => "ended",
    };

    public static Outcome OutcomeFromKey(string? key) => key switch
    {
        "clear" => Outcome.Clear,
        "wipe" => Outcome.Wipe,
        _ => Outcome.Ended,
    };

    /// <summary>"Pull 3 · 6:42 · Wiped".</summary>
    public static string PullLine(int pull, double duration, Outcome outcome) =>
        $"Pull {Math.Max(1, pull)} · {Clock(duration)} · {OutcomeWord(outcome)}";

    /// <summary>"1 mechanic", "38 mechanics".</summary>
    public static string Count(int n, string one, string? many = null) =>
        n == 1 ? $"1 {one}" : $"{n.ToString("N0", En)} {many ?? one + "s"}";

    /// <summary>
    /// A duty's name for a file or a plan: the game's, its first letter up ("the Omega Protocol"
    /// becomes "The Omega Protocol"), spaces tidied; "Unknown duty" when there's none.
    /// </summary>
    public static string DutyName(string? name)
    {
        var s = Regex.Replace(name ?? string.Empty, @"\s+", " ").Trim();
        if (s.Length == 0) return "Unknown duty";
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>A pull's name, as it's sent and saved: "AAC Heavyweight M4 (Savage) · pull 3", 120 letters at most (the site's limit).</summary>
    public static string PullName(string duty, int pull)
    {
        var name = $"{DutyName(duty)} · pull {Math.Max(1, pull)}";
        return name.Length <= 120 ? name : name[..120].TrimEnd();
    }

    /// <summary>A safe file name from a pull: "AAC Heavyweight M4 (Savage) pull 3", without what Windows refuses.</summary>
    public static string FileStem(string duty, int pull, DateTimeOffset at)
    {
        var sb = new StringBuilder();
        foreach (var ch in DutyName(duty))
        {
            if (ch < 32 || "<>:\"/\\|?*".IndexOf(ch) >= 0) sb.Append(' ');
            else sb.Append(ch);
        }
        var name = Regex.Replace(sb.ToString(), @"\s+", " ").Trim().TrimEnd('.');
        if (name.Length > 60) name = name[..60].TrimEnd();
        if (name.Length == 0) name = "Pull";
        return $"{at.ToLocalTime().ToString("yyyy-MM-dd HHmm", En)} {name} pull {Math.Max(1, pull)}";
    }

    /// <summary>The ending every recording's file has.</summary>
    public const string Extension = ".raidrec.json";
}
