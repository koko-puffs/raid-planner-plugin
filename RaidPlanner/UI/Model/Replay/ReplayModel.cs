using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>How a line of "Just before" reads: bad (red), good (green), plain, or quiet (muted).</summary>
public enum LogTone
{
    Plain,
    Bad,
    Good,
    Quiet,
}

/// <summary>A line of the pull's events (the site's <c>eventLine</c>): when, what, how it reads, and more for its tooltip.</summary>
public sealed record LogLine(double T, string Text, LogTone Tone, string? Title = null);

/// <summary>An enemy the moment's panel gives a card for the whole pull: its name and its actors (whose casts are its).</summary>
public sealed record EnemyCard(string Name, IReadOnlyList<int> Ids);

/// <summary>A death on the dock's ruler: when, and who ("M1 died at 3:12").</summary>
public sealed record DockDeath(double T, string Slot, string Words);

/// <summary>
/// Everything Replay shows of one pull, worked out once on a worker (the site's useReplay.ts's computeds that don't
/// depend on the moment): the track, the arena and the board's frame, the steps and phases a plan made from it would
/// have, the bosses' buffs and the party's debuffs over the pull for the dock, the enemies' cards, the deaths, the
/// pull's events for "Just before", and the words for its end. Pure: <see cref="Build"/> from a recording and,
/// optionally, a plan's arena for the duty.
/// </summary>
public sealed class ReplayModel
{
    public required ReplayTrack Track { get; init; }
    public required PlanArena Arena { get; init; }
    public required BoardFrame Frame { get; init; }
    public required IReadOnlyList<ReplayStep> Steps { get; init; }
    public required IReadOnlyList<ReplayPhase> Phases { get; init; }
    public required IReadOnlyList<BuffSpan> BossBuffs { get; init; }
    public required IReadOnlyList<DebuffWave> PartyWaves { get; init; }
    public required IReadOnlyList<MemberDebuffs> Members { get; init; }
    public required IReadOnlyList<EnemyCard> Cards { get; init; }

    /// <summary>With two cards or more, whether some casts are no card's (a helper's, another add's): the "Other casts" card.</summary>
    public required bool OtherCasts { get; init; }
    public required IReadOnlyList<DockDeath> Deaths { get; init; }
    public required IReadOnlyList<LogLine> Log { get; init; }

    /// <summary>The party in the panel's order (tanks, healers, DPS, each by their spot): entity indexes.</summary>
    public required IReadOnlyList<int> PartyOrder { get; init; }

    /// <summary>The pull's length on the dock (the pull, or further where the steps or a death went past it).</summary>
    public required double Total { get; init; }

    /// <summary>The words under the pull's end (the site's <c>endCardWords</c>), and the big word ("Clear", "Wiped", "Ended").</summary>
    public required string EndWords { get; init; }
    public required string EndWord { get; init; }

    /// <summary>The time the pull was fought for (a wipe's ends at its last death).</summary>
    public required double FoughtFor { get; init; }

    /// <summary>Whether the HP of anyone is known (plugin 0.3), and whether the party's places were recorded.</summary>
    public required bool HasHp { get; init; }
    public required bool HasPlaces { get; init; }

