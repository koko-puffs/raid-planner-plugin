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
    /// <summary>How many there are (a protean's baits); 0 when not set.</summary>
    public int Count { get; init; }
}

/// <summary>The arena a step is drawn on: its shape, proportions, outlines (custom shapes) and floor colour.</summary>
public sealed record PlanArena(string Shape, float Aspect, IReadOnlyList<float[]> Outlines, uint Color)
{
    public static PlanArena Default { get; } = new("circle", 1, Array.Empty<float[]>(), 0x2a2a2f);
}

/// <summary>A step of a plan (a phase on the site): its name, time ("1:24"), notes and board; its id on the site, when it has one.</summary>
public sealed record PlanStep(string Name, string Time, string Notes, IReadOnlyList<BoardObj> Objects, PlanArena? Arena)
{
    public string? Id { get; init; }
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
        return new PlanStep(string.IsNullOrEmpty(name) ? $"Step {index + 1}" : name, Str(p["time"], 20)?.Trim() ?? string.Empty,
            Str(p["notes"], MaxNotes) ?? string.Empty, objects, p["arena"] is JsonObject a ? Arena(a) : null)
        {
            Id = string.IsNullOrEmpty(id) ? null : id,
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
        };
    }

    /// <summary>How the small board draws a type: the area attacks by their shape, mechanics on a spot as circles, the rest by kind.</summary>
    public static BoardKind KindOf(string type) => type switch
    {
        "player" => BoardKind.Player,
        "enemy" => BoardKind.Enemy,
        "circle" or "puddle" or "landmine" or "proximity" or "targetproximity" or "stack" or "multistack" or "spread"
            or "tankbuster" or "sharedbuster" or "defamation" or "tower" or "safe" or "danger" => BoardKind.Circle,
        "donut" => BoardKind.Donut,
        "cone" => BoardKind.Cone,
        "protean" => BoardKind.Protean,
        "rect" or "sideknockback" => BoardKind.Rect,
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
        return new PlanArena(shape, Clamp(Num(a["aspect"]) ?? 1, 0.1, 10), outlines, Hex(Str(a["color"], 16)) ?? PlanArena.Default.Color);
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
        BoardKind.Enemy => 0xd94848,
        BoardKind.Tether => 0xc084fc,
        BoardKind.Text or BoardKind.Line or BoardKind.Arrow => 0xf5f5f6,
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
