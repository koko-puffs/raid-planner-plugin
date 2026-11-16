using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using RaidPlanner.Core;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>0.6.0: the recording's names, worlds, levels, game version and server time; uploading every pull to the site's logs.</summary>
public class SixRecordingTests
{
    [Fact]
    public void Party_names_worlds_levels_game_and_server_time_go_through_the_file()
    {
        var r = RecordingTests.Sample();
        var json = r.ToJson();
        var o = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("2026.10.30.0000.0000", (string?)o["game"]);
        Assert.Equal("2026-11-02T19:59:58.640Z", (string?)o["startedAt"]);
        Assert.Equal("2026-11-02T20:00:00Z", (string?)o["recordedAt"]);
        var first = o["party"]![0]!.AsObject();
        Assert.Equal("Alpha Tester", (string?)first["name"]);
        Assert.Equal("Twintania", (string?)first["world"]);
        Assert.Equal(100, (int)first["lvl"]!);
        var blm = o["party"]!.AsArray().Single(p => (string?)p!["job"] == "BLM")!;
        Assert.Equal(99, (int)blm["lvl"]!);
        Assert.True((bool)blm["self"]!);

        var back = Recording.Parse(json);
        Assert.Equal("2026.10.30.0000.0000", back.GameVersion);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 19, 59, 58, 640, TimeSpan.Zero), back.StartedAt);
        Assert.Equal(("Alpha Tester", "Twintania", 100), (back.Party[0].Name, back.Party[0].World, back.Party[0].Level));
        Assert.Equal(json, back.ToJson());
        // A copy for sending (thinned frames) keeps them.
        var copy = back.WithFrames(Array.Empty<Frame>(), false);
        Assert.Equal(back.GameVersion, copy.GameVersion);
        Assert.Equal(back.StartedAt, copy.StartedAt);
    }

    [Fact]
    public void An_older_file_has_none_and_writes_none()
    {
        var o = JsonNode.Parse(RecordingTests.Sample().ToJson())!.AsObject();
        o.Remove("game");
        o.Remove("startedAt");
        foreach (var p in o["party"]!.AsArray())
        {
            p!.AsObject().Remove("name");
            p.AsObject().Remove("world");
            p.AsObject().Remove("lvl");
        }
        var r = Recording.Parse(o.ToJsonString());
        Assert.Null(r.GameVersion);
        Assert.Null(r.StartedAt);
        Assert.All(r.Party, p => Assert.True(p.Name == null && p.World == null && p.Level == 0));
        var again = JsonNode.Parse(r.ToJson())!.AsObject();
        Assert.False(again.ContainsKey("game"));
        Assert.False(again.ContainsKey("startedAt"));
        Assert.All(again["party"]!.AsArray(), p => Assert.False(p!.AsObject().ContainsKey("name") || p.AsObject().ContainsKey("world") || p.AsObject().ContainsKey("lvl")));
    }

    [Fact]
    public void Names_are_read_leniently()
    {
        var o = JsonNode.Parse(RecordingTests.Sample().ToJson())!.AsObject();
        var party = o["party"]!.AsArray();
        party[0]!["name"] = "  " + new string('x', 60) + "  ";
        party[1]!["name"] = 12;
        party[1]!["world"] = "   ";
        party[2]!["lvl"] = -3;
        party[3]!["lvl"] = "90";
        o["game"] = "";
        o["startedAt"] = "not a time";
        var r = Recording.Parse(o.ToJsonString());
        Assert.Equal(PartyMember.MostName, r.Party[0].Name!.Length);
        Assert.Null(r.Party[1].Name);
        Assert.Null(r.Party[1].World);
        Assert.Equal(0, r.Party[2].Level);
        Assert.Equal(0, r.Party[3].Level);
        Assert.Null(r.GameVersion);
        Assert.Null(r.StartedAt);
    }

    [Fact]
    public void The_builder_keeps_whoever_had_no_name_without_one()
    {
        var b = new RecordingBuilder(new DutyInfo("Somewhere", 1, 2), 1, DateTimeOffset.UnixEpoch, true, "0.6.0", _ => null);
        b.SetParty(new[] { new Partier(1, "WAR", 1, 0, "Some One", "Lich", 100), new Partier(2, "WHM", 4) });
        b.SetGame("  ", null);
        Assert.Equal("Some One", b.Finish(1, Outcome.Ended).Party[0].Name);
        var r = b.Finish(1, Outcome.Ended);
        Assert.Null(r.Party[1].Name);
        Assert.Null(r.GameVersion);
        Assert.Null(r.StartedAt);
    }
}

