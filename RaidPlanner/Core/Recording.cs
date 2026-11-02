using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RaidPlanner.Core;

/// <summary>How a pull ended.</summary>
public enum Outcome
{
    /// <summary>Combat ended without the game saying the duty was won or lost (left, stopped by hand).</summary>
    Ended,

    /// <summary>The party wiped (the game's duty wiped).</summary>
    Wipe,

    /// <summary>The duty was completed.</summary>
    Clear,
}

/// <summary>The duty a pull was in: its name as the game says it, and the game's ids for it.</summary>
public sealed record DutyInfo(string Name, uint Territory, uint Content);

/// <summary>
/// One player in the party, as the plan will have them: a spot (MT, OT, H1…), their job and role.
/// Names are never kept. <paramref name="Id"/> is the recording's own number for them (1 to 8, or more in an alliance).
/// </summary>
public sealed record PartyMember(int Id, string Slot, string Job, string Role);

/// <summary>What an enemy or other actor in the fight is, as the plan draws it.</summary>
public enum ActorKind
{
    /// <summary>A boss: the biggest targetable enemy, and any as big.</summary>
    Boss,

    /// <summary>A smaller targetable enemy.</summary>
    Add,

    /// <summary>Never targetable: an invisible actor that only casts (the game's helpers). Not drawn, only its attacks.</summary>
    Helper,
}

/// <summary>An enemy (or a helper) seen in the fight. <paramref name="Id"/> is the recording's own number (101 up).</summary>
public sealed record Actor(int Id, string Name, ActorKind Kind, float Hitbox, uint DataId);

/// <summary>
/// What the game's data says about an action: its name, cast type (its shape), its reach and width
/// in yalms, the floor marker it shows (its omen's path, which names a cone's angle), and its cast.
/// </summary>
public sealed record ActionInfo(string Name, int CastType, float Range, float Width, string Omen, float Cast);

/// <summary>Something that happened, <see cref="T"/> seconds after the pull.</summary>
public abstract record RecEvent(double T)
{
    public abstract void Write(Utf8JsonWriter w);
}

/// <summary>
/// An enemy began a cast: the action, how long it takes, where the caster stood and faced, who it
/// targets (a party member's or actor's id, 0 for none) and where on the ground it's aimed.
/// </summary>
public sealed record CastEvent(double T, int Source, uint Action, float Duration, float X, float Z, float Rotation, int Target, float? TargetX, float? TargetZ)
    : RecEvent(T)
{
    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", Round(T, 2));
        w.WriteString("k", "cast");
        w.WriteNumber("src", Source);
        w.WriteNumber("action", Action);
        w.WriteNumber("dur", Round(Duration, 2));
        w.WriteNumber("x", Round(X, 2));
        w.WriteNumber("z", Round(Z, 2));
        w.WriteNumber("rot", Round(Rotation, 3));
        if (Target != 0) w.WriteNumber("tgt", Target);
        if (TargetX is { } tx && TargetZ is { } tz)
        {
            w.WriteNumber("tx", Round(tx, 2));
            w.WriteNumber("tz", Round(tz, 2));
        }
        w.WriteEndObject();
    }

    /// <summary>A number as it's written: rounded, and 0 for one that isn't a number at all (JSON can't hold NaN or infinity).</summary>
    internal static double Round(double v, int digits) => double.IsFinite(v) ? Math.Round(v, digits, MidpointRounding.AwayFromZero) : 0;
}

/// <summary>
/// An enemy's action landed: where the caster stood and faced, where the hit was centred, and the
/// party members it hit (their ids).
/// </summary>
public sealed record HitEvent(double T, int Source, uint Action, float X, float Z, float Rotation, float HitX, float HitZ, IReadOnlyList<int> Targets)
    : RecEvent(T)
{
    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", "hit");
        w.WriteNumber("src", Source);
        w.WriteNumber("action", Action);
        w.WriteNumber("x", CastEvent.Round(X, 2));
        w.WriteNumber("z", CastEvent.Round(Z, 2));
        w.WriteNumber("rot", CastEvent.Round(Rotation, 3));
        w.WriteNumber("hx", CastEvent.Round(HitX, 2));
        w.WriteNumber("hz", CastEvent.Round(HitZ, 2));
        w.WriteStartArray("tgts");
        foreach (var t in Targets) w.WriteNumberValue(t);
        w.WriteEndArray();
        w.WriteEndObject();
    }
}

