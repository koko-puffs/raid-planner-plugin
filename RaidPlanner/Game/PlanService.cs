using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using RaidPlanner.Core;

namespace RaidPlanner.Game;

/// <summary>
/// Your plans in the game. In a duty, the plan made for it (its <c>game.content</c> is the duty's
/// ContentFinderCondition) is fetched and shown; during a pull it follows along (<see cref="CueFollower"/>),
/// calls each step out a few seconds early, and shares the step with the site when you've asked it
/// to. The list of plans is kept for a few minutes, and each plan read is kept until it changes.
///
/// Everything here runs on the framework thread; calls to the site go on workers
/// (<see cref="SiteSession"/>) and come back through <c>RunOnFrameworkThread</c>, dropped if the
/// account changed meanwhile or the plugin unloaded.
/// </summary>
public sealed class PlanService : IDisposable
{
    /// <summary>The list of plans is read again after this (or on Refresh).</summary>
    private static readonly TimeSpan ListFresh = TimeSpan.FromMinutes(3);

    /// <summary>How many plans are kept read.</summary>
    private const int Kept = 8;

    /// <summary>How many duties' picked plans are remembered.</summary>
    private const int Remembered = 64;

    /// <summary>The chat's sound for a callout (as &lt;se.1&gt; in a macro), a short soft ping.</summary>
    private const uint CalloutSound = 1;

    private readonly Configuration config;
    private readonly GameData data;
    private readonly FightRecorder recorder;
    private readonly SiteSession site;
    private readonly LiveShare live = new();

