using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RaidPlanner.Core;

/// <summary>One of your plans as the site lists them (<c>rp_plugin_plans</c>): the duty it's for, when there's one.</summary>
public sealed record PlanListItem(string Id, string Name, DateTimeOffset? UpdatedAt, uint? Content, string? Duty)
{
    /// <summary>The site's own time it was saved, as text: a plan read again only when this changes.</summary>
    public string Stamp { get; init; } = string.Empty;

    /// <summary>How many steps it has, when the site says (the plans menu shows "19 steps"); null when it doesn't.</summary>
    public int? Steps { get; init; }
}

/// <summary>What a board object is, as the plugin's small board draws it.</summary>
public enum BoardKind
{
    Other,
    Player,
    Enemy,
    Circle,
    Donut,
    Cone,
    Protean,
    Rect,
    LineStack,
    Cross,
    Waymark,
    Marker,
    Tether,
    Line,
    Arrow,
    Text,
    Stack,
    Spread,
    Defamation,
    Tankbuster,
    SharedBuster,
    Tower,
    Proximity,
    Puddle,
    Landmine,
    Safe,
    Danger,
    Moving,
    Gaze,
    Knockback,
    DrawIn,
    KnockUp,
    SideKnockback,
    Rotation,
    Tiles,
    LineOfSight,
    Pen,
}

/// <summary>
/// An object on a step's board, as the site's plan has it (board units: ±500, the arena within ±420,
/// y down; rotation in degrees, 0 north, clockwise). Only what the small board draws is kept.
/// </summary>
public sealed record BoardObj(
    BoardKind Kind,
    string Type,
    float X,
    float Y,
    float Rotation,
    float Size,
    uint Color,
    float Opacity,
    string? Label,
    string? Variant,
    float Width,
    float Length,
    float Angle,
    float Inner,
    float Dx,
    float Dy,
    int? Layer)
{
    /// <summary>How many there are (a protean's baits, a tower's soakers, a stack's group); 0 when not set.</summary>
    public int Count { get; init; }

    /// <summary>Which hit it is of a run (1 first, 2 next, …; 0 none): later ones are drawn hollow with a numbered badge.</summary>
    public int Order { get; init; }

    /// <summary>How far a mechanic reaches past its size (a moving AoE's travel ahead), board units.</summary>
    public float Reach { get; init; }

    /// <summary>A moving AoE's step between hits, board units.</summary>
    public float Spacing { get; init; }

    /// <summary>A line or arrow drawn dashed.</summary>
    public bool Dashed { get; init; }

    /// <summary>A boss without its monster icon: just the hitbox ring and its facing.</summary>
    public bool NoIcon { get; init; }

    /// <summary>A pen stroke's points, flat (x0, y0, x1, y1, …), relative to the object.</summary>
    public float[]? Points { get; init; }
}

/// <summary>The arena a step is drawn on: its shape, proportions, outlines (custom shapes) and floor colour.</summary>
public sealed record PlanArena(string Shape, float Aspect, IReadOnlyList<float[]> Outlines, uint Color)
{
    public static PlanArena Default { get; } = new("circle", 1, Array.Empty<float[]>(), 0x1b1c21);

    /// <summary>The arena's half-width in yalms (the board's ±420 is this many): the board's marks are sized by it.</summary>
    public float Yalms { get; init; } = 20;

    /// <summary>What's at the edge: a wall, a death wall (ember) or a drop (dashed).</summary>
    public string Edge { get; init; } = "wall";

    /// <summary>The floor's grid: none, square, radial or both.</summary>
    public string Grid { get; init; } = "none";
}

/// <summary>A step of a plan (a phase on the site): its name, time ("1:24"), notes and board; its id on the site, when it has one.</summary>
public sealed record PlanStep(string Name, string Time, string Notes, IReadOnlyList<BoardObj> Objects, PlanArena? Arena)
{
    public string? Id { get; init; }

    /// <summary>The step in the words a raid lead says on voice ("Out, then in"), when it has them (the site's <c>phase.callout</c>).</summary>
    public string? Callout { get; init; }

    /// <summary>What's said big for the step: its callout, else its name (without a "P2 · " in front).</summary>
    public string Big => Callout is { Length: > 0 } c ? c : PlanJson.WithoutPhase(Name);
}

/// <summary>Where a step's first mechanic goes off in the fight: the step, its action, seconds from the pull.</summary>
public readonly record struct PlanCue(int Step, uint Action, double T);

/// <summary>A plan's link to the game (contract §2): the duty, and the cues that move the plan along during a pull.</summary>
public sealed record PlanGame(uint Content, uint Territory, string Duty, IReadOnlyList<PlanCue> Cues);

