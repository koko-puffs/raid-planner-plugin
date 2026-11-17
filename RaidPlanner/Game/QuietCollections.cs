using System;
using System.Runtime;

namespace RaidPlanner.Game;

/// <summary>
/// While a pull goes (recorded or followed), the runtime is asked not to stop the game for a full garbage collection
/// (<see cref="GCLatencyMode.SustainedLowLatency"/>: a full collection runs in the background instead), and set back as
/// it was when the pull ends. Since 0.7.9, after a 970 ms pause mid-pull in a trial: every plugin shares one heap (that
/// night 3.2 GB, mostly other plugins' files), and whichever allocates when it's due pays for all of it, the game with it.
/// <para>
/// The setting is the process's, so it's only taken when nothing else has asked for a low latency already, and only given
/// back if it's still ours. It can't stop a collection something asks for itself (<c>GC.Collect</c>), nor one the system
/// needs when it's short of memory; those are still said in /xllog (<see cref="Stalls"/>). It costs some memory while on
/// (the heap grows a little more before it's collected), never time.
/// </para>
/// </summary>
internal static class QuietCollections
{
    private static GCLatencyMode? before;

    /// <summary>A pull began: quiet collections until it ends (no-op when already on, or when something else set a low latency).</summary>
    public static void PullBegan()
    {
        try
        {
            if (before != null) return;
            var now = GCSettings.LatencyMode;
            if (now is GCLatencyMode.SustainedLowLatency or GCLatencyMode.LowLatency or GCLatencyMode.NoGCRegion) return;
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            // Not taken (concurrent collections off in this process): nothing to give back.
            if (GCSettings.LatencyMode == GCLatencyMode.SustainedLowLatency) before = now;
        }
        catch (Exception)
        {
            // The runtime didn't take it: collections as they were.
        }
    }

    /// <summary>The pull ended (or the plugin unloads): the latency as it was, if it's still ours.</summary>
    public static void PullEnded()
    {
        try
        {
            if (before is not { } was) return;
            before = null;
            if (GCSettings.LatencyMode == GCLatencyMode.SustainedLowLatency) GCSettings.LatencyMode = was;
        }
        catch (Exception)
        {
            // Left as it is.
        }
    }

    /// <summary>On during a pull (said with a long pause in /xllog).</summary>
    public static bool On => before != null;
}
