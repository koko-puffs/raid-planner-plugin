using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPlanner.Core.Meter;

/// <summary>A party member as the game shows them this frame (the meter's names stay in the plugin).</summary>
/// <param name="Role">The game's ClassJob role: 1 tank, 2 melee, 3 ranged, 4 healer.</param>
/// <param name="Weakness">Since 0.4.0: Weakness (1) or Brink of Death (2) on them now, 0 neither.</param>
public readonly record struct PartyState(uint Id, string Name, string Job, byte Role, long Hp, long MaxHp, bool InCombat, bool You, byte Weakness = 0)
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
/// duty says wiped or completed, or on leaving the zone. Its length (what ENCDPS, HPS and damage taken
/// a second are over) runs from that first damage to the end: a wipe's last death, else the last damage
/// between the party and an enemy, ticks included (ACT's convention): the one rule the summary's
/// <c>stats.len</c> and the site's numbers follow too (<see cref="PullStats.Length"/>). Phases are the
/// recording's rule (<see cref="PhaseClock"/>), from the enemies' targetability the game side reports.
///
/// Since 0.4.0 also what the party used (<see cref="Use"/>: GCDs, items, limit breaks, interrupts, a status
/// resisted; the potions in the <see cref="PrePull.Keep"/> seconds before an encounter are its own, as the
/// summary's <c>pots</c> count them), raises (a dead member's HP above 0 again: credited as the recording
/// credits them, to the Raise status a party member put on them while down, else the healers' limit break 3,
/// else the last raise used on them while down), the enemies' casts (<see cref="EnemyCast"/>) and their being stopped
/// (<see cref="Cancelled"/>, credited as the recording credits them: <see cref="CastWatch"/>), the bosses'
/// targets (<see cref="Aggro"/>: tank swaps, a non-tank holding a boss), stuns and the like landing (a control
/// status a party member's action put on someone outside the party, its message's own status effect, with damage or
/// not: <see cref="StatusOn"/>), and the limit break gauge (<see cref="Gauge"/>), as each encounter's <see cref="Events"/> and the rows' counts.
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

    /// <summary>The most events kept for an encounter (the newest so many: a long pull's feed goes on to its end).</summary>
    public const int MostEvents = 200;

    private readonly Func<uint, string> actionName;
    private readonly Func<uint, string> statusName;
    private readonly Func<uint, string> itemName;
    private readonly CombatKinds kinds;

    /// <summary>Newest first; the live one (if any) is first.</summary>
    private readonly List<Fight> fights = new();

    private readonly Dictionary<uint, bool> down = new();
    private readonly List<PartyState> party = new();
    private readonly PartyEffect[] early = new PartyEffect[64];
    private readonly PartyUse[] earlyUses = new PartyUse[32];

    // Items used out of an encounter (the last few): those in the PrePull.Keep seconds before one begins are its own.
    private readonly PartyUse[] earlyItems = new PartyUse[16];
    private int earlyItemCount;
    private int earlyItemNext;
    private int earlyUseCount;
    private int earlyUseNext;
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
    // The last breakdown and pace worked out, with when and whether their encounter was live then: one
    // worked out live is worked out again a moment later (and once more after its end), one worked out
    // after its end is kept as it is.
    private (int Fight, int Member, MeterTab Tab, double At, bool Live, IReadOnlyList<AbilityRow> Rows)? abilities;
    private (int Fight, PaceAgainst Against, double At, bool Live, PaceLine? Line)? pace;

    /// <param name="actionName">An action's name (the game's Action sheet).</param>
    /// <param name="statusName">A status's name (the game's Status sheet).</param>
    /// <param name="itemName">An item's name by the id the game sent (a high-quality one's +1,000,000).</param>
    /// <param name="kinds">Which actions and statuses are GCDs, interrupts, raises, control (<see cref="CombatKinds"/>).</param>
    public MeterSession(Func<uint, string>? actionName = null, Func<uint, string>? statusName = null, Func<uint, string>? itemName = null,
        CombatKinds? kinds = null)
    {
        this.actionName = actionName ?? (id => $"Action {id}");
        this.statusName = statusName ?? (id => $"Status {id}");
        this.itemName = itemName ?? (id => $"Item {id}");
        this.kinds = kinds ?? CombatKinds.None;
    }

    /// <summary>The limit break gauge now (<see cref="Gauge"/>), or null outside a party that fills one.</summary>
    public LimitBreakGauge? LimitBreak { get; private set; }

    /// <summary>
    /// The limit break gauge as the game has it: the units in it, the bars the party has, the units a bar (Settings' gauge,
    /// the Events tab's limit breaks). No bars: no gauge.
    /// </summary>
    public void Gauge(int units, int bars, int barUnits)
    {
        if (bars <= 0 || barUnits <= 0)
        {
            LimitBreak = null;
            return;
        }
        units = Math.Clamp(units, 0, bars * barUnits);
        var full = Math.Min(bars, units / barUnits);
        var into = full >= bars ? 0 : units - full * barUnits;
        if (LimitBreak is { } g && g.Bars == full && g.MaxBars == bars && g.Units == into && g.BarUnits == barUnits) return;
        LimitBreak = new LimitBreakGauge(full, bars, into, barUnits);
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
            // A potion drunk in another zone is no pull's here.
            earlyItemCount = 0;
            // Who's down is read afresh (a member out of sight isn't kept dead from the last zone).
            down.Clear();
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
    /// them is in combat. Deaths (HP reaching 0) are seen here, and the encounter ends here. A member whose HP isn't
    /// known (<see cref="PartyState.MaxHp"/> 0) is taken to be as they were last seen (alive when never seen), and
    /// isn't counted in "everyone's down" (the recorder's rule: those whose HP is known, at least one).
    /// </summary>
    public void Frame(double t, ReadOnlySpan<PartyState> partyNow, bool anyInCombat)
    {
        now = t;
        inCombat = anyInCombat;
        party.Clear();
        // Everyone down: every member whose HP is known this frame (at least one), as the recorder's pull tells a wipe.
        var allDown = true;
        var anyKnown = false;
        foreach (var p in partyNow)
        {
            party.Add(p);
            var known = down.TryGetValue(p.Id, out var was);
            // HP not known this frame (MaxHp 0: since 0.5.0 a cross-world member who isn't near): as they were last seen, so a
            // member out of sight is neither raised nor killed again when they're seen once more.
            var dead = p.MaxHp <= 0 && known ? was : p.Dead;
            if (p.MaxHp > 0)
            {
                anyKnown = true;
                if (!dead) allDown = false;
            }
            down[p.Id] = dead;
            if (live == null) continue;
            var m = live.Join(p);
            m.Dead = dead;
            if (p.MaxHp > 0) m.MaxHp = p.MaxHp;
            if (!dead) m.Weakness = p.Weakness;
            if (known && !was && dead) live.Died(m, t, this);
            else if (known && was && !dead) live.Raised(m, t);
        }
        allDown &= anyKnown;
        live?.Watch(t, this);
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
            last.End = last.EndOf();
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
        f.Outcome = how;
        f.End = f.EndOf();
        f.EndedAt = now;
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
            if (f.ByEntity.TryGetValue(p.Id, out var m)) m.Dead = down.TryGetValue(p.Id, out var was) ? was : p.Dead;
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
        for (var i = 0; i < earlyUseCount; i++)
        {
            var u = earlyUses[(earlyUseNext - earlyUseCount + i + earlyUses.Length) % earlyUses.Length];
            if (t - u.T <= Before) f.Use(u with { T = Math.Max(t, u.T) }, this);
        }
        earlyUseCount = 0;
        // The potions drunk before it (the pre-pull tincture): its own, at their own (negative) times.
        for (var i = 0; i < earlyItemCount; i++)
        {
            var u = earlyItems[(earlyItemNext - earlyItemCount + i + earlyItems.Length) % earlyItems.Length];
            if (t - u.T <= PrePull.Keep && u.T <= t) f.Use(u, this);
        }
        earlyItemCount = 0;
        kept = null;
    }

    // ---------------------------------------------------------------------------------------------
    // From the parser.

    public void Used(double t, uint member, uint caster, uint action)
    {
    }

    /// <summary>A party member's action or item (since 0.4.0): counted in the encounter going on, and its events.</summary>
    public void Use(in PartyUse u)
    {
        if (Problem != null) return;
        if (live == null)
        {
            // An item kept longer (the pre-pull tincture is the pull's); anything else a moment, as the effects are: a use in
            // the second before the first hit is the encounter's.
            if ((u.Flags & UseFlags.Item) != 0)
            {
                earlyItems[earlyItemNext] = u;
                earlyItemNext = (earlyItemNext + 1) % earlyItems.Length;
                earlyItemCount = Math.Min(earlyItemCount + 1, earlyItems.Length);
                return;
            }
            earlyUses[earlyUseNext] = u;
            earlyUseNext = (earlyUseNext + 1) % earlyUses.Length;
            earlyUseCount = Math.Min(earlyUseCount + 1, earlyUses.Length);
            return;
        }
        live.Use(u, this);
    }

    /// <summary>
    /// An enemy's cast as it began (the game side reads the cast bars): kept until its end, so a cast stopped can be told
    /// (<see cref="Cancelled"/>), and an interruptible one that went off is an event.
    /// </summary>
    public void EnemyCast(double t, uint enemy, uint action, float duration, bool interruptible)
    {
        if (Problem != null || live == null || enemy == 0 || action == 0 || !double.IsFinite(t)) return;
        live.EnemyCast(t, enemy, action, float.IsFinite(duration) ? duration : 0, interruptible);
    }

    /// <summary>
    /// The game said an enemy's cast stopped (actor control 15; <paramref name="interrupted"/>, else cancelled): one the
    /// meter holds, before its end less a quarter second, is an Interrupt event, credited to <paramref name="byMember"/>
    /// (an entity id) or, with 0, as the recording credits it (<see cref="CastWatch"/>).
    /// </summary>
    public void Cancelled(double t, uint enemy, uint action, bool interrupted, uint byMember = 0)
    {
        if (Problem != null || live == null) return;
        live.Cancelled(t, enemy, action, interrupted, byMember, this);
    }

    /// <summary>
    /// A boss's target now (an entity id, 0 none), reported while it's targetable; <paramref name="castTarget"/> when it's
    /// casting at that one (a tankbuster's target, not aggro). Tank swaps and aggro on a non-tank are told from it.
    /// </summary>
    public void Aggro(double t, uint enemy, uint target, bool castTarget = false)
    {
        if (Problem != null || live == null || enemy == 0) return;
        live.Aggro(t, enemy, target, castTarget, this);
    }

    /// <summary>An encounter's events (the Events tab), oldest first, the newest <see cref="MostEvents"/>.</summary>
    public IReadOnlyList<MeterEvent> Events(int encounterId) => Find(encounterId)?.EventList(this) ?? (IReadOnlyList<MeterEvent>)Array.Empty<MeterEvent>();

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

    /// <summary>
    /// A status a party member put on (with damage or not: Shield Bash's stun too): a stun (sleep, bind…) on someone outside the
    /// party is one landed (the game side reads whether a target is hostile only when it needs its HP, so not for an enemy the
    /// party only puts a status on), and the Raise status on a member who's down is what their getting up is credited to.
    /// </summary>
    public void StatusOn(in PartyEffect e)
    {
        if (Problem != null || live == null || e.Kind != FxKind.Status) return;
        if (!e.RecipientParty && kinds.Control(e.Status)) live.Control(e, this);
        else if (e.RecipientParty && kinds.RaiseStatus(e.Status)) live.RaiseStatus(e, this);
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
        var f = Find(encounterId);
        if (abilities is { } a && a.Fight == encounterId && a.Member == partyId && a.Tab == tab && Fresh(f, a.At, a.Live))
            return a.Rows;
        IReadOnlyList<AbilityRow> rows = f == null ? Array.Empty<AbilityRow>() : f.Abilities(partyId, tab, this);
        abilities = (encounterId, partyId, tab, now, f is { IsLive: true }, rows);
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
        var f = Find(encounterId);
        if (pace is { } c && c.Fight == encounterId && c.Against == against && Fresh(f, c.At, c.Live)) return c.Line;
        var line = WorkOutPace(encounterId, against);
        pace = (encounterId, against, now, f is { IsLive: true }, line);
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

    /// <summary>
    /// Is what was worked out for an encounter at <paramref name="at"/> still right: it's gone (nothing
    /// to work out again), or it was over then (and so is now), or it's live and that was a moment ago.
    /// One worked out while live and over since is worked out again, with its last moments.
    /// </summary>
    private bool Fresh(Fight? f, double at, bool wasLive) => f == null || (!wasLive && !f.IsLive) || (f.IsLive && now - at < RefreshEvery);

    public void Clear()
    {
        fights.RemoveAll(f => f != live);
        abilities = null;
        pace = null;
        kept = null;
    }

    /// <summary>A member's name for the events (their job when the party list gave none).</summary>
    private static string Who(Member? m) => m == null ? "Someone" : m.Name.Length > 0 ? m.Name : m.Job.Length > 0 ? m.Job : m.Slot;

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

        /// <summary>Their max HP as last seen (the party list's; 0 not known): what a death's recap is drawn against.</summary>
        public long MaxHp;

        public double LastDied = double.NaN;

        // Since 0.4.0: GCDs, items, raises given and taken, interrupts; Weakness or Brink on them now; the last raise on them while down.
        public int Casts;
        public int Potions;
        public int RaisesGiven;
        public int Raised;
        public int Interrupts;
        public byte Weakness;
        public (double T, Member By, bool Lb)? RaisedWith;

        /// <summary>The Raise status a party member put on them while down (when, by whom): what their getting up is credited to first.</summary>
        public (double T, Member By)? RaiseStatus;
        public Death? LastDeath;

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

    /// <summary>A line of a recap as it's kept: when (session time), what (an action or a status), the amount (hits negative), HP before; an instant death.</summary>
    private readonly record struct Recap(double T, AbilityKey What, long Amount, long HpBefore, string Note, bool Hit, bool Instant = false);

    private sealed class Death
    {
        public required Member Who;
        public required double T;
        public required Recap[] Recent;

        /// <summary>Their max HP as they died (0 not known).</summary>
        public long MaxHp;

        public bool PartOfWipe;

        // Since 0.4.0: when they were alive again (encounter time), by whom; Weakness or Brink on them as they died.
        public double? RaisedAt;
        public Member? RaisedBy;
        public bool HadWeakness;
    }

    /// <summary>An event as it's kept (the text is made when it's read: a member's name may come later).</summary>
    private readonly record struct Happening(double T, MeterEventKind Kind, Member? Who, Member? Other, uint Action, uint Status, Death? Death, double Seconds = 0);

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

        // Since 0.4.0: the events; the enemies' casts going on; who interrupted; each boss's target as a span, and the tank holding it.
        // The events in a ring (the newest MostEvents, nothing made per event), and how many there have been (what a list is made again by).
        private readonly Happening[] happenings = new Happening[MostEvents];
        private int happeningNext;
        private int happeningCount;
        private long happened;
        private readonly Dictionary<uint, (double T, uint Action, double End, bool Interruptible)> casts = new();
        private readonly CastWatch interrupts = new();
        private readonly Dictionary<uint, (uint On, double Since, bool CastTarget, bool Judged, Member? Held)> aggro = new();
        private readonly List<uint> judging = new();
        private IReadOnlyList<MeterEvent>? eventList;
        private long eventCount = -1;

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

        /// <summary>When it ended, by its outcome: a wipe at its last death, else at the last damage between the party and an enemy.</summary>
        public double EndOf() => Math.Max(Start, Outcome == Core.Outcome.Wipe && Deaths.Count > 0 ? LastDeath : LastEvent);

        public double Duration(double now) => Math.Max(0, (IsLive ? now : End) - Start);

        public bool Stale(double now) => IsLive && now - snapshotAt >= RefreshEvery;

        public void Changed() => snapshotAt = double.NegativeInfinity;

        public Member Add(PartyState p, string slot)
        {
            var m = new Member
            {
                Id = Members.Count + 1, Entity = p.Id, Name = p.Name, Job = p.Job, Role = Roles.RoleWord(p.Role), Slot = slot, You = p.You,
                MaxHp = Math.Max(0, p.MaxHp),
            };
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
            var d = new Death { Who = m, T = t - Start, Recent = m.RecentInOrder(), MaxHp = m.MaxHp, HadWeakness = m.Weakness != 0 };
            Deaths.Add(d);
            m.LastDeath = d;
            m.RaisedWith = null;
            m.RaiseStatus = null;
            Happen(new Happening(t - Start, MeterEventKind.Death, m, null, 0, 0, d));
        }

        // -----------------------------------------------------------------------------------------
        // Since 0.4.0.

        private void Happen(in Happening h)
        {
            // The oldest let go of once there are MostEvents: the live feed shows the newest.
            happenings[happeningNext] = h;
            happeningNext = (happeningNext + 1) % happenings.Length;
            happeningCount = Math.Min(happeningCount + 1, happenings.Length);
            happened++;
        }

        /// <summary>
        /// A dead member alive again, credited as the recording credits it (<c>RecordingBuilder.Raised</c>): to the Raise status a
        /// party member put on them while down, else to the last raise used on them while down (or the healers' limit break 3).
        /// </summary>
        public void Raised(Member m, double t)
        {
            var use = m.RaisedWith;
            var by = m.RaiseStatus?.By ?? use?.By;
            m.RaisedWith = null;
            m.RaiseStatus = null;
            m.Raised++;
            if (by != null) by.RaisesGiven++;
            if (m.LastDeath is { RaisedAt: null } d)
            {
                d.RaisedAt = t - Start;
                d.RaisedBy = by;
            }
            Happen(new Happening(t - Start, MeterEventKind.Raise, m, by, 0, 0, null, use is { Lb: true } ? 1 : 0));
        }

        /// <summary>The Raise status put on a member who's down, by a party member [verify: the game names the raiser as its source].</summary>
        public void RaiseStatus(in PartyEffect e, MeterSession s)
        {
            if (Of(e.Recipient) is { Dead: true } on && Of(e.Member, s) is { } by) on.RaiseStatus = (e.T, by);
        }

        public void Use(in PartyUse u, MeterSession s)
        {
            if (Of(u.Member, s) is not { } m) return;
            var t = u.T - Start;
            if ((u.Flags & UseFlags.Item) != 0)
            {
                // One before the encounter (the pre-pull tincture) at its own time, before 0.
                m.Potions++;
                Happen(new Happening(t, MeterEventKind.Potion, m, null, u.Action, 0, null));
                return;
            }
            if ((u.Flags & UseFlags.Pet) == 0 && s.kinds.Gcd(u.Action)) m.Casts++;
            if ((u.Flags & UseFlags.LimitBreak) != 0) Happen(new Happening(t, MeterEventKind.LimitBreak, m, null, u.Action, 0, null));
            if ((u.Flags & UseFlags.StatusResisted) != 0) Happen(new Happening(t, MeterEventKind.Resisted, m, null, u.Action, 0, null));
            interrupts.Used(u.T, u.Target, u.Member, u.Action, (u.Flags & UseFlags.Interrupted) != 0, s.kinds.Interrupt(u.Action));
            // A raise on one down, or the healers' limit break 3 on all who are: what their getting up is credited to.
            if ((u.Flags & UseFlags.Revive) != 0)
            {
                foreach (var x in Members)
                    if (x.Dead) x.RaisedWith = (u.T, m, true);
            }
            else if (s.kinds.RaiseAction(u.Action) && Of(u.Target) is { Dead: true } on)
            {
                on.RaisedWith = (u.T, m, false);
            }
        }

        /// <summary>A stun (or sleep, bind…) a member put on an enemy.</summary>
        public void Control(in PartyEffect e, MeterSession s)
        {
            if (Of(e.Member, s) is { } m) Happen(new Happening(e.T - Start, MeterEventKind.Stun, m, null, e.Action, e.Status, null));
        }

        public void EnemyCast(double t, uint enemy, uint action, float duration, bool interruptible)
        {
            if (casts.TryGetValue(enemy, out var had) && had.Action == action && Math.Abs(had.T - t) < 0.3) return;
            casts[enemy] = (t, action, t + Math.Max(0, duration), interruptible);
        }

        public void Cancelled(double t, uint enemy, uint action, bool interrupted, uint byMember, MeterSession s)
        {
            if (!casts.TryGetValue(enemy, out var c) || c.Action != action || t >= c.End - RecordingBuilder.CancelBeforeEnd) return;
            casts.Remove(enemy);
            if (!interrupted) return;
            var by = byMember != 0 ? byMember : interrupts.Credit(t, enemy)?.Member ?? 0;
            var m = by == 0 ? null : Of(by, s);
            if (m != null) m.Interrupts++;
            Happen(new Happening(t - Start, MeterEventKind.Interrupt, m, null, action, 0, null));
        }

        public void Aggro(double t, uint enemy, uint target, bool castTarget, MeterSession s)
        {
            if (aggro.TryGetValue(enemy, out var a) && a.On == target)
            {
                // Still on them: a span that began as a cast's target and goes on after it is theirs.
                if (a.CastTarget && !castTarget) aggro[enemy] = a with { CastTarget = false, Since = t };
                return;
            }
            if (aggro.TryGetValue(enemy, out var was)) Judge(enemy, was, t, true);
            var held = aggro.TryGetValue(enemy, out var prev) ? prev.Held : null;
            aggro[enemy] = (target, t, castTarget, false, held);
        }

        /// <summary>
        /// A boss's span on someone, once it's held <see cref="PullStats.AggroSpan"/> (or it ends): a tank's after another
        /// tank's is a tank swap; a non-tank's (not a cast's target) is aggro lost, said when it ends with how long.
        /// </summary>
        private void Judge(uint enemy, (uint On, double Since, bool CastTarget, bool Judged, Member? Held) a, double t, bool ending)
        {
            if (a.CastTarget || t - a.Since < PullStats.AggroSpan || a.On == 0) return;
            var m = Of(a.On);
            if (m == null) return;
            if (m.Role == "tank")
            {
                if (a.Judged) return;
                if (a.Held != null && a.Held != m) Happen(new Happening(a.Since - Start, MeterEventKind.TankSwap, m, a.Held, 0, 0, null));
                aggro[enemy] = a with { Judged = true, Held = m };
                return;
            }
            if (ending) Happen(new Happening(a.Since - Start, MeterEventKind.AggroLost, m, null, 0, 0, null, t - a.Since));
        }

        /// <summary>Each frame: the bosses' spans held long enough, and the interruptible casts that went off.</summary>
        public void Watch(double t, MeterSession s)
        {
            // The keys copied first (Judge writes back), into a list kept for it: nothing made each frame.
            judging.Clear();
            foreach (var (enemy, a) in aggro)
                if (!a.Judged && !a.CastTarget && t - a.Since >= PullStats.AggroSpan)
                    judging.Add(enemy);
            foreach (var enemy in judging) Judge(enemy, aggro[enemy], t, false);
            if (casts.Count == 0) return;
            List<uint>? done = null;
            foreach (var (enemy, c) in casts)
            {
                if (t < c.End + RecordingBuilder.CancelBeforeEnd) continue;
                (done ??= new()).Add(enemy);
                if (c.Interruptible) Happen(new Happening(c.End - Start, MeterEventKind.CastWentOff, null, null, c.Action, 0, null));
            }
            if (done != null)
                foreach (var enemy in done) casts.Remove(enemy);
        }

        /// <summary>The events as the window reads them (made again when there are more, or a death became the wipe's).</summary>
        public IReadOnlyList<MeterEvent> EventList(MeterSession s)
        {
            var stamp = happened * 2 + (IsLive ? 0 : 1);
            if (eventList != null && eventCount == stamp && !IsLive) return eventList;
            if (eventList != null && eventCount == stamp && IsLive && s.now - snapshotAt < RefreshEvery) return eventList;
            eventCount = stamp;
            var kept = new List<Happening>(happeningCount);
            for (var i = 0; i < happeningCount; i++) kept.Add(happenings[(happeningNext - happeningCount + i + happenings.Length) % happenings.Length]);
            eventList = kept.OrderBy(h => h.T).Select(h => Line(h, s)).ToList();
            return eventList;
        }

        private MeterEvent Line(in Happening h, MeterSession s)
        {
            var who = MeterSession.Who(h.Who);
            var other = MeterSession.Who(h.Other);
            var text = h.Kind switch
            {
                MeterEventKind.Death when h.Death is { } d => $"{who} died to {DeathOf(d, s).KilledBy}" + (d.PartOfWipe ? " (the wipe)" : d.HadWeakness ? " with Weakness" : string.Empty),
                MeterEventKind.Death => $"{who} died",
                MeterEventKind.Raise => h.Other == null ? $"{who} was raised" : h.Seconds > 0 ? $"{who} was raised by {other}'s limit break" : $"{who} was raised by {other}",
                MeterEventKind.Interrupt => h.Who == null ? $"{s.actionName(h.Action)} was interrupted" : $"{who} interrupted {s.actionName(h.Action)}",
                MeterEventKind.CastWentOff => $"{s.actionName(h.Action)} went off",
                MeterEventKind.Stun => $"{who}'s {s.statusName(h.Status)} landed",
                MeterEventKind.Resisted => $"{who}'s {s.actionName(h.Action)} was resisted",
                MeterEventKind.Potion => h.T < 0 ? $"{who} used {s.itemName(h.Action)} before the pull" : $"{who} used {s.itemName(h.Action)}",
                MeterEventKind.LimitBreak => $"{who} used {s.actionName(h.Action)}",
                MeterEventKind.TankSwap => $"Tank swap: {other} to {who}",
                MeterEventKind.AggroLost => $"{who} held a boss for {h.Seconds:0.0}s",
                _ => string.Empty,
            };
            return new MeterEvent(h.T, h.Kind, h.Who?.Id ?? 0, h.Who?.Slot ?? string.Empty, text, h.Other?.Id ?? 0, h.Other?.Slot ?? string.Empty,
                h.Kind is MeterEventKind.Stun ? h.Status : h.Action);
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
            m.Remember(new Recap(e.T, new AbilityKey(e.Action, false, false, false), -e.Amount, e.HpBefore, note, true, (e.Fx & Effects.FxInstantDeath) != 0));
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
                    Deaths.Count(d => d.Who == m && !d.PartOfWipe), m.Dead, double.IsNaN(m.LastDied) ? null : m.LastDied, m, duration));
            if (Tally.LimitBreakUsed)
                rows.Add(Row(-1, "LB", "Limit Break", string.Empty, string.Empty, false, true, Tally.LimitBreak, clock, s, 0, false, null, null, duration));
            var party = PartyRow(rows, clock);
            var (phase, phaseAt) = Phases.At(duration);
            var deaths = Deaths.Select(d => DeathOf(d, s)).ToList();
            return new MeterEncounter(Id, Pull, Duty, Content, Start, duration, IsLive, Outcome, duration, $"P{phase + 1}", phaseAt, rows, party, deaths);
        }

        private MeterRow Row(int id, string slot, string name, string job, string role, bool you, bool lb, MemberTotals t, double clock, MeterSession s,
            int deaths, bool dead, double? diedAt, Member? m, double duration)
        {
            // Seconds dead: each death not the wipe's, to when they were up again (or the end, or now).
            var deadFor = m == null ? 0 : Deaths.Where(d => d.Who == m && !d.PartOfWipe).Sum(d => Math.Max(0, Math.Min(d.RaisedAt ?? duration, duration) - d.T));
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
                deaths, dead, diedAt, lb ? 0 : t.Active,
                m?.Casts ?? 0, m?.Potions ?? 0, m?.RaisesGiven ?? 0, m?.Raised ?? 0, deadFor, m?.Interrupts ?? 0);
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
                rows.Sum(r => r.Deaths), Members.Count > 0 && Members.All(m => m.Dead), null, Math.Max(0, clock),
                rows.Sum(r => r.Casts), rows.Sum(r => r.Potions), rows.Sum(r => r.RaisesGiven), rows.Sum(r => r.Raised), rows.Sum(r => r.DeadSeconds),
                rows.Sum(r => r.Interrupts));
        }

        private DeathEntry DeathOf(Death d, MeterSession s)
        {
            var recap = d.Recent.Select(r => new RecapLine(r.T - Start, s.Name(r.What), r.Amount, r.HpBefore, r.Note)).ToList();
            var blow = KillingBlow(d.Recent, Start + d.T);
            var amount = blow is { } b ? -b.Amount : 0;
            var overkill = blow is { } k && k.HpBefore >= 0 ? Math.Max(0, amount - k.HpBefore) : 0;
            return new DeathEntry(d.Who.Id, d.Who.Slot, d.Who.Name, d.T, blow is { } w ? s.Name(w.What) : "Unknown", amount, overkill, d.PartOfWipe, recap,
                d.MaxHp, d.RaisedAt, d.RaisedBy?.Slot, d.HadWeakness);
        }

        /// <summary>
        /// The killing blow: the last hit (or bleed's tick) in the few seconds before the death that could
        /// kill, an instant death or one whose amount was at least the HP they had before it; else the
        /// last hit. A hit's damage is taken off a moment after the game says it landed, so a bleed's small
        /// tick can come between a lethal hit and the HP reaching 0: that tick isn't what killed them. (The
        /// recording's rule for a death's cause, <see cref="RecordingBuilder.Alive"/>.)
        /// </summary>
        private static Recap? KillingBlow(Recap[] recent, double diedAt)
        {
            Recap? last = null;
            for (var i = recent.Length - 1; i >= 0; i--)
            {
                var r = recent[i];
                if (!r.Hit) continue;
                last ??= r;
                if (diedAt - r.T <= RecordingBuilder.DeathCause && (r.Instant || (r.HpBefore >= 0 && -r.Amount >= r.HpBefore))) return r;
            }
            return last;
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