/// <summary>A plan as the plugin shows it (<c>rp_plugin_plan</c>).</summary>
public sealed record Plan(string Id, string Name, string Stamp, PlanArena Arena, IReadOnlyList<PlanStep> Steps, PlanGame? Game)
{
    /// <summary>The arena a step is drawn on: the last one set at or before it, else the plan's own.</summary>
    public PlanArena ArenaAt(int step)
    {
        for (var i = Math.Min(step, Steps.Count - 1); i >= 1; i--)
            if (Steps[i].Arena is { } a) return a;
        return Arena;
    }
}

/// <summary>
/// Reads what the site sends back for plans, trusting none of it: anything missing, of the wrong
/// kind, not a finite number or too long is left out, cut short or given its usual value, and there
/// are caps on everything that can grow (plans, steps, objects, cues, outlines).
/// </summary>
public static class PlanJson
{
    public const int MaxPlans = 200;
    public const int MaxSteps = 400;
    public const int MaxObjects = 600;
    public const int MaxCues = 500;
    public const int MaxOutlines = 16;
    public const int MaxOutlinePoints = 512;
    public const int MaxNotes = 4000;
    public const int MaxCallout = 200;
    public const int MaxPenPoints = 256;

    /// <summary>Board units objects are kept within (the board is ±500; a little past it is still drawn at the edge).</summary>
    private const float Far = 5000;

    /// <summary>The list of your plans; an empty list for anything that isn't one.</summary>
    public static List<PlanListItem> List(string? json)
    {
        var list = new List<PlanListItem>();
        if (Parse(json) is not JsonArray arr) return list;
        foreach (var n in arr)
        {
            if (list.Count >= MaxPlans) break;
            if (n is not JsonObject o) continue;
            var id = Str(o["id"], 64);
            if (string.IsNullOrWhiteSpace(id)) continue;
            var stamp = Str(o["updated_at"], 64) ?? string.Empty;
            list.Add(new PlanListItem(id, Name(o["name"]), When(stamp), UInt(o["content"]) is { } c and > 0 ? c : null, Str(o["duty"], 120))
            {
                Stamp = stamp,
                Steps = UInt(o["steps"]) is { } n and <= MaxSteps * 10 ? (int)n : null,
            });
        }
        return list;
    }

    /// <summary>One plan, or null for anything that isn't one (no id, no steps).</summary>
    public static Plan? Read(string? json)
    {
        if (Parse(json) is not JsonObject o) return null;
        var id = Str(o["id"], 64);
        if (string.IsNullOrWhiteSpace(id)) return null;
        var steps = new List<PlanStep>();
        if (o["phases"] is JsonArray phases)
            foreach (var p in phases)
            {
                if (steps.Count >= MaxSteps) break;
                if (p is JsonObject po) steps.Add(Step(po, steps.Count));
            }
        if (steps.Count == 0) return null;
        var arena = o["arena"] is JsonObject a ? Arena(a) : PlanArena.Default;
        return new Plan(id, Name(o["name"]), Str(o["updated_at"], 64) ?? string.Empty, arena, steps, o["game"] is JsonObject g ? Game(g, steps) : null);
    }

    private static JsonNode? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Name(JsonNode? n)
    {
        var s = Str(n, 200)?.Trim();
        return string.IsNullOrEmpty(s) ? "Untitled plan" : s;
    }

    private static PlanStep Step(JsonObject p, int index)
    {
        var name = Str(p["name"], 200)?.Trim();
        var objects = new List<BoardObj>();
        if (p["objects"] is JsonArray arr)
            foreach (var n in arr)
            {
                if (objects.Count >= MaxObjects) break;
                if (n is JsonObject o && Obj(o) is { } obj) objects.Add(obj);
            }
        var id = Str(p["id"], 64);
        var callout = Str(p["callout"], MaxCallout)?.Trim();
        return new PlanStep(string.IsNullOrEmpty(name) ? $"Step {index + 1}" : name, Str(p["time"], 20)?.Trim() ?? string.Empty,
            Str(p["notes"], MaxNotes) ?? string.Empty, objects, p["arena"] is JsonObject a ? Arena(a) : null)
        {
            Id = string.IsNullOrEmpty(id) ? null : id,
            Callout = string.IsNullOrEmpty(callout) ? null : callout,
        };
    }

