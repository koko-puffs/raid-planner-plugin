using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.UI.Model;

/// <summary>What a line of the death recap's card is: a hit, a heal, or a barrier put on.</summary>
public enum RecapKind
{
    Hit,
    Heal,
    Shield,
}

/// <summary>
/// A line of the death recap's card: how long before the death (<see cref="Offset"/>, seconds, 0 or less), what it was and from
/// whom (a heal's healer, a barrier's), its amount (a hit negative; a barrier's isn't known: 0), the HP after it, and whether
/// it's the killing blow.
/// </summary>
public sealed record RecapRow(double Offset, string What, string? Who, long Amount, long HpAfter, RecapKind Kind, bool Killing);

/// <summary>
/// One death as the death recap shows it (since 0.9.0), made once as it's seen from the meter's own death
/// (<see cref="DeathEntry"/>: the Deaths tab's): the fight and its index there (what Open in the meter opens), who, when and in
/// which phase, the killing blow, how far over and what it was, the last seconds' lines, the mitigations up at the hit and, for a
/// tank, their big cooldowns that weren't, and Weakness.
/// </summary>
public sealed record RecapCard(
    int FightId,
    int Index,
    int PartyId,
    string Name,
    string Slot,
    string Job,
    string Role,
    bool IsYou,
    double At,
    string? Phase,
    string KilledBy,
    long Amount,
    long Overkill,
    DeathHit Hit,
    IReadOnlyList<RecapRow> Rows,
    IReadOnlyList<string> Up,
    bool MitigationKnown,
    bool NoMitigation,
    IReadOnlyList<string> Missing,
    bool Weakness,
    long MaxHp)
{
    /// <summary>Who it's about: their name, or with Show: Jobs only (Settings → Damage meter) their spot.</summary>
    public string Who(bool jobsOnly) => jobsOnly || Name.Length == 0 ? Slot : Name;
}

/// <summary>The death recap's words and lines (since 0.9.0), worked out away from ImGui so they're tested.</summary>
public static class DeathRecapView
{
    /// <summary>The card shows the last this many seconds before the death.</summary>
    public const double Seconds = 6;

    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    /// <summary>The card for the death at <paramref name="index"/> of an encounter (null for none there).</summary>
    public static RecapCard? Card(MeterEncounter e, int index)
    {
        if (index < 0 || index >= e.Deaths.Count) return null;
        var d = e.Deaths[index];
        var row = e.Rows.FirstOrDefault(r => r.PartyId == d.PartyId && !r.IsLimitBreak);
        return Card(e.Id, index, d, row?.Job ?? string.Empty, row?.Role ?? string.Empty, row?.IsYou ?? false);
    }

    /// <summary>The card for a death, with the dying member's job, role and whether it's you (the encounter's row).</summary>
    public static RecapCard Card(int fightId, int index, DeathEntry d, string job, string role, bool you)
    {
        var killing = MeterView.KillingLine(d);
        var rows = new List<RecapRow>();
        for (var i = 0; i < d.Recap.Count; i++)
        {
            var l = d.Recap[i];
            var offset = l.At - d.At;
            // The last seconds, and the killing blow whenever it came.
            if (offset < -Seconds - 1e-9 && i != killing) continue;
            var kind = l.Amount < 0 ? RecapKind.Hit : l.Amount > 0 ? RecapKind.Heal : l.Note.StartsWith("Shield", StringComparison.Ordinal) ? RecapKind.Shield : RecapKind.Hit;
            var after = Math.Max(0, l.HpBefore + l.Amount);
            if (i == killing) after = 0;
            rows.Add(new RecapRow(Math.Min(0, offset), l.What, WhoOf(l), l.Amount, after, kind, i == killing));
        }
        var up = Mitigations.In(d.Mitigation);
        var tank = string.Equals(role, "tank", StringComparison.OrdinalIgnoreCase);
        // Up at the hit: the mitigations first, the barriers after; for a tank, their big cooldowns that weren't.
        var words = up.Where(m => m.Kind != MitigationRole.Shield).Select(m => m.Key).Concat(up.Where(m => m.Kind == MitigationRole.Shield).Select(m => m.Key)).ToList();
        var missing = tank && d.MitigationKnown ? Mitigations.MissingFor(job, d.Mitigation) : new List<string>();
        return new RecapCard(fightId, index, d.PartyId, d.Name, d.Slot, job, role, you, d.At, d.Phase, d.KilledBy, d.Amount, d.Overkill, d.Hit, rows, words,
            d.MitigationKnown, d.MitigationKnown && !Mitigations.AnyMitigation(d.Mitigation), missing, d.HadWeakness, MeterView.RecapMax(d));
    }

