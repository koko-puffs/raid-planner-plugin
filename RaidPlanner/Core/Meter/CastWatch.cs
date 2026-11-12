using System;

namespace RaidPlanner.Core.Meter;

/// <summary>
/// Who stopped an enemy's cast. The game says that a cast was interrupted (actor control 15) but not
/// by whom, so it's inferred as ACT and FF Logs infer it: the party member whose action carried the
/// interrupt effect (type 76) on that enemy in the second before, else whose interrupt (Interject,
/// Head Graze, Low Blow, Leg Sweep: <see cref="CombatLists.InterruptNames"/>) hit it in that second.
/// Game-free, by whatever ids the caller uses (the recording's, or the game's entity ids for the
/// meter). Since 0.4.0.
/// </summary>
public sealed class CastWatch
{
    /// <summary>How long before the cast stopped an action can be what stopped it (seconds).</summary>
    public const double Window = 1.0;

    private readonly (double T, uint Target, uint Member, uint Action, bool Flagged)[] recent = new (double, uint, uint, uint, bool)[64];
    private int next;
    private int count;

    /// <summary>
    /// A party member's action on <paramref name="target"/>: kept when it carried the interrupt effect
    /// (<paramref name="flagged"/>) or is one of the interrupts (<paramref name="named"/>).
    /// </summary>
    public void Used(double t, uint target, uint member, uint action, bool flagged, bool named)
    {
        if (target == 0 || member == 0 || (!flagged && !named)) return;
        recent[next] = (t, target, member, action, flagged);
        next = (next + 1) % recent.Length;
        count = Math.Min(count + 1, recent.Length);
    }

    /// <summary>
    /// Who's credited with stopping <paramref name="target"/>'s cast at <paramref name="t"/>: the latest
    /// flagged action in the <see cref="Window"/> before, else the latest named one; null for no one.
    /// </summary>
    public (uint Member, uint Action)? Credit(double t, uint target)
    {
        (uint, uint)? named = null;
        double namedAt = double.NegativeInfinity, flaggedAt = double.NegativeInfinity;
        (uint, uint)? flagged = null;
        for (var i = 0; i < count; i++)
        {
            var r = recent[(next - 1 - i + recent.Length) % recent.Length];
            // A moment after too: the effect and the game's word can come in either order within a frame.
            if (r.Target != target || r.T > t + 0.1 || t - r.T > Window) continue;
            if (r.Flagged)
            {
                if (r.T > flaggedAt)
                {
                    flaggedAt = r.T;
                    flagged = (r.Member, r.Action);
                }
            }
            else if (r.T > namedAt)
            {
                namedAt = r.T;
                named = (r.Member, r.Action);
            }
        }
        return flagged ?? named;
    }

    public void Clear()
    {
        count = 0;
        next = 0;
    }
}

/// <summary>
/// The party's actions in the moments before a pull (a tincture at −2, a precast at −1.5, the shields
/// put up), which happen before the game's combat starts the recording: out of combat in a duty the
/// plugin records, the hooks stay on and this keeps the last <see cref="Keep"/> seconds of the party's
/// <see cref="PartyUse"/>s; at the pull's start the recorder copies them into the recording's
/// <c>combat.pre</c>. Game-free. Since 0.4.0.
/// </summary>
public sealed class PrePull : ICombatSink
{
    /// <summary>How long before the pull is kept (seconds): the file's <c>pre.from</c>.</summary>
    public const double Keep = 30;

    private readonly PartyUse[] ring = new PartyUse[256];
    private int next;
    private int count;

    public void Used(double t, uint member, uint caster, uint action)
    {
    }

    public void Effect(in PartyEffect e)
    {
    }

    public void Taken(in TakenHit e)
    {
    }

    public void Tick(in TickEvent e, ReadOnlySpan<TickShare> shares)
    {
    }

    public void Use(in PartyUse u)
    {
        ring[next] = u;
        next = (next + 1) % ring.Length;
        count = Math.Min(count + 1, ring.Length);
    }

    /// <summary>The uses in the <see cref="Keep"/> seconds up to <paramref name="start"/>, oldest first, their times from it (≤ 0).</summary>
    public PartyUse[] Before(double start)
    {
        var list = new System.Collections.Generic.List<PartyUse>();
        for (var i = 0; i < count; i++)
        {
            var u = ring[(next - count + i + ring.Length) % ring.Length];
            var dt = u.T - start;
            if (dt > 0 || dt < -Keep) continue;
            list.Add(u with { T = dt });
        }
        list.Sort((a, b) => a.T.CompareTo(b.T));
        return list.ToArray();
    }

    /// <summary>Forget them (a new zone, a pull begun, the duty not recorded).</summary>
    public void Clear()
    {
        count = 0;
        next = 0;
    }
}

/// <summary>
/// The game's countdown (the party's "/countdown"), watched each frame out of a pull: its length when
/// it began and the moment it reaches (or would reach) zero, so a pull's start can say how early or late
/// it was (<c>combat.cd</c>). Game-free: the game side hands over the agent's <c>Active</c> and
/// <c>TimeRemaining</c>. Since 0.4.0.
/// </summary>
public sealed class CountdownWatch
{
    /// <summary>Only a countdown that reached zero this close to the pull's start is the pull's (seconds).</summary>
    public const double Near = 30;

    private bool active;
    private double length;
    private double zeroAt = double.NaN;
    private double lastRemaining;

    /// <summary>This frame: the clock, whether a countdown is going, and the time it has left.</summary>
    public void Frame(double now, bool isActive, float remaining)
    {
        if (isActive && float.IsFinite(remaining))
        {
            if (!active)
            {
                // Its length as set (5–30 whole seconds): the first reading, a frame into it, rounded up.
                length = Math.Max(0, Math.Ceiling(remaining - 0.05));
                active = true;
            }
            zeroAt = now + remaining;
            lastRemaining = remaining;
            return;
        }
        if (active)
        {
            active = false;
            // Stopped well before zero: cancelled (the "countdown cancelled" message), not the pull's.
            if (lastRemaining > 1.0) zeroAt = double.NaN;
        }
    }

    /// <summary>The countdown that reached zero within <see cref="Near"/> of <paramref name="start"/>: its length, and start − zero (negative: combat began before zero).</summary>
    public (double Length, double End)? At(double start)
    {
        if (double.IsNaN(zeroAt) || Math.Abs(start - zeroAt) > Near) return null;
        return (length, Math.Round(start - zeroAt, 2, MidpointRounding.AwayFromZero));
    }

    public void Clear()
    {
        active = false;
        zeroAt = double.NaN;
    }
}
