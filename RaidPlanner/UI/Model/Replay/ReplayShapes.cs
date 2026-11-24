using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>An attack's shape, as the site's replay has it (model/recording.ts's <c>Shape.kind</c>).</summary>
public enum ShapeKind
{
    Circle,
    Cone,
    Donut,
    Rect,
    Cross,
    Charge,
    Target,
    Raidwide,
    Gaze,
    Knockback,
}

/// <summary>What a mechanic asks of the party (model/recording.ts's <c>Role</c>); <see cref="None"/> when not known.</summary>
public enum MechRole
{
    None,
    Stack,
    LineStack,
    Spread,
    Tankbuster,
    SharedTankbuster,
    Cleave,
    Tower,
    Raidwide,
    Knockback,
    Gaze,
    Other,
}

/// <summary>
/// An attack's shape in yalms (the site's <c>Shape</c>): a circle's radius, a cone's radius and angle, a donut's
/// outer and inner radii, a line's reach in front and behind and its width, a cross's arm and width, a charge's
/// width; <see cref="Hitbox"/> adds the caster's hitbox to the reach; <see cref="Offset"/> turns it from the
/// caster's facing (degrees, the game's way round).
/// </summary>
public sealed record ReplayShape(ShapeKind Kind, float R = 0, float Angle = 90, float Inner = 0, float Front = 0, float Back = 0, float Width = 4,
    float Arm = 0, bool Hitbox = false, float Offset = 0, MechRole Role = MechRole.None)
{
    /// <summary>Whether it points a way (its facing matters): a cone, a line, a cross, a charge, a knockback.</summary>
    public bool Pointing => Kind is ShapeKind.Cone or ShapeKind.Rect or ShapeKind.Cross or ShapeKind.Charge or ShapeKind.Knockback;
}

/// <summary>
/// The shapes and facts the replay reads off a recording (the site's model/recording.ts): an action's shape from the
/// game's own data (its cast type, reach and width; a few the data gets wrong fixed as the site fixes them), its name
/// as the codex says it, how far a knockback reaches, which way a caster off the arena's middle aims, and which
/// plugin versions recorded hits' and casts' facing. The site also reads BossMod's encounter data (a mechanic's role:
/// stack, spread, buster); the plugin hasn't it, so a circle on players is told a stack or a spread from who it hit
/// (<see cref="InferRoles"/>, the plugin's timeline's rule). Pure.
/// </summary>
public static class ReplayShapes
{
    /// <summary>Ability names that are the boss's ordinary swings, not mechanics (the site's <c>AUTO_ATTACK</c>).</summary>
    public static bool IsAutoAttack(string? name) =>
        name != null && AutoAttack.IsMatch(name.Trim());

    private static readonly Regex AutoAttack = new("^(attack|auto-attack|shot)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The game's ActionCategory of an auto-attack.</summary>
    private const int AutoAttackCategory = 1;

    /// <summary>
    /// An action's name from a recording (the site's <c>actionNameIn</c>): its own; an unnamed one the game counts as an
    /// auto-attack, "Auto-attack"; else "Action 31742".
    /// </summary>
    public static string ActionName(IReadOnlyDictionary<uint, ActionInfo> actions, uint id)
    {
        if (actions.TryGetValue(id, out var a))
        {
            var name = a.Name?.Trim();
            if (!string.IsNullOrEmpty(name)) return name;
            if (a.Category == AutoAttackCategory) return "Auto-attack";
        }
        return "Action " + id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A cone's angle from its floor marker's name ("gl_fan120_1bf"): 90 when it doesn't say.</summary>
    public static float OmenAngle(string? omen)
    {
        if (string.IsNullOrEmpty(omen)) return 90;
        var m = FanRe.Match(omen);
        if (!m.Success || !float.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)) return 90;
        return a > 0 && a <= 360 ? a : 90;
    }

    private static readonly Regex FanRe = new(@"fan(\d{2,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The shape the game's own action data gives (the site's <c>shapeFromGame</c>): circles at the target or on the
    /// caster, cones, lines, charges, donuts and crosses; a single target is a target. Null when it gives none.
    /// </summary>
    public static ReplayShape? FromGame(ActionInfo a)
    {
        var r = a.Range;
        var w = a.Width > 0 ? a.Width : 4;
        return a.CastType switch
        {
            1 => new ReplayShape(ShapeKind.Target),
            2 or 6 or 7 => r > 0 ? new ReplayShape(ShapeKind.Circle, R: r) : new ReplayShape(ShapeKind.Target),
            5 => r > 0 ? new ReplayShape(ShapeKind.Circle, R: r, Hitbox: true) : null,
            3 or 13 => r > 0 ? new ReplayShape(ShapeKind.Cone, R: r, Angle: OmenAngle(a.Omen), Hitbox: true) : null,
            4 => r > 0 ? new ReplayShape(ShapeKind.Rect, Front: r, Back: 0, Width: w, Hitbox: true) : null,
            12 => r > 0 ? new ReplayShape(ShapeKind.Rect, Front: r, Back: 0, Width: w) : null,
            8 => new ReplayShape(ShapeKind.Charge, Width: w),
            // The game keeps no inner radius; half the outer is the usual.
            10 => r > 0 ? new ReplayShape(ShapeKind.Donut, R: r, Inner: r / 2, Hitbox: true) : null,
            11 => r > 0 ? new ReplayShape(ShapeKind.Cross, Arm: r, Width: w) : null,
            _ => null,
        };
    }

    /// <summary>
    /// A few shapes the game's data gets wrong (the site's <c>SHAPE_FIXES</c>, from BossMod Reborn's Ex8Seiryu): Great
    /// Typhoon's three rings, Serpent-eye Sigil's hole, the adds' Explosion a raidwide. Never in a field zone.
    /// </summary>
    public static readonly IReadOnlyDictionary<uint, ReplayShape> Fixes = new Dictionary<uint, ReplayShape>
    {
        [14352] = new(ShapeKind.Donut, R: 28, Inner: 20),
        [14353] = new(ShapeKind.Donut, R: 34, Inner: 26),
        [14354] = new(ShapeKind.Donut, R: 40, Inner: 32),
        [14856] = new(ShapeKind.Donut, R: 30, Inner: 7),
        [14347] = new(ShapeKind.Raidwide, Role: MechRole.Raidwide),
        [14348] = new(ShapeKind.Raidwide, Role: MechRole.Raidwide),
    };

    /// <summary>An action's shape (a fix, else the game's), with the role the pull says it has; null when neither knows.</summary>
    public static ReplayShape? Of(Recording r, uint action, IReadOnlyDictionary<uint, MechRole>? roles = null)
    {
        ReplayShape? shape = null;
        if (!r.Duty.Field && Fixes.TryGetValue(action, out var fix)) shape = fix;
        else if (r.Actions.TryGetValue(action, out var a)) shape = FromGame(a);
        if (shape != null && shape.Role == MechRole.None && roles != null && roles.TryGetValue(action, out var role)) shape = shape with { Role = role };
        return shape;
    }

    /// <summary>
    /// What the plugin can tell of a circle on players' role from the pull itself (in the site's place, the encounter
    /// data's): for each enemy action drawn as a circle (cast type 2, 6 or 7, smaller than the arena), the times it went
    /// off (its hits within 0.3 s of each other): one or two hits each on two people or more is a stack; two hits or more
    /// each on one, or one cast at a player that hit only them, a spread. Its role is what most of its times say, when
    /// it was put on players (cast at one, or going off where one stood); else none (a puddle on the floor).
    /// </summary>
    public static Dictionary<uint, MechRole> InferRoles(Recording r)
    {
        var party = new HashSet<int>(r.Party.Select(p => p.Id));
        var roles = new Dictionary<uint, MechRole>();
        var castAt = new Dictionary<uint, bool>();
        foreach (var c in r.Events.OfType<CastEvent>())
            if (!party.Contains(c.Source) && c.Target != 0 && party.Contains(c.Target))
                castAt[c.Action] = true;
        foreach (var group in r.Events.OfType<HitEvent>().Where(h => !party.Contains(h.Source)).GroupBy(h => h.Action))
        {
            if (!r.Actions.TryGetValue(group.Key, out var a) || a.CastType is not (2 or 6 or 7) || a.Range <= 0 || a.Range >= 30) continue;
            if (!r.Duty.Field && Fixes.ContainsKey(group.Key)) continue;
            var hits = group.OrderBy(h => h.T).ToList();
            var onPlayer = castAt.ContainsKey(group.Key) || hits.Any(h => OnSomeone(h));
            if (!onPlayer) continue;
            int stack = 0, spread = 0, times = 0;
            for (var i = 0; i < hits.Count;)
            {
                var j = i;
                var most = 0;
                while (j < hits.Count && hits[j].T - hits[i].T <= 0.3)
                {
                    most = Math.Max(most, hits[j].Targets.Count(party.Contains));
                    j++;
                }
                var events = j - i;
                times++;
                if (most >= 2 && events <= 2) stack++;
                else if (events >= 2 && most <= 1) spread++;
                else if (events == 1 && most == 1 && castAt.ContainsKey(group.Key)) spread++;
                i = j;
            }
            if (stack * 2 > times) roles[group.Key] = MechRole.Stack;
            else if (spread * 2 > times) roles[group.Key] = MechRole.Spread;
        }
        return roles;
    }

    /// <summary>A hit that went off where one of those it hit stood (within 1.5 yalms): put on a player.</summary>
    private static bool OnSomeone(HitEvent h)
    {
        if ((h.HitX == 0 && h.HitZ == 0) || h.Px == null || h.Pz == null) return false;
        for (var i = 0; i < h.Targets.Count && i < h.Px.Count; i++)
        {
            var x = h.Px[i];
            var z = h.Pz[i];
            if (float.IsFinite(x) && float.IsFinite(z) && MathF.Sqrt((h.HitX - x) * (h.HitX - x) + (h.HitZ - z) * (h.HitZ - z)) <= 1.5f) return true;
        }
        return false;
    }

    /// <summary>
    /// How far a knockback reaches from its caster, in yalms (the site's <c>knockbackReach</c>): a circle of the game's
    /// own (cast type 2, 5 with the caster's hitbox, 7) its range; else the whole arena from where the caster stands.
    /// </summary>
    public static float KnockbackReach(ActionInfo? game, float x, float z, float hitbox, ArenaChoice arena)
    {
        if (game != null && game.Range > 0 && game.CastType is 2 or 5 or 7) return game.Range + (game.CastType == 5 ? hitbox : 0);
        return MathF.Sqrt((x - arena.Cx) * (x - arena.Cx) + (z - arena.Cz) * (z - arena.Cz)) + MathF.Max(arena.Width, arena.Depth) / 2;
    }

    /// <summary>The plugin's version as three numbers ("0.7.10" is 0, 7, 10); missing parts 0.</summary>
    public static (int Major, int Minor, int Patch) Version(string? plugin)
    {
        var parts = (plugin ?? string.Empty).Split('.');
        int At(int i) => i < parts.Length && int.TryParse(new string(parts[i].TakeWhile(char.IsDigit).ToArray()), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
        return (At(0), At(1), At(2));
    }

    /// <summary>Plugin 0.7.10 on records a cast's facing (its caster's as it began); before, every cast's was 0 (the site's <c>facingRecorded</c>).</summary>
    public static bool FacingRecorded(string? plugin)
    {
        var (a, b, c) = Version(plugin);
        return a > 0 || b > 7 || (b == 7 && c >= 10);
    }

    /// <summary>
    /// Whether a recording's hits face their own way (plugin 0.2.1 on; the site's <c>hitsFaceTheirWay</c>): its version,
    /// or anything only a later plugin writes (the combat block, a hit's damage).
    /// </summary>
    public static bool HitsFaceTheirWay(Recording r)
    {
        var (a, b, c) = Version(r.PluginVersion);
        if (a > 0 || b > 2 || (b == 2 && c >= 1)) return true;
        return r.Combat != null || r.Events.OfType<HitEvent>().Any(h => h.Damage != null);
    }

    /// <summary>
    /// Which way a caster standing off the arena's centre aims across it, in the game's radians (0 south), when nothing
    /// recorded says (the site's <c>acrossArena</c>): a round arena's at the centre; a square or rectangular one's
    /// straight across from its nearer side. Null near the centre (within a quarter of the arena's size).
    /// </summary>
    public static float? AcrossArena(ArenaChoice arena, float x, float z)
    {
        var dx = arena.Cx - x;
        var dz = arena.Cz - z;
        if (MathF.Sqrt(dx * dx + dz * dz) < MathF.Min(arena.Width, arena.Depth) / 4) return null;
        if (arena.Shape == "circle") return MathF.Atan2(dx, dz);
        return MathF.Abs(dx) / MathF.Max(1, arena.Width) >= MathF.Abs(dz) / MathF.Max(1, arena.Depth)
            ? MathF.Atan2(MathF.Sign(dx), 0)
            : MathF.Atan2(0, MathF.Sign(dz));
    }

    /// <summary>The plan's facing (degrees, 0 north, clockwise) for the game's (radians, 0 south, anticlockwise from above).</summary>
    public static float PlanRotation(float gameRot)
    {
        var d = (180 - gameRot * 180 / MathF.PI) % 360;
        return MathF.Round((d < 0 ? d + 360 : d) * 10) / 10;
    }

    /// <summary>The angle from <paramref name="a"/> to <paramref name="b"/> the shorter way round (radians, −π…π).</summary>
    public static float Turn(float a, float b)
    {
        var d = (b - a) % (2 * MathF.PI);
        if (d > MathF.PI) d -= 2 * MathF.PI;
        if (d < -MathF.PI) d += 2 * MathF.PI;
        return d;
    }

    /// <summary>An angle in radians brought into (−π, π], to three places.</summary>
    public static float Wrap(float a)
    {
        var r = ((a + MathF.PI) % (2 * MathF.PI) + 2 * MathF.PI) % (2 * MathF.PI) - MathF.PI;
        return MathF.Round((r <= -MathF.PI + 1e-6f ? MathF.PI : r) * 1000) / 1000;
    }
}
