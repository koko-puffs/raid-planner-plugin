using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.Core;

/// <summary>
/// What the progress summary adds in round three (the site's contract §3), worked out from a kept
/// recording: the main boss's HP left at the end (<see cref="BossHp"/>), when the phases began
/// (<see cref="PhaseStarts"/>), and each spot's numbers with the enemies' hits and the failure
/// debuffs (<see cref="Stats"/>). The numbers are the meter's sums (<see cref="Tally"/>) over the
/// recording's combat block, so the summary and the meter agree.
/// </summary>
public static class PullStats
{
    /// <summary>The most bytes the summary's <c>stats</c> may take (the site's column allows 8 KB).</summary>
    public const int MaxBytes = 6 * 1024;

    public const int MaxHits = 400;
    public const int MaxDebuffs = 200;

    /// <summary>The most spots in <c>slots</c> (a slot mask is a whole number).</summary>
    public const int MaxSlots = 30;

    /// <summary>A wiped pull's deaths within this of its last are the wipe, not anyone's (the site's rule).</summary>
    public const double WipeWindow = 6.0;

    /// <summary>
    /// When each phase began, in seconds (the first at 0): every enemy that had been targetable gone
    /// for 8 seconds or more, then one targetable (the site's <c>phaseStarts</c>, 50 at most after the first).
    /// </summary>
    public static List<double> PhaseStarts(Recording r)
    {
        var up = new HashSet<int>();
        var seen = new HashSet<int>();
        var targets = r.Events.OfType<TargetEvent>().OrderBy(e => e.T).ToList();
        // An enemy's first event says what it was before: going untargetable means it was targetable.
        foreach (var e in targets)
            if (seen.Add(e.Actor) && !e.On)
                up.Add(e.Actor);
        var starts = new List<double> { 0 };
        var downAt = -1.0;
        foreach (var e in targets)
        {
            if (e.On)
            {
                if (up.Count == 0 && downAt >= 0 && e.T - downAt >= RecordingBuilder.PhaseGap && starts.Count < PhaseClock.MostPhases) starts.Add(e.T);
                up.Add(e.Actor);
                downAt = -1;
            }
            else
            {
                up.Remove(e.Actor);
                if (up.Count == 0 && downAt < 0) downAt = e.T;
            }
        }
        return starts;
    }