    private static BoardObj? Obj(JsonObject o)
    {
        var type = Str(o["type"], 40);
        if (type == null) return null;
        var x = Num(o["x"]);
        var y = Num(o["y"]);
        if (x is null || y is null) return null;
        var kind = KindOf(type);
        var size = Clamp(Num(o["size"]) ?? 20, 0, Far);
        // The site's own defaults where they differ: a protean's cones are 30°, a line stack 60 wide.
        var protean = type == "protean";
        return new BoardObj(kind, type, Clamp(x.Value, -Far, Far), Clamp(y.Value, -Far, Far), (float)((Num(o["rotation"]) ?? 0) % 360), size,
            Color(o["color"], kind), Clamp(Num(o["opacity"]) ?? 0.35, 0, 1), Str(o["label"], 40), Str(o["variant"], 24),
            Clamp(Num(o["width"]) ?? (type == "linestack" ? 60 : 0), 0, Far), Clamp(Num(o["length"]) ?? 0, 0, Far),
            Clamp(Num(o["angle"]) ?? (protean ? 30 : 90), 0, 360),
            Clamp(Num(o["inner"]) ?? 0, 0, Far), Clamp(Num(o["dx"]) ?? 0, -Far, Far), Clamp(Num(o["dy"]) ?? 0, -Far, Far),
            Num(o["layer"]) is { } l ? (int)Math.Clamp(l, -1000, 1000) : null)
        {
            Count = Num(o["count"]) is { } n ? (int)Math.Clamp(Math.Round(n), 0, 64) : 0,
            Order = Num(o["order"]) is { } ord ? (int)Math.Clamp(Math.Round(ord), 0, 99) : 0,
            Reach = Clamp(Num(o["reach"]) ?? 0, 0, Far),
            Spacing = Clamp(Num(o["spacing"]) ?? 0, 0, Far),
            Dashed = o["dashed"] is JsonValue dv && dv.TryGetValue<bool>(out var dashed) && dashed,
            NoIcon = o["noIcon"] is JsonValue nv && nv.TryGetValue<bool>(out var noIcon) && noIcon,
            Points = kind == BoardKind.Pen ? Points(o["points"]) : null,
        };
    }

    /// <summary>A pen stroke's points (flat pairs, relative), capped and kept within reach; null when there are fewer than two.</summary>
    private static float[]? Points(JsonNode? n)
    {
        if (n is not JsonArray arr) return null;
        var count = Math.Min(arr.Count, MaxPenPoints * 2) & ~1;
        if (count < 4) return null;
        var flat = new float[count];
        for (var i = 0; i < count; i++)
        {
            if (Num(arr[i]) is not { } v) return null;
            flat[i] = Clamp(v, -Far, Far);
        }
        return flat;
    }

    /// <summary>How the small board draws a type: the area attacks by their shape, mechanics on a spot as circles, the rest by kind.</summary>
    public static BoardKind KindOf(string type) => type switch
    {
        "player" => BoardKind.Player,
        "enemy" => BoardKind.Enemy,
        "circle" => BoardKind.Circle,
        "stack" or "multistack" => BoardKind.Stack,
        "spread" => BoardKind.Spread,
        "defamation" => BoardKind.Defamation,
        "tankbuster" => BoardKind.Tankbuster,
        "sharedbuster" => BoardKind.SharedBuster,
        "tower" => BoardKind.Tower,
        "proximity" or "targetproximity" => BoardKind.Proximity,
        "puddle" => BoardKind.Puddle,
        "landmine" => BoardKind.Landmine,
        "safe" => BoardKind.Safe,
        "danger" => BoardKind.Danger,
        "moving" => BoardKind.Moving,
        "gaze" => BoardKind.Gaze,
        "knockback" => BoardKind.Knockback,
        "drawin" => BoardKind.DrawIn,
        "knockup" => BoardKind.KnockUp,
        "sideknockback" => BoardKind.SideKnockback,
        "rotation" => BoardKind.Rotation,
        "tiles" => BoardKind.Tiles,
        "los" => BoardKind.LineOfSight,
        "pen" => BoardKind.Pen,
        "donut" => BoardKind.Donut,
        "cone" => BoardKind.Cone,
        "protean" => BoardKind.Protean,
        "rect" => BoardKind.Rect,
        "linestack" => BoardKind.LineStack,
        "cross" => BoardKind.Cross,
        "waymark" => BoardKind.Waymark,
        "sign" or "limitcut" or "psmarker" or "movelight" => BoardKind.Marker,
        "tether" => BoardKind.Tether,
        "line" => BoardKind.Line,
        "arrow" => BoardKind.Arrow,
        "text" => BoardKind.Text,
        _ => BoardKind.Other,
    };

