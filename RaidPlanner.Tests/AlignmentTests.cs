using System.Text.Json.Nodes;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// A whole 0.3 pull for checking that the game's meter, the site's Review and its Progress tab read the
/// same numbers: the pull is played as the game plays it, through the plugin's own code (the combat
/// parser, its listeners the meter's session and the recording's sink, the recorder's builder and the
/// game's status lists read each frame for both), so its recording, its summary and the meter's numbers
/// after the fight all come from one stream of events. They're the site's fixtures
/// (<c>kokos-tools/src/features/raid-planner/model/__fixtures__/align.raidrec.json</c>,
/// <c>align.summary.json</c> and <c>align.meter.json</c>; with <c>RP_ALIGN_OUT</c> set to that folder,
/// this writes them), and the site's <c>alignment.spec.ts</c> checks that its numbers for the recording
/// (Review) and for the summary (Progress) are the meter's. Since 0.4.0 the pull also carries every new table, event
/// and column (uses before and during it, an interrupt credited and an interruptible cast that went off, a stun resisted
/// and one that landed, a provoke and a tank swap, a non-tank holding the boss, the BRD raised and dying again with
/// Weakness, potions, the countdown, the gauge, the enmity list, MP and barriers, signs, knockbacks, an absorbed buster,
/// bonus %, a party cast the game said was cancelled), and the meter's counts and events are checked against the summary's.
/// </summary>
public class AlignmentTests
{
    // The party in the party list's order (not the spots'), so a slot mask's bits (the summary's
    // `slots` order) aren't the spots' order: BLM R1, WHM H1, PLD MT, DRG M1, SCH H2, BRD R2, WAR OT, NIN M2.
    public const int Blm = 1, Whm = 2, Pld = 3, Drg = 4, Sch = 5, Brd = 6, War = 7, Nin = 8;

    /// <summary>The game's entity ids: the party's (21–28, in the party list's order), the enemies', the NIN's pet's.</summary>
    private const uint BossId = 900, AddId = 901, HelperId = 902, PetId = 950;

    private static uint Entity(int who) => (uint)(20 + who);

    private static readonly Dictionary<uint, ActionInfo> Actions = new()
    {
        // The enemies'.
        [100] = new ActionInfo("Raidwide Roar", 2, 60, 0, "", 5),
        [300] = new ActionInfo("Pillar Stack", 2, 6, 0, "", 0),
        [400] = new ActionInfo("Scatter", 2, 5, 0, "", 0),
        [500] = new ActionInfo("attack", 1, 0, 0, "", 0),
        [501] = new ActionInfo("Shot", 1, 25, 0, "", 0),
        [600] = new ActionInfo("Crushing Blow", 1, 0, 0, "", 5),
        [700] = new ActionInfo("Exaflare", 2, 6, 0, "general_1bf", 0),
        [800] = new ActionInfo("Spark", 1, 0, 0, "", 0),
        [1000] = new ActionInfo("Doom's End", 1, 0, 0, "", 0),
        [1100] = new ActionInfo("Ultimate End", 2, 60, 0, "", 8),
        // Since 0.4.0: the add's interruptible casts.
        [1200] = new ActionInfo("Stone Chant", 1, 0, 0, "", 4),
        [1201] = new ActionInfo("Quake Chant", 1, 0, 0, "", 3),
        // The party's (category, base recast; since 0.4.0 the cooldown group: 58 the GCD's).
        [9] = new ActionInfo("Fast Blade", 1, 3, 0, "", 0, 3, 2.5f, 58),
        [31] = new ActionInfo("Heavy Swing", 1, 3, 0, "", 0, 3, 2.5f, 58),
        [75] = new ActionInfo("True Thrust", 1, 3, 0, "", 0, 3, 2.5f, 58),
        [2240] = new ActionInfo("Spinning Edge", 1, 3, 0, "", 0, 3, 2.5f, 58),
        [25774] = new ActionInfo("Phantom Kamaitachi", 2, 3, 0, "", 0, 3, 0),
        [16495] = new ActionInfo("Burst Shot", 1, 25, 0, "", 0, 3, 2.5f, 58),
        [7406] = new ActionInfo("Caustic Bite", 1, 25, 0, "", 0, 3, 2.5f, 58),
        [7407] = new ActionInfo("Stormbite", 1, 25, 0, "", 0, 3, 2.5f, 58),
        [25865] = new ActionInfo("Broil IV", 1, 25, 0, "", 1.5f, 2, 2.5f, 58),
        [16540] = new ActionInfo("Biolysis", 1, 25, 0, "", 0, 2, 2.5f, 58),
        [185] = new ActionInfo("Adloquium", 1, 30, 0, "", 2, 2, 2.5f, 58),
        [190] = new ActionInfo("Physick", 1, 30, 0, "", 1.5f, 2, 2.5f, 58),
        [25859] = new ActionInfo("Glare III", 1, 25, 0, "", 1.5f, 2, 2.5f, 58),
        [16532] = new ActionInfo("Dia", 1, 25, 0, "", 0, 2, 2.5f, 58),
        [135] = new ActionInfo("Cure II", 1, 30, 0, "", 2, 2, 2.5f, 58),
        [137] = new ActionInfo("Regen", 1, 30, 0, "", 0, 2, 2.5f, 58),
        [3576] = new ActionInfo("Blizzard IV", 2, 25, 5, "", 2.5f, 2, 2.5f, 58),
        [3577] = new ActionInfo("Fire IV", 2, 25, 5, "", 2.8f, 2, 2.5f, 58),
        [36986] = new ActionInfo("High Thunder", 2, 25, 5, "", 0, 2, 2.5f, 58),
        [3557] = new ActionInfo("Battle Litany", 2, 0, 30, "", 0, 4, 120, 19),
        [205] = new ActionInfo("Meteor", 2, 25, 8, "", 4.5f, 9, 0),
        // Since 0.4.0: the roles' and the raise's.
        [7538] = new ActionInfo("Interject", 1, 3, 0, "", 0, 4, 30, 44),
        [7540] = new ActionInfo("Low Blow", 1, 3, 0, "", 0, 4, 25, 42),
        [7863] = new ActionInfo("Leg Sweep", 1, 3, 0, "", 0, 4, 40, 41),
        [7533] = new ActionInfo("Provoke", 1, 25, 0, "", 0, 4, 30, 43),
        [7548] = new ActionInfo("Arm's Length", 1, 0, 0, "", 0, 4, 120, 46),
        [7561] = new ActionInfo("Swiftcast", 1, 0, 0, "", 0, 4, 60, 44),
        [173] = new ActionInfo("Resurrection", 1, 30, 0, "", 8, 2, 2.5f, 58),
        [188] = new ActionInfo("Sacred Soil", 7, 30, 10, "", 0, 4, 30, 5, 0, 0, 0, true),
    };

    /// <summary>The items used (by the Item sheet's row): the BLM's in the pull (high quality), the PLD's before it.</summary>
    private static readonly Dictionary<uint, ItemInfo> Items = new()
    {
        [39727] = new ItemInfo("Grade 2 Gemdraught of Intelligence", 22457, false),
        [39726] = new ItemInfo("Grade 2 Gemdraught of Strength", 22456, false),
    };

    private static ItemInfo? Item(uint id) => Items.TryGetValue(id > 1_000_000 ? id - 1_000_000 : id, out var i) ? i : null;

