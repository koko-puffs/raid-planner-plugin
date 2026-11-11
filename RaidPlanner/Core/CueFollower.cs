using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPlanner.Core;

/// <summary>
/// Moves a plan along during a pull, from its cues (contract §2: each step's first mechanic, its
/// action and when it went off in the pull the plan was made from).
///
/// - A pull starting goes back to the first step.
/// - A cast of a cue's action beginning (or its hit landing, for an action with no cast) moves to
///   that cue's step: the cue nearest where it was expected among those not matched yet, within
///   <see cref="MatchWindow"/> of it. That's the truth, so it can move back too (when the clock ran
///   ahead). It never jumps past a cue that isn't due yet: an action used again before its own
///   step (the same attack in an earlier step) is left to the clock, which catches up instead.
/// - Between those, the plan moves ahead by the cues' times, counted from the last cue matched, a
///   couple of seconds early, and only forward; but not before a cue has been matched this pull
///   (so a trash pull in a duty with several stays on the first step, quiet).
/// - A step is called out a few seconds before its first cue (or as its cast begins), once a pull;
///   one already well past isn't called late. Nothing is called before the first match.
/// - After a match, the same action is quiet for a while: its own hits (or a hit-only action's next
///   hits, an exaflare's chain) for <see cref="OwnHits"/> seconds, and casts of it going off with it
///   (several helpers casting together) for <see cref="SameCast"/>. And the action just matched never
///   matches a later cue of its own that isn't due yet.
///
/// Times are seconds from the pull; a cue's is when its mechanic goes off, so a cast is placed at
/// its end. Game-free and allocation-free once made, so it's tested and cheap every frame.
/// </summary>
public sealed class CueFollower
{
    /// <summary>How early the plan moves to a step by the clock.</summary>
    public const double Lead = 2.0;

    /// <summary>How long before a step's mechanic it's called out.</summary>
    public const double CalloutLead = 5.0;

    /// <summary>A cast this far from where its cue was expected (counted from the last cue matched) isn't taken for it.</summary>
    public const double MatchWindow = 12;

    /// <summary>A match that would pass over a cue expected more than this after it isn't taken: that cue isn't due yet.</summary>
    public const double NotDue = 5;

    /// <summary>A callout this late (the clock jumped past it) isn't said.</summary>
    public const double Stale = 3;

    /// <summary>After a match, hits of the same action within this of it going off aren't matched again.</summary>
    public const double OwnHits = 7;

    /// <summary>After a match, casts of the same action going off within this of it are the same mechanic (helpers casting together).</summary>
    public const double SameCast = 1;

    private readonly PlanCue[] cues;
    private readonly bool[] said;
    private int lastMatched = -1;
    private int timeIndex;
    private int callIndex;
    private int pendingCall = -1;
    private double anchorT;
    private double anchorAt;
    private uint quietAction;
    private double quietFrom;
    private double quietUntil;

    /// <summary>A cue has been matched this pull: until then the clock doesn't move the plan and nothing is called.</summary>
    private bool engaged;

    public CueFollower(IReadOnlyList<PlanCue> cues, int steps)
    {
        Steps = Math.Max(1, steps);
        this.cues = cues.Where(c => c.Step >= 0 && c.Step < Steps && double.IsFinite(c.T)).OrderBy(c => c.T).ThenBy(c => c.Step).ToArray();
        said = new bool[Steps];
    }

    public int Steps { get; }

    public int Step { get; private set; }

    /// <summary>A pull is being followed.</summary>
    public bool Following { get; private set; }

    public bool HasCues => cues.Length > 0;

    /// <summary>A pull began: the first step, and the clock from the pull.</summary>
    public void Start()
    {
        Following = true;
        Step = 0;
        lastMatched = -1;
        timeIndex = 0;
        callIndex = 0;
        pendingCall = -1;
        anchorT = 0;
        anchorAt = 0;
        quietAction = 0;
        engaged = false;
        Array.Clear(said);
    }

    /// <summary>
    /// Takes over from the follower of the same plan before it was read again (it changed on the
    /// site): the step, the clock, whether it's following, and what's been said and matched this
    /// pull, so nothing is said twice and the step doesn't jump back. The cues may have changed, so
    /// the last one matched is found again by its time.
    /// </summary>
    public void CarryFrom(CueFollower old)
    {
        Following = old.Following;
        Step = Math.Clamp(old.Step, 0, Steps - 1);
        anchorT = old.anchorT;
        anchorAt = old.anchorAt;
        quietAction = old.quietAction;
        quietFrom = old.quietFrom;
        quietUntil = old.quietUntil;
        engaged = old.engaged;
        Array.Clear(said);
        Array.Copy(old.said, said, Math.Min(old.said.Length, said.Length));
        pendingCall = old.pendingCall < Steps ? old.pendingCall : -1;
        lastMatched = -1;
        if (old.lastMatched >= 0)
            for (var i = 0; i < cues.Length && cues[i].T <= anchorT; i++)
                lastMatched = i;
        timeIndex = lastMatched + 1;
        // Cues the clock had passed already stay passed (the clock only moves ahead from the step).
        var passed = old.timeIndex > 0 ? old.cues[Math.Min(old.timeIndex, old.cues.Length) - 1].T : double.NegativeInfinity;
        while (timeIndex < cues.Length && cues[timeIndex].T <= passed) timeIndex++;
        callIndex = 0;
    }

