using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RaidPlanner.Core;

/// <summary>What an upload's answer means for the queue (the site's contract, SPEC10 §1).</summary>
public enum UploadOutcome
{
    /// <summary>Stored (or already there: a duplicate): marked sent.</summary>
    Sent,

    /// <summary>This pull is never taken (not a recording, too big): marked so, never tried again.</summary>
    Refused,

    /// <summary>The token isn't taken, or the account is full: every upload stops until something changes.</summary>
    Stop,

    /// <summary>Saving to accounts is off on the site: everything waits half an hour.</summary>
    Pause,

    /// <summary>The hourly limit, or the site busy: everything waits as long as the site says.</summary>
    Wait,

    /// <summary>No answer (no connection, the site down): tried again later, waiting longer each time.</summary>
    Retry,
}

/// <summary>
/// What became of an upload: <see cref="Outcome"/>, the pull's id on the site when it's stored (<paramref name="Id"/>: the same id
/// <c>rp_plugin_send</c> gives, so a pull uploaded counts as sent), why not in words for players, how long the site asked to wait,
/// its hint, whether it was there already, whether it's public now, and whether it went thinned (bigger than the site takes whole).
/// Since 0.7.8, <paramref name="ThisPull"/>: a failure that's about this pull alone (its file couldn't be read, the site said no
/// to it with a 4xx it doesn't explain), not the connection or the site (no answer, a timeout, a 5xx, busy, the hourly limit).
/// </summary>
public sealed record UploadAnswer(UploadOutcome Outcome, string? Id = null, string? Why = null, TimeSpan? Wait = null, string? Hint = null,
    bool Duplicate = false, bool? Public = null, bool Thinned = false, bool ThisPull = false);

/// <summary>
/// Uploading a pull whole to the site's logs (since 0.6.0): <c>POST https://logs.kokos-tools.com/v1/upload</c>, the token as
/// <c>Authorization: Bearer</c> (the logs' Worker takes it there, never in the address; never logged), the body gzipped JSON
/// <c>{ v, clientId, name, summary, recording }</c>. Game-free; the queue is <see cref="UploadQueue"/>, the game side
/// <c>Game/LogUploader.cs</c>.
/// </summary>
public static class LogUpload
{
    /// <summary>Where pulls go.</summary>
    public const string Url = "https://logs.kokos-tools.com/v1/upload";

    /// <summary>The body's format.</summary>
    public const int BodyVersion = 1;

    /// <summary>
    /// The most the site takes once ungzipped (its 8 MB, counted in millions to be safe: the logs' Worker has 128 MB, and a
    /// recording read takes several times its size there; a bigger pull is thinned to fit, as a send is).
    /// </summary>
    public const int MaxRaw = 8_000_000;

    /// <summary>The most the site takes gzipped.</summary>
    public const int MaxGzip = 10_000_000;

    /// <summary>The most the recording may be (the body less room for the summary, its 8 KB, and the rest).</summary>
    public const int MaxRecording = MaxRaw - 64_000;

    /// <summary>How long the queue waits when saving to accounts is off on the site (<c>off</c>).</summary>
    public static readonly TimeSpan OffPause = TimeSpan.FromMinutes(30);

    /// <summary>The longest wait the site's <c>Retry-After</c> is taken for (its hourly limit's window, and a little).</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(65);

    /// <summary>How long an upload waits for the site: half a minute, and a quarter more for each MB sent.</summary>
    public static TimeSpan TimeoutFor(int bytes) => TimeSpan.FromSeconds(30 + 15.0 * bytes / 1_000_000);

    /// <summary>
    /// The recording as it's uploaded: whole when it's at most <see cref="MaxRecording"/> bytes (nearly every pull: a long one
    /// is a few MB), else thinned as a send is (<see cref="SiteLink.Fit(string, int, Func{string, int})"/>); null when even that's too big.
    /// </summary>
    public static (string Json, bool Thinned)? Fit(string recordingJson) => SiteLink.Fit(recordingJson, MaxRecording, SiteLink.SizeRule.Utf8);