    /// <summary>
    /// The HP % left (0–100, two decimals) of the fight's main boss at the pull's end: of the bosses,
    /// the one targetable last (the one with the most max HP among those), read from the last frame
    /// with its HP. 0 on a clear; null when it isn't known (no HP in the frames, no boss).
    /// </summary>
    public static double? BossHp(Recording r)
    {
        if (r.Outcome == Outcome.Clear) return 0;
        var bosses = r.Actors.Where(a => a.Kind == ActorKind.Boss).ToList();
        if (bosses.Count == 0) return null;
        var since = new Dictionary<int, double>();
        var last = new Dictionary<int, double>();
        foreach (var e in r.Events.OfType<TargetEvent>().OrderBy(e => e.T))
        {
            if (e.On) since.TryAdd(e.Actor, e.T);
            else if (since.Remove(e.Actor)) last[e.Actor] = e.T;
        }
        foreach (var actor in since.Keys) last[actor] = r.Duration;
        var main = bosses.OrderByDescending(a => last.TryGetValue(a.Id, out var t) ? t : -1).ThenByDescending(a => a.MaxHp).First();
        long? hp = null;
        for (var i = r.Frames.Count - 1; i >= 0 && hp == null; i--)
            if (r.Frames[i].Hp is { } h && h.TryGetValue(main.Id, out var v))
                hp = v;
        if (hp is not { } now || main.MaxHp <= 0) return null;
        return Math.Round(Math.Clamp(100.0 * now / main.MaxHp, 0, 100), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The deaths that were the wipe: a wiped pull's, within <see cref="WipeWindow"/> of its last.</summary>
    public static HashSet<DeathEvent> WipeDeaths(Recording r)
    {
        var deaths = r.Events.OfType<DeathEvent>().ToList();
        if (r.Outcome != Outcome.Wipe || deaths.Count == 0) return new HashSet<DeathEvent>();
        var last = deaths.Max(d => d.T);
        return deaths.Where(d => d.T >= last - WipeWindow).ToHashSet();
    }

    /// <summary>
    /// The party's numbers for the pull, as the meter adds them up: each member's damage (their pets'
    /// and their DoTs' included; the limit break apart), healing that landed and overheal, damage
    /// taken (enemy hits and bleeds), crits and direct hits, and active time. A tick the game sent
    /// without saying whose is shared between the party's DoTs on the target then (<paramref name="isDot"/>).
    /// </summary>
    public static Tally Totals(Recording r, Func<uint, bool>? isDot = null) => Totals(r, isDot, out _);

    /// <summary>
    /// <see cref="Totals(Recording, Func{uint, bool}?)"/>, with when the damage between the party and the
    /// enemies began and last went on (<paramref name="fought"/>, seconds from the pull; NaN without any): the
    /// party's hits on an enemy (the limit break's too), an enemy's hit that did damage to one of them or
    /// killed them outright, and (for the last only: a tick never starts it) a damage tick on one of them
    /// or one credited to one of them, a tick shared out included (one nobody had a DoT for isn't).
    /// </summary>
    public static Tally Totals(Recording r, Func<uint, bool>? isDot, out (double First, double Last) fought)
    {
        var tally = new Tally();
        double first = double.NaN, last = double.NaN;
        void Fought(double t, bool tick)
        {
            if (!tick && (double.IsNaN(first) || t < first)) first = t;
            if (double.IsNaN(last) || t > last) last = t;
        }
        var party = r.Party.Select(p => p.Id).ToHashSet();
        var maxHp = r.Party.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().MaxHp);
        var hpAt = PartyHp(r);
        if (r.Combat is { } c)
        {
            foreach (var f in c.Fx)
            {
                if (!party.Contains(f.Origin)) continue;
                var flags = (FxFlags)(f.Kind & ~7);
                switch ((FxKind)(f.Kind & 7))
                {
                    case FxKind.Damage:
                        // On the party (a cost of one's own) it isn't the party's damage (the parser doesn't keep it).
                        if (party.Contains(f.Recipient)) break;
                        tally.Damage(f.Origin, f.T, f.Action, f.Amount, flags);
                        Fought(f.T, false);
                        break;
                    case FxKind.Heal:
                        var max = maxHp.TryGetValue(f.Recipient, out var m) ? m : 0;
                        tally.Heal(f.Origin, f.Action, f.Amount, PartyEffect.Over(f.Amount, f.HpBefore, max), flags);
                        break;
                    case FxKind.Shield:
                        tally.Shield(f.Origin);
                        break;
                }
            }
            // The party's DoTs on each enemy, as the statuses came and went, for ticks that named no one.
            var dots = new Dictionary<int, Dictionary<(uint Status, int Source), double>>();
            var st = c.Statuses.Where(s => isDot != null && isDot(s.Status) && !party.Contains(s.On)).OrderBy(s => s.T).ToList();
            var next = 0;
            foreach (var tick in c.Ticks.OrderBy(x => x.T))
            {
                for (; next < st.Count && st[next].T <= tick.T; next++)
                {
                    var s = st[next];
                    if (!dots.TryGetValue(s.On, out var on)) dots[s.On] = on = new();
                    if (s.Gain) on[(s.Status, s.Source)] = s.T;
                    else on.Remove((s.Status, s.Source));
                }
                if (party.Contains(tick.On))
                {
                    if (!tick.Heal)
                    {
                        tally.Taken(tick.On, tick.Status, true, tick.Amount);
                        Fought(tick.T, true);
                    }
                    else if (party.Contains(tick.Source))
                    {
                        // A HoT's overheal against the HP the game side read as it ticked (as the meter does); a file
                        // from before that was kept has only the frames' (the last before it).
                        var max = maxHp.TryGetValue(tick.On, out var m) ? m : 0;
                        var before = tick.HpBefore >= 0 ? tick.HpBefore : hpAt(tick.On, tick.T);
                        tally.Heal(tick.Source, 0, tick.Amount, PartyEffect.Over(tick.Amount, before, max), FxFlags.None, true, tick.Status);
                    }
                    continue;
                }
                if (tick.Heal) continue;
                if (party.Contains(tick.Source))
                {
                    tally.Damage(tick.Source, tick.T, 0, tick.Amount, FxFlags.None, true, tick.Status);
                    Fought(tick.T, true);
                }
                else if (tick.Source == 0 && dots.TryGetValue(tick.On, out var on))
                {
                    // Each party member with one of the jobs' DoTs on it (the tick's status when it names one), in
                    // the party's order: a whole share each, the remainder the first's (the meter's CombatParser.Share).
                    var sharers = on.Keys.Where(k => tick.Status == 0 || k.Status == tick.Status).Select(k => k.Source).Distinct().OrderBy(x => x).ToList();
                    if (sharers.Count == 0) continue;
                    var each = tick.Amount / sharers.Count;
                    for (var i = 0; i < sharers.Count; i++)
                        tally.Damage(sharers[i], tick.T, 0, i == 0 ? tick.Amount - each * (sharers.Count - 1) : each, FxFlags.None, true, tick.Status, true);
                    Fought(tick.T, true);
                }
            }
        }
        foreach (var h in r.Events.OfType<HitEvent>())
        {
            if (h.Damage == null || party.Contains(h.Source)) continue;
            for (var i = 0; i < h.Targets.Count; i++)
            {
                if (!party.Contains(h.Targets[i])) continue;
                if (h.Damage[i] > 0) tally.Taken(h.Targets[i], h.Action, false, h.Damage[i]);
                if (h.Damage[i] > 0 || (h.Fx != null && (h.Fx[i] & Effects.FxInstantDeath) != 0)) Fought(h.T, false);
            }
        }
        fought = (first, last);
        return tally;
    }

    /// <summary>
    /// The fight's length, seconds (two decimals, from the times as the file has them): what ENCDPS, HPS
    /// and damage taken a second are over, the meter's rule (<see cref="MeterSession"/>): from the first
    /// damage between the party and an enemy (either way; a tick never starts it) to the end, a wipe's
    /// last death, else the last damage between them, ticks included (ACT's convention). Null when the
    /// recording has no amounts (before 0.3.0) or no such damage.
    /// </summary>
    public static double? Length(Recording r, (double First, double Last) fought)
    {
        if (double.IsNaN(fought.First)) return null;
        var party = r.Party.Select(p => p.Id).ToHashSet();
        var deaths = r.Events.OfType<DeathEvent>().Where(d => party.Contains(d.Who)).Select(d => d.T).ToList();
        var end = r.Outcome == Outcome.Wipe && deaths.Count > 0 ? deaths.Max() : fought.Last;
        return Math.Max(0, CastEvent.Round(CastEvent.Round(end, 2) - CastEvent.Round(fought.First, 2), 2));
    }

    /// <summary>A party member's HP at a time, from the frames (the last at or before it; −1 when the frames don't have it).</summary>
    private static Func<int, double, long> PartyHp(Recording r)
    {
        var byMember = new Dictionary<int, List<(double T, long Hp)>>();
        foreach (var f in r.Frames)
        {
            if (f.Hp == null) continue;
            foreach (var (id, hp) in f.Hp)
            {
                if (id > 100) continue;
                if (!byMember.TryGetValue(id, out var list)) byMember[id] = list = new();
                list.Add((f.T, hp));
            }
        }
        return (id, t) =>
        {
            if (!byMember.TryGetValue(id, out var list) || list.Count == 0 || list[0].T > t) return -1;
            int lo = 0, hi = list.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (list[mid].T <= t) lo = mid;
                else hi = mid - 1;
            }
            return list[lo].Hp;
        };
    }

