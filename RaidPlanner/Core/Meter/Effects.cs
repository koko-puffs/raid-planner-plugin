using System;

namespace RaidPlanner.Core.Meter;

/// <summary>
/// One effect of an action on one target, as the game's message has it (FFXIVClientStructs'
/// <c>ActionEffectHandler.Effect</c>: eight bytes), handed over as plain values by the game side.
/// </summary>
public readonly record struct RawEffect(byte Type, byte Param0, byte Param1, byte Param2, byte Param3, byte Param4, ushort Value);

/// <summary>What an effect is, as the meter reads it.</summary>
public enum EffectKind : byte
{
    /// <summary>Nothing, or an effect the meter doesn't read (knockbacks, threat, VFX…).</summary>
    None,

    /// <summary>Damage (blocked and parried included).</summary>
    Damage,

    /// <summary>A heal (never MP).</summary>
    Heal,

    /// <summary>The target evaded it (the game's "miss").</summary>
    Miss,

    /// <summary>Fully resisted (a status resisted too).</summary>
    Resisted,

    /// <summary>The target couldn't be hurt (Hallowed Ground, Holmgang's floor, a transcendent player).</summary>
    Invulnerable,

    /// <summary>A status put on the target (<see cref="Decoded.Status"/>, its stacks or param in <see cref="Decoded.Param"/>).</summary>
    StatusOn,

    /// <summary>A status the target lost.</summary>
    StatusOff,

    /// <summary>MP gained or lost: never damage, never healing.</summary>
    Mp,

    /// <summary>The healer's limit break 3 raising the party.</summary>
    ReviveLb,

    /// <summary>Instant death.</summary>
    InstantDeath,
}

/// <summary>
/// An effect decoded: its kind, its amount (the value with its high byte when the game set it), crit
/// and direct hit (a heal's crit sits in another byte), blocked or parried, whether it came from the
/// target (reflected, as Vengeance's) or went to the caster (lifesteal, a self-applied status).
/// </summary>
public readonly record struct Decoded(
    EffectKind Kind,
    long Amount,
    bool Crit,
    bool DirectHit,
    bool Blocked,
    bool Parried,
    bool FromTarget,
    bool ToSource,
    uint Status,
    byte Param);

/// <summary>
/// The game's effect types and how they're read, from BossMod's annotated table (its
/// <c>ActionEffect.cs</c>) and cactbot's log guide; the numbers have been stable for years.
/// </summary>
public static class Effects
{
    public const byte Miss = 1;
    public const byte FullResist = 2;
    public const byte Damage = 3;
    public const byte Heal = 4;
    public const byte BlockedDamage = 5;
    public const byte ParriedDamage = 6;
    public const byte Invulnerable = 7;
    public const byte MpLoss = 10;
    public const byte MpGain = 11;
    public const byte StatusToTarget = 14;
    public const byte StatusToSource = 15;
    public const byte LoseStatusTarget = 17;
    public const byte LoseStatusSource = 18;
    public const byte ReviveLb = 50;

    /// <summary>Instant death (cactbot's 0x33; BossMod doesn't list it).</summary>
    public const byte InstantDeath = 51;

    public const byte FullResistStatus = 55;

    /// <summary>A damage effect's <c>Param4</c>: the target can't be hurt (0 damage).</summary>
    private const byte Immune = 0x10;

    /// <summary><c>Param4</c>, every effect: it comes from the target, not the caster (retaliation).</summary>
    private const byte FromTargetBit = 0x20;

    /// <summary><c>Param4</c>: the value has a third byte, in <c>Param3</c>.</summary>
    private const byte LargeValue = 0x40;

    /// <summary><c>Param4</c>, every effect: it lands on the caster, not the target (lifesteal, a self-status).</summary>
    private const byte ToSourceBit = 0x80;

    /// <summary>The amount of a damage or heal effect: the value, and the third byte when the game says it's there.</summary>
    public static long Amount(in RawEffect e) => e.Value + ((e.Param4 & LargeValue) != 0 ? (long)e.Param3 << 16 : 0);

