using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RaidPlanner.Core.Meter;

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

/// <summary>
/// The duty a pull was in: its name as the game says it, and the game's ids for it. <paramref name="Field"/>
/// (since 0.3.0): a field operation's open zone (Eureka, Bozja and Zadnor, the Occult Crescent), where the
/// site never applies an encounter's data.
/// </summary>
public sealed record DutyInfo(string Name, uint Territory, uint Content, bool Field = false);

/// <summary>
/// One player in the party, as the plan will have them: a spot (MT, OT, H1…), their job and role.
/// Names are never kept. <paramref name="Id"/> is the recording's own number for them (1 to 8, or more in an alliance);
/// <paramref name="MaxHp"/> their max HP as the pull began (0 unknown; since 0.3.0); <paramref name="Self"/> the
/// spot of the player who recorded it (since 0.4.0: the summary's <c>slots[].self</c>, the site's personal bests).
/// </summary>
public sealed record PartyMember(int Id, string Slot, string Job, string Role, long MaxHp = 0, bool Self = false);

/// <summary>
/// One piece of the recording player's gear (since 0.5.0): its place in the game's equipped-items container
/// (<paramref name="Slot"/>: 0 main hand, 1 off hand, 2 head, 3 body, 4 hands, 6 legs, 7 feet, 8 ears, 9 neck,
/// 10 wrists, 11 and 12 rings, 13 the soul crystal), its Item sheet row, whether high-quality and its name.
/// </summary>
public sealed record GearItem(int Slot, uint Id, bool Hq, string Name);

/// <summary>
/// The recording player's gear as the pull began (since 0.5.0; <c>gear</c>, absent when it couldn't be read): their job,
/// average item level (<paramref name="ItemLevel"/>, the items' own as the character window works it out, before any level
/// sync), the items, and their attributes then (<paramref name="Stats"/>, by <see cref="StatKeys"/>; level sync applied, as the
/// game had them). Read from the game's equipped-items container and the player's attributes [verify].
/// </summary>
public sealed record Gear(string Job, int ItemLevel, IReadOnlyList<GearItem> Items, IReadOnlyDictionary<string, int> Stats)
{
    /// <summary>The attributes kept, in the order they're written: the site's keys.</summary>
    public static readonly string[] StatKeys = { "str", "dex", "vit", "int", "mnd", "crit", "dh", "det", "sks", "sps", "ten", "pie" };

    /// <summary>The most items kept (the container's 14 places).</summary>
    public const int MostItems = 14;

    /// <summary>
    /// The average item level as the character window works it out [verify]: the 12 places that count (not the waist's,
    /// 5, nor the soul crystal, 13), an empty one 0, the main hand counted again in the off hand's place when it takes both
    /// hands; over 12, rounded down.
    /// </summary>
    public static int AverageItemLevel(IEnumerable<(int Slot, int ItemLevel)> items, bool twoHanded)
    {
        var sum = 0L;
        var main = 0;
        foreach (var (slot, ilvl) in items)
        {
            if (slot is 5 or >= 13 or < 0 || (slot == 1 && twoHanded)) continue;
            sum += Math.Max(0, ilvl);
            if (slot == 0) main = Math.Max(0, ilvl);
        }
        if (twoHanded) sum += main;
        return (int)(sum / 12);
    }