    /// <summary>The pull ended: the step stays where it is, and nothing moves it by itself.</summary>
    public void Stop() => Following = false;

    /// <summary>A step picked by hand (prev and next). The clock goes on from the last cue.</summary>
    public void Set(int step) => Step = Math.Clamp(step, 0, Steps - 1);

    /// <summary>When a cue is expected to go off, by the last cue matched (the pull at first).</summary>
    private double Expected(int i) => anchorAt + (cues[i].T - anchorT);

    /// <summary>Where the pull the plan was made from would be now.</summary>
    private double PlanTime(double now) => anchorT + (now - anchorAt);

    /// <summary>The plan's own time now (its cues' times), once a cue has matched in this pull; null before.</summary>
    public double? Clock(double now) => Following && engaged ? PlanTime(now) : null;

    /// <summary>An enemy's cast began, going off <paramref name="castLeft"/> seconds from <paramref name="now"/>. True when the step changed.</summary>
    public bool Cast(uint action, double now, double castLeft)
    {
        var goesOff = now + (double.IsFinite(castLeft) ? Math.Clamp(castLeft, 0, 60) : 0);
        // Another caster of the mechanic just matched (helpers start the same cast together).
        if (action == quietAction && Math.Abs(goesOff - quietFrom) <= SameCast) return false;
        return Match(action, goesOff);
    }

    /// <summary>An enemy's action landed. True when the step changed.</summary>
    public bool Hit(uint action, double now)
    {
        if (action == quietAction && now >= quietFrom - SameCast && now <= quietUntil) return false;
        return Match(action, now);
    }

    private bool Match(uint action, double goesOff)
    {
        if (!Following || action == 0) return false;
        var again = lastMatched >= 0 && cues[lastMatched].Action == action;
        var best = -1;
        var bestGap = double.MaxValue;
        for (var i = lastMatched + 1; i < cues.Length; i++)
        {
            if (cues[i].Action != action) continue;
            var ahead = Expected(i) - goesOff;
            var gap = Math.Abs(ahead);
            if (gap > MatchWindow || gap >= bestGap) continue;
            // The action just matched, going off again: not its next cue until that's due.
            if (again && ahead > NotDue) continue;
            // The cues are in time order, so the one just before is the latest it would pass over.
            if (i - 1 > lastMatched && Expected(i - 1) - goesOff > NotDue) continue;
            best = i;
            bestGap = gap;
        }
        if (best < 0) return false;
        lastMatched = best;
        timeIndex = best + 1;
        anchorT = cues[best].T;
        anchorAt = goesOff;
        engaged = true;
        quietAction = action;
        quietFrom = goesOff;
        quietUntil = goesOff + OwnHits;
        var step = cues[best].Step;
        if (!said[step]) pendingCall = step;
        var changed = step != Step;
        Step = step;
        return changed;
    }

    /// <summary>Every frame during a pull: moves ahead by the clock. True when the step changed.</summary>
    public bool Tick(double now)
    {
        if (!Following || !engaged) return false;
        var t = PlanTime(now) + Lead;
        var target = Step;
        while (timeIndex < cues.Length && cues[timeIndex].T <= t)
        {
            target = Math.Max(target, cues[timeIndex].Step);
            timeIndex++;
        }
        if (target <= Step) return false;
        Step = target;
        return true;
    }

    /// <summary>The step to call out now, if any (each once a pull); call every frame during a pull.</summary>
    public int? DueCallout(double now)
    {
        if (!Following || !engaged) return null;
        if (pendingCall >= 0)
        {
            var p = pendingCall;
            pendingCall = -1;
            if (!said[p])
            {
                said[p] = true;
                return p;
            }
        }
        var t = PlanTime(now);
        while (callIndex < cues.Length)
        {
            var c = cues[callIndex];
            if (said[c.Step] || t >= c.T + Stale)
            {
                callIndex++;
                continue;
            }
            if (t < c.T - CalloutLead) return null;
            callIndex++;
            said[c.Step] = true;
            return c.Step;
        }
        return null;
    }
}