    /// <summary>A heal's healer ("Koko"), a barrier's ("Shield from Koko"); a hit's note ("Critical") isn't a who.</summary>
    private static string? WhoOf(RecapLine l)
    {
        if (l.Amount > 0) return l.Note.Length > 0 && l.Note != "Over time" ? l.Note.Replace(", over time", string.Empty, StringComparison.Ordinal) : null;
        if (l.Amount == 0 && l.Note.StartsWith("Shield from ", StringComparison.Ordinal)) return l.Note["Shield from ".Length..];
        return null;
    }

    /// <summary>When, under the name: "4:12 · P3" (just the clock in a fight of one phase).</summary>
    public static string When(RecapCard c) => c.Phase is { Length: > 0 } p ? $"{Format.Clock(c.At)} · {p}" : Format.Clock(c.At);

    /// <summary>What the killing blow was, in a sentence: "a tank buster", "a raid-wide"; null when it isn't known.</summary>
    public static string? HitWords(DeathHit hit) => hit switch
    {
        DeathHit.TankBuster => "a tank buster",
        DeathHit.RaidWide => "a raid-wide",
        _ => null,
    };

    /// <summary>The killing blow's kind in a stacked row's brackets: "buster", "raid-wide"; null when it isn't known.</summary>
    public static string? HitShort(DeathHit hit) => hit switch
    {
        DeathHit.TankBuster => "buster",
        DeathHit.RaidWide => "raid-wide",
        _ => null,
    };

    /// <summary>
    /// The killed-by sentence's parts: "Killed by " · the blow · " for " · the amount · " (41,200 more than they had)" and the rest
    /// (", a tank buster, with no mitigation up."). The window draws the blow and the amount in their own weight.
    /// </summary>
    public static (string Over, string Tail) KilledWords(RecapCard c)
    {
        var over = c.Overkill > 0 ? $" ({Format.Thousands(c.Overkill)} more than they had)" : string.Empty;
        var parts = new List<string>(2);
        if (HitWords(c.Hit) is { } hit) parts.Add(hit);
        if (c.NoMitigation) parts.Add("with no mitigation up");
        var tail = parts.Count == 0 ? "." : ", " + string.Join(", ", parts) + ".";
        return (over, tail);
    }

    /// <summary>A tank's big cooldowns that weren't up: "No Rampart, Guardian or Hallowed Ground" (null for none).</summary>
    public static string? MissingLine(IReadOnlyList<string> missing) => missing.Count switch
    {
        0 => null,
        1 => "No " + missing[0],
        _ => "No " + string.Join(", ", missing.Take(missing.Count - 1)) + " or " + missing[^1],
    };

    /// <summary>A stacked row's words after the name: "Latent Defect (buster)", "Hello World (raid-wide)".</summary>
    public static string RowWords(RecapCard c) => HitShort(c.Hit) is { } k ? $"{c.KilledBy} ({k})" : c.KilledBy;

    /// <summary>A line's time before the death: "−5.8 s", "0.0 s".</summary>
    public static string Offset(double offset)
    {
        var tenths = (long)Math.Round(Math.Max(0, -offset) * 10);
        var text = (tenths / 10).ToString(En) + "." + (tenths % 10).ToString(En) + " s";
        return tenths == 0 ? text : Format.Minus + text;
    }

