using System.Text.Json.Nodes;
using RaidPlanner.Core;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// 0.7.8's fixes for stalls in long fights: the party's count in the index (no file read for it), a delete that never waits on
/// the disk, uploads that back off through an outage, a pull never uploaded twice, and a load that leaves a save in progress alone.
/// </summary>
public class StallStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rp-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    private string IndexPath => Path.Combine(folder, RecordingStore.IndexFile);

    [Fact]
    public void The_party_count_is_kept_in_the_index()
    {
        var party = RecordingTests.Sample().Party.Count;
        Assert.True(party > 0);
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        Assert.Equal(party, info.PartyCount);
        Assert.Equal(party, ((JsonArray)JsonNode.Parse(File.ReadAllText(IndexPath))!)[0]!["partyCount"]!.GetValue<int>());
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Equal(party, again.All[0].PartyCount);
        // Marks keep it.
        Assert.Equal(party, again.MarkSent(again.All[0], "6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", DateTimeOffset.UtcNow)!.PartyCount);
        // A lost index is made again with it.
        File.Delete(IndexPath);
        var third = new RecordingStore(folder);
        third.Load();
        Assert.Equal(party, third.All[0].PartyCount);
    }

    [Fact]
    public void An_older_index_without_the_party_count_still_reads()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        // As 0.7.6 wrote it: no partyCount.
        var arr = (JsonArray)JsonNode.Parse(File.ReadAllText(IndexPath))!;
        ((JsonObject)arr[0]!).Remove("partyCount");
        File.WriteAllText(IndexPath, arr.ToJsonString());
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Equal(info.File, again.All[0].File);
        Assert.Null(again.All[0].PartyCount);
        // Written again, it stays without one (unknown, never a made-up 0).
        again.MarkSent(again.All[0], "6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", DateTimeOffset.UtcNow);
        var back = (JsonObject)((JsonArray)JsonNode.Parse(File.ReadAllText(IndexPath))!)[0]!;
        Assert.False(back.ContainsKey("partyCount"));
        Assert.Equal("6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", back["sentId"]!.GetValue<string>());
    }

    [Fact]
    public void A_bad_party_count_costs_only_the_count()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        var list = RecordingStore.ReadIndex(
            $"[{{\"file\":\"{info.File}\",\"pull\":3,\"partyCount\":\"eight\"}},{{\"file\":\"a{info.File}\",\"partyCount\":2.5}},{{\"file\":\"b{info.File}\",\"partyCount\":-1}},{{\"file\":\"c{info.File}\",\"partyCount\":8}}]");
        Assert.Equal(4, list.Count);
        Assert.Equal(new int?[] { null, null, null, 8 }, list.Select(i => i.PartyCount).ToArray());
        Assert.Equal(3, list[0].Pull);
    }

    [Fact]
    public async Task Deleting_later_takes_it_off_the_list_at_once_and_the_disk_after()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        var keep = store.Save(RecordingTests.Sample());
        var version = store.Version;
        var done = store.DeleteLater(info);
        // The list, at once.
        Assert.DoesNotContain(store.All, i => i.File == info.File);
        Assert.True(store.Version > version);
        Assert.Null(store.Find(info.File));
        await done.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(Path.Combine(folder, info.File)));
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Equal(new[] { keep.File }, again.All.Select(i => i.File).ToArray());
        // Nothing left marked gone: it went.
        var marks = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(folder, RecordingStore.MarksFile)))!;
        Assert.Empty((JsonArray)marks["gone"]!);
    }

    [Fact]
    public async Task Deletes_and_saves_at_once_end_with_the_latest_list_written()
    {
        var store = new RecordingStore(folder) { Keep = 100 };
        store.Load();
        var first = Enumerable.Range(0, 6).Select(_ => store.Save(RecordingTests.Sample())).ToList();
        var work = new List<Task>();
        foreach (var info in first.Take(3)) work.Add(store.DeleteLater(info));
        work.Add(Task.Run(() => store.Save(RecordingTests.Sample())));
        work.Add(Task.Run(() => store.MarkSent(first[4], "6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", DateTimeOffset.UtcNow)));
        foreach (var info in first.Skip(3).Take(1)) work.Add(store.DeleteLater(info));
        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(20));
        var listed = store.All.Select(i => i.File).OrderBy(f => f).ToArray();
        Assert.Equal(3, listed.Length);
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Equal(listed, again.All.Select(i => i.File).OrderBy(f => f).ToArray());
        Assert.Equal(listed, RecordingStore.ReadIndex(File.ReadAllText(IndexPath)).Select(i => i.File).OrderBy(f => f).ToArray());
        Assert.Equal("6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", again.All.Single(i => i.File == first[4].File).SentId);
        Assert.Equal(3, Directory.GetFiles(folder, "*" + Words.Extension).Length);
    }

    [Fact]
    public void A_load_during_a_save_leaves_its_files_alone()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var loads = 0;
        store.WhileWriting = () =>
        {
            // The pull's name is taken (an empty file) and its temporary file is being written: a load now must touch neither.
            var empty = Directory.GetFiles(folder, "*" + Words.Extension).Single(f => new FileInfo(f).Length == 0);
            Assert.True(File.Exists(empty + ".tmp"));
            store.Load();
            loads++;
            Assert.True(File.Exists(empty));
            Assert.True(File.Exists(empty + ".tmp"));
        };
        var info = store.Save(RecordingTests.Sample());
        Assert.Equal(1, loads);
        Assert.Single(store.All);
        Assert.Equal(3, store.Read(info).Pull);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void An_empty_file_left_by_a_crash_mid_save_is_cleaned_up()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        // A crash between taking the name and putting the file in place, with no temporary file left.
        var empty = Path.Combine(folder, "Test Arena (Savage) crash" + Words.Extension);
        File.WriteAllBytes(empty, Array.Empty<byte>());
        // And one whose temporary file was whole: put in place.
        var whole = Path.Combine(folder, "Test Arena (Savage) whole" + Words.Extension);
        File.WriteAllBytes(whole, Array.Empty<byte>());
        File.WriteAllText(whole + ".tmp", RecordingTests.Sample().ToJson());
        var again = new RecordingStore(folder);
        again.Load();
        Assert.False(File.Exists(empty));
        Assert.True(new FileInfo(whole).Length > 0);
        Assert.Equal(2, again.All.Count);
        Assert.Contains(again.All, i => i.File == info.File);
    }
}

