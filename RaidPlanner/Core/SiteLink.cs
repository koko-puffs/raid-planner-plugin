using System;
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
public sealed record SendResult(string? Id, string? Why, bool TokenRefused = false)
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

    /// <summary>The longest a send waits for the site (a long pull is a big file).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    /// <summary>The most the site takes in one recording (its own limit is the same).</summary>
    public const int MaxBytes = 3_000_000;

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

    /// <summary>What's sent: the token, the pull's name and the recording (its file's JSON, as an object).</summary>
    public static string Body(string token, string name, string recordingJson) =>
        new JsonObject
        {
            ["p_token"] = token,
            ["p_name"] = name,
            ["p_recording"] = JsonNode.Parse(recordingJson),
        }.ToJsonString();

    /// <summary>Words that are the database's, not the site's (a missing function, a bad value): not for players.</summary>
    private static readonly string[] Internal =
    {
        "function", "column", "relation", "syntax", "violates", "permission denied", "schema", "operator does not exist", "invalid input",
    };

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
        if (hint is "off" or "rate" or "size" or "recording" && message != null)
            return new SendResult(null, message);
        if (message != null && code is 400 or 403 or 409 or 500 && message.Length <= 200
            && !Array.Exists(Internal, w => message.Contains(w, StringComparison.OrdinalIgnoreCase)))
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

    private static string? Text(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    /// <summary>
    /// Sends a pull to the token's owner's Raid Planner and says how it went; never throws (a lost
    /// connection or the wait running out is said in words). The token goes in the body only.
    /// </summary>
    public static async Task<SendResult> SendAsync(HttpClient http, string token, string name, string recordingJson, CancellationToken cancel = default)
    {
        if (!LooksRight(token)) return new SendResult(null, "That isn't a Raid Planner token. Link again in Settings.", true);
        if (Encoding.UTF8.GetByteCount(recordingJson) > MaxBytes)
            return new SendResult(null, "This pull is too big to send. Save it as a file instead, and import it on the site.");
        string body;
        try
        {
            body = Body(token, name, recordingJson);
        }
        catch (JsonException)
        {
            return new SendResult(null, "The pull couldn't be written down to send.");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/rest/v1/rpc/rp_plugin_send")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("apikey", PublishableKey);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Read(response.StatusCode, text);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new SendResult(null, "The site didn't answer in time. Look in the Raid Planner before sending it again.");
        }
        catch (OperationCanceledException)
        {
            return new SendResult(null, "Stopped.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new SendResult(null, "Couldn't reach the site. Check your connection and try again.");
        }
    }
}
