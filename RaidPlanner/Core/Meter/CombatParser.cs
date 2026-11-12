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
/// - A tick is credited to the party member the game names (a pet's owner); a damage tick on an enemy
///   that names no one is shared out between the party members with one of the jobs' DoTs on the target
///   then (the tick's own status when it names one), a whole share each and the remainder to the first
///   in the party list, and marked estimated; with nobody to share it, it's nobody's. Which DoTs are on
///   whom is what the game's status lists say, read by the game side (<see cref="BeginStatuses"/> …
///   <see cref="EndStatuses"/>; every frame of a pull, in the same reads the recording's <c>combat.st</c>
///   rows are made from, so the meter, the summary and the site share a tick alike; four times a second
///   otherwise). A HoT's tick that names no one is nobody's.
/// - Since 0.4.0 every action (and item) a party member or their pet uses is handed on as a
///   <see cref="PartyUse"/> whatever it did (auto-attacks, ActionCategory 1, left out: they're the
///   effects' already), with what its effects said (an interrupt, a status resisted, a provoke, a
///   revive, all missed); an item (the message's action type 2) is only that, never an effect. One
///   action resolved in two messages (the same global sequence) is one use.
/// - Nonsense (a single non-limit-break hit or heal over <see cref="Absurd"/>) is dropped and counted
///   in <see cref="Anomalies"/>: the game has moved something the parser reads. A heal landing on an
///   enemy is dropped (never the party's healing) but isn't nonsense: an enemy that absorbs an element
///   (as some of Eureka's do) is healed by the party's attacks.
/// </summary>
public sealed class CombatParser
{
    /// <summary>
    /// How long the action that put a status on is remembered (seconds): what a DoT's ticks are named after when
    /// its status isn't in the game's lists (sharing goes by the lists, <see cref="StatusOn"/>).
    /// </summary>
    public const double DotMemory = 60;

    /// <summary>A single hit bigger than this that isn't a limit break can't be right.</summary>
    public const long Absurd = 5_000_000;

    private const int MostParty = 24;

    private readonly Func<uint, int> category;
    private readonly Func<uint, bool> isShield;
    private readonly Func<uint, bool> isDot;
    private readonly Func<uint, bool> isArea;
    private readonly List<ICombatSink> sinks = new();

    /// <summary>The last few uses' (caster, global sequence), so an action that came in two messages is one use.</summary>
    private readonly (uint Caster, uint Sequence)[] usedSeq = new (uint, uint)[32];
    private int usedNext;

    /// <summary>The message's action type for an item.</summary>
    public const byte ItemType = 2;
    private readonly uint[] party = new uint[MostParty];
    private int partyCount;

    /// <summary>Statuses the party put on anyone with an action: (on, status, party member) → the action and when.</summary>
    private readonly Dictionary<(uint On, uint Status, uint Member), (uint Action, double At)> applied = new();

    private readonly List<(uint, uint, uint)> stale = new();

    /// <summary>When <see cref="applied"/> may next be pruned: at most once a second, so a crowd of statuses isn't walked on every one put on.</summary>
    private double nextPrune = double.NegativeInfinity;

    /// <summary>The most statuses remembered: past it (more than a minute's worth can't be pruned yet) they're forgotten, so memory stays bounded.</summary>
    private const int MostApplied = 4096;
    private readonly TickShare[] shares = new TickShare[MostParty];
    private readonly uint[] sharers = new uint[MostParty];

    // The party's DoTs on enemies as the game's status lists show them, by (on, status, party member)
    // with when each was first seen and last seen; those seen this frame, and whose lists were read this frame.
    private readonly Dictionary<(uint On, uint Status, uint Member), (double Seen, double Last)> dots = new();
    private readonly HashSet<(uint On, uint Status, uint Member)> dotsSeen = new();
    private readonly HashSet<uint> dotsRead = new();
    private readonly List<(uint On, uint Status, uint Member)> dotsGone = new();

    /// <param name="category">An action's ActionCategory row (limit breaks are 9 and 15).</param>
    /// <param name="isShield">Is a status a barrier (<see cref="CombatLists.ShieldNames"/>)?</param>
    /// <param name="isDot">Is a status one of the jobs' DoTs (<see cref="CombatLists.DotNames"/>)?</param>
    /// <param name="isArea">Is an action placed on the ground (the Action sheet's TargetArea: Sacred Soil, Asylum…)?</param>
    public CombatParser(Func<uint, int>? category = null, Func<uint, bool>? isShield = null, Func<uint, bool>? isDot = null, Func<uint, bool>? isArea = null)
    {
        this.category = category ?? (_ => 0);
        this.isShield = isShield ?? (_ => false);
        this.isDot = isDot ?? (_ => false);
        this.isArea = isArea ?? (_ => false);
    }

    /// <summary>An action's ActionCategory row, as the parser reads it (the diagnostics' question: 1 an auto-attack).</summary>
    public int Category(uint action) => category(action);