    /// <summary>
    /// Where a type draws by default, back (0) to front, as the site's <c>TYPE_INFO</c> layers:
    /// waymarks 0, area attacks and mechanics 1, lines, tethers, text and spots 2, enemies 3, gazes 4,
    /// players 5, markers over heads 6. An object's own <c>layer</c> (To front 7, To back -1) wins.
    /// </summary>
    public static int DefaultLayer(string type) => type switch
    {
        "waymark" => 0,
        "rotation" or "tether" or "text" or "arrow" or "line" or "pen" or "safe" or "danger" => 2,
        "enemy" => 3,
        "gaze" => 4,
        "player" => 5,
        "sign" or "limitcut" or "psmarker" or "movelight" => 6,
        _ => 1,
    };

    private static PlanArena Arena(JsonObject a)
    {
        var outlines = new List<float[]>();
        if (a["outlines"] is JsonArray arr)
            foreach (var n in arr)
            {
                if (outlines.Count >= MaxOutlines) break;
                if (n is not JsonArray pts) continue;
                var count = Math.Min(pts.Count, MaxOutlinePoints * 2) & ~1;
                var flat = new float[count];
                var ok = count >= 6;
                for (var i = 0; i < count && ok; i++)
                {
                    if (Num(pts[i]) is { } v) flat[i] = Clamp(v, -Far, Far);
                    else ok = false;
                }
                if (ok) outlines.Add(flat);
            }
        var shape = Str(a["shape"], 16) is ("circle" or "square" or "rectangle" or "custom") and var s ? s : "circle";
        return new PlanArena(shape, Clamp(Num(a["aspect"]) ?? 1, 0.1, 10), outlines, Hex(Str(a["color"], 16)) ?? PlanArena.Default.Color)
        {
            Yalms = Clamp(Num(a["yalms"]) ?? 20, 1, 200),
            Edge = Str(a["edge"], 8) is ("wall" or "death" or "drop") and var e ? e : "wall",
            Grid = Str(a["grid"], 8) is ("square" or "radial" or "both") and var g ? g : "none",
        };
    }

