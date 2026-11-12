using System;
using System.Collections.Generic;
using System.Diagnostics;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.UI.Model;

/// <summary>
/// A sample fight, to see the meter mid-fight without one (Settings → Meter's _Show a sample fight_,
/// the empty meter's _See a sample_, `/raidplan meter preview`): the mock-ups' pull 12 in Example
/// Arena, eight players, played from 2:15 with its clock running and its numbers moving as a pull's
/// do (four snapshots a second; bursts in the two-minute buffs, a raidwide every 24 seconds, each
/// player's damage rising and falling with their rotation), the limit break used at 0:58, P2 from
/// 2:30, pace against pull 4 from 10 seconds into each phase, Odette dying at 2:51 and Fenn at 3:22,
/// and the wipe at 3:31. It stays on the wipe for 8 seconds and plays again. Its earlier pulls (11,
/// 10, 9, and 4, the best) are kept, still, for the picker. Shown only when asked for, under the
/// meter's Preview tag, and never mixed with the real numbers (the windows read it instead of the
/// meter's own, never both); the plugin ends it when a real fight starts.
/// </summary>
public sealed class MeterPreview : IMeterSource
{
    /// <summary>Where it starts playing (seconds into the pull): 2:15, the limit break used and pace showing.</summary>
    public const double Begins = 135;

    /// <summary>The wipe: the pull's last death, its length.</summary>
    public const double Ends = 211;

    /// <summary>How long it stays on the wipe before it plays again.</summary>
    public const double Holds = 8;

    /// <summary>When P2 begins.</summary>
    public const double PhaseTwo = 150;

    /// <summary>When the limit break was used (LB2, Bladedance).</summary>
    public const double LimitBreakAt = 58;

    private const long LimitBreakDamage = 1_337_000;

    /// <summary>The pull the mock-ups' numbers are read at (3:11), so ENCDPS there is theirs.</summary>
    private const double Ref = 191;

    private const int LivePull = 12;

    /// <summary>A snapshot four times a second, as the meter's own.</summary>
    private const double Step = 0.25;

    private readonly Func<double> now;
    private readonly double start;
    private readonly List<MeterEncounter> still;
    private readonly List<MeterEncounter> kept;
    private MeterEncounter? live;
    private double liveAt = double.NaN;
    private (MeterEncounter? Of, int Member, MeterTab Tab, IReadOnlyList<AbilityRow> Rows) abilities = (null, 0, MeterTab.Damage, Array.Empty<AbilityRow>());
    private (MeterEncounter? Of, IReadOnlyList<MeterEvent> Events) events = (null, Array.Empty<MeterEvent>());
    private (double At, LimitBreakGauge? Gauge) gauge = (double.NaN, null);

    /// <summary>A sample from now; <paramref name="clock"/> is seconds on any steady clock (the tests' own; else the system's).</summary>
    public MeterPreview(Func<double>? clock = null)
    {
        now = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        start = now();
        still = new List<MeterEncounter>
        {
            Still(11, 252, -540),
            Still(10, 178, -960),
            Still(9, 232, -1380),
            Still(4, 255, -3600),
        };
        kept = new List<MeterEncounter>(still.Count + 1);
    }

    /// <summary>Where the sample is now: seconds into the pull (<see cref="Ends"/> while it stays on the wipe).</summary>
    public double At
    {
        get
        {
            var loop = Ends - Begins + Holds;
            var e = Math.Max(0, now() - start) % loop;
            var t = Begins + Math.Floor(e / Step) * Step;
            return Math.Min(t, Ends);
        }
    }

    public MeterEncounter? Current => Snapshot();

    public bool InEncounter => At < Ends;

    public bool Waiting => false;

    public string? Problem => null;

    public IReadOnlyList<MeterEncounter> Kept
    {
        get
        {
            Snapshot();
            return kept;
        }
    }

