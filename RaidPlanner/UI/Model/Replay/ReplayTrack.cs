using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>Who someone on the replay's board is.</summary>
public enum EntityKind
{
    Party,
    Boss,
    Add,
    Helper,
}

/// <summary>
/// Someone or something on the board over the pull (the site's <c>ReplayEntity</c>): its samples in time order, where,
/// facing (the game's radians) and HP (NaN not known).
/// </summary>
public sealed class ReplayEntity
{
    public int Id { get; init; }
    public EntityKind Kind { get; init; }

    /// <summary>A party member's spot ("MT"), job ("WAR") and role ("tank", "healer", "dps").</summary>
    public string Slot { get; init; } = string.Empty;
    public string Job { get; init; } = string.Empty;
    public string Role { get; init; } = "dps";

    /// <summary>An enemy's name; a party member's character's name, else their spot.</summary>
    public string Name { get; init; } = string.Empty;
    public float Hitbox { get; init; }

    /// <summary>The most HP it had (0 unknown).</summary>
    public long MaxHp { get; init; }
    public float[] T { get; init; } = Array.Empty<float>();
    public float[] X { get; init; } = Array.Empty<float>();
    public float[] Z { get; init; } = Array.Empty<float>();
    public float[] Rot { get; init; } = Array.Empty<float>();
    public float[] Hp { get; init; } = Array.Empty<float>();
}

/// <summary>How a cast ended early, if it did.</summary>
public enum CastStop
{
    None,
    Interrupted,
    Cancelled,
}

/// <summary>An enemy's cast (the site's <c>ReplayCast</c>): when, its shape, where it began and was aimed, at whom, how it ended.</summary>
public sealed class ReplayCast
{
    public double T { get; set; }
    public double End { get; set; }
    public int Src { get; set; }
    public uint Action { get; set; }
    public string Name { get; set; } = string.Empty;
    public ReplayShape? Shape { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public float Rot { get; set; }
    public float? Tx { get; set; }
    public float? Tz { get; set; }

    /// <summary>Whom it's on (a party member's or an actor's id; 0 none).</summary>
    public int Target { get; set; }
    public bool Interruptible { get; set; }
    public CastStop Stopped { get; set; }

    /// <summary>A knockback across a rectangle (cast type 4 or 12): its reach and width (yalms); 0 none.</summary>
    public float PushLength { get; set; }
    public float PushWidth { get; set; }

    /// <summary>A knockback's reach from its caster (yalms); NaN for anything else.</summary>
    public float Reach { get; set; } = float.NaN;
}

/// <summary>One target of an enemy's hit: where they stood (NaN not known), the damage (−1 not known), its flags, whether it landed.</summary>
public readonly record struct HitTarget(int Id, float X, float Z, long Damage, int Fx, bool Landed);

/// <summary>An enemy's hit (the site's <c>ReplayHit</c>); <see cref="Loose"/>: it came with no cast of its own and isn't an auto.</summary>
public sealed class ReplayHit
{
    public double T { get; init; }
    public int Src { get; init; }
    public uint Action { get; init; }
    public float X { get; init; }
    public float Z { get; init; }
    public float Rot { get; init; }
    public float Hx { get; init; }
    public float Hz { get; init; }
    public string Name { get; init; } = string.Empty;
    public ReplayShape? Shape { get; init; }
    public float Reach { get; set; } = float.NaN;
    public bool Loose { get; set; }
    public HitTarget[] Targets { get; init; } = Array.Empty<HitTarget>();
}

/// <summary>What a head marker means (the encounter data's on the site; here a limit cut's number or a turn's way round).</summary>
public enum MarkerMeaning
{
    None,
    Spread,
    Stack,
    Tankbuster,
    LineStack,
    LimitCut,
    Target,
    RotateCw,
    RotateCcw,
}

/// <summary>A head marker (the site's <c>ReplayMarker</c>): on whom, its icon, meaning, and the mechanic it led to (<see cref="Hit"/>, −1 none).</summary>
public sealed class ReplayMarker
{
    public double T { get; init; }
    public int On { get; init; }
    public uint Icon { get; init; }
    public MarkerMeaning Meaning { get; init; }

    /// <summary>A limit cut's number, 1 to 8 (0 none).</summary>
    public int Number { get; init; }

    /// <summary>The hit it led to (an index into the track's hits), −1 none.</summary>
    public int Hit { get; set; } = -1;

    /// <summary>Its drawing of its mechanic stands until this (NaN none).</summary>
    public double Until { get; set; } = double.NaN;

