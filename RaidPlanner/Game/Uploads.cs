using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using RaidPlanner.Core;

namespace RaidPlanner.Game;

/// <summary>
/// What goes to the site without a pull's whole recording: each kept pull's summary for the
/// progress tracker (with Track my progress on), and your party's names for your roster (only when
/// you press the button). Summaries wait in a short queue: sent one at a time, each with the pull's
/// own id (so one sent twice is kept once), tried again later when the site can't be reached.
/// On the framework thread; the calls go on workers.
/// </summary>
public sealed class Uploads : IDisposable
{
    /// <summary>The most summaries waiting; past it the oldest goes.</summary>
    private const int Waiting = 20;

    private readonly Configuration config;
    private readonly GameData data;
    private readonly SiteSession site;
    private readonly FightRecorder recorder;
    private readonly SummaryQueue queue = new(Waiting);
    private readonly Backoff backoff = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
    private bool rosterSending;

    public Uploads(Configuration config, GameData data, FightRecorder recorder, SiteSession site)
    {
        this.config = config;
        this.data = data;
        this.site = site;
        this.recorder = recorder;
        recorder.Summarised += OnSummarised;
        Services.Framework.Update += OnUpdate;
    }

    /// <summary>Your party's names are on their way to your roster.</summary>
    public bool RosterSending => rosterSending;

    private void OnSummarised(RecordingInfo info, string summary)
    {
        if (!config.TrackProgress || !site.Linked) return;
        queue.Add(summary, SiteLink.ClientId(info.File, info.RecordedAt));
        Pump();
    }

    private void OnUpdate(IFramework framework)
    {
        try
        {
            if (queue.Count > 0) Pump();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Raid Planner couldn't send a pull's summary");
        }
    }

    private void Pump()
    {
        if (queue.Sending || queue.Count == 0 || !backoff.Ready(DateTime.UtcNow)) return;
        if (!config.TrackProgress || !site.Linked)
        {
            queue.Clear();
            return;
        }
        if (queue.Next() is not var (summary, clientId)) return;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(summary);
        }
        catch (JsonException)
        {
            queue.Answered(true);
            return;
        }
        var call = site.Call("rp_plugin_pull", new JsonObject { ["p_summary"] = node, ["p_client_id"] = clientId });
        Back(call, r =>
        {
            if (r.Ok)
            {
                backoff.Succeeded();
                queue.Answered(true);
                return;
            }
            if (r.TokenRefused)
            {
                queue.Answered(false);
                queue.Clear();
                return;
            }
            if (r.Unreachable || r.Hint == "rate")
            {
                // No connection, the site down or busy: the same one again later.
                queue.Answered(false);
                backoff.Failed(DateTime.UtcNow);
                return;
            }
            // Refused for good (turned off, the site not ready for it): this one isn't sent.
            Services.Log.Information($"Raid Planner's pull summary wasn't taken: {r.Why}");
            queue.Answered(true);
            backoff.Failed(DateTime.UtcNow);
        });
    }

    /// <summary>
    /// Sends your party's names, by spot (as a recording gives the spots), to your own roster on the
    /// site; <paramref name="done"/> hears how it went, in words, on the framework thread.
    /// </summary>
    public void SendRoster(Action<string, bool> done)
    {
        if (rosterSending) return;
        if (!site.Linked)
        {
            done("Link your account first.", false);
            return;
        }
        var party = new List<(string Name, byte Role)>();
        // The party (since 0.5.0 a cross-world party's group too, outside a duty: PartyMembers).
        foreach (var m in PartyMembers.Read())
        {
            if (m.Name.Length == 0) continue;
            party.Add((m.Name, data.Job(m.ClassJob).Role));
        }
        // Never just you: sending what's read (when the party can't be read) would replace the roster
        // on the site with one name.
        var roster = PartyRoster.Build(party);
        if (PartyRoster.WhyNot(roster.Count) is { } why)
        {
            done(why, false);
            return;
        }
        rosterSending = true;
        var count = roster.Count;
        Back(site.Call("rp_plugin_roster", new JsonObject { ["p_roster"] = roster }), r =>
        {
            rosterSending = false;
            done(r.Ok ? $"Sent {Words.Count(count, "name")} to your roster." : r.Why ?? "Couldn't send them.", r.Ok);
        });
    }

    /// <summary>A call's answer, on the framework thread; nothing after the plugin has unloaded.</summary>
    private void Back(Task<RpcResult> call, Action<RpcResult> then)
    {
        _ = call.ContinueWith(task =>
        {
            var r = task.IsCompletedSuccessfully ? task.Result : RpcResult.Stopped;
            if (site.Closed) return;
            try
            {
                _ = Services.Framework.RunOnFrameworkThread(() =>
                {
                    try
                    {
                        if (!site.Closed) then(r);
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
        recorder.Summarised -= OnSummarised;
    }
}
