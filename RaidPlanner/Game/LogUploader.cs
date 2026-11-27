using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using RaidPlanner.Core;

namespace RaidPlanner.Game;

/// <summary>
/// Upload my logs (since 0.6.0): every kept pull goes whole to your logs on the Raid Codex (<see cref="LogUpload"/>), as soon as
/// it's kept, one at a time and the oldest first, off the game's thread. What's waiting is the kept pulls not sent yet
/// (<see cref="UploadQueue"/>, from the store's sent marks), so pulls kept while offline or when the game closed go at the next
/// chance; and the store holds those still to go past Keep the last (up to <see cref="RecordingStore.MostHeld"/>). On the
/// framework thread; the reading, gzipping and sending on a worker. Since the uploads round (not yet released) none starts while a fight is on or about to
/// start (<see cref="FightRecorder.FightNear"/>: in combat, a pull recorded, a countdown going), and the one on its way when
/// one begins is stopped and goes again whole after (<see cref="UploadQueue.Fight"/>), so a slow connection never shares
/// the fight with an upload.
/// </summary>
public sealed class LogUploader : IDisposable
{
    /// <summary>The summaries kept in memory to go with their pulls (the newest; one older than these goes without, and the site makes its own).</summary>
    private const int SummariesKept = 40;

    private readonly Configuration config;
    private readonly RecordingStore store;
    private readonly FightRecorder recorder;
    private readonly SiteSession site;
    private readonly string version;
    private readonly UploadQueue queue = new();
    private readonly Dictionary<string, string> summaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> summaryOrder = new();
    private readonly Every every = new(250);

    /// <summary>What's wanted, as of the last frame (the store asks from its worker too: never the settings themselves).</summary>
    private volatile UploadPrefs prefs;
    private volatile bool linked;
    private string lastToken;

    /// <summary>Bumped when the account changes: an answer for the one before is ignored.</summary>
    private int generation;
    private bool stopTold;

    /// <summary>Stops the upload on its way (a fight began); null with none. On the framework thread.</summary>
    private CancellationTokenSource? sending;

    public LogUploader(Configuration config, RecordingStore store, FightRecorder recorder, SiteSession site, string version)
    {
        this.config = config;
        this.store = store;
        this.recorder = recorder;
        this.site = site;
        this.version = version;
        prefs = config.UploadPrefsNow();
        lastToken = site.Token;
        linked = SiteLink.LooksRight(lastToken);
        store.Waiting = Waits;
        recorder.Summarised += OnSummarised;
        Services.Framework.Update += OnUpdate;
    }

    /// <summary>A kept pull is still to be uploaded (any thread).</summary>
    private bool Waits(RecordingInfo info) => linked && prefs.Wants(info);

    /// <summary>Uploading stopped (a refused token, a full account): why, in the site's words.</summary>
    public string? Stopped => queue.Stopped;

    /// <summary>What last held it up (the hourly limit, no connection), and until when.</summary>
    public string? Held => queue.Held;

    public DateTime HeldUntil => queue.NotBefore;

    /// <summary>A pull is on its way.</summary>
    public bool Uploading => queue.InFlight != null;

    /// <summary>How many have gone since the plugin loaded.</summary>
    public int SentCount => queue.SentCount;

    /// <summary>How many kept pulls are waiting to go.</summary>
    public int WaitingCount => UploadQueue.Waiting(store.All, Waits).Count;

    /// <summary>Kept pulls from before Upload since that haven't gone (what Upload my older pulls adds).</summary>
    public int OlderCount => config.UploadPrefsNow().Older(store.All);

    /// <summary>Upload my older pulls: every kept pull not sent goes too, the oldest first.</summary>
    public void UploadOlder()
    {
        config.UploadSince = DateTimeOffset.MinValue;
        config.Save();
        prefs = config.UploadPrefsNow();
        Resume();
    }

    /// <summary>Upload my logs turned on or off: on, from now (the pulls kept while it was off only with Upload my older pulls).</summary>
    public void SetOn(bool on)
    {
        config.UploadLogs = on;
        if (on) config.UploadSince = DateTimeOffset.Now;
        config.Save();
        prefs = config.UploadPrefsNow();
        if (on) Resume();
    }

    private void Resume()
    {
        queue.Resume();
        stopTold = false;
    }

    private void OnSummarised(RecordingInfo info, string summary)
    {
        if (summaries.ContainsKey(info.File)) return;
        summaries[info.File] = summary;
        summaryOrder.Enqueue(info.File);
        while (summaryOrder.Count > SummariesKept) summaries.Remove(summaryOrder.Dequeue());
    }

    private void OnUpdate(IFramework framework)
    {
        try
        {
            Pump();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Raid Codex couldn't upload a pull");
        }
    }

    private void Pump()
    {
        if (!every.Due(Environment.TickCount64) || site.Closed) return;
        // A fight on or about to start: nothing starts, and the one on its way stops (it goes again whole after; the site
        // keeps a pull sent twice once). Also never started during a pull for the game's sake: fitting a long pull's file to
        // the site's size, serialising and compressing it is tens of megabytes of work, and though it's a worker's, the
        // garbage it leaves can pause the game mid-fight.
        queue.Fight = recorder.FightNear;
        if (queue.Cancels && sending is { IsCancellationRequested: false } stop) stop.Cancel();
        prefs = config.UploadPrefsNow();
        var token = site.Token;
        if (!string.Equals(token, lastToken, StringComparison.Ordinal))
        {
            // Linked again (or to another account): what's on its way is for the one before; start afresh.
            lastToken = token;
            generation++;
            queue.Drop();
            Resume();
        }
        linked = SiteLink.LooksRight(token);
        if (!prefs.UploadLogs || !linked) return;
        if (queue.Next(store.All, DateTime.UtcNow, Waits) is { } next) Start(next, token);
    }

