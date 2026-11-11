using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPlanner.Core.Meter;

/// <summary>A party member as the game shows them this frame (the meter's names stay in the plugin).</summary>
/// <param name="Role">The game's ClassJob role: 1 tank, 2 melee, 3 ranged, 4 healer.</param>
public readonly record struct PartyState(uint Id, string Name, string Job, byte Role, long Hp, long MaxHp, bool InCombat, bool You)
{
    public bool Dead => MaxHp > 0 && Hp <= 0;
}

/// <summary>
/// The meter: the parser's findings (<see cref="ICombatSink"/>) gathered into encounters, and each
/// frame's party (<see cref="Frame"/>) for who's who, in combat and alive. Everything here is
/// game-free and runs on the framework thread; the game side (<c>Game/CombatMeter</c>) feeds it.
///
/// An encounter starts on the first damage between the party and an enemy (either way: a hit on the
/// enemy, or the enemy's on the party; a DoT's tick never starts one), with the party's heals and
/// shields from the second before folded in. It ends when nobody in the party has been in combat for
/// <see cref="EndAfter"/> seconds (<c>Ended</c>), when the whole party is down (<c>Wipe</c>), when the
/// duty says wiped or completed, or on leaving the zone. Its length runs from the first hit to the
/// last damage or death. Phases are the recording's rule (<see cref="PhaseClock"/>), from the
/// enemies' targetability the game side reports.
/// </summary>
public sealed class MeterSession : IMeterSource, ICombatSink
{
    /// <summary>A live encounter's numbers are worked out again at most this often (seconds).</summary>
    public const double RefreshEvery = 0.25;

    /// <summary>The party's effects this long before the first hit are part of the encounter (an opener's buffs and shields).</summary>
    public const double Before = 1.0;

    /// <summary>Each party member's last this many hits and heals are kept for a death's recap.</summary>
    public const int RecapLength = 10;

    /// <summary>A wiped encounter's deaths within this of its last are the wipe, not anyone's (the site's rule).</summary>
    public const double WipeWindow = 6.0;

    /// <summary>Pace is shown from this far into a phase.</summary>
    public const double PaceFrom = 10.0;

    /// <summary>The duty saying wiped or completed this long after an encounter ended still sets how it ended.</summary>
    public const double LateOutcome = 15.0;

