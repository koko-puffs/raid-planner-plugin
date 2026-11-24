using System;
using System.Collections.Generic;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>World (yalms) to the board (the site's <c>BoardFrame</c>): the world point at the board's centre, and board units a yalm.</summary>
public readonly record struct BoardFrame(float Cx, float Cz, float Upy)
{
    /// <summary>A board from an arena: its centre, and its ±420 spanning its yalms.</summary>
    public static BoardFrame Of(PlanArena arena, ArenaChoice fallback) =>
        new(arena.Center?.X ?? fallback.Cx, arena.Center?.Z ?? fallback.Cz, 840f / MathF.Max(5, arena.Yalms));

    public float BoardX(float x) => (x - Cx) * Upy;

    public float BoardY(float z) => (z - Cz) * Upy;
}

/// <summary>
/// A mechanic drawn on the board at a moment (the site's board object for it, `replay.ts`'s `castObjects`), in board
/// units: what it is (the plan board's kinds), where, turned how (degrees, 0 north, clockwise), its size (a radius),
/// width, length, a donut's inner radius, a cone's angle, its colour (the site's palette's) and how bright it's built
/// up so far (0.3 faint as it begins, 1 as it goes off). <see cref="Variant"/>: a buster's cleave, a rotation's "ccw".
/// </summary>
public struct Tele
{
    public BoardKind Kind;
    public float X;
    public float Y;
    public float Rot;
    public float Size;
    public float Width;
    public float Length;
    public float Inner;
    public float Angle;
    public uint Color;
    public float Bright;
    public string? Variant;
}

/// <summary>A tether at a moment, in board units.</summary>
public struct TetherNow
{
    public float X1;
    public float Y1;
    public float X2;
    public float Y2;
}

/// <summary>A head marker's badge at a moment: over whom (their entity's index), its kind, a limit cut's number.</summary>
public struct BadgeNow
{
    public int Entity;
    public MarkerMeaning Kind;
    public MechRole Role;
    public int Number;
}

/// <summary>A status on someone now (the site's <c>StatusNow</c>): its row, name, icon, category, stacks, seconds left (NaN to the end), the share of its time left.</summary>
public struct StatusNow
{
    public uint Id;
    public string Name;
    public uint Icon;
    public int Cat;
    public int Stacks;
    public float Left;
    public float Share;
}

/// <summary>
/// One moment of a replay (the site's components/review/replay.ts and useReplay.ts's moment): everyone's place, what
/// the board draws (each cast's telegraph built up by brightness, the hits that came with no cast as they land, a
/// marker drawn as the mechanic it led to, the rotations, the tethers), who's dead and since when, raised or hit just
/// now, whom each enemy is on, the head markers' badges, the fight's statuses on each one. Its lists are kept and
/// filled again each moment (<see cref="At"/>): nothing is allocated as it plays.
/// </summary>
public sealed class ReplayScene
{
    /// <summary>How long a telegraph stays once it's gone off (it flashes, then goes), seconds.</summary>
    public const double Linger = 0.45;

    /// <summary>How long a head marker is shown.</summary>
    public const double MarkerFor = 5;

    /// <summary>How long a raise's ring and a hit's flash last.</summary>
    public const double RaisedFor = 1.6;

    public const double HitFor = 0.4;

    public const uint Aoe = 0xff8a3d;
    public const uint StackColor = 0xffcf4a;
    public const uint SpreadColor = 0xb46cff;
    public const uint DefamationColor = 0xff5fa2;
    public const uint BusterColor = 0xff4f4f;
    public const uint TowerColor = 0x7aa2ff;
    public const uint GazeColor = 0xe8383d;

    public ReplayTrack Track { get; }
    public BoardFrame Frame { get; }

    /// <summary>The moment last worked out.</summary>
    public double T { get; private set; } = double.NaN;

    /// <summary>Everyone now, by entity index.</summary>
    public ReplayPoint[] Points { get; }

    public List<Tele> Teles { get; } = new();
    public List<TetherNow> TethersNow { get; } = new();
    public List<BadgeNow> Badges { get; } = new();

    /// <summary>Since when each one has been dead (by entity index; NaN alive).</summary>
    public double[] DeadSince { get; }

    /// <summary>Seconds since each was raised (NaN not just now) and since a hit landed on them (NaN none just now).</summary>
    public double[] Raised { get; }

    public double[] HitAgo { get; }

    /// <summary>Whom each enemy is on now (an id; 0 none), by entity index.</summary>
    public int[] TargetOf { get; }

    /// <summary>The fight's statuses on each one now (the soonest gone first), by entity index.</summary>
    public List<StatusNow>[] StatusesOn { get; }

    /// <summary>The waymarks now (null none).</summary>
    public ReplayWaymarks? WaymarksNow { get; private set; }

    /// <summary>The head signs now (null none).</summary>
    public ReplaySigns? SignsNow { get; private set; }

    /// <summary>Each rotation's first moment (its marker, or the cast it follows, else just before its first step).</summary>
    private readonly double[] rotationFrom;

    private readonly double[] lastRaise;
    private readonly double[] lastHit;
    private readonly int[] badgeAt;

    public ReplayScene(ReplayTrack track, BoardFrame frame)
    {
        Track = track;
        Frame = frame;
        var n = track.Entities.Count;
        Points = new ReplayPoint[n];
        DeadSince = new double[n];
        Raised = new double[n];
        HitAgo = new double[n];
        TargetOf = new int[n];
        lastRaise = new double[n];
        lastHit = new double[n];
        badgeAt = new int[n];
        StatusesOn = new List<StatusNow>[n];
        for (var i = 0; i < n; i++) StatusesOn[i] = new List<StatusNow>(4);
        rotationFrom = new double[track.Rotations.Count];
        for (var i = 0; i < rotationFrom.Length; i++)
        {
            var r = track.Rotations[i];
            var lead = r.T - 0.5;
            foreach (var c in track.Casts)
                if (c.Src == r.Src && c.Action == r.Action && Math.Abs(c.End - r.T) <= 1)
                {
                    lead = c.T;
                    break;
                }
            rotationFrom[i] = Math.Min(double.IsNaN(r.MarkedAt) ? lead : r.MarkedAt, lead);
        }
    }

    /// <summary>How far through its cast a telegraph is, 0–1 (1 once it's gone off, for <see cref="Linger"/>); −1 when not on.</summary>
    public static float CastProgress(double start, double end, bool stopped, double t)
    {
        if (t < start) return -1;
        if (t >= end) return !stopped && t < end + Linger ? 1 : -1;
        return (float)Math.Clamp((t - start) / Math.Max(0.1, end - start), 0, 1);
    }

    /// <summary>Works out the moment <paramref name="t"/>; <paramref name="still"/> (reduced motion): telegraphs full from the start.</summary>
    public void At(double t, bool still = false)
    {
        T = t;
        var tr = Track;
        tr.At(t, Points);
        Teles.Clear();
        TethersNow.Clear();
        Badges.Clear();

        // Casts: from the first that could still be on (begun no further back than the longest cast and a linger).
        var casts = tr.Casts;
        for (var i = FirstFrom(casts, t - tr.LongestCast - Linger - 0.5); i < casts.Count; i++)
        {
            var c = casts[i];
            if (c.T > t) break;
            var cl = new CastLike(c.T, c.End, c.Src, c.Shape, c.X, c.Z, c.Rot, c.Tx, c.Tz, c.Target, c.Stopped != CastStop.None, c.PushLength, c.PushWidth,
                c.Reach, true, MechRole.None);
            CastTele(in cl, t, still);
        }
        var hits = tr.Hits;
        for (var i = FirstHit(hits, t - Linger); i < hits.Count; i++)
        {
            var h = hits[i];
            if (h.T > t) break;
            HitTele(h, t, still);
        }
        var markers = tr.Markers;
        for (var i = 0; i < markers.Count; i++)
        {
            var mk = markers[i];
            if (mk.T > t) break;
            if (mk.Hit >= 0) MarkerTele(mk, t, still);
        }
        for (var i = 0; i < tr.Rotations.Count; i++) RotationTele(i, t);

        foreach (var th in tr.Tethers)
        {
            if (th.Start > t || th.End <= t) continue;
            if (!Here(th.From, out var a) || !Here(th.To, out var b)) continue;
            TethersNow.Add(new TetherNow { X1 = Frame.BoardX(a.X), Y1 = Frame.BoardY(a.Z), X2 = Frame.BoardX(b.X), Y2 = Frame.BoardY(b.Z) });
        }
        WaymarksNow = LastBefore(tr.Waymarks, t);
        SignsNow = LastBefore(tr.Signs, t);

        // Who's dead (died, and not raised since), raised just now, hit just now.
        var n = tr.Entities.Count;
        for (var i = 0; i < n; i++)
        {
            DeadSince[i] = double.NaN;
            lastRaise[i] = double.NaN;
            lastHit[i] = double.NaN;
            TargetOf[i] = 0;
            badgeAt[i] = -1;
        }
        foreach (var d in tr.Deaths)
        {
            if (d.T > t) break;
            if (tr.Index.TryGetValue(d.Who, out var k)) DeadSince[k] = d.T;
        }
        foreach (var r in tr.Raises)
        {
            if (r.T > t) break;
            if (!tr.Index.TryGetValue(r.Who, out var k)) continue;
            if (!double.IsNaN(DeadSince[k]) && r.T >= DeadSince[k]) DeadSince[k] = double.NaN;
            lastRaise[k] = r.T;
        }
        for (var i = FirstHit(hits, t - HitFor); i < hits.Count; i++)
        {
            var h = hits[i];
            if (h.T > t) break;
            foreach (var x in h.Targets)
                if (x.Landed && x.Damage != 0 && tr.Index.TryGetValue(x.Id, out var k))
                    lastHit[k] = h.T;
        }
        for (var i = 0; i < n; i++)
        {
            Raised[i] = !double.IsNaN(lastRaise[i]) && t - lastRaise[i] < RaisedFor ? t - lastRaise[i] : double.NaN;
            HitAgo[i] = !still && !double.IsNaN(lastHit[i]) ? t - lastHit[i] : double.NaN;
        }
        foreach (var a in tr.Aggro)
        {
            if (!tr.Index.TryGetValue(a.Actor, out var k)) continue;
            foreach (var s in a.Spans)
                if (s.Start <= t && t < s.End)
                {
                    TargetOf[k] = s.On;
                    break;
                }
        }

        // Head markers: the latest on each within MarkerFor; one drawn as its mechanic keeps a badge only for a limit cut's number or a turn.
        for (var i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            if (m.T > t) break;
            if (t - m.T > MarkerFor || !tr.Index.TryGetValue(m.On, out var k)) continue;
            badgeAt[k] = -1;
            if (m.Hit >= 0 && m.Meaning is not (MarkerMeaning.LimitCut or MarkerMeaning.RotateCw or MarkerMeaning.RotateCcw)) continue;
            badgeAt[k] = i;
        }
        for (var k = 0; k < n; k++)
            if (badgeAt[k] >= 0 && Points[k].Here)
            {
                var m = markers[badgeAt[k]];
                Badges.Add(new BadgeNow { Entity = k, Kind = m.Meaning, Role = m.Role, Number = m.Number });
            }

        ReplayStatuses.FightStatusesAt(tr, t, StatusesOn);
    }

    private bool Here(int id, out ReplayPoint p)
    {
        if (Track.Index.TryGetValue(id, out var k) && Points[k].Here)
        {
            p = Points[k];
            return true;
        }
        p = default;
        return false;
    }

    private static ReplayWaymarks? LastBefore(IReadOnlyList<ReplayWaymarks> list, double t)
    {
        ReplayWaymarks? found = null;
        foreach (var x in list)
        {
            if (x.T > t) break;
            found = x;
        }
        return found;
    }

    private static ReplaySigns? LastBefore(IReadOnlyList<ReplaySigns> list, double t)
    {
        ReplaySigns? found = null;
        foreach (var x in list)
        {
            if (x.T > t) break;
            found = x;
        }
        return found;
    }

    private static int FirstFrom(IReadOnlyList<ReplayCast> list, double t)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (list[mid].T < t) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static int FirstHit(IReadOnlyList<ReplayHit> list, double t)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (list[mid].T < t) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>A cast, or something drawn as one (a hit with no cast, a marker's mechanic), with nothing allocated.</summary>
    private readonly record struct CastLike(double T, double End, int Src, ReplayShape? Shape, float X, float Z, float Rot, float? Tx, float? Tz, int Target,
        bool Stopped, float PushLength, float PushWidth, float Reach, bool Turns, MechRole RoleOverride);

    /// <summary>A hit that came with no cast of its own as it lands (the site's <c>hitObjects</c>): its shape where it went off, the way it faced, full for <see cref="Linger"/>.</summary>
    private void HitTele(ReplayHit h, double t, bool still)
    {
        if (!h.Loose || h.Shape is not { } shape || shape.Kind is ShapeKind.Target or ShapeKind.Raidwide) return;
        if (t < h.T || t >= h.T + Linger) return;
        var one = h.Targets.Length == 1 ? h.Targets[0].Id : 0;
        float? tx = h.Hx != 0 || h.Hz != 0 ? h.Hx : null;
        float? tz = h.Hx != 0 || h.Hz != 0 ? h.Hz : null;
        var cl = new CastLike(h.T, h.T, h.Src, shape, h.X, h.Z, h.Rot, tx, tz, one, false, 0, 0, h.Reach, false, MechRole.None);
        CastTele(in cl, t, still);
    }

    /// <summary>
    /// A head marker that led to a mechanic drawn as that mechanic on the one marked from the marker on (the site's
    /// <c>markerObjects</c>): its real shape and size, following them, built up until it goes off; a line stack from its
    /// caster to them, a cone or a line turned to face them.
    /// </summary>
    private void MarkerTele(ReplayMarker mk, double t, bool still)
    {
        if (mk.Hit < 0 || double.IsNaN(mk.Until)) return;
        var h = Track.Hits[mk.Hit];
        if (h.Shape is not { } shape) return;
        var lands = mk.Until >= h.T && !h.Loose;
        if (t < mk.T || t >= (lands ? h.T + Linger : mk.Until)) return;
        if (!Here(mk.On, out var who)) return;
        var cx = h.X;
        var cz = h.Z;
        if (Here(h.Src, out var caster))
        {
            cx = caster.X;
            cz = caster.Z;
        }
        var toward = MathF.Atan2(who.X - cx, who.Z - cz);
        var role = mk.Meaning switch
        {
            MarkerMeaning.Spread => MechRole.Spread,
            MarkerMeaning.Stack => MechRole.Stack,
            MarkerMeaning.Tankbuster => MechRole.Tankbuster,
            MarkerMeaning.LineStack => MechRole.LineStack,
            _ => shape.Role != MechRole.None ? shape.Role : mk.Role != MechRole.None ? mk.Role : shape.Kind == ShapeKind.Target ? MechRole.Tankbuster : MechRole.Spread,
        };
        var cl = new CastLike(mk.T, Math.Min(h.T, mk.Until), h.Src, shape, cx, cz, toward, null, null, mk.On, false, 0, 0, float.NaN, false, role);
        CastTele(in cl, t, still);
    }

    /// <summary>A rotating mechanic's way round as a Rotation round the one turning (clockwise orange, anticlockwise blue), from its first moment until its last step has gone.</summary>
    private void RotationTele(int i, double t)
    {
        var r = Track.Rotations[i];
        if (t < rotationFrom[i] || t >= r.End + Linger) return;
        float x = r.X, z = r.Z;
        if (Here(r.Src, out var now))
        {
            x = now.X;
            z = now.Z;
        }
        else if (Track.TurnedSince(r.Src, rotationFrom[i], t) is { } turned)
        {
            x = turned.X;
            z = turned.Z;
        }
        var hitbox = Track.Entity(r.Src)?.Hitbox ?? 0.5f;
        Teles.Add(new Tele
        {
            Kind = BoardKind.Rotation,
            X = Frame.BoardX(x),
            Y = Frame.BoardY(z),
            Size = MathF.Max(40, MathF.Round(hitbox * Frame.Upy + 20)),
            Color = r.Dir == Spin.Ccw ? TowerColor : Aoe,
            Bright = 1,
            Variant = r.Dir == Spin.Ccw ? "ccw" : null,
        });
    }

    /// <summary>
    /// One cast's telegraph (the site's <c>castObjects</c>; none for a raidwide or a single target off a tank). One whose
    /// caster turned or moved as it cast turns and moves with it; a stack, spread or buster on a player follows them.
    /// Built by brightness only (nothing grows): faint as it begins, full as it goes off.
    /// </summary>
    private void CastTele(in CastLike cast, double t, bool still)
    {
        var c = cast;
        if (c.Shape is not { } shape) return;
        var p = CastProgress(c.T, c.End, c.Stopped, t);
        if (p < 0) return;
        var f = Frame;
        var u = f.Upy;
        var hasCaster = Here(c.Src, out var caster);
        var casterHitbox = Track.Entity(c.Src)?.Hitbox ?? 0;
        var turned = c.Turns ? Track.TurnedSince(c.Src, c.T, Math.Min(t, c.End)) : null;
        // The caster where it stood as it began, or now when the cast has no place (0,0); turned as it turned.
        var placed = c.X != 0 || c.Z != 0;
        var cx = turned?.X ?? (placed ? c.X : hasCaster ? caster.X : 0);
        var cz = turned?.Z ?? (placed ? c.Z : hasCaster ? caster.Z : 0);
        var deg = ReplayShapes.PlanRotation(turned?.Rot ?? c.Rot) - shape.Offset;
        float Reach(float n) => n + (shape.Hitbox ? casterHitbox : 0);
        var hasTarget = c.Target != 0 && Here(c.Target, out _);
        Here(c.Target, out var target);
        var targetEntity = c.Target != 0 ? Track.Entity(c.Target) : null;
        var onPlayer = hasTarget && targetEntity is { Kind: EntityKind.Party };
        var role = c.RoleOverride != MechRole.None ? c.RoleOverride : shape.Role != MechRole.None ? shape.Role : MechRole.Other;
        (float X, float Z) Centre()
        {
            if (onPlayer && (role is MechRole.Stack or MechRole.Spread or MechRole.Tankbuster or MechRole.Cleave || shape.Kind == ShapeKind.Target))
                return (target.X, target.Z);
            if (c.Tx is { } tx && c.Tz is { } tz && (tx != 0 || tz != 0)) return (tx, tz);
            return (cx, cz);
        }
        var bright = still || p >= 1 ? 1 : 0.3f + 0.7f * MathF.Round(p * 10) / 10;
        void Put(BoardKind kind, uint color, float x, float z, float size = 0, float rot = 0, float width = 0, float length = 0, float inner = 0,
            float angle = 0, string? variant = null) =>
            Teles.Add(new Tele
            {
                Kind = kind,
                X = f.BoardX(x),
                Y = f.BoardY(z),
                Rot = rot,
                Size = size,
                Width = width,
                Length = length,
                Inner = inner,
                Angle = angle,
                Color = color,
                Bright = bright,
                Variant = variant,
            });

        // A knockback: a side push across its rectangle (no bigger than 60 yalms), else a ring as far as it reaches.
        void Knockback()
        {
            if (c.PushLength > 0)
            {
                const float most = 60;
                Put(BoardKind.SideKnockback, Aoe, cx, cz, rot: deg, width: MathF.Max(40, MathF.Min(most, c.PushWidth) * u),
                    length: MathF.Max(40, MathF.Min(most, c.PushLength) * u));
                return;
            }
            Put(BoardKind.Knockback, Aoe, cx, cz, MathF.Max(40, !float.IsNaN(c.Reach) ? c.Reach * u : casterHitbox * u + 40));
        }

        var roundOrTarget = shape.Kind is ShapeKind.Circle or ShapeKind.Target or ShapeKind.Knockback or ShapeKind.Gaze;
        if (!roundOrTarget)
        {
            if (role == MechRole.Raidwide) return;
            if (role == MechRole.Stack && shape.Kind == ShapeKind.Rect) role = MechRole.LineStack;
            else if (role != MechRole.LineStack) role = MechRole.Other;
        }
        switch (role)
        {
            case MechRole.Stack:
            {
                var (x, z) = Centre();
                Put(BoardKind.Stack, StackColor, x, z, MathF.Max(20, (shape.R > 0 ? shape.R : 6) * u));
                return;
            }
            case MechRole.LineStack:
                Put(BoardKind.LineStack, StackColor, cx, cz, rot: deg, width: (shape.Width > 0 ? shape.Width : 4) * u, length: Reach(shape.Front > 0 ? shape.Front : 20) * u);
                return;
            case MechRole.Spread:
            {
                var (x, z) = Centre();
                var r = shape.R > 0 ? shape.R : 5;
                if (r >= 15) Put(BoardKind.Defamation, DefamationColor, x, z, MathF.Max(20, r * u));
                else Put(BoardKind.Spread, SpreadColor, x, z, MathF.Max(20, r * u));
                return;
            }
            case MechRole.Tankbuster:
            case MechRole.Cleave:
            {
                var (x, z) = Centre();
                var r = shape.Kind == ShapeKind.Circle && shape.R > 0 ? shape.R : 3;
                Put(BoardKind.Tankbuster, BusterColor, x, z, MathF.Max(30, r * u),
                    variant: role == MechRole.Cleave || (shape.Kind == ShapeKind.Circle && shape.R > 3) ? "cleave" : null);
                return;
            }
            case MechRole.SharedTankbuster:
            {
                var (x, z) = Centre();
                Put(BoardKind.SharedBuster, BusterColor, x, z, MathF.Max(30, (shape.R > 0 ? shape.R : 4) * u));
                return;
            }
            case MechRole.Tower:
            {
                var (x, z) = Centre();
                Put(BoardKind.Tower, TowerColor, x, z, MathF.Max(20, (shape.R > 0 ? shape.R : 3) * u));
                return;
            }
            case MechRole.Knockback:
                Knockback();
                return;
            case MechRole.Gaze:
                Put(BoardKind.Gaze, GazeColor, cx, cz, 30);
                return;
            case MechRole.Raidwide:
                return;
        }
        switch (shape.Kind)
        {
            case ShapeKind.Circle:
            {
                var (x, z) = shape.Hitbox ? (cx, cz) : Centre();
                Put(BoardKind.Circle, Aoe, x, z, MathF.Max(8, Reach(shape.R > 0 ? shape.R : 5) * u));
                return;
            }
            case ShapeKind.Cone:
                Put(BoardKind.Cone, Aoe, cx, cz, MathF.Max(20, Reach(shape.R > 0 ? shape.R : 10) * u), deg, angle: Math.Clamp(shape.Angle, 5, 360));
                return;
            case ShapeKind.Donut:
            {
                var outer = Reach(shape.R > 0 ? shape.R : 20);
                Put(BoardKind.Donut, Aoe, cx, cz, MathF.Max(20, outer * u), inner: MathF.Max(4, MathF.Min(outer - 1, shape.Inner > 0 ? shape.Inner : outer / 2) * u));
                return;
            }
            case ShapeKind.Rect:
            {
                var front = Reach(shape.Front > 0 ? shape.Front : 20);
                var back = shape.Back;
                var a = deg * MathF.PI / 180;
                var along = (front - back) / 2;
                Put(BoardKind.Rect, Aoe, cx + MathF.Sin(a) * along, cz - MathF.Cos(a) * along, rot: deg, width: MathF.Max(4, shape.Width * u),
                    length: MathF.Max(8, (front + back) * u));
                return;
            }
            case ShapeKind.Cross:
                Put(BoardKind.Cross, Aoe, cx, cz, rot: deg, width: MathF.Max(4, shape.Width * u), length: MathF.Max(8, shape.Arm * 2 * u));
                return;
            case ShapeKind.Charge:
            {
                float ex, ez;
                if (hasTarget)
                {
                    ex = target.X;
                    ez = target.Z;
                }
                else if (c.Tx is { } tx && c.Tz is { } tz)
                {
                    ex = tx;
                    ez = tz;
                }
                else return;
                var dx = ex - cx;
                var dz = ez - cz;
                var len = MathF.Sqrt(dx * dx + dz * dz);
                if (len < 1) return;
                var rot = (MathF.Atan2(dx, -dz) * 180 / MathF.PI % 360 + 360) % 360;
                Put(BoardKind.Rect, Aoe, cx + dx / 2, cz + dz / 2, rot: MathF.Round(rot * 10) / 10, width: MathF.Max(4, shape.Width * u), length: len * u);
                return;
            }
            case ShapeKind.Target:
                if (!onPlayer || targetEntity!.Role != "tank") return;
                Put(BoardKind.Tankbuster, BusterColor, target.X, target.Z, 30);
                return;
            case ShapeKind.Knockback:
                Knockback();
                return;
            case ShapeKind.Gaze:
                Put(BoardKind.Gaze, GazeColor, cx, cz, 30);
                return;
        }
    }
}