    internal void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteString("job", Job);
        w.WriteNumber("ilvl", Math.Max(0, ItemLevel));
        w.WriteStartArray("items");
        foreach (var i in Items.OrderBy(i => i.Slot))
        {
            w.WriteStartObject();
            w.WriteNumber("slot", i.Slot);
            w.WriteNumber("id", i.Id);
            w.WriteBoolean("hq", i.Hq);
            w.WriteString("name", i.Name);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartObject("stats");
        foreach (var key in StatKeys)
            if (Stats.TryGetValue(key, out var v) && v > 0)
                w.WriteNumber(key, v);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    /// <summary>Read leniently: a bad item (a slot outside 0–13, no id) or stat (unknown key, not a whole number above 0) is left out; null when it isn't an object.</summary>
    internal static Gear? Read(JsonNode? n, Func<JsonNode?, double?> num, Func<JsonNode?, string?> str, Func<JsonNode?, bool> boolean)
    {
        if (n is not JsonObject o) return null;
        var items = new List<GearItem>();
        if (o["items"] is JsonArray a)
            foreach (var i in a.OfType<JsonObject>())
                if (num(i["slot"]) is { } slot && slot is >= 0 and < MostItems && slot == Math.Floor(slot)
                    && num(i["id"]) is { } id && id is > 0 and <= uint.MaxValue && items.All(x => x.Slot != (int)slot))
                    items.Add(new GearItem((int)slot, (uint)id, boolean(i["hq"]), str(i["name"]) ?? string.Empty));
        var stats = new Dictionary<string, int>();
        if (o["stats"] is JsonObject s)
            foreach (var key in StatKeys)
                if (num(s[key]) is { } v && v is > 0 and <= int.MaxValue)
                    stats[key] = (int)v;
        var ilvl = num(o["ilvl"]) is { } l && l is >= 0 and <= 10000 ? (int)l : 0;
        return new Gear(str(o["job"]) ?? string.Empty, ilvl, items, stats);
    }
}

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

/// <summary>An enemy (or a helper) seen in the fight. <paramref name="Id"/> is the recording's own number (101 up); <paramref name="MaxHp"/> the most it had (0 unknown; since 0.3.0).</summary>
public sealed record Actor(int Id, string Name, ActorKind Kind, float Hitbox, uint DataId, long MaxHp = 0);

/// <summary>
/// What the game's data says about an action: its name, cast type (its shape), its reach and width
/// in yalms, the floor marker it shows (its omen's path, which names a cone's angle), and its cast; since
/// 0.3.0 its ActionCategory (<paramref name="Category"/>: 9 a limit break, 15 PvP's) and base recast in
/// seconds (0 none), for the party's own actions now listed too. Since 0.4.0 (each 0 or false when not known,
/// and then not written): <paramref name="CooldownGroup"/> and <paramref name="CooldownGroup2"/> (the sheet's
/// CooldownGroup and AdditionalCooldownGroup: 58 is the GCD's), <paramref name="Charges"/> (the most charges),
/// <paramref name="AdjustedRecast"/> (the recording player's own recast for it in seconds, traits and level
/// sync included, as the game works it out: their own non-GCD actions only; <c>rc</c>), and
/// <paramref name="Area"/> (placed on the ground: the sheet's TargetArea).
/// </summary>
public sealed record ActionInfo(string Name, int CastType, float Range, float Width, string Omen, float Cast, int Category = 0, float Recast = 0,
    int CooldownGroup = 0, int CooldownGroup2 = 0, int Charges = 0, float AdjustedRecast = 0, bool Area = false);

/// <summary>
/// What the game's data says about a status (a buff or debuff): its name and icon number; since 0.4.0 its
/// StatusCategory (<paramref name="Category"/>: 1 beneficial, 2 detrimental; 0 unknown) and whether it can
/// be cleansed (<paramref name="Dispel"/>: the sheet's CanDispel).
/// </summary>
public sealed record StatusInfo(string Name, uint Icon, int Category = 0, bool Dispel = false);

/// <summary>Something that happened, <see cref="T"/> seconds after the pull.</summary>
public abstract record RecEvent(double T)
{
    public abstract void Write(Utf8JsonWriter w);
}

/// <summary>
/// An enemy began a cast: the action, how long it takes, where the caster stood and faced, who it
/// targets (a party member's or actor's id, 0 for none) and where on the ground it's aimed; since 0.4.0
/// whether it can be interrupted (<paramref name="Interruptible"/>, <c>int</c>).
/// </summary>
public sealed record CastEvent(double T, int Source, uint Action, float Duration, float X, float Z, float Rotation, int Target, float? TargetX, float? TargetZ,
    bool Interruptible = false)
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
        if (Interruptible) w.WriteBoolean("int", true);
        w.WriteEndObject();
    }

    /// <summary>A number as it's written: rounded, and 0 for one that isn't a number at all (JSON can't hold NaN or infinity).</summary>
    internal static double Round(double v, int digits) => double.IsFinite(v) ? Math.Round(v, digits, MidpointRounding.AwayFromZero) : 0;
}

