using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RaidPlanner.Core;

/// <summary>
/// One of the party's action effects (<c>combat.fx</c>): time, action, origin (party id), recipient (party or actor id, 0 not in the
/// recording), amount, kind + flags, the recipient's HP before (heals; −1 otherwise); since 0.4.0 a damage effect's bonus % (the
/// battle log's "(+61%)": positional and combo bonuses; signed, a blocked or parried hit's reduction; the optional column <c>b</c>).
/// </summary>
public readonly record struct FxRow(double T, uint Action, int Origin, int Recipient, long Amount, int Kind, long HpBefore, int Bonus = 0);

/// <summary>
/// A DoT or HoT tick (<c>combat.tick</c>): on whom, credited to whom (a party id; an actor's for an enemy's bleed; 0 when the game
/// didn't say), the status, the amount, a heal, and (since the alignment round, the optional column <c>h</c>) the HP the one it
/// ticked on had just before it, as the game side read it (a HoT's target, or a party member; −1 not known): what a HoT's
/// overheal is measured against, as the meter measures it.
/// </summary>
public readonly record struct TickRow(double T, int On, int Source, uint Status, long Amount, bool Heal, long HpBefore = -1);

/// <summary>
/// A party member's cast (<c>combat.cast</c>): when it began, who, the action, its cast time, and whether it was cancelled or
/// interrupted (<c>x</c>: no action of theirs went off by its end, or the game said so); since 0.4.0 what the game said
/// (<paramref name="Why"/>, the optional column <c>why</c>: 0 finished or not said, 1 cancelled, 2 interrupted).
/// </summary>
public readonly record struct CastRow(double T, int Source, uint Action, float Duration, bool Cancelled, int Why = 0);

/// <summary>
/// One of the party's actions or items used (<c>combat.use</c>, since 0.4.0): the party member (a pet's owner), the action (an
/// item's id as sent, flag 2), the first target (a party or actor id; the caster's own for themselves; 0 none or not in the
/// recording), how many targets, and its flags (<see cref="Meter.UseFlags"/>).
/// </summary>
public readonly record struct UseRow(double T, int Source, uint Action, int Target, int Count, int Flags);

/// <summary>A ground-placed party action (<c>combat.area</c>, since 0.4.0): who, the action, where.</summary>
public readonly record struct AreaRow(double T, int Source, uint Action, float X, float Z);

/// <summary>
/// The recording player's target's enmity list (<c>combat.hate</c>, since 0.4.0): the enemy (an actor id), the party member, their
/// enmity as a % of the top (0–100), or −1 when they left the list.
/// </summary>
public readonly record struct HateRow(double T, int On, int Who, int Pct);

/// <summary>A party member's MP (0–10000 as the game has it) and barrier (% of max HP) (<c>combat.res</c>, since 0.4.0).</summary>
public readonly record struct ResRow(double T, int Who, int Mp, int Shield);

/// <summary>The limit break gauge (<c>combat.lb</c>, since 0.4.0): units now, and the most (bars × units a bar).</summary>
public readonly record struct GaugeRow(double T, int Value, int Max);

/// <summary>An item the <c>use</c> and <c>pre</c> rows name (<c>combat.items</c>, since 0.4.0): its name, icon, and whether high quality.</summary>
public sealed record ItemInfo(string Name, uint Icon, bool Hq);

/// <summary>A status the party put on someone, gained or gone (<c>combat.st</c>): on whom, by whom, the status, the action that put it on (0 unknown), its time left, its stacks or param.</summary>
public readonly record struct StatusRow(double T, int On, int Source, uint Status, uint Action, float Duration, int Stacks, bool Gain);

/// <summary>
/// The recording's <c>combat</c> block (the site's contract §2.8): the party's own fight, beside the
/// events so it never counts towards their cap, columnar and in time order. Each table's columns are
/// equally long; times are deltas in centiseconds from the row before (the first from the pull's
/// start; never below 0). A table dropped to fit the site's limit is left out and named in
/// <see cref="Thinned"/> (since 0.4.0: <c>hate</c>, <c>res</c>, <c>area</c>, <c>cast</c>, <c>use</c>, <c>tick</c>; never
/// <c>fx</c>, <c>st</c>, <c>lb</c>, <c>pre</c>, <c>cd</c> or <c>items</c>). Since 0.4.0 also what the party used
/// (<c>use</c>), their ground placements (<c>area</c>), the recording player's target's enmity (<c>hate</c>), MP and
/// barriers (<c>res</c>), the limit break gauge (<c>lb</c>), what they used in the 30 seconds before the pull
/// (<c>pre</c>), the countdown (<c>cd</c>) and the items named (<c>items</c>).
/// </summary>
public sealed class CombatLog
{
    public const int Version = 1;

