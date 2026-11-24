using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>Which way round a mechanic turns.</summary>
public enum Spin
{
    None,
    Cw,
    Ccw,
}

/// <summary>A rotating mechanic found in a pull (the site's model/rotation.ts's <c>Rotation</c>).</summary>
public sealed class ReplayRotation
{
    /// <summary>Its first step (or, from a marker alone, the mechanic's end, else the marker), and its last.</summary>
    public double T { get; set; }
    public double End { get; set; }

    /// <summary>The one turning (an actor's id; a party member's for a marker over a player).</summary>
    public int Src { get; init; }

    /// <summary>The mechanic it turns (0 when not known).</summary>
    public uint Action { get; init; }
    public Spin Dir { get; set; }

    /// <summary>Degrees a step (NaN when not measured).</summary>
    public float Step { get; init; } = float.NaN;
    public int Steps { get; init; } = 1;
    public float X { get; init; }
    public float Z { get; init; }
    public float Rot { get; init; }

    /// <summary>What says so: "steps", "marker" or "turns".</summary>
    public string From { get; init; } = "steps";

    /// <summary>When the marker that said which way came (NaN none).</summary>
    public double MarkedAt { get; set; } = double.NaN;
}

/// <summary>
/// The rotating mechanics in a pull (the site's model/rotation.ts): an enemy's attack going off three times or more
/// from one spot, each turned the same way by about the same step; a cast its caster turned through (plugin 0.7.4's
/// turns); a head marker or a status that says which way round. Pure.
/// </summary>
public static class ReplayRotations
{
    /// <summary>
    /// Head markers that say which way a mechanic turns, by the game's icon id (every IconID BossMod names RotateCW/CCW,
    /// TurnRight/Left, SpinCW/CCW…, as the site lists them).
    /// </summary>
    private static readonly uint[] CwIcons = { 133, 156, 167, 235, 484, 487, 493, 501, 540, 546, 564, 624, 644, 684, 689, 691, 692, 693, 694 };

    private static readonly uint[] CcwIcons = { 134, 157, 168, 236, 485, 490, 494, 502, 541, 547, 565, 625, 645, 685, 690, 695, 696, 697, 698 };

    /// <summary>A head marker's way round, or <see cref="Spin.None"/>.</summary>
    public static Spin IconSpin(uint icon) => Array.IndexOf(CwIcons, icon) >= 0 ? Spin.Cw : Array.IndexOf(CcwIcons, icon) >= 0 ? Spin.Ccw : Spin.None;

    /// <summary>Statuses whose param says which way something turns (FRU's Delight's Hourglass: 0x10D anticlockwise, 0x15C clockwise).</summary>
    private static Spin StatusSpin(uint status, int param) => status == 2970 ? param switch
    {
        0x10d => Spin.Ccw,
        0x15c => Spin.Cw,
        _ => Spin.None,
    } : Spin.None;

    private const float Deg = 180 / MathF.PI;
    private const double MaxGap = 4;
    private const float Stay = 1.5f;
    private const float MinStep = 2;
    private const float MaxStep = 120;
    private const double MarkerAhead = 20;
    private const double MarkerCast = 15;
    private const float MinTurn = 30;
    private const double MinTurnSpan = 1;

    /// <summary>A turn's way round: negative is clockwise.</summary>
    public static Spin SpinOf(float d) => d < 0 ? Spin.Cw : Spin.Ccw;

    private sealed record Landing(double T, uint Action, float X, float Z, float Rot)
    {
        public bool Mixed { get; set; }
    }