    /// <summary>
    /// The summary's <c>stats</c> (contract §3): each spot's totals, the limit break's damage, the
    /// enemies' hits on the party (autos left out), the damage downs and vulnerability ups gained, and
    /// the names of the actions and statuses those use. At most <see cref="MaxBytes"/>: past that the
    /// latest hits go first, then the latest debuffs.
    /// </summary>
    /// <param name="failKind">A status's kind (<see cref="CombatLists.FailKind"/>); by default from its name in the recording.</param>
    public static JsonObject Stats(Recording r, Func<uint, int>? failKind = null, Func<uint, bool>? isDot = null)
    {
        failKind ??= id => r.Statuses.TryGetValue(id, out var s) ? CombatLists.FailKind(s.Name) : CombatLists.NotAFail;
        var slots = r.Party.Take(MaxSlots).ToList();
        var bit = slots.Select((p, i) => (p.Id, i)).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().i);
        var tally = Totals(r, isDot, out var fought);
        var length = Length(r, fought);
        var wipe = WipeDeaths(r);

        // Damage downs and vulnerability ups gained, each gain counted with its stacks.
        var dd = new Dictionary<int, int>();
        var vuln = new Dictionary<int, int>();
        var stacks = new Dictionary<(int On, uint Id), int>();
        var debuffs = new List<(long Ds, uint Status, int Mask)>();
        foreach (var s in r.Events.OfType<StatusEvent>().OrderBy(e => e.T))
        {
            if (!bit.TryGetValue(s.On, out var b)) continue;
            var kind = failKind(s.Id);
            if (kind == CombatLists.NotAFail) continue;
            if (!s.Gain)
            {
                stacks.Remove((s.On, s.Id));
                continue;
            }
            var had = stacks.TryGetValue((s.On, s.Id), out var n) ? n : 0;
            var now = Math.Max(1, s.Stacks);
            var gained = s.Refresh ? Math.Max(1, now - had) : now;
            stacks[(s.On, s.Id)] = now;
            var counts = kind == CombatLists.FailDamageDown ? dd : vuln;
            counts[s.On] = (counts.TryGetValue(s.On, out var c) ? c : 0) + gained;
            var ds = Ds(s.T);
            var at = debuffs.FindIndex(x => x.Ds == ds && x.Status == s.Id);
            if (at >= 0) debuffs[at] = debuffs[at] with { Mask = debuffs[at].Mask | (1 << b) };
            else debuffs.Add((ds, s.Id, 1 << b));
        }

