using System.Collections.Generic;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.Core;

/// <summary>
/// An enemy's hit as the recording takes it in (<c>Game/FightRecorder.OnHit</c>), in plain values so it's tested:
/// whom of the party it hit and what it did to each (<see cref="Collect"/>), and whether a hit from a caster the
/// recording hasn't met yet is kept at all (<see cref="TakeUnmet"/>).
/// </summary>
public static class HitIntake
{
    /// <summary>The game's battle NPC sub-kinds an enemy's caster is: a combatant, a part of one, and the invisible helpers.</summary>
    public const byte Combatant = 5;

    public const byte Part = 1;

    public const byte Helper = 11;

    /// <summary>The party members one message hit, with what it did to each, in step (lists kept and reused hit after hit).</summary>
    public sealed class Lists
    {
        public readonly List<int> Targets = new();
        public readonly List<long> Damage = new();
        public readonly List<int> Fx = new();
        public readonly List<long> Hp = new();
        public readonly List<int> Shield = new();
        public readonly List<float> X = new();
        public readonly List<float> Z = new();

        /// <summary>A party member took damage (or was killed outright): only an enemy does that.</summary>
        public bool Hurt;

        public void Clear()
        {
            Targets.Clear();
            Damage.Clear();
            Fx.Clear();
            Hp.Clear();
            Shield.Clear();
            X.Clear();
            Z.Clear();
            Hurt = false;
        }
    }

    /// <summary>
    /// Every party member in <paramref name="hit"/>'s targets (each by the entity id the message gave), with the damage, the
    /// hit's flags (<see cref="Effects.Taken"/>), their HP and barrier as it came and where they stood, into <paramref name="into"/>
    /// (cleared first). Anyone else in the list (an enemy, someone outside the party) is left out.
    /// </summary>
    public static void Collect(EffectBatch hit, RecordingBuilder b, Lists into)
    {
        into.Clear();
        for (var i = 0; i < hit.Count; i++)
        {
            if (b.PartyId(hit.Targets[i]) is not { } p) continue;
            var (dmg, flags) = Effects.Taken(hit.EffectsOf(i));
            into.Targets.Add(p);
            into.Damage.Add(dmg);
            into.Fx.Add(flags);
            into.Hp.Add(hit.Hp[i]);
            into.Shield.Add(hit.Shield[i]);
            into.X.Add(hit.X[i]);
            into.Z.Add(hit.Z[i]);
            if (dmg > 0 || (flags & Effects.FxInstantDeath) != 0) into.Hurt = true;
        }
    }

    /// <summary>
    /// Whether a hit from a caster the recording hasn't met (not in the frames yet: a helper come and gone between them, or
    /// one the game has no object for) is kept. <paramref name="hasObject"/>: the game had the caster's object; then
    /// <paramref name="battleNpc"/>, its <paramref name="subKind"/>, <paramref name="owned"/> (it has an owner: a pet, a
    /// summon, a chocobo) and <paramref name="nearYou"/>. A hit that did damage to a party member is an enemy's whatever the
    /// game says the caster is, so it's kept. Otherwise only a battle NPC enemy's (a combatant, a part, a helper; never an
    /// owned one: a pet's or a summon's heal is the party's own), and in a field zone (<paramref name="onlyEngaged"/>) one
    /// that hit none of the party only near you (else it's someone else's fight).
    /// </summary>
    public static bool TakeUnmet(bool hasObject, bool battleNpc, byte subKind, bool owned, bool nearYou, Lists hit, bool onlyEngaged)
    {
        if (hit.Hurt) return true;
        if (!hasObject || !battleNpc) return false;
        if (subKind is not (Combatant or Part or Helper)) return false;
        if (owned) return false;
        if (onlyEngaged && hit.Targets.Count == 0 && !nearYou) return false;
        return true;
    }
}
