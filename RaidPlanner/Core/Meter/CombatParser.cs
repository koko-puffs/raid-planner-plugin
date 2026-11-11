using System;
using System.Collections.Generic;

namespace RaidPlanner.Core.Meter;

/// <summary>
/// The one combat parser: the game's action effects and DoT/HoT ticks, handed over as plain values,
/// turned into what the party did (<see cref="PartyEffect"/>), what enemies did to the party
/// (<see cref="TakenHit"/>) and ticks with whom they're credited to (<see cref="TickShare"/>), for
/// every listener at once: the meter's session and, while a pull is recorded, the recording's
/// combat block. Game-free and allocation-free per message, so it's tested and cheap.
///
/// The rules (plugin-data.md §3, from BossMod's and cactbot's notes):
/// - Each effect comes from the caster, or from the target when it says so (retaliation:
///   Vengeance's damage is the tank's); and lands on the target, or on the caster when it says so
///   (lifesteal, a self-applied status).
/// - A pet's or summon's action is its owner's (the owner comes from the game), flagged as a pet's.
/// - Limit breaks (ActionCategory 9 and 15) are flagged; the meter gives them a row of their own.
/// - Damage is types 3, 5 and 6; heals type 4 (its crit in another byte); MP is never healing;
///   a barrier (a shield status) is never healing either.
/// - Damage between party members, and anything between two others, isn't kept.
/// - A tick is credited to the party member the game names (a pet's owner); when it names no one, it's
///   shared out between the party's DoTs known to be on the target, and marked estimated.
/// - Nonsense (a single non-limit-break hit or heal over <see cref="Absurd"/>) is dropped and counted
///   in <see cref="Anomalies"/>: the game has moved something the parser reads. A heal landing on an
///   enemy is dropped (never the party's healing) but isn't nonsense: an enemy that absorbs an element
///   (as some of Eureka's do) is healed by the party's attacks.
/// </summary>
public sealed class CombatParser
{
    /// <summary>How long a DoT put on an enemy is remembered for sharing out a tick that names no one (seconds).</summary>
    public const double DotMemory = 60;

    /// <summary>A single hit bigger than this that isn't a limit break can't be right.</summary>
    public const long Absurd = 5_000_000;

    private const int MostParty = 24;

    private readonly Func<uint, int> category;
    private readonly Func<uint, bool> isShield;
    private readonly Func<uint, bool> isDot;
    private readonly List<ICombatSink> sinks = new();
    private readonly uint[] party = new uint[MostParty];
    private int partyCount;

    /// <summary>Statuses the party put on anyone with an action: (on, status, party member) → the action and when.</summary>
    private readonly Dictionary<(uint On, uint Status, uint Member), (uint Action, double At)> applied = new();

    private readonly List<(uint, uint, uint)> stale = new();
    private readonly TickShare[] shares = new TickShare[MostParty];
    private readonly uint[] sharers = new uint[MostParty];

    /// <param name="category">An action's ActionCategory row (limit breaks are 9 and 15).</param>
    /// <param name="isShield">Is a status a barrier (<see cref="CombatLists.ShieldNames"/>)?</param>
    /// <param name="isDot">Is a status one of the jobs' DoTs (<see cref="CombatLists.DotNames"/>)?</param>
    public CombatParser(Func<uint, int>? category = null, Func<uint, bool>? isShield = null, Func<uint, bool>? isDot = null)
    {
        this.category = category ?? (_ => 0);
        this.isShield = isShield ?? (_ => false);
        this.isDot = isDot ?? (_ => false);
    }

    /// <summary>Effects that made no sense and were dropped (see the class's notes).</summary>
    public int Anomalies { get; private set; }

    /// <summary>Someone wants what it finds (the meter, a recording).</summary>
    public bool Listening => sinks.Count > 0;

    public void Add(ICombatSink sink)
    {
        if (!sinks.Contains(sink)) sinks.Add(sink);
    }

    public void Remove(ICombatSink sink) => sinks.Remove(sink);

    /// <summary>The party now, by the game's entity ids (just you when you're alone). Cheap to call every frame.</summary>
    public void SetParty(ReadOnlySpan<uint> ids)
    {
        partyCount = 0;
        foreach (var id in ids)
            if (id != 0 && partyCount < MostParty)
                party[partyCount++] = id;
    }

    public bool IsParty(uint id)
    {
        if (id == 0) return false;
        for (var i = 0; i < partyCount; i++)
            if (party[i] == id)
                return true;
        return false;
    }

    /// <summary>The party member an actor stands for: itself, or its owner (a pet, a summon); 0 for none.</summary>
    public uint MemberOf(uint actor, uint owner) => IsParty(actor) ? actor : owner != 0 && IsParty(owner) ? owner : 0;

