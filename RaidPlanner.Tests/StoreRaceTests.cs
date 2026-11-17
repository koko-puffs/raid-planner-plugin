using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RaidPlanner.Core;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// 0.7.7's store: a mark made in memory and still waiting for the disk survives a load at that moment (the mark waits outside
/// the list's lock). The disk's lock is held here (through reflection) to open that moment on purpose.
/// </summary>
public class StoreRaceTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rp-race-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    private static object Disk(RecordingStore s) =>
        typeof(RecordingStore).GetField("disk", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;

    [Fact]
    public void A_load_while_a_mark_waits_for_the_disk_keeps_the_mark()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        var disk = Disk(store);
        Task mark;
        lock (disk)
        {
            mark = Task.Run(() => store.MarkSent(info, "6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", DateTimeOffset.UtcNow));
            Assert.True(SpinWait.SpinUntil(() => store.All[0].SentId != null, 5000));
            // The index and marks on the disk don't have it yet.
            store.Load();
        }
        Assert.True(mark.Wait(5000));
        Assert.NotNull(store.All[0].SentId);
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Equal("6f1c9a52-8a43-4c4e-9d55-9a1f5c3b2a10", again.All[0].SentId);
    }

    [Fact]
    public void A_deleted_pull_stays_gone_after_a_load()
    {
        var store = new RecordingStore(folder);
        store.Load();
        var info = store.Save(RecordingTests.Sample());
        Assert.True(store.DeleteLater(info).Wait(5000));
        Assert.False(File.Exists(store.PathOf(info)));
        var again = new RecordingStore(folder);
        again.Load();
        Assert.Empty(again.All);
    }
}
