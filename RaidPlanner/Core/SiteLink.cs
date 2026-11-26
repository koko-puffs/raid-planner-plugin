using System;
using System.Linq;
using System.Security.Cryptography;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RaidPlanner.Core;

/// <summary>What became of a send: the pull's id on the site, or why it didn't go.</summary>
/// <param name="TokenRefused">The site doesn't know the token (revoked, or mistyped): link again.</param>
/// <param name="Thinned">It was too big whole, so it went with fewer of the party's places.</param>
public sealed record SendResult(string? Id, string? Why, bool TokenRefused = false, bool Thinned = false)
{
    public bool Sent => Id != null;
}

/// <summary>
/// Sending a pull to your Raid Codex, with the token made on the site (Settings → Raid Codex
/// → In-game plugin), as Decorium's plugin sends a house. The plugin calls the site's
/// database function <c>rp_plugin_send</c> with the site's publishable key, as the site itself
/// would; the token says whose account it goes to, and it waits there under From the game until
/// it's made a plan. The token is never logged.
/// </summary>
public static class SiteLink
{
    /// <summary>The site's database (Supabase): its address and its publishable key, public by design.</summary>
    public const string SupabaseUrl = "https://upjcxjuuzxujqddtwehn.supabase.co";

    public const string PublishableKey = "sb_publishable_A-5zpDIk5O6xyKTicipcuA_M3J0sGVP";

    /// <summary>The Raid Codex.</summary>
    public const string CodexUrl = "https://www.kokos-tools.com/raid-codex";

    /// <summary>Where a token is made: Settings → Raid Codex → In-game plugin.</summary>
    public const string TokensUrl = "https://www.kokos-tools.com/raid-codex?settings=raid-plugin";

    /// <summary>Where you make your logs private, and hide your character (since 0.6.0): Settings → Your logs.</summary>
    public const string LogsSettingsUrl = "https://www.kokos-tools.com/raid-codex?settings=raid-logs";

    /// <summary>How long a send waits for the site: half a minute, and a quarter more for each MB (a slow upload).</summary>
    public static TimeSpan TimeoutFor(int bytes) => TimeSpan.FromSeconds(30 + 15.0 * bytes / 1_000_000);

    /// <summary>
    /// The most the site takes in one recording, as the database counts it (<see cref="ServerSize"/>):
    /// its own limit is the same number (5 MB since round three's migration, for the combat block; 3 MB before).
    /// </summary>
    public const int MaxBytes = 5_000_000;