public class StallUploadTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);
    private const string Uuid = "6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10";

    private static RecordingInfo Pull(string file, int minutesAgo) =>
        new(file + Words.Extension, "Arena", 1, new DateTimeOffset(Now).AddMinutes(-minutesAgo), 120, Outcome.Wipe, 3, true);

    private static readonly RecordingInfo[] Pulls = { Pull("a", 50), Pull("b", 40), Pull("c", 30), Pull("d", 20) };

    [Fact]
    public void An_outage_keeps_the_wait_growing_past_set_aside_pulls()
    {
        var q = new UploadQueue();
        var t = Now;
        var waits = new List<TimeSpan>();
        var tried = new List<string>();
        for (var n = 0; n < 9; n++)
        {
            var p = q.Next(Pulls, t, _ => true)!;
            Assert.NotNull(p);
            tried.Add(p.File[..1]);
            q.Done(p.File, new UploadAnswer(UploadOutcome.Retry, Why: "Couldn't reach the site."), t);
            waits.Add(q.NotBefore - t);
            // Nothing before the wait is over.
            Assert.Null(q.Next(Pulls, q.NotBefore.AddSeconds(-1), _ => true));
            t = q.NotBefore;
        }
        // Each pull is still set aside after three tries, but the wait never starts over: 30 s doubling to 15 minutes.
        Assert.Equal(new[] { "a", "a", "a", "b", "b", "b" }, tried.Take(6));
        Assert.Equal(new[] { 30, 60, 120, 240, 480, 900, 900, 900, 900 }.Select(s => TimeSpan.FromSeconds(s)), waits);
        // An upload that goes through starts it over.
        var next = q.Next(Pulls, t, _ => true)!;
        q.Done(next.File, new UploadAnswer(UploadOutcome.Sent, Uuid), t);
        Assert.True(q.NotBefore <= t);
    }

    [Fact]
    public void The_site_busy_keeps_the_wait_growing_past_a_set_aside_pull()
    {
        var q = new UploadQueue();
        var t = Now;
        for (var i = 0; i < UploadQueue.TriesBeforeAside; i++)
        {
            var p = q.Next(Pulls, t, _ => true)!;
            q.Done(p.File, LogUpload.Read(502, "<html>bad gateway</html>", null), t);
            t = q.NotBefore;
        }
        Assert.True(q.Aside("a" + Words.Extension, t));
        var b = q.Next(Pulls, t, _ => true)!;
        Assert.Equal("b" + Words.Extension, b.File);
        q.Done(b.File, LogUpload.Read(500, null, null), t);
        // The fourth failure in a row: four times the first wait, not the first again.
        Assert.Equal(TimeSpan.FromSeconds(240), q.NotBefore - t);
    }

    [Fact]
    public void A_pull_failing_on_its_own_still_lets_the_next_go_at_once()
    {
        var q = new UploadQueue();
        var t = Now;
        for (var i = 0; i < UploadQueue.TriesBeforeAside; i++)
        {
            var p = q.Next(Pulls, t, _ => true)!;
            Assert.Equal("a" + Words.Extension, p.File);
            q.Done(p.File, new UploadAnswer(UploadOutcome.Retry, Why: "Its file couldn't be read.", ThisPull: true), t);
            if (i < UploadQueue.TriesBeforeAside - 1) t = q.NotBefore;
        }
        Assert.True(q.Aside("a" + Words.Extension, t));
        Assert.True(q.NotBefore <= t);
        Assert.Equal("b" + Words.Extension, q.Next(Pulls, t, _ => true)!.File);
    }

    [Fact]
    public void Which_answers_are_the_pulls_own()
    {
        Assert.True(LogUpload.Read(418, null, null).ThisPull);
        Assert.True(LogUpload.Read(409, "{\"error\":\"Conflict.\"}", null).ThisPull);
        Assert.False(LogUpload.Read(500, null, null).ThisPull);
        Assert.False(LogUpload.Read(503, "<html>error code: 1102</html>", null).ThisPull);
        Assert.False(LogUpload.Read(429, null, null).ThisPull);
        Assert.False(LogUpload.Read(200, "<html>a captive portal</html>", null).ThisPull);
        Assert.False(LogUpload.Read(302, null, null).ThisPull);
    }

    [Fact]
    public void A_pull_just_uploaded_isnt_picked_again_before_the_store_shows_it()
    {
        var q = new UploadQueue();
        var a = q.Next(Pulls, Now, _ => true)!;
        Assert.Equal("a" + Words.Extension, a.File);
        Assert.NotNull(q.Done(a.File, new UploadAnswer(UploadOutcome.Sent, Uuid), Now));
        // The store's mark is still being written: the list doesn't say sent yet. The next goes, never this one again.
        var b = q.Next(Pulls, Now, _ => true)!;
        Assert.Equal("b" + Words.Extension, b.File);
        Assert.NotNull(q.Done(b.File, new UploadAnswer(UploadOutcome.Refused, Why: "Too big."), Now));
        Assert.Equal("c" + Words.Extension, q.Next(Pulls, Now, _ => true)!.File);
        q.Drop();
        // Once the store shows the marks, they're let go of: the list decides again.
        var marked = Pulls.Select(p => p.File == a.File ? p with { SentId = Uuid } : p.File == b.File ? p with { UploadRefused = "Too big." } : p).ToArray();
        Assert.Equal("c" + Words.Extension, q.Next(marked, Now, _ => true)!.File);
        q.Drop();
        // Had it not been marked after all (a list without the marks again), it would be picked: nothing's held any more.
        Assert.Equal("a" + Words.Extension, q.Next(Pulls, Now, _ => true)!.File);
    }

    [Fact]
    public void A_mark_that_failed_lets_the_pull_go_again()
    {
        var q = new UploadQueue();
        var a = q.Next(Pulls, Now, _ => true)!;
        q.Done(a.File, new UploadAnswer(UploadOutcome.Sent, Uuid), Now);
        Assert.Equal("b" + Words.Extension, q.Next(Pulls, Now, _ => true)!.File);
        q.Drop();
        q.Settled(a.File);
        Assert.Equal("a" + Words.Extension, q.Next(Pulls, Now, _ => true)!.File);
        q.Drop();
        // One no longer kept (deleted meanwhile) is let go of too.
        var a2 = q.Next(Pulls, Now, _ => true)!;
        q.Done(a2.File, new UploadAnswer(UploadOutcome.Sent, Uuid), Now);
        var rest = Pulls.Where(p => p.File != a2.File).ToArray();
        Assert.Equal("b" + Words.Extension, q.Next(rest, Now, _ => true)!.File);
        q.Drop();
        Assert.Equal("a" + Words.Extension, q.Next(Pulls, Now, _ => true)!.File);
    }
}