    /// <summary>A line's amount: "−182,400", "+41,230"; a barrier's isn't known: "shield".</summary>
    public static string Amount(RecapRow r) => r.Kind == RecapKind.Shield && r.Amount == 0 ? "shield" : r.Amount == 0 ? string.Empty : Format.Signed(r.Amount);

    /// <summary>"Goes in 8 s" (the seconds rounded up).</summary>
    public static string GoesIn(double left) => $"Goes in {Math.Max(0, (int)Math.Ceiling(left - 1e-9)).ToString(En)} s";
}

/// <summary>
/// The deaths the death recap shows (since 0.9.0), newest first, as they're seen in the meter's encounter (its own deaths, read
/// a few times a second): yours only, or everyone's (Settings → Damage meter → Recap deaths of). The deaths an encounter had when
/// it was first seen aren't new (the plugin loaded mid-fight, or the recap was turned on): only those after. They go
/// <see cref="Lasts"/> seconds after the last one came, the time standing still while the mouse is over them (held) or while
/// they're pinned. Several at once (a wipe) are one-line rows; one pressed opens into the full card.
/// </summary>
public sealed class DeathRecapQueue
{
    /// <summary>How long they show after the last death came (seconds).</summary>
    public const double Lasts = 8;

    /// <summary>The most kept at once (the newest).</summary>
    public const int Most = 8;

    private readonly List<RecapCard> cards = new();
    private int fight = -1;
    private int seen;
    private bool started;
    private double last = double.NaN;

    /// <summary>The deaths shown, newest first.</summary>
    public IReadOnlyList<RecapCard> Cards => cards;

    /// <summary>Seconds left before they go (the time stands still while held or pinned).</summary>
    public double Left { get; private set; }

    /// <summary>Kept by the pin (mouse only) until it's pressed again.</summary>
    public bool Pinned { get; private set; }

    /// <summary>The stacked row opened into its card (its fight and index), or null: with one death, that one's card shows.</summary>
    public (int Fight, int Index)? Opened { get; private set; }

    /// <summary>The card shown in full: the only one, or the one opened.</summary>
    public RecapCard? Full => cards.Count == 1 ? cards[0] : Opened is { } o ? cards.FirstOrDefault(c => c.FightId == o.Fight && c.Index == o.Index) : null;

    /// <summary>
    /// The meter's encounter now (live, or the last: null before any): its deaths since the last look that are wanted (yours only,
    /// unless <paramref name="everyone"/>) come first in the list, and the time starts again.
    /// </summary>
    public void Feed(MeterEncounter? e, bool everyone)
    {
        if (e == null) return;
        if (!started)
        {
            // What was there before the first look isn't new.
            started = true;
            fight = e.Id;
            seen = e.Deaths.Count;
            return;
        }
        if (e.Id != fight)
        {
            // An older one shown again (the meter's picker never changes Current): only a newer encounter counts.
            if (e.Id < fight) return;
            fight = e.Id;
            seen = 0;
        }
        if (e.Deaths.Count <= seen) return;
        var added = false;
        for (var i = seen; i < e.Deaths.Count; i++)
        {
            if (DeathRecapView.Card(e, i) is not { } card || (!everyone && !card.IsYou)) continue;
            cards.Insert(0, card);
            added = true;
        }
        seen = e.Deaths.Count;
        if (!added) return;
        while (cards.Count > Most) cards.RemoveAt(cards.Count - 1);
        Left = Lasts;
    }

    /// <summary>
    /// Looks afresh, as at first (the recap was off or not looked at for a while): the encounter's deaths then aren't new. The
    /// cards shown go.
    /// </summary>
    public void Restart()
    {
        started = false;
        // Pinned cards stay (the UI hidden for a cutscene, say): only the pin lets them go.
        if (!Pinned) Clear();
    }

