using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RaidPlanner.Game;

/// <summary>
/// The plugin's own time on the framework thread, watched: what the game waits on each frame (the
/// recorder's frame, the meter's, the plan's, the windows' drawing, the game's messages read by the
/// hooks). A part that takes longer than <see cref="SlowMs"/> in one frame is written to /xllog as a
/// warning (each part at most every few seconds), and so is a garbage collection that paused the
/// process that long (the runtime's, shared with every plugin: it says so). Since 0.7.6, after a
/// player's report of dropped inputs and stutters in a long ultimate: so the next report says where
/// the time went. Off the framework thread nothing's measured (the game doesn't wait on a worker).
/// </summary>
internal static class Stalls
{
    /// <summary>More than this in one frame (ms) is said: a frame at 60 fps is 16.7 ms all told.</summary>
    public const double SlowMs = 20;

    /// <summary>How often each part may be said (ms): a stutter that lasts says so once in a while, not every frame.</summary>
    private const long Every = 5000;

    private static readonly Dictionary<string, long> said = new();
    private static TimeSpan paused = GC.GetTotalPauseDuration();
    private static int gen2 = GC.CollectionCount(2);

    /// <summary>Now, for <see cref="End"/>.</summary>
    public static long Start() => Stopwatch.GetTimestamp();

    /// <summary>A part's time since <paramref name="started"/>, said when it was slow.</summary>
    public static void End(string part, long started) => Took(part, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

    /// <summary>A part's time (ms), said when it was slow.</summary>
    public static void Took(string part, double ms)
    {
        if (ms < SlowMs) return;
        if (!Due(part)) return;
        Services.Log.Warning($"Raid Codex: {part} took {ms:0} ms in one frame (the game waits on it)");
    }

    /// <summary>Once a frame: a garbage collection that paused the process for long since the last frame, said.</summary>
    public static void CheckCollections()
    {
        var now = GC.GetTotalPauseDuration();
        var ms = (now - paused).TotalMilliseconds;
        paused = now;
        var g2 = GC.CollectionCount(2);
        gen2 = g2;
        if (ms < SlowMs || !Due("gc")) return;
        // What kind (since 0.7.9): a full one that stopped everything, whether it compacted, how much of the heap was
        // gaps, and whether a pull had collections quiet (QuietCollections: then only one something asked for, or one
        // the system needed, stops the game).
        var info = GC.GetGCMemoryInfo(GCKind.Any);
        var kind = info.Generation >= 2 ? (info.Concurrent ? "a full background collection" : "a full blocking collection") : $"a generation {info.Generation} collection";
        var mb = (long b) => b / (1024 * 1024);
        Services.Log.Warning($"Raid Codex: garbage collection paused the game for {ms:0} ms ({kind}{(info.Compacted ? ", compacting" : "")}; the process's, every plugin's together; managed memory {GC.GetTotalMemory(false) / (1024 * 1024)} MB, {mb(info.FragmentedBytes)} MB of it gaps; quiet collections {(QuietCollections.On ? "on (a pull)" : "off")})");
    }

    private static bool Due(string part)
    {
        var t = Environment.TickCount64;
        lock (said)
        {
            if (said.TryGetValue(part, out var last) && t - last < Every) return false;
            said[part] = t;
            return true;
        }
    }
}
