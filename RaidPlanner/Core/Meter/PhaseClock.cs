using System;
using System.Collections.Generic;

namespace RaidPlanner.Core.Meter;

/// <summary>
/// Where the phases begin, from the enemies' targetability: a new phase each time every enemy that
/// had been targetable was gone for <see cref="Gap"/> seconds or more and then one became targetable
/// (again, or for the first time: a new boss). The recording's rule (<see cref="RecordingBuilder.Phase"/>)
/// and the site's (<c>phaseStarts</c>), so the meter, the summary and the site count the same.
/// Report every enemy each time (<see cref="Targetable"/>, then <see cref="EndFrame"/>).
/// </summary>
public sealed class PhaseClock
{
    public const double Gap = RecordingBuilder.PhaseGap;

    /// <summary>The most phase starts kept (the site's cap: 50 after the first).</summary>
    public const int MostPhases = 51;

    private readonly Dictionary<uint, bool> canTarget = new();
    private readonly HashSet<uint> seen = new();
    private readonly List<uint> gone = new();
    private int up;
    private double allGoneAt = -1;

    /// <summary>When each phase began, the first at 0.</summary>
    public List<double> Starts { get; } = new() { 0 };

    /// <summary>The phase now, from 1.</summary>
    public int Phase => Starts.Count;

    public void Targetable(double t, uint enemy, bool on)
    {
        if (enemy == 0) return;
        seen.Add(enemy);
        var known = canTarget.TryGetValue(enemy, out var was);
        if (known ? was == on : !on) return;
        canTarget[enemy] = on;
        if (on) Became(t);
        else Lost(t);
    }

    /// <summary>Every enemy reported: one targetable that wasn't (it left the game) is untargetable now.</summary>
    public void EndFrame(double t)
    {
        if (canTarget.Count > 0)
        {
            gone.Clear();
            foreach (var (enemy, on) in canTarget)
                if (on && !seen.Contains(enemy))
                    gone.Add(enemy);
            foreach (var enemy in gone)
            {
                canTarget[enemy] = false;
                Lost(t);
            }
        }
        seen.Clear();
    }

    private void Became(double t)
    {
        if (up == 0 && allGoneAt >= 0 && t - allGoneAt >= Gap && Starts.Count < MostPhases) Starts.Add(t);
        up++;
        allGoneAt = -1;
    }

    private void Lost(double t)
    {
        up = Math.Max(0, up - 1);
        if (up == 0 && allGoneAt < 0) allGoneAt = t;
    }

    /// <summary>The phase going on at a time (from 0) and when it began.</summary>
    public (int Index, double Start) At(double t)
    {
        var i = Starts.Count - 1;
        while (i > 0 && Starts[i] > t) i--;
        return (i, Starts[i]);
    }
}