/// <summary>
/// An enemy's action landed: where the caster stood, which way the action faced (its own direction
/// from the game's message since 0.2.1; the caster's facing before), where the hit was centred, and
/// the party members it hit (their ids). Since 0.3.0, aligned with them: the damage each took
/// (<paramref name="Damage"/>), the hit's flags on each (<paramref name="Fx"/>: 1 crit, 2 direct hit,
/// 4 blocked, 8 parried, 16 fully resisted, 32 invulnerable, 64 instant death, 128 dodged) and each
/// one's HP just before (<paramref name="Hp"/>, −1 unknown); null in older files. Since 0.4.0 also, aligned:
/// each one's barrier before it as a % of their max HP (<paramref name="Shield"/>, <c>sh</c>, −1 unknown) and
/// where each stood as the effect arrived (<paramref name="Px"/>, <paramref name="Pz"/>, NaN unknown, written
/// as null), and the flags 256 knocked back or drawn in, 512 partly absorbed, 1024 a status resisted (no
/// longer 16).
/// </summary>
public sealed record HitEvent(double T, int Source, uint Action, float X, float Z, float Rotation, float HitX, float HitZ, IReadOnlyList<int> Targets,
    IReadOnlyList<long>? Damage = null, IReadOnlyList<int>? Fx = null, IReadOnlyList<long>? Hp = null, IReadOnlyList<int>? Shield = null,
    IReadOnlyList<float>? Px = null, IReadOnlyList<float>? Pz = null)
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
        if (Damage != null && Damage.Count == Targets.Count)
        {
            w.WriteStartArray("dmg");
            foreach (var d in Damage) w.WriteNumberValue(d);
            w.WriteEndArray();
        }
        if (Fx != null && Fx.Count == Targets.Count)
        {
            w.WriteStartArray("fx");
            foreach (var f in Fx) w.WriteNumberValue(f);
            w.WriteEndArray();
        }
        if (Hp != null && Hp.Count == Targets.Count)
        {
            w.WriteStartArray("hp");
            foreach (var h in Hp) w.WriteNumberValue(h);
            w.WriteEndArray();
        }
        if (Shield != null && Shield.Count == Targets.Count)
        {
            w.WriteStartArray("sh");
            foreach (var h in Shield) w.WriteNumberValue(h);
            w.WriteEndArray();
        }
        if (Px != null && Pz != null && Px.Count == Targets.Count && Pz.Count == Targets.Count)
        {
            Place(w, "px", Px);
            Place(w, "pz", Pz);
        }
        w.WriteEndObject();
    }

    private static void Place(Utf8JsonWriter w, string name, IReadOnlyList<float> values)
    {
        w.WriteStartArray(name);
        foreach (var v in values)
        {
            if (float.IsFinite(v)) w.WriteNumberValue(CastEvent.Round(v, 2));
            else w.WriteNullValue();
        }
        w.WriteEndArray();
    }
}

/// <summary>
/// An enemy's cast stopped before its end (since 0.4.0): the game said so (actor control 15) while the
/// recorder held the cast and before its end less a quarter second (the game sends it on some casts that
/// finished). <see cref="Interrupted"/> when the game said interrupted (else cancelled); <see cref="By"/>
/// and <see cref="With"/> the party member and action credited (0 none: <see cref="CastWatch"/>).
/// </summary>
public sealed record CancelEvent(double T, int Source, uint Action, bool Interrupted, int By = 0, uint With = 0) : RecEvent(T)
{
    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", "cancel");
        w.WriteNumber("src", Source);
        w.WriteNumber("action", Action);
        if (Interrupted) w.WriteBoolean("int", true);
        if (By != 0) w.WriteNumber("by", By);
        if (With != 0) w.WriteNumber("with", With);
        w.WriteEndObject();
    }
}

/// <summary>
/// A dead party member alive again (since 0.4.0), at the frame their HP went above 0 (they accepted):
/// <see cref="By"/> the raiser (0 not known), <see cref="At"/> when the raise landed (null not known),
/// <see cref="Lb"/> the healers' limit break 3.
/// </summary>
public sealed record RaiseEvent(double T, int Who, int By, double? At = null, bool Lb = false) : RecEvent(T)
{
    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", "raise");
        w.WriteNumber("who", Who);
        w.WriteNumber("by", By);
        if (At is { } at && double.IsFinite(at)) w.WriteNumber("at", CastEvent.Round(at, 2));
        if (Lb) w.WriteBoolean("lb", true);
        w.WriteEndObject();
    }
}