    /// <summary>
    /// Does the game side need to read this target's HP and whether it's an enemy: a party member's
    /// (hits and heals on them), or anyone the action heals (overheal; a heal on an enemy isn't the party's healing).
    /// </summary>
    public bool WantsTarget(uint target, ReadOnlySpan<RawEffect> effects)
    {
        if (IsParty(target)) return true;
        foreach (var e in effects)
            if (e.Type == Effects.Heal && (e.Param4 & 0x80) == 0)
                return true;
        return false;
    }

    /// <summary>Does the game side need the caster's HP (a heal that lands on the caster: lifesteal)?</summary>
    public static bool WantsCaster(ReadOnlySpan<RawEffect> effects)
    {
        foreach (var e in effects)
            if (e.Type == Effects.Heal && (e.Param4 & 0x80) != 0)
                return true;
        return false;
    }

    /// <summary>
    /// Is an action worth reading at all: the party's (or a party pet's), or one landing on the party?
    /// Most messages (other players' in a city, enemies hitting each other) stop here.
    /// </summary>
    public bool Concerns(uint caster, uint owner, ReadOnlySpan<uint> targets)
    {
        if (sinks.Count == 0) return false;
        if (MemberOf(caster, owner) != 0) return true;
        foreach (var t in targets)
            if (IsParty(t))
                return true;
        return false;
    }

    /// <summary>The action that put a status on someone (0 when not known): what a DoT's ticks are named after.</summary>
    public uint ActionFor(uint on, uint status, uint member) => applied.TryGetValue((on, status, member), out var a) ? a.Action : 0;

    /// <summary>An action went off (only actions, type 1, are read).</summary>
    public void Action(EffectBatch b)
    {
        if (sinks.Count == 0 || b.ActionType != 1 || b.Action == 0) return;
        var member = MemberOf(b.Caster, b.CasterOwner);
        var pet = member != 0 && member != b.Caster;
        var lb = member != 0 && CombatLists.IsLimitBreak(category(b.Action));
        if (member != 0)
            foreach (var s in sinks)
                s.Used(b.T, member, b.Caster, b.Action);
        for (var i = 0; i < b.Count; i++)
        {
            var target = b.Targets[i];
            var targetParty = IsParty(target);
            // Neither side is the party: nothing for it.
            if (member == 0 && !targetParty) continue;
            var effects = b.EffectsOf(i);
            // A status that came with damage or a heal on the same target isn't a row of its own ("status applied only").
            var withAmount = false;
            foreach (var e in effects)
                if (e.Type is Effects.Damage or Effects.Heal or Effects.BlockedDamage or Effects.ParriedDamage)
                    withAmount = true;
            for (var j = 0; j < effects.Length; j++)
            {
                if (effects[j].Type == 0) continue;
                var d = Effects.Decode(effects[j]);
                if (d.Kind == EffectKind.None) continue;
                var origin = d.FromTarget ? target : b.Caster;
                var recipient = d.ToSource ? b.Caster : target;
                var onTarget = recipient == target;
                var hp = onTarget ? b.Hp[i] : b.CasterHp;
                var max = onTarget ? b.MaxHp[i] : b.CasterMaxHp;
                var recipientParty = IsParty(recipient);
                // The caster is an enemy unless it's the party's; a target is what the game side said.
                var recipientHostile = onTarget ? b.Hostile[i] : member == 0;
                // The party member it's from: the caster's member, or the target when it came from them.
                var from = d.FromTarget ? (targetParty ? target : 0) : member;
                if (from != 0) FromParty(b, d, from, origin, recipient, recipientParty, recipientHostile, hp, max, pet && !d.FromTarget, lb && !d.FromTarget, withAmount);
                else if (recipientParty) OnParty(b, d, origin, recipient, hp, max);
            }
        }
    }

    private void FromParty(EffectBatch b, in Decoded d, uint from, uint origin, uint recipient, bool recipientParty, bool recipientHostile,
        long hp, long max, bool pet, bool lb, bool withAmount)
    {
        var flags = FxFlags.None;
        if (pet) flags |= FxFlags.Pet;
        if (lb) flags |= FxFlags.LimitBreak;
        if (d.FromTarget) flags |= FxFlags.Reflected;
        else if (d.ToSource) flags |= FxFlags.Self;
        switch (d.Kind)
        {
            case EffectKind.Damage:
                // Between party members (a self-inflicted cost): not the party's damage.
                if (recipientParty) return;
                if (!lb && d.Amount > Absurd)
                {
                    Anomalies++;
                    return;
                }
                if (d.Crit) flags |= FxFlags.Crit;
                if (d.DirectHit) flags |= FxFlags.DirectHit;
                Emit(new PartyEffect(b.T, b.Action, from, origin, recipient, FxKind.Damage, flags, d.Amount, -1, -1, 0, false, recipientHostile));
                return;
            case EffectKind.Heal:
                // A heal that big can't be right (as a hit that big).
                if (!lb && d.Amount > Absurd)
                {
                    Anomalies++;
                    return;
                }
                // On an enemy it's never the party's healing, but it isn't nonsense either: an enemy
                // that absorbs an element (as some of Eureka's do) is healed by the party's attacks.
                if (recipientHostile && !recipientParty) return;
                if (d.Crit) flags |= FxFlags.Crit;
                Emit(new PartyEffect(b.T, b.Action, from, origin, recipient, FxKind.Heal, flags, d.Amount, hp, max, 0, recipientParty, false));
                return;
            case EffectKind.StatusOn:
                if (d.Status == 0) return;
                // Remembered on anyone (the action a buff, a DoT or a HoT came from; a DoT's for sharing out a tick).
                applied[(recipient, d.Status, from)] = (b.Action, b.T);
                if (applied.Count > 512) Prune(b.T);
                var kind = isShield(d.Status) ? FxKind.Shield : FxKind.Status;
                if (kind == FxKind.Status && withAmount) return;
                Emit(new PartyEffect(b.T, b.Action, from, origin, recipient, kind, flags, 0, hp, max, d.Status, recipientParty, recipientHostile));
                return;
        }
    }