    /// <summary>What the hit says it was, when the marker's meaning isn't known.</summary>
    public MechRole Role { get; set; }
}

public readonly record struct ReplayTether(int From, int To, uint Id, double Start, double End);

public sealed record ReplayWaymarks(double T, IReadOnlyList<(string Name, float X, float Z)> Marks);

public sealed record ReplaySigns(double T, IReadOnlyList<(int Index, int On)> Marks);

public readonly record struct AggroSpan(double Start, double End, int On);

public sealed record ReplayAggro(int Actor, IReadOnlyList<AggroSpan> Spans);

/// <summary>A party's ground-placed action (Sacred Soil, Asylum): where, how big, until when.</summary>
public readonly record struct ReplayArea(double T, int Src, uint Action, string Name, float X, float Z, float Radius, double Until);

public readonly record struct ReplayDeath(double T, int Who);

public readonly record struct ReplayRaise(double T, int Who, int By);

/// <summary>
/// One of the fight's statuses (an enemy's or no one's, on a party member or an enemy) as a span: on whom, the Status
/// row, its name, from and to, stacks (or a parameter above 16), its icon and category (1 a buff, 2 a debuff, 0 not
/// known).
/// </summary>
public sealed class ReplayStatus
{
    public int On { get; init; }
    public uint Status { get; init; }
    public string Name { get; init; } = string.Empty;
    public double Start { get; init; }
    public double End { get; set; }
    public int Stacks { get; init; }
    public uint Icon { get; init; }
    public int Cat { get; init; }
}

/// <summary>An enemy turning or moving at a mechanic (plugin 0.7.4's turns).</summary>
public readonly record struct TurnAt(double T, float Rot, float X, float Z);

/// <summary>Where someone is at a moment: there or not, place, facing, HP (NaN not known).</summary>
public struct ReplayPoint
{
    public bool Here;
    public float X;
    public float Z;
    public float Rot;
    public float Hp;
}

/// <summary>
/// A pull to replay on the board (the site's model/replay.ts): everyone's places, facing and HP over the pull as
/// sampled, and everything that happened on the floor, each a list in time order: the enemies' casts with their shapes
/// (and whether they were stopped), their hits, head markers and the mechanic each led to, tethers as spans,
/// waymarks, head signs, each enemy's target, the party's ground effects, deaths and raises, the fight's statuses as
/// spans, the enemies' turns and the rotating mechanics. Made once a pull (<see cref="Build"/>); <see cref="At"/>
/// reads everyone at a moment, between samples, into a buffer (nothing allocated). World coordinates and the game's
/// facing, as the recording has them.
/// </summary>
public sealed class ReplayTrack
{
    public double End { get; private init; }

    /// <summary>Frames a second as recorded.</summary>
    public float Rate { get; private init; } = 2;
    public IReadOnlyList<ReplayEntity> Entities { get; private init; } = Array.Empty<ReplayEntity>();
    public IReadOnlyList<ReplayCast> Casts { get; private init; } = Array.Empty<ReplayCast>();
    public IReadOnlyList<ReplayHit> Hits { get; private init; } = Array.Empty<ReplayHit>();
    public IReadOnlyList<ReplayMarker> Markers { get; private init; } = Array.Empty<ReplayMarker>();
    public IReadOnlyList<ReplayTether> Tethers { get; private init; } = Array.Empty<ReplayTether>();
    public IReadOnlyList<ReplayWaymarks> Waymarks { get; private init; } = Array.Empty<ReplayWaymarks>();
    public IReadOnlyList<ReplaySigns> Signs { get; private init; } = Array.Empty<ReplaySigns>();
    public IReadOnlyList<ReplayAggro> Aggro { get; private init; } = Array.Empty<ReplayAggro>();
    public IReadOnlyList<ReplayArea> Areas { get; private init; } = Array.Empty<ReplayArea>();
    public IReadOnlyList<ReplayDeath> Deaths { get; private init; } = Array.Empty<ReplayDeath>();
    public IReadOnlyList<ReplayRaise> Raises { get; private init; } = Array.Empty<ReplayRaise>();

    /// <summary>The fight's statuses (never the party's own), by their start.</summary>
    public IReadOnlyList<ReplayStatus> Statuses { get; private init; } = Array.Empty<ReplayStatus>();
    public IReadOnlyDictionary<int, List<TurnAt>> Turns { get; private init; } = new Dictionary<int, List<TurnAt>>();
    public IReadOnlyList<ReplayRotation> Rotations { get; private init; } = Array.Empty<ReplayRotation>();

    /// <summary>The arena the pull was worked out to be on.</summary>
    public ArenaChoice Arena { get; private init; }

    /// <summary>The names of the party's own statuses (food, their buffs): a recording from before plugin 0.4 doesn't say a status's category.</summary>
    public IReadOnlySet<string> PartyOwnNames { get; private init; } = new HashSet<string>();

    /// <summary>Each entity's index in <see cref="Entities"/>, by id.</summary>
    public IReadOnlyDictionary<int, int> Index { get; private init; } = new Dictionary<int, int>();

    /// <summary>The longest cast (s): how far back a cast still on can have begun.</summary>
    public double LongestCast { get; private init; }

    /// <summary>The entity with this id, or null.</summary>
    public ReplayEntity? Entity(int id) => Index.TryGetValue(id, out var i) ? Entities[i] : null;