public class ServerClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Before_a_tick_over_it_is_the_whole_second()
    {
        var c = new ServerClock();
        var s = T0.ToUnixTimeSeconds();
        c.Observe(s, T0.AddMilliseconds(300));
        Assert.False(c.Fine);
        Assert.Equal(T0, c.Now(s, T0.AddMilliseconds(500)));
        Assert.Null(c.Now(0, T0));
    }

    [Fact]
    public void A_tick_over_gives_the_milliseconds()
    {
        var c = new ServerClock();
        var s = T0.ToUnixTimeSeconds();
        // The computer's clock is 2.25 s behind the server: the server's next second begins at the computer's 17.75.
        var local = T0.AddSeconds(-2.25);
        c.Observe(s, local.AddMilliseconds(900));
        c.Observe(s + 1, local.AddMilliseconds(1000));
        Assert.True(c.Fine);
        Assert.Equal(T0.AddMilliseconds(1000 + 640), c.Now(s + 1, local.AddMilliseconds(1640)));
        // Off by more than a second from what the server says (a hitch): the server's second.
        Assert.Equal(T0.AddSeconds(5), c.Now(s + 5, local.AddMilliseconds(1100)));
        // A jump forgets the offset until the next tick over.
        c.Observe(s + 9, local.AddMilliseconds(1200));
        Assert.False(c.Fine);
    }
}

public class LogUploadTests
{
    private const string Uuid = "6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10";