    /// <summary>The statuses, by the Status sheet's ids and English names (the site's lists go by these ids).</summary>
    private static readonly Dictionary<uint, StatusInfo> Statuses = new()
    {
        [2912] = new StatusInfo("Vulnerability Up", 215532),
        [2911] = new StatusInfo("Damage Down", 215501),
        [1138] = new StatusInfo("Physical Vulnerability Up", 215532),
        [2088] = new StatusInfo("Bleeding", 215503),
        [1871] = new StatusInfo("Dia", 212675),
        [1200] = new StatusInfo("Caustic Bite", 212616),
        [1201] = new StatusInfo("Stormbite", 212617),
        [1895] = new StatusInfo("Biolysis", 212836),
        [3871] = new StatusInfo("High Thunder", 212681),
        [786] = new StatusInfo("Battle Litany", 212578),
        [158] = new StatusInfo("Regen", 212626),
        [297] = new StatusInfo("Galvanize", 212801),
        // Since 0.4.0.
        [148] = new StatusInfo("Raise", 210402, 1),
        [43] = new StatusInfo("Weakness", 215009, 2, true),
        [49] = new StatusInfo("Medicated", 216202, 1),
        [2] = new StatusInfo("Stun", 215004, 2),
        [1209] = new StatusInfo("Arm's Length", 210813, 1),
    };

    /// <summary>What's what (interrupts, raises, control, the GCDs), as the game side finds them by their English names.</summary>
    public static readonly CombatKinds Kinds = CombatKinds.FromNames(id => Actions.TryGetValue(id, out var a) ? a.Name : null,
        id => Statuses.TryGetValue(id, out var s) ? s.Name : null, id => Actions.TryGetValue(id, out var a) && CombatLists.IsGcd(a.CooldownGroup, a.CooldownGroup2));

    /// <summary>A status of the jobs' DoTs, as the game side finds them (by their English names: <see cref="CombatLists.DotNames"/>).</summary>
    public static bool IsDot(uint id) => Statuses.TryGetValue(id, out var s) && CombatLists.DotNames.Contains(s.Name);

    private static bool IsShield(uint id) => Statuses.TryGetValue(id, out var s) && CombatLists.ShieldNames.Contains(s.Name);

    private static readonly long[] MaxHp = { 0, 78_000, 80_000, 100_000, 85_000, 80_500, 79_000, 102_000, 84_000 };

    /// <summary>
    /// When each first died (none survived the enrage): BRD to a bleed (raised by the SCH at 60, then to the enrage with
    /// Weakness at 84.5, with the SCH), WHM to an instant death, NIN to an exaflare in the wipe, the rest to the enrage.
    /// </summary>
    private static readonly Dictionary<int, double> DiedAt = new()
    {
        [Brd] = 51.2, [Whm] = 60.2, [Nin] = 80.2, [Blm] = 84.2, [Pld] = 84.3, [Drg] = 84.4, [Sch] = 84.5, [War] = 84.6,
    };

    /// <summary>The BRD's raise (accepted) and second death.</summary>
    private const double BrdUp = 60.0, BrdDiesAgain = 84.5;

    private static bool Dead(int who, double t) => who == Brd ? (t >= DiedAt[Brd] && t < BrdUp) || t >= BrdDiesAgain : t >= DiedAt[who];

    /// <summary>The pull's start on the game's clock, and the countdown's zero (the pull began a quarter second before it).</summary>
    private const double Clock = 100, CountdownZero = Clock + 0.25;

    /// <summary>A status on someone for a while, declared every frame it's on (its stacks may change: a refresh).</summary>
    private sealed record On(int Who, uint Id, ulong From, StatusFrom Kind, int Source, double Start, double End, uint Action = 0, int Stacks = 0,
        double? StacksUpAt = null);

    /// <summary>The pull as the game plays it: its recording, and the meter's session after it (its encounter, the meter's numbers after the fight).</summary>
    public sealed record Played(Recording Recording, MeterSession Session, MeterEncounter Meter);

    // Effects as the game's messages carry them (an amount over 65,535 in Param3, flagged in Param4; since 0.4.0 a bonus % in
    // Param2 and partly absorbed in Param4).
    private static RawEffect Damage(long amount, bool crit = false, bool dh = false, sbyte bonus = 0, bool absorbed = false) =>
        new(Effects.Damage, (byte)((crit ? 0x20 : 0) | (dh ? 0x40 : 0)), 0, unchecked((byte)bonus), (byte)(amount >> 16),
            (byte)((amount > 0xFFFF ? 0x40 : 0) | (absorbed ? 0x04 : 0)), (ushort)(amount & 0xFFFF));

    private static readonly RawEffect Knockback = new(Effects.Knockback, 0, 0, 0, 0, 0, 5);
    private static readonly RawEffect Interrupted = new(Effects.Interrupt, 0, 0, 0, 0, 0, 0);
    private static readonly RawEffect Provoked = new(Effects.ThreatPosition, 0, 0, 0, 0, 0, 1);
    private static readonly RawEffect StunResisted = new(Effects.FullResistStatus, 0, 0, 0, 0, 0, 2);

    private static RawEffect Heal(long amount, bool crit = false) =>
        new(Effects.Heal, 0, (byte)(crit ? 0x20 : 0), 0, (byte)(amount >> 16), (byte)(amount > 0xFFFF ? 0x40 : 0), (ushort)(amount & 0xFFFF));

    private static RawEffect StatusOn(uint status, bool onCaster = false) => new(onCaster ? Effects.StatusToSource : Effects.StatusToTarget, 0, 0, 0, 0, 0, (ushort)status);

    private static readonly RawEffect Dodged = new(Effects.Miss, 0, 0, 0, 0, 0, 0);
    private static readonly RawEffect Invulnerable = new(Effects.Invulnerable, 0, 0, 0, 0, 0, 82);
    private static readonly RawEffect InstantDeath = new(Effects.InstantDeath, 0, 0, 0, 0, 0, 0);

    /// <summary>The pull's recording (<see cref="Play"/>).</summary>
    public static Recording Pull() => Play().Recording;

