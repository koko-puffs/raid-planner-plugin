namespace RaidPlanner.Core;

/// <summary>
/// Settings → Recording → <i>Smoother replays</i> on by default since 0.7.6. Settings saved before it had it off unless
/// someone chose it, so it's turned on once (<see cref="Migrate"/>, version 1); turned off after that, it stays off.
/// Game-free, so it's tested.
/// </summary>
public static class SmoothDefault
{
    public const int Current = 1;

    /// <summary>The setting's version and value after bringing older settings up to date.</summary>
    public static (int Version, bool Smooth) Migrate(int version, bool smooth) =>
        version < Current ? (Current, true) : (version, smooth);
}
