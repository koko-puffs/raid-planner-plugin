using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RaidPlanner.UI.Model;

/// <summary>What a tracked cooldown is for (its group in Settings → Cooldowns, and the tracker's order).</summary>
public enum CooldownKind
{
    /// <summary>The two-minute raid buffs: Battle Litany, Divination, Technical Step…</summary>
    RaidBuff,

    /// <summary>The party's mitigation from tanks and DPS: Reprisal, Addle, Feint, Troubadour, Divine Veil…</summary>
    Mitigation,

    /// <summary>Healers' party heals and mitigation: Temperance, Expedient, Neutral Sect, Kerachole…</summary>
    Healer,

    /// <summary>What a raise waits on: Swiftcast.</summary>
    Raise,

    /// <summary>Tanks' Provoke, Shirk and invulnerabilities.</summary>
    Tank,

    /// <summary>Interrupts and stuns: Interject, Head Graze, Low Blow, Leg Sweep.</summary>
    Interrupt,
}

/// <summary>
/// A cooldown the tracker can follow: its key (its first name), what it's for, its English names on
/// the game's Action sheet (each row with one of them counts: a renamed or upgraded action, such as
/// Mug and Dokumori, is one cooldown), the jobs that have it, and whether it's tracked unless you say otherwise.
/// </summary>
public sealed record CooldownSpec(string Key, CooldownKind Kind, IReadOnlyList<string> Names, IReadOnlyList<string> Jobs, bool On)
{
    /// <summary>Does this job have it?</summary>
    public bool For(string? job)
    {
        if (string.IsNullOrEmpty(job)) return false;
        foreach (var j in Jobs)
            if (string.Equals(j, job, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

/// <summary>
/// The cooldowns the tracker knows, by their English names (the game side finds their rows on the
/// Action sheet read in English, as the meter's lists are: <c>Core/Meter/CombatLists</c>), so the ids,
/// icons, base recasts and charges are the game's own and a new patch's rows are found without a
/// release. Level 100 jobs; a class (GLA, CNJ…) isn't listed.
/// </summary>
public static class CooldownCatalog
{
    private static readonly string[] Tanks = { "PLD", "WAR", "DRK", "GNB" };
    private static readonly string[] Melee = { "MNK", "DRG", "NIN", "SAM", "RPR", "VPR" };
    private static readonly string[] Ranged = { "BRD", "MCH", "DNC" };
    private static readonly string[] Casters = { "BLM", "SMN", "RDM", "PCT" };
    private static readonly string[] Raisers = { "WHM", "SCH", "AST", "SGE", "SMN", "RDM" };

    private static CooldownSpec S(CooldownKind kind, bool on, string name, params string[] jobs) => new(name, kind, new[] { name }, jobs, on);

    private static CooldownSpec S(CooldownKind kind, bool on, string[] names, params string[] jobs) => new(names[0], kind, names, jobs, on);

    /// <summary>Every cooldown, in the tracker's order (by kind, then as listed here).</summary>
    public static readonly IReadOnlyList<CooldownSpec> All = new[]
    {
        S(CooldownKind.RaidBuff, true, "Battle Litany", "DRG"),
        S(CooldownKind.RaidBuff, true, "Brotherhood", "MNK"),
        S(CooldownKind.RaidBuff, true, "Arcane Circle", "RPR"),
        S(CooldownKind.RaidBuff, true, new[] { "Dokumori", "Mug" }, "NIN"),
        S(CooldownKind.RaidBuff, true, "Battle Voice", "BRD"),
        S(CooldownKind.RaidBuff, true, "Radiant Finale", "BRD"),
        S(CooldownKind.RaidBuff, true, "Technical Step", "DNC"),
        S(CooldownKind.RaidBuff, true, "Searing Light", "SMN"),
        S(CooldownKind.RaidBuff, true, "Embolden", "RDM"),
        S(CooldownKind.RaidBuff, true, "Starry Muse", "PCT"),
        S(CooldownKind.RaidBuff, true, "Divination", "AST"),
        S(CooldownKind.RaidBuff, true, "Chain Stratagem", "SCH"),

        new("Reprisal", CooldownKind.Mitigation, new[] { "Reprisal" }, Tanks, true),
        new("Addle", CooldownKind.Mitigation, new[] { "Addle" }, Casters, true),
        new("Feint", CooldownKind.Mitigation, new[] { "Feint" }, Melee, true),
        S(CooldownKind.Mitigation, true, "Troubadour", "BRD"),
        S(CooldownKind.Mitigation, true, "Tactician", "MCH"),
        S(CooldownKind.Mitigation, true, "Shield Samba", "DNC"),
        S(CooldownKind.Mitigation, true, "Dismantle", "MCH"),
        S(CooldownKind.Mitigation, true, "Divine Veil", "PLD"),
        S(CooldownKind.Mitigation, true, "Passage of Arms", "PLD"),
        S(CooldownKind.Mitigation, true, "Shake It Off", "WAR"),
        S(CooldownKind.Mitigation, true, "Dark Missionary", "DRK"),
        S(CooldownKind.Mitigation, true, "Heart of Light", "GNB"),
        S(CooldownKind.Mitigation, true, "Magick Barrier", "RDM"),
        S(CooldownKind.Mitigation, false, "Tempera Coat", "PCT"),
        S(CooldownKind.Mitigation, false, "Nature's Minne", "BRD"),
        S(CooldownKind.Mitigation, false, "Improvisation", "DNC"),
        S(CooldownKind.Mitigation, false, "Mantra", "MNK"),

        S(CooldownKind.Healer, true, "Temperance", "WHM"),
        S(CooldownKind.Healer, false, "Liturgy of the Bell", "WHM"),
        S(CooldownKind.Healer, false, "Asylum", "WHM"),
        S(CooldownKind.Healer, false, "Plenary Indulgence", "WHM"),
        S(CooldownKind.Healer, true, "Expedient", "SCH"),
        S(CooldownKind.Healer, true, "Fey Illumination", "SCH"),
        S(CooldownKind.Healer, false, "Sacred Soil", "SCH"),
        S(CooldownKind.Healer, false, "Summon Seraph", "SCH"),
        S(CooldownKind.Healer, false, "Seraphism", "SCH"),
        S(CooldownKind.Healer, true, "Collective Unconscious", "AST"),
        S(CooldownKind.Healer, true, "Neutral Sect", "AST"),
        S(CooldownKind.Healer, false, "Macrocosmos", "AST"),
        S(CooldownKind.Healer, false, "Earthly Star", "AST"),
        S(CooldownKind.Healer, true, "Kerachole", "SGE"),
        S(CooldownKind.Healer, true, "Holos", "SGE"),
        S(CooldownKind.Healer, true, "Panhaima", "SGE"),
        S(CooldownKind.Healer, false, new[] { "Physis II", "Physis" }, "SGE"),
        S(CooldownKind.Healer, false, "Philosophia", "SGE"),

        new("Swiftcast", CooldownKind.Raise, new[] { "Swiftcast" }, Raisers, true),

        new("Provoke", CooldownKind.Tank, new[] { "Provoke" }, Tanks, false),
        new("Shirk", CooldownKind.Tank, new[] { "Shirk" }, Tanks, false),
        S(CooldownKind.Tank, false, "Hallowed Ground", "PLD"),
        S(CooldownKind.Tank, false, "Holmgang", "WAR"),
        S(CooldownKind.Tank, false, "Living Dead", "DRK"),
        S(CooldownKind.Tank, false, "Superbolide", "GNB"),

        new("Interject", CooldownKind.Interrupt, new[] { "Interject" }, Tanks, false),
        new("Head Graze", CooldownKind.Interrupt, new[] { "Head Graze" }, Ranged, false),
        new("Low Blow", CooldownKind.Interrupt, new[] { "Low Blow" }, Tanks, false),
        new("Leg Sweep", CooldownKind.Interrupt, new[] { "Leg Sweep" }, Melee, false),
    };

    /// <summary>The kinds in the tracker's order.</summary>
    public static readonly CooldownKind[] Kinds =
        { CooldownKind.RaidBuff, CooldownKind.Mitigation, CooldownKind.Healer, CooldownKind.Raise, CooldownKind.Tank, CooldownKind.Interrupt };

    private static readonly Dictionary<string, CooldownSpec> ByKey = All.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

    public static CooldownSpec? Find(string key) => ByKey.TryGetValue(key, out var s) ? s : null;

    /// <summary>A kind in Settings' words.</summary>
    public static string KindWord(CooldownKind kind) => kind switch
    {
        CooldownKind.RaidBuff => "Raid buffs",
        CooldownKind.Mitigation => "Party mitigation",
        CooldownKind.Healer => "Healers' party heals and mitigation",
        CooldownKind.Raise => "Raises: Swiftcast",
        CooldownKind.Tank => "Tanks: Provoke, Shirk, invulnerabilities",
        _ => "Interrupts and stuns",
    };

    /// <summary>Whether a cooldown is tracked: as you picked it (Settings → Cooldowns), else its default.</summary>
    public static bool IsOn(CooldownSpec spec, IReadOnlyDictionary<string, bool>? picks) =>
        picks != null && picks.TryGetValue(spec.Key, out var on) ? on : spec.On;

    /// <summary>How many of a kind's cooldowns are tracked, of how many.</summary>
    public static (int On, int Of) Count(CooldownKind kind, IReadOnlyDictionary<string, bool>? picks)
    {
        var on = 0;
        var of = 0;
        foreach (var s in All)
        {
            if (s.Kind != kind) continue;
            of++;
            if (IsOn(s, picks)) on++;
        }
        return (on, of);
    }
}

/// <summary>A cooldown's state now: how long until a charge is ready, the charges ready, the recast it was worked out by.</summary>
/// <param name="ReadyIn">Seconds until one is ready (0 when one is).</param>
/// <param name="NextIn">Seconds until the next charge comes back (0 when they're all back).</param>
/// <param name="Learned">The recast was learned from this player's own uses (two closer than the sheet's recast).</param>
/// <param name="Seen">They've used it since the tracker began watching (before that it's taken to be ready).</param>
public readonly record struct CooldownState(double ReadyIn, double NextIn, int Charges, int MaxCharges, double Recast, bool Learned, bool Seen);

/// <summary>
/// The party's cooldowns as the tracker learns them from their uses (the parser's <c>Used</c>, every party
/// action that goes off): each (member, cooldown) keeps its charges and when the charge being
/// recharged began. Charges come back one at a time, a recast each. The recast is the sheet's
/// (or what the game says for one of your own role's actions), until two uses closer than it teach the
/// real one (SPEC4 §2.13: a trait, a level, a charge the sheet doesn't know). A use this close to the
/// last of the same cooldown (<see cref="SameUse"/>) is the same use seen twice (a pet's echo).
/// Game-free and tested; the recording player's own cooldowns are the game's, exactly, not this.
/// </summary>
public sealed class CooldownBook
{
    /// <summary>Two uses of a cooldown this close are one (seconds).</summary>
    public const double SameUse = 1.0;

    /// <summary>A use at least this long before the recast says it's ready teaches the recast (seconds).</summary>
    public const double Slack = 1.0;

    private sealed class Track
    {
        public int Charges;
        public double Since;
        public double Recast = double.NaN;
        public double LastUse = double.NegativeInfinity;
    }

    private readonly Dictionary<(uint Member, string Key), Track> tracks = new();

    /// <summary>How many (member, cooldown) pairs it follows.</summary>
    public int Count => tracks.Count;

    /// <summary>Forget everything (a new zone; a wipe resets the game's recasts).</summary>
    public void Clear() => tracks.Clear();

    /// <summary>Charges come back up to <paramref name="t"/>.</summary>
    private static void Advance(ref int charges, ref double since, double t, double recast, int max)
    {
        if (charges >= max) return;
        if (!(recast > 0))
        {
            charges = max;
            return;
        }
        var k = (int)Math.Floor((t - since) / recast);
        if (k <= 0) return;
        charges = Math.Min(max, charges + k);
        since += k * recast;
    }

    /// <summary>A party member used a cooldown at <paramref name="t"/> (seconds on the tracker's clock).</summary>
    public void Use(uint member, string key, double t, double baseRecast, int maxCharges)
    {
        if (!double.IsFinite(t) || !(baseRecast > 0)) return;
        var max = Math.Max(1, maxCharges);
        if (!tracks.TryGetValue((member, key), out var track))
        {
            track = new Track { Charges = max, Since = t };
            tracks[(member, key)] = track;
        }
        if (t - track.LastUse < SameUse && t >= track.LastUse) return;
        var recast = double.IsNaN(track.Recast) ? baseRecast : track.Recast;
        var charges = Math.Min(track.Charges, max);
        var since = track.Since;
        Advance(ref charges, ref since, t, recast, max);
        if (charges <= 0)
        {
            // Used before it should be ready: the recast is shorter than thought (a trait, a level).
            var early = since + recast - t;
            var seen = t - since;
            if (early > Slack && seen >= SameUse) track.Recast = seen;
            charges = 1;
            since = t;
        }
        if (charges >= max) since = t;
        charges--;
        track.Charges = charges;
        track.Since = since;
        track.LastUse = t;
    }

    /// <summary>A cooldown's state at <paramref name="now"/>; ready (and not seen) when they haven't used it.</summary>
    public CooldownState State(uint member, string key, double now, double baseRecast, int maxCharges)
    {
        var max = Math.Max(1, maxCharges);
        if (!tracks.TryGetValue((member, key), out var track) || !(baseRecast > 0))
            return new CooldownState(0, 0, max, max, Math.Max(0, baseRecast), false, false);
        var learned = !double.IsNaN(track.Recast);
        var recast = learned ? track.Recast : baseRecast;
        var charges = Math.Min(track.Charges, max);
        var since = track.Since;
        Advance(ref charges, ref since, now, recast, max);
        var next = charges >= max ? 0 : Math.Max(0, since + recast - now);
        return new CooldownState(charges > 0 ? 0 : next, next, charges, max, recast, learned, true);
    }
}

/// <summary>A party member as the tracker shows them: the game's id, their spot, job and role, and whether it's you.</summary>
public sealed record CooldownMember(uint Id, string Slot, string Job, byte Role, bool IsYou);

/// <summary>What the game says about a tracked cooldown: the action's id, name (in the client's language) and icon, its base recast (s) and charges.</summary>
public sealed record CooldownAction(string Key, uint Id, string Name, uint Icon, double Recast, int Charges);

/// <summary>One of your own cooldowns as the game has it now (exact): until a charge is ready, the next charge, charges, recast.</summary>
public readonly record struct OwnCooldown(double ReadyIn, double NextIn, int Charges, int MaxCharges, double Recast);

/// <summary>A row of the tracker: who, which cooldown, and its state now. <see cref="Exact"/> for your own (the game's numbers).</summary>
public sealed record CooldownLine(CooldownMember Who, CooldownSpec Spec, CooldownAction Action, double ReadyIn, double NextIn, int Charges, int MaxCharges,
    double Recast, bool Exact, bool Learned, bool Seen)
{
    public bool Ready => ReadyIn <= 0;

    /// <summary>How far the recharge has come (0–1; 1 ready).</summary>
    public float Fill => Ready ? 1 : Recast > 0 ? (float)Math.Clamp(1 - ReadyIn / Recast, 0, 1) : 0;
}

/// <summary>The tracker's rows and words, worked out away from ImGui so they're tested.</summary>
public static class CooldownView
{
    private static readonly string[] SpotOrderOf = { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" };

    public static int SpotOrder(string slot) => Array.IndexOf(SpotOrderOf, slot) is var i and >= 0 ? i : 99;

    /// <summary>
    /// The tracker's rows now: each party member's tracked cooldowns (those their job has), by kind,
    /// then as the catalogue lists them, then spot order. Your own are the game's (<paramref name="own"/>),
    /// the others' the book's. Without <paramref name="you"/>, yours are left out; with
    /// <paramref name="hideReady"/>, those ready are.
    /// </summary>
    public static List<CooldownLine> Lines(IReadOnlyList<CooldownMember> party, Func<string, CooldownAction?> action, IReadOnlyDictionary<string, bool>? picks,
        CooldownBook book, Func<string, OwnCooldown?> own, double now, bool you = true, bool hideReady = false)
    {
        var lines = new List<CooldownLine>();
        var order = 0;
        var orderOf = new Dictionary<CooldownSpec, int>();
        foreach (var spec in CooldownCatalog.All) orderOf[spec] = order++;
        foreach (var spec in CooldownCatalog.All)
        {
            if (!CooldownCatalog.IsOn(spec, picks)) continue;
            CooldownAction? a = null;
            var looked = false;
            foreach (var m in party)
            {
                if (!spec.For(m.Job)) continue;
                if (m.IsYou && !you) continue;
                if (!looked)
                {
                    a = action(spec.Key);
                    looked = true;
                }
                if (a == null) break;
                CooldownLine line;
                if (m.IsYou && own(spec.Key) is { } o)
                    line = new CooldownLine(m, spec, a, Math.Max(0, o.ReadyIn), Math.Max(0, o.NextIn), o.Charges, Math.Max(1, o.MaxCharges), o.Recast > 0 ? o.Recast : a.Recast,
                        true, false, true);
                else
                {
                    var s = book.State(m.Id, spec.Key, now, a.Recast, a.Charges);
                    line = new CooldownLine(m, spec, a, s.ReadyIn, s.NextIn, s.Charges, s.MaxCharges, s.Recast, false, s.Learned, s.Seen);
                }
                if (hideReady && line.Ready) continue;
                lines.Add(line);
            }
        }
        lines.Sort((x, y) =>
        {
            var c = ((int)x.Spec.Kind).CompareTo((int)y.Spec.Kind);
            if (c != 0) return c;
            c = orderOf[x.Spec].CompareTo(orderOf[y.Spec]);
            return c != 0 ? c : SpotOrder(x.Who.Slot).CompareTo(SpotOrder(y.Who.Slot));
        });
        return lines;
    }

    /// <summary>The time to ready as the tracker writes it: "Ready", "42" (seconds, rounded up), "1:05".</summary>
    public static string Time(double readyIn)
    {
        if (!(readyIn > 0)) return "Ready";
        var s = (int)Math.Ceiling(readyIn - 1e-9);
        return s < 60 ? s.ToString(CultureInfo.InvariantCulture) : Format.Clock(s);
    }

    /// <summary>The charges as a short count ("1/2"), or null for a cooldown with one.</summary>
    public static string? Charges(CooldownLine line) =>
        line.MaxCharges > 1 ? line.Charges.ToString(CultureInfo.InvariantCulture) + "/" + line.MaxCharges.ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>A row's tooltip: whose and how it's known.</summary>
    public static string Tip(CooldownLine line)
    {
        var who = line.Who.IsYou ? "Yours" : $"{line.Who.Slot} · {line.Who.Job}";
        var how = line.Exact ? "the game's own timer"
            : !line.Seen ? "not used since the tracker began watching, so taken to be ready"
            : line.Learned ? $"a {Format.Clock(line.Recast)} recast learned from their own uses"
            : $"the game's base recast ({Format.Clock(line.Recast)}) from their last use: an estimate until they use it twice";
        return $"{line.Action.Name} · {who}. {char.ToUpperInvariant(how[0])}{how[1..]}.";
    }
}

/// <summary>
/// A sample of the tracker (Settings → Cooldowns, `/raidplan cooldowns preview`), to place and size
/// it without a party: the meter's sample party (PLD, DRK, WHM, SCH, DRG, NIN, BRD, SMN) with their
/// default cooldowns, each used now and then, recharging, coming back. Made-up and shown only under a Preview tag.
/// </summary>
public static class CooldownSample
{
    public static readonly IReadOnlyList<CooldownMember> Party = new[]
    {
        new CooldownMember(1, "MT", "PLD", 1, true), new CooldownMember(2, "OT", "DRK", 1, false),
        new CooldownMember(3, "H1", "WHM", 4, false), new CooldownMember(4, "H2", "SCH", 4, false),
        new CooldownMember(5, "M1", "DRG", 2, false), new CooldownMember(6, "M2", "NIN", 2, false),
        new CooldownMember(7, "R1", "BRD", 3, false), new CooldownMember(8, "R2", "SMN", 3, false),
    };

    /// <summary>A made-up action for a cooldown (no icon: the tracker draws its kind's mark): 120 s for raid buffs, 90 for the rest, 40 for Swiftcast.</summary>
    public static CooldownAction Action(string key)
    {
        var spec = CooldownCatalog.Find(key);
        var recast = spec?.Kind == CooldownKind.RaidBuff ? 120 : key == "Swiftcast" ? 40 : key == "Reprisal" ? 60 : 90;
        return new CooldownAction(key, 0, spec?.Names[0] ?? key, 0, recast, 1);
    }

    /// <summary>The sample's rows at second <paramref name="t"/> (it loops): each cooldown used at its own moment of the loop, then recharging.</summary>
    public static List<CooldownLine> At(double t, IReadOnlyDictionary<string, bool>? picks, bool you = true, bool hideReady = false)
    {
        var lines = new List<CooldownLine>();
        var i = 0;
        foreach (var spec in CooldownCatalog.All)
        {
            if (!CooldownCatalog.IsOn(spec, picks)) continue;
            foreach (var m in Party)
            {
                if (!spec.For(m.Job) || (m.IsYou && !you)) continue;
                var a = Action(spec.Key);
                // Each used once a loop (its recast and a bit more), at its own offset.
                var loop = a.Recast + 20 + (i % 3) * 10;
                var into = ((t + i * 17) % loop + loop) % loop;
                var readyIn = Math.Max(0, a.Recast - into);
                i++;
                var line = new CooldownLine(m, spec, a, readyIn, readyIn, readyIn > 0 ? 0 : 1, 1, a.Recast, m.IsYou, false, true);
                if (hideReady && line.Ready) continue;
                lines.Add(line);
            }
        }
        // Already in order: the catalogue is by kind, the party in spot order.
        return lines;
    }
}