    /// <summary>
    /// The body, gzipped: <c>{ "v": 1, "clientId", "name" (≤ 120), "summary" (the summary's JSON, or null), "recording" }</c>. The
    /// recording goes in as it's written (checked to be JSON as it's copied, never read into a tree of nodes), and so does the summary.
    /// </summary>
    /// <exception cref="JsonException">The recording or the summary isn't JSON.</exception>
    public static byte[] Body(string clientId, string name, string? summaryJson, string recordingJson)
    {
        using var ms = new MemoryStream(recordingJson.Length / 4 + 1024);
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new Utf8JsonWriter(gz))
        {
            w.WriteStartObject();
            w.WriteNumber("v", BodyVersion);
            w.WriteString("clientId", clientId);
            w.WriteString("name", name.Length > 120 ? name[..120] : name);
            w.WritePropertyName("summary");
            if (string.IsNullOrWhiteSpace(summaryJson)) w.WriteNullValue();
            else w.WriteRawValue(summaryJson);
            w.WritePropertyName("recording");
            w.WriteRawValue(recordingJson);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>The site's answer, read as the contract says (§1); <paramref name="retryAfter"/> from its <c>Retry-After</c> header.</summary>
    public static UploadAnswer Read(int status, string? body, TimeSpan? retryAfter)
    {
        JsonObject? o = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body)) o = JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's page): read by the status below.
        }
        var hint = Text(o?["hint"]);
        var said = Text(o?["error"]) is { Length: <= 300 } e ? e : null;
        var wait = retryAfter is { } ra ? Clamp(ra) : (TimeSpan?)null;

        if (status is >= 200 and < 300)
        {
            if (o?["ok"] is JsonValue ok && ok.TryGetValue<bool>(out var isOk) && isOk && Text(o["id"]) is { } id && SiteLink.IsUuid(id))
                return new UploadAnswer(UploadOutcome.Sent, id, Duplicate: Bool(o["duplicate"]),
                    Public: o["public"] is JsonValue pv && pv.TryGetValue<bool>(out var pub) ? pub : null);
            // An answer that isn't the contract's: tried again later (the client id keeps it once).
            return new UploadAnswer(UploadOutcome.Retry, Why: "The site answered, but not with the pull.");
        }
        switch (hint)
        {
            case "token":
                return new UploadAnswer(UploadOutcome.Stop, Why: (said ?? "The site doesn't know this token.") + " Link again in Settings with a new one.", Hint: hint);
            case "full":
                return new UploadAnswer(UploadOutcome.Stop, Why: said ?? "Your logs on the site are full. Delete some there to upload more.", Hint: hint);
            case "off":
                return new UploadAnswer(UploadOutcome.Pause, Why: said ?? "The site isn't taking pulls right now.", Wait: OffPause, Hint: hint);
            case "rate":
                return new UploadAnswer(UploadOutcome.Wait, Why: said ?? "The site's hourly limit is reached.", Wait: wait ?? TimeSpan.FromMinutes(5), Hint: hint);
            case "busy":
                return new UploadAnswer(UploadOutcome.Wait, Why: said ?? "The site is busy.", Wait: wait ?? TimeSpan.FromMinutes(1), Hint: hint);
            case "recording" or "size":
                return new UploadAnswer(UploadOutcome.Refused, Why: said ?? (hint == "size" ? "This pull is too big for the site." : "The site couldn't read this pull."), Hint: hint);
        }
        return status switch
        {
            401 => new UploadAnswer(UploadOutcome.Stop, Why: "The site doesn't know this token. Link again in Settings with a new one.", Hint: "token"),
            400 or 415 => new UploadAnswer(UploadOutcome.Refused, Why: said ?? "The site couldn't read this pull.", Hint: "recording"),
            413 => new UploadAnswer(UploadOutcome.Refused, Why: said ?? "This pull is too big for the site.", Hint: "size"),
            429 or 503 => new UploadAnswer(UploadOutcome.Wait, Why: "The site is busy.", Wait: wait ?? TimeSpan.FromMinutes(1)),
            404 => new UploadAnswer(UploadOutcome.Pause, Why: "The site can't take uploads right now. The plugin may need updating.", Wait: OffPause),
            >= 500 => new UploadAnswer(UploadOutcome.Retry, Why: $"The site had a problem ({status})."),
            // A 4xx the contract doesn't name: about this pull, not the site being down; but for those about the way there (a
            // firewall's 403, a proxy's 407, a timeout's 408, 421, 425), which any pull would get.
            _ => new UploadAnswer(UploadOutcome.Retry, Why: $"The site said no ({status}).",
                ThisPull: status is >= 400 and < 500 and not (403 or 407 or 408 or 421 or 425)),
        };
    }

    /// <summary>A wait as the site asked, at least a second and at most <see cref="LongestWait"/>.</summary>
    private static TimeSpan Clamp(TimeSpan t) => t < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : t > LongestWait ? LongestWait : t;

    /// <summary>The <c>Retry-After</c> header as a wait (its seconds, or its date less now), or null.</summary>
    public static TimeSpan? RetryAfter(RetryConditionHeaderValue? header, DateTimeOffset now) =>
        header?.Delta is { } d ? d : header?.Date is { } at ? at - now : null;

    /// <summary>
    /// Posts a body made by <see cref="Body"/> and says how it went; never throws (no connection or the wait running out is a
    /// <see cref="UploadOutcome.Retry"/>, the plugin unloading too, with "Stopped."). A token that isn't one never leaves.
    /// </summary>
    public static async Task<UploadAnswer> SendAsync(HttpClient http, string token, byte[] gzipped, string pluginVersion, CancellationToken cancel)
    {
        if (!SiteLink.LooksRight(token))
            return new UploadAnswer(UploadOutcome.Stop, Why: "Link your Raid Codex account in Settings first.", Hint: "token");
        if (gzipped.Length > MaxGzip) return new UploadAnswer(UploadOutcome.Refused, Why: "This pull is too big for the site.", Hint: "size");
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        wait.CancelAfter(TimeoutFor(gzipped.Length));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Url);
            var content = new ByteArrayContent(gzipped);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            content.Headers.ContentEncoding.Add("gzip");
            request.Content = content;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd($"RaidPlanner/{pluginVersion}");
            using var response = await http.SendAsync(request, wait.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(wait.Token).ConfigureAwait(false);
            return Read((int)response.StatusCode, text, RetryAfter(response.Headers.RetryAfter, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new UploadAnswer(UploadOutcome.Retry, Why: "The site didn't answer in time.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return new UploadAnswer(UploadOutcome.Retry, Why: "Stopped.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new UploadAnswer(UploadOutcome.Retry, Why: "Couldn't reach the site.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return new UploadAnswer(UploadOutcome.Retry, Why: "The upload couldn't be made.", ThisPull: true);
        }
    }

    private static string? Text(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    private static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}

/// <summary>What the store should note after an upload's answer: sent (with its id), refused for good (why), or nothing.</summary>
public readonly record struct UploadMark(string File, string? SentId, string? Refused);

/// <summary>
/// The queue of pulls to upload (since 0.6.0). It holds no list of its own: what's waiting is worked out each time from the kept
/// pulls (<see cref="RecordingStore"/>: those not sent and not refused, that the caller wants: Upload my logs on, linked, kept
/// since Upload since), so it's the same after a restart. One at a time, the oldest first; after no answer it waits longer each
/// time (<see cref="Backoff"/>: 30 seconds doubling to 15 minutes); the site's hourly limit and its being busy wait as long as it
/// says (<c>Retry-After</c>); saving switched off on the site waits half an hour; a refused token or a full account stops it
/// until <see cref="Resume"/> (linked again, Upload my logs turned on, Upload my older pulls). A pull that has failed
/// <see cref="TriesBeforeAside"/> times in a row (no answer, the site's error) is set aside for a while (half an hour, doubling
/// to <see cref="LongestAside"/>), so one the site can't take never holds up the ones after it. Since 0.7.8 setting one aside
/// lets the next go at once only when the failures were its own (<see cref="UploadAnswer.ThisPull"/>); when they were the
/// connection's or the site's, the wait keeps growing until an upload goes through, so an outage costs one try every 15 minutes,
/// not every waiting pull read, gzipped and sent in turn. A pull just uploaded or refused isn't picked again until the store shows
/// its mark (<see cref="Settled"/>). Game-free; on one thread.
/// </summary>
public sealed class UploadQueue
{
    /// <summary>Failures in a row before a pull is set aside and the next goes first.</summary>
    public const int TriesBeforeAside = 3;

    /// <summary>The first time a pull is set aside for (doubling each time after, up to <see cref="LongestAside"/>).</summary>
    public static readonly TimeSpan FirstAside = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan LongestAside = TimeSpan.FromHours(6);

    private readonly Backoff backoff = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));

    /// <summary>Each pull's failures in a row, and until when it's set aside (after <see cref="TriesBeforeAside"/>).</summary>
    private readonly Dictionary<string, (int Fails, DateTime Until)> failing = new(StringComparer.OrdinalIgnoreCase);
    private DateTime notBefore = DateTime.MinValue;
    private TimeSpan notBeforeWait;

    /// <summary>
    /// Since 0.7.8: pulls uploaded or refused whose mark the store doesn't show yet (it's written on a worker after
    /// <see cref="Done"/>): never picked again meanwhile. Each goes once the store shows it marked (or not kept), or
    /// <see cref="Settled"/> says the mark failed.
    /// </summary>
    private readonly HashSet<string> marking = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The file on its way (null: none).</summary>
    public string? InFlight { get; private set; }

    /// <summary>Why uploading stopped (a refused token, a full account), in the site's words; null while it goes on.</summary>
    public string? Stopped { get; private set; }

    /// <summary>The last thing that held it up (the hourly limit, no connection), in words; null after a success.</summary>
    public string? Held { get; private set; }

    /// <summary>When it may go on (after a wait the site asked for, or a pause; <see cref="DateTime.MinValue"/>: now).</summary>
    public DateTime NotBefore => notBefore > backoff.Until ? notBefore : backoff.Until;

    /// <summary>How many uploads have gone since the plugin loaded (stored or already there).</summary>
    public int SentCount { get; private set; }

    /// <summary>The pulls waiting, oldest first.</summary>
    public static List<RecordingInfo> Waiting(IReadOnlyList<RecordingInfo> all, Func<RecordingInfo, bool> wanted) =>
        all.Where(i => i.SentId == null && i.UploadRefused == null && wanted(i)).OrderBy(i => i.RecordedAt).ToList();

    /// <summary>
    /// The next pull to upload now, or null (one on its way, stopped, waiting, nothing to send). The one returned is on its way
    /// until <see cref="Done"/>.
    /// </summary>
    public RecordingInfo? Next(IReadOnlyList<RecordingInfo> all, DateTime now, Func<RecordingInfo, bool> wanted)
    {
        if (InFlight != null || Stopped != null || !backoff.Ready(now)) return null;
        // A clock set back by more than the wait counts as waited (as Backoff does).
        if (now < notBefore && notBefore - now <= notBeforeWait) return null;
        if (marking.Count > 0) ForgetMarked(all);
        RecordingInfo? oldest = null;
        foreach (var i in all)
            if (i.SentId == null && i.UploadRefused == null && (oldest == null || i.RecordedAt < oldest.RecordedAt) && !Aside(i.File, now)
                && !marking.Contains(i.File) && wanted(i))
                oldest = i;
        if (oldest != null) InFlight = oldest.File;
        return oldest;
    }

    /// <summary>
    /// How the upload of <paramref name="file"/> went; returns what the store should note (nothing for an answer to a call that
    /// isn't the one on its way, which is ignored).
    /// </summary>
    public UploadMark? Done(string file, UploadAnswer answer, DateTime now)
    {
        if (InFlight == null || !string.Equals(InFlight, file, StringComparison.OrdinalIgnoreCase)) return null;
        InFlight = null;
        switch (answer.Outcome)
        {
            case UploadOutcome.Sent:
                backoff.Succeeded();
                Held = null;
                SentCount++;
                failing.Remove(file);
                marking.Add(file);
                return new UploadMark(file, answer.Id, null);
            case UploadOutcome.Refused:
                failing.Remove(file);
                marking.Add(file);
                return new UploadMark(file, null, answer.Why ?? "The site didn't take it.");
            case UploadOutcome.Stop:
                Stopped = answer.Why ?? "The site stopped the uploads.";
                return null;
            case UploadOutcome.Pause or UploadOutcome.Wait:
                Hold(now, answer.Wait ?? LogUpload.OffPause);
                // The site busy: longer each time it's still busy (its Retry-After at least).
                if (answer.Hint != "rate") backoff.Failed(now);
                // Busy (an error page, or the site's own): this pull counts a failure; the hourly limit and saving off don't.
                // The site's, not the pull's: setting it aside doesn't cut the wait short.
                if (answer.Outcome == UploadOutcome.Wait && answer.Hint != "rate") Failed(file, now, answer.ThisPull);
                Held = answer.Why;
                return null;
            default:
                // "Stopped." is the plugin unloading: nothing to wait for.
                if (answer.Why == "Stopped.") return null;
                backoff.Failed(now);
                Failed(file, now, answer.ThisPull);
                Held = answer.Why;
                return null;
        }
    }

    /// <summary>
    /// Since 0.7.8: the store's mark for <paramref name="file"/> failed (or won't come): it may be picked again. Without this it's
    /// let go of once the store shows it marked.
    /// </summary>
    public void Settled(string file) => marking.Remove(file);

    /// <summary>Lets go of the pulls just done that the store now shows marked (or no longer keeps).</summary>
    private void ForgetMarked(IReadOnlyList<RecordingInfo> all)
    {
        var still = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in all)
            if (i.SentId == null && i.UploadRefused == null && marking.Contains(i.File))
                still.Add(i.File);
        marking.IntersectWith(still);
    }

    /// <summary>Whether a pull is set aside now (a clock set back by more than the longest counts as over).</summary>
    public bool Aside(string file, DateTime now) =>
        failing.TryGetValue(file, out var f) && f.Until > now && f.Until - now <= LongestAside;

    /// <summary>A failure for <paramref name="file"/>; <paramref name="ownFault"/>: about this pull alone, not the connection or the site.</summary>
    private void Failed(string file, DateTime now, bool ownFault)
    {
        var fails = (failing.TryGetValue(file, out var f) ? f.Fails : 0) + 1;
        var until = DateTime.MinValue;
        if (fails >= TriesBeforeAside)
        {
            var times = Math.Min(fails - TriesBeforeAside, 8);
            var wait = TimeSpan.FromTicks(Math.Min(LongestAside.Ticks, FirstAside.Ticks << times));
            until = now + wait;
            // Set aside for its own failures: the next pull goes after the usual wait, not this one's. For the connection's or
            // the site's (since 0.7.8), the wait keeps growing: only an upload that goes through starts it over.
            if (ownFault) backoff.Succeeded();
        }
        failing[file] = (fails, until);
    }

    /// <summary>Goes on from where it stopped or waited (linked again, Upload my logs turned on, Upload my older pulls).</summary>
    public void Resume()
    {
        failing.Clear();
        Stopped = null;
        Held = null;
        notBefore = DateTime.MinValue;
        notBeforeWait = TimeSpan.Zero;
        backoff.Succeeded();
    }

    /// <summary>The call on its way is let go of (its answer will be ignored): the account changed, or it couldn't start.</summary>
    public void Drop() => InFlight = null;

    private void Hold(DateTime now, TimeSpan wait)
    {
        notBeforeWait = wait;
        notBefore = now + wait;
    }
}