    /// <summary>
    /// A recording's size as the database counts it: its JSON written back out, which puts a space
    /// after every colon and comma (outside the strings). The file itself is compact, so it's
    /// smaller than this by about a sixth.
    /// </summary>
    public static int ServerSize(string json)
    {
        var size = Encoding.UTF8.GetByteCount(json);
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
            }
            else if (c == '"') inString = true;
            else if (c is ':' or ',') size++;
        }
        return size;
    }

    /// <summary>
    /// The recording as it's sent: whole when it fits; else, each step only while it's still too big (0.4.0's
    /// order, the site's contract §3.5): (1) a file with four frames a second down to two; (2) the combat block's
    /// enmity list (<c>hate</c>); (3) its MP and barriers (<c>res</c>); (4) the frames kept less often (every
    /// second, then every two, then four; then the enemies' places only, at those steps; then none); (5) its
    /// ground placements (<c>area</c>); (6) the party's casts (<c>cast</c>); (7) what the party used (<c>use</c>);
    /// (8) the ticks (<c>tick</c>). Frames are thinned by time (a frame kept as it crosses each step), never by
    /// count. Never the effects (<c>fx</c>), statuses, gauge, pre-pull, countdown, items or events. The block
    /// says what it lost (<c>combat.thinned</c>). Null when even that's too big (a pull with more events than
    /// the site takes).
    /// </summary>
    public static (string Json, bool Thinned)? Fit(string json) => Fit(json, MaxBytes, SizeRule.Server);

    /// <summary>
    /// <see cref="Fit(string)"/> to another limit, measured another way (since 0.6.0: an upload's, <see cref="LogUpload.Fit"/>, by its
    /// UTF-8 bytes up to 8 MB).
    /// </summary>
    public static (string Json, bool Thinned)? Fit(string json, int max, Func<string, int> size)
    {
        if (size(json) <= max) return (json, false);
        if (Steps(json) is not { } steps) return null;
        foreach (var candidate in steps)
        {
            var text = candidate.ToJson();
            if (size(text) <= max) return (text, true);
        }
        return null;
    }

    /// <summary>How a recording's size is counted: its UTF-8 bytes (an upload's), or as the database counts it (<see cref="ServerSize"/>, a send's).</summary>
    public enum SizeRule
    {
        Utf8,
        Server,
    }

    /// <summary>
    /// <see cref="Fit(string, int, Func{string, int})"/> with <paramref name="rule"/>'s count, the same answer to the byte
    /// (tested: <c>ThinningTests</c>), but each step only measured, as its JSON is written into a counter, not made into a
    /// string: only the one that fits is. Since 0.7.6: a long pull's file thinned up to a dozen times left hundreds of
    /// megabytes of strings behind, and collecting them paused the game.
    /// </summary>
    public static (string Json, bool Thinned)? Fit(string json, int max, SizeRule rule)
    {
        if (Measure(json, rule) <= max) return (json, false);
        if (Steps(json) is not { } steps) return null;
        foreach (var candidate in steps)
            if (Measure(candidate, rule) <= max)
                return (candidate.ToJson(), true);
        return null;
    }

    /// <summary>A recording's JSON's size by <paramref name="rule"/>, counted as it's written (nothing kept).</summary>
    public static long Measure(Recording r, SizeRule rule)
    {
        using var counter = new JsonCounter(rule == SizeRule.Server);
        using (var w = new System.Text.Json.Utf8JsonWriter(counter, new System.Text.Json.JsonWriterOptions { Indented = false }))
            r.Write(w);
        return counter.Size;
    }

    /// <summary>A JSON text's size by <paramref name="rule"/>.</summary>
    public static long Measure(string json, SizeRule rule) => rule == SizeRule.Server ? ServerSize(json) : Encoding.UTF8.GetByteCount(json);

    /// <summary>
    /// The JSON's bytes counted as they're written, and (for <see cref="SizeRule.Server"/>) one more for each colon and comma
    /// outside a string, as <see cref="ServerSize"/> counts them: a UTF-8 byte below 128 is that character, so reading bytes is
    /// reading characters here.
    /// </summary>
    private sealed class JsonCounter : System.IO.Stream
    {
        private readonly bool server;
        private bool inString;
        private bool escaped;

        public JsonCounter(bool server) => this.server = server;

        public long Size { get; private set; }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Size += buffer.Length;
            if (!server) return;
            foreach (var c in buffer)
            {
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == (byte)'\\') escaped = true;
                    else if (c == (byte)'"') inString = false;
                }
                else if (c == (byte)'"') inString = true;
                else if (c is (byte)':' or (byte)',') Size++;
            }
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Flush()
        {
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Size;
        public override long Position { get => Size; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>
    /// The thinning's steps in order (see <see cref="Fit(string)"/>), each the recording as it would be sent at that step: the
    /// first that fits is the one sent. Null when the JSON isn't a recording. Made one at a time, as they're asked for.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<Recording>? Steps(string json)
    {
        Recording r;
        try
        {
            r = Recording.Parse(json);
        }
        catch (FormatException)
        {
            return null;
        }
        return StepsOf(r);
    }

    internal static System.Collections.Generic.IEnumerable<Recording> StepsOf(Recording r)
    {
        var drops = new System.Collections.Generic.List<string>();
        Recording Try(System.Collections.Generic.IEnumerable<Frame> frames, bool positions) =>
            r.With(frames, positions, r.Combat == null || drops.Count == 0 ? r.Combat : r.Combat.Without(drops.ToArray()));

        var frames = r.Frames;
        // (1) Smoother replays' four frames a second, down to two.
        if (frames.Count >= 2 && (frames[^1].T - frames[0].T) / (frames.Count - 1) < 0.4)
        {
            frames = ByTime(frames, RecordingBuilder.FrameEvery);
            yield return Try(frames, r.PartyPositions);
        }
        // (2), (3) The enmity list, then MP and barriers.
        if (r.Combat != null)
            foreach (var table in new[] { "hate", "res" })
            {
                // A table with nothing in it isn't dropped (nor said to be).
                if (Rows(r.Combat, table) == 0) continue;
                drops.Add(table);
                yield return Try(frames, r.PartyPositions);
            }
        // (4) The frames: every 1, 2, 4 seconds; the enemies only, so; none.
        foreach (var step in new[] { 1.0, 2.0, 4.0 })
            yield return Try(ByTime(frames, step), r.PartyPositions);
        foreach (var step in new[] { 1.0, 2.0, 4.0 })
            yield return Try(
                ByTime(frames, step)
                    .Select(f => f with
                    {
                        At = f.At.Where(kv => kv.Key > 100).ToDictionary(kv => kv.Key, kv => kv.Value),
                        Hp = f.Hp?.Where(kv => kv.Key > 100).ToDictionary(kv => kv.Key, kv => kv.Value),
                    })
                    .Where(f => f.At.Count > 0),
                false);
        yield return Try(Array.Empty<Frame>(), false);
        if (r.Combat == null) yield break;
        // (5)–(8) Then the ground placements, the casts, the uses, the ticks.
        foreach (var table in new[] { "area", "cast", "use", "tick" })
        {
            if (Rows(r.Combat, table) == 0) continue;
            drops.Add(table);
            yield return Try(Array.Empty<Frame>(), false);
        }
    }

    private static int Rows(CombatLog c, string table) => table switch
    {
        "hate" => c.Hate.Count,
        "res" => c.Res.Count,
        "area" => c.Areas.Count,
        "cast" => c.Casts.Count,
        "use" => c.Uses.Count,
        "tick" => c.Ticks.Count,
        _ => 0,
    };

    /// <summary>The frames kept as they cross each multiple of <paramref name="step"/> seconds (the first in each step), in order.</summary>
    public static System.Collections.Generic.List<Frame> ByTime(System.Collections.Generic.IReadOnlyList<Frame> frames, double step)
    {
        var kept = new System.Collections.Generic.List<Frame>();
        var last = long.MinValue;
        foreach (var f in frames)
        {
            // A hundredth's slack: a frame due at 1.0 read at 0.999 is the 1.0's.
            var bucket = (long)Math.Floor((f.T + 0.01) / step);
            if (bucket <= last) continue;
            last = bucket;
            kept.Add(f);
        }
        return kept;
    }

    /// <summary>
    /// The pull's own id for the site, made from its file and when it was recorded: the same pull sent
    /// twice (a send that timed out after it arrived, then sent again) is kept once.
    /// </summary>
    public static string ClientId(string file, DateTimeOffset recordedAt)
    {
        // To the second, as the file has it: the same after the list is made again from the files.
        var at = recordedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{file}|{at}"));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    private static readonly Regex Shape = new(@"^rpp_[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant);

    private static readonly Regex Uuid = new(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant);

    /// <summary>A token as pasted, without the spaces, quotes or line breaks that come with a copy.</summary>
    public static string Clean(string? pasted) => (pasted ?? string.Empty).Trim().Trim('"', '\'', '`').Trim();

    /// <summary>Does it look like a token the site makes ("rpp_" and 43 letters)?</summary>
    public static bool LooksRight(string? token) => token != null && Shape.IsMatch(token);

    /// <summary>A token shown without giving it away: "rpp_••••••••3xQz".</summary>
    public static string Masked(string? token) =>
        string.IsNullOrEmpty(token) ? string.Empty
        : token.Length <= 8 ? new string('•', 8)
        : (token.StartsWith("rpp_", StringComparison.Ordinal) ? "rpp_" : string.Empty) + new string('•', 8) + token[^4..];

    /// <summary>
    /// A pull of yours on the site by its id (the one a send or an upload gives back): its night's report with the pull picked
    /// (the site's <c>/raid-codex/pulls/:id</c>, which finds the night), on <paramref name="view"/> when given (its Replay,
    /// <c>replay</c>). Open the pull, and Open on the site, go here (0.9.1; Review on a plan, or From the game, before).
    /// </summary>
    public static string PullUrl(string id, string? view = null) =>
        $"{CodexUrl}/pulls/{Uri.EscapeDataString(id)}" + (view != null ? $"?view={Uri.EscapeDataString(view)}" : string.Empty);

    public static bool IsUuid(string? id) => id != null && Uuid.IsMatch(id);

    /// <summary>
    /// What's sent: the token, the pull's name, its own id and the recording (its file's JSON, as an
    /// object). The recording goes in as it's written (checked to be JSON as it's copied), never read into
    /// a tree of nodes first: a big pull's tree is a hundred MB or more that the game's process would have
    /// to collect afterwards.
    /// </summary>
    /// <exception cref="JsonException">The recording isn't JSON.</exception>
    public static string Body(string token, string name, string recordingJson, string? clientId = null)
    {
        using var ms = new MemoryStream(recordingJson.Length + 512);
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("p_token", token);
            w.WriteString("p_name", name.Length > 120 ? name[..120] : name);
            w.WritePropertyName("p_recording");
            w.WriteRawValue(recordingJson);
            if (clientId != null) w.WriteString("p_client_id", clientId);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    /// <summary>
    /// What the site answered, in words: the new pull's id (a quoted id), or its refusal's message
    /// (the database's own words, written to be shown) with what to do about a refused token.
    /// </summary>
    public static SendResult Read(HttpStatusCode status, string? body)
    {
        var code = (int)status;
        JsonNode? node = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body)) node = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's page): said by the status below.
        }

        if (code is >= 200 and < 300)
        {
            var id = node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            return IsUuid(id)
                ? new SendResult(id, null)
                : new SendResult(null, "The site answered, but not with the pull. Look in the Raid Codex before sending it again.");
        }

        string? message = null, hint = null;
        if (node is JsonObject o)
        {
            message = Text(o["message"]);
            hint = Text(o["hint"]);
        }
        if (hint == "token")
            return new SendResult(null, (message ?? "The site doesn't know this token.") + " Link again in Settings with a new one.", true);
        if (SiteRpc.Worded(hint) && message != null && message.Length <= 300)
            return new SendResult(null, message);
        return code switch
        {
            401 or 403 => new SendResult(null, "The site turned the plugin away. It may need updating."),
            404 => new SendResult(null, "The site can't take pulls from the plugin right now. The plugin may need updating."),
            413 => new SendResult(null, "This pull is too big to send. Save it as a file instead."),
            429 => new SendResult(null, "The site is busy. Try again in a minute."),
            >= 500 => new SendResult(null, $"The site had a problem ({code}). Try again in a minute."),
            _ => new SendResult(null, $"The site said no ({code})."),
        };
    }

    /// <summary>Posts through the plugin's one way of calling the site (<see cref="SiteRpc"/>).</summary>
    private static Task<(HttpStatusCode Status, string Text)> PostAsync(HttpClient http, string body, CancellationToken cancel) =>
        SiteRpc.PostAsync(http, "rp_plugin_send", body, cancel);

    private static string? Text(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    /// <summary>
    /// Sends a pull to the token's owner's Raid Codex and says how it went; never throws (a lost
    /// connection or the wait running out is said in words). The token goes in the body only.
    /// </summary>
    public static async Task<SendResult> SendAsync(HttpClient http, string token, string name, string recordingJson, string? clientId = null,
        CancellationToken cancel = default)
    {
        if (!LooksRight(token)) return new SendResult(null, "That isn't a Raid Codex token. Link again in Settings.", true);
        if (Fit(recordingJson) is not { } fit)
            return new SendResult(null, "This pull is too big to send. Save it as a file instead, and import it on the site.");
        string body;
        try
        {
            body = Body(token, name, fit.Json, clientId);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return new SendResult(null, "The pull couldn't be written down to send.");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeoutFor(Encoding.UTF8.GetByteCount(body)));
        try
        {
            var (status, text) = await PostAsync(http, body, timeout.Token).ConfigureAwait(false);
            // A site that doesn't take the pull's own id yet (its database not updated): sent again without it.
            if (status == HttpStatusCode.NotFound && clientId != null)
                (status, text) = await PostAsync(http, Body(token, name, fit.Json), timeout.Token).ConfigureAwait(false);
            var read = Read(status, text);
            return read.Sent && fit.Thinned ? read with { Thinned = true } : read;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new SendResult(null, "The site didn't answer in time. Look in the Raid Codex before sending it again.");
        }
        catch (OperationCanceledException)
        {
            return new SendResult(null, "Stopped.");
        }
        catch (ObjectDisposedException)
        {
            return new SendResult(null, "Stopped.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new SendResult(null, "Couldn't reach the site. Check your connection and try again.");
        }
    }
}