    public MeterEncounter? Get(int id)
    {
        if (id == LivePull) return Snapshot();
        foreach (var f in still)
            if (f.Id == id)
                return f;
        return null;
    }

    /// <summary>
    /// The sample's events (the Events tab), up to its second now: the tinctures at the pull and at
    /// 2:00, a stun on an add, the limit break at 0:58, an interrupt and a cast that went off, a tank
    /// swap and a moment the boss turned to a healer, the deaths and the wipe; an earlier pull's own
    /// (with Odette raised). Made once a snapshot.
    /// </summary>
    public IReadOnlyList<MeterEvent> Events(int encounterId)
    {
        if (Get(encounterId) is not { } e) return Array.Empty<MeterEvent>();
        if (ReferenceEquals(events.Of, e)) return events.Events;
        var list = new List<MeterEvent>();
        foreach (var ev in e.Live || e.Id == LivePull ? Script : StillScript)
            if (ev.At <= e.Duration)
                list.Add(ev);
        events = (e, list);
        return list;
    }

    /// <summary>
    /// The limit break gauge while the sample plays: three bars, filling 350 units a second, two spent
    /// on the limit break at 0:58; none once it's over.
    /// </summary>
    public LimitBreakGauge? LimitBreak
    {
        get
        {
            var t = At;
            if (t >= Ends) return null;
            if (gauge.At == t) return gauge.Gauge;
            var units = t < LimitBreakAt ? t * 350 : LimitBreakAt * 350 - 20000 + (t - LimitBreakAt) * 350;
            units = Math.Clamp(units, 0, 30000);
            var bars = Math.Min(3, (int)(units / 10000));
            gauge = (t, new LimitBreakGauge(bars, 3, bars >= 3 ? 0 : (int)(units - bars * 10000), 10000));
            return gauge.Gauge;
        }
    }

    private static MeterEvent Ev(double at, MeterEventKind kind, int id, string slot, string text, int other = 0, string otherSlot = "") =>
        new(at, kind, id, slot, text, other, otherSlot);

    /// <summary>The live pull's events, oldest first (names as the meter's own: they stay in the game).</summary>
    private static readonly MeterEvent[] Script =
    {
        Ev(0.6, MeterEventKind.Potion, 1, "M1", "Rook used a Grade 2 Gemdraught of Strength"),
        Ev(0.9, MeterEventKind.Potion, 3, "M2", "Tamsin used a Grade 2 Gemdraught of Dexterity"),
        Ev(1.2, MeterEventKind.Potion, 2, "R2", "Ilya used a Grade 2 Gemdraught of Intelligence"),
        Ev(46.2, MeterEventKind.Stun, 6, "MT", "Koko stunned Ice Sprite with Low Blow"),
        Ev(58.0, MeterEventKind.LimitBreak, 1, "M1", "Rook used Bladedance (limit break 2)"),
        Ev(84.4, MeterEventKind.Interrupt, 5, "OT", "Brann interrupted Ice Sprite's Dark Spike with Interject"),
        Ev(100.3, MeterEventKind.TankSwap, 5, "OT", "Example Boss turned to Brann from Koko (Provoke)", 6, "MT"),
        Ev(110.8, MeterEventKind.CastWentOff, 0, "", "Ice Sprite's Dark Spike went off: nobody interrupted it"),
        Ev(120.4, MeterEventKind.Potion, 1, "M1", "Rook used a Grade 2 Gemdraught of Strength"),
        Ev(120.9, MeterEventKind.Potion, 4, "R1", "Fenn used a Grade 2 Gemdraught of Dexterity"),
        Ev(126.1, MeterEventKind.AggroLost, 8, "H2", "Example Boss turned to Odette for 2 s"),
        Ev(171.3, MeterEventKind.Death, 8, "H2", "Odette died to Exaflare (104,880)"),
        Ev(202.0, MeterEventKind.Death, 4, "R1", "Fenn died to Line of Sight (246,300)"),
        Ev(209.4, MeterEventKind.Death, 6, "MT", "Koko died to Starfall: the wipe"),
    };