    /// <summary>The rotations in a pull, in time order; <paramref name="shapeOf"/> gives each action's shape.</summary>
    public static List<ReplayRotation> Find(Recording rec, Func<uint, ReplayShape?> shapeOf)
    {
        var party = rec.Party.Select(p => p.Id).ToHashSet();
        var pointless = new Dictionary<uint, bool>();
        bool IsPointless(uint a)
        {
            if (pointless.TryGetValue(a, out var v)) return v;
            var kind = shapeOf(a)?.Kind;
            v = kind is ShapeKind.Circle or ShapeKind.Donut or ShapeKind.Raidwide or ShapeKind.Target or ShapeKind.Knockback or ShapeKind.Gaze
                || ReplayShapes.IsAutoAttack(rec.Actions.TryGetValue(a, out var info) ? info.Name : null);
            pointless[a] = v;
            return v;
        }
        var events = rec.Events.OrderBy(e => e.T).ToList();

        /* ---------- each enemy's landings ---------- */
        var landings = new Dictionary<int, List<Landing>>();
        foreach (var h in events.OfType<HitEvent>())
        {
            if (party.Contains(h.Source) || h.Source <= 100 || IsPointless(h.Action)) continue;
            if (!landings.TryGetValue(h.Source, out var list)) landings[h.Source] = list = new List<Landing>();
            if (list.Count > 0 && h.T - list[^1].T <= 0.1)
            {
                if (MathF.Abs(ReplayShapes.Turn(list[^1].Rot, h.Rotation)) * Deg > MinStep) list[^1].Mixed = true;
                continue;
            }
            list.Add(new Landing(h.T, h.Action, h.X, h.Z, h.Rotation));
        }

        /* ---------- the steps ---------- */
        var outList = new List<ReplayRotation>();
        foreach (var (src, list) in landings)
        {
            var chain = new List<Landing>();
            void Close()
            {
                if (chain.Count >= 3) outList.Add(FromChain(src, chain));
                chain = new List<Landing>();
            }
            foreach (var l in list)
            {
                if (l.Mixed)
                {
                    Close();
                    continue;
                }
                if (chain.Count == 0)
                {
                    chain.Add(l);
                    continue;
                }
                var prev = chain[^1];
                var d = ReplayShapes.Turn(prev.Rot, l.Rot) * Deg;
                var near = l.T - prev.T <= MaxGap && l.T - prev.T > 0.1 && Dist(l.X, l.Z, chain[0].X, chain[0].Z) <= Stay;
                var sized = MathF.Abs(d) >= MinStep && MathF.Abs(d) <= MaxStep;
                var fits = near && sized;
                if (fits && chain.Count >= 2)
                {
                    var step = ReplayShapes.Turn(chain[0].Rot, chain[1].Rot) * Deg;
                    fits = MathF.Sign(d) == MathF.Sign(step) && MathF.Abs(MathF.Abs(d) - MathF.Abs(step)) <= MathF.Max(3, MathF.Abs(step) / 3)
                        && l.Action == chain[1].Action;
                }
                if (fits)
                {
                    chain.Add(l);
                    continue;
                }
                Close();
                // The last landing may begin the next one.
                chain = near && sized ? new List<Landing> { prev, l } : new List<Landing> { l };
            }
            Close();
        }

        /* ---------- turning through a cast (plugin 0.7.4) ---------- */
        var turnsOf = events.OfType<TurnEvent>().GroupBy(e => e.Actor).ToDictionary(g => g.Key, g => g.ToList());
        if (turnsOf.Count > 0)
            foreach (var e in events.OfType<CastEvent>())
            {
                if (party.Contains(e.Source) || (e.Target != 0 && party.Contains(e.Target))) continue;
                if (IsPointless(e.Action)) continue;
                var end = e.T + e.Duration;
                if (outList.Any(r => r.Src == e.Source && r.T <= end + 0.5 && r.End >= e.T)) continue;
                // Only while the cast bar is up.
                var list = turnsOf.TryGetValue(e.Source, out var all) ? all.Where(x => x.T >= e.T && x.T <= end).ToList() : new List<TurnEvent>();
                if (list.Count == 0) continue;
                var from = e.Rotation;
                var total = 0f;
                var back = 0;
                var turning = new List<double>();
                foreach (var x in list)
                {
                    var d = ReplayShapes.Turn(from, x.Rotation) * Deg;
                    total += d;
                    from = x.Rotation;
                    if (MathF.Sign(d) != MathF.Sign(total) && MathF.Abs(d) > 3) back++;
                    if (MathF.Abs(d) >= MinStep) turning.Add(x.T);
                }
                if (MathF.Abs(total) < MinTurn || back > 0) continue;
                if (turning.Count < 3 || turning[^1] - turning[0] < MinTurnSpan) continue;
                outList.Add(new ReplayRotation
                {
                    T = list[0].T,
                    End = list[^1].T,
                    Src = e.Source,
                    Action = e.Action,
                    Dir = SpinOf(total),
                    Steps = list.Count,
                    X = e.X,
                    Z = e.Z,
                    Rot = e.Rotation,
                    From = "turns",
                });
            }
        outList.Sort((a, b) => a.T.CompareTo(b.T));

        /* ---------- the markers (and the statuses that say so) ---------- */
        var cues = new List<(double T, int On, int Tgt, Spin Dir)>();
        foreach (var e in events)
        {
            if (e is MarkerEvent m && IconSpin(m.Icon) is var dir and not Spin.None) cues.Add((m.T, m.On, m.Target, dir));
            else if (e is StatusEvent { Gain: true, Refresh: false } s && StatusSpin(s.Id, s.Stacks) is var sd and not Spin.None) cues.Add((s.T, s.On, 0, sd));
        }
        var casts = events.OfType<CastEvent>().ToList();
        var claimed = new HashSet<ReplayRotation>();
        foreach (var e in cues)
        {
            var where = PlaceOf(rec, e.On, e.T);
            var r = outList.FirstOrDefault(x => !claimed.Contains(x) && x.T >= e.T - 1 && x.T <= e.T + MarkerAhead
                && (x.Src == e.On || (e.Tgt != 0 && x.Src == e.Tgt) || (where is { } w && Dist(x.X, x.Z, w.X, w.Z) <= 3)));
            if (r != null)
            {
                claimed.Add(r);
                r.MarkedAt = e.T;
                if (float.IsNaN(r.Step) && r.From == "turns") r.Dir = e.Dir;
                continue;
            }
            var next = casts.FirstOrDefault(c => c.Source == e.On && c.T + c.Duration >= e.T && c.T + c.Duration <= e.T + MarkerCast);
            var alone = new ReplayRotation
            {
                T = next != null ? next.T + next.Duration : e.T,
                End = next != null ? next.T + next.Duration : e.T,
                Src = e.On,
                Action = next?.Action ?? 0,
                Dir = e.Dir,
                X = next?.X ?? where?.X ?? 0,
                Z = next?.Z ?? where?.Z ?? 0,
                Rot = next?.Rotation ?? 0,
                From = "marker",
                MarkedAt = e.T,
            };
            claimed.Add(alone);
            outList.Add(alone);
        }
        outList.Sort((a, b) => a.T.CompareTo(b.T));
        return outList;
    }