    private static string Gunzip(byte[] gz)
    {
        using var input = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
        using var reader = new StreamReader(input, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void The_body_is_gzipped_json_as_the_contract_says()
    {
        var rec = RecordingTests.Sample().ToJson();
        var gz = LogUpload.Body("0123456789abcdef0123456789abcdef", "Test Arena (Savage), pull 3", "{\"content\":99999}", rec);
        Assert.Equal(0x1f, gz[0]);
        Assert.Equal(0x8b, gz[1]);
        var o = JsonNode.Parse(Gunzip(gz))!.AsObject();
        Assert.Equal(1, (int)o["v"]!);
        Assert.Equal("0123456789abcdef0123456789abcdef", (string?)o["clientId"]);
        Assert.Equal("Test Arena (Savage), pull 3", (string?)o["name"]);
        Assert.Equal(99999, (int)o["summary"]!["content"]!);
        Assert.Equal("kokos-raid-recording", (string?)o["recording"]!["format"]);
        Assert.Equal(rec, o["recording"]!.ToJsonString());
        Assert.True(gz.Length < Encoding.UTF8.GetByteCount(rec) / 2);

        var none = JsonNode.Parse(Gunzip(LogUpload.Body("ab", new string('n', 200), null, rec)))!.AsObject();
        Assert.Null(none["summary"]);
        Assert.Equal(120, ((string?)none["name"])!.Length);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => LogUpload.Body("ab", "x", null, "{not json"));
    }

    [Fact]
    public void A_pull_too_big_whole_is_thinned_to_the_limit()
    {
        var json = RecordingTests.Sample().ToJson();
        Assert.Equal((json, false), LogUpload.Fit(json));
        var size = Encoding.UTF8.GetByteCount(json);
        var fit = SiteLink.Fit(json, size - 1000, Encoding.UTF8.GetByteCount);
        Assert.NotNull(fit);
        Assert.True(fit!.Value.Thinned);
        Assert.True(Encoding.UTF8.GetByteCount(fit.Value.Json) <= size - 1000);
    }

    [Theory]
    [InlineData(200, "{\"ok\":true,\"id\":\"" + Uuid + "\",\"pull\":null,\"duplicate\":false,\"public\":true}", UploadOutcome.Sent)]
    [InlineData(200, "{\"ok\":true,\"id\":\"" + Uuid + "\",\"duplicate\":true}", UploadOutcome.Sent)]
    [InlineData(200, "{\"ok\":true}", UploadOutcome.Retry)]
    [InlineData(400, "{\"ok\":false,\"hint\":\"recording\",\"error\":\"That isn't a recording.\"}", UploadOutcome.Refused)]
    [InlineData(401, "{\"ok\":false,\"hint\":\"token\",\"error\":\"This token was revoked.\"}", UploadOutcome.Stop)]
    [InlineData(401, "", UploadOutcome.Stop)]
    [InlineData(403, "{\"ok\":false,\"hint\":\"off\",\"error\":\"Saving is off.\"}", UploadOutcome.Pause)]
    [InlineData(403, "{\"ok\":false,\"hint\":\"full\",\"error\":\"Your logs are full.\"}", UploadOutcome.Stop)]
    [InlineData(413, "{\"ok\":false,\"hint\":\"size\",\"error\":\"Too big.\"}", UploadOutcome.Refused)]
    [InlineData(415, "{\"ok\":false,\"hint\":\"recording\",\"error\":\"Not gzip.\"}", UploadOutcome.Refused)]
    [InlineData(429, "{\"ok\":false,\"hint\":\"rate\",\"error\":\"60 an hour.\"}", UploadOutcome.Wait)]
    [InlineData(503, "{\"ok\":false,\"hint\":\"busy\",\"error\":\"Busy.\"}", UploadOutcome.Wait)]
    [InlineData(502, "<html>bad gateway</html>", UploadOutcome.Retry)]
    [InlineData(500, "", UploadOutcome.Retry)]
    public void Answers_are_read_as_the_contract_says(int status, string body, UploadOutcome outcome)
    {
        var a = LogUpload.Read(status, body, TimeSpan.FromSeconds(42));
        Assert.Equal(outcome, a.Outcome);
        if (outcome == UploadOutcome.Sent) Assert.Equal(Uuid, a.Id);
        if (outcome == UploadOutcome.Wait) Assert.Equal(TimeSpan.FromSeconds(42), a.Wait);
        if (outcome == UploadOutcome.Pause) Assert.Equal(LogUpload.OffPause, a.Wait);
        if (outcome != UploadOutcome.Sent) Assert.False(string.IsNullOrWhiteSpace(a.Why));
    }

    [Fact]
    public void The_sites_words_are_kept_and_waits_are_bounded()
    {
        var a = LogUpload.Read(401, "{\"ok\":false,\"hint\":\"token\",\"error\":\"This token was revoked.\"}", null);
        Assert.StartsWith("This token was revoked.", a.Why);
        Assert.Contains("Link again", a.Why);
        Assert.True(LogUpload.Read(200, "{\"ok\":true,\"id\":\"" + Uuid + "\",\"duplicate\":true,\"public\":false}", null) is { Duplicate: true, Public: false });
        Assert.Equal(LogUpload.LongestWait, LogUpload.Read(429, "{\"hint\":\"rate\"}", TimeSpan.FromHours(5)).Wait);
        Assert.Equal(TimeSpan.FromSeconds(1), LogUpload.Read(429, "{\"hint\":\"rate\"}", TimeSpan.Zero).Wait);
        Assert.Equal(TimeSpan.FromMinutes(5), LogUpload.Read(429, "{\"hint\":\"rate\"}", null).Wait);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(TimeSpan.FromSeconds(30), LogUpload.RetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)), now));
        Assert.Equal(TimeSpan.FromSeconds(90), LogUpload.RetryAfter(new RetryConditionHeaderValue(now.AddSeconds(90)), now));
    }

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;
        public byte[]? Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            return answer(request);
        }
    }

    private const string Token = "rpp_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";

    [Fact]
    public async Task It_posts_the_gzipped_body_with_the_token_as_bearer()
    {
        var fake = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true,\"id\":\"" + Uuid + "\",\"duplicate\":false,\"public\":false}"),
        });
        using var http = new HttpClient(fake);
        var body = LogUpload.Body("ab", "x", null, RecordingTests.Sample().ToJson());
        var a = await LogUpload.SendAsync(http, Token, body, "0.6.0", CancellationToken.None);
        Assert.Equal(UploadOutcome.Sent, a.Outcome);
        Assert.Equal(LogUpload.Url, fake.Seen!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, fake.Seen.Method);
        Assert.Equal("Bearer", fake.Seen.Headers.Authorization!.Scheme);
        Assert.Equal(Token, fake.Seen.Headers.Authorization.Parameter);
        Assert.Contains("gzip", fake.Seen.Content!.Headers.ContentEncoding);
        Assert.Equal("application/json", fake.Seen.Content.Headers.ContentType!.MediaType);
        Assert.Contains("RaidPlanner/0.6.0", fake.Seen.Headers.UserAgent.ToString());
        Assert.Equal(body, fake.Body);
        // The token is never in the address.
        Assert.DoesNotContain("rpp_", fake.Seen.RequestUri.ToString());
    }

    [Fact]
    public async Task Retry_after_and_failures_are_answers_not_throws()
    {
        var rate = new Fake(_ =>
        {
            var m = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("{\"ok\":false,\"hint\":\"rate\",\"error\":\"60 an hour.\"}") };
            m.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1234));
            return m;
        });
        using (var http = new HttpClient(rate))
        {
            var a = await LogUpload.SendAsync(http, Token, new byte[] { 1 }, "0.6.0", CancellationToken.None);
            Assert.Equal((UploadOutcome.Wait, TimeSpan.FromSeconds(1234), "rate"), (a.Outcome, a.Wait, a.Hint));
        }
        using (var down = new HttpClient(new Fake(_ => throw new HttpRequestException("down"))))
            Assert.Equal(UploadOutcome.Retry, (await LogUpload.SendAsync(down, Token, new byte[] { 1 }, "0.6.0", CancellationToken.None)).Outcome);
        using (var any = new HttpClient(new Fake(_ => throw new InvalidOperationException())))
        {
            Assert.Equal(UploadOutcome.Stop, (await LogUpload.SendAsync(any, "not a token", new byte[] { 1 }, "0.6.0", CancellationToken.None)).Outcome);
            Assert.Equal(UploadOutcome.Refused, (await LogUpload.SendAsync(any, Token, new byte[LogUpload.MaxGzip + 1], "0.6.0", CancellationToken.None)).Outcome);
        }
    }
}