    /// <summary>
    /// The plan's link to the game. Each cue's step is the one whose id is its <c>phase</c> (steps
    /// can be moved or deleted after the plan is made); a cue whose step isn't there any more is
    /// left out. Only a cue with no <c>phase</c> goes by its <c>step</c> number, when that's in range.
    /// </summary>
    private static PlanGame? Game(JsonObject g, IReadOnlyList<PlanStep> stepList)
    {
        if (UInt(g["content"]) is not { } content) return null;
        var steps = stepList.Count;
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < stepList.Count; i++)
            if (stepList[i].Id is { } sid) byId.TryAdd(sid, i);
        var cues = new List<PlanCue>();
        if (g["cues"] is JsonArray arr)
            foreach (var n in arr)
            {
                if (cues.Count >= MaxCues) break;
                if (n is not JsonObject c) continue;
                int step;
                if (c["phase"] is { } phaseNode)
                {
                    // A cue that names its step: that step, or none (it was deleted), never the number.
                    if (Str(phaseNode, 64) is { } phase && byId.TryGetValue(phase, out var at)) step = at;
                    else continue;
                }
                else if (Num(c["step"]) is { } k && k >= 0 && k < steps && k == Math.Floor(k)) step = (int)k;
                else continue;
                if (UInt(c["action"]) is not { } action || action == 0) continue;
                if (Num(c["t"]) is not { } t || t < 0 || t > 6 * 3600) continue;
                cues.Add(new PlanCue(step, action, t));
            }
        cues.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.Step.CompareTo(b.Step));
        return new PlanGame(content, UInt(g["territory"]) ?? 0, Str(g["duty"], 120) ?? string.Empty, cues);
    }

    /// <summary>A colour as "#rrggbb" or "#rgb"; else the kind's usual (the site's grey for players, its orange for attacks).</summary>
    private static uint Color(JsonNode? n, BoardKind kind) => Hex(Str(n, 16)) ?? kind switch
    {
        BoardKind.Player => 0x7d7d86,
        BoardKind.Enemy => 0xb44cf0,
        BoardKind.Tether => 0xc084fc,
        BoardKind.Text or BoardKind.Line or BoardKind.Arrow or BoardKind.Pen => 0xf5f5f6,
        BoardKind.Stack => 0xffcf4a,
        BoardKind.Spread => 0xb46cff,
        BoardKind.Tankbuster or BoardKind.SharedBuster => 0xff4f4f,
        BoardKind.Tower => 0x7aa2ff,
        BoardKind.Defamation => 0xff5fa2,
        BoardKind.Puddle => 0x2dd4bf,
        BoardKind.Gaze => 0xe8383d,
        BoardKind.Safe => 0x3ddc84,
        BoardKind.Danger => 0xff5a5a,
        _ => 0xff8a3d,
    };

    public static uint? Hex(string? s)
    {
        if (s == null || s.Length is not (4 or 7) || s[0] != '#') return null;
        if (!uint.TryParse(s.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v)) return null;
        if (s.Length == 7) return v;
        var r = (v >> 8) & 0xF;
        var g = (v >> 4) & 0xF;
        var b = v & 0xF;
        return (r * 17 << 16) | (g * 17 << 8) | (b * 17);
    }

    private static DateTimeOffset? When(string s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    private static float Clamp(double v, double lo, double hi) => (float)Math.Clamp(v, lo, hi);

    private static string? Str(JsonNode? n, int max)
    {
        if (n is not JsonValue v || !v.TryGetValue<string>(out var s)) return null;
        return s.Length <= max ? s : s[..max];
    }

    private static double? Num(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d) || (v.TryGetValue<long>(out var l) && (d = l) == l)) return double.IsFinite(d) ? d : null;
        return null;
    }

    private static uint? UInt(JsonNode? n) => Num(n) is { } d && d >= 0 && d <= uint.MaxValue && d == Math.Floor(d) ? (uint)d : null;

    /// <summary>A step named for its phase ("P2 · Exaflares", "Phase 3: …"), as the site's timeline reads it (model/timeline.ts PHASE_PREFIX).</summary>
    private static readonly System.Text.RegularExpressions.Regex PhasePrefix =
        new(@"^\s*(P\d{1,2}|Phase\s+\d{1,2})\s*[·:—–-]\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The phase a step's name says it's in ("P2"), or null.</summary>
    public static string? PhaseOf(string name)
    {
        var m = PhasePrefix.Match(name);
        if (!m.Success) return null;
        var p = m.Groups[1].Value.Trim();
        return p.StartsWith("phase", StringComparison.OrdinalIgnoreCase) ? "P" + p[5..].Trim() : p.ToUpperInvariant();
    }

    /// <summary>A step's name without the phase in front of it ("P2 · Exaflares" is "Exaflares").</summary>
    public static string WithoutPhase(string name)
    {
        var m = PhasePrefix.Match(name);
        var rest = m.Success ? name[m.Length..].Trim() : name.Trim();
        return rest.Length > 0 ? rest : name.Trim();
    }

    /// <summary>
    /// The phase each step is in, as Present says it ("P2"), from the steps' names: a step named for a
    /// phase starts it, the steps after it are in it, and the steps before the first one named are P1
    /// (when a later phase is named). Null for every step of a plan that names none.
    /// </summary>
    public static string?[] Phases(IReadOnlyList<PlanStep> steps)
    {
        var phases = new string?[steps.Count];
        string? current = null;
        var first = -1;
        for (var i = 0; i < steps.Count; i++)
        {
            if (PhaseOf(steps[i].Name) is { } p)
            {
                current = p;
                if (first < 0) first = i;
            }
            phases[i] = current;
        }
        if (first > 0 && phases[first] != "P1")
            for (var i = 0; i < first; i++) phases[i] = "P1";
        return phases;
    }

    /// <summary>
    /// What chat says five seconds before a step: its name, then its callout as a raid lead would say
    /// it ("Exaflares: middle, step through the first."); the name alone when it has no callout.
    /// </summary>
    public static string ChatLine(PlanStep step)
    {
        var name = WithoutPhase(step.Name);
        var callout = step.Callout?.Trim();
        if (string.IsNullOrEmpty(callout) || string.Equals(callout, name, StringComparison.OrdinalIgnoreCase)) return name;
        // "Middle, step …" reads on after a colon as "middle, step …"; "MT soaks" keeps its capitals.
        if (callout.Length > 1 && char.IsUpper(callout[0]) && char.IsLower(callout[1])) callout = char.ToLowerInvariant(callout[0]) + callout[1..];
        if (!".!?…".Contains(callout[^1], StringComparison.Ordinal)) callout += ".";
        return $"{name}: {callout}";
    }

    /// <summary>A step's notes, its first line with something on it (a "- " list mark left off): what a callout says.</summary>
    public static string FirstLine(string? notes)
    {
        if (string.IsNullOrEmpty(notes)) return string.Empty;
        foreach (var raw in notes.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)) line = line[2..].Trim();
            if (line.Length > 0) return line.Length <= 200 ? line : line[..200].TrimEnd() + "…";
        }
        return string.Empty;
    }
}