    /// <summary>How long a ground-placed party action lasts, by name (the game's tooltips); the rest 15 s. Only for drawing it.</summary>
    private static readonly Dictionary<string, double> AreaSeconds = new(StringComparer.Ordinal)
    {
        ["Sacred Soil"] = 15,
        ["Asylum"] = 24,
        ["Earthly Star"] = 20,
        ["Doton"] = 18,
        ["Salted Earth"] = 15,
        ["Liturgy of the Bell"] = 20,
        ["Ley Lines"] = 20,
    };

    private const double AreaDefault = 15;

    /// <summary>The game's own limit cut markers: icons 79 to 86 are 1 to 8.</summary>
    public static int LimitCutNumber(uint icon) => icon is >= 79 and <= 86 ? (int)icon - 78 : 0;

    private sealed class Column
    {
        public readonly List<float> T = new(), X = new(), Z = new(), Rot = new(), Hp = new();

        public void Add(double t, float x, float z, float rot, float hp)
        {
            T.Add((float)t);
            X.Add(x);
            Z.Add(z);
            Rot.Add(rot);
            Hp.Add(hp);
        }
    }

    /// <summary>A pull as a track to replay (see the class's summary).</summary>
    public static ReplayTrack Build(Recording rec)
    {
        var partyIds = rec.Party.Select(p => p.Id).ToHashSet();
        var field = rec.Duty.Field;
        var lastFrame = rec.Frames.Count > 0 ? rec.Frames[^1].T : 0;
        var end = Math.Max(rec.Duration, lastFrame);
        var roles = ReplayShapes.InferRoles(rec);
        var arena = ReplayArena.Guess(rec);
        // A line that runs through its caster is drawn centred on it (the site's actionShape: throughShape of shapeFor).
        var through = ReplayShapes.ThroughLines(rec, arena);
        var shapes = new Dictionary<uint, ReplayShape?>();
        ReplayShape? ShapeOf(uint a)
        {
            if (!shapes.TryGetValue(a, out var s)) shapes[a] = s = ReplayShapes.Through(ReplayShapes.Of(rec, a, roles), through.Contains(a));
            return s;
        }
        string Name(uint a) => ReplayShapes.ActionName(rec.Actions, a);

        /* ---------- places ---------- */
        var turns = new Dictionary<int, List<TurnAt>>();
        foreach (var e in rec.Events.OfType<TurnEvent>().OrderBy(e => e.T))
        {
            if (!turns.TryGetValue(e.Actor, out var list)) turns[e.Actor] = list = new List<TurnAt>();
            list.Add(new TurnAt(e.T, e.Rotation, e.X, e.Z));
        }
        var cols = new Dictionary<int, Column>();
        var turnsAt = turns.ToDictionary(kv => kv.Key, _ => 0);
        var lastHp = new Dictionary<int, float>();
        void SampleTurns(int id, Column c, double upTo, float hp)
        {
            if (!turns.TryGetValue(id, out var list)) return;
            var i = turnsAt[id];
            while (i < list.Count && list[i].T < upTo - 0.01)
            {
                var x = list[i++];
                c.Add(x.T, x.X, x.Z, x.Rot, hp);
            }
            turnsAt[id] = i;
        }
        foreach (var f in rec.Frames)
            foreach (var (id, p) in f.At)
            {
                if (!cols.TryGetValue(id, out var c)) cols[id] = c = new Column();
                // Its turns since its last frame first (only after its first frame: one before isn't in the fight yet).
                if (c.T.Count > 0) SampleTurns(id, c, f.T, lastHp.TryGetValue(id, out var h) ? h : float.NaN);
                else if (turns.TryGetValue(id, out var list))
                {
                    var i = turnsAt[id];
                    while (i < list.Count && list[i].T < f.T - 0.01) i++;
                    turnsAt[id] = i;
                }
                var hp = f.Hp != null && f.Hp.TryGetValue(id, out var v) ? v : float.NaN;
                lastHp[id] = hp;
                c.Add(f.T, p.X, p.Z, p.Rotation, hp);
            }
        // The turns after an enemy's last frame, up to a second and a half after it (no further: it's gone).
        foreach (var (id, c) in cols)
            if (c.T.Count > 0 && turns.ContainsKey(id))
                SampleTurns(id, c, c.T[^1] + 1.5, lastHp.TryGetValue(id, out var h) ? h : float.NaN);

        var entities = new List<ReplayEntity>();
        ReplayEntity Make(int id, EntityKind kind, string slot, string job, string role, string name, float hitbox, long maxHp)
        {
            cols.TryGetValue(id, out var c);
            return new ReplayEntity
            {
                Id = id,
                Kind = kind,
                Slot = slot,
                Job = job,
                Role = role,
                Name = name,
                Hitbox = hitbox,
                MaxHp = maxHp,
                T = c?.T.ToArray() ?? Array.Empty<float>(),
                X = c?.X.ToArray() ?? Array.Empty<float>(),
                Z = c?.Z.ToArray() ?? Array.Empty<float>(),
                Rot = c?.Rot.ToArray() ?? Array.Empty<float>(),
                Hp = c?.Hp.ToArray() ?? Array.Empty<float>(),
            };
        }
        foreach (var p in rec.Party)
            entities.Add(Make(p.Id, EntityKind.Party, p.Slot, p.Job, p.Role, PartyMember.Clean(p.Name) ?? p.Slot, 0.5f, p.MaxHp));
        foreach (var a in rec.Actors)
            if (cols.ContainsKey(a.Id) && !partyIds.Contains(a.Id))
                entities.Add(Make(a.Id, a.Kind switch { ActorKind.Boss => EntityKind.Boss, ActorKind.Add => EntityKind.Add, _ => EntityKind.Helper },
                    string.Empty, string.Empty, string.Empty, a.Name, a.Hitbox, a.MaxHp));
        var index = new Dictionary<int, int>();
        for (var i = 0; i < entities.Count; i++) index.TryAdd(entities[i].Id, i);
        // How often the frames come: the median gap, as frames a second (2 or 4; 2 without frames).
        var gaps = new List<double>();
        for (var i = 1; i < rec.Frames.Count; i++)
        {
            var d = rec.Frames[i].T - rec.Frames[i - 1].T;
            if (d > 0.01) gaps.Add(d);
        }
        gaps.Sort();
        var gap = gaps.Count > 0 ? gaps[gaps.Count >> 1] : 0.5;
        var rate = (float)Math.Max(0.25, Math.Round(1 / gap * 4) / 4);

        /* ---------- events ---------- */
        var casts = new List<ReplayCast>();
        var hits = new List<ReplayHit>();
        var markers = new List<ReplayMarker>();
        var tethers = new List<(int From, int To, uint Id, double Start, double End)>();
        var openTethers = new Dictionary<(int, int, uint), int>();
        var waymarks = new List<ReplayWaymarks>();
        var signs = new List<ReplaySigns>();
        var deaths = new List<ReplayDeath>();
        var raises = new List<ReplayRaise>();
        var aggroBy = new Dictionary<int, List<AggroSpan>>();
        var openCasts = new Dictionary<int, List<ReplayCast>>();
        var statuses = new List<ReplayStatus>();
        var openStatus = new Dictionary<(int, uint), ReplayStatus>();
        void CloseStatus((int, uint) key, double t)
        {
            if (!openStatus.Remove(key, out var had)) return;
            had.End = Math.Max(had.Start, Math.Min(had.End, t));
        }
        foreach (var e in rec.Events.OrderBy(e => e.T))
        {
            switch (e)
            {
                case CastEvent c:
                {
                    if (partyIds.Contains(c.Source)) break;
                    // One cast seen twice (plugin 0.7.9 and before): a caster can't cast the same thing twice at once.
                    if (openCasts.TryGetValue(c.Source, out var open) && open.Any(x => x.Action == c.Action && x.Stopped == CastStop.None && c.T < x.End - 0.05))
                        break;
                    var shape = ShapeOf(c.Action);
                    rec.Actions.TryGetValue(c.Action, out var game);
                    var rc = new ReplayCast
                    {
                        T = c.T,
                        End = c.T + c.Duration,
                        Src = c.Source,
                        Action = c.Action,
                        Name = Name(c.Action),
                        Shape = shape,
                        X = c.X,
                        Z = c.Z,
                        Rot = c.Rotation,
                        Tx = c.TargetX,
                        Tz = c.TargetZ,
                        Target = c.Target,
                        Interruptible = c.Interruptible,
                    };
                    if (shape?.Kind == ShapeKind.Knockback && game is { CastType: 4 or 12, Range: > 0 })
                    {
                        rc.PushLength = game.Range;
                        rc.PushWidth = game.Width > 0 ? game.Width : game.Range;
                    }
                    casts.Add(rc);
                    if (open == null) openCasts[c.Source] = open = new List<ReplayCast>();
                    open.Add(rc);
                    break;
                }
                case CancelEvent x:
                {
                    if (!openCasts.TryGetValue(x.Source, out var list)) break;
                    // The enemy's cast it stopped: the latest of that action still going.
                    for (var i = list.Count - 1; i >= 0; i--)
                    {
                        var c = list[i];
                        if (c.Action != x.Action || c.Stopped != CastStop.None || x.T < c.T - 0.05 || x.T > c.End + 0.05) continue;
                        c.Stopped = x.Interrupted ? CastStop.Interrupted : CastStop.Cancelled;
                        c.End = Math.Max(c.T, x.T);
                        break;
                    }
                    break;
                }
                case HitEvent h:
                {
                    if (partyIds.Contains(h.Source)) break;
                    var targets = new HitTarget[h.Targets.Count];
                    for (var j = 0; j < targets.Length; j++)
                    {
                        var fx = h.Fx != null && j < h.Fx.Count ? h.Fx[j] : 0;
                        var dmg = h.Damage != null && j < h.Damage.Count ? h.Damage[j] : -1;
                        var px = h.Px != null && j < h.Px.Count ? h.Px[j] : float.NaN;
                        var pz = h.Pz != null && j < h.Pz.Count ? h.Pz[j] : float.NaN;
                        targets[j] = new HitTarget(h.Targets[j], px, pz, dmg, fx, Landed(fx, dmg));
                    }
                    var name = Name(h.Action);
                    hits.Add(new ReplayHit
                    {
                        T = h.T,
                        Src = h.Source,
                        Action = h.Action,
                        X = h.X,
                        Z = h.Z,
                        Rot = h.Rotation,
                        Hx = h.HitX,
                        Hz = h.HitZ,
                        Name = name,
                        Shape = ShapeOf(h.Action),
                        Loose = !ReplayShapes.IsAutoAttack(rec.Actions.TryGetValue(h.Action, out var ai) ? ai.Name : null),
                        Targets = targets,
                    });
                    break;
                }
                case MarkerEvent m:
                {
                    var cut = field ? 0 : LimitCutNumber(m.Icon);
                    var spin = ReplayRotations.IconSpin(m.Icon);
                    markers.Add(new ReplayMarker
                    {
                        T = m.T,
                        On = m.On,
                        Icon = m.Icon,
                        Meaning = cut > 0 ? MarkerMeaning.LimitCut : spin switch
                        {
                            Spin.Cw => MarkerMeaning.RotateCw,
                            Spin.Ccw => MarkerMeaning.RotateCcw,
                            _ => MarkerMeaning.None,
                        },
                        Number = cut,
                    });
                    break;
                }
                case TetherEvent t when t.On:
                {
                    var key = (t.From, t.To, t.Kind);
                    // On again with no "off" between: the one before ended now.
                    if (openTethers.TryGetValue(key, out var had)) tethers[had] = tethers[had] with { End = Math.Max(tethers[had].Start, t.T) };
                    openTethers[key] = tethers.Count;
                    tethers.Add((t.From, t.To, t.Kind, t.T, end));
                    break;
                }
                case TetherEvent t:
                {
                    // The game may not say which tether went: the one between the two, else any from it.
                    var key = (t.From, t.To, t.Kind);
                    if (!openTethers.ContainsKey(key))
                        foreach (var k in openTethers.Keys)
                            if (k.Item1 == t.From && k.Item2 == t.To)
                            {
                                key = k;
                                break;
                            }
                    if (!openTethers.ContainsKey(key))
                        foreach (var k in openTethers.Keys)
                            if (k.Item1 == t.From)
                            {
                                key = k;
                                break;
                            }
                    if (openTethers.Remove(key, out var at)) tethers[at] = tethers[at] with { End = Math.Max(tethers[at].Start, t.T) };
                    break;
                }
                case WaymarksEvent w:
                    waymarks.Add(new ReplayWaymarks(w.T, WaymarksEvent.Names.Where(w.Marks.ContainsKey).Select(n => (n, w.Marks[n].X, w.Marks[n].Z)).ToList()));
                    break;
                case SignsEvent s:
                    signs.Add(new ReplaySigns(s.T, s.Marks.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList()));
                    break;
                case DeathEvent d:
                    if (partyIds.Contains(d.Who)) deaths.Add(new ReplayDeath(d.T, d.Who));
                    break;
                case RaiseEvent r:
                    raises.Add(new ReplayRaise(r.T, r.Who, r.By));
                    break;
                case AggroEvent a:
                {
                    if (!aggroBy.TryGetValue(a.Actor, out var list)) aggroBy[a.Actor] = list = new List<AggroSpan>();
                    if (list.Count > 0)
                    {
                        var cur = list[^1];
                        if (cur.On == a.On) break;
                        list[^1] = cur with { End = Math.Max(cur.Start, a.T) };
                    }
                    list.Add(new AggroSpan(a.T, end, a.On));
                    break;
                }
                case StatusEvent s:
                {
                    // By whom it's on and what: an enemy's status going is written with no source, so the source isn't the key.
                    var key = (s.On, s.Id);
                    if (s.Gain)
                    {
                        CloseStatus(key, s.T);
                        rec.Statuses.TryGetValue(s.Id, out var facts);
                        var name = facts?.Name?.Trim() is { Length: > 0 } n ? n : $"Status {s.Id}";
                        var span = new ReplayStatus
                        {
                            On = s.On,
                            Status = s.Id,
                            Name = name,
                            Start = s.T,
                            End = s.Duration > 0 ? Math.Min(end, s.T + s.Duration) : end,
                            Stacks = s.Stacks,
                            Icon = facts?.Icon ?? 0,
                            Cat = facts?.Category ?? 0,
                        };
                        statuses.Add(span);
                        openStatus[key] = span;
                    }
                    else CloseStatus(key, s.T);
                    break;
                }
            }
        }
        statuses.Sort((a, b) => a.Start.CompareTo(b.Start));
        var partyOwn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rec.Combat != null)
            foreach (var st in rec.Combat.Statuses)
                if (rec.Statuses.TryGetValue(st.Status, out var info) && !string.IsNullOrWhiteSpace(info.Name))
                    partyOwn.Add(info.Name.Trim());