    /// <summary>An earlier pull's events: Odette raised by Sylvie.</summary>
    private static readonly MeterEvent[] StillScript =
    {
        Ev(0.8, MeterEventKind.Potion, 1, "M1", "Rook used a Grade 2 Gemdraught of Strength"),
        Ev(58.0, MeterEventKind.LimitBreak, 1, "M1", "Rook used Bladedance (limit break 2)"),
        Ev(84.4, MeterEventKind.Interrupt, 5, "OT", "Brann interrupted Ice Sprite's Dark Spike with Interject"),
        Ev(171.3, MeterEventKind.Death, 8, "H2", "Odette died to Exaflare (104,880)"),
        Ev(180.4, MeterEventKind.Raise, 8, "H2", "Odette was raised by Sylvie (Swiftcast Raise) after 0:09", 7, "H1"),
    };

    /// <summary>Pull 4: the one that got furthest.</summary>
    public MeterEncounter? Best(uint content) => still[^1];

    public void Clear()
    {
    }

    /// <summary>
    /// Pace against pull 4 (best) or 11 (last) from 10 seconds into each phase while the pull is on:
    /// the party's damage since the phase began, a few percent ahead, the lead moving.
    /// </summary>
    public PaceLine? Pace(int encounterId, PaceAgainst against)
    {
        if (against == PaceAgainst.Off || encounterId != LivePull) return null;
        var t = At;
        if (t >= Ends) return null;
        var phaseAt = t < PhaseTwo ? 0 : PhaseTwo;
        var into = t - phaseAt;
        if (into < 10) return null;
        var since = (long)Math.Round(PartyDamage(t) - PartyDamage(phaseAt));
        var ahead = 0.034 + 0.021 * Math.Sin(t / 7.0) + (against == PaceAgainst.Last ? 0.05 : 0);
        var other = (long)Math.Round(since / (1 + ahead));
        return new PaceLine(against == PaceAgainst.Last ? 11 : 4, against, t < PhaseTwo ? "P1" : "P2", into, since, other, ahead);
    }

    /// <summary>
    /// The picked player's damage by ability (the Damage tab; the others have none here): the mock-up's
    /// dragoon's, scaled to their damage now. Worked out once a snapshot and player.
    /// </summary>
    public IReadOnlyList<AbilityRow> Abilities(int encounterId, int partyId, MeterTab tab)
    {
        if (tab != MeterTab.Damage || Get(encounterId) is not { } e) return Array.Empty<AbilityRow>();
        if (ReferenceEquals(abilities.Of, e) && abilities.Member == partyId && abilities.Tab == tab) return abilities.Rows;
        long damage = 0;
        foreach (var r in e.Rows)
            if (r.PartyId == partyId && !r.IsLimitBreak)
                damage = r.Damage;
        var list = new List<AbilityRow>(Moves.Length);
        var scale = e.Duration / Ref;
        for (var i = 0; i < Moves.Length; i++)
        {
            var m = Moves[i];
            list.Add(new AbilityRow((uint)(100 + i), m.Name, m.Tick, false, (long)Math.Round(damage * m.Share), m.Share, Math.Max(1, (int)Math.Round(m.Hits * scale)),
                m.Crit, m.Dh, m.Max, false));
        }
        abilities = (e, partyId, tab, list);
        return list;
    }

