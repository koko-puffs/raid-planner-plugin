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
/// Sending a pull to your Raid Planner, with the token made on the site (Settings → Raid Planner
/// → In-game plugin), as Housing Studio's plugin sends a house. The plugin calls the site's
/// database function <c>rp_plugin_send</c> with the site's publishable key, as the site itself
/// would; the token says whose account it goes to, and it waits there under From the game until
/// it's made a plan. The token is never logged.
/// </summary>
public static class SiteLink
{
    /// <summary>The site's database (Supabase): its address and its publishable key, public by design.</summary>
    public const string SupabaseUrl = "https://upjcxjuuzxujqddtwehn.supabase.co";

    public const string PublishableKey = "sb_publishable_A-5zpDIk5O6xyKTicipcuA_M3J0sGVP";

    /// <summary>The Raid Planner.</summary>
    public const string PlannerUrl = "https://www.kokos-tools.com/raid-planner";

    /// <summary>Where a token is made: Settings → Raid Planner → In-game plugin.</summary>
    public const string TokensUrl = "https://www.kokos-tools.com/raid-planner?settings=raid-plugin";

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
    /// The recording as it's sent: whole when it fits; else frames go first (the places kept less
    /// often: every second, then every two, then four; then the enemies' places only, at those steps; then
    /// none), then the combat block's casts, then its ticks (never its effects or statuses), the block
    /// saying what it lost (<c>combat.thinned</c>). Null when even that's too big (a pull with more
    /// events than the site takes).
    /// </summary>
    public static (string Json, bool Thinned)? Fit(string json)
    {
        if (ServerSize(json) <= MaxBytes) return (json, false);
        Recording r;
        try
        {
            r = Recording.Parse(json);
        }
        catch (FormatException)
        {
            return null;
        }
        var frames = r.Frames;
        for (var step = 2; step <= 8; step *= 2)
        {
            var thinned = r.WithFrames(frames.Where((_, i) => i % step == 0), r.PartyPositions).ToJson();
            if (ServerSize(thinned) <= MaxBytes) return (thinned, true);
        }
        for (var step = 2; step <= 8; step *= 2)
        {
            var every = step;
            var enemies = r.WithFrames(
                frames.Where((_, i) => i % every == 0)
                    .Select(f => f with
                    {
                        At = f.At.Where(kv => kv.Key > 100).ToDictionary(kv => kv.Key, kv => kv.Value),
                        Hp = f.Hp?.Where(kv => kv.Key > 100).ToDictionary(kv => kv.Key, kv => kv.Value),
                    })
                    .Where(f => f.At.Count > 0),
                false).ToJson();
            if (ServerSize(enemies) <= MaxBytes) return (enemies, true);
        }
        var none = r.WithFrames(Array.Empty<Frame>(), false).ToJson();
        if (ServerSize(none) <= MaxBytes) return (none, true);
        if (r.Combat == null) return null;
        // Then the combat block's casts, then its ticks.
        var noCasts = r.With(Array.Empty<Frame>(), false, r.Combat.Without("cast")).ToJson();
        if (ServerSize(noCasts) <= MaxBytes) return (noCasts, true);
        var noTicks = r.With(Array.Empty<Frame>(), false, r.Combat.Without("cast", "tick")).ToJson();
        return ServerSize(noTicks) <= MaxBytes ? (noTicks, true) : null;
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

    /// <summary>Where a sent pull opens: the planner, with it ready to make into a plan.</summary>
    public static string OpenUrl(string id) => $"{PlannerUrl}?from-game={Uri.EscapeDataString(id)}";

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
                : new SendResult(null, "The site answered, but not with the pull. Look in the Raid Planner before sending it again.");
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
    /// Sends a pull to the token's owner's Raid Planner and says how it went; never throws (a lost
    /// connection or the wait running out is said in words). The token goes in the body only.
    /// </summary>
    public static async Task<SendResult> SendAsync(HttpClient http, string token, string name, string recordingJson, string? clientId = null,
        CancellationToken cancel = default)
    {
        if (!LooksRight(token)) return new SendResult(null, "That isn't a Raid Planner token. Link again in Settings.", true);
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
            return new SendResult(null, "The site didn't answer in time. Look in the Raid Planner before sending it again.");
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