    /// <summary>Puts cards in place of the shown ones (a sample), shown until they go as any would.</summary>
    public void Show(IEnumerable<RecapCard> sample)
    {
        cards.Clear();
        cards.AddRange(sample);
        Opened = null;
        Left = Lasts;
    }

    /// <summary>
    /// A frame at <paramref name="now"/> (seconds, any steady clock): the time left runs down unless <paramref name="held"/> (the
    /// mouse over them) or pinned; at 0 they go.
    /// </summary>
    public void Tick(double now, bool held)
    {
        var dt = double.IsNaN(last) || now < last ? 0 : now - last;
        last = now;
        if (cards.Count == 0) return;
        if (held || Pinned) return;
        Left -= dt;
        if (Left <= 0) Clear();
    }

    /// <summary>The pin pressed: kept, or (unpinned) going again, a few seconds from now at least.</summary>
    public void TogglePin()
    {
        Pinned = !Pinned;
        if (!Pinned) Left = Math.Max(Left, 3);
    }

    /// <summary>A stacked row pressed: it opens into its card (another one open folds); pressed again, it folds.</summary>
    public void Toggle(RecapCard c) => Opened = Opened is { } o && o.Fight == c.FightId && o.Index == c.Index ? null : (c.FightId, c.Index);

    /// <summary>They go now (unpinned).</summary>
    public void Clear()
    {
        cards.Clear();
        Opened = null;
        Pinned = false;
        Left = 0;
    }
}

/// <summary>The death recap's sample (Settings, Arrange windows): made-up deaths of a made-up party, nothing real.</summary>
public static class DeathRecapSample
{
    /// <summary>The fight id the sample's deaths have (no real one is below 1): Open in the meter does nothing for them.</summary>
    public const int FightId = -1;

    /// <summary>A wipe's three deaths, newest first: a tank to a buster with nothing up, a healer to a cleave, a bard to a raid-wide.</summary>
    public static List<RecapCard> Cards()
    {
        var tank = new DeathEntry(1, "MT", "Velka Moor", 252, "Latent Defect", 182_400, 41_200, false, new[]
        {
            new RecapLine(246.2, "Auto-attack", -11_200, 141_200, string.Empty),
            new RecapLine(247.9, "Tetragrammaton", 41_230, 99_970, "Koko Nyan"),
            new RecapLine(249.4, "Auto-attack", -18_900, 141_200, string.Empty),
            new RecapLine(250.8, "Divine Benison", 0, 122_300, "Shield from Koko Nyan"),
            new RecapLine(252.0, "Latent Defect", -182_400, 141_200, string.Empty),
        }, 152_000, null, null, false, Mitigations.Mask("Divine Benison"), true, DeathHit.TankBuster, "P3");
        var healer = new DeathEntry(4, "H2", "Odette Vale", 257, "Latent Defect", 96_800, 12_400, false, new[]
        {
            new RecapLine(253.5, "Hello World", -38_620, 122_000, string.Empty),
            new RecapLine(255.1, "Physis II", 9_800, 83_380, "Odette Vale, over time"),
            new RecapLine(257.0, "Latent Defect", -96_800, 84_400, string.Empty),
        }, 122_000, null, null, true, Mitigations.Mask("Kerachole"), true, DeathHit.Unknown, "P3");
        var bard = new DeathEntry(7, "R1", "Fenn Hollow", 259, "Hello World", 104_880, 22_310, false, new[]
        {
            new RecapLine(255.6, "Hello World", -38_200, 120_770, string.Empty),
            new RecapLine(259.0, "Hello World", -104_880, 82_570, "Critical"),
        }, 121_000, null, null, false, Mitigations.Mask("Troubadour", "Reprisal"), true, DeathHit.RaidWide, "P3");
        return new List<RecapCard>
        {
            DeathRecapView.Card(FightId, 2, bard, "BRD", "dps", false),
            DeathRecapView.Card(FightId, 1, healer, "SGE", "healer", false),
            DeathRecapView.Card(FightId, 0, tank, "PLD", "tank", true),
        };
    }
}