    private static readonly (string Name, double Share, int Hits, double? Crit, double? Dh, long Max, bool Tick)[] Moves =
    {
        ("Drakesbane", 0.125, 15, 0.333, 0.467, 98610, false), ("Auto-attack", 0.116, 68, 0.265, 0.397, 21340, false),
        ("Raiden Thrust", 0.091, 15, 0.267, 0.400, 71880, false), ("Nastrond", 0.082, 6, 0.333, 0.500, 108930, false),
        ("Heavens' Thrust", 0.069, 8, 0.750, 0.500, 104270, false), ("Chaotic Spring", 0.053, 63, null, null, 6920, true),
        ("Lance Barrage", 0.051, 8, 0.250, 0.375, 69450, false), ("Fang and Claw", 0.051, 8, 0.250, 0.500, 74180, false),
        ("High Jump", 0.045, 6, 0.333, 0.500, 88560, false), ("Wheeling Thrust", 0.045, 7, 0.286, 0.429, 73920, false),
        ("Geirskogul", 0.038, 6, 0.333, 0.333, 61000, false), ("Stardiver", 0.034, 3, 0.333, 0.667, 98000, false),
    };

    // ---------------------------------------------------------------------------------------------
    // The pull.

    /// <summary>A player of the sample: their spot, name and job, and their numbers at 3:11 (the mock-ups').</summary>
    private sealed record Player(int Id, string Slot, string Name, string Job, string Role, long Damage, double Crit, double Dh, long MaxHit, string MaxHitName,
        double Hps, double Over, long Taken, long MaxTaken, double Period, double Died);

    /// <summary>The party in spot order (as the meter's own rows come), party ids as the mock-ups': Odette dies at 2:51, Fenn at 3:22, the rest in the wipe.</summary>
    private static readonly Player[] Party =
    {
        new(6, "MT", "Koko", "PLD", "tank", 2905110, 0.271, 0.338, 126310, "Blade of Honor", 2310, 0.48, 3912400, 104880, 19, 209.4),
        new(5, "OT", "Brann", "DRK", "tank", 3233630, 0.264, 0.352, 118940, "Disesteem", 1860, 0.41, 2486100, 98200, 17, 209.9),
        new(7, "H1", "Sylvie", "WHM", "healer", 2379860, 0.253, 0.316, 98470, "Afflatus Misery", 8940, 0.31, 1004200, 61200, 12, 211),
        new(8, "H2", "Odette", "SCH", "healer", 1728550, 0.248, 0.294, 61240, "Baneful Impaction", 4120, 0.22, 1324700, 104880, 14, 171.3),
        new(1, "M1", "Rook", "DRG", "dps", 5317440, 0.314, 0.442, 112460, "Stardiver", 410, 0.63, 1086900, 71300, 11, 210.3),
        new(3, "M2", "Tamsin", "NIN", "dps", 4988920, 0.306, 0.439, 158630, "Zesho Meppo", 380, 0.58, 1052600, 70800, 9.5, 210.6),
        new(4, "R1", "Fenn", "BRD", "dps", 4484680, 0.289, 0.461, 124380, "Radiant Encore", 240, 0.71, 1198300, 246300, 15, 202.0),
        new(2, "R2", "Ilya", "SMN", "dps", 5139810, 0.298, 0.417, 214830, "Akh Morn", 1150, 0.44, 1037800, 69400, 13, 210.8),
    };

    private static readonly DeathEntry[] Deaths = MakeDeaths();

    private static DeathEntry[] MakeDeaths()
    {
        var list = new List<DeathEntry>
        {
            new(8, "H2", "Odette", 171.3, "Exaflare", 104880, 18770, false, new List<RecapLine>
            {
                new(161.0, "Full HP", 0, 97210, "No shield on her"),
                new(165.2, "Exaflare", -38620, 97210, "Vulnerability Up from it, 1 stack"),
                new(166.4, "Embrace", 6120, 58590, "Eos, her fairy"),
                new(167.9, "Cure II", 21400, 64710, "Sylvie"),
                new(171.3, "Exaflare", -104880, 86110, "With Vulnerability Up: the killing blow"),
            }, 97210),
            new(4, "R1", "Fenn", 202.0, "Line of Sight", 246300, 161230, false, new List<RecapLine>
            {
                new(194.0, "Full HP", 0, 94470, "No shield on him"),
                new(197.1, "Exaflare", -21400, 94470, string.Empty),
                new(199.0, "Medica III", 12000, 73070, "Sylvie"),
                new(202.0, "Line of Sight", -246300, 85070, "Not behind a pillar: the killing blow"),
            }, 94470),
        };
        // The wipe: everyone left, within a second and a half of the last (not counted against anyone).
        foreach (var p in Party)
        {
            if (p.Died < 205) continue;
            var hp = p.Role == "tank" ? 126000 : 93000;
            list.Add(new DeathEntry(p.Id, p.Slot, p.Name, p.Died, "Starfall", 412000, 412000 - hp, true, new List<RecapLine>
            {
                new(p.Died, "Starfall", -412000, hp, "The enrage: the killing blow"),
            }, hp));
        }
        list.Sort((a, b) => a.At.CompareTo(b.At));
        return list.ToArray();
    }

