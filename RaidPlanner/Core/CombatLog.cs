using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RaidPlanner.Core;

/// <summary>One of the party's action effects (<c>combat.fx</c>): time, action, origin (party id), recipient (party or actor id, 0 not in the recording), amount, kind + flags, the recipient's HP before (heals; −1 otherwise).</summary>
public readonly record struct FxRow(double T, uint Action, int Origin, int Recipient, long Amount, int Kind, long HpBefore);

/// <summary>
/// A DoT or HoT tick (<c>combat.tick</c>): on whom, credited to whom (a party id; an actor's for an enemy's bleed; 0 when the game
/// didn't say), the status, the amount, a heal, and (since the alignment round, the optional column <c>h</c>) the HP the one it
/// ticked on had just before it, as the game side read it (a HoT's target, or a party member; −1 not known): what a HoT's
/// overheal is measured against, as the meter measures it.
/// </summary>
public readonly record struct TickRow(double T, int On, int Source, uint Status, long Amount, bool Heal, long HpBefore = -1);

/// <summary>A party member's cast (<c>combat.cast</c>): when it began, who, the action, its cast time, and whether it was cancelled or interrupted.</summary>
public readonly record struct CastRow(double T, int Source, uint Action, float Duration, bool Cancelled);

/// <summary>A status the party put on someone, gained or gone (<c>combat.st</c>): on whom, by whom, the status, the action that put it on (0 unknown), its time left, its stacks or param.</summary>
public readonly record struct StatusRow(double T, int On, int Source, uint Status, uint Action, float Duration, int Stacks, bool Gain);

/// <summary>
/// The recording's <c>combat</c> block (the site's contract §2.8): the party's own fight, beside the
/// events so it never counts towards their cap, columnar and in time order. Each table's columns are
/// equally long; times are deltas in centiseconds from the row before (the first from the pull's
/// start; never below 0). A table dropped to fit the site's limit is left out and named in
/// <see cref="Thinned"/> (<c>cast</c> first, then <c>tick</c>; never <c>fx</c>).
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

    /// <summary>The tables left out to fit the site's limit (<c>"cast"</c>, <c>"tick"</c>).</summary>
    public List<string> Thinned { get; } = new();

    public bool HasCasts => !Thinned.Contains("cast");

    public bool HasTicks => !Thinned.Contains("tick");

    public bool IsEmpty => Fx.Count == 0 && Ticks.Count == 0 && Casts.Count == 0 && Statuses.Count == 0;

    /// <summary>A copy without some tables (the rows shared): what's sent when the whole is too big.</summary>
    public CombatLog Without(params string[] tables)
    {
        var c = new CombatLog();
        c.Fx.AddRange(Fx);
        c.Statuses.AddRange(Statuses);
        if (!tables.Contains("tick")) c.Ticks.AddRange(Ticks);
        if (!tables.Contains("cast")) c.Casts.AddRange(Casts);
        c.Thinned.AddRange(Thinned);
        foreach (var t in tables)
            if (!c.Thinned.Contains(t))
                c.Thinned.Add(t);
        return c;
    }

    /// <summary>A time as the file has it: whole centiseconds.</summary>
    private static long Cs(double t) => double.IsFinite(t) ? (long)Math.Round(t * 100, MidpointRounding.AwayFromZero) : 0;

    /// <summary>Each row's time as a delta from the one before, in centiseconds, the rows in time order (stable).</summary>
    private static List<int> Order<T>(List<T> rows, Func<T, double> time, out long[] deltas)
    {
        var order = Enumerable.Range(0, rows.Count).OrderBy(i => Cs(time(rows[i]))).ThenBy(i => i).ToList();
        deltas = new long[order.Count];
        long prev = 0;
        for (var i = 0; i < order.Count; i++)
        {
            var cs = Math.Max(0, Cs(time(rows[order[i]])));
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

        if (Thinned.Count > 0)
        {
            w.WriteStartArray("thinned");
            foreach (var t in Thinned) w.WriteStringValue(t);
            w.WriteEndArray();
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
                if (t is JsonValue v && v.TryGetValue<string>(out var s) && s is "cast" or "tick" && !c.Thinned.Contains(s))
                    c.Thinned.Add(s);
        if (Table(o["fx"], out var fx, "t", "a", "o", "r", "v", "k", "h"))
        {
            var t = Times(fx[0]);
            for (var i = 0; i < t.Length; i++)
                c.Fx.Add(new FxRow(t[i], (uint)Math.Clamp(L(fx[1], i), 0, uint.MaxValue), (int)I(fx[2], i), (int)I(fx[3], i), L(fx[4], i), (int)I(fx[5], i), L(fx[6], i)));
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
            for (var i = 0; i < t.Length; i++)
                c.Casts.Add(new CastRow(t[i], (int)I(cast[1], i), (uint)Math.Clamp(L(cast[2], i), 0, uint.MaxValue), (float)D(cast[3], i), L(cast[4], i) != 0));
        }
        if (Table(o["st"], out var st, "t", "on", "src", "s", "a", "dur", "n", "gain"))
        {
            var t = Times(st[0]);
            for (var i = 0; i < t.Length; i++)
                c.Statuses.Add(new StatusRow(t[i], (int)I(st[1], i), (int)I(st[2], i), (uint)Math.Clamp(L(st[3], i), 0, uint.MaxValue),
                    (uint)Math.Clamp(L(st[4], i), 0, uint.MaxValue), (float)D(st[5], i), (int)I(st[6], i), L(st[7], i) != 0));
        }
        return c;
    }

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
