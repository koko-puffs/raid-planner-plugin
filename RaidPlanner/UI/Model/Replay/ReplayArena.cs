using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>
/// An arena worked out for a pull (the site's <c>ArenaChoice</c>): circle, square or rectangle, its width and depth in
/// yalms, and its centre in world coordinates.
/// </summary>
public readonly record struct ArenaChoice(string Shape, float Width, float Depth, float Cx, float Cz)
{
    /// <summary>
    /// The arena as the board draws it (the site's <c>arenaFor</c>): its shape, how many yalms the board's ±420 spans
    /// (the longest side, 5 at least), a rectangle's depth to width, and its centre.
    /// </summary>
    public PlanArena ToPlan()
    {
        var shape = Shape is "circle" or "rectangle" ? Shape : "square";
        var yalms = MathF.Max(5, MathF.Round(Shape == "rectangle" ? MathF.Max(Width, Depth) : Width));
        var aspect = Shape == "rectangle" ? Math.Clamp(Depth / MathF.Max(1, Width), 0.25f, 4) : 1;
        return new PlanArena(shape, aspect, Array.Empty<float[]>(), PlanArena.Default.Color)
        {
            Yalms = yalms,
            Center = new ArenaCenter(Cx, Cz),
        };
    }
}

/// <summary>
/// Who the party fought and where (the site's model/recording.ts's <c>fightOf</c> and <c>guessArena</c>, without the
/// encounter data the site also has): the enemies in the party's fight, and the arena the pull was on, from where the
/// bosses, the party and the mechanics were. Pure.
/// </summary>
public static class ReplayArena
{
    /// <summary>A boss further than this from the nearest of the party (yalms, most of the pull) isn't in their fight.</summary>
    private const float BossNear = 40;

    /// <summary>Further than this from the fight's middle (yalms), a point says nothing of the arena.</summary>
    private const float FightReach = 60;

    /// <summary>
    /// The enemies the party fought (the site's <c>fightOf</c>): one that hit a party member, cast at one, put a status on
    /// one, killed one or was tethered to one; a boss also when it was mostly within <see cref="BossNear"/> of the party
    /// (or always, when the party's places weren't recorded). When none is, every enemy is.
    /// </summary>
    public static HashSet<int> FightActors(Recording r)
    {
        var party = new HashSet<int>(r.Party.Select(p => p.Id));
        var known = new HashSet<int>(r.Actors.Select(a => a.Id));
        bool Enemy(int id) => known.Contains(id) && !party.Contains(id);
        var actors = new HashSet<int>();
        foreach (var e in r.Events)
            switch (e)
            {
                case HitEvent h when Enemy(h.Source) && h.Targets.Any(party.Contains):
                    actors.Add(h.Source);
                    break;
                case CastEvent c when Enemy(c.Source) && c.Target != 0 && party.Contains(c.Target):
                    actors.Add(c.Source);
                    break;
                case TetherEvent t:
                    if (party.Contains(t.From) && Enemy(t.To)) actors.Add(t.To);
                    if (party.Contains(t.To) && Enemy(t.From)) actors.Add(t.From);
                    break;
                case StatusEvent s when Enemy(s.Source) && party.Contains(s.On):
                    actors.Add(s.Source);
                    break;
                case DeathEvent d when Enemy(d.From):
                    actors.Add(d.From);
                    break;
            }
        foreach (var a in r.Actors)
        {
            if (a.Kind != ActorKind.Boss || actors.Contains(a.Id) || party.Contains(a.Id)) continue;
            if (!r.PartyPositions)
            {
                actors.Add(a.Id);
                continue;
            }
            var apart = new List<float>();
            foreach (var f in r.Frames)
            {
                if (!f.At.TryGetValue(a.Id, out var b)) continue;
                var nearest = float.PositiveInfinity;
                foreach (var m in r.Party)
                    if (f.At.TryGetValue(m.Id, out var q))
                        nearest = MathF.Min(nearest, Dist(q.X, q.Z, b.X, b.Z));
                if (nearest < float.PositiveInfinity) apart.Add(nearest);
            }
            if (apart.Count == 0 || Median(apart) <= BossNear) actors.Add(a.Id);
        }
        if (actors.Count == 0)
            foreach (var a in r.Actors)
                if (!party.Contains(a.Id)) actors.Add(a.Id);
        return actors;
    }