    /// <summary>The sample at its second now: worked out again only for a new snapshot.</summary>
    private MeterEncounter Snapshot()
    {
        var t = At;
        if (live != null && t == liveAt) return live;
        liveAt = t;
        live = PullAt(t);
        kept.Clear();
        kept.Add(live);
        kept.AddRange(still);
        return live;
    }

    /// <summary>The pull at second <paramref name="t"/> (live until the wipe at <see cref="Ends"/>).</summary>
    public static MeterEncounter PullAt(double t)
    {
        t = Math.Clamp(t, 0, Ends);
        var clock = Math.Max(1, t);
        var lb = t >= LimitBreakAt;
        var party = (double)(lb ? LimitBreakDamage : 0);
        var healed = 0.0;
        var taken = 0.0;
        Span<double> dmg = stackalloc double[Party.Length];
        Span<double> heal = stackalloc double[Party.Length];
        Span<double> hit = stackalloc double[Party.Length];
        for (var i = 0; i < Party.Length; i++)
        {
            dmg[i] = DamageOf(Party[i], t);
            heal[i] = HealingOf(Party[i], t);
            hit[i] = TakenOf(Party[i], t);
            party += dmg[i];
            healed += heal[i];
            taken += hit[i];
        }
        var rows = new List<MeterRow>(Party.Length + 1);
        var counted = 0;
        for (var i = 0; i < Party.Length; i++)
        {
            var p = Party[i];
            var dead = p.Died <= t;
            var active = Math.Max(1, Math.Min(t, p.Died));
            var over = heal[i] * p.Over / (1 - p.Over);
            var deaths = dead && p.Died < 205 ? 1 : 0;
            counted += deaths;
            rows.Add(new MeterRow(p.Id, p.Slot, p.Name, p.Job, p.Role, p.Slot == "MT", false,
                (long)Math.Round(dmg[i]), dmg[i] / clock, dmg[i] / active, party <= 0 ? 0 : dmg[i] / party, p.Crit, p.Dh, p.MaxHit, p.MaxHitName,
                (long)Math.Round(heal[i]), (long)Math.Round(over), heal[i] / clock, p.Over, healed <= 0 ? 0 : heal[i] / healed, 0.2, p.Role == "healer" ? 31200 : 9800,
                (long)Math.Round(hit[i]), hit[i] / clock, taken <= 0 ? 0 : hit[i] / taken, p.MaxTaken, deaths, dead, dead ? p.Died : null, active)
            {
                // 0.4.0's: a GCD every 2.45 s while alive, the tinctures, Brann's interrupt, the time dead (the wipe's not counted).
                Casts = (int)(Math.Min(t, p.Died) / 2.45),
                Potions = PotionsBy(p.Id, t),
                Interrupts = p.Slot == "OT" && t >= 84.4 ? 1 : 0,
                DeadSeconds = deaths > 0 ? t - p.Died : 0,
            });
        }
        // The limit break's row is party id −1, as the meter's own (the party's total is 0).
        if (lb)
            rows.Add(new MeterRow(-1, "LB", "Limit Break", string.Empty, string.Empty, false, true, LimitBreakDamage, LimitBreakDamage / clock, LimitBreakDamage / clock,
                LimitBreakDamage / party, 0, 0, LimitBreakDamage, "Bladedance", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, null, 0));
        var overAll = 0.0;
        foreach (var r in rows) overAll += r.Overheal;
        var (casts, potions, interrupts, deadFor) = (0, 0, 0, 0.0);
        foreach (var r in rows)
        {
            casts += r.Casts;
            potions += r.Potions;
            interrupts += r.Interrupts;
            deadFor += r.DeadSeconds;
        }
        var total = new MeterRow(0, string.Empty, "Party", string.Empty, string.Empty, false, false, (long)Math.Round(party), party / clock, party / clock, 1, 0.28, 0.4,
            lb ? LimitBreakDamage : 214830, lb ? "Bladedance" : "Akh Morn", (long)Math.Round(healed), (long)Math.Round(overAll), healed / clock,
            healed + overAll <= 0 ? 0 : overAll / (healed + overAll), 1, 0.2, 31200, (long)Math.Round(taken), taken / clock, 1, 246300, counted, false, null, clock)
        {
            Casts = casts,
            Potions = potions,
            Interrupts = interrupts,
            DeadSeconds = deadFor,
        };
        var deathsNow = new List<DeathEntry>(Deaths.Length);
        foreach (var d in Deaths)
            if (d.At <= t)
                deathsNow.Add(d);
        var ended = t >= Ends;
        return new MeterEncounter(LivePull, LivePull, "Example Arena", 1, 0, t, !ended, ended ? Outcome.Wipe : null, t, t < PhaseTwo ? "P1" : "P2", t < PhaseTwo ? 0 : PhaseTwo,
            rows, total, deathsNow);
    }