    /// <summary>The most rows kept in each table (the site reads as many).</summary>
    public const int MaxRows = 200_000;

    public List<FxRow> Fx { get; } = new();
    public List<TickRow> Ticks { get; } = new();
    public List<CastRow> Casts { get; } = new();
    public List<StatusRow> Statuses { get; } = new();

    public List<UseRow> Uses { get; } = new();
    public List<AreaRow> Areas { get; } = new();
    public List<HateRow> Hate { get; } = new();
    public List<ResRow> Res { get; } = new();
    public List<GaugeRow> Gauge { get; } = new();

    /// <summary>The party's uses before the pull, their times ≤ 0 (seconds from the pull's start).</summary>
    public List<UseRow> Pre { get; } = new();

    /// <summary>The countdown that ended near the start: its length, and start − zero (s).</summary>
    public (double Length, double End)? Countdown { get; set; }

    public Dictionary<uint, ItemInfo> Items { get; } = new();

    /// <summary>
    /// The block has 0.4.0's tables (a new one, or one read with any of them): a block read from an older file
    /// is written back without them, so it doesn't seem to say its party used nothing.
    /// </summary>
    public bool Extended { get; set; } = true;

    /// <summary>The tables that may be dropped to fit the site's limit, in no order (SiteLink.Fit has the order).</summary>
    public static readonly string[] Droppable = { "hate", "res", "area", "cast", "use", "tick" };

    /// <summary>The tables left out to fit the site's limit (<see cref="Droppable"/>).</summary>
    public List<string> Thinned { get; } = new();

    public bool HasCasts => !Thinned.Contains("cast");

    public bool HasTicks => !Thinned.Contains("tick");

    public bool HasUses => Extended && !Thinned.Contains("use");

    public bool IsEmpty => Fx.Count == 0 && Ticks.Count == 0 && Casts.Count == 0 && Statuses.Count == 0 && Uses.Count == 0 && Pre.Count == 0;

    /// <summary>A copy without some tables (the rows shared): what's sent when the whole is too big.</summary>
    public CombatLog Without(params string[] tables)
    {
        var c = new CombatLog { Extended = Extended, Countdown = Countdown };
        c.Fx.AddRange(Fx);
        c.Statuses.AddRange(Statuses);
        c.Gauge.AddRange(Gauge);
        c.Pre.AddRange(Pre);
        foreach (var (k, v) in Items) c.Items[k] = v;
        if (!tables.Contains("tick")) c.Ticks.AddRange(Ticks);
        if (!tables.Contains("cast")) c.Casts.AddRange(Casts);
        if (!tables.Contains("use")) c.Uses.AddRange(Uses);
        if (!tables.Contains("area")) c.Areas.AddRange(Areas);
        if (!tables.Contains("hate")) c.Hate.AddRange(Hate);
        if (!tables.Contains("res")) c.Res.AddRange(Res);
        c.Thinned.AddRange(Thinned);
        foreach (var t in tables)
            if (!c.Thinned.Contains(t))
                c.Thinned.Add(t);
        return c;
    }

    /// <summary>A time as the file has it: whole centiseconds.</summary>
    private static long Cs(double t) => double.IsFinite(t) ? (long)Math.Round(t * 100, MidpointRounding.AwayFromZero) : 0;

    /// <summary>
    /// Each row's time as a delta from the one before, in centiseconds, the rows in time order (stable); the first from
    /// <paramref name="from"/> seconds before the start (0: the start; <c>pre</c>'s 30).
    /// </summary>
    private static List<int> Order<T>(List<T> rows, Func<T, double> time, out long[] deltas, double from = 0)
    {
        var order = Enumerable.Range(0, rows.Count).OrderBy(i => Cs(time(rows[i]))).ThenBy(i => i).ToList();
        deltas = new long[order.Count];
        long prev = 0;
        for (var i = 0; i < order.Count; i++)
        {
            var cs = Math.Max(0, Cs(time(rows[order[i]]) + from));
            deltas[i] = Math.Max(0, cs - prev);
            prev = Math.Max(prev, cs);
        }
        return order;
    }

