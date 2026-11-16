using System;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using RaidPlanner.Core;

namespace RaidPlanner.Game;

/// <summary>
/// The game's version and its server's clock (since 0.6.0: a recording's <c>game</c> and <c>startedAt</c>, for the site's logs:
/// the patch a pull was fought on, and two players' recordings of one pull told from two pulls). Each guarded: null when it
/// can't be read [verify: <c>Framework.GameVersionString</c> as "2025.07.17.0000.0000"; <c>Framework.GetServerTime</c> as Unix
/// seconds, ticking over with the server]. On the framework thread.
/// </summary>
public static unsafe class GameClock
{
    /// <summary>The game's version ("2025.07.17.0000.0000"), read once; null when it can't be.</summary>
    public static string? Version()
    {
        if (version != null) return version.Length == 0 ? null : version;
        try
        {
            var fw = Framework.Instance();
            var text = fw == null ? null : fw->GameVersionString;
            version = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }
        catch (Exception ex)
        {
            version = string.Empty;
            Services.Log.Warning(ex, "Raid Codex couldn't read the game's version");
        }
        return version.Length == 0 ? null : version;
    }

    private static string? version;

    private static readonly ServerClock clock = new();
    private static bool broken;

    /// <summary>Each frame: the server's second against the computer's clock (<see cref="ServerClock"/>), for the milliseconds.</summary>
    public static void Observe()
    {
        if (broken) return;
        try
        {
            clock.Observe(Framework.GetServerTime(), DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            broken = true;
            Services.Log.Warning(ex, "Raid Codex couldn't read the server's time");
        }
    }

    /// <summary>The game server's time now (to the millisecond once a second's tick has been seen), or null when it can't be read or makes no sense.</summary>
    public static DateTimeOffset? ServerTime()
    {
        if (broken) return null;
        try
        {
            var seconds = Framework.GetServerTime();
            // Before 2020 or more than a day off the computer's clock: not a time the server gave.
            if (seconds < 1_577_836_800) return null;
            var now = DateTimeOffset.UtcNow;
            clock.Observe(seconds, now);
            var at = clock.Now(seconds, now);
            return at is { } a && Math.Abs((a - now).TotalDays) <= 1 ? a : null;
        }
        catch (Exception ex)
        {
            broken = true;
            Services.Log.Warning(ex, "Raid Codex couldn't read the server's time");
            return null;
        }
    }
}