        // The enemies' hits on the party, autos left out (as the timeline leaves them out).
        var tanks = r.Party.Where(p => p.Role == "tank").Select(p => p.Id).ToHashSet();
        var party = r.Party.Select(p => p.Id).ToHashSet();
        var hits = new List<(long Ds, uint Action, int Mask, long Damage)>();
        foreach (var h in r.Events.OfType<HitEvent>().OrderBy(e => e.T))
        {
            if (party.Contains(h.Source) || !r.Actions.TryGetValue(h.Action, out var info) || !Named(info)) continue;
            // Only those it landed on: a miss (fully resisted), a dodge or an invulnerable target isn't
            // a hit to fail by (as the site reads a recording's hits).
            var mask = 0;
            for (var i = 0; i < h.Targets.Count; i++)
                if (bit.TryGetValue(h.Targets[i], out var b) && (h.Fx == null || (h.Fx[i] & NotLanded) == 0))
                    mask |= 1 << b;
            if (mask == 0) continue;
            if (info.CastType <= 1 && h.Targets.Count <= 1 && !(h.Targets.Count == 1 && tanks.Contains(h.Targets[0]) && info.Cast > 0)) continue;
            hits.Add((Ds(h.T), h.Action, mask, h.Damage?.Sum() ?? 0));
        }
        if (hits.Count > MaxHits) hits.RemoveRange(MaxHits, hits.Count - MaxHits);
        if (debuffs.Count > MaxDebuffs) debuffs.RemoveRange(MaxDebuffs, debuffs.Count - MaxDebuffs);