    private static void Column(Utf8JsonWriter w, string name, long[] values)
    {
        w.WriteStartArray(name);
        foreach (var v in values) w.WriteNumberValue(v);
        w.WriteEndArray();
    }

    private static void Column<T>(Utf8JsonWriter w, string name, List<T> rows, List<int> order, Func<T, long> value)
    {
        w.WriteStartArray(name);
        foreach (var i in order) w.WriteNumberValue(value(rows[i]));
        w.WriteEndArray();
    }

    private static void Column<T>(Utf8JsonWriter w, string name, List<T> rows, List<int> order, Func<T, double> value)
    {
        w.WriteStartArray(name);
        foreach (var i in order) w.WriteNumberValue(CastEvent.Round(value(rows[i]), 2));
        w.WriteEndArray();
    }

    public void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteNumber("v", Version);

        var fx = Order(Fx, r => r.T, out var fxT);
        w.WriteStartObject("fx");
        Column(w, "t", fxT);
        Column(w, "a", Fx, fx, r => (long)r.Action);
        Column(w, "o", Fx, fx, r => (long)r.Origin);
        Column(w, "r", Fx, fx, r => (long)r.Recipient);
        Column(w, "v", Fx, fx, r => r.Amount);
        Column(w, "k", Fx, fx, r => (long)r.Kind);
        Column(w, "h", Fx, fx, r => r.HpBefore);
        if (Extended) Column(w, "b", Fx, fx, r => (long)r.Bonus);
        w.WriteEndObject();

        if (HasTicks)
        {
            var tick = Order(Ticks, r => r.T, out var tickT);
            w.WriteStartObject("tick");
            Column(w, "t", tickT);
            Column(w, "on", Ticks, tick, r => (long)r.On);
            Column(w, "src", Ticks, tick, r => (long)r.Source);
            Column(w, "s", Ticks, tick, r => (long)r.Status);
            Column(w, "v", Ticks, tick, r => r.Amount);
            Column(w, "heal", Ticks, tick, r => r.Heal ? 1L : 0L);
            Column(w, "h", Ticks, tick, r => r.HpBefore < 0 ? -1L : r.HpBefore);
            w.WriteEndObject();
        }

        if (HasCasts)
        {
            var cast = Order(Casts, r => r.T, out var castT);
            w.WriteStartObject("cast");
            Column(w, "t", castT);
            Column(w, "src", Casts, cast, r => (long)r.Source);
            Column(w, "a", Casts, cast, r => (long)r.Action);
            Column(w, "dur", Casts, cast, r => (double)r.Duration);
            Column(w, "x", Casts, cast, r => r.Cancelled ? 1L : 0L);
            if (Extended) Column(w, "why", Casts, cast, r => (long)Math.Clamp(r.Why, 0, 2));
            w.WriteEndObject();
        }

        var st = Order(Statuses, r => r.T, out var stT);
        w.WriteStartObject("st");
        Column(w, "t", stT);
        Column(w, "on", Statuses, st, r => (long)r.On);
        Column(w, "src", Statuses, st, r => (long)r.Source);
        Column(w, "s", Statuses, st, r => (long)r.Status);
        Column(w, "a", Statuses, st, r => (long)r.Action);
        Column(w, "dur", Statuses, st, r => (double)Math.Max(0, r.Duration));
        Column(w, "n", Statuses, st, r => (long)r.Stacks);
        Column(w, "gain", Statuses, st, r => r.Gain ? 1L : 0L);
        w.WriteEndObject();

        if (Extended) WriteExtended(w);