    /// <summary>Something not the party's landing on a party member: damage (or instant death) taken.</summary>
    private void OnParty(EffectBatch b, in Decoded d, uint origin, uint recipient, long hp, long max)
    {
        int fx;
        switch (d.Kind)
        {
            case EffectKind.Damage:
                if (d.Amount <= 0) return;
                fx = (d.Crit ? Effects.FxCrit : 0) | (d.DirectHit ? Effects.FxDirectHit : 0) | (d.Blocked ? Effects.FxBlocked : 0) | (d.Parried ? Effects.FxParried : 0);
                break;
            case EffectKind.InstantDeath:
                fx = Effects.FxInstantDeath;
                break;
            default:
                return;
        }
        var hit = new TakenHit(b.T, origin, b.Action, recipient, d.Amount, fx, hp, max);
        foreach (var s in sinks) s.Taken(hit);
    }

    private void Emit(in PartyEffect e)
    {
        foreach (var s in sinks) s.Effect(e);
    }

    /// <summary>
    /// A DoT or HoT tick (the game's actor control 23): on whom, the status (often 0), a heal or
    /// damage, the amount, the source and its owner as the game said (0 none), and the target's HP.
    /// </summary>
    public void Tick(double t, uint target, uint status, bool heal, long amount, uint source, uint sourceOwner, long hp, long max)
    {
        if (sinks.Count == 0 || amount <= 0 || target == 0) return;
        var targetParty = IsParty(target);
        var member = MemberOf(source, sourceOwner);
        var e = new TickEvent(t, target, member != 0 ? member : source, status, amount, heal, hp, max, targetParty, member != 0);
        var n = 0;
        if (member != 0)
        {
            // A party member's DoT on an enemy, or their HoT on anyone (a damage tick on the party from the party isn't kept).
            if (heal || !targetParty)
            {
                var (s, action) = status != 0 ? (status, ActionFor(target, status, member)) : LatestDot(target, member, t);
                shares[n++] = new TickShare(member, amount, s, action, false);
            }
        }
        else if (!heal && !targetParty && source == 0)
        {
            n = Share(t, target, status, amount);
        }
        foreach (var sink in sinks) sink.Tick(e, shares.AsSpan(0, n));
    }

    /// <summary>A member's latest DoT on a target (its status and action), for naming a tick that came without its status.</summary>
    private (uint Status, uint Action) LatestDot(uint target, uint member, double t)
    {
        (uint, uint) best = (0, 0);
        var at = double.NegativeInfinity;
        foreach (var (key, value) in applied)
        {
            if (key.On != target || key.Member != member || !isDot(key.Status) || t - value.At > DotMemory || value.At <= at) continue;
            at = value.At;
            best = (key.Status, value.Action);
        }
        return best;
    }

    /// <summary>A tick that named no one, shared out evenly between the party members with a DoT on the target.</summary>
    private int Share(double t, uint target, uint status, long amount)
    {
        var count = 0;
        foreach (var (key, value) in applied)
        {
            if (key.On != target || !isDot(key.Status) || (status != 0 && key.Status != status) || t - value.At > DotMemory) continue;
            if (Array.IndexOf(sharers, key.Member, 0, count) >= 0 || count >= sharers.Length) continue;
            sharers[count++] = key.Member;
        }
        if (count == 0) return 0;
        var each = amount / count;
        for (var i = 0; i < count; i++)
        {
            var (s, action) = status != 0 ? (status, ActionFor(target, status, sharers[i])) : LatestDot(target, sharers[i], t);
            // The remainder to the first, so the shares add up to the tick.
            shares[i] = new TickShare(sharers[i], i == 0 ? amount - each * (count - 1) : each, s, action, true);
        }
        return count;
    }

    private void Prune(double t)
    {
        stale.Clear();
        foreach (var (key, value) in applied)
            if (t - value.At > DotMemory)
                stale.Add(key);
        foreach (var key in stale) applied.Remove(key);
    }

    /// <summary>Forget the statuses remembered (a new zone).</summary>
    public void Forget() => applied.Clear();
}