public class UploadQueueTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);
    private const string Uuid = "6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10";

    private static RecordingInfo Pull(string file, int minutesAgo, string? sent = null, string? refused = null) =>
        new(file + Words.Extension, "Arena", 1, new DateTimeOffset(Now).AddMinutes(-minutesAgo), 120, Outcome.Wipe, 3, true, sent, null, refused);

    private static readonly RecordingInfo[] Pulls =
    {
        Pull("new", 1), Pull("sent", 5, Uuid), Pull("old", 30), Pull("refused", 40, refused: "Too big."), Pull("mid", 10),
    };

    [Fact]
    public void One_at_a_time_the_oldest_first()
    {
        var q = new UploadQueue();
        var first = q.Next(Pulls, Now, _ => true);
        Assert.Equal("old" + Words.Extension, first!.File);
        Assert.Null(q.Next(Pulls, Now, _ => true));
        Assert.Equal(new UploadMark(first.File, Uuid, null), q.Done(first.File, new UploadAnswer(UploadOutcome.Sent, Uuid), Now));
        Assert.Equal(1, q.SentCount);
        // The store marks it sent; then the next.
        var after = Pulls.Select(p => p.File == first.File ? p with { SentId = Uuid } : p).ToArray();
        Assert.Equal("mid" + Words.Extension, q.Next(after, Now, _ => true)!.File);
        Assert.Equal(2, UploadQueue.Waiting(after, _ => true).Count);
    }

    [Fact]
    public void What_isnt_wanted_isnt_sent()
    {
        var q = new UploadQueue();
        Assert.Equal("mid" + Words.Extension, q.Next(Pulls, Now, p => p.RecordedAt > new DateTimeOffset(Now).AddMinutes(-20))!.File);
        Assert.Null(new UploadQueue().Next(Pulls, Now, _ => false));
    }

    [Fact]
    public void A_refused_pull_is_marked_and_the_queue_goes_on()
    {
        var q = new UploadQueue();
        var p = q.Next(Pulls, Now, _ => true)!;
        Assert.Equal(new UploadMark(p.File, null, "Too big."), q.Done(p.File, new UploadAnswer(UploadOutcome.Refused, Why: "Too big."), Now));
        Assert.NotNull(q.Next(Pulls.Where(x => x.File != p.File).ToArray(), Now, _ => true));
    }

    [Fact]
    public void A_refused_token_or_a_full_account_stops_it_until_resumed()
    {
        var q = new UploadQueue();
        var p = q.Next(Pulls, Now, _ => true)!;
        Assert.Null(q.Done(p.File, new UploadAnswer(UploadOutcome.Stop, Why: "Your logs are full."), Now));
        Assert.Equal("Your logs are full.", q.Stopped);
        Assert.Null(q.Next(Pulls, Now.AddDays(1), _ => true));
        q.Resume();
        Assert.Null(q.Stopped);
        Assert.NotNull(q.Next(Pulls, Now, _ => true));
    }

    [Fact]
    public void The_hourly_limit_waits_as_long_as_the_site_says()
    {
        var q = new UploadQueue();
        var p = q.Next(Pulls, Now, _ => true)!;
        q.Done(p.File, new UploadAnswer(UploadOutcome.Wait, Why: "60 an hour.", Wait: TimeSpan.FromMinutes(20), Hint: "rate"), Now);
        Assert.Equal("60 an hour.", q.Held);
        Assert.Null(q.Next(Pulls, Now.AddMinutes(19), _ => true));
        Assert.Equal(p.File, q.Next(Pulls, Now.AddMinutes(20), _ => true)!.File);
        // A clock set back by more than the wait counts as waited.
        var q2 = new UploadQueue();
        q2.Done(q2.Next(Pulls, Now, _ => true)!.File, new UploadAnswer(UploadOutcome.Wait, Wait: TimeSpan.FromMinutes(20), Hint: "rate"), Now);
        Assert.NotNull(q2.Next(Pulls, Now.AddHours(-2), _ => true));
    }

    [Fact]
    public void Saving_off_pauses_half_an_hour()
    {
        var q = new UploadQueue();
        var p = q.Next(Pulls, Now, _ => true)!;
        q.Done(p.File, LogUpload.Read(403, "{\"hint\":\"off\",\"error\":\"Off.\"}", null), Now);
        Assert.Null(q.Next(Pulls, Now.AddMinutes(29), _ => true));
        Assert.NotNull(q.Next(Pulls, Now.AddMinutes(31), _ => true));
    }

    [Fact]
    public void No_answer_waits_longer_each_time()
    {
        var q = new UploadQueue();
        var p = q.Next(Pulls, Now, _ => true)!;
        q.Done(p.File, new UploadAnswer(UploadOutcome.Retry, Why: "Couldn't reach the site."), Now);
        Assert.Null(q.Next(Pulls, Now.AddSeconds(29), _ => true));
        p = q.Next(Pulls, Now.AddSeconds(30), _ => true)!;
        q.Done(p.File, new UploadAnswer(UploadOutcome.Retry), Now.AddSeconds(30));
        Assert.Null(q.Next(Pulls, Now.AddSeconds(89), _ => true));
        Assert.NotNull(q.Next(Pulls, Now.AddSeconds(90), _ => true));
    }

    [Fact]
    public void A_pull_the_site_keeps_failing_on_is_set_aside_so_the_next_goes()
    {
        var q = new UploadQueue();
        var t = Now;
        RecordingInfo p = null!;
        for (var i = 0; i < UploadQueue.TriesBeforeAside; i++)
        {
            p = q.Next(Pulls, t, _ => true)!;
            Assert.Equal("old" + Words.Extension, p.File);
            // The Worker running out of memory: Cloudflare's error page, no hint.
            q.Done(p.File, LogUpload.Read(503, "<html>error code: 1102</html>", null), t);
            t = q.NotBefore > t ? q.NotBefore : t;
        }
        Assert.True(q.Aside(p.File, t));
        // The next one goes, without the first's wait.
        Assert.Equal("mid" + Words.Extension, q.Next(Pulls, t, _ => true)!.File);
        q.Done("mid" + Words.Extension, new UploadAnswer(UploadOutcome.Sent, Uuid), t);
        var rest = Pulls.Select(x => x.File == "mid" + Words.Extension ? x with { SentId = Uuid } : x).ToArray();
        Assert.Equal("new" + Words.Extension, q.Next(rest, t, _ => true)!.File);
        q.Done("new" + Words.Extension, new UploadAnswer(UploadOutcome.Sent, Uuid), t);
        rest = rest.Select(x => x.File == "new" + Words.Extension ? x with { SentId = Uuid } : x).ToArray();
        Assert.Null(q.Next(rest, t, _ => true));
        // Half an hour on, it's tried again; a success forgets its failures.
        var back = q.Next(rest, t + UploadQueue.FirstAside, _ => true)!;
        Assert.Equal("old" + Words.Extension, back.File);
        q.Done(back.File, new UploadAnswer(UploadOutcome.Sent, Uuid), t + UploadQueue.FirstAside);
        Assert.False(q.Aside(back.File, t + UploadQueue.FirstAside));
    }

    [Fact]
    public void The_hourly_limit_never_sets_a_pull_aside()
    {
        var q = new UploadQueue();
        var t = Now;
        for (var i = 0; i < UploadQueue.TriesBeforeAside + 1; i++)
        {
            var p = q.Next(Pulls, t, _ => true)!;
            Assert.Equal("old" + Words.Extension, p.File);
            q.Done(p.File, new UploadAnswer(UploadOutcome.Wait, Wait: TimeSpan.FromMinutes(5), Hint: "rate"), t);
            t += TimeSpan.FromMinutes(5);
        }
        Assert.False(q.Aside("old" + Words.Extension, t));
    }

    [Fact]
    public void An_upload_is_at_most_what_the_site_can_read()
    {
        Assert.Equal(8_000_000, LogUpload.MaxRaw);
        Assert.True(LogUpload.MaxRecording < LogUpload.MaxRaw);
    }

    [Fact]
    public void An_answer_not_for_the_one_on_its_way_is_ignored()
    {
        var q = new UploadQueue();
        var p = q.Next(Pulls, Now, _ => true)!;
        Assert.Null(q.Done("other" + Words.Extension, new UploadAnswer(UploadOutcome.Sent, Uuid), Now));
        Assert.Equal(p.File, q.InFlight);
        q.Drop();
        Assert.Null(q.Done(p.File, new UploadAnswer(UploadOutcome.Sent, Uuid), Now));
    }
}