/// <summary>A head marker appeared over someone (the game's icon number, as it sent it).</summary>
public sealed record MarkerEvent(double T, int On, uint Icon) : RecEvent(T)
{
    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", "marker");
        w.WriteNumber("on", On);
        w.WriteNumber("icon", Icon);
        w.WriteEndObject();
    }
}

/// <summary>A tether from one to another appeared (<see cref="On"/>) or went.</summary>
public sealed record TetherEvent(double T, int From, int To, uint Kind, bool On) : RecEvent(T)
{
    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", On ? "tether" : "untether");
        w.WriteNumber("from", From);
        w.WriteNumber("to", To);
        w.WriteNumber("id", Kind);
        w.WriteEndObject();
    }
}

/// <summary>The waymarks on the floor (A–D, 1–4) and where, as they are from now on; one missing isn't placed.</summary>
public sealed record WaymarksEvent(double T, IReadOnlyDictionary<string, (float X, float Z)> Marks) : RecEvent(T)
{
    public static readonly string[] Names = { "A", "B", "C", "D", "1", "2", "3", "4" };

    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", "waymarks");
        w.WriteStartObject("marks");
        foreach (var name in Names)
        {
            if (!Marks.TryGetValue(name, out var p)) continue;
            w.WriteStartArray(name);
            w.WriteNumberValue(CastEvent.Round(p.X, 2));
            w.WriteNumberValue(CastEvent.Round(p.Z, 2));
            w.WriteEndArray();
        }
        w.WriteEndObject();
        w.WriteEndObject();
    }
}

/// <summary>
/// A map effect: the game changing something about the arena (a floor breaking, walls rising, the
/// arena shrinking), by its index and state, as the game sent them. Encounter data (BossMod's)
/// says which ones change the arena's shape.
/// </summary>
public sealed record MapEffectEvent(double T, byte Index, uint State) : RecEvent(T)
{
    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", "mapfx");
        w.WriteNumber("index", Index);
        w.WriteNumber("state", State);
        w.WriteEndObject();
    }
}

/// <summary>Where everyone was at one moment: by id, (x, z, facing).</summary>
public sealed record Frame(double T, IReadOnlyDictionary<int, (float X, float Z, float Rotation)> At);

/// <summary>
/// One pull, as recorded: the file the plugin keeps, sends to the site and saves for its Import.
/// World coordinates as the game has them (x east, z south, yalms), facing in radians as the game
/// has it (0 south, a quarter turn east); the site turns them into the plan's board.
/// </summary>
public sealed class Recording
{
    public const string FormatName = "kokos-raid-recording";
    public const int FormatVersion = 1;

    public string PluginVersion { get; set; } = "0.0.0";
    public DateTimeOffset RecordedAt { get; set; }
    public double Duration { get; set; }
    public Outcome Outcome { get; set; }
    public int Pull { get; set; } = 1;
    public DutyInfo Duty { get; set; } = new("Unknown duty", 0, 0);
    public List<PartyMember> Party { get; } = new();

    /// <summary>Whether the party's positions are in <see cref="Frames"/> (Settings → Record where the party stands).</summary>
    public bool PartyPositions { get; set; }

    public List<Actor> Actors { get; } = new();
    public Dictionary<uint, ActionInfo> Actions { get; } = new();
    public List<RecEvent> Events { get; } = new();
    public List<Frame> Frames { get; } = new();

    /// <summary>The mechanics in it: the enemies' casts, and their hits that came without one.</summary>
    public int MechanicCount => Timeline.Build(this).Count;