/// <summary>A targetable enemy's target changed (since 0.4.0): <see cref="On"/> a party id, an actor's, or 0 for none.</summary>
public sealed record AggroEvent(double T, int Actor, int On) : RecEvent(T)
{
    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", "aggro");
        w.WriteNumber("actor", Actor);
        w.WriteNumber("on", On);
        w.WriteEndObject();
    }
}

/// <summary>
/// The head signs (the party's target markers) as they now are (since 0.4.0): by the game's index
/// (0–16; assumed attack 1–5, bind 1–3, ignore 1–2, square, circle, cross, triangle, attack 6–8
/// [verify]) the party id or actor id marked. One missing isn't placed.
/// </summary>
public sealed record SignsEvent(double T, IReadOnlyDictionary<int, int> Marks) : RecEvent(T)
{
    public const int Count = 17;

    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", "signs");
        w.WriteStartObject("marks");
        foreach (var (i, on) in Marks.OrderBy(kv => kv.Key))
            w.WriteNumber(i.ToString(CultureInfo.InvariantCulture), on);
        w.WriteEndObject();
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

/// <summary>
/// A status put on a party member (<see cref="On"/>) by an enemy, a helper or no one (<see cref="Source"/>
/// 0), gained or gone; <see cref="Duration"/> is the time it had left when gained (0 without a timer,
/// and when it goes). Since 0.3.0 also an enemy's own (a boss's buff, a phase marker: <see cref="On"/> its
/// actor id), with its stacks or param (<see cref="Stacks"/>, 0 none) and <see cref="Refresh"/> for one
/// already on that was put on again or changed stacks (still a gain, for older readers; not a new one).
/// The party's own buffs and debuffs are in the combat block instead (<see cref="CombatLog"/>).
/// </summary>
public sealed record StatusEvent(double T, int On, uint Id, int Source, float Duration, bool Gain, int Stacks = 0, bool Refresh = false) : RecEvent(T)
{
    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", "status");
        w.WriteNumber("on", On);
        w.WriteNumber("id", Id);
        w.WriteNumber("src", Source);
        w.WriteNumber("dur", CastEvent.Round(Math.Max(0, Duration), 2));
        w.WriteBoolean("gain", Gain);
        if (Stacks != 0) w.WriteNumber("n", Stacks);
        if (Refresh) w.WriteBoolean("re", true);
        w.WriteEndObject();
    }
}

/// <summary>
/// A party member died; <see cref="By"/> and <see cref="From"/> are the last enemy action that hit them
/// within 3 seconds before (0 for none), or since 0.3.0 the status whose tick did it (<see cref="ByStatus"/>:
/// <see cref="By"/> is a status id; a bleed, a boss's DoT).
/// </summary>
public sealed record DeathEvent(double T, int Who, uint By, int From, bool ByStatus = false) : RecEvent(T)
{
    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", "death");
        w.WriteNumber("who", Who);
        w.WriteNumber("by", By);
        w.WriteNumber("from", From);
        if (ByStatus) w.WriteBoolean("byStatus", true);
        w.WriteEndObject();
    }
}

/// <summary>An enemy that has been targetable in the pull became untargetable (<see cref="On"/> false) or targetable again: how phases are told apart.</summary>
public sealed record TargetEvent(double T, int Actor, bool On) : RecEvent(T)
{
    public override void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("t", CastEvent.Round(T, 2));
        w.WriteString("k", "target");
        w.WriteNumber("actor", Actor);
        w.WriteBoolean("on", On);
        w.WriteEndObject();
    }
}