        if (Thinned.Count > 0)
        {
            w.WriteStartArray("thinned");
            foreach (var t in Thinned) w.WriteStringValue(t);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    /// <summary>The time before the pull <c>pre</c>'s deltas count from (seconds).</summary>
    public const double PreFrom = Meter.PrePull.Keep;

    /// <summary>0.4.0's tables (<see cref="Extended"/>): those not dropped, then <c>lb</c>, <c>pre</c>, <c>cd</c> and <c>items</c>.</summary>
    private void WriteExtended(Utf8JsonWriter w)
    {
        if (!Thinned.Contains("use"))
        {
            var use = Order(Uses, r => r.T, out var useT);
            w.WriteStartObject("use");
            Column(w, "t", useT);
            Column(w, "src", Uses, use, r => (long)r.Source);
            Column(w, "a", Uses, use, r => (long)r.Action);
            Column(w, "tgt", Uses, use, r => (long)r.Target);
            Column(w, "n", Uses, use, r => (long)Math.Clamp(r.Count, 0, 32));
            Column(w, "f", Uses, use, r => (long)r.Flags);
            w.WriteEndObject();
        }
        if (!Thinned.Contains("area"))
        {
            var area = Order(Areas, r => r.T, out var areaT);
            w.WriteStartObject("area");
            Column(w, "t", areaT);
            Column(w, "src", Areas, area, r => (long)r.Source);
            Column(w, "a", Areas, area, r => (long)r.Action);
            Column(w, "x", Areas, area, r => (double)r.X);
            Column(w, "z", Areas, area, r => (double)r.Z);
            w.WriteEndObject();
        }
        if (!Thinned.Contains("hate"))
        {
            var hate = Order(Hate, r => r.T, out var hateT);
            w.WriteStartObject("hate");
            Column(w, "t", hateT);
            Column(w, "on", Hate, hate, r => (long)r.On);
            Column(w, "who", Hate, hate, r => (long)r.Who);
            Column(w, "pct", Hate, hate, r => (long)Math.Clamp(r.Pct, -1, 100));
            w.WriteEndObject();
        }
        if (!Thinned.Contains("res"))
        {
            var res = Order(Res, r => r.T, out var resT);
            w.WriteStartObject("res");
            Column(w, "t", resT);
            Column(w, "id", Res, res, r => (long)r.Who);
            Column(w, "mp", Res, res, r => (long)Math.Max(0, r.Mp));
            Column(w, "sh", Res, res, r => (long)Math.Clamp(r.Shield, 0, 100));
            w.WriteEndObject();
        }
        var lb = Order(Gauge, r => r.T, out var lbT);
        w.WriteStartObject("lb");
        Column(w, "t", lbT);
        Column(w, "v", Gauge, lb, r => (long)Math.Max(0, r.Value));
        Column(w, "max", Gauge, lb, r => (long)Math.Max(0, r.Max));
        w.WriteEndObject();

        var pre = Order(Pre, r => Math.Clamp(r.T, -PreFrom, 0), out var preT, PreFrom);
        w.WriteStartObject("pre");
        w.WriteNumber("from", PreFrom);
        Column(w, "t", preT);
        Column(w, "src", Pre, pre, r => (long)r.Source);
        Column(w, "a", Pre, pre, r => (long)r.Action);
        Column(w, "tgt", Pre, pre, r => (long)r.Target);
        Column(w, "f", Pre, pre, r => (long)r.Flags);
        w.WriteEndObject();

        if (Countdown is { } cd && double.IsFinite(cd.Length) && double.IsFinite(cd.End))
        {
            w.WriteStartObject("cd");
            w.WriteNumber("len", CastEvent.Round(cd.Length, 2));
            w.WriteNumber("end", CastEvent.Round(cd.End, 2));
            w.WriteEndObject();
        }

        w.WriteStartObject("items");
        foreach (var (id, item) in Items.OrderBy(kv => kv.Key))
        {
            w.WriteStartObject(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            w.WriteString("name", item.Name);
            w.WriteNumber("icon", item.Icon);
            if (item.Hq) w.WriteBoolean("hq", true);
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    /// <summary>
    /// Reads the block back, never throwing: a table whose columns aren't all arrays of the same length
    /// is left out, a value that isn't a number reads as 0, and at most <see cref="MaxRows"/> rows each.
    /// </summary>
    public static CombatLog Read(JsonObject o)
    {
        var c = new CombatLog();
        if (o["thinned"] is JsonArray thinned)
            foreach (var t in thinned)
                if (t is JsonValue v && v.TryGetValue<string>(out var s) && Droppable.Contains(s) && !c.Thinned.Contains(s))
                    c.Thinned.Add(s);
        // A block with any of 0.4.0's tables (or one of them dropped) is written back with them.
        c.Extended = o.ContainsKey("use") || o.ContainsKey("lb") || o.ContainsKey("pre") || o.ContainsKey("items")
            || c.Thinned.Any(x => x is "use" or "area" or "hate" or "res");
        if (Table(o["fx"], out var fx, "t", "a", "o", "r", "v", "k", "h"))
        {
            var t = Times(fx[0]);
            var bonus = Optional(o["fx"], "b", fx[0].Count);
            for (var i = 0; i < t.Length; i++)
                c.Fx.Add(new FxRow(t[i], (uint)Math.Clamp(L(fx[1], i), 0, uint.MaxValue), (int)I(fx[2], i), (int)I(fx[3], i), L(fx[4], i), (int)I(fx[5], i), L(fx[6], i),
                    bonus == null ? 0 : (int)Math.Clamp(L(bonus, i), sbyte.MinValue, sbyte.MaxValue)));
        }
        if (Table(o["tick"], out var tick, "t", "on", "src", "s", "v", "heal"))
        {
            var t = Times(tick[0]);
            // `h` is optional (a file from before the alignment round has none): −1 where it's missing or not as long.
            var h = o["tick"]!["h"] is JsonArray hp && hp.Count == tick[0].Count ? hp : null;
            for (var i = 0; i < t.Length; i++)
                c.Ticks.Add(new TickRow(t[i], (int)I(tick[1], i), (int)I(tick[2], i), (uint)Math.Clamp(L(tick[3], i), 0, uint.MaxValue), L(tick[4], i), L(tick[5], i) != 0,
                    h == null ? -1 : Hp(h, i)));
        }
        if (Table(o["cast"], out var cast, "t", "src", "a", "dur", "x"))
        {
            var t = Times(cast[0]);
            var why = Optional(o["cast"], "why", cast[0].Count);
            for (var i = 0; i < t.Length; i++)
                c.Casts.Add(new CastRow(t[i], (int)I(cast[1], i), (uint)Math.Clamp(L(cast[2], i), 0, uint.MaxValue), (float)D(cast[3], i), L(cast[4], i) != 0,
                    why == null ? 0 : (int)Math.Clamp(L(why, i), 0, 2)));
        }
        if (Table(o["st"], out var st, "t", "on", "src", "s", "a", "dur", "n", "gain"))
        {
            var t = Times(st[0]);
            for (var i = 0; i < t.Length; i++)
                c.Statuses.Add(new StatusRow(t[i], (int)I(st[1], i), (int)I(st[2], i), (uint)Math.Clamp(L(st[3], i), 0, uint.MaxValue),
                    (uint)Math.Clamp(L(st[4], i), 0, uint.MaxValue), (float)D(st[5], i), (int)I(st[6], i), L(st[7], i) != 0));
        }
        if (Table(o["use"], out var use, "t", "src", "a", "tgt", "n", "f"))
        {
            var t = Times(use[0]);
            for (var i = 0; i < t.Length; i++)
                c.Uses.Add(new UseRow(t[i], (int)I(use[1], i), Id(use[2], i), (int)I(use[3], i), (int)Math.Clamp(L(use[4], i), 0, 32), (int)Math.Clamp(L(use[5], i), 0, ushort.MaxValue)));
        }
        if (Table(o["area"], out var area, "t", "src", "a", "x", "z"))
        {
            var t = Times(area[0]);
            for (var i = 0; i < t.Length; i++)
                c.Areas.Add(new AreaRow(t[i], (int)I(area[1], i), Id(area[2], i), (float)D(area[3], i), (float)D(area[4], i)));
        }
        if (Table(o["hate"], out var hate, "t", "on", "who", "pct"))
        {
            var t = Times(hate[0]);
            for (var i = 0; i < t.Length; i++)
                c.Hate.Add(new HateRow(t[i], (int)I(hate[1], i), (int)I(hate[2], i), (int)Math.Clamp(L(hate[3], i), -1, 100)));
        }
        if (Table(o["res"], out var res, "t", "id", "mp", "sh"))
        {
            var t = Times(res[0]);
            for (var i = 0; i < t.Length; i++)
                c.Res.Add(new ResRow(t[i], (int)I(res[1], i), (int)Math.Clamp(L(res[2], i), 0, int.MaxValue), (int)Math.Clamp(L(res[3], i), 0, 100)));
        }
        if (Table(o["lb"], out var lb, "t", "v", "max"))
        {
            var t = Times(lb[0]);
            for (var i = 0; i < t.Length; i++)
                c.Gauge.Add(new GaugeRow(t[i], (int)Math.Clamp(L(lb[1], i), 0, int.MaxValue), (int)Math.Clamp(L(lb[2], i), 0, int.MaxValue)));
        }
        if (Table(o["pre"], out var pre, "t", "src", "a", "tgt", "f"))
        {
            var from = o["pre"]!["from"] is JsonValue fv && fv.TryGetValue<double>(out var fd) && double.IsFinite(fd) ? Math.Clamp(fd, 0, 3600) : PreFrom;
            var t = Times(pre[0]);
            for (var i = 0; i < t.Length; i++)
                c.Pre.Add(new UseRow(Math.Round(t[i] - from, 2), (int)I(pre[1], i), Id(pre[2], i), (int)I(pre[3], i), 0, (int)Math.Clamp(L(pre[4], i), 0, ushort.MaxValue)));
        }
        if (o["cd"] is JsonObject cd && cd["len"] is JsonValue len && cd["end"] is JsonValue end
            && len.TryGetValue<double>(out var l) && end.TryGetValue<double>(out var e) && double.IsFinite(l) && double.IsFinite(e))
            c.Countdown = (l, e);
        if (o["items"] is JsonObject items)
            foreach (var (key, value) in items)
                if (uint.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && value is JsonObject item)
                    c.Items[id] = new ItemInfo(item["name"] is JsonValue nv && nv.TryGetValue<string>(out var name) ? name : string.Empty,
                        item["icon"] is JsonValue iv && iv.TryGetValue<double>(out var icon) && icon is >= 0 and <= uint.MaxValue ? (uint)icon : 0,
                        item["hq"] is JsonValue hv && hv.TryGetValue<bool>(out var hq) && hq);
        return c;
    }

    /// <summary>An optional column of a table: there and as long as its time column, or null (read as its default).</summary>
    private static JsonArray? Optional(JsonNode? table, string name, int count) => table is JsonObject o && o[name] is JsonArray a && a.Count == count ? a : null;

    private static uint Id(JsonArray a, int i) => (uint)Math.Clamp(L(a, i), 0, uint.MaxValue);

    private static bool Table(JsonNode? node, out JsonArray[] columns, params string[] names)
    {
        columns = new JsonArray[names.Length];
        if (node is not JsonObject o) return false;
        for (var i = 0; i < names.Length; i++)
        {
            if (o[names[i]] is not JsonArray a || (i > 0 && a.Count != columns[0].Count)) return false;
            columns[i] = a;
        }
        return true;
    }

    /// <summary>The time column back to seconds from the pull (deltas below 0 read as 0).</summary>
    private static double[] Times(JsonArray deltas)
    {
        var n = Math.Min(deltas.Count, MaxRows);
        var t = new double[n];
        long cs = 0;
        for (var i = 0; i < n; i++)
        {
            cs += Math.Max(0, L(deltas, i));
            t[i] = cs / 100.0;
        }
        return t;
    }

    private static double D(JsonArray a, int i) =>
        a[i] is JsonValue v && (v.TryGetValue<double>(out var d) || (v.TryGetValue<long>(out var l) && (d = l) == l)) && double.IsFinite(d) ? d : 0;

    private static long L(JsonArray a, int i)
    {
        var d = D(a, i);
        return d >= long.MinValue && d <= long.MaxValue ? (long)Math.Round(d) : 0;
    }

    private static long I(JsonArray a, int i) => Math.Clamp(L(a, i), int.MinValue, int.MaxValue);

    /// <summary>An HP column's value: a whole number from 0 (to a trillion), else −1 (not known; also for anything that isn't a number).</summary>
    private static long Hp(JsonArray a, int i) =>
        a[i] is JsonValue v && (v.TryGetValue<double>(out var d) || (v.TryGetValue<long>(out var l) && (d = l) == l)) && d is >= 0 and <= 1e12
            ? (long)Math.Round(d)
            : -1;
}