        // A hit is its cast's (not loose) when a cast of its action ended a second before it to 2.5 s after.
        var castEnds = new Dictionary<uint, List<double>>();
        foreach (var c in casts)
        {
            if (!castEnds.TryGetValue(c.Action, out var list)) castEnds[c.Action] = list = new List<double>();
            // One stopped never went off: its action's hits after aren't its.
            list.Add(c.Stopped != CastStop.None ? double.PositiveInfinity : c.End);
        }
        foreach (var h in hits)
            if (h.Loose && castEnds.TryGetValue(h.Action, out var ends) && ends.Any(e => h.T >= e - 1 && h.T <= e + 2.5))
                h.Loose = false;
        var aggro = aggroBy.Select(kv => new ReplayAggro(kv.Key, kv.Value.Where((s, i) => s.End > s.Start || i == kv.Value.Count - 1).ToList())).ToList();

        /* ---------- ground-placed party actions (plugin 0.4) ---------- */
        var areas = new List<ReplayArea>();
        if (rec.Combat != null)
            foreach (var a in rec.Combat.Areas)
            {
                var name = Name(a.Action);
                var lasts = AreaSeconds.TryGetValue(name, out var s) ? s : AreaDefault;
                var radius = rec.Actions.TryGetValue(a.Action, out var info) && info.Range > 0 ? info.Range : 5;
                areas.Add(new ReplayArea(a.T, a.Source, a.Action, name, a.X, a.Z, radius, Math.Min(end, a.T + lasts)));
            }