public class UploadPrefsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_05_configuration_uploads_from_now_on()
    {
        Assert.Equal(new UploadPrefs(2, true, Now), UploadPrefs.Migrate(new UploadPrefs(1, false, null), Now));
        Assert.Equal(new UploadPrefs(2, true, Now), UploadPrefs.Migrate(new UploadPrefs(2, true, null), Now));
        var kept = new UploadPrefs(2, false, Now.AddDays(-3));
        Assert.Same(kept, UploadPrefs.Migrate(kept, Now));
    }

    [Fact]
    public void It_wants_pulls_that_ended_since_and_all_after_older_pulls()
    {
        RecordingInfo P(int minutes, string? sent = null, string? refused = null) =>
            new("f" + minutes + Words.Extension, "Arena", 1, Now.AddMinutes(minutes), 120, Outcome.Wipe, 3, true, sent, null, refused);
        var prefs = new UploadPrefs(2, true, Now);
        Assert.True(prefs.Wants(P(5)));
        // Began a minute before, ended a minute after: it counts.
        Assert.True(prefs.Wants(P(-1)));
        Assert.False(prefs.Wants(P(-10)));
        Assert.False(prefs.Wants(P(5, sent: "x")));
        Assert.False(prefs.Wants(P(5, refused: "Too big.")));
        Assert.False((prefs with { UploadLogs = false }).Wants(P(5)));
        Assert.True((prefs with { UploadSince = DateTimeOffset.MinValue }).Wants(P(-1000)));
        var all = new[] { P(5), P(-10), P(-20), P(-30, sent: "x") };
        Assert.Equal(2, prefs.Older(all));
        Assert.Equal(0, (prefs with { UploadSince = DateTimeOffset.MinValue }).Older(all));
    }
}