    /// <summary>The tinctures a player had used by second <paramref name="t"/> (the script's).</summary>
    private static int PotionsBy(int id, double t)
    {
        var n = 0;
        foreach (var ev in Script)
            if (ev.Kind == MeterEventKind.Potion && ev.PartyId == id && ev.At <= t)
                n++;
        return n;
    }

    /// <summary>The party's damage by second <paramref name="t"/>, the limit break's included.</summary>
    private static double PartyDamage(double t)
    {
        var sum = t >= LimitBreakAt ? LimitBreakDamage : 0.0;
        foreach (var p in Party) sum += DamageOf(p, t);
        return sum;
    }

    /// <summary>
    /// A player's damage by second <paramref name="t"/>: their rate rising and falling with their
    /// rotation (±18% over their own period), 45% more in the buffs at the pull and at 2:00, and none
    /// once they're dead; at 3:11 (or their death, before it) it's the mock-ups'.
    /// </summary>
    private static double DamageOf(Player p, double t)
    {
        var until = Math.Min(t, p.Died);
        var at = Math.Min(Ref, p.Died);
        return p.Damage * Shape(p, until, 0.45) / Shape(p, at, 0.45);
    }

    /// <summary>Healing by second <paramref name="t"/>: steady, with more after each raidwide.</summary>
    private static double HealingOf(Player p, double t)
    {
        var until = Math.Min(t, p.Died);
        return p.Hps * Ref * (until + 4 * Raidwides(until)) / (Ref + 4 * Raidwides(Ref));
    }

    /// <summary>Damage taken by second <paramref name="t"/>: steady (the tanks' autos), and a step at each raidwide.</summary>
    private static double TakenOf(Player p, double t)
    {
        var until = Math.Min(t, p.Died);
        return p.Taken * (until + 9 * Raidwides(until)) / (Ref + 9 * Raidwides(Ref));
    }

    /// <summary>How many raidwides have gone off by second <paramref name="t"/>: one at 0:12, then every 24 seconds.</summary>
    private static int Raidwides(double t) => t < 12 ? 0 : (int)Math.Floor((t - 12) / 24) + 1;