        // How far each knockback reaches: its own circle, else the arena.
        float HitboxOf(int id) => index.TryGetValue(id, out var i) ? entities[i].Hitbox : 0;
        foreach (var c in casts)
            if (c.Shape is { } sh && (sh.Kind == ShapeKind.Knockback || sh.Role == MechRole.Knockback))
                c.Reach = ReplayShapes.KnockbackReach(rec.Actions.GetValueOrDefault(c.Action), c.X, c.Z, HitboxOf(c.Src), arena);
        foreach (var h in hits)
            if (h.Shape is { } sh && (sh.Kind == ShapeKind.Knockback || sh.Role == MechRole.Knockback))
                h.Reach = ReplayShapes.KnockbackReach(rec.Actions.GetValueOrDefault(h.Action), h.X, h.Z, HitboxOf(h.Src), arena);

        if (!ReplayShapes.FacingRecorded(rec.PluginVersion)) FaceUnrecorded(casts, turns, entities, index, arena);

        // A cast whose caster never turned while it cast points where its own hit went (plugin 0.2.1 on).
        if (ReplayShapes.HitsFaceTheirWay(rec))
        {
            var own = hits.GroupBy(h => (h.Src, h.Action)).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var c in casts)
            {
                if (c.Stopped != CastStop.None || c.Shape is not { Pointing: true }) continue;
                if (turns.TryGetValue(c.Src, out var its) && its.Any(x => x.T >= c.T && x.T <= c.End)) continue;
                if (!own.TryGetValue((c.Src, c.Action), out var list)) continue;
                var h = list.FirstOrDefault(x => x.T >= c.End - 1 && x.T <= c.End + 2.5);
                if (h != null && float.IsFinite(h.Rot)) c.Rot = h.Rot;
            }
        }