/// <summary>The settings that say what's uploaded (since 0.6.0; the configuration's): its version, Upload my logs, and since when.</summary>
public sealed record UploadPrefs(int Version, bool UploadLogs, DateTimeOffset? UploadSince)
{
    /// <summary>The configuration's version since 0.6.0 (1 before: Send each pull, no uploads).</summary>
    public const int Current = 2;

    /// <summary>
    /// The settings as 0.6.0 has them, from what was loaded: a 0.5.x configuration (version 1) gets Upload my logs on (it replaces
    /// Send each pull, which was off by default; every pull is uploaded now, the site's decision) from now on, so the pulls kept
    /// before aren't uploaded unless asked (Upload my older pulls); a new one (no Upload since yet) starts from now too.
    /// </summary>
    public static UploadPrefs Migrate(UploadPrefs loaded, DateTimeOffset now)
    {
        if (loaded.Version < Current) return new UploadPrefs(Current, true, now);
        return loaded.UploadSince == null ? loaded with { UploadSince = now } : loaded;
    }

    /// <summary>
    /// Is a kept pull one Upload my logs sends (with it on and linked)? Not sent yet, not refused, and ended at or after
    /// <see cref="UploadSince"/> (<see cref="DateTimeOffset.MinValue"/> after Upload my older pulls: every kept pull).
    /// </summary>
    public bool Wants(RecordingInfo info) =>
        UploadLogs && info.SentId == null && info.UploadRefused == null
        && (UploadSince is not { } since || since == DateTimeOffset.MinValue || info.RecordedAt.AddSeconds(Math.Max(0, Finite(info.Duration))) >= since);

    /// <summary>Kept pulls not sent that ended before <see cref="UploadSince"/> (what Upload my older pulls would add).</summary>
    public int Older(IReadOnlyList<RecordingInfo> all) =>
        UploadSince is not { } since || since == DateTimeOffset.MinValue ? 0
        : all.Count(i => i.SentId == null && i.UploadRefused == null && i.RecordedAt.AddSeconds(Math.Max(0, Finite(i.Duration))) < since);

    private static double Finite(double d) => double.IsFinite(d) ? d : 0;
}