    /// <summary>The recording as its file's JSON (compact).</summary>
    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            Write(w);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteString("format", FormatName);
        w.WriteNumber("version", FormatVersion);
        w.WriteString("plugin", PluginVersion);
        w.WriteString("recordedAt", RecordedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        w.WriteNumber("duration", CastEvent.Round(Duration, 2));
        w.WriteString("outcome", Words.OutcomeKey(Outcome));
        w.WriteNumber("pull", Pull);

        w.WriteStartObject("duty");
        w.WriteString("name", Duty.Name);
        w.WriteNumber("territory", Duty.Territory);
        w.WriteNumber("content", Duty.Content);
        w.WriteEndObject();

        w.WriteBoolean("partyPositions", PartyPositions);
        w.WriteStartArray("party");
        foreach (var p in Party)
        {
            w.WriteStartObject();
            w.WriteNumber("id", p.Id);
            w.WriteString("slot", p.Slot);
            w.WriteString("job", p.Job);
            w.WriteString("role", p.Role);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("actors");
        foreach (var a in Actors)
        {
            w.WriteStartObject();
            w.WriteNumber("id", a.Id);
            w.WriteString("name", a.Name);
            w.WriteString("kind", a.Kind.ToString().ToLowerInvariant());
            w.WriteNumber("hitbox", CastEvent.Round(a.Hitbox, 2));
            w.WriteNumber("dataId", a.DataId);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartObject("actions");
        foreach (var (id, a) in Actions.OrderBy(kv => kv.Key))
        {
            w.WriteStartObject(id.ToString(CultureInfo.InvariantCulture));
            w.WriteString("name", a.Name);
            w.WriteNumber("castType", a.CastType);
            w.WriteNumber("range", CastEvent.Round(a.Range, 2));
            w.WriteNumber("width", CastEvent.Round(a.Width, 2));
            w.WriteString("omen", a.Omen);
            w.WriteNumber("cast", CastEvent.Round(a.Cast, 2));
            w.WriteEndObject();
        }
        w.WriteEndObject();

        w.WriteStartArray("events");
        foreach (var e in Events.OrderBy(e => e.T)) e.Write(w);
        w.WriteEndArray();

        w.WriteStartArray("frames");
        foreach (var f in Frames)
        {
            w.WriteStartObject();
            w.WriteNumber("t", CastEvent.Round(f.T, 2));
            w.WriteStartObject("p");
            foreach (var (id, (x, z, r)) in f.At.OrderBy(kv => kv.Key))
            {
                w.WriteStartArray(id.ToString(CultureInfo.InvariantCulture));
                w.WriteNumberValue(CastEvent.Round(x, 2));
                w.WriteNumberValue(CastEvent.Round(z, 2));
                w.WriteNumberValue(CastEvent.Round(r, 3));
                w.WriteEndArray();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>
    /// Reads a recording's file back. Throws <see cref="FormatException"/> for anything that isn't
    /// one, and only that (a duplicate key, a value of the wrong kind: all the same to the caller).
    /// </summary>
    public static Recording Parse(string json)
    {
        try
        {
            return ParseCore(json);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new FormatException("That isn't a recording.", ex);
        }
    }

    /// <summary>A copy with other frames (the rest shared): what's sent when the whole is too big.</summary>
    public Recording WithFrames(IEnumerable<Frame> frames, bool partyPositions)
    {
        var r = new Recording
        {
            PluginVersion = PluginVersion,
            RecordedAt = RecordedAt,
            Duration = Duration,
            Outcome = Outcome,
            Pull = Pull,
            Duty = Duty,
            PartyPositions = partyPositions,
        };
        r.Party.AddRange(Party);
        r.Actors.AddRange(Actors);
        foreach (var (k, v) in Actions) r.Actions[k] = v;
        r.Events.AddRange(Events);
        r.Frames.AddRange(frames);
        return r;
    }

    private static Recording ParseCore(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("That isn't a recording.", ex);
        }
        if (root is not JsonObject o || Str(o["format"]) != FormatName)
            throw new FormatException("That isn't a recording.");
        if (Int(o["version"]) is not { } version || version > FormatVersion)
            throw new FormatException("That recording is from a newer plugin.");

        var r = new Recording
        {
            PluginVersion = Str(o["plugin"]) ?? "0.0.0",
            RecordedAt = DateTimeOffset.TryParse(Str(o["recordedAt"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : DateTimeOffset.MinValue,
            Duration = Num(o["duration"]) ?? 0,
            Outcome = Words.OutcomeFromKey(Str(o["outcome"])),
            Pull = Int(o["pull"]) ?? 1,
            PartyPositions = o["partyPositions"] is JsonValue pv && pv.TryGetValue<bool>(out var b) && b,
        };
        if (o["duty"] is JsonObject d)
            r.Duty = new DutyInfo(Str(d["name"]) ?? "Unknown duty", (UInt(d["territory"]) ?? 0), (UInt(d["content"]) ?? 0));
        if (o["party"] is JsonArray party)
            foreach (var p in party.OfType<JsonObject>())
                r.Party.Add(new PartyMember(Int(p["id"]) ?? 0, Str(p["slot"]) ?? "", Str(p["job"]) ?? "", Str(p["role"]) ?? "dps"));
        if (o["actors"] is JsonArray actors)
            foreach (var a in actors.OfType<JsonObject>())
                r.Actors.Add(new Actor(Int(a["id"]) ?? 0, Str(a["name"]) ?? "", Enum.TryParse<ActorKind>(Str(a["kind"]), true, out var k) ? k : ActorKind.Add,
                    (float)(Num(a["hitbox"]) ?? 0), (UInt(a["dataId"]) ?? 0)));
        if (o["actions"] is JsonObject actions)
            foreach (var (key, value) in actions)
                if (uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && value is JsonObject a)
                    r.Actions[id] = new ActionInfo(Str(a["name"]) ?? "", Int(a["castType"]) ?? 0, (float)(Num(a["range"]) ?? 0), (float)(Num(a["width"]) ?? 0),
                        Str(a["omen"]) ?? "", (float)(Num(a["cast"]) ?? 0));
        if (o["events"] is JsonArray events)
            foreach (var e in events.OfType<JsonObject>())
                if (ReadEvent(e) is { } ev) r.Events.Add(ev);
        if (o["frames"] is JsonArray frames)
            foreach (var f in frames.OfType<JsonObject>())
            {
                var at2 = new Dictionary<int, (float, float, float)>();
                if (f["p"] is JsonObject p)
                    foreach (var (key, value) in p)
                        if (int.TryParse(key, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) && value is JsonArray xyz && xyz.Count >= 3)
                            at2[id] = ((float)(Num(xyz[0]) ?? 0), (float)(Num(xyz[1]) ?? 0), (float)(Num(xyz[2]) ?? 0));
                r.Frames.Add(new Frame(Num(f["t"]) ?? 0, at2));
            }
        return r;
    }

    private static RecEvent? ReadEvent(JsonObject e)
    {
        var t = Num(e["t"]) ?? 0;
        switch (Str(e["k"]))
        {
            case "cast":
                return new CastEvent(t, Int(e["src"]) ?? 0, (UInt(e["action"]) ?? 0), (float)(Num(e["dur"]) ?? 0), (float)(Num(e["x"]) ?? 0),
                    (float)(Num(e["z"]) ?? 0), (float)(Num(e["rot"]) ?? 0), Int(e["tgt"]) ?? 0, (float?)Num(e["tx"]), (float?)Num(e["tz"]));
            case "hit":
                return new HitEvent(t, Int(e["src"]) ?? 0, (UInt(e["action"]) ?? 0), (float)(Num(e["x"]) ?? 0), (float)(Num(e["z"]) ?? 0),
                    (float)(Num(e["rot"]) ?? 0), (float)(Num(e["hx"]) ?? 0), (float)(Num(e["hz"]) ?? 0),
                    e["tgts"] is JsonArray tg ? tg.Select(x => Int(x) ?? 0).Where(x => x != 0).ToList() : new List<int>());
            case "marker":
                return new MarkerEvent(t, Int(e["on"]) ?? 0, (UInt(e["icon"]) ?? 0));
            case "tether":
            case "untether":
                return new TetherEvent(t, Int(e["from"]) ?? 0, Int(e["to"]) ?? 0, (UInt(e["id"]) ?? 0), Str(e["k"]) == "tether");
            case "mapfx":
                return new MapEffectEvent(t, (byte)Math.Clamp(Int(e["index"]) ?? 0, 0, 255), UInt(e["state"]) ?? 0);
            case "waymarks":
                var marks = new Dictionary<string, (float, float)>();
                if (e["marks"] is JsonObject m)
                    foreach (var (key, value) in m)
                        if (value is JsonArray xz && xz.Count >= 2) marks[key] = ((float)(Num(xz[0]) ?? 0), (float)(Num(xz[1]) ?? 0));
                return new WaymarksEvent(t, marks);
            default:
                return null;
        }
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Num(JsonNode? n) =>
        n is JsonValue v && (v.TryGetValue<double>(out var d) || (v.TryGetValue<long>(out var l) && (d = l) == l)) && double.IsFinite(d) ? d : null;

    private static int? Int(JsonNode? n) => Num(n) is { } d && d >= int.MinValue && d <= int.MaxValue ? (int)d : null;

    private static uint? UInt(JsonNode? n) => Num(n) is { } d && d >= 0 && d <= uint.MaxValue ? (uint)d : null;
}