    /// <summary>
    /// Replay's whole pull from its recording. <paramref name="planArena"/>: a plan's arena for this duty, when one with a
    /// centre is at hand (a real arena's shape); else the arena is worked out from the pull.
    /// </summary>
    public static ReplayModel Build(Recording r, PlanArena? planArena = null)
    {
        var track = ReplayTrack.Build(r);
        var arena = planArena is { Center: not null } ? planArena : track.Arena.ToPlan();
        var frame = BoardFrame.Of(arena, track.Arena);
        var phaseStarts = PullStats.PhaseStarts(r);
        var steps = ReplayLanes.StepsOf(Timeline.Build(r), phaseStarts);
        var phases = ReplayLanes.PhasesOf(steps);
        var party = r.Party.ToDictionary(p => p.Id);
        string Who(int id) => party.TryGetValue(id, out var p) ? PartyMember.Clean(p.Name) ?? p.Slot : track.Entity(id)?.Name is { Length: > 0 } n ? n : "someone";

        // The cards: every boss, and an add with buffs of its own; one card a name.
        var ownBuffs = track.Statuses.Select(s => s.On).ToHashSet();
        var byName = new List<(string Name, List<int> Ids)>();
        foreach (var e in track.Entities)
        {
            if (e.Kind != EntityKind.Boss && !(e.Kind == EntityKind.Add && ownBuffs.Contains(e.Id))) continue;
            var name = string.IsNullOrWhiteSpace(e.Name) ? "Boss" : e.Name;
            var at = byName.FindIndex(x => x.Name == name);
            if (at < 0) byName.Add((name, new List<int> { e.Id }));
            else byName[at].Ids.Add(e.Id);
        }
        var cards = byName.Select(x => new EnemyCard(x.Name, x.Ids)).ToList();
        var cardIds = cards.SelectMany(c => c.Ids).ToHashSet();
        var other = cards.Count >= 2 && track.Casts.Any(c => !cardIds.Contains(c.Src));

        var deaths = track.Deaths.Select(d =>
        {
            var slot = party.TryGetValue(d.Who, out var p) ? p.Slot : "?";
            return new DockDeath(d.T, slot, $"{Who(d.Who)} died at {Format.Clock(d.T)}");
        }).ToList();
        var lastStepEnd = steps.Count > 0 ? steps[^1].End : 0;
        var total = Math.Max(1, Math.Max(Math.Max(lastStepEnd, track.End), deaths.Count > 0 ? deaths.Max(d => d.T) : 0));

        var order = new List<int>();
        for (var i = 0; i < track.Entities.Count; i++)
            if (track.Entities[i].Kind == EntityKind.Party) order.Add(i);
        order = order.OrderBy(i => RoleRank(track.Entities[i].Role)).ThenBy(i => PlanJson.SpotRank(track.Entities[i].Slot)).ToList();

        var wipe = PullStats.WipeDeaths(r);
        var counted = r.Events.OfType<DeathEvent>().Count(d => !wipe.Contains(d) && party.ContainsKey(d.Who));
        var fought = r.Outcome == Outcome.Wipe && wipe.Count > 0 ? wipe.Max(d => d.T) : r.Duration;
        string? endedBy = null;
        if (r.Outcome == Outcome.Wipe && wipe.Count > 0)
        {
            var lastDeath = wipe.OrderBy(d => d.T).Last();
            endedBy = lastDeath.By == 0 ? null : lastDeath.ByStatus
                ? r.Statuses.TryGetValue(lastDeath.By, out var st) ? st.Name : null
                : ReplayShapes.ActionName(r.Actions, lastDeath.By);
            endedBy ??= steps.Count > 0 ? PlanJson.WithoutPhase(steps[ReplayLanes.StepAt(steps, lastDeath.T)].Name) : null;
        }
        var pullWord = $"Pull {Math.Max(1, r.Pull)}";
        var endWords = EndCardWords(r.Outcome, pullWord, endedBy, PullStats.BossHp(r), counted);

        return new ReplayModel
        {
            Track = track,
            Arena = arena,
            Frame = frame,
            Steps = steps,
            Phases = phases,
            BossBuffs = ReplayStatuses.BossBuffLane(track),
            PartyWaves = ReplayStatuses.PartyDebuffLane(track),
            Members = ReplayStatuses.MemberDebuffLanes(track),
            Cards = cards,
            OtherCasts = other,
            Deaths = deaths,
            Log = LogOf(r, track, wipe, phaseStarts, Who),
            PartyOrder = order,
            Total = total,
            EndWords = endWords,
            EndWord = r.Outcome switch
            {
                Outcome.Clear => "Clear",
                Outcome.Wipe => "Wiped",
                _ => "Ended",
            },
            FoughtFor = fought,
            HasHp = track.Entities.Any(e => e.Hp.Any(float.IsFinite)),
            HasPlaces = r.PartyPositions && track.Entities.Any(e => e.Kind == EntityKind.Party && e.T.Length > 0),
        };
    }

    private static int RoleRank(string role) => role switch
    {
        "tank" => 0,
        "healer" => 1,
        _ => 2,
    };

    /// <summary>
    /// The words under the pull's end (the site's <c>endCardWords</c>): "Pull 14 · nobody died" for a clear; for a wipe
    /// "Pull 12 · at Knockback Wave · the boss at 38%".
    /// </summary>
    public static string EndCardWords(Outcome outcome, string pull, string? at, double? boss, int deaths)
    {
        if (outcome == Outcome.Clear) return $"{pull} · {(deaths > 0 ? Words.Count(deaths, "death") : "nobody died")}";
        var parts = new List<string> { pull };
        if (!string.IsNullOrWhiteSpace(at)) parts.Add($"at {at}");
        if (outcome == Outcome.Wipe && boss is { } b && double.IsFinite(b)) parts.Add($"the boss at {Math.Round(b, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)}%");
        return string.Join(" · ", parts);
    }

