using System;

namespace RaidPlanner.Core.Meter;

/// <summary>What one of the party's action effects did (the recording's <c>combat.fx</c> kind, its low 3 bits).</summary>
public enum FxKind : byte
{
    Damage = 1,
    Heal = 2,

    /// <summary>A barrier put on (never healing; no amount: the game doesn't say how big).</summary>
    Shield = 3,

    /// <summary>A status put on, and nothing else.</summary>
    Status = 4,
}

/// <summary>An effect's flags (the recording's <c>combat.fx</c> <c>k</c>, above the kind).</summary>
[Flags]
public enum FxFlags : ushort
{
    None = 0,
    Crit = 8,
    DirectHit = 16,

    /// <summary>A pet's or summon's, credited to its owner.</summary>
    Pet = 32,

    LimitBreak = 64,

    /// <summary>Came from the target back at its attacker (Vengeance, Damnation): the party member it came from is credited.</summary>
    Reflected = 128,

    /// <summary>Landed on the caster (lifesteal, a self-applied status).</summary>
    Self = 256,
}

/// <summary>
/// One action going off, as the game's message had it, in plain values (the game side fills it in
/// place: no allocation per message). Each target's HP is read only when it's wanted
/// (<see cref="CombatParser.WantsHp"/>); −1 is unknown.
/// </summary>
public sealed class EffectBatch
{
    public const int MaxTargets = 32;
    public const int PerTarget = 8;

    public double T;
    public uint Caster;

    /// <summary>The caster's owner when it's a pet or a summon (0 for none).</summary>
    public uint CasterOwner;

    public long CasterHp = -1;
    public long CasterMaxHp = -1;
    public uint Action;

    /// <summary>The game's action type: 1 an action, 2 an item (a party member's is read since 0.4.0: a <see cref="PartyUse"/> only), and others.</summary>
    public byte ActionType;

    /// <summary>The header's global sequence (0 unknown): one action resolved in two messages has the same (since 0.4.0).</summary>
    public uint Sequence;

    /// <summary>Where a ground-targeted action was placed (the hook's target position), when <see cref="HasGround"/> (since 0.4.0).</summary>
    public bool HasGround;

    public float GroundX;
    public float GroundZ;

    public int Count;
    public readonly uint[] Targets = new uint[MaxTargets];
    public readonly long[] Hp = new long[MaxTargets];
    public readonly long[] MaxHp = new long[MaxTargets];

    /// <summary>The target is an enemy (hostile to the party).</summary>
    public readonly bool[] Hostile = new bool[MaxTargets];

    /// <summary>A party target's barrier as the game shows it, % of max HP (−1 unknown; read with HP, since 0.4.0).</summary>
    public readonly int[] Shield = new int[MaxTargets];

    /// <summary>A party target's place as the effect arrived (NaN unknown; read with HP, since 0.4.0).</summary>
    public readonly float[] X = new float[MaxTargets];

    public readonly float[] Z = new float[MaxTargets];

    public readonly RawEffect[] Effects = new RawEffect[MaxTargets * PerTarget];

    public void Begin(double t, uint caster, uint casterOwner, uint action, byte actionType)
    {
        T = t;
        Caster = caster;
        CasterOwner = casterOwner;
        Action = action;
        ActionType = actionType;
        CasterHp = -1;
        CasterMaxHp = -1;
        Count = 0;
        Sequence = 0;
        HasGround = false;
        GroundX = 0;
        GroundZ = 0;
    }

    /// <summary>A target, with its effects to fill in (<see cref="EffectsOf"/>); −1 when there's no room.</summary>
    public int Add(uint target)
    {
        if (Count >= MaxTargets) return -1;
        var i = Count++;
        Targets[i] = target;
        Hp[i] = -1;
        MaxHp[i] = -1;
        Hostile[i] = false;
        Shield[i] = -1;
        X[i] = float.NaN;
        Z[i] = float.NaN;
        Array.Clear(Effects, i * PerTarget, PerTarget);
        return i;
    }

    public Span<RawEffect> EffectsOf(int i) => Effects.AsSpan(i * PerTarget, PerTarget);
}