        LinkMarkers(markers, hits, casts, entities.Where(e => e.Kind == EntityKind.Party).Select(e => e.Id).ToHashSet());

        return new ReplayTrack
        {
            End = end,
            Rate = rate,
            Entities = entities,
            Casts = casts,
            Hits = hits,
            Markers = markers,
            Tethers = tethers.Select(t => new ReplayTether(t.From, t.To, t.Id, t.Start, t.End)).ToList(),
            Waymarks = waymarks,
            Signs = signs,
            Aggro = aggro,
            Areas = areas,
            Deaths = deaths,
            Raises = raises,
            Statuses = statuses,
            Turns = turns,
            Rotations = ReplayRotations.Find(rec, s => ShapeOf(s)),
            Arena = arena,
            PartyOwnNames = partyOwn,
            Index = index,
            LongestCast = casts.Count > 0 ? casts.Max(c => c.End - c.T) : 0,
        };
    }

    /// <summary>
    /// Whether an enemy hit landed on a target (the site's <c>hitLanded</c>): not dodged (128), and not missed (16) unless
    /// it did damage (plugin 0.3's mistake for a status resisted).
    /// </summary>
    public static bool Landed(int fx, long damage) => (fx & 128) == 0 && ((fx & 16) == 0 || damage > 0);

    /// <summary>
    /// Through plugin 0.7.9 a cast's facing was never recorded (every cast faced south). A cast that points takes its
    /// caster's: its turns while it cast (drawn from them), else how it stood as it began (its frames), else its last turn
    /// before, else the facings of the same action's casters beside it that had one (one each), else, several from one
    /// spot, spread evenly round it half a step on from the action's last wave; a single one off the arena's middle aims
    /// across it (the site's rule, model/replay.ts).
    /// </summary>
    private static void FaceUnrecorded(List<ReplayCast> casts, Dictionary<int, List<TurnAt>> turns, List<ReplayEntity> entities, Dictionary<int, int> index,
        ArenaChoice arena)
    {
        var said = new List<ReplayCast>();
        var unsaid = new List<ReplayCast>();
        foreach (var c in casts)
        {
            if (c.Rot != 0 || c.Shape is not { Pointing: true }) continue;
            turns.TryGetValue(c.Src, out var its);
            if (its != null && its.Any(x => x.T >= c.T && x.T <= c.End)) continue;
            if (index.TryGetValue(c.Src, out var ei) && FacingAt(entities[ei], c.T) is { } r)
            {
                c.Rot = r;
                said.Add(c);
                continue;
            }
            var before = its?.LastOrDefault(x => x.T <= c.T);
            if (before is { } b && float.IsFinite(b.Rot))
            {
                c.Rot = b.Rot;
                said.Add(c);
                continue;
            }
            unsaid.Add(c);
        }
        var groups = unsaid.GroupBy(c => (c.Action, Math.Round(c.T * 2), MathF.Round(c.X), MathF.Round(c.Z))).Select(g => g.ToList()).OrderBy(g => g[0].T).ToList();
        var known = said.Select(c => (c.Action, c.T, c.Rot)).ToList();
        foreach (var group in groups)
        {
            var first = group[0];
            var pattern = said.Where(x => x.Action == first.Action && Math.Abs(x.T - first.T) <= 0.5).Select(x => MathF.Round(x.Rot * 1000) / 1000).Distinct().OrderBy(x => x).ToList();
            var bySrc = group.OrderBy(c => c.Src).ToList();
            if (pattern.Count >= group.Count && group.Count > 1)
            {
                for (var i = 0; i < bySrc.Count; i++)
                {
                    bySrc[i].Rot = pattern[i];
                    known.Add((bySrc[i].Action, bySrc[i].T, bySrc[i].Rot));
                }
                continue;
            }
            if (group.Count > 1)
            {
                var step = 2 * MathF.PI / group.Count;
                var earlier = known.Where(x => x.Action == first.Action && x.T < first.T - 0.5).ToList();
                var from = 0f;
                if (earlier.Count > 0)
                {
                    var at = earlier.Max(x => x.T);
                    from = earlier.Where(x => x.T >= at - 0.5).Min(x => x.Rot) + step / 2;
                }
                for (var i = 0; i < bySrc.Count; i++)
                {
                    bySrc[i].Rot = ReplayShapes.Wrap(from + i * step);
                    known.Add((bySrc[i].Action, bySrc[i].T, bySrc[i].Rot));
                }
                continue;
            }
            if (ReplayShapes.AcrossArena(arena, first.X, first.Z) is { } aim) first.Rot = aim;
        }
    }

    /// <summary>An entity's facing in its samples at <paramref name="t"/> (the last at or before it, within a second); null for none.</summary>
    private static float? FacingAt(ReplayEntity e, double t)
    {
        var i = Before(e.T, t);
        if (i < 0 || t - e.T[i] > 1) return null;
        var r = e.Rot[i];
        return float.IsFinite(r) ? r : null;
    }

    /// <summary>How long after a marker its mechanic can come (s); a limit cut's last number up to half a minute on.</summary>
    private const double MarkerReach = 12;

    private const double LimitCutReach = 30;

    /// <summary>
    /// Each head marker on a party member, the mechanic it led to (the site's <c>linkMarkers</c>): the first hit on the one
    /// marked, before their next marker and within 12 s (a limit cut's 30), by an enemy, that's a mechanic on a player: a
    /// spread, stack, buster or line stack by its role, a single-target buster with its own cast, or a circle that went
    /// off where they stood and hit at most two. Never a raidwide, a knockback or an auto-attack. Its drawing stands
    /// until the hit, or the start of the hit's own cast at them.
    /// </summary>
    public static void LinkMarkers(List<ReplayMarker> markers, IReadOnlyList<ReplayHit> hits, IReadOnlyList<ReplayCast> casts, IReadOnlySet<int> party)
    {
        var nextOn = new Dictionary<int, double>();
        for (var i = markers.Count - 1; i >= 0; i--)
        {
            var m = markers[i];
            if (!party.Contains(m.On)) continue;
            var hasNext = nextOn.TryGetValue(m.On, out var next);
            nextOn[m.On] = m.T;
            if (m.Meaning is MarkerMeaning.RotateCw or MarkerMeaning.RotateCcw) continue;
            var reach = m.T + (m.Meaning == MarkerMeaning.LimitCut ? LimitCutReach : MarkerReach);
            var last = hasNext && next > m.T ? Math.Min(reach, next) : reach;
            int lo = 0, hi = hits.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (hits[mid].T <= m.T) lo = mid + 1;
                else hi = mid;
            }
            for (var j = lo; j < hits.Count; j++)
            {
                var h = hits[j];
                if (h.T > last) break;
                if (party.Contains(h.Src) || h.Shape is not { } shape) continue;
                if (shape.Kind is ShapeKind.Raidwide or ShapeKind.Knockback || ReplayShapes.IsAutoAttack(h.Name)) continue;
                var k = Array.FindIndex(h.Targets, x => x.Id == m.On);
                if (k < 0) continue;
                var on = h.Targets[k];
                var puddle = shape.Kind == ShapeKind.Circle && h.Targets.Length <= 2 && float.IsFinite(on.X) && float.IsFinite(on.Z)
                    && (h.Hx != 0 || h.Hz != 0) && MathF.Sqrt((h.Hx - on.X) * (h.Hx - on.X) + (h.Hz - on.Z) * (h.Hz - on.Z)) <= 1.5f;
                var buster = shape.Kind == ShapeKind.Target && !h.Loose && h.Targets.Length == 1;
                var marked = shape.Role is MechRole.Spread or MechRole.Stack or MechRole.Tankbuster or MechRole.SharedTankbuster or MechRole.LineStack;
                if (!marked && !puddle && !buster) continue;
                m.Hit = j;
                if (m.Meaning == MarkerMeaning.None)
                    m.Role = buster ? MechRole.Tankbuster : shape.Role != MechRole.None ? shape.Role : MechRole.Spread;
                ReplayCast? own = null;
                if (!h.Loose)
                    foreach (var c in casts)
                        if (c.Action == h.Action && c.Target == m.On && c.T > m.T && c.End >= h.T - 1 && c.End <= h.T + 2.5)
                        {
                            own = c;
                            break;
                        }
                m.Until = own?.T ?? h.T;
                break;
            }
        }
    }

    /// <summary>The last index with <c>list[i] &lt;= t</c> (−1 when none).</summary>
    public static int Before(float[] list, double t)
    {
        int lo = 0, hi = list.Length - 1;
        if (hi < 0 || list[0] > t) return -1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (list[mid] <= t) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>
    /// Everyone at <paramref name="t"/> into <paramref name="into"/> (one a entity, by its index; at least as long as
    /// <see cref="Entities"/>): places linear between samples, facing the shorter way round, HP the last sample's (the
    /// site's <c>replayAt</c>). Someone is there from their first sample to a little after their last (a sample and a
    /// half); a gap of more than 3 s (or two and a half samples, for a thinned file) is their being away.
    /// </summary>
    public void At(double t, ReplayPoint[] into)
    {
        var rate = Math.Max(0.25f, Rate);
        var linger = 1.5 / rate;
        var away = Math.Max(3, 2.5 / rate);
        for (var k = 0; k < Entities.Count; k++)
        {
            var e = Entities[k];
            ref var p = ref into[k];
            p.Here = false;
            var n = e.T.Length;
            if (n == 0) continue;
            var i = Before(e.T, t);
            if (i < 0)
            {
                if (e.T[0] - t > 0.01) continue;
                Set(ref p, e, 0);
                continue;
            }
            if (i == n - 1)
            {
                if (t - e.T[i] > linger) continue;
                Set(ref p, e, i);
                continue;
            }
            double t0 = e.T[i], t1 = e.T[i + 1];
            if (t1 - t0 > away)
            {
                if (t - t0 > linger) continue;
                Set(ref p, e, i);
                continue;
            }
            var f = (float)(t1 > t0 ? Math.Clamp((t - t0) / (t1 - t0), 0, 1) : 0);
            p.Here = true;
            p.X = e.X[i] + (e.X[i + 1] - e.X[i]) * f;
            p.Z = e.Z[i] + (e.Z[i + 1] - e.Z[i]) * f;
            p.Rot = e.Rot[i] + ReplayShapes.Turn(e.Rot[i], e.Rot[i + 1]) * f;
            p.Hp = e.Hp[i];
        }
    }

    private static void Set(ref ReplayPoint p, ReplayEntity e, int i)
    {
        p.Here = true;
        p.X = e.X[i];
        p.Z = e.Z[i];
        p.Rot = e.Rot[i];
        p.Hp = e.Hp[i];
    }

    /// <summary>
    /// How the one casting stands now, when its turns show it turned or moved since its cast began (the site's
    /// <c>turnedSince</c>): its latest; else null.
    /// </summary>
    public TurnAt? TurnedSince(int src, double from, double t)
    {
        if (!Turns.TryGetValue(src, out var list) || list.Count == 0) return null;
        // The last at or before t (they're in time order), when it's since the cast began.
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (list[mid].T <= t) lo = mid + 1;
            else hi = mid;
        }
        if (lo == 0) return null;
        var x = list[lo - 1];
        return x.T >= from ? x : null;
    }
}