        var slotsJson = new JsonArray();
        foreach (var p in slots)
        {
            var t = tally.Has(p.Id) ? tally.Of(p.Id) : new MemberTotals();
            slotsJson.Add(new JsonObject
            {
                ["slot"] = p.Slot,
                ["job"] = p.Job,
                ["dmg"] = t.Damage,
                ["heal"] = t.Healing,
                ["over"] = t.Overheal,
                ["taken"] = t.Taken,
                ["crit"] = Math.Round(t.CritRate, 3),
                ["dh"] = Math.Round(t.DirectHitRate, 3),
                ["active"] = Math.Round(t.Active, 1),
                ["deaths"] = r.Events.OfType<DeathEvent>().Count(d => d.Who == p.Id && !wipe.Contains(d)),
                ["dd"] = dd.TryGetValue(p.Id, out var ddn) ? ddn : 0,
                ["vuln"] = vuln.TryGetValue(p.Id, out var vn) ? vn : 0,
            });
        }

        // The first so many hits and debuffs, with the names they use.
        JsonObject Make(int hitCount, int debuffCount)
        {
            var names = new JsonObject();
            foreach (var h in hits.Take(hitCount))
                if (!names.ContainsKey(Key(h.Action)) && r.Actions.TryGetValue(h.Action, out var a) && a.Name.Trim().Length > 0)
                    names[Key(h.Action)] = a.Name.Trim();
            foreach (var d in debuffs.Take(debuffCount))
                if (!names.ContainsKey(Key(d.Status)) && r.Statuses.TryGetValue(d.Status, out var s) && s.Name.Trim().Length > 0)
                    names[Key(d.Status)] = s.Name.Trim();
            var o = new JsonObject
            {
                ["v"] = 1,
                ["slots"] = slotsJson.DeepClone(),
                ["lb"] = tally.LimitBreak.Damage,
                ["hits"] = new JsonArray(hits.Take(hitCount).Select(h => (JsonNode)new JsonArray(h.Ds, (long)h.Action, (long)h.Mask, h.Damage)).ToArray()),
                ["debuffs"] = new JsonArray(debuffs.Take(debuffCount).Select(d => (JsonNode)new JsonArray(d.Ds, (long)d.Status, (long)d.Mask)).ToArray()),
                ["names"] = names,
            };
            // The fight's length the numbers are over (the meter's), when there was damage to measure it by.
            if (length is { } len) o["len"] = len;
            return o;
        }

        // The most that fits: as many of the earliest hits as fit with every debuff; with none of the
        // hits too big, as many of the earliest debuffs as fit.
        static int Most(int count, Func<int, bool> fits)
        {
            int lo = 0, hi = count;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (fits(mid)) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        var stats = Make(hits.Count, debuffs.Count);
        if (Size(stats) <= MaxBytes) return stats;
        if (Size(Make(0, debuffs.Count)) <= MaxBytes)
            return Make(Most(hits.Count, n => Size(Make(n, debuffs.Count)) <= MaxBytes), debuffs.Count);
        return Make(0, Most(debuffs.Count, n => Size(Make(0, n)) <= MaxBytes));
    }

    private static string Key(uint id) => id.ToString(CultureInfo.InvariantCulture);

    private static long Ds(double t) => double.IsFinite(t) ? (long)Math.Round(Math.Max(0, t) * 10, MidpointRounding.AwayFromZero) : 0;

    public static int Size(JsonNode node) => Encoding.UTF8.GetByteCount(node.ToJsonString());

    /// <summary>A target's effect bits that mean the hit didn't count on them: missed (fully resisted), invulnerable, dodged.</summary>
    private const int NotLanded = Effects.FxResisted | Effects.FxInvulnerable | Effects.FxDodged;

    /// <summary>An auto-attack's names, as the site's <c>AUTO_ATTACK</c> has them: never a mechanic.</summary>
    private static readonly string[] Autos = { "attack", "auto-attack", "shot" };

    private static bool Named(ActionInfo info) =>
        !string.IsNullOrWhiteSpace(info.Name) && !Autos.Contains(info.Name.Trim(), StringComparer.OrdinalIgnoreCase);
}