    /// <summary>∫ rate: 1 ± 18% over the player's period, plus <paramref name="buff"/> more in the two buff windows (0–20 s, 2:00–2:20). Never falls.</summary>
    private static double Shape(Player p, double t, double buff)
    {
        if (t <= 0) return 0;
        var w = 2 * Math.PI / p.Period;
        var phase = p.Id * 0.9;
        var wobble = 0.18 * (Math.Cos(phase) - Math.Cos(w * t + phase)) / w;
        var inBuffs = Math.Clamp(t, 0, 20) + Math.Clamp(t - 120, 0, 20);
        return t + wobble + buff * inBuffs;
    }

    /// <summary>An earlier pull, kept and still (the picker's), with the mock-ups' numbers.</summary>
    private static MeterEncounter Still(int pull, double duration, double ago)
    {
        var rows = new List<MeterRow>(Party.Length + 1);
        long damage = LimitBreakDamage;
        foreach (var p in Party) damage += p.Damage;
        long taken = 0;
        foreach (var p in Party) taken += p.Taken;
        foreach (var p in Party)
        {
            var died = p.Slot == "H2";
            var active = died ? 168 : duration;
            rows.Add(new MeterRow(p.Id, p.Slot, p.Name, p.Job, p.Role, p.Slot == "MT", false, p.Damage, p.Damage / Ref, p.Damage / Math.Min(active, Ref),
                (double)p.Damage / damage, p.Crit, p.Dh, p.MaxHit, p.MaxHitName, (long)(p.Hps * duration), (long)(p.Hps * duration * p.Over / (1 - p.Over)), p.Hps, p.Over,
                p.Hps / 19410, 0.2, 40000, p.Taken, p.Taken / duration, (double)p.Taken / taken, p.MaxTaken, died ? 1 : 0, died, died ? 171 : null, active));
        }
        rows.Add(new MeterRow(-1, "LB", "Limit Break", string.Empty, string.Empty, false, true, LimitBreakDamage, 7000, 7000, (double)LimitBreakDamage / damage, 0, 0,
            LimitBreakDamage, "Bladedance", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, null, 0));
        var total = new MeterRow(0, string.Empty, "Party", string.Empty, string.Empty, false, false, damage, damage / Ref, damage / Ref, 1, 0.28, 0.4,
            LimitBreakDamage, "Bladedance", 3707310, 1200000, 19410, 0.3, 1, 0.2, 40000, taken, taken / duration, 1, 104880, 1, false, null, duration);
        // Odette was raised in the earlier pulls that lasted long enough (by Sylvie, H1, nine seconds after she died).
        var raised = duration > 181;
        var deaths = new List<DeathEntry> { raised ? Deaths[0] with { RaisedAt = 180.4, RaisedBy = "H1" } : Deaths[0] };
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.IsLimitBreak) continue;
            rows[i] = r with
            {
                Casts = (int)(duration / 2.45),
                Potions = r.Role == "dps" ? 2 : 0,
                Interrupts = r.Slot == "OT" ? 1 : 0,
                RaisesGiven = raised && r.Slot == "H1" ? 1 : 0,
                Raised = raised && r.Slot == "H2" ? 1 : 0,
                DeadSeconds = r.Slot != "H2" ? 0 : raised ? 9.1 : duration - 171.3,
                Dead = r.Dead && !raised,
            };
        }
        total = total with
        {
            Casts = (int)(duration / 2.45) * 8, Potions = 8, Interrupts = 1, RaisesGiven = raised ? 1 : 0, Raised = raised ? 1 : 0,
            DeadSeconds = raised ? 9.1 : duration - 171.3,
        };
        return new MeterEncounter(pull, pull, "Example Arena", 1, ago, duration, false, Outcome.Wipe, duration, "P2", 150, rows, total, deaths);
    }
}