    /// <summary>Seconds said (the site's <c>secs</c>): "4.5 s" under ten, "12 s" under a minute, else "1:05".</summary>
    public static string Secs(double n)
    {
        if (!double.IsFinite(n)) return "–";
        var a = Math.Abs(n);
        return a < 60 ? n.ToString(a < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " s" : Format.Clock(a);
    }

    /// <summary>A big number short: "182.4k", "1.2M".</summary>
    public static string Short(long n) =>
        n >= 1_000_000 ? (n / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + "M"
        : n >= 1_000 ? (n / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : n.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The pull's events, in time order (the site's pull events and <c>eventLine</c>, the ones "Just before" shows): a
    /// phase beginning, deaths (the wipe's quiet), raises, interrupts and an interruptible cast that went off, a hit on
    /// half the party or more (or a buster that took a quarter of a tank's HP), the fight's debuffs going on the party,
    /// an enemy's own buffs, and head markers (by their meaning, or the mechanic they led to). Never an enemy's cast
    /// beginning (the cards show those).
    /// </summary>
    public static List<LogLine> LogOf(Recording r, ReplayTrack track, HashSet<DeathEvent> wipe, IReadOnlyList<double> phaseStarts, Func<int, string> who)
    {
        var log = new List<LogLine>();
        var party = r.Party.ToDictionary(p => p.Id);
        for (var i = 1; i < phaseStarts.Count; i++) log.Add(new LogLine(phaseStarts[i], $"P{i + 1} begins", LogTone.Quiet));
        var deaths = r.Events.OfType<DeathEvent>().Where(d => party.ContainsKey(d.Who)).OrderBy(d => d.T).ToList();
        foreach (var d in deaths)
        {
            if (wipe.Contains(d))
            {
                log.Add(new LogLine(d.T, $"{who(d.Who)} went down in the wipe", LogTone.Quiet));
                continue;
            }
            var by = d.By == 0 ? null : d.ByStatus
                ? r.Statuses.TryGetValue(d.By, out var st) ? st.Name : null
                : ReplayShapes.ActionName(r.Actions, d.By);
            log.Add(new LogLine(d.T, by is { Length: > 0 } ? $"{who(d.Who)} died to {by.Trim()}" : $"{who(d.Who)} died", LogTone.Bad));
        }
        foreach (var x in r.Events.OfType<RaiseEvent>())
        {
            var died = deaths.LastOrDefault(d => d.Who == x.Who && d.T <= x.T);
            var deadFor = died != null ? x.T - died.T : 0;
            log.Add(new LogLine(x.T, $"{who(x.Who)} raised by {(x.Lb ? "the limit break" : x.By != 0 ? who(x.By) : "someone")} after {Secs(deadFor)} dead", LogTone.Good));
        }
        foreach (var c in r.Events.OfType<CancelEvent>().Where(c => c.Interrupted))
        {
            var name = ReplayShapes.ActionName(r.Actions, c.Action);
            var by = c.By != 0 ? $" by {who(c.By)}{(c.With != 0 ? $" ({ReplayShapes.ActionName(r.Actions, c.With)})" : string.Empty)}" : string.Empty;
            log.Add(new LogLine(c.T, $"{name} interrupted{by}", LogTone.Good));
        }
        foreach (var c in track.Casts.Where(c => c.Interruptible && c.Stopped == CastStop.None && c.End <= track.End))
            log.Add(new LogLine(c.End, $"{c.Name} went off, not interrupted", LogTone.Bad));

        // Hits that landed on half the party or more (or a buster), together within 0.3 s.
        var partySize = Math.Max(1, r.Party.Count);
        foreach (var group in track.Hits.GroupBy(h => h.Action))
        {
            var list = group.OrderBy(h => h.T).ToList();
            for (var i = 0; i < list.Count;)
            {
                var j = i;
                var landed = new Dictionary<int, long>();
                var hasDmg = false;
                while (j < list.Count && list[j].T - list[i].T <= 0.3)
                {
                    foreach (var x in list[j].Targets)
                    {
                        if (!party.ContainsKey(x.Id) || !x.Landed) continue;
                        landed[x.Id] = (landed.TryGetValue(x.Id, out var v) ? v : 0) + Math.Max(0, x.Damage);
                        if (x.Damage >= 0) hasDmg = true;
                    }
                    j++;
                }
                var ids = landed.Keys.ToList();
                var buster = ids.Count is > 0 and <= 2 && ids.All(id => party[id].Role == "tank") && hasDmg
                    && ids.Any(id => party[id].MaxHp > 0 && landed[id] >= 0.25 * party[id].MaxHp);
                if (ids.Count >= Math.Max(2, (int)Math.Ceiling(partySize / 2.0)) || buster)
                {
                    var whom = ids.Count > 4 ? $"{ids.Count} people" : string.Join(", ", r.Party.Where(p => landed.ContainsKey(p.Id)).Select(p => who(p.Id)));
                    var total = landed.Values.Sum();
                    log.Add(new LogLine(list[i].T, $"{list[i].Name} hit {whom}{(hasDmg ? $" for {Short(total)}" : string.Empty)}", LogTone.Plain));
                }
                i = j;
            }
        }

        // The fight's debuffs going on the party, and an enemy's own buffs, each status's gains within a second together.
        var gains = track.Statuses.Where(s => !ReplayStatuses.IsNameless(s.Name)).ToList();
        foreach (var byStatus in gains.GroupBy(s => (s.Status, Party: party.ContainsKey(s.On))))
        {
            var list = byStatus.OrderBy(s => s.Start).ToList();
            for (var i = 0; i < list.Count;)
            {
                var j = i;
                var on = new List<string>();
                while (j < list.Count && list[j].Start - list[i].Start <= 1)
                {
                    var w = who(list[j].On);
                    if (!on.Contains(w)) on.Add(w);
                    j++;
                }
                var s = list[i];
                i = j;
                if (byStatus.Key.Party && !ReplayStatuses.MemberShows(track, s)) continue;
                var whom = on.Count > 4 ? $"{on.Count} people" : string.Join(", ", on);
                var stacks = s.Stacks is > 1 and <= ReplayStatuses.MaxStacks ? $" ×{s.Stacks}" : string.Empty;
                var lasts = s.End < track.End ? $" ({Secs(s.End - s.Start)})" : string.Empty;
                log.Add(new LogLine(s.Start, byStatus.Key.Party ? $"{s.Name}{stacks} on {whom}{lasts}" : $"{s.Name}{stacks} on {whom}", byStatus.Key.Party ? LogTone.Bad : LogTone.Plain));
            }
        }

        // Head markers: one line for a marker over several at once, by its meaning or the mechanic it led to.
        ReplayMarker? first = null;
        List<string>? on2 = null;
        void FlushMarker()
        {
            if (first == null || on2 == null) return;
            var whom = on2.Count > 4 ? $"{on2.Count} people" : string.Join(", ", on2);
            var s = on2.Count > 1 ? "s" : string.Empty;
            var link = track.Markers.Where(m => m.Icon == first.Icon && Math.Abs(m.T - first.T) <= 0.1 && m.Hit >= 0).Select(m => track.Hits[m.Hit].Name).FirstOrDefault();
            var role = track.Markers.Where(m => m.Icon == first.Icon && Math.Abs(m.T - first.T) <= 0.1 && m.Role != MechRole.None).Select(m => m.Role).FirstOrDefault();
            var what = link != null ? $": {link}" : string.Empty;
            string text;
            if (first.Meaning is MarkerMeaning.RotateCw or MarkerMeaning.RotateCcw)
                text = $"Marker{s} over {whom}: turn {(first.Meaning == MarkerMeaning.RotateCw ? "clockwise" : "anticlockwise")}";
            else if (first.Meaning != MarkerMeaning.None) text = $"{MarkerWord(first.Meaning)} marker{s} over {whom}{what}";
            else if (role != MechRole.None) text = $"{RoleWord(role)} marker{s} over {whom}{what}";
            else text = $"Head marker{s} over {whom}{what}";
            log.Add(new LogLine(first.T, text, LogTone.Quiet, $"Head marker icon {first.Icon}"));
        }
        foreach (var m in track.Markers)
        {
            var name = party.ContainsKey(m.On) ? who(m.On) : track.Entity(m.On)?.Name is { Length: > 0 } en ? en : m.On > 100 ? "a helper" : string.Empty;
            if (name.Length == 0) continue;
            if (first != null && first.Icon == m.Icon && m.T - first.T <= 0.1)
            {
                if (!on2!.Contains(name)) on2.Add(name);
                continue;
            }
            FlushMarker();
            first = m;
            on2 = new List<string> { name };
        }
        FlushMarker();
        log.Sort((a, b) => a.T.CompareTo(b.T));
        return log;
    }

    /// <summary>A marker's meaning said: "Stack", "Limit cut".</summary>
    public static string MarkerWord(MarkerMeaning m) => m switch
    {
        MarkerMeaning.Spread => "Spread",
        MarkerMeaning.Stack => "Stack",
        MarkerMeaning.Tankbuster => "Tankbuster",
        MarkerMeaning.LineStack => "Line stack",
        MarkerMeaning.LimitCut => "Limit cut",
        MarkerMeaning.Target => "Target",
        MarkerMeaning.RotateCw => "Turns clockwise",
        MarkerMeaning.RotateCcw => "Turns anticlockwise",
        _ => "Head",
    };

    /// <summary>A mechanic's role said: "Spread", "Tankbuster".</summary>
    public static string RoleWord(MechRole r) => r switch
    {
        MechRole.Stack => "Stack",
        MechRole.LineStack => "Line stack",
        MechRole.Spread => "Spread",
        MechRole.Tankbuster => "Tankbuster",
        MechRole.SharedTankbuster => "Shared tankbuster",
        MechRole.Cleave => "Cleave",
        MechRole.Tower => "Tower",
        MechRole.Knockback => "Knockback",
        MechRole.Gaze => "Gaze",
        _ => "Head",
    };
}