    private static readonly string[] SlotOrder = { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" };

    private readonly Func<uint, string> actionName;
    private readonly Func<uint, string> statusName;

    /// <summary>Newest first; the live one (if any) is first.</summary>
    private readonly List<Fight> fights = new();

    private readonly Dictionary<uint, bool> down = new();
    private readonly List<PartyState> party = new();
    private readonly PartyEffect[] early = new PartyEffect[64];
    private int earlyCount;
    private int earlyNext;
    private Fight? live;
    private int nextId = 1;
    private int ownPull;
    private double now;
    private double outOfCombatSince = double.NaN;
    private bool inCombat;
    private bool anyAlive;
    private string duty = string.Empty;
    private uint content;
    private uint territory;
    private int recordedPull;
    private List<MeterEncounter>? kept;
    private (int Fight, int Member, MeterTab Tab, double At, IReadOnlyList<AbilityRow> Rows)? abilities;
    private (int Fight, PaceAgainst Against, double At, PaceLine? Line)? pace;

    /// <param name="actionName">An action's name (the game's Action sheet).</param>
    /// <param name="statusName">A status's name (the game's Status sheet).</param>
    public MeterSession(Func<uint, string>? actionName = null, Func<uint, string>? statusName = null)
    {
        this.actionName = actionName ?? (id => $"Action {id}");
        this.statusName = statusName ?? (id => $"Status {id}");
    }

    /// <summary>How many encounters are kept (Settings).</summary>
    public int Keep { get; set; } = 20;

    /// <summary>Nobody in the party in combat this long ends an encounter (Settings).</summary>
    public double EndAfter { get; set; } = 4;

    /// <summary>Why the meter has stopped (the game moved something it reads), or null.</summary>
    public string? Problem { get; set; }

    // ---------------------------------------------------------------------------------------------
    // What the game side tells it.

    /// <summary>
    /// Where the party is: the duty's name and ids (a new zone ends the encounter going on, and its
    /// pulls are counted from 1 again).
    /// </summary>
    public void Place(uint territoryId, uint contentId, string dutyName)
    {
        if (territoryId == territory && contentId == content && dutyName == duty) return;
        if (territoryId != territory)
        {
            if (live != null) End(Outcome.Ended);
            ownPull = 0;
        }
        territory = territoryId;
        content = contentId;
        duty = dutyName;
    }

    /// <summary>
    /// The recorder's pull going on (0 for none): an encounter that starts during it takes its number,
    /// and so does one that started a moment before it (the first hit can come a frame before the
    /// game's in-combat flag that starts the recorder's pull).
    /// </summary>
    public void RecordedPull(int pull)
    {
        recordedPull = pull;
        if (pull <= 0 || live is not { OwnCount: true } f || f.Pull == pull || now - f.Start > 10) return;
        ownPull = Math.Max(ownPull - 1, pull);
        f.Pull = pull;
        f.OwnCount = false;
        f.Changed();
        kept = null;
    }

    /// <summary>
    /// This frame: the time (seconds, the clock every event is timed by), the party, and whether any of
    /// them is in combat. Deaths (HP reaching 0) are seen here, and the encounter ends here.
    /// </summary>
    public void Frame(double t, ReadOnlySpan<PartyState> partyNow, bool anyInCombat)
    {
        now = t;
        inCombat = anyInCombat;
        party.Clear();
        var allDown = partyNow.Length > 0;
        foreach (var p in partyNow)
        {
            party.Add(p);
            var dead = p.Dead;
            if (!dead) allDown = false;
            var known = down.TryGetValue(p.Id, out var was);
            down[p.Id] = dead;
            if (live == null) continue;
            var m = live.Join(p);
            m.Dead = dead;
            if (known && !was && dead) live.Died(m, t, this);
        }
        anyAlive = !allDown;
        if (live == null) return;
        if (allDown)
        {
            End(Outcome.Wipe);
            return;
        }
        if (anyInCombat)
        {
            outOfCombatSince = double.NaN;
            return;
        }
        if (double.IsNaN(outOfCombatSince)) outOfCombatSince = t;
        else if (t - outOfCombatSince >= EndAfter) End(Outcome.Ended);
    }

    /// <summary>An enemy's targetability (report them all, then <see cref="EndTargets"/>): how phases are told.</summary>
    public void Targetable(uint enemy, bool on) => live?.Phases.Targetable(now - live.Start, enemy, on);

    public void EndTargets() => live?.Phases.EndFrame(now - live.Start);

    /// <summary>The enemies worth reporting outside a duty: those the party has fought this encounter.</summary>
    public bool Engaged(uint enemy) => live != null && live.Enemies.Contains(enemy);

    /// <summary>The duty said how it went: the encounter going on ends so, or the one just ended takes it.</summary>
    public void Said(Outcome how)
    {
        if (live != null)
        {
            End(how);
            return;
        }
        if (fights.Count == 0) return;
        var last = fights[0];
        if (last.Outcome is Outcome.Ended && now - last.EndedAt <= LateOutcome)
        {
            last.Outcome = how;
            last.MarkWipe();
            last.Changed();
            kept = null;
        }
    }

    /// <summary>Ends the encounter going on (a zone change, logging out, the meter turned off).</summary>
    public void Stop() => End(Outcome.Ended);

    private void End(Outcome how)
    {
        if (live is not { } f) return;
        f.End = Math.Max(f.Start, Math.Max(f.LastEvent, f.LastDeath));
        f.EndedAt = now;
        f.Outcome = how;
        f.MarkWipe();
        f.Changed();
        live = null;
        outOfCombatSince = double.NaN;
        kept = null;
    }

    private void Start(double t)
    {
        var pull = recordedPull > 0 ? recordedPull : ownPull + 1;
        ownPull = Math.Max(ownPull + (recordedPull > 0 ? 0 : 1), recordedPull);
        var f = new Fight(nextId++, pull, duty, content, t) { OwnCount = recordedPull <= 0 };
        foreach (var (who, slot) in Roles.Assign(party.Select(p => new Partier(p.Id, p.Job, p.Role)).ToList()))
            f.Add(party.First(p => p.Id == (uint)who.Key), slot);
        foreach (var p in party)
            if (f.ByEntity.TryGetValue(p.Id, out var m)) m.Dead = p.Dead;
        live = f;
        outOfCombatSince = double.NaN;
        fights.Insert(0, f);
        var keep = Math.Clamp(Keep, 1, 100);
        while (fights.Count > keep) fights.RemoveAt(fights.Count - 1);
        // The party's effects from the second before: an opener's buffs, shields and heals.
        for (var i = 0; i < earlyCount; i++)
        {
            var e = early[(earlyNext - earlyCount + i + early.Length) % early.Length];
            if (t - e.T <= Before) f.Effect(e with { T = Math.Max(t, e.T) }, this);
        }
        earlyCount = 0;
        kept = null;
    }

    // ---------------------------------------------------------------------------------------------
    // From the parser.

    public void Used(double t, uint member, uint caster, uint action)
    {
    }

    public void Effect(in PartyEffect e)
    {
        if (Problem != null) return;
        if (live == null)
        {
            if (e.Kind == FxKind.Damage && !e.RecipientParty)
            {
                Start(e.T);
            }
            else
            {
                early[earlyNext] = e;
                earlyNext = (earlyNext + 1) % early.Length;
                earlyCount = Math.Min(earlyCount + 1, early.Length);
                return;
            }
        }
        live!.Effect(e, this);
    }

    public void Taken(in TakenHit e)
    {
        if (Problem != null) return;
        if (live == null)
        {
            if (e.Amount <= 0 && (e.Fx & Effects.FxInstantDeath) == 0) return;
            Start(e.T);
        }
        live!.Taken(e, this);
    }

    public void Tick(in TickEvent e, ReadOnlySpan<TickShare> shares)
    {
        if (Problem != null || live == null) return;
        live.Tick(e, shares, this);
    }

    // ---------------------------------------------------------------------------------------------
    // What the window reads (IMeterSource).

    public MeterEncounter? Current => (live ?? (fights.Count > 0 ? fights[0] : null))?.Snapshot(this);

    public bool InEncounter => live != null;

    public bool Waiting => live == null && inCombat && anyAlive && Problem == null;

    public IReadOnlyList<MeterEncounter> Kept
    {
        get
        {
            if (live != null && (kept == null || kept.Count == 0 || !ReferenceEquals(kept[0], live.Snapshot(this)))) kept = null;
            return kept ??= fights.Select(f => f.Snapshot(this)).ToList();
        }
    }

    public MeterEncounter? Get(int id) => Find(id)?.Snapshot(this);

    private Fight? Find(int id)
    {
        foreach (var f in fights)
            if (f.Id == id)
                return f;
        return null;
    }

    public IReadOnlyList<AbilityRow> Abilities(int encounterId, int partyId, MeterTab tab)
    {
        if (abilities is { } a && a.Fight == encounterId && a.Member == partyId && a.Tab == tab
            && (Find(encounterId) is not { } stillLive || !stillLive.IsLive || now - a.At < RefreshEvery))
            return a.Rows;
        var f = Find(encounterId);
        IReadOnlyList<AbilityRow> rows = f == null ? Array.Empty<AbilityRow>() : f.Abilities(partyId, tab, this);
        abilities = (encounterId, partyId, tab, now, rows);
        return rows;
    }

    public MeterEncounter? Best(uint contentId) => BestFight(contentId, null)?.Snapshot(this);

    private Fight? BestFight(uint contentId, Fight? not) =>
        fights.Where(f => f != not && !f.IsLive && f.Content == contentId)
            .OrderByDescending(f => f.Outcome == Outcome.Clear)
            .ThenByDescending(f => f.Phases.Phase)
            .ThenByDescending(f => f.End - f.Start)
            .ThenByDescending(f => f.Id)
            .FirstOrDefault();

    /// <summary>
    /// Pace: the party's damage since the current phase began, against the compared encounter's at the
    /// same second of the same phase. Null when it isn't meaningful: pace off, another duty (or a place
    /// outside one), under <see cref="PaceFrom"/> seconds into the phase, or the other never reached
    /// this phase or left it (moved on, or ended) before this second of it.
    /// </summary>
    public PaceLine? Pace(int encounterId, PaceAgainst against)
    {
        if (pace is { } c && c.Fight == encounterId && c.Against == against && (Find(encounterId) is not { IsLive: true } || now - c.At < RefreshEvery))
            return c.Line;
        var line = WorkOutPace(encounterId, against);
        pace = (encounterId, against, now, line);
        return line;
    }

    private PaceLine? WorkOutPace(int encounterId, PaceAgainst against)
    {
        if (against == PaceAgainst.Off || Find(encounterId) is not { } f || f.Content == 0) return null;
        var other = against == PaceAgainst.Best
            ? BestFight(f.Content, f)
            : fights.Where(x => x != f && !x.IsLive && x.Content == f.Content && x.Id < f.Id).OrderByDescending(x => x.Id).FirstOrDefault();
        if (other == null) return null;
        var at = f.Duration(now);
        var (phase, start) = f.Phases.At(at);
        var into = at - start;
        if (into < PaceFrom || other.Phases.Starts.Count <= phase) return null;
        var otherStart = other.Phases.Starts[phase];
        var otherEnd = other.Phases.Starts.Count > phase + 1 ? other.Phases.Starts[phase + 1] : other.Duration(now);
        if (otherStart + into > otherEnd + 1e-9) return null;
        var mine = f.DamageBetween(start, start + into);
        var theirs = other.DamageBetween(otherStart, otherStart + into);
        if (theirs <= 0) return null;
        return new PaceLine(other.Pull, against, $"P{phase + 1}", into, mine, theirs, (double)mine / theirs - 1);
    }

    public void Clear()
    {
        fights.RemoveAll(f => f != live);
        abilities = null;
        pace = null;
        kept = null;
    }

    private string Name(AbilityKey key) => key.Id == 0 ? (key.Tick ? "Damage over time" : "Unknown") : key.IsStatus ? statusName(key.Id) : actionName(key.Id);

    private static int SlotRank(string slot)
    {
        var i = Array.IndexOf(SlotOrder, slot);
        if (i >= 0) return i;
        return slot.Length > 1 && slot[0] == 'P' && int.TryParse(slot.AsSpan(1), out var n) ? n : 1000;
    }

    // ---------------------------------------------------------------------------------------------

    private sealed class Member
    {
        public required int Id;
        public required uint Entity;
        public required string Name;
        public required string Job;
        public required string Role;
        public required string Slot;
        public bool You;
        public bool Dead;
        public double LastDied = double.NaN;
        public readonly Recap[] Recent = new Recap[RecapLength];
        public int RecentCount;
        public int RecentNext;

        public void Remember(in Recap r)
        {
            Recent[RecentNext] = r;
            RecentNext = (RecentNext + 1) % Recent.Length;
            RecentCount = Math.Min(RecentCount + 1, Recent.Length);
        }

        public Recap[] RecentInOrder()
        {
            var list = new Recap[RecentCount];
            for (var i = 0; i < RecentCount; i++) list[i] = Recent[(RecentNext - RecentCount + i + Recent.Length) % Recent.Length];
            return list;
        }
    }

    /// <summary>A line of a recap as it's kept: when (session time), what (an action or a status), the amount (hits negative), HP before.</summary>
    private readonly record struct Recap(double T, AbilityKey What, long Amount, long HpBefore, string Note, bool Hit);

    private sealed class Death
    {
        public required Member Who;
        public required double T;
        public required Recap[] Recent;
        public bool PartOfWipe;
    }

    private sealed class Fight
    {
        public readonly int Id;
        public int Pull;

        /// <summary>Numbered by the meter's own count (no recorded pull when it began).</summary>
        public bool OwnCount;
        public readonly string Duty;
        public readonly uint Content;
        public readonly double Start;
        public double End = double.NaN;
        public double EndedAt;
        public Outcome? Outcome;
        public double LastEvent;
        public double LastDeath;
        public readonly List<Member> Members = new();
        public readonly Dictionary<uint, Member> ByEntity = new();
        public readonly HashSet<uint> Enemies = new();
        public readonly Tally Tally = new();
        public readonly PhaseClock Phases = new();
        public readonly List<Death> Deaths = new();

        /// <summary>The party's damage in each second of the encounter (the limit break's too), for pace.</summary>
        private readonly List<long> perSecond = new();

        private MeterEncounter? snapshot;
        private double snapshotAt = double.NegativeInfinity;

        public Fight(int id, int pull, string duty, uint content, double start)
        {
            Id = id;
            Pull = pull;
            Duty = duty;
            Content = content;
            Start = start;
            LastEvent = start;
            LastDeath = start;
        }

        public bool IsLive => double.IsNaN(End);

        public double Duration(double now) => Math.Max(0, (IsLive ? now : End) - Start);

        public bool Stale(double now) => IsLive && now - snapshotAt >= RefreshEvery;

        public void Changed() => snapshotAt = double.NegativeInfinity;

        public Member Add(PartyState p, string slot)
        {
            var m = new Member { Id = Members.Count + 1, Entity = p.Id, Name = p.Name, Job = p.Job, Role = Roles.RoleWord(p.Role), Slot = slot, You = p.You };
            Members.Add(m);
            ByEntity[p.Id] = m;
            return m;
        }

        /// <summary>A party member in this frame's list: one who joined mid-encounter takes a free spot (or P9 up).</summary>
        public Member Join(PartyState p)
        {
            if (ByEntity.TryGetValue(p.Id, out var m))
            {
                if (m.Name.Length == 0 && p.Name.Length > 0) m.Name = p.Name;
                return m;
            }
            var taken = Members.Select(x => x.Slot).ToHashSet();
            var wanted = p.Role switch
            {
                1 => new[] { "MT", "OT" },
                4 => new[] { "H1", "H2" },
                2 => new[] { "M1", "M2", "R1", "R2" },
                3 => new[] { "R1", "R2", "M1", "M2" },
                _ => Array.Empty<string>(),
            };
            var slot = wanted.Concat(SlotOrder).FirstOrDefault(s => !taken.Contains(s));
            if (slot == null)
            {
                var n = 9;
                while (taken.Contains($"P{n}")) n++;
                slot = $"P{n}";
            }
            return Add(p, slot);
        }

        public void Died(Member m, double t, MeterSession s)
        {
            m.LastDied = t - Start;
            LastDeath = Math.Max(LastDeath, t);
            Deaths.Add(new Death { Who = m, T = t - Start, Recent = m.RecentInOrder() });
        }

        /// <summary>A wiped encounter: the deaths within <see cref="WipeWindow"/> of the last are the wipe.</summary>
        public void MarkWipe()
        {
            var last = Deaths.Count == 0 ? double.NaN : Deaths.Max(d => d.T);
            foreach (var d in Deaths) d.PartOfWipe = Outcome == Core.Outcome.Wipe && d.T >= last - WipeWindow;
        }

        private void Damage(double t, long amount)
        {
            var second = (int)Math.Clamp(Math.Floor(t - Start), 0, 24 * 3600);
            while (perSecond.Count <= second) perSecond.Add(0);
            perSecond[second] += amount;
        }

        /// <summary>The party's damage between two times of the encounter (to the second).</summary>
        public long DamageBetween(double from, double to)
        {
            long sum = 0;
            var a = (int)Math.Max(0, Math.Floor(from));
            var b = (int)Math.Min(perSecond.Count, Math.Floor(to));
            for (var i = a; i < b; i++) sum += perSecond[i];
            return sum;
        }

        private Member? Of(uint entity) => ByEntity.TryGetValue(entity, out var m) ? m : null;

        /// <summary>A party member by entity id: one in this frame's party list who isn't in the encounter yet joins it.</summary>
        private Member? Of(uint entity, MeterSession s)
        {
            if (ByEntity.TryGetValue(entity, out var m)) return m;
            foreach (var p in s.party)
                if (p.Id == entity)
                    return Join(p);
            return null;
        }

        public void Effect(in PartyEffect e, MeterSession s)
        {
            if (Of(e.Member, s) is not { } m) return;
            switch (e.Kind)
            {
                case FxKind.Damage:
                    Tally.Damage(m.Id, e.T - Start, e.Action, e.Amount, e.Flags);
                    Damage(e.T, e.Amount);
                    Enemies.Add(e.Recipient);
                    LastEvent = Math.Max(LastEvent, e.T);
                    break;
                case FxKind.Heal:
                    Tally.Heal(m.Id, e.Action, e.Amount, e.Overheal, e.Flags);
                    if (e.RecipientParty && Of(e.Recipient, s) is { } healed)
                        healed.Remember(new Recap(e.T, new AbilityKey(e.Action, false, (e.Flags & FxFlags.Pet) != 0, false), e.Amount, e.HpBefore,
                            (e.Flags & FxFlags.Pet) != 0 ? $"{m.Name}'s pet" : m.Name, false));
                    break;
                case FxKind.Shield:
                    Tally.Shield(m.Id);
                    if (e.RecipientParty && Of(e.Recipient, s) is { } shielded)
                        shielded.Remember(new Recap(e.T, new AbilityKey(e.Status, false, false, true), 0, e.HpBefore, $"Shield from {m.Name}", false));
                    break;
            }
        }

        public void Taken(in TakenHit e, MeterSession s)
        {
            if (Of(e.Target, s) is not { } m) return;
            Tally.Taken(m.Id, e.Action, false, e.Amount);
            Enemies.Add(e.Source);
            LastEvent = Math.Max(LastEvent, e.T);
            var note = (e.Fx & Effects.FxInstantDeath) != 0 ? "Instant death" : (e.Fx & Effects.FxCrit) != 0 ? "Critical" : string.Empty;
            m.Remember(new Recap(e.T, new AbilityKey(e.Action, false, false, false), -e.Amount, e.HpBefore, note, true));
        }

        public void Tick(in TickEvent e, ReadOnlySpan<TickShare> shares, MeterSession s)
        {
            if (e.TargetParty && Of(e.Target, s) is { } on)
            {
                if (!e.Heal)
                {
                    Tally.Taken(on.Id, e.Status, true, e.Amount);
                    LastEvent = Math.Max(LastEvent, e.T);
                    on.Remember(new Recap(e.T, new AbilityKey(e.Status, true, false, true), -e.Amount, e.HpBefore, "Over time", true));
                }
                else
                {
                    var by = e.SourceParty && Of(e.Source) is { } healer ? $"{healer.Name}, over time" : "Over time";
                    on.Remember(new Recap(e.T, new AbilityKey(e.Status, true, false, true), e.Amount, e.HpBefore, by, false));
                }
            }
            var over = e.Overheal;
            foreach (var share in shares)
            {
                if (Of(share.Member, s) is not { } m) continue;
                if (e.Heal)
                {
                    var part = e.Amount == 0 ? 0 : over * share.Amount / e.Amount;
                    Tally.Heal(m.Id, share.Action, share.Amount, part, FxFlags.None, true, share.Status, share.Estimated);
                }
                else
                {
                    Tally.Damage(m.Id, e.T - Start, share.Action, share.Amount, FxFlags.None, true, share.Status, share.Estimated);
                    Damage(e.T, share.Amount);
                    Enemies.Add(e.Target);
                    LastEvent = Math.Max(LastEvent, e.T);
                }
            }
        }

        public MeterEncounter Snapshot(MeterSession s)
        {
            if (snapshot != null && !(IsLive && s.now - snapshotAt >= RefreshEvery) && !double.IsNegativeInfinity(snapshotAt)) return snapshot;
            snapshotAt = s.now;
            snapshot = Build(s);
            return snapshot;
        }

        private MeterEncounter Build(MeterSession s)
        {
            var duration = Duration(s.now);
            var clock = Math.Max(1, duration);
            var rows = new List<MeterRow>(Members.Count + 1);
            foreach (var m in Members.OrderBy(x => SlotRank(x.Slot)).ThenBy(x => x.Id))
                rows.Add(Row(m.Id, m.Slot, m.Name, m.Job, m.Role, m.You, false, Tally.Has(m.Id) ? Tally.Of(m.Id) : new MemberTotals(), clock, s,
                    Deaths.Count(d => d.Who == m && !d.PartOfWipe), m.Dead, double.IsNaN(m.LastDied) ? null : m.LastDied));
            if (Tally.LimitBreakUsed)
                rows.Add(Row(-1, "LB", "Limit Break", string.Empty, string.Empty, false, true, Tally.LimitBreak, clock, s, 0, false, null));
            var party = PartyRow(rows, clock);
            var (phase, phaseAt) = Phases.At(duration);
            var deaths = Deaths.Select(d => DeathOf(d, s)).ToList();
            return new MeterEncounter(Id, Pull, Duty, Content, Start, duration, IsLive, Outcome, duration, $"P{phase + 1}", phaseAt, rows, party, deaths);
        }

        private MeterRow Row(int id, string slot, string name, string job, string role, bool you, bool lb, MemberTotals t, double clock, MeterSession s,
            int deaths, bool dead, double? diedAt)
        {
            var encDps = t.Damage / clock;
            // The limit break has no time of its own: its DPS is its ENCDPS (the window shows a dash).
            var dps = lb ? encDps : t.Damage / Math.Max(1, t.Active);
            var healed = t.Healing + t.Overheal;
            return new MeterRow(id, slot, name, job, role, you, lb,
                t.Damage, encDps, dps, Tally.PartyDamage == 0 ? 0 : (double)t.Damage / Tally.PartyDamage, t.CritRate, t.DirectHitRate, t.MaxHit,
                t.MaxHit > 0 ? s.Name(t.MaxHitBy) : string.Empty,
                t.Healing, t.Overheal, t.Healing / clock, healed == 0 ? 0 : (double)t.Overheal / healed,
                Tally.PartyHealing == 0 ? 0 : (double)t.Healing / Tally.PartyHealing, t.HealCritRate, t.MaxHeal,
                t.Taken, t.Taken / clock, Tally.PartyTaken == 0 ? 0 : (double)t.Taken / Tally.PartyTaken, t.MaxTaken,
                deaths, dead, diedAt, lb ? 0 : t.Active);
        }

        private MeterRow PartyRow(List<MeterRow> rows, double clock)
        {
            var hits = 0;
            var crits = 0;
            var dhs = 0;
            var heals = 0;
            var healCrits = 0;
            foreach (var t in Tally.Members.Values)
            {
                hits += t.Hits;
                crits += t.Crits;
                dhs += t.DirectHits;
                heals += t.Heals;
                healCrits += t.HealCrits;
            }
            var best = rows.Count == 0 ? null : rows.MaxBy(r => r.MaxHit);
            var healed = Tally.PartyHealing + Tally.PartyOverheal;
            return new MeterRow(0, string.Empty, "Party", string.Empty, string.Empty, false, false,
                Tally.PartyDamage, Tally.PartyDamage / clock, rows.Sum(r => r.Dps), Tally.PartyDamage == 0 ? 0 : 1,
                hits == 0 ? 0 : (double)crits / hits, hits == 0 ? 0 : (double)dhs / hits, best?.MaxHit ?? 0, best?.MaxHitName ?? string.Empty,
                Tally.PartyHealing, Tally.PartyOverheal, Tally.PartyHealing / clock, healed == 0 ? 0 : (double)Tally.PartyOverheal / healed,
                Tally.PartyHealing == 0 ? 0 : 1, heals == 0 ? 0 : (double)healCrits / heals, rows.Count == 0 ? 0 : rows.Max(r => r.MaxHeal),
                Tally.PartyTaken, Tally.PartyTaken / clock, Tally.PartyTaken == 0 ? 0 : 1, rows.Count == 0 ? 0 : rows.Max(r => r.MaxTaken),
                rows.Sum(r => r.Deaths), Members.Count > 0 && Members.All(m => m.Dead), null, Math.Max(0, clock));
        }

        private DeathEntry DeathOf(Death d, MeterSession s)
        {
            var recap = d.Recent.Select(r => new RecapLine(r.T - Start, s.Name(r.What), r.Amount, r.HpBefore, r.Note)).ToList();
            // The killing blow: the last hit before it.
            Recap? blow = null;
            for (var i = d.Recent.Length - 1; i >= 0; i--)
                if (d.Recent[i].Hit)
                {
                    blow = d.Recent[i];
                    break;
                }
            var amount = blow is { } b ? -b.Amount : 0;
            var overkill = blow is { } k && k.HpBefore >= 0 ? Math.Max(0, amount - k.HpBefore) : 0;
            return new DeathEntry(d.Who.Id, d.Who.Slot, d.Who.Name, d.T, blow is { } w ? s.Name(w.What) : "Unknown", amount, overkill, d.PartOfWipe, recap);
        }

        public IReadOnlyList<AbilityRow> Abilities(int partyId, MeterTab tab, MeterSession s)
        {
            MemberTotals? t = partyId == -1 ? Tally.LimitBreak : Tally.Has(partyId) ? Tally.Of(partyId) : null;
            if (t == null) return Array.Empty<AbilityRow>();
            var by = tab switch
            {
                MeterTab.Damage => t.DamageBy,
                MeterTab.Healing => t.HealingBy,
                MeterTab.Taken => t.TakenBy,
                _ => null,
            };
            if (by == null || by.Count == 0) return Array.Empty<AbilityRow>();
            var total = by.Values.Sum(x => x.Amount);
            return by.OrderByDescending(kv => kv.Value.Amount)
                .Select(kv => new AbilityRow(kv.Key.IsStatus ? 0 : kv.Key.Id, s.Name(kv.Key), kv.Key.Tick, kv.Key.Pet, kv.Value.Amount,
                    total == 0 ? 0 : (double)kv.Value.Amount / total, kv.Value.Hits,
                    kv.Key.Tick || tab == MeterTab.Taken || kv.Value.Hits == 0 ? null : (double)kv.Value.Crits / kv.Value.Hits,
                    kv.Key.Tick || tab != MeterTab.Damage || kv.Value.Hits == 0 ? null : (double)kv.Value.DirectHits / kv.Value.Hits,
                    kv.Value.Max, kv.Value.Estimated))
                .ToList();
        }
    }
}