    public static Decoded Decode(in RawEffect e)
    {
        var from = (e.Param4 & FromTargetBit) != 0;
        var to = (e.Param4 & ToSourceBit) != 0;
        switch (e.Type)
        {
            case Damage:
            case BlockedDamage:
            case ParriedDamage:
                // Immune: the game says so with this bit and a 0 value.
                if ((e.Param4 & Immune) != 0) return new Decoded(EffectKind.Invulnerable, 0, false, false, false, false, from, to, 0, 0);
                return new Decoded(EffectKind.Damage, Amount(e), (e.Param0 & 0x20) != 0, (e.Param0 & 0x40) != 0, e.Type == BlockedDamage,
                    e.Type == ParriedDamage, from, to, 0, 0);
            case Heal:
                // A heal's crit is Param1's 0x20 (Param0 holds lifedrain bits); heals never direct hit.
                return new Decoded(EffectKind.Heal, Amount(e), (e.Param1 & 0x20) != 0, false, false, false, from, to, 0, 0);
            case Miss:
                return new Decoded(EffectKind.Miss, 0, false, false, false, false, from, to, 0, 0);
            case FullResist:
            case FullResistStatus:
                return new Decoded(EffectKind.Resisted, 0, false, false, false, false, from, to, 0, 0);
            case Invulnerable:
                return new Decoded(EffectKind.Invulnerable, 0, false, false, false, false, from, to, e.Value, 0);
            case StatusToTarget:
            case StatusToSource:
                // Type 15 is a status on the caster whatever the bit says.
                return new Decoded(EffectKind.StatusOn, 0, false, false, false, false, from, to || e.Type == StatusToSource, e.Value, e.Param2);
            case LoseStatusTarget:
            case LoseStatusSource:
                return new Decoded(EffectKind.StatusOff, 0, false, false, false, false, from, to || e.Type == LoseStatusSource, e.Value, 0);
            case MpLoss:
            case MpGain:
                return new Decoded(EffectKind.Mp, e.Value, false, false, false, false, from, to, 0, 0);
            case ReviveLb:
                return new Decoded(EffectKind.ReviveLb, 0, false, false, false, false, from, to, 0, 0);
            case InstantDeath:
                return new Decoded(EffectKind.InstantDeath, 0, false, false, false, false, from, to, 0, 0);
            default:
                return default;
        }
    }

    /// <summary>A hit's flags in the recording (<c>hit.fx</c>, the site's contract §2.3): crit.</summary>
    public const int FxCrit = 1;

    public const int FxDirectHit = 2;
    public const int FxBlocked = 4;
    public const int FxParried = 8;

    /// <summary>Fully resisted (the game's "full resist", a status resisted too).</summary>
    public const int FxResisted = 16;

    public const int FxInvulnerable = 32;
    public const int FxInstantDeath = 64;

    /// <summary>The target evaded it (the game's "miss": its one kind of miss).</summary>
    public const int FxDodged = 128;

    /// <summary>
    /// What one target took from an enemy's action, from its effects: the damage it did (0 for
    /// none) and the hit's flags (<c>Fx…</c>). Effects that come from the target (retaliation) or go
    /// to the caster (lifesteal) aren't the target's.
    /// </summary>
    public static (long Damage, int Fx) Taken(ReadOnlySpan<RawEffect> effects)
    {
        long damage = 0;
        var fx = 0;
        foreach (var raw in effects)
        {
            if (raw.Type == 0) continue;
            var d = Decode(raw);
            if (d.FromTarget || d.ToSource) continue;
            switch (d.Kind)
            {
                case EffectKind.Damage:
                    damage += d.Amount;
                    if (d.Crit) fx |= FxCrit;
                    if (d.DirectHit) fx |= FxDirectHit;
                    if (d.Blocked) fx |= FxBlocked;
                    if (d.Parried) fx |= FxParried;
                    break;
                case EffectKind.Miss:
                    fx |= FxDodged;
                    break;
                case EffectKind.Resisted:
                    fx |= FxResisted;
                    break;
                case EffectKind.Invulnerable:
                    fx |= FxInvulnerable;
                    break;
                case EffectKind.InstantDeath:
                    fx |= FxInstantDeath;
                    break;
            }
        }
        return (damage, fx);
    }
}