    private void Start(RecordingInfo info, string token)
    {
        var path = store.PathOf(info);
        var name = info.Name;
        var clientId = SiteLink.ClientId(info.File, info.RecordedAt);
        summaries.TryGetValue(info.File, out var summary);
        var gen = generation;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(site.Closing);
        sending = stop;
        var cancel = stop.Token;
        var http = site.Http;
        _ = Task.Run(async () =>
        {
            UploadAnswer answer;
            try
            {
                var json = await File.ReadAllTextAsync(path, cancel).ConfigureAwait(false);
                if (LogUpload.Fit(json) is not { } fit)
                    answer = new UploadAnswer(UploadOutcome.Refused, Why: "This pull is too big for the site.", Hint: "size");
                else
                {
                    byte[]? body = null;
                    try
                    {
                        body = LogUpload.Body(clientId, name, summary, fit.Json);
                    }
                    catch (JsonException)
                    {
                        // A summary that isn't JSON goes without; a recording that isn't is refused below.
                        if (summary != null)
                            try
                            {
                                body = LogUpload.Body(clientId, name, null, fit.Json);
                            }
                            catch (JsonException)
                            {
                                body = null;
                            }
                    }
                    answer = body == null
                        ? new UploadAnswer(UploadOutcome.Refused, Why: "Its file couldn't be read.", Hint: "recording")
                        : await LogUpload.SendAsync(http, token, body, version, cancel).ConfigureAwait(false);
                    if (fit.Thinned && answer.Outcome == UploadOutcome.Sent) answer = answer with { Thinned = true };
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                answer = new UploadAnswer(UploadOutcome.Refused, Why: "Its file is gone.");
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                answer = new UploadAnswer(UploadOutcome.Retry, Why: "Stopped.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                answer = new UploadAnswer(UploadOutcome.Retry, Why: "Its file couldn't be read.", ThisPull: true);
            }
            catch (Exception ex)
            {
                Services.Log.Error(ex, "Raid Codex couldn't upload a pull");
                answer = new UploadAnswer(UploadOutcome.Retry, Why: "The upload couldn't be made.", ThisPull: true);
            }
            if (site.Closed) return;
            // Stopped for a fight (not the plugin unloading): it waits, with no failure counted, and goes again after.
            if (answer.Why == "Stopped." && stop.IsCancellationRequested) answer = LogUpload.ForTheFight;
            try
            {
                _ = Services.Framework.RunOnFrameworkThread(() =>
                {
                    try
                    {
                        Finish(info, gen, answer, stop);
                    }
                    catch (Exception ex)
                    {
                        Services.Log.Error(ex, "Raid Codex couldn't use the upload's answer");
                    }
                });
            }
            catch (Exception)
            {
                // Unloaded meanwhile.
            }
        });
    }

    /// <summary>
    /// A store mark on a worker (files forced to disk), never the framework thread's; a failure is logged, and the queue told
    /// (on the framework thread) so the pull may be picked again. Until the store shows the mark, the queue doesn't pick it.
    /// </summary>
    private void MarkOffFramework(string file, Action write, string what)
    {
        _ = Task.Run(() =>
        {
            try
            {
                write();
            }
            catch (Exception ex)
            {
                Services.Log.Error(ex, $"Raid Codex couldn't {what}");
                if (site.Closed) return;
                try
                {
                    _ = Services.Framework.RunOnFrameworkThread(() => queue.Settled(file));
                }
                catch (Exception)
                {
                    // Unloaded meanwhile.
                }
            }
        });
    }

    private void Finish(RecordingInfo info, int gen, UploadAnswer answer, CancellationTokenSource stop)
    {
        if (ReferenceEquals(sending, stop)) sending = null;
        stop.Dispose();
        if (site.Closed || gen != generation) return;
        var mark = queue.Done(info.File, answer, DateTime.UtcNow);
        if (mark is { SentId: { } id })
        {
            // Written on a worker: the store forces each of its two small files to disk, and on the
            // framework thread that could stall the game (an answer often comes as the next pull starts). The queue doesn't
            // pick it again meanwhile (it would be uploaded twice): it waits for the store to show it sent.
            var at = DateTimeOffset.UtcNow;
            MarkOffFramework(info.File, () => store.MarkSent(info, id, at), "mark a pull sent");
            summaries.Remove(info.File);
            Services.Log.Information($"Raid Codex uploaded {info.Name}{(answer.Duplicate ? " (already there)" : "")}{(answer.Thinned ? ", thinned" : "")}");
            return;
        }
        if (mark is { Refused: { } why })
        {
            MarkOffFramework(info.File, () => store.MarkRefused(info, why), "mark a pull refused");
            summaries.Remove(info.File);
            Services.Log.Information($"Raid Codex's upload of {info.Name} was refused: {why}");
            return;
        }
        if (answer.Outcome == UploadOutcome.Stop && !stopTold)
        {
            stopTold = true;
            PlanService.Print($"Uploading your pulls stopped: {answer.Why}");
        }
        else if (answer == LogUpload.ForTheFight)
        {
            Services.Log.Information($"Raid Codex's upload of {info.Name} stopped for the fight: it goes again after");
        }
        else if (answer.Outcome != UploadOutcome.Sent)
        {
            Services.Log.Information($"Raid Codex's upload of {info.Name} waits: {answer.Why}");
        }
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        recorder.Summarised -= OnSummarised;
        store.Waiting = null;
    }
}