    /// <summary>
    /// The pull: a raidwide, a stack one short, an exaflare (one dodged it, one was invulnerable; the
    /// one it hit twice gets two stacks of Vulnerability Up), a buster on the MT (and its Physical
    /// Vulnerability Up, no failure), autos and a ranged auto, a single-target spark, spreads with one
    /// overlapping (Damage Down), the boss away for 12 s (a second phase), a bleed that kills the BRD,
    /// an instant death on the WHM, then the enrage: a wipe. The party's own fight: everyone's hits
    /// (crits, direct hits), the NIN's pet, the WAR on the add, DoTs (BRD's two, SCH's, WHM's, BLM's)
    /// with ticks credited, ticks that named no one shared out (one naming its status, one on the add
    /// with no DoT on it), heals with overheal, a HoT's ticks (one on an MT just hit, between two
    /// frames, and one that named no one), a shield, Battle Litany, casts (one cancelled), and the
    /// BLM's limit break. Played in time order: every frame (a tenth of a second) the party's HP and
    /// who's down, the enemies' targetability and the statuses on the party and the boss (the game's
    /// lists, read for the recording and the parser alike); between them the game's messages, read by
    /// the parser as the hooks hand them over (the enemies' hits also kept as the recorder keeps them).
    /// </summary>
    public static Played Play()
    {
        var b = new RecordingBuilder(new DutyInfo("Alignment Arena (Savage)", 1, 99998), 12, new DateTimeOffset(2026, 11, 10, 21, 0, 0, TimeSpan.Zero), true,
            "0.4.0", id => Actions.TryGetValue(id, out var a) ? a : null, id => Statuses.TryGetValue(id, out var s) ? s : null, Kinds, Item);
        var parser = new CombatParser(id => Actions.TryGetValue(id, out var a) ? a.Category : 0, IsShield, IsDot, id => Actions.TryGetValue(id, out var a) && a.Area);
        var session = new MeterSession(id => Actions.TryGetValue(id, out var a) ? a.Name : $"Action {id}",
            id => Statuses.TryGetValue(id, out var s) ? s.Name : $"Status {id}", id => Item(id)?.Name ?? $"Item {id}", Kinds);
        // The game's clock for the parser and the meter is the session's; the pull began 100 s into it (Clock).
        parser.Add(session);
        var party = new[]
        {
            new Partier(Entity(Blm), "BLM", 3, MaxHp[Blm]), new Partier(Entity(Whm), "WHM", 4, MaxHp[Whm]), new Partier(Entity(Pld), "PLD", 1, MaxHp[Pld]),
            new Partier(Entity(Drg), "DRG", 2, MaxHp[Drg]), new Partier(Entity(Sch), "SCH", 4, MaxHp[Sch]), new Partier(Entity(Brd), "BRD", 3, MaxHp[Brd]),
            new Partier(Entity(War), "WAR", 1, MaxHp[War]), new Partier(Entity(Nin), "NIN", 2, MaxHp[Nin]),
        };
        b.SetParty(party);
        // The BLM recorded it.
        b.SetSelf(Entity(Blm));
        parser.SetParty(party.Select(p => (uint)p.Key).ToArray());
        session.Place(1, 99998, "Alignment Arena (Savage)");

        // Before the pull (since 0.4.0), as the recorder keeps it out of combat: the countdown (10 s) and the party's uses
        // (the SCH's Sacred Soil at −3, the PLD's potion at −2.1), then the pull's start copies them in.
        var before = new PrePull();
        var countdown = new CountdownWatch();
        parser.Add(before);
        for (var t = CountdownZero - 9.98; t < Clock; t += 0.1) countdown.Frame(t, true, (float)(CountdownZero - t));
        var soil = new EffectBatch();
        soil.Begin(Clock - 3, Entity(Sch), 0, 188, 1);
        soil.HasGround = true;
        soil.GroundX = 104;
        soil.GroundZ = 110;
        parser.Action(soil);
        var tincture = new EffectBatch();
        tincture.Begin(Clock - 2.1, Entity(Pld), 0, 39726, 2);
        tincture.EffectsOf(tincture.Add(Entity(Pld)))[0] = StatusOn(49);
        parser.Action(tincture);
        parser.Remove(before);
        b.PrePull(before.Before(Clock).Select(u => (u.T, b.PartyId(u.Member) ?? 0, u.Action, u.Target == 0 ? 0 : b.Who(u.Target), (int)u.Flags)),
            countdown.At(Clock));
        parser.Add(new RecordingSink(() => b, () => Clock));
        session.RecordedPull(12);
        var boss = b.Actor(BossId, "The Twinning", 5, 2001);
        var add = b.Actor(AddId, "Iron Add", 2, 2002);
        var helper = b.Actor(HelperId, "", 0.5f, 2003);
        b.SeenTargetable(boss, 10_000_000);
        b.SeenTargetable(add, 1_000_000);
        var everyone = Enumerable.Range(1, 8).ToArray();
        (float, float) At(int who) => who == Drg || who == Sch ? (104 + (who == Sch ? 0.6f : 0), 110) : (100 + who * 3, 112);
        uint EntityOf(int id) => id == boss ? BossId : id == add ? AddId : id == helper ? HelperId : Entity(id);

        // The party's HP as the game reads it: full, but for the PLD (healed and hit), the WAR (hit, then
        // healed), the BRD (bled; raised with little) and the dead.
        long Hp(int who, double t)
        {
            if (Dead(who, t)) return 0;
            if (who == Brd) return t < 45 ? 79_000 : t < 48 ? 49_000 : t < 51 ? 19_000 : t < BrdUp ? 1 : 15_800;
            if (who == War) return t < 8 ? 102_000 : t < 9 ? 72_000 : 80_000;
            if (who != Pld) return MaxHp[who];
            return t < 8 ? 100_000 : t < 15 ? 70_000 : t < 18 ? 52_000 : t < 27.2 ? 100_000 : t < 28 ? 10_000 : t < 33 ? 5_000 : 60_000;
        }

        // What the game does, in time order (each moment's messages before its frame).
        var game = new List<(double T, int Frame, int Order, Action Do)>();
        void When(double t, Action what) => game.Add((t, 0, game.Count, what));

        // An enemy's action landing: the recorder's hit event (as FightRecorder.OnHit makes it) and the parser's read of it.
        void EnemyHit(double t, int source, uint action, float x, float z, float hitX, float hitZ, params (int Who, RawEffect[] Effects)[] on) => When(t, () =>
        {
            var batch = new EffectBatch();
            batch.Begin(Clock + t, EntityOf(source), 0, action, 1);
            var targets = new List<int>();
            var damage = new List<long>();
            var fx = new List<int>();
            var hp = new List<long>();
            var shield = new List<int>();
            var px = new List<float>();
            var pz = new List<float>();
            foreach (var (who, effects) in on)
            {
                var k = batch.Add(Entity(who));
                effects.CopyTo(batch.EffectsOf(k));
                batch.Hp[k] = Hp(who, t - 0.01);
                batch.MaxHp[k] = MaxHp[who];
                // Read with the HP (since 0.4.0): the barrier and where they stand.
                batch.Shield[k] = Barrier(who, t - 0.01);
                (batch.X[k], batch.Z[k]) = At(who);
                var (dmg, flags) = Effects.Taken(batch.EffectsOf(k));
                targets.Add(who);
                damage.Add(dmg);
                fx.Add(flags);
                hp.Add(batch.Hp[k]);
                shield.Add(batch.Shield[k]);
                px.Add(batch.X[k]);
                pz.Add(batch.Z[k]);
            }
            b.Hit(t, source, action, x, z, 0, hitX, hitZ, targets, damage, fx, hp, shield, px, pz);
            parser.Action(batch);
        });

        // One of the party's actions on one target (a pet's with its owner): the HP read only on the party, as the game side reads it.
        void PartyAction(double t, uint caster, uint action, uint target, params RawEffect[] effects) => When(t, () =>
        {
            var batch = new EffectBatch();
            batch.Begin(Clock + t, caster, caster == PetId ? Entity(Nin) : 0, action, 1);
            var k = batch.Add(target);
            effects.CopyTo(batch.EffectsOf(k));
            var who = (int)target - 20;
            var onParty = who is >= 1 and <= 8;
            batch.Hp[k] = onParty ? Hp(who, t - 0.01) : -1;
            batch.MaxHp[k] = onParty ? MaxHp[who] : -1;
            batch.Hostile[k] = !onParty;
            parser.Action(batch);
        });

        // A DoT's or HoT's tick as the hooks hand it over: the target's HP read for a heal or on the party.
        void Tick(double t, uint target, uint status, bool heal, long amount, uint source) => When(t, () =>
        {
            var who = (int)target - 20;
            var onParty = who is >= 1 and <= 8;
            parser.Tick(Clock + t, target, status, heal, amount, source, 0, heal || onParty ? Hp(who, t - 0.01) : -1, heal || onParty ? MaxHp[who] : -1);
        });

        // The PLD's barrier (the SCH's Galvanize, 26.1 until the buster takes it).
        int Barrier(int who, double t) => who == Pld && t >= 26.1 && t < 27.2 ? 15 : 0;

        // The enemies' mechanics. The raidwide knocks back everyone but the DRG (Arm's Length).
        When(3, () => b.Cast(3, boss, 100, 5, 100, 100, 0, 0, null, null));
        EnemyHit(8, boss, 100, 100, 100, 100, 100, everyone.Select(i => (i, i == Drg ? new[] { Damage(30_000) } : new[] { Damage(30_000), Knockback })).ToArray());
        var stack = new[] { Blm, Whm, Pld, Drg, Sch, Brd, War };
        EnemyHit(15, helper, 300, 90, 90, 104, 112, stack.Select(i => (i, new[] { Damage(18_000) })).ToArray());
        EnemyHit(20, helper, 700, 95, 112, 95, 112, (Blm, new[] { Damage(50_000) }), (Pld, new[] { Dodged }), (War, new[] { Invulnerable }));
        EnemyHit(21.1, helper, 700, 99, 112, 99, 112, (Blm, new[] { Damage(50_000) }));
        When(22, () => b.Cast(22, boss, 600, 5, 100, 100, 0, Pld, null, null));
        // The buster lands between two frames (27.0 read the MT full; 27.5 reads them low), partly into their barrier.
        EnemyHit(27.2, boss, 600, 100, 100, 109, 112, (Pld, new[] { Damage(90_000, crit: true, absorbed: true) }));
        EnemyHit(28, boss, 500, 100, 100, 109, 112, (Pld, new[] { Damage(5_000) }));
        EnemyHit(29, add, 501, 110, 100, 121, 112, (War, new[] { Damage(2_000) }));
        EnemyHit(32, helper, 800, 90, 90, 106, 112, (Whm, new[] { Damage(10_000) }));
        foreach (var who in everyone)
        {
            var (x, z) = At(who);
            // The DRG's spread took the SCH, standing in it, too.
            var on = who == Drg ? new[] { Drg, Sch } : new[] { who };
            EnemyHit(35, helper, 400, 90, 90, x, z, on.Select(i => (i, new[] { Damage(15_000) })).ToArray());
        }
        // The bleed's ticks on the BRD (the boss's), the last killing them.
        foreach (var t in new[] { 45.0, 48.0, 51.0 }) Tick(t, Entity(Brd), 2088, false, 30_000, BossId);
        EnemyHit(60, helper, 1000, 90, 90, 106, 112, (Whm, new[] { InstantDeath }));
        EnemyHit(80, helper, 700, 124, 112, 124, 112, (Nin, new[] { Damage(120_000) }));
        When(76, () => b.Cast(76, boss, 1100, 8, 100, 100, 0, 0, null, null));
        EnemyHit(84, boss, 1100, 100, 100, 100, 100, new[] { Blm, Pld, Drg, Sch, War, Brd }.Select(i => (i, new[] { Damage(999_999) })).ToArray());

        // Since 0.4.0. The add's interruptible casts: the WAR interrupts the first (Interject, carrying the interrupt
        // effect), the second goes off; the game's word goes to the recording and the meter alike.
        When(12, () =>
        {
            b.Cast(12, add, 1200, 4, 110, 100, 0, 0, null, null, true);
            session.EnemyCast(Clock + 12, AddId, 1200, 4, true);
        });
        PartyAction(13.9, Entity(War), 7538, AddId, Interrupted);
        When(14, () =>
        {
            b.Cancel(14, add, 1200, true);
            session.Cancelled(Clock + 14, AddId, 1200, true);
        });
        When(22, () =>
        {
            b.Cast(22, add, 1201, 3, 110, 100, 0, 0, null, null, true);
            session.EnemyCast(Clock + 22, AddId, 1201, 3, true);
        });
        // The WAR's Low Blow on the boss (immune to its stun); the DRG's Leg Sweep stuns the add; the DRG's Arm's Length.
        PartyAction(16, Entity(War), 7540, BossId, StunResisted);
        PartyAction(25, Entity(Drg), 7863, AddId, StatusOn(2));
        PartyAction(6.9, Entity(Drg), 7548, Entity(Drg), StatusOn(1209, onCaster: true));
        // The WAR's Provoke before taking the boss (27.5).
        PartyAction(27.4, Entity(War), 7533, BossId, Provoked);
        // The BLM's potion (high quality); the SCH's Sacred Soil on the ground.
        When(20, () =>
        {
            var pot = new EffectBatch();
            pot.Begin(Clock + 20, Entity(Blm), 0, 1_039_727, 2);
            pot.EffectsOf(pot.Add(Entity(Blm)))[0] = StatusOn(49);
            parser.Action(pot);
        });
        When(34, () =>
        {
            var area = new EffectBatch();
            area.Begin(Clock + 34, Entity(Sch), 0, 188, 1);
            area.HasGround = true;
            area.GroundX = 104.5f;
            area.GroundZ = 110.25f;
            parser.Action(area);
        });
        // The SCH raises the BRD: Swiftcast, Resurrection (its Raise status on them from 58.1), accepted at 60.
        PartyAction(57.5, Entity(Sch), 7561, Entity(Sch));
        PartyAction(58, Entity(Sch), 173, Entity(Brd), StatusOn(148));

        // The party's own fight.
        bool BossUp(double t) => t < 40 || t >= 52;
        for (var k = 0; k < 34; k++)
        {
            var t = 1 + k * 2.5;
            if (!BossUp(t) || t >= 84) continue;
            if (t < DiedAt[Pld]) PartyAction(t, Entity(Pld), 9, BossId, Damage(12_000 + k * 100, crit: k % 4 == 3, dh: k % 5 == 4));
            if (t < DiedAt[War]) PartyAction(t + 0.2, Entity(War), 31, t >= 11 && t < 30 ? AddId : BossId, Damage(10_000, crit: k % 3 == 2));
            // The DRG's with their bonus % (a positional's or a combo's, as the battle log shows it).
            if (t < DiedAt[Drg]) PartyAction(t + 0.4, Entity(Drg), 75, BossId, Damage(14_000, dh: k % 2 == 1, bonus: (sbyte)(k % 3 == 0 ? 22 : 0)));
            if (t < DiedAt[Nin]) PartyAction(t + 0.6, Entity(Nin), 2240, BossId, Damage(13_000));
            if (t < DiedAt[Brd]) PartyAction(t + 0.8, Entity(Brd), 16495, BossId, Damage(11_000, crit: k % 6 == 0, dh: k % 6 == 0));
            if (t < DiedAt[Sch]) PartyAction(t + 1.0, Entity(Sch), 25865, BossId, Damage(9_000));
            if (t < DiedAt[Whm]) PartyAction(t + 1.2, Entity(Whm), 25859, BossId, Damage(9_500));
            if (t < DiedAt[Blm] && t + 1.4 < 37) PartyAction(t + 1.4, Entity(Blm), 3577, BossId, Damage(20_000, crit: k % 2 == 0));
        }
        // The NIN's pet, credited to the NIN.
        PartyAction(28.3, PetId, 25774, BossId, Damage(18_000));
        PartyAction(33.3, PetId, 25774, BossId, Damage(18_000, crit: true));
        // DoTs going on (their hits with their statuses, and the SCH's with none), and their ticks the game credited.
        PartyAction(4, Entity(Whm), 16532, BossId, Damage(10_000), StatusOn(1871));
        PartyAction(5, Entity(Brd), 7406, BossId, Damage(15_000), StatusOn(1200));
        PartyAction(6, Entity(Brd), 7407, BossId, Damage(16_000), StatusOn(1201));
        PartyAction(6, Entity(Sch), 16540, BossId, StatusOn(1895));
        PartyAction(7, Entity(Blm), 36986, BossId, Damage(20_000, crit: true), StatusOn(3871));
        foreach (var t in new[] { 8.0, 11.0, 14.0, 17.0, 20.0 }) Tick(t, BossId, 1200, false, 3_000, Entity(Brd));
        foreach (var t in new[] { 8.5, 11.5, 14.5 }) Tick(t, BossId, 1201, false, 3_200, Entity(Brd));
        foreach (var t in new[] { 10.0, 16.0, 22.0 }) Tick(t, BossId, 1895, false, 2_800, Entity(Sch));
        foreach (var t in new[] { 7.5, 13.5 }) Tick(t, BossId, 1871, false, 2_600, Entity(Whm));
        // The BLM's High Thunder ticks after their last Fire IV (34.9): their active time runs to it.
        Tick(36.5, BossId, 3871, false, 4_100, Entity(Blm));
        // Ticks the game named no one for: shared between the party's DoTs on the target then.
        Tick(9.05, BossId, 0, false, 10_003, 0);
        Tick(12.05, BossId, 0, false, 8_000, 0);
        Tick(13.55, BossId, 1201, false, 3_333, 0);
        Tick(20.55, AddId, 0, false, 5_000, 0);
        Tick(37.55, BossId, 0, false, 6_001, 0);
        // Heals: the SCH's Physick on the WAR, the WHM's Cure II on the PLD (7,000 of it overheal),
        // Regen's ticks (full, full, then on an MT just hit by the buster: the last frame said full, the
        // game's HP at the tick says not; then on a low MT), one HoT tick that named no one; a shield.
        PartyAction(9, Entity(Sch), 190, Entity(War), Heal(8_000));
        PartyAction(18, Entity(Whm), 135, Entity(Pld), Heal(55_000, crit: true));
        foreach (var t in new[] { 21.0, 24.0, 27.3, 28.5 }) Tick(t, Entity(Pld), 158, true, 2_500, Entity(Whm));
        Tick(29.5, Entity(Pld), 0, true, 4_000, 0);
        PartyAction(26, Entity(Sch), 185, Entity(Pld), StatusOn(297));
        PartyAction(12, Entity(Drg), 3557, Entity(Drg), StatusOn(786, onCaster: true));
        // The BLM's casts (as the game's cast bars are read each frame: a Blizzard IV cut short by Fire IV's,
        // which goes off; Meteor), and the limit break.
        When(29, () => b.PartyCast(29, Blm, 3576, 2.5f));
        When(30.1, () => b.PartyCast(30.1, Blm, 3577, 2.8f));
        When(33.5, () => b.PartyCast(33.5, Blm, 205, 4.5f));
        // The game says the Blizzard IV was cancelled (since 0.4.0: its why).
        When(30.05, () => b.Cancel(30.05, Blm, 3576, false));
        PartyAction(38, Entity(Blm), 205, BossId, Damage(2_400_000));

        // Statuses: the enemies' on the party (status events) and the party's on anyone (combat rows).
        var statuses = new List<On>
        {
            new(Blm, 2912, HelperId, StatusFrom.Enemy, helper, 20.3, 50.3, Stacks: 1, StacksUpAt: 21.4),
            new(Sch, 2911, HelperId, StatusFrom.Enemy, helper, 35.3, 65.3, Stacks: 1),
            new(Pld, 1138, BossId, StatusFrom.Enemy, boss, 27.3, 57.3, Stacks: 1),
            new(Brd, 2088, BossId, StatusFrom.Enemy, boss, 43.5, 51.3),
            new(boss, 1871, Entity(Whm), StatusFrom.Party, Whm, 4.1, 34.0, 16532),
            new(boss, 1871, Entity(Whm), StatusFrom.Party, Whm, 34.0, 64.0, 16532),
            new(boss, 1200, Entity(Brd), StatusFrom.Party, Brd, 5.1, 50.1, 7406),
            new(boss, 1201, Entity(Brd), StatusFrom.Party, Brd, 6.1, 51.1, 7407),
            new(boss, 1895, Entity(Sch), StatusFrom.Party, Sch, 6.1, 36.0, 16540),
            new(boss, 3871, Entity(Blm), StatusFrom.Party, Blm, 7.1, 37.0, 36986),
            new(Pld, 158, Entity(Whm), StatusFrom.Party, Whm, 18.5, 36.5, 137),
            new(Pld, 297, Entity(Sch), StatusFrom.Party, Sch, 26.1, 56.1, 185),
        };
        foreach (var who in everyone) statuses.Add(new On(who, 786, Entity(Drg), StatusFrom.Party, Drg, 12.1, 32.1, 3557));
        // Since 0.4.0: the Raise on the BRD (the SCH's), their Weakness from no one once up, the BLM's Medicated, the DRG's Arm's Length.
        statuses.Add(new On(Brd, 148, Entity(Sch), StatusFrom.Party, Sch, 58.1, BrdUp, 173));
        statuses.Add(new On(Brd, 43, 0, StatusFrom.Enemy, 0, BrdUp, BrdDiesAgain));
        statuses.Add(new On(Blm, 49, Entity(Blm), StatusFrom.Party, Blm, 20.1, 50.1));
        statuses.Add(new On(Drg, 1209, Entity(Drg), StatusFrom.Party, Drg, 7.0, 17.0, 7548));

        // The boss's target (the PLD, the WAR from their Provoke, the WHM for 1.5 s), the add's (the WAR).
        int BossOn(double t) => t < 27.5 ? Pld : t >= 33 && t < 34.5 ? Whm : War;
        // The BLM's view of the boss's enmity list (its target), MP, the gauge.
        (int, int)[] HateOn(double t) => t < 27.5 ? new[] { (Pld, 100), (War, 60), (Blm, 35) } : new[] { (War, 100), (Pld, 92), (Blm, 40) };
        int Mp(int who, double t) => who == Whm ? Math.Max(2_000, 10_000 - 300 * (int)(t / 3)) : who == Blm ? Math.Max(0, 10_000 - 160 * (int)t) : 10_000;
        int Gauge(double t) => t < 38 ? (int)Math.Min(30_000, 1_000 + t * 800) : (int)Math.Min(30_000, (t - 38) * 500);

        for (var step = 0; step <= 950; step++)
        {
            var t = step / 10.0;
            var aSecond = step % 10 == 0;
            game.Add((t, 1, game.Count, () =>
            {
                // Everyone's place and HP (the builder keeps them twice a second).
                var at = new Dictionary<int, (float, float, float)> { [boss] = (100, 100, 0), [add] = (110, 100, 0), [helper] = (90, 90, 0) };
                var hp = new Dictionary<int, long>
                {
                    [boss] = 10_000_000 - (long)(Math.Min(t, 84.6) * 60_000), [add] = t < 10 ? 1_000_000 : Math.Max(0, 1_000_000 - (long)((t - 10) * 50_000)), [helper] = 1,
                };
                foreach (var who in everyone)
                {
                    var (x, z) = At(who);
                    at[who] = (x, z, 3.14f);
                    hp[who] = Hp(who, t);
                }
                b.Positions(t, at, hp);
                // The statuses on the party and the boss, the game's lists read for the recording and for the parser.
                b.BeginStatuses();
                parser.BeginStatuses();
                foreach (var who in everyone.Append(boss))
                {
                    b.StatusesOf(who);
                    parser.StatusesOf(EntityOf(who));
                }
                // The one (or the second) running at a time of each status on each.
                foreach (var s in statuses.Where(s => t >= s.Start && t < s.End).GroupBy(s => (s.Who, s.Id)).Select(g => g.Last()))
                {
                    var stacks = s.StacksUpAt is { } up && t >= up ? s.Stacks + 1 : s.Stacks;
                    var left = (float)(s.End - t);
                    var restarted = statuses.Any(o => o != s && o.Who == s.Who && o.Id == s.Id && o.End == s.Start && Math.Abs(t - s.Start) < 0.05);
                    if (restarted || !b.StatusKnown(t, s.Who, s.Id, s.From, stacks, left))
                        b.StatusGained(t, s.Who, s.Id, s.From, s.Kind, s.Source, stacks, left, s.Action);
                    if (s.Kind == StatusFrom.Party) parser.StatusOn(Clock + t, EntityOf(s.Who), s.Id, (uint)s.From);
                }
                b.EndStatuses(t);
                parser.EndStatuses(Clock + t);
                foreach (var who in everyone) b.Alive(t, who, Dead(who, t));
                b.Targetable(t, boss, BossUp(t));
                if (t >= 10) b.Targetable(t, add, t < 30);
                b.EndTargets(t);
                // Since 0.4.0, every frame: the enemies' targets, MP and barriers, the gauge; once a second the enmity list and the signs.
                if (BossUp(t)) b.Aggro(t, boss, BossOn(t));
                if (t >= 10 && t < 30) b.Aggro(t, add, War);
                foreach (var who in everyone) b.Resources(t, who, Mp(who, t), Barrier(who, t));
                b.LimitGauge(t, Gauge(t), 30_000);
                if (aSecond)
                {
                    if (BossUp(t)) b.Hate(t, boss, HateOn(t));
                    b.Signs(t, t >= 30 && t < 40 ? new Dictionary<int, int> { [0] = boss, [5] = Brd } : new Dictionary<int, int> { [0] = boss });
                }
                // The meter's frame: the party as the party list has it (Weakness on the BRD once raised), the enemies' targetability,
                // the gauge, and the boss's target.
                var states = everyone.Select(who => new PartyState(Entity(who), $"Player {who}", party[who - 1].Job, party[who - 1].Role, Hp(who, t),
                    MaxHp[who], Hp(who, t) > 0, who == Blm, (byte)(who == Brd && t >= BrdUp && t < BrdDiesAgain ? 1 : 0))).ToArray();
                session.Gauge(Gauge(t), 3, 10_000);
                session.Frame(Clock + t, states, true);
                session.Targetable(BossId, BossUp(t));
                if (t >= 10) session.Targetable(AddId, t < 30);
                session.EndTargets();
                if (BossUp(t)) session.Aggro(Clock + t, BossId, Entity(BossOn(t)));
            }));
        }

        foreach (var (_, _, _, what) in game.OrderBy(g => g.T).ThenBy(g => g.Frame).ThenBy(g => g.Order)) what();
        var recording = b.Finish(95, Outcome.Wipe);
        return new Played(recording, session, session.Current!);
    }