    /// <summary>
    /// The arena a pull was on, worked out from the fight (the site's <c>guessArena</c> without encounter data): its
    /// centre where the bosses mostly stood from the first enemy cast to the last (else the party, the enemies, where
    /// their attacks landed), snapped to 100, 100 within 15 yalms of it; its size how far the party went from it (all but
    /// the furthest 2%) or how far the mechanics reached (three in four), rounded up to 5 yalms, 20 to 80 across; a square
    /// when people stood in its corners (or there's little to go on), else a circle.
    /// </summary>
    public static ArenaChoice Guess(Recording r)
    {
        var actors = FightActors(r);
        var party = new HashSet<int>(r.Party.Select(p => p.Id));
        var bosses = new HashSet<int>(r.Actors.Where(a => a.Kind == ActorKind.Boss && actors.Contains(a.Id)).Select(a => a.Id));
        var helpers = new HashSet<int>(r.Actors.Where(a => a.Kind == ActorKind.Helper).Select(a => a.Id));
        var casts = r.Events.OfType<CastEvent>().Where(c => actors.Contains(c.Source)).ToList();
        var from = double.NegativeInfinity;
        var to = double.PositiveInfinity;
        if (casts.Count > 0)
        {
            from = casts[0].T;
            to = 0;
            foreach (var c in casts) to = Math.Max(to, c.T + c.Duration + 1);
        }
        var frames = r.Frames.Where(f => f.T >= from && f.T <= to).ToList();
        if (frames.Count < 4) frames = r.Frames;
        List<(float X, float Z)> At(Func<int, bool> keep)
        {
            var pts = new List<(float, float)>();
            foreach (var f in frames)
                foreach (var (id, p) in f.At)
                    if (keep(id)) pts.Add((p.X, p.Z));
            return pts;
        }
        var hits = new List<(float X, float Z)>();
        foreach (var h in r.Events.OfType<HitEvent>())
            if (actors.Contains(h.Source) && (h.HitX != 0 || h.HitZ != 0) && h.T >= from - 1 && h.T <= to)
                hits.Add((h.HitX, h.HitZ));
        var stood = At(party.Contains);
        var enemies = At(id => actors.Contains(id) && !helpers.Contains(id));
        var middleOf = new[] { At(bosses.Contains), stood, enemies, hits }.FirstOrDefault(l => l.Count > 0);
        if (middleOf == null) return new ArenaChoice("square", 40, 40, 100, 100);
        var cx = Median(middleOf.Select(p => p.X).ToList());
        var cz = Median(middleOf.Select(p => p.Z).ToList());
        if (Dist(cx, cz, 100, 100) < 15)
        {
            cx = 100;
            cz = 100;
        }
        bool Near((float X, float Z) p) => Dist(p.X, p.Z, cx, cz) <= FightReach;
        var pts = stood.Concat(enemies).Concat(hits).Where(Near).ToList();
        // Waymarks sit inside the arena, usually near its edge: they say how big it is.
        foreach (var w in r.Events.OfType<WaymarksEvent>())
            foreach (var p in w.Marks.Values)
                if (Near(p)) pts.Add(p);
        var far = pts.Count > 0 ? Percentile(pts.Select(p => MathF.Max(MathF.Abs(p.X - cx), MathF.Abs(p.Z - cz))).ToList(), 0.98f) : 0;
        var reaches = new List<float>();
        var hitboxes = r.Actors.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First().Hitbox);
        foreach (var c in casts)
        {
            if (c.T < from || c.T > to) continue;
            var shape = ReplayShapes.Of(r, c.Action);
            var reach = shape == null ? 0 : Reach(shape, hitboxes.TryGetValue(c.Source, out var hb) ? hb : 0);
            if (reach <= 0 || reach >= 40) continue;
            var (x, z) = c.TargetX is { } tx && c.TargetZ is { } tz ? (tx, tz) : (c.X, c.Z);
            if (!Near((x, z))) continue;
            reaches.Add(Dist(x, z, cx, cz) + reach);
        }
        var mech = reaches.Count > 0 ? Percentile(reaches, 0.75f) : 0;
        var half = MathF.Min(40, MathF.Max(10, MathF.Max(MathF.Ceiling((far + 1) / 5) * 5, MathF.Ceiling(mech / 5) * 5)));
        var corners = pts.Count(p => MathF.Abs(p.X - cx) > half * 0.78f && MathF.Abs(p.Z - cz) > half * 0.78f);
        var shapeName = corners >= 3 || pts.Count < 40 ? "square" : "circle";
        return new ArenaChoice(shapeName, half * 2, half * 2, MathF.Round(cx * 10) / 10, MathF.Round(cz * 10) / 10);
    }

    /// <summary>How far an attack reaches from where it goes off (yalms; 0 for one that isn't an area).</summary>
    public static float Reach(ReplayShape shape, float hitbox)
    {
        var plus = shape.Hitbox ? hitbox : 0;
        return shape.Kind switch
        {
            ShapeKind.Circle or ShapeKind.Cone or ShapeKind.Donut => shape.R + plus,
            ShapeKind.Rect => shape.Front + plus,
            ShapeKind.Cross => shape.Arm,
            _ => 0,
        };
    }

    private static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    private static float Median(List<float> list)
    {
        if (list.Count == 0) return 0;
        list.Sort();
        return list[list.Count / 2];
    }

    private static float Percentile(List<float> list, float p)
    {
        if (list.Count == 0) return 0;
        list.Sort();
        return list[Math.Min(list.Count - 1, (int)MathF.Floor(list.Count * p))];
    }
}