/// <summary>
/// One of the party's effects: <see cref="Member"/> is the party member credited (a pet's owner, the
/// tank whose Vengeance reflected it), <see cref="Caster"/> who actually used it. HP before and max
/// HP are the recipient's (−1 unknown): overheal is the heal beyond the HP missing then. Since 0.4.0 a
/// damage effect's <see cref="Bonus"/>: the battle log's bonus % (positional and combo bonuses), signed.
/// </summary>
public readonly record struct PartyEffect(
    double T,
    uint Action,
    uint Member,
    uint Caster,
    uint Recipient,
    FxKind Kind,
    FxFlags Flags,
    long Amount,
    long HpBefore,
    long MaxHp,
    uint Status,
    bool RecipientParty,
    bool RecipientHostile,
    sbyte Bonus = 0)
{
    /// <summary>A heal's overheal: what went past the HP the recipient was missing (0 when HP isn't known).</summary>
    public long Overheal => Kind == FxKind.Heal ? Over(Amount, HpBefore, MaxHp) : 0;

    public static long Over(long heal, long hpBefore, long maxHp) =>
        hpBefore < 0 || maxHp <= 0 ? 0 : Math.Clamp(heal - Math.Max(0, maxHp - hpBefore), 0, heal);
}

/// <summary>An enemy's action landing on a party member (damage, or an instant death): its amount, the hit's flags (<see cref="Effects"/> Fx…), the HP before.</summary>
public readonly record struct TakenHit(double T, uint Source, uint Action, uint Target, long Amount, int Fx, long HpBefore, long MaxHp);

/// <summary>
/// A damage- or heal-over-time tick (actor control 23): on whom, the source as the game said (a
/// pet's owner; 0 when it didn't say), the status (often 0), the amount, and the target's HP then.
/// </summary>
public readonly record struct TickEvent(
    double T,
    uint Target,
    uint Source,
    uint Status,
    long Amount,
    bool Heal,
    long HpBefore,
    long MaxHp,
    bool TargetParty,
    bool SourceParty)
{
    public long Overheal => Heal ? PartyEffect.Over(Amount, HpBefore, MaxHp) : 0;
}

/// <summary>Who a tick is credited to: the party member, their share, the status and the action that put it on (0 unknown). <see cref="Estimated"/> when the tick was shared out.</summary>
public readonly record struct TickShare(uint Member, long Amount, uint Status, uint Action, bool Estimated);

/// <summary>A <see cref="PartyUse"/>'s flags (the recording's <c>combat.use.f</c>).</summary>
[Flags]
public enum UseFlags : ushort
{
    None = 0,

    /// <summary>A pet's or summon's, credited to its owner.</summary>
    Pet = 1,

    /// <summary>An item (a potion): the action id is the item's as the game sent it (+1,000,000 for high quality).</summary>
    Item = 2,

    LimitBreak = 4,

    /// <summary>It interrupted a target's cast (effect type 76).</summary>
    Interrupted = 8,

    /// <summary>A target resisted a status it carries (effect type 55).</summary>
    StatusResisted = 16,

    /// <summary>It missed every target: each had only misses, full resists or invulnerability (types 1, 2, 7).</summary>
    Missed = 32,

    /// <summary>It had no target (a buff on no one, a ground action).</summary>
    NoTarget = 64,

    /// <summary>It put the caster at the top of a target's enmity (effect type 24: Provoke).</summary>
    Threat = 128,

    /// <summary>The healers' limit break 3 raising (effect type 50).</summary>
    Revive = 256,
}

/// <summary>
/// One of the party's actions (or items) used, whatever it did, auto-attacks left out (since 0.4.0):
/// when, the party member credited (a pet's owner) and who used it, the action (an item's id as sent,
/// <see cref="ActionType"/> 2), the first target (0 none) and how many it had, its flags, and, for a
/// ground-placed action, where.
/// </summary>
public readonly record struct PartyUse(double T, uint Member, uint Caster, uint Action, byte ActionType, uint Target, int Targets, UseFlags Flags,
    bool Ground, float X, float Z);

/// <summary>What the parser hands on (on the framework thread).</summary>
public interface ICombatSink
{
    /// <summary>A party member's action (or their pet's) went off, whatever it did: how a cast is known to have finished.</summary>
    void Used(double t, uint member, uint caster, uint action);

    /// <summary>A party member's action or item, as a row of what the party used (since 0.4.0; nothing by default).</summary>
    void Use(in PartyUse u)
    {
    }

    void Effect(in PartyEffect e);

    void Taken(in TakenHit e);

    /// <summary>A tick, with whom it's credited to (none: not the party's, or a bleed on the party).</summary>
    void Tick(in TickEvent e, ReadOnlySpan<TickShare> shares);
}