    /// <summary>The summary the plugin sends of it: the game side's lists (fail kinds by name, DoTs by name).</summary>
    public static JsonObject Summary(Recording r) => PullSummary.Build(r, 2, isDot: IsDot);

    [Fact]
    public void The_pull_is_what_it_says()
    {
        var r = Pull();
        Assert.Equal(new[] { "R1", "H1", "MT", "M1", "H2", "R2", "OT", "M2" }, r.Party.Select(p => p.Slot));
        Assert.Equal(new[] { 0.0, 52.0 }, PullStats.PhaseStarts(r));
        // The BRD to the bleed (a status), the WHM to the instant death, the rest to hits.
        // (The BRD's first; since 0.4.0 they die again in the wipe.)
        var deaths = r.Events.OfType<DeathEvent>().GroupBy(d => d.Who).ToDictionary(g => g.Key, g => g.First());
        Assert.True(deaths[Brd].ByStatus);
        Assert.Equal(2088u, deaths[Brd].By);
        Assert.Equal(1000u, deaths[Whm].By);
        Assert.Equal(1100u, deaths[War].By);
        // The DoT and the HoT going on, refreshed, and off.
        var dia = r.Combat!.Statuses.Where(s => s.Status == 1871).ToList();
        Assert.Equal(new[] { true, true, false }, dia.Select(s => s.Gain));
        Assert.Contains(r.Events.OfType<StatusEvent>(), s => s.Id == 2912 && s.Refresh && s.Stacks == 2);
        // Since 0.4.0. The BRD died twice: the bleed, then (raised, with Weakness) the enrage.
        Assert.Equal(new[] { 51.2, 84.5 }, r.Events.OfType<DeathEvent>().Where(d => d.Who == Brd).Select(d => d.T));
        var raise = r.Events.OfType<RaiseEvent>().Single();
        Assert.Equal((60.0, Brd, Sch, (double?)58.1, false), (raise.T, raise.Who, raise.By, raise.At, raise.Lb));
        // The add's first chant interrupted by the WAR's Interject; the second went off.
        var cancel = r.Events.OfType<CancelEvent>().Single();
        Assert.Equal((1200u, true, War, 7538u), (cancel.Action, cancel.Interrupted, cancel.By, cancel.With));
        Assert.Equal(2, r.Events.OfType<CastEvent>().Count(c => c.Interruptible));
        // The boss's target on change: the PLD, the WAR (Provoke), the WHM a moment, the WAR.
        Assert.Equal(new[] { Pld, War, Whm, War }, r.Events.OfType<AggroEvent>().Where(a => a.Actor == 101).Select(a => a.On));
        var c = r.Combat!;
        var uses = c.Uses;
        Assert.Contains(uses, u => u.Action == 7538 && u.Source == War && ((UseFlags)u.Flags & UseFlags.Interrupted) != 0);
        Assert.Contains(uses, u => u.Action == 7540 && ((UseFlags)u.Flags & UseFlags.StatusResisted) != 0);
        Assert.Contains(uses, u => u.Action == 7533 && ((UseFlags)u.Flags & UseFlags.Threat) != 0);
        Assert.Contains(uses, u => u.Action == 1_039_727 && u.Source == Blm && ((UseFlags)u.Flags & UseFlags.Item) != 0);
        Assert.Contains(uses, u => u.Action == 205 && ((UseFlags)u.Flags & UseFlags.LimitBreak) != 0);
        Assert.Contains(uses, u => u.Action == 25774 && u.Source == Nin && ((UseFlags)u.Flags & UseFlags.Pet) != 0);
        Assert.Equal(new AreaRow(34, Sch, 188, 104.5f, 110.25f), c.Areas.Single());
        // Before the pull: Sacred Soil at −3, the PLD's potion at −2.1; the countdown's zero a quarter second after the start.
        Assert.Equal(new[] { (-3.0, Sch, 188u), (-2.1, Pld, 39726u) }, c.Pre.Select(p => (Math.Round(p.T, 2), p.Source, p.Action)));
        Assert.Equal((10.0, -0.25), c.Countdown);
        Assert.Equal(1, c.Casts.Single(x => x.Action == 3576).Why);
        // The raidwide knocked back all but the DRG; the buster partly absorbed by the PLD's barrier (15 %).
        var raidwide = r.Events.OfType<HitEvent>().First(h => h.Action == 100);
        Assert.Equal(Effects.FxKnockback, raidwide.Fx![0]);
        Assert.Equal(0, raidwide.Fx[raidwide.Targets.ToList().IndexOf(Drg)]);
        var buster = r.Events.OfType<HitEvent>().Single(h => h.Action == 600);
        Assert.Equal(Effects.FxCrit | Effects.FxAbsorbed, buster.Fx![0]);
        Assert.Equal(15, buster.Shield![0]);
        Assert.Equal((109f, 112f), (buster.Px![0], buster.Pz![0]));
        Assert.Contains(c.Fx, f => f.Origin == Drg && f.Bonus == 22);
        Assert.NotEmpty(c.Hate);
        Assert.NotEmpty(c.Res);
        Assert.Equal(30_000, c.Gauge.Max(g => g.Value));
        Assert.True(r.Party.Single(p => p.Id == Blm).Self);
    }

