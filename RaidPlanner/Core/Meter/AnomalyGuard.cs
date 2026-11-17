namespace RaidPlanner.Core.Meter;

/// <summary>
/// When the meter stops itself for nonsense: the parser's dropped effects (<see cref="CombatParser.Anomalies"/>,
/// a hit or a heal too big to be real) are the sign that a patch has moved what it reads, and a moved layout gives nonsense
/// on nearly every hit, so it shows within one pull. A stray one now and then mustn't add up over a long night, so they're
/// counted by stretch: from the meter starting to listen, the end of an encounter or a zone change, to the next. Only those
/// dropped while the meter listens count (the parser runs for the recorder without it too).
///
/// <see cref="PerStretch"/> in one stretch stops the meter until the next zone, where it tries again; the
/// <see cref="MostStops"/>th stop of the session is for good (until the plugin is reloaded), so a layout that's really
/// moved can't keep coming back. Game-free (the meter's game side asks it each frame), so it's tested.
/// </summary>
public sealed class AnomalyGuard
{
    /// <summary>Nonsense in one stretch that stops the meter.</summary>
    public const int PerStretch = 3;

    /// <summary>Stops in a session after which the meter stays stopped.</summary>
    public const int MostStops = 3;

    /// <summary>The parser's count when the stretch began.</summary>
    private int from;

    /// <summary>How many times the meter has stopped this session.</summary>
    public int Stops { get; private set; }

    /// <summary>The meter is stopped (until the next zone, or for good: <see cref="ForGood"/>).</summary>
    public bool Stopped { get; private set; }

    /// <summary>The meter is stopped until the plugin is reloaded.</summary>
    public bool ForGood => Stopped && Stops >= MostStops;

    /// <summary>A new stretch begins (the meter starts listening, an encounter has ended): counted from the parser's count now.</summary>
    public void Restart(int anomalies) => from = anomalies;

    /// <summary>
    /// Must the meter stop, with the parser's count as it is now? True from the moment a stretch reaches
    /// <see cref="PerStretch"/> until the stop is lifted (<see cref="NewZone"/>); a stop is counted once.
    /// </summary>
    public bool Check(int anomalies)
    {
        if (Stopped) return true;
        // The parser's count went back (a new parser): counted from there.
        if (anomalies < from) from = anomalies;
        if (anomalies - from < PerStretch) return false;
        Stopped = true;
        Stops++;
        from = anomalies;
        return true;
    }

    /// <summary>A new zone: a new stretch, and a stop that isn't for good is lifted. True when one was.</summary>
    public bool NewZone(int anomalies)
    {
        from = anomalies;
        if (!Stopped || Stops >= MostStops) return false;
        Stopped = false;
        return true;
    }
}
