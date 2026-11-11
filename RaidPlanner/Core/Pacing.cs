using System;

namespace RaidPlanner.Core;

/// <summary>
/// Waiting longer after each failure: first <c>first</c>, doubling up to <c>most</c>, back to none
/// after a success. So a site that's down (or a connection that's gone) isn't asked again every frame.
/// </summary>
public sealed class Backoff
{
    private readonly TimeSpan first;
    private readonly TimeSpan most;
    private TimeSpan wait;
    private DateTime until = DateTime.MinValue;

    public Backoff(TimeSpan first, TimeSpan most)
    {
        this.first = first;
        this.most = most;
    }

    /// <summary>
    /// The wait is over; or the computer's clock has been set back by more than the wait (else a clock
    /// put back an hour would hold every call up for that hour).
    /// </summary>
    public bool Ready(DateTime now) => now >= until || until - now > wait;

    /// <summary>When it's ready again.</summary>
    public DateTime Until => until;

    public void Failed(DateTime now)
    {
        wait = wait == TimeSpan.Zero ? first : TimeSpan.FromTicks(Math.Min(most.Ticks, wait.Ticks * 2));
        until = now + wait;
    }

    public void Succeeded()
    {
        wait = TimeSpan.Zero;
        until = DateTime.MinValue;
    }
}

/// <summary>The step shared with the site: the plan, the step's index and its id (the site prefers the id).</summary>
public readonly record struct LiveStep(string Plan, int Step, string? Phase);

/// <summary>A call to share a step: what, and its number (an answer to an older call is ignored).</summary>
public readonly record struct LiveCall(LiveStep What, int Seq);

/// <summary>
/// Sharing your step with the site, paced: a change is sent once it's held still for a moment (so
/// clicking through steps sends only where you stopped; the last change always goes), one call at
/// a time, only when it differs from what the site has, and less often after failures.
/// </summary>
public sealed class LiveShare
{
    /// <summary>How long a step must hold before it's sent.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(600);

    private readonly Backoff backoff = new(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
    private LiveStep? pending;
    private LiveStep? sent;
    private DateTime due;

    /// <summary>The call on its way, by number (0: none), and the last number given.</summary>
    private int inFlight;
    private int seq;

    /// <summary>The step shown now: it's sent once it has held for <see cref="Settle"/> (a newer change starts the wait again).</summary>
    public void Changed(LiveStep step, DateTime now)
    {
        pending = step;
        due = now + Settle;
    }

    /// <summary>What to send now, if anything: it's settled, nothing's on its way, and the site doesn't have it yet.</summary>
    public LiveCall? Due(DateTime now)
    {
        // Not settled yet (a clock set back by more than the wait counts as settled).
        if (inFlight != 0 || pending is not { } p || (now < due && due - now <= Settle) || !backoff.Ready(now)) return null;
        if (sent == p)
        {
            pending = null;
            return null;
        }
        seq = seq == int.MaxValue ? 1 : seq + 1;
        inFlight = seq;
        return new LiveCall(p, seq);
    }

    /// <summary>
    /// How a call went (on the framework thread). An answer to a call from before a
    /// <see cref="Reset"/> (or any but the one on its way) is ignored. A change made meanwhile is
    /// still pending, so it goes next.
    /// </summary>
    public void Done(LiveCall call, bool ok, DateTime now)
    {
        if (call.Seq != inFlight || inFlight == 0) return;
        inFlight = 0;
        if (ok)
        {
            backoff.Succeeded();
            sent = call.What;
            if (pending == call.What) pending = null;
        }
        else
        {
            backoff.Failed(now);
        }
    }

    /// <summary>
    /// Forget what the site has (unlinked, linked again, the setting turned off): the next change is
    /// sent whatever it is, and a call still on its way no longer holds it up.
    /// </summary>
    public void Reset()
    {
        pending = null;
        sent = null;
        inFlight = 0;
        backoff.Succeeded();
    }
}