    [Fact]
    public void Its_summary()
    {
        var r = Pull();
        var s = Summary(r);
        // The boss lost 60,000 a second until the wipe (84.6 s) of 10 M.
        Assert.Equal(49.24, s["boss_hp"]!.GetValue<double>(), 2);
        Assert.Equal(new[] { 0, 52 }, s["phases"]!.AsArray().Select(x => x!.GetValue<int>()));
        var stats = s["stats"]!.AsObject();
        var slots = stats["slots"]!.AsArray().Select(x => x!.AsObject()).ToList();
        int Bit(int who) => 1 << (who - 1);
        var slot = (int who) => slots[who - 1];
        // The exaflare hit the BLM twice (Vulnerability Up, two stacks); the PLD dodged it, the WAR was invulnerable.
        Assert.Equal(2, slot(Blm)["vuln"]!.GetValue<int>());
        Assert.Equal(1, slot(Sch)["dd"]!.GetValue<int>());
        Assert.Equal(0, slot(Pld)["vuln"]!.GetValue<int>());
        var hits = stats["hits"]!.AsArray().Select(h => h!.AsArray().Select(x => x!.GetValue<long>()).ToArray()).ToList();
        Assert.Equal(new long[] { 200, 700, Bit(Blm), 50_000 }, hits.First(h => h[1] == 700));
        // Autos, the ranged auto and the spark aren't mechanics; the buster on the MT is.
        Assert.DoesNotContain(hits, h => h[1] is 500 or 501 or 800 or 1000);
        Assert.Contains(hits, h => h[1] == 600 && h[2] == Bit(Pld));
        // The BRD's and the WHM's deaths count; the NIN's (80.2) and the enrage's (84.2–84.6) are the wipe.
        Assert.Equal(new[] { 0, 1, 0, 0, 0, 1, 0, 0 }, slots.Select(x => x["deaths"]!.GetValue<int>()));
        // Ticks that named no one: 10,003 shared by BLM, WHM, SCH and BRD (the remainder the first's),
        // 8,000 the same, 3,333 named Stormbite (the BRD's), 6,001 by WHM and BRD once SCH's and BLM's
        // were off; none for the add's.
        var sch = 9_000L * Enumerable.Range(0, 34).Count(k => 1 + k * 2.5 < 84 && (1 + k * 2.5 < 40 || 1 + k * 2.5 >= 52)) + 3 * 2_800 + 2_500 + 2_000;
        Assert.Equal(sch, slot(Sch)["dmg"]!.GetValue<long>());
        // Active time runs from the first damage to the last, ticks too: the BLM's last is High Thunder's at 36.5.
        Assert.Equal(36.5 - 2.4, slot(Blm)["active"]!.GetValue<double>(), 1);
        Assert.Equal(2_400_000, stats["lb"]!.GetValue<long>());
        // Cure II: 55,000 with 48,000 missing; Regen: 2,500 twice on a full PLD, then on the MT just hit by
        // the buster (the game's HP as it ticked, not the frame's before the buster) and on a low MT, landing.
        Assert.Equal(8_000, slot(Sch)["heal"]!.GetValue<long>());
        Assert.Equal(48_000 + 2 * 2_500, slot(Whm)["heal"]!.GetValue<long>());
        Assert.Equal(7_000 + 2 * 2_500, slot(Whm)["over"]!.GetValue<long>());
        // The fight's length: from the PLD's first hit (1.0) to the wipe's last death (84.6).
        Assert.Equal(83.6, stats["len"]!.GetValue<double>());
        // Since 0.4.0. The SCH raised the BRD (dead 51.2–60); the WHM dead from 60.2 to the fight's end (84.6); the BRD's
        // second death and the rest are the wipe's. The WAR's interrupt; potions in the pull and before it.
        Assert.Equal(1, slot(Sch)["raises"]!.GetValue<int>());
        Assert.Equal(1, slot(Brd)["raised"]!.GetValue<int>());
        Assert.Equal(8.8, slot(Brd)["dead"]!.GetValue<double>());
        Assert.Equal(24.4, slot(Whm)["dead"]!.GetValue<double>());
        Assert.False(slot(Pld).ContainsKey("dead"));
        Assert.Equal(1, slot(War)["ints"]!.GetValue<int>());
        Assert.Equal(1, slot(Blm)["pots"]!.GetValue<int>());
        Assert.Equal(1, slot(Pld)["pots"]!.GetValue<int>());
        Assert.True(slot(Blm)["self"]!.GetValue<bool>());
        Assert.True(slot(Pld)["gcds"]!.GetValue<int>() > 20);
        var ph = stats["ph"]!.AsArray().Select(x => x!.AsArray().Select(v => v!.GetValue<long>()).ToArray()).ToList();
        Assert.Equal(new long[] { 0, 52 }, ph.Select(x => x[0]));
        for (var i = 0; i < slots.Count; i++) Assert.Equal(slots[i]["dmg"]!.GetValue<long>(), ph.Sum(x => x[i + 1]));
        var extra = stats["x"]!.AsObject();
        Assert.Equal(new[] { 1, 2 }, extra["int"]!.AsArray().Select(v => v!.GetValue<int>()));
        Assert.Equal(1, extra["swaps"]!.GetValue<int>());
        Assert.Equal(new long[] { 380, 205 }, extra["lbUses"]!.AsArray().Single()!.AsArray().Select(v => v!.GetValue<long>()));
        Assert.Equal(-0.25, extra["cd"]!.GetValue<double>());
    }