    /// <summary>After the site said "too many" (or couldn't be reached), the list isn't read again by itself for a while.</summary>
    private readonly Backoff listBackoff = new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));
    private readonly Dictionary<string, Plan> plans = new();
    private readonly List<string> plansOrder = new();

    /// <summary>Bumped when the account changes: answers to calls made before are dropped.</summary>
    private int generation;

    private string lastToken;
    private bool lastShare;
    private DateTime listAt = DateTime.MinValue;
    private uint territory = uint.MaxValue;

    /// <summary>In a duty, the plan for it still to be picked (once the list is in).</summary>
    private uint pickFor;

    /// <summary>The duty's plan nudge is still to be said (once its plan is in).</summary>
    private bool nudgeDue;

    /// <summary>The window was opened for this duty (so it closes again when you leave).</summary>
    private bool shownForDuty;

    private DateTime chimeFailedAt;

    public PlanService(Configuration config, GameData data, FightRecorder recorder, SiteSession site)
    {
        this.config = config;
        this.data = data;
        this.recorder = recorder;
        this.site = site;
        lastToken = config.SiteToken;
        lastShare = config.ShareStep;
        recorder.PullStarted += OnPullStarted;
        recorder.PullEnded += OnPullEnded;
        recorder.CastBegan += OnCast;
        recorder.HitLanded += OnHit;
        Services.Framework.Update += OnUpdate;
    }

    /// <summary>Shows or hides the plan window (set by the plugin).</summary>
    public Action<bool>? ShowWindow { get; set; }

    /// <summary>Bumped on every change the window draws (a new plan, a new step, the list).</summary>
    public int Version { get; private set; }

    public bool Linked => site.Linked;

    public IReadOnlyList<PlanListItem> List { get; private set; } = Array.Empty<PlanListItem>();

    public bool ListLoaded { get; private set; }

    public bool ListLoading { get; private set; }

    /// <summary>Why the list couldn't be read, in words.</summary>
    public string? ListWhy { get; private set; }

    public Plan? Plan { get; private set; }

    /// <summary>The plan being fetched, by id.</summary>
    public string? Loading { get; private set; }

    /// <summary>Why the plan couldn't be read, in words.</summary>
    public string? PlanWhy { get; private set; }

    /// <summary>The plan that couldn't be read (with <see cref="PlanWhy"/>), so Try again reads it again.</summary>
    public string? FailedId { get; private set; }

    /// <summary>Bumped on each plan asked for: only the newest one's answer is shown (a slower earlier one isn't).</summary>
    private int planRequest;

    private CueFollower? follower;

    public int Step => follower?.Step ?? 0;

    /// <summary>The duty you're in now (its ContentFinderCondition), or 0.</summary>
    public uint Content { get; private set; }

    public string? DutyName { get; private set; }

    /// <summary>The plan is for the duty you're in, so pulls move it along.</summary>
    public bool ForThisDuty => Plan?.Game is { } g && Content != 0 && g.Content == Content;

    /// <summary>A pull is moving the plan along now.</summary>
    public bool Following => follower is { Following: true } && ForThisDuty;

    /// <summary>Where the pull is in the plan's own time (its cues' times), once a cue has matched; null before (the plan window's slim cast bar).</summary>
    public double? PlanClock => follower != null && Following && recorder.InPull ? follower.Clock(recorder.Elapsed) : null;

    /// <summary>Your plans for the duty you're in.</summary>
    public IEnumerable<PlanListItem> ForDuty => Content == 0 ? Enumerable.Empty<PlanListItem>() : List.Where(p => p.Content == Content);

    // ---------------------------------------------------------------------------------------------
    // Every frame.

    private void OnUpdate(IFramework framework)
    {
        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Raid Planner's plan couldn't update");
        }
    }

    private void Tick()
    {
        if (config.SiteToken != lastToken)
        {
            lastToken = config.SiteToken;
            Forget();
        }
        if (config.ShareStep != lastShare)
        {
            lastShare = config.ShareStep;
            live.Reset();
            if (lastShare) ShareNow();
        }
        var here = Services.ClientState.TerritoryType;
        if (here != territory)
        {
            territory = here;
            EnteredZone();
        }
        else if (Content == 0 && InDuty && data.Duty(here) is { Content: > 0 })
        {
            // The duty flag can come a moment after the zone.
            EnteredZone();
        }

        if (pickFor != 0 && ListLoaded && !ListLoading && pickFor == Content) PickForDuty();
        if (nudgeDue && Plan != null && ForThisDuty && Loading == null) Nudge();

        if (follower != null && Following && recorder.InPull)
        {
            var now = recorder.Elapsed;
            if (follower.Tick(now)) StepChanged();
            if (follower.DueCallout(now) is { } step)
            {
                if (config.Callouts) Callout(step);
            }
        }

        if (config.ShareStep && site.Linked && live.Due(DateTime.UtcNow) is { } due) Share(due);
    }

    private static bool InDuty =>
        Services.Condition[ConditionFlag.BoundByDuty] || Services.Condition[ConditionFlag.BoundByDuty56] || Services.Condition[ConditionFlag.BoundByDuty95];

    private void EnteredZone()
    {
        var duty = InDuty ? data.Duty(territory) : null;
        var content = duty?.Content ?? 0;
        if (content == Content) return;
        follower?.Stop();
        if (shownForDuty)
        {
            shownForDuty = false;
            ShowWindow?.Invoke(false);
        }
        Content = content;
        DutyName = duty?.Name;
        pickFor = 0;
        nudgeDue = false;
        Version++;
        if (content == 0 || !site.Linked) return;
        pickFor = content;
        nudgeDue = true;
        if (!ListLoaded || DateTime.UtcNow - listAt > ListFresh) RefreshList();
    }

    /// <summary>The duty's plan: the one you picked for it last time, else your newest for it.</summary>
    private void PickForDuty()
    {
        pickFor = 0;
        var mine = ForDuty.ToList();
        if (mine.Count == 0)
        {
            nudgeDue = false;
            return;
        }
        var pick = config.PlanForDuty.TryGetValue(Content, out var id) && mine.FirstOrDefault(p => p.Id == id) is { } remembered ? remembered : mine[0];
        if (Plan?.Id == pick.Id && Plan.Stamp == pick.Stamp) return;
        Open(pick.Id, false);
    }

    /// <summary>Entering a duty you have a plan for: said once in chat, and the window opened (with Show my plan in duties on).</summary>
    private void Nudge()
    {
        nudgeDue = false;
        Print($"You have a plan for this duty: {Plan!.Name}. /raidplan plan shows it.");
        if (!config.ShowPlanInDuties) return;
        shownForDuty = true;
        ShowWindow?.Invoke(true);
    }

    // ---------------------------------------------------------------------------------------------
    // Pulls.

    private void OnPullStarted()
    {
        if (follower == null || !ForThisDuty) return;
        var was = follower.Step;
        follower.Start();
        if (follower.Step != was) StepChanged();
        else Version++;
    }

    private void OnPullEnded()
    {
        if (follower == null) return;
        follower.Stop();
        Version++;
    }

    private void OnCast(uint action, double t, double castLeft)
    {
        if (follower is { Following: true } f && ForThisDuty && f.Cast(action, t, castLeft)) StepChanged();
    }

    private void OnHit(uint action, double t)
    {
        if (follower is { Following: true } f && ForThisDuty && f.Hit(action, t)) StepChanged();
    }

    private void StepChanged()
    {
        Version++;
        if (config.ShareStep) ShareNow();
    }

    /// <summary>The step shown now is to be shared (once it settles): the plan, the step and its id.</summary>
    private void ShareNow()
    {
        if (Plan == null) return;
        var step = Step;
        var id = step >= 0 && step < Plan.Steps.Count ? Plan.Steps[step].Id : null;
        live.Changed(new LiveStep(Plan.Id, step, id), DateTime.UtcNow);
    }

    /// <summary>The next or previous step (the window's buttons, /raidplan next and prev).</summary>
    public void Move(int by)
    {
        if (follower == null || Plan == null) return;
        var was = follower.Step;
        follower.Set(was + by);
        if (follower.Step != was) StepChanged();
    }

    public void GoTo(int step)
    {
        if (follower == null) return;
        var was = follower.Step;
        follower.Set(step);
        if (follower.Step != was) StepChanged();
    }

    // ---------------------------------------------------------------------------------------------
    // Callouts and chat.

    /// <summary>A step said in chat: its name, then its callout as a raid lead would say it (<see cref="PlanJson.ChatLine"/>), with a soft sound.</summary>
    private void Callout(int step)
    {
        if (Plan == null || step < 0 || step >= Plan.Steps.Count) return;
        Print(PlanJson.ChatLine(Plan.Steps[step]));
        Chime();
    }

    public static void Print(string text)
    {
        try
        {
            Services.Chat.Print(text, "Raid Planner");
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Raid Planner couldn't write to chat");
        }
    }

    /// <summary>The chat's soft sound, when the game's function for it is there (never called through a missing address).</summary>
    private unsafe void Chime()
    {
        try
        {
            if ((nint)UIGlobals.MemberFunctionPointers.PlaySoundEffect == 0) return;
            UIGlobals.PlayChatSoundEffect(CalloutSound);
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - chimeFailedAt < TimeSpan.FromMinutes(1)) return;
            chimeFailedAt = DateTime.UtcNow;
            Services.Log.Warning(ex, "Raid Planner couldn't play its callout sound");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The site.

    /// <summary>Unlinked, or linked to another account: nothing read before is kept.</summary>
    private void Forget()
    {
        generation++;
        List = Array.Empty<PlanListItem>();
        ListLoaded = false;
        ListLoading = false;
        ListWhy = null;
        listAt = DateTime.MinValue;
        plans.Clear();
        plansOrder.Clear();
        Plan = null;
        FailedId = null;
        follower = null;
        Loading = null;
        PlanWhy = null;
        live.Reset();
        Version++;
        if (Content != 0 && site.Linked)
        {
            pickFor = Content;
            nudgeDue = false;
            RefreshList();
        }
    }

    /// <summary>
    /// Reads your plans again: by hand (the window's buttons), or by itself on entering a duty when
    /// the list is old, which waits after the site has said too many (its <c>rate</c> refusal).
    /// </summary>
    public void RefreshList(bool byHand = false)
    {
        if (ListLoading || !site.Linked) return;
        if (!byHand && !listBackoff.Ready(DateTime.UtcNow)) return;
        ListLoading = true;
        ListWhy = null;
        Version++;
        var g = generation;
        Then(site.Call("rp_plugin_plans"), r => r.Ok ? PlanJson.List(r.Body) : null, (r, list) =>
        {
            if (g != generation) return;
            ListLoading = false;
            Version++;
            if (list == null)
            {
                ListWhy = r.Why;
                if (r.Hint == "rate" || r.Unreachable) listBackoff.Failed(DateTime.UtcNow);
                return;
            }
            listBackoff.Succeeded();
            List = list;
            ListLoaded = true;
            listAt = DateTime.UtcNow;
            // A plan shown that's changed on the site since: read again (unless another is being opened).
            if (Plan != null && Loading == null && list.FirstOrDefault(p => p.Id == Plan.Id) is { } item && item.Stamp != Plan.Stamp) Open(Plan.Id, false, true);
        });
    }

    /// <summary>
    /// Shows a plan: from what's kept when it hasn't changed, else read from the site. Picked by hand
    /// in a duty, it's remembered as that duty's plan.
    /// </summary>
    public void Open(string id, bool byHand, bool fresh = false)
    {
        if (byHand && Content != 0)
        {
            if (!config.PlanForDuty.ContainsKey(Content) && config.PlanForDuty.Count >= Remembered)
                config.PlanForDuty.Remove(config.PlanForDuty.Keys.First());
            config.PlanForDuty[Content] = id;
            config.Save();
        }
        var stamp = List.FirstOrDefault(p => p.Id == id)?.Stamp;
        if (!fresh && plans.TryGetValue(id, out var kept) && (stamp == null || kept.Stamp == stamp))
        {
            // The newest pick: one still on its way is no longer wanted.
            planRequest++;
            Loading = null;
            FailedId = null;
            Show(kept);
            return;
        }
        if (!site.Linked || Loading == id) return;
        var request = ++planRequest;
        Loading = id;
        PlanWhy = null;
        FailedId = null;
        Version++;
        var g = generation;
        Then(site.Call("rp_plugin_plan", new JsonObject { ["p_id"] = id }), r => r.Ok ? PlanJson.Read(r.Body) : null, (r, plan) =>
        {
            if (g != generation) return;
            if (plan != null) Keep(plan);
            // Another plan was asked for since: this answer is kept for later, not shown.
            if (request != planRequest) return;
            Loading = null;
            Version++;
            if (plan == null)
            {
                PlanWhy = r.Ok ? "That plan couldn't be read." : r.Why;
                FailedId = id;
                return;
            }
            Show(plan);
        });
    }

    /// <summary>Try again: the plan that couldn't be read is read again; else the list.</summary>
    public void Retry()
    {
        if (PlanWhy != null && FailedId is { } id)
        {
            PlanWhy = null;
            FailedId = null;
            Version++;
            Open(id, false, true);
            return;
        }
        PlanWhy = null;
        RefreshList(true);
    }

    private void Keep(Plan plan)
    {
        plans[plan.Id] = plan;
        plansOrder.Remove(plan.Id);
        plansOrder.Add(plan.Id);
        while (plansOrder.Count > Kept)
        {
            plans.Remove(plansOrder[0]);
            plansOrder.RemoveAt(0);
        }
    }

    private void Show(Plan plan)
    {
        var old = Plan?.Id == plan.Id ? follower : null;
        Plan = plan;
        PlanWhy = null;
        FailedId = null;
        var next = new CueFollower(plan.Game?.Cues ?? Array.Empty<PlanCue>(), plan.Steps.Count);
        if (old != null)
        {
            // The same plan read again (changed on the site, or picked again): where it was, what's
            // been said and matched this pull, all kept.
            next.CarryFrom(old);
        }
        if (!next.Following && recorder.InPull && ForThisDuty)
        {
            // Another plan mid-pull: followed from the pull's own clock.
            next.Start();
            next.Tick(recorder.Elapsed);
        }
        follower = next;
        StepChanged();
    }

    private void Share(LiveCall call)
    {
        var g = generation;
        var args = new JsonObject { ["p_plan"] = call.What.Plan, ["p_step"] = call.What.Step };
        if (call.What.Phase != null) args["p_phase"] = call.What.Phase;
        Then(site.Call("rp_plugin_live", args), _ => true, (r, _) =>
        {
            // Always told, so a call that's no longer wanted can't hold sharing up (a stale one is ignored).
            live.Done(call, r.Ok, DateTime.UtcNow);
            if (g != generation) return;
            if (!r.Ok && !r.Unreachable) Services.Log.Information($"Raid Planner couldn't share the step: {r.Why}");
        });
    }

    /// <summary>
    /// A call's answer, worked on (parsed) on the worker, then handed to <paramref name="then"/> on the
    /// framework thread; nothing after the plugin has unloaded.
    /// </summary>
    private void Then<T>(Task<RpcResult> call, Func<RpcResult, T> work, Action<RpcResult, T> then)
    {
        _ = call.ContinueWith(task =>
        {
            var r = task.IsCompletedSuccessfully ? task.Result : RpcResult.Stopped;
            T value;
            try
            {
                value = work(r);
            }
            catch (Exception ex)
            {
                Services.Log.Error(ex, "Raid Planner couldn't read the site's answer");
                value = default!;
                r = r with { Ok = false, Why = "The site's answer couldn't be read." };
            }
            if (site.Closed) return;
            try
            {
                _ = Services.Framework.RunOnFrameworkThread(() =>
                {
                    try
                    {
                        if (!site.Closed) then(r, value);
                    }
                    catch (Exception ex)
                    {
                        Services.Log.Error(ex, "Raid Planner couldn't use the site's answer");
                    }
                });
            }
            catch (Exception)
            {
                // Unloaded meanwhile.
            }
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        recorder.PullStarted -= OnPullStarted;
        recorder.PullEnded -= OnPullEnded;
        recorder.CastBegan -= OnCast;
        recorder.HitLanded -= OnHit;
    }
}