    /// <summary>A chain of landings as a rotation: its median step, signed by the way round.</summary>
    private static ReplayRotation FromChain(int src, List<Landing> chain)
    {
        var steps = new List<float>();
        for (var i = 1; i < chain.Count; i++) steps.Add(ReplayShapes.Turn(chain[i - 1].Rot, chain[i].Rot) * Deg);
        var abs = steps.Select(MathF.Abs).OrderBy(x => x).ToList();
        var first = chain[0];
        return new ReplayRotation
        {
            T = first.T,
            End = chain[^1].T,
            Src = src,
            Action = first.Action,
            Dir = SpinOf(steps[0]),
            Step = MathF.Round(abs[abs.Count >> 1] * 10) / 10,
            Steps = chain.Count,
            X = first.X,
            Z = first.Z,
            Rot = first.Rot,
            From = "steps",
        };
    }

    /// <summary>Where someone was near a moment: their frame (within a second), else their own cast, hit or turn nearest it (within 20 s).</summary>
    private static (float X, float Z)? PlaceOf(Recording rec, int id, double t)
    {
        (float X, float Z)? best = null;
        var gap = double.PositiveInfinity;
        foreach (var f in rec.Frames)
            if (f.At.TryGetValue(id, out var p) && Math.Abs(f.T - t) < gap)
            {
                gap = Math.Abs(f.T - t);
                best = (p.X, p.Z);
            }
        if (gap <= 1) return best;
        foreach (var e in rec.Events)
        {
            (float X, float Z)? at = e switch
            {
                CastEvent c when c.Source == id => (c.X, c.Z),
                HitEvent h when h.Source == id => (h.X, h.Z),
                TurnEvent u when u.Actor == id => (u.X, u.Z),
                _ => null,
            };
            if (at != null && Math.Abs(e.T - t) < gap)
            {
                gap = Math.Abs(e.T - t);
                best = at;
            }
        }
        return gap <= 20 ? best : null;
    }

    private static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
}