/// <summary>Where everyone was at one moment: by id, (x, z, facing); since 0.3.0 their HP then (<paramref name="Hp"/>, by id: the enemies always, the party when their places are kept).</summary>
public sealed record Frame(double T, IReadOnlyDictionary<int, (float X, float Z, float Rotation)> At, IReadOnlyDictionary<int, long>? Hp = null);

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

    /// <summary>The statuses the status events name, by id.</summary>
    public Dictionary<uint, StatusInfo> Statuses { get; } = new();

    public List<RecEvent> Events { get; } = new();
    public List<Frame> Frames { get; } = new();

    /// <summary>The party's own fight (since 0.3.0), or null in older files.</summary>
    public CombatLog? Combat { get; set; }

    /// <summary>The recording player's gear as the pull began (since 0.5.0), or null (older files; it couldn't be read).</summary>
    public Gear? Gear { get; set; }

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
        if (Duty.Field) w.WriteBoolean("field", true);
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
            if (p.MaxHp > 0) w.WriteNumber("maxHp", p.MaxHp);
            if (p.Self) w.WriteBoolean("self", true);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        if (Gear != null)
        {
            w.WritePropertyName("gear");
            Gear.Write(w);
        }

        w.WriteStartArray("actors");
        foreach (var a in Actors)
        {
            w.WriteStartObject();
            w.WriteNumber("id", a.Id);
            w.WriteString("name", a.Name);
            w.WriteString("kind", a.Kind.ToString().ToLowerInvariant());
            w.WriteNumber("hitbox", CastEvent.Round(a.Hitbox, 2));
            w.WriteNumber("dataId", a.DataId);
            if (a.MaxHp > 0) w.WriteNumber("maxHp", a.MaxHp);
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
            if (a.Category > 0) w.WriteNumber("cat", a.Category);
            if (a.Recast > 0) w.WriteNumber("recast", CastEvent.Round(a.Recast, 2));
            if (a.CooldownGroup > 0) w.WriteNumber("cdg", a.CooldownGroup);
            if (a.CooldownGroup2 > 0) w.WriteNumber("cdg2", a.CooldownGroup2);
            if (a.Charges > 1) w.WriteNumber("charges", a.Charges);
            if (a.AdjustedRecast > 0) w.WriteNumber("rc", CastEvent.Round(a.AdjustedRecast, 2));
            if (a.Area) w.WriteBoolean("area", true);
            w.WriteEndObject();
        }
        w.WriteEndObject();

        w.WriteStartObject("statuses");
        foreach (var (id, st) in Statuses.OrderBy(kv => kv.Key))
        {
            w.WriteStartObject(id.ToString(CultureInfo.InvariantCulture));
            w.WriteString("name", st.Name);
            w.WriteNumber("icon", st.Icon);
            if (st.Category > 0) w.WriteNumber("cat", st.Category);
            if (st.Dispel) w.WriteBoolean("dispel", true);
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
                if (f.Hp != null && f.Hp.TryGetValue(id, out var hp)) w.WriteNumberValue(Math.Max(0, hp));
                w.WriteEndArray();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        if (Combat != null)
        {
            w.WritePropertyName("combat");
            Combat.Write(w);
        }
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
    public Recording WithFrames(IEnumerable<Frame> frames, bool partyPositions) => With(frames, partyPositions, Combat);

    /// <summary>A copy with other frames and another combat block (the rest shared).</summary>
    public Recording With(IEnumerable<Frame> frames, bool partyPositions, CombatLog? combat)
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
        foreach (var (k, v) in Statuses) r.Statuses[k] = v;
        r.Events.AddRange(Events);
        r.Frames.AddRange(frames);
        r.Combat = combat;
        r.Gear = Gear;
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
            r.Duty = new DutyInfo(Str(d["name"]) ?? "Unknown duty", (UInt(d["territory"]) ?? 0), (UInt(d["content"]) ?? 0),
                d["field"] is JsonValue fv && fv.TryGetValue<bool>(out var field) && field);
        if (o["party"] is JsonArray party)
            foreach (var p in party.OfType<JsonObject>())
                r.Party.Add(new PartyMember(Int(p["id"]) ?? 0, Str(p["slot"]) ?? "", Str(p["job"]) ?? "", Str(p["role"]) ?? "dps", Hp(p["maxHp"]) ?? 0, Bool(p["self"])));
        if (o["actors"] is JsonArray actors)
            foreach (var a in actors.OfType<JsonObject>())
                r.Actors.Add(new Actor(Int(a["id"]) ?? 0, Str(a["name"]) ?? "", Enum.TryParse<ActorKind>(Str(a["kind"]), true, out var k) ? k : ActorKind.Add,
                    (float)(Num(a["hitbox"]) ?? 0), (UInt(a["dataId"]) ?? 0), Hp(a["maxHp"]) ?? 0));
        if (o["actions"] is JsonObject actions)
            foreach (var (key, value) in actions)
                if (uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && value is JsonObject a)
                    r.Actions[id] = new ActionInfo(Str(a["name"]) ?? "", Int(a["castType"]) ?? 0, (float)(Num(a["range"]) ?? 0), (float)(Num(a["width"]) ?? 0),
                        Str(a["omen"]) ?? "", (float)(Num(a["cast"]) ?? 0), Math.Max(0, Int(a["cat"]) ?? 0), (float)Math.Max(0, Num(a["recast"]) ?? 0),
                        Math.Clamp(Int(a["cdg"]) ?? 0, 0, 255), Math.Clamp(Int(a["cdg2"]) ?? 0, 0, 255), Math.Clamp(Int(a["charges"]) ?? 0, 0, 255),
                        (float)Math.Max(0, Num(a["rc"]) ?? 0), Bool(a["area"]));
        if (o["statuses"] is JsonObject statuses)
            foreach (var (key, value) in statuses)
                if (uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && value is JsonObject st)
                    r.Statuses[id] = new StatusInfo(Str(st["name"]) ?? "", UInt(st["icon"]) ?? 0, Math.Clamp(Int(st["cat"]) ?? 0, 0, 255), Bool(st["dispel"]));
        if (o["events"] is JsonArray events)
            foreach (var e in events.OfType<JsonObject>())
                if (ReadEvent(e) is { } ev) r.Events.Add(ev);
        if (o["frames"] is JsonArray frames)
            foreach (var f in frames.OfType<JsonObject>())
            {
                var at2 = new Dictionary<int, (float, float, float)>();
                Dictionary<int, long>? hp = null;
                if (f["p"] is JsonObject p)
                    foreach (var (key, value) in p)
                        if (int.TryParse(key, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) && value is JsonArray xyz && xyz.Count >= 3)
                        {
                            at2[id] = ((float)(Num(xyz[0]) ?? 0), (float)(Num(xyz[1]) ?? 0), (float)(Num(xyz[2]) ?? 0));
                            if (xyz.Count >= 4 && Hp(xyz[3]) is { } h) (hp ??= new Dictionary<int, long>())[id] = h;
                        }
                r.Frames.Add(new Frame(Num(f["t"]) ?? 0, at2, hp));
            }
        if (o["combat"] is JsonObject combat) r.Combat = CombatLog.Read(combat);
        r.Gear = Gear.Read(o["gear"], Num, Str, Bool);
        return r;
    }

    private static RecEvent? ReadEvent(JsonObject e)
    {
        var t = Num(e["t"]) ?? 0;
        switch (Str(e["k"]))
        {
            case "cast":
                return new CastEvent(t, Int(e["src"]) ?? 0, (UInt(e["action"]) ?? 0), (float)(Num(e["dur"]) ?? 0), (float)(Num(e["x"]) ?? 0),
                    (float)(Num(e["z"]) ?? 0), (float)(Num(e["rot"]) ?? 0), Int(e["tgt"]) ?? 0, (float?)Num(e["tx"]), (float?)Num(e["tz"]), Bool(e["int"]));
            case "hit":
                return ReadHit(e, t);
            case "marker":
                return new MarkerEvent(t, Int(e["on"]) ?? 0, (UInt(e["icon"]) ?? 0));
            case "tether":
            case "untether":
                return new TetherEvent(t, Int(e["from"]) ?? 0, Int(e["to"]) ?? 0, (UInt(e["id"]) ?? 0), Str(e["k"]) == "tether");
            case "mapfx":
                return new MapEffectEvent(t, (byte)Math.Clamp(Int(e["index"]) ?? 0, 0, 255), UInt(e["state"]) ?? 0);
            case "status":
                return new StatusEvent(t, Int(e["on"]) ?? 0, UInt(e["id"]) ?? 0, Int(e["src"]) ?? 0, (float)(Num(e["dur"]) ?? 0),
                    e["gain"] is JsonValue gv && gv.TryGetValue<bool>(out var gain) && gain, Int(e["n"]) ?? 0,
                    e["re"] is JsonValue rv && rv.TryGetValue<bool>(out var re) && re);
            case "death":
                return new DeathEvent(t, Int(e["who"]) ?? 0, UInt(e["by"]) ?? 0, Int(e["from"]) ?? 0,
                    e["byStatus"] is JsonValue bv && bv.TryGetValue<bool>(out var byStatus) && byStatus);
            case "target":
                return new TargetEvent(t, Int(e["actor"]) ?? 0, e["on"] is JsonValue ov && ov.TryGetValue<bool>(out var on) && on);
            case "waymarks":
                var marks = new Dictionary<string, (float, float)>();
                if (e["marks"] is JsonObject m)
                    foreach (var (key, value) in m)
                        if (value is JsonArray xz && xz.Count >= 2) marks[key] = ((float)(Num(xz[0]) ?? 0), (float)(Num(xz[1]) ?? 0));
                return new WaymarksEvent(t, marks);
            case "cancel":
                return new CancelEvent(t, Int(e["src"]) ?? 0, UInt(e["action"]) ?? 0, Bool(e["int"]), Int(e["by"]) ?? 0, UInt(e["with"]) ?? 0);
            case "raise":
                return new RaiseEvent(t, Int(e["who"]) ?? 0, Int(e["by"]) ?? 0, Num(e["at"]), Bool(e["lb"]));
            case "aggro":
                return new AggroEvent(t, Int(e["actor"]) ?? 0, Int(e["on"]) ?? 0);
            case "signs":
                var signs = new Dictionary<int, int>();
                if (e["marks"] is JsonObject sm)
                    foreach (var (key, value) in sm)
                        if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < SignsEvent.Count && Int(value) is { } marked && marked != 0)
                            signs[i] = marked;
                return new SignsEvent(t, signs);
            default:
                return null;
        }
    }

    /// <summary>
    /// A hit, its targets and (since 0.3.0) what each took: the per-target lists are kept only when as
    /// long as the targets, and a target that isn't one (0) is left out of all of them together.
    /// </summary>
    private static HitEvent ReadHit(JsonObject e, double t)
    {
        var raw = e["tgts"] is JsonArray tg ? tg.Select(x => Int(x) ?? 0).ToList() : new List<int>();
        List<T>? Aligned<T>(string key, Func<JsonNode?, T> read) =>
            e[key] is JsonArray a && a.Count == raw.Count ? a.Select(read).ToList() : null;
        var dmg = Aligned("dmg", x => Hp(x) ?? 0);
        var fx = Aligned("fx", x => Int(x) ?? 0);
        var hp = Aligned("hp", x => Num(x) is { } d && d >= -1 && d <= long.MaxValue ? (long)d : -1);
        var sh = Aligned("sh", x => Int(x) is { } v && v is >= 0 and <= 100 ? v : -1);
        var px = Aligned("px", x => Num(x) is { } d ? (float)d : float.NaN);
        var pz = Aligned("pz", x => Num(x) is { } d ? (float)d : float.NaN);
        if (px == null || pz == null) px = pz = null;
        var keep = Enumerable.Range(0, raw.Count).Where(i => raw[i] != 0).ToList();
        return new HitEvent(t, Int(e["src"]) ?? 0, (UInt(e["action"]) ?? 0), (float)(Num(e["x"]) ?? 0), (float)(Num(e["z"]) ?? 0),
            (float)(Num(e["rot"]) ?? 0), (float)(Num(e["hx"]) ?? 0), (float)(Num(e["hz"]) ?? 0),
            keep.Select(i => raw[i]).ToList(), dmg == null ? null : keep.Select(i => dmg[i]).ToList(),
            fx == null ? null : keep.Select(i => fx[i]).ToList(), hp == null ? null : keep.Select(i => hp[i]).ToList(),
            sh == null ? null : keep.Select(i => sh[i]).ToList(), px == null ? null : keep.Select(i => px[i]).ToList(),
            pz == null ? null : keep.Select(i => pz![i]).ToList());
    }

    /// <summary>An amount of HP (or damage): a whole number from 0 up, or null.</summary>
    private static long? Hp(JsonNode? n) => Num(n) is { } d && d >= 0 && d <= long.MaxValue ? (long)d : null;

    private static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Num(JsonNode? n) =>
        n is JsonValue v && (v.TryGetValue<double>(out var d) || (v.TryGetValue<long>(out var l) && (d = l) == l)) && double.IsFinite(d) ? d : null;

    private static int? Int(JsonNode? n) => Num(n) is { } d && d >= int.MinValue && d <= int.MaxValue ? (int)d : null;

    private static uint? UInt(JsonNode? n) => Num(n) is { } d && d >= 0 && d <= uint.MaxValue ? (uint)d : null;
}
