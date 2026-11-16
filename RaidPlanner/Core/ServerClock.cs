using System;

namespace RaidPlanner.Core;

/// <summary>
/// The game server's clock to the millisecond (since 0.6.0, a recording's <c>startedAt</c>). The game gives its server's time
/// only in whole seconds; watched each frame, the moment it ticks over to the next second is that second's start on this
/// computer's clock (within a frame), so the server's time is the computer's plus the offset seen then. Until a tick over has
/// been seen (or after the server's time jumps), it's the whole second the game gives. Game-free; fed on the framework thread.
/// </summary>
public sealed class ServerClock
{
    private long lastSecond;
    private double? offsetMs;

    /// <summary>The offset is known (a tick over has been seen).</summary>
    public bool Fine => offsetMs != null;

    /// <summary>The server's whole second (Unix) and the computer's clock now, each frame (or as often as it can be).</summary>
    public void Observe(long serverSecond, DateTimeOffset local)
    {
        if (serverSecond <= 0) return;
        if (lastSecond != 0 && serverSecond == lastSecond + 1)
            offsetMs = serverSecond * 1000.0 - local.ToUnixTimeMilliseconds();
        else if (lastSecond != 0 && serverSecond != lastSecond)
            // A jump (a hitch of more than a second, the clock changed): the offset is seen again at the next tick over.
            offsetMs = null;
        lastSecond = serverSecond;
    }

    /// <summary>
    /// The server's time now: the computer's clock and the offset, when it's known and agrees with the server's second (else the
    /// second itself); null before the server's time has been seen.
    /// </summary>
    public DateTimeOffset? Now(long serverSecond, DateTimeOffset local)
    {
        if (serverSecond <= 0) return null;
        var whole = serverSecond * 1000.0;
        if (offsetMs is { } off)
        {
            var fine = local.ToUnixTimeMilliseconds() + off;
            // Within the second the server says (a frame's slack either side): the fine time.
            if (fine >= whole - 50 && fine < whole + 1050) return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(Math.Clamp(fine, whole, whole + 999)));
        }
        return DateTimeOffset.FromUnixTimeMilliseconds((long)whole);
    }
}