    [Fact]
    public void A_hot_tick_is_measured_against_the_hp_read_as_it_ticked_else_the_last_frames()
    {
        var r = Pull();
        // The tick on the MT a moment after the buster: the game read 10,000 as it ticked; the last frame (27.0) had them full.
        var tick = r.Combat!.Ticks.Single(t => t.Heal && Math.Abs(t.T - 27.3) < 0.001);
        Assert.Equal(10_000, tick.HpBefore);
        Assert.Equal(48_000 + 2 * 2_500, PullStats.Totals(r, IsDot).Of(Whm).Healing);
        // A file from before the column (`h`) has none: the frames' HP, and that tick is all overheal.
        var old = new CombatLog();
        old.Fx.AddRange(r.Combat.Fx);
        old.Ticks.AddRange(r.Combat.Ticks.Select(t => t with { HpBefore = -1 }));
        old.Statuses.AddRange(r.Combat.Statuses);
        var before = PullStats.Totals(r.With(r.Frames, r.PartyPositions, old), IsDot).Of(Whm);
        Assert.Equal(48_000 + 2_500, before.Healing);
        Assert.Equal(7_000 + 3 * 2_500, before.Overheal);
    }

    [Fact]
    public void The_meter_after_the_fight_reads_as_the_summary()
    {
        var played = Play();
        var m = played.Meter;
        Assert.False(m.Live);
        Assert.Equal(Outcome.Wipe, m.Ended);
        var stats = Summary(played.Recording)["stats"]!.AsObject();
        // The same length (the summary's to the hundredth): the PLD's first hit to the wipe's last death.
        var len = stats["len"]!.GetValue<double>();
        Assert.Equal(83.6, m.Duration, 9);
        Assert.Equal(len, Math.Round(m.Duration, 2));
        var slots = stats["slots"]!.AsArray().Select(x => x!.AsObject()).ToList();
        Assert.Equal(slots.Select(x => x["slot"]!.GetValue<string>()).OrderBy(x => x), m.Rows.Where(r => !r.IsLimitBreak).Select(r => r.Slot).OrderBy(x => x));
        foreach (var s in slots)
        {
            var row = m.Rows.Single(r => r.Slot == s["slot"]!.GetValue<string>());
            Assert.Equal(s["job"]!.GetValue<string>(), row.Job);
            Assert.Equal(s["dmg"]!.GetValue<long>(), row.Damage);
            Assert.Equal(s["heal"]!.GetValue<long>(), row.Healing);
            Assert.Equal(s["over"]!.GetValue<long>(), row.Overheal);
            Assert.Equal(s["taken"]!.GetValue<long>(), row.Taken);
            Assert.Equal(s["deaths"]!.GetValue<int>(), row.Deaths);
            Assert.Equal(s["crit"]!.GetValue<double>(), Math.Round(row.CritRate, 3));
            Assert.Equal(s["dh"]!.GetValue<double>(), Math.Round(row.DirectHitRate, 3));
            Assert.Equal(s["active"]!.GetValue<double>(), Math.Round(row.ActiveSeconds, 1));
            // ENCDPS and HPS over that length, DPS over their own active time.
            Assert.Equal(row.Damage / len, row.EncDps, 6);
            Assert.Equal(row.Healing / len, row.Hps, 6);
            Assert.Equal(row.Damage / Math.Max(1, row.ActiveSeconds), row.Dps, 6);
            // Since 0.4.0: the same counts (the potions before the pull aren't the meter's: not in an encounter).
            int Count(string key) => s[key]?.GetValue<int>() ?? 0;
            var member = played.Recording.Party.Single(p => p.Slot == row.Slot).Id;
            var prePots = played.Recording.Combat!.Pre.Count(p => p.Source == member && ((UseFlags)p.Flags & UseFlags.Item) != 0);
            Assert.Equal(Count("gcds"), row.Casts);
            Assert.Equal(Count("pots"), row.Potions + prePots);
            Assert.Equal(Count("raises"), row.RaisesGiven);
            Assert.Equal(Count("raised"), row.Raised);
            Assert.Equal(Count("ints"), row.Interrupts);
            Assert.Equal(s["dead"]?.GetValue<double>() ?? 0, Math.Round(row.DeadSeconds, 1), 6);
        }
        var lb = m.Rows.Single(r => r.IsLimitBreak);
        Assert.Equal(stats["lb"]!.GetValue<long>(), lb.Damage);
        Assert.Equal(slots.Sum(x => x["dmg"]!.GetValue<long>()) + lb.Damage, m.Party.Damage);
        // The deaths: the BRD's and the WHM's counted, the rest the wipe's; killed by what the recording says.
        Assert.Equal(new[] { ("R2", false, "Bleeding"), ("H1", false, "Doom's End"), ("M2", true, "Exaflare"), ("R1", true, "Ultimate End"),
            ("MT", true, "Ultimate End"), ("M1", true, "Ultimate End"), ("H2", true, "Ultimate End"), ("R2", true, "Ultimate End"), ("OT", true, "Ultimate End") },
            m.Deaths.Select(d => (d.Slot, d.PartOfWipe, d.KilledBy)));
        // The BRD's first death: raised by the SCH (H2) 8.8 s later; the second with Weakness.
        Assert.Equal((59.0, "H2"), (m.Deaths[0].RaisedAt!.Value, m.Deaths[0].RaisedBy!));
        Assert.True(m.Deaths[7].HadWeakness);
        Assert.False(m.Deaths[0].HadWeakness);
        // The events: the interrupt (the WAR's), the Low Blow resisted, the potion, the Leg Sweep's stun, the chant that
        // went off (the same moment: its end), the tank swap (MT to OT), the WHM holding the boss, the limit break, the raise.
        var events = played.Session.Events(m.Id);
        Assert.Equal(new[]
        {
            MeterEventKind.Interrupt, MeterEventKind.Resisted, MeterEventKind.Potion, MeterEventKind.Stun, MeterEventKind.CastWentOff,
            MeterEventKind.TankSwap, MeterEventKind.AggroLost, MeterEventKind.LimitBreak, MeterEventKind.Death, MeterEventKind.Raise,
            MeterEventKind.Death, MeterEventKind.Death, MeterEventKind.Death, MeterEventKind.Death, MeterEventKind.Death, MeterEventKind.Death,
            MeterEventKind.Death, MeterEventKind.Death,
        }, events.Select(e => e.Kind));
        Assert.Equal(("OT", "MT"), (events[5].Slot, events[5].OtherSlot));
        Assert.Equal(("R2", "H2"), (events[9].Slot, events[9].OtherSlot));
    }