    /// <summary>Is an action placed on the ground, as the parser reads it (the diagnostics' question).</summary>
    public bool IsArea(uint action) => isArea(action);

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

    public bool IsParty(uint id) => PartyIndex(id) >= 0;

    /// <summary>Where someone is in the party list (0 first), −1 for someone not in it.</summary>
    public int PartyIndex(uint id)
    {
        if (id == 0) return -1;
        for (var i = 0; i < partyCount; i++)
            if (party[i] == id)
                return i;
        return -1;
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

    // ---------------------------------------------------------------------------------------------
    // The party's DoTs on enemies, from the game's status lists (the game side reads them every frame
    // a pull is recorded, as it reads them for the recording's combat.st, and otherwise four times a
    // second while the meter listens in combat): BeginStatuses, StatusesOf for each whose list is
    // read, StatusOn for each status on them a party member (or their pet) put on, then EndStatuses.
    // A DoT is on from the frame it's first seen to the frame a read of its target no longer has it,
    // just as the recording's rows say it's gained and gone.

    /// <summary>A frame's reading of statuses begins.</summary>
    public void BeginStatuses()
    {
        dotsSeen.Clear();
        dotsRead.Clear();
    }

    /// <summary>Someone whose statuses are read this frame (someone not read, out of sight, keeps theirs).</summary>
    public void StatusesOf(uint on) => dotsRead.Add(on);

    /// <summary>
    /// A status on <paramref name="on"/> that party member <paramref name="member"/> (or their pet) put on,
    /// seen this frame at <paramref name="t"/>. Only the jobs' DoTs on someone outside the party are kept.
    /// </summary>
    public void StatusOn(double t, uint on, uint status, uint member)
    {
        if (on == 0 || status == 0 || member == 0 || !isDot(status) || IsParty(on)) return;
        var key = (on, status, member);
        dotsSeen.Add(key);
        dots[key] = (dots.TryGetValue(key, out var had) ? had.Seen : t, t);
    }

    /// <summary>
    /// The frame's statuses are read: a DoT not seen on someone whose list was read is gone. One on
    /// someone not read for <see cref="DotMemory"/> (gone from sight for good: no DoT lasts that long) is
    /// forgotten too, so a long session in one zone doesn't keep every enemy it ever fought.
    /// </summary>
    public void EndStatuses(double t)
    {
        if (dots.Count == 0) return;
        dotsGone.Clear();
        foreach (var (key, when) in dots)
            if (!dotsSeen.Contains(key) && (dotsRead.Contains(key.On) || t - when.Last > DotMemory))
                dotsGone.Add(key);
        foreach (var key in dotsGone) dots.Remove(key);
    }

    /// <summary>Is a party member's DoT on someone now (as the game's lists last said)? With <paramref name="status"/> 0, any of theirs.</summary>
    public bool DotOn(uint on, uint member, uint status = 0)
    {
        foreach (var key in dots.Keys)
            if (key.On == on && key.Member == member && (status == 0 || key.Status == status))
                return true;
        return false;
    }

    /// <summary>An action went off (actions, type 1; a party member's item, type 2, is only a use).</summary>
    public void Action(EffectBatch b)
    {
        if (sinks.Count == 0 || b.Action == 0) return;
        var member = MemberOf(b.Caster, b.CasterOwner);
        if (b.ActionType == ItemType)
        {
            if (member != 0) Use(b, member, UseFlags.Item);
            return;
        }
        if (b.ActionType != 1) return;
        var pet = member != 0 && member != b.Caster;
        var cat = category(b.Action);
        var lb = member != 0 && CombatLists.IsLimitBreak(cat);
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
        // The use after its effects, so an encounter its first hit starts has begun when the use is told.
        if (member != 0 && !CombatLists.IsAutoAttack(cat)) Use(b, member, (pet ? UseFlags.Pet : UseFlags.None) | (lb ? UseFlags.LimitBreak : UseFlags.None));
    }

    /// <summary>
    /// A party member's use of an action or item, with what its effects said (<see cref="UseFlags"/>):
    /// handed on once per (caster, global sequence).
    /// </summary>
    private void Use(EffectBatch b, uint member, UseFlags flags)
    {
        if (b.Sequence != 0)
        {
            foreach (var (caster, seq) in usedSeq)
                if (caster == b.Caster && seq == b.Sequence)
                    return;
            usedSeq[usedNext] = (b.Caster, b.Sequence);
            usedNext = (usedNext + 1) % usedSeq.Length;
        }
        if (b.Count == 0) flags |= UseFlags.NoTarget;
        var allMissed = b.Count > 0;
        for (var i = 0; i < b.Count; i++)
        {
            var any = false;
            var onlyMissed = true;
            foreach (var e in b.EffectsOf(i))
            {
                if (e.Type == 0) continue;
                switch (e.Type)
                {
                    case Effects.Interrupt:
                        flags |= UseFlags.Interrupted;
                        break;
                    case Effects.FullResistStatus:
                        flags |= UseFlags.StatusResisted;
                        break;
                    case Effects.ThreatPosition:
                        flags |= UseFlags.Threat;
                        break;
                    case Effects.ReviveLb:
                        flags |= UseFlags.Revive;
                        break;
                }
                // What went to the caster (a self-buff) or came from the target says nothing of whether it hit.
                if ((e.Param4 & 0xA0) != 0) continue;
                any = true;
                if (e.Type is not (Effects.Miss or Effects.FullResist or Effects.Invulnerable)) onlyMissed = false;
            }
            if (!any || !onlyMissed) allMissed = false;
        }
        if (allMissed) flags |= UseFlags.Missed;
        var ground = b.HasGround && isArea(b.Action);
        var u = new PartyUse(b.T, member, b.Caster, b.Action, b.ActionType, b.Count > 0 ? b.Targets[0] : 0, b.Count, flags, ground,
            ground ? b.GroundX : 0, ground ? b.GroundZ : 0);
        foreach (var s in sinks) s.Use(u);
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
                Emit(new PartyEffect(b.T, b.Action, from, origin, recipient, FxKind.Damage, flags, d.Amount, -1, -1, 0, false, recipientHostile, d.Bonus));
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
                if (applied.Count > 512 && b.T >= nextPrune) Prune(b.T);
                var kind = isShield(d.Status) ? FxKind.Shield : FxKind.Status;
                var put = new PartyEffect(b.T, b.Action, from, origin, recipient, kind, flags, 0, hp, max, d.Status, recipientParty, recipientHostile);
                // Every status put on is told (a stun that came with damage too: the meter's stuns, as the status lists show them),
                // but one with damage or a heal on the same target isn't a row of its own.
                foreach (var s in sinks) s.StatusOn(put);
                if (kind == FxKind.Status && withAmount) return;
                Emit(put);
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

    /// <summary>
    /// A member's latest DoT on a target (its status and the action that put it on), for naming a tick
    /// that came without its status: the one on it now first seen last (the game's lists), else the last
    /// they put on it in the last minute (their actions' effects).
    /// </summary>
    private (uint Status, uint Action) LatestDot(uint target, uint member, double t)
    {
        (uint, uint) best = (0, 0);
        var at = double.NegativeInfinity;
        foreach (var (key, when) in dots)
        {
            if (key.On != target || key.Member != member || when.Seen <= at) continue;
            at = when.Seen;
            best = (key.Status, ActionFor(target, key.Status, member));
        }
        if (at > double.NegativeInfinity) return best;
        foreach (var (key, value) in applied)
        {
            if (key.On != target || key.Member != member || !isDot(key.Status) || t - value.At > DotMemory || value.At <= at) continue;
            at = value.At;
            best = (key.Status, value.Action);
        }
        return best;
    }

    /// <summary>
    /// A damage tick on an enemy that named no one, shared out between the party members with one of the
    /// jobs' DoTs on it now (the tick's status when it names one), as the game's lists say: a whole share
    /// each, the remainder to the first in the party list (the recording's lowest party id), so the
    /// shares add up to the tick. None (nobody's) when no one has.
    /// </summary>
    private int Share(double t, uint target, uint status, long amount)
    {
        var count = 0;
        foreach (var key in dots.Keys)
        {
            if (key.On != target || (status != 0 && key.Status != status) || !IsParty(key.Member)) continue;
            if (Array.IndexOf(sharers, key.Member, 0, count) >= 0 || count >= sharers.Length) continue;
            sharers[count++] = key.Member;
        }
        if (count == 0) return 0;
        // In the party list's order (a few at most: an insertion sort, nothing allocated).
        for (var i = 1; i < count; i++)
        {
            var m = sharers[i];
            var at = PartyIndex(m);
            var j = i - 1;
            for (; j >= 0 && PartyIndex(sharers[j]) > at; j--) sharers[j + 1] = sharers[j];
            sharers[j + 1] = m;
        }
        var each = amount / count;
        for (var i = 0; i < count; i++)
        {
            var (s, action) = status != 0 ? (status, ActionFor(target, status, sharers[i])) : LatestDot(target, sharers[i], t);
            shares[i] = new TickShare(sharers[i], i == 0 ? amount - each * (count - 1) : each, s, action, true);
        }
        return count;
    }

    private void Prune(double t)
    {
        nextPrune = t + 1;
        stale.Clear();
        foreach (var (key, value) in applied)
            if (t - value.At > DotMemory)
                stale.Add(key);
        foreach (var key in stale) applied.Remove(key);
        // Still too many (a minute of a huge crowd): forgotten, rather than growing without end.
        if (applied.Count > MostApplied) applied.Clear();
    }

    /// <summary>Forget the statuses remembered (a new zone).</summary>
    public void Forget()
    {
        applied.Clear();
        dots.Clear();
        nextPrune = double.NegativeInfinity;
        Array.Clear(usedSeq);
        usedNext = 0;
    }
}