public class SixStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rp-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    [Fact]
    public void A_refusal_is_kept_across_a_restart_even_without_the_index()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        Assert.Equal("Too big.", store.MarkRefused(info, "Too big.")!.UploadRefused);
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Equal("Too big.", again.All[0].UploadRefused);
        File.Delete(Path.Combine(folder, RecordingStore.IndexFile));
        var third = new RecordingStore(folder);
        third.Load();
        Assert.Equal("Too big.", third.All[0].UploadRefused);
        // So the queue after a restart is the queue before.
        Assert.Empty(UploadQueue.Waiting(third.All, _ => true));
    }

    [Fact]
    public void Pulls_still_to_upload_are_held_past_keep()
    {
        var store = new RecordingStore(folder) { Keep = 2, Waiting = _ => true };
        store.Load();
        var first = store.Save(RecordingTests.Sample());
        store.Save(RecordingTests.Sample());
        store.Save(RecordingTests.Sample());
        Assert.Equal(3, store.All.Count);
        // One uploaded goes first, the waiting ones stay.
        store.MarkSent(store.All[1], "6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", DateTimeOffset.UtcNow);
        store.Save(RecordingTests.Sample());
        Assert.Equal(3, store.All.Count);
        Assert.All(store.All, i => Assert.Null(i.SentId));
        Assert.Contains(store.All, i => i.File == first.File);
        // Not waiting any more (uploads off): back to Keep the last.
        store.Waiting = _ => false;
        store.Save(RecordingTests.Sample());
        Assert.Equal(2, store.All.Count);
        Assert.DoesNotContain(store.All, i => i.File == first.File);
    }

    [Fact]
    public void Held_pulls_stop_at_the_most()
    {
        var store = new RecordingStore(folder) { Keep = 1, Waiting = _ => true, HoldUpTo = 3 };
        store.Load();
        var first = store.Save(RecordingTests.Sample());
        for (var i = 0; i < 3; i++) store.Save(RecordingTests.Sample());
        Assert.Equal(3, store.All.Count);
        Assert.DoesNotContain(store.All, i => i.File == first.File);
        Assert.Equal(200, RecordingStore.MostHeld);
    }
}