    /// <summary>
    /// The meter's numbers for an encounter as the site's alignment test reads them (<c>align.meter.json</c>):
    /// its length, and each spot's row (and the limit break's, and the party's) as the meter shows them after the fight.
    /// </summary>
    public static JsonObject MeterNumbers(MeterEncounter m, IReadOnlyList<MeterEvent>? events = null)
    {
        JsonObject Row(MeterRow r) => new()
        {
            ["slot"] = r.Slot,
            ["job"] = r.Job,
            ["damage"] = r.Damage,
            ["encDps"] = r.EncDps,
            ["dps"] = r.Dps,
            ["crit"] = r.CritRate,
            ["dh"] = r.DirectHitRate,
            ["active"] = r.ActiveSeconds,
            ["healing"] = r.Healing,
            ["overheal"] = r.Overheal,
            ["overhealRate"] = r.OverhealRate,
            ["hps"] = r.Hps,
            ["taken"] = r.Taken,
            ["takenPerSecond"] = r.TakenPerSecond,
            ["deaths"] = r.Deaths,
            // Since 0.4.0.
            ["casts"] = r.Casts,
            ["potions"] = r.Potions,
            ["raisesGiven"] = r.RaisesGiven,
            ["raised"] = r.Raised,
            ["deadSeconds"] = Math.Round(r.DeadSeconds, 2),
            ["interrupts"] = r.Interrupts,
        };
        var lb = m.Rows.FirstOrDefault(r => r.IsLimitBreak);
        return new JsonObject
        {
            ["pull"] = m.Pull,
            ["outcome"] = m.Ended is { } o ? Words.OutcomeKey(o) : null,
            ["duration"] = m.Duration,
            ["slots"] = new JsonArray(m.Rows.Where(r => !r.IsLimitBreak).Select(r => (JsonNode)Row(r)).ToArray()),
            ["lb"] = lb == null ? null : new JsonObject { ["damage"] = lb.Damage, ["encDps"] = lb.EncDps },
            ["party"] = Row(m.Party),
            ["deaths"] = new JsonArray(m.Deaths.Select(d => (JsonNode)new JsonObject
            {
                ["slot"] = d.Slot, ["at"] = d.At, ["by"] = d.KilledBy, ["wipe"] = d.PartOfWipe,
                ["raisedAt"] = d.RaisedAt, ["raisedBy"] = d.RaisedBy, ["weakness"] = d.HadWeakness,
            }).ToArray()),
            // Since 0.4.0: the events as the meter lists them (encounter time; no names).
            ["events"] = new JsonArray((events ?? Array.Empty<MeterEvent>()).Select(e => (JsonNode)new JsonObject
            {
                ["at"] = Math.Round(e.At, 2), ["kind"] = e.Kind.ToString(), ["slot"] = e.Slot, ["other"] = e.OtherSlot, ["action"] = e.Action,
            }).ToArray()),
        };
    }

    [Fact]
    public void Writes_the_sites_alignment_fixtures_when_asked()
    {
        var played = Play();
        var r = played.Recording;
        var json = r.ToJson();
        Assert.Equal(json, Recording.Parse(json).ToJson());
        var summary = Summary(r).ToJsonString();
        var meter = MeterNumbers(played.Meter, played.Session.Events(played.Meter.Id)).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var dir = Environment.GetEnvironmentVariable("RP_ALIGN_OUT");
        if (string.IsNullOrWhiteSpace(dir)) return;
        File.WriteAllText(Path.Combine(dir, "align.raidrec.json"), json);
        File.WriteAllText(Path.Combine(dir, "align.summary.json"), summary);
        File.WriteAllText(Path.Combine(dir, "align.meter.json"), meter + "\n");
    }
}
