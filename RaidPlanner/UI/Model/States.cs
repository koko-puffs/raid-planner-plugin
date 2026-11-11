using System;
using System.Collections.Generic;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>What the plan window says when it has no plan to show.</summary>
public enum EmptyKind
{
    /// <summary>Not linked: the Boss asleep; Link your account (the telegraph).</summary>
    NotLinked,

    /// <summary>Reading your plans, or opening one: the party running over a cast bar.</summary>
    Reading,

    /// <summary>Something went wrong: the Boss awake; Try again (outlined).</summary>
    Wrong,

    /// <summary>No plans at all: the Boss asleep; Open the Raid Planner (outlined).</summary>
    NoPlans,

    /// <summary>In a duty with no plan for it: the Boss tilts its head; Pick a plan (the telegraph).</summary>
    NoPlanHere,

    /// <summary>Not in a duty: Pick a plan.</summary>
    Pick,
}

/// <summary>The plan window's empty state: its kind, its small callout, its one line, its button's words (none for reading).</summary>
public sealed record EmptyState(EmptyKind Kind, string Title, string Line, string? Button)
{
    /// <summary>The state from what the plans service knows, in the order it matters.</summary>
    public static EmptyState Of(bool linked, bool opening, bool listReading, string? why, int plans, bool inDuty)
    {
        if (!linked) return new(EmptyKind.NotLinked, "Your plans live on the site", "Link your Raid Planner account to see them here.", "Link your account");
        if (opening) return new(EmptyKind.Reading, "Opening the plan", string.Empty, null);
        if (listReading) return new(EmptyKind.Reading, "Reading your plans", string.Empty, null);
        if (why != null)
        {
            var (title, line) = Format.Headline(why);
            return new(EmptyKind.Wrong, title, line, "Try again");
        }
        if (plans == 0) return new(EmptyKind.NoPlans, "No plans yet", "Make one on the site, or from a pull you've sent.", "Open the Raid Planner");
        if (inDuty) return new(EmptyKind.NoPlanHere, "No plan for this duty", "Pick one of yours, or make one on the site.", "Pick a plan");
        return new(EmptyKind.Pick, "Pick a plan", "Pick one of your plans to show it here.", "Pick a plan");
    }
}

/// <summary>How far the step shown is towards the next, while a pull moves the plan (the plan window's slim cast bar).</summary>
public static class StepProgress
{
    /// <summary>
    /// When a step begins and the next one comes, in the plan's own time (its cues' times): its first
    /// cue (else the last cue of the steps before it, else 0), and the first cue of a later step after
    /// that (null when it's the last).
    /// </summary>
    public static (double? Start, double? Next) Window(IReadOnlyList<PlanCue>? cues, int step)
    {
        if (cues == null || cues.Count == 0) return (null, null);
        double? own = null;
        double? before = null;
        foreach (var c in cues)
        {
            if (c.Step == step) own = own is { } o ? Math.Min(o, c.T) : c.T;
            else if (c.Step < step) before = before is { } b ? Math.Max(b, c.T) : c.T;
        }
        var start = own ?? before ?? 0;
        double? next = null;
        foreach (var c in cues)
            if (c.Step > step && c.T > start)
                next = next is { } n ? Math.Min(n, c.T) : c.T;
        return (start, next);
    }

    /// <summary>The bar's fill (0–1) and the seconds until the next step, from the plan's clock now (null: not matched yet, nothing to show).</summary>
    public static (float Fraction, double? NextIn) Of(double? start, double? next, double? clock)
    {
        if (clock is not { } now || start is not { } s) return (0, null);
        if (next is not { } n) return (1, null);
        var span = n - s;
        if (span <= 0) return (1, null);
        return ((float)Math.Clamp((now - s) / span, 0, 1), Math.Max(0, n - now));
    }
}

/// <summary>What a failed send says and what its one button does (the mock-ups' "Every failure, said plainly").</summary>
public enum SendFix
{
    /// <summary>The token's gone: Link again (Settings, Account), the telegraph.</summary>
    LinkAgain,

    /// <summary>The site needs a newer plugin: Save as file until then.</summary>
    Update,

    /// <summary>Busy, a 500, no answer, no connection: Try again.</summary>
    TryAgain,

    /// <summary>It may be there already: Open the Raid Planner.</summary>
    Look,

    /// <summary>Too big, or it couldn't be written or read: Save as file.</summary>
    SaveFile,

    /// <summary>Stopped by you: nothing to say.</summary>
    Stopped,
}

/// <summary>A failed send, said plainly: its fix and its words.</summary>
public sealed record SendFailure(SendFix Fix, string Text)
{
    /// <summary>The plugin's reasons (Core/SiteLink's words), in five kinds, each with the one thing to do.</summary>
    public static SendFailure Of(SendResult r)
    {
        var why = r.Why ?? string.Empty;
        bool Has(string s) => why.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (r.TokenRefused || Has("token"))
            return new(SendFix.LinkAgain, "Couldn't send it. The site doesn't know this token any more: link again with a new one.");
        if (why == "Stopped.") return new(SendFix.Stopped, string.Empty);
        if (Has("may need updating") || Has("turned the plugin away") || Has("can't take pulls"))
            return new(SendFix.Update, "Couldn't send it. The site needs a newer plugin: update it in Dalamud's plugin installer.");
        if (Has("busy")) return new(SendFix.TryAgain, "The site is busy. Try again in a minute.");
        if (Has("had a problem") || Has("couldn't reach") || Has("connection"))
            return new(SendFix.TryAgain, "Couldn't reach the site. Check your connection, then try again.");
        if (Has("didn't answer in time") || Has("not with the pull") || Has("look in the raid planner"))
            return new(SendFix.Look, "The site may have it already. Look in the Raid Planner before sending it again.");
        if (Has("file couldn't be read")) return new(SendFix.SaveFile, "This pull's file couldn't be read.");
        if (Has("too big") || Has("couldn't be written"))
            return new(SendFix.SaveFile, "This pull is too big to send, even made smaller. Save it as a file and import it on the site.");
        if (Has("went wrong")) return new(SendFix.TryAgain, "Something went wrong sending it. The details are in /xllog.");
        // The site's own words for players (a full inbox, sending turned off): said as it says them.
        return new(SendFix.TryAgain, why.Length > 0 && !why.StartsWith("The site said no", StringComparison.Ordinal)
            ? $"Couldn't send it. {why}"
            : "Something went wrong sending it. The details are in /xllog.");
    }
}
