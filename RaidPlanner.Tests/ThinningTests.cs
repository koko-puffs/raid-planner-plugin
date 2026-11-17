using System.Text;
using RaidPlanner.Core;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// 0.7.6's thinning (SiteLink.Fit with a <see cref="SiteLink.SizeRule"/>): each step measured as it's written, not made into
/// a string. It must pick the very step the string-measured thinning picks and send the same bytes, for a send's count
/// (the database's, a space after each colon and comma outside strings) and an upload's (UTF-8 bytes), at every limit on
/// either side of each step's size, with strings full of quotes, colons, commas, backslashes and letters past ASCII.
/// </summary>
public class ThinningTests
{
    /// <summary>A pull with every step to take: four frames a second (the party and three enemies), each table the thinning drops.</summary>
    private static Recording Rich()
    {
        var r = RecordingTests.Sample();
        var frames = new List<Frame>();
        for (var i = 0; i < 480; i++)
        {
            var t = i * 0.25;
            var at = new Dictionary<int, (float, float, float)>();
            var hp = new Dictionary<int, long>();
            for (var id = 1; id <= 8; id++) at[id] = (id + i * 0.01f, -id - i * 0.02f, 0.5f);
            for (var id = 101; id <= 103; id++)
            {
                at[id] = (100 - id + i * 0.03f, 1.5f, -1f);
                hp[id] = 10_000_000 - i * 1000L * id;
            }
            frames.Add(new Frame(t, at, hp));
        }
        var c = new CombatLog();
        for (var i = 0; i < 400; i++)
        {
            c.Fx.Add(new FxRow(i * 0.3, 9001, 1 + i % 8, 101, 12_000 + i, 1, -1, i % 5));
            c.Ticks.Add(new TickRow(i * 0.3, 101, 1 + i % 8, 1871, 2500 + i, i % 7 == 0));
            c.Casts.Add(new CastRow(i * 0.3, 1 + i % 8, 9005, 2.8f, i % 3 == 0));
            c.Uses.Add(new UseRow(i * 0.3, 1 + i % 8, 7535, 101, 1, 0));
            c.Areas.Add(new AreaRow(i * 0.3, 1 + i % 8, 3569, i * 0.1f, -i * 0.1f));
            c.Hate.Add(new HateRow(i * 0.3, 101, 1 + i % 2, 100 - i % 30));
            c.Res.Add(new ResRow(i * 0.3, 1 + i % 8, 10_000 - i, i % 20));
        }
        var rich = r.With(frames, true, c);
        rich.Duty = new DutyInfo("The \"Omega\": Protocol, \\ (Ultimate) — Ÿ ½", rich.Duty.Territory, rich.Duty.Content);
        return rich;
    }

    private static Func<string, int> SizeOf(SiteLink.SizeRule rule) =>
        rule == SiteLink.SizeRule.Server ? SiteLink.ServerSize : Encoding.UTF8.GetByteCount;

    [Theory]
    [InlineData(SiteLink.SizeRule.Server)]
    [InlineData(SiteLink.SizeRule.Utf8)]
    public void Each_step_measured_as_it_is_written_is_its_text_measured(SiteLink.SizeRule rule)
    {
        var json = Rich().ToJson();
        var size = SizeOf(rule);
        Assert.Equal(size(json), SiteLink.Measure(json, rule));
        var steps = SiteLink.StepsOf(Recording.Parse(json)).ToList();
        // Halved frames, the enmity list, MP and barriers, three frame steps, three enemy-only steps, none, then four tables.
        Assert.Equal(1 + 2 + 3 + 3 + 1 + 4, steps.Count);
        foreach (var step in steps) Assert.Equal(size(step.ToJson()), SiteLink.Measure(step, rule));
    }

    [Theory]
    [InlineData(SiteLink.SizeRule.Server)]
    [InlineData(SiteLink.SizeRule.Utf8)]
    public void The_same_step_and_the_same_bytes_at_every_limit(SiteLink.SizeRule rule)
    {
        var json = Rich().ToJson();
        var size = SizeOf(rule);
        var sizes = SiteLink.StepsOf(Recording.Parse(json)).Select(s => size(s.ToJson())).Append(size(json)).ToList();
        var limits = sizes.SelectMany(s => new[] { s - 1, s, s + 1 }).Append(0).Append(int.MaxValue).Distinct().ToList();
        var thinned = 0;
        foreach (var max in limits)
        {
            var byText = SiteLink.Fit(json, max, size);
            var measured = SiteLink.Fit(json, max, rule);
            Assert.Equal(byText, measured);
            if (measured is { Thinned: true }) thinned++;
        }
        // Not a test of nothing: most limits thin it, at every step.
        Assert.True(thinned >= sizes.Count);
    }

    [Fact]
    public void A_send_and_an_upload_go_by_their_own_count()
    {
        var json = Rich().ToJson();
        Assert.Equal(SiteLink.Fit(json, SiteLink.MaxBytes, SiteLink.ServerSize), SiteLink.Fit(json));
        Assert.Equal(SiteLink.Fit(json, LogUpload.MaxRecording, Encoding.UTF8.GetByteCount), LogUpload.Fit(json));
        // Not a recording, and too big: nothing to send, as before (and whole when it fits, as before).
        Assert.Null(SiteLink.Fit("{not json", 5, SiteLink.SizeRule.Utf8));
        Assert.Equal(SiteLink.Fit("{not json", 5, Encoding.UTF8.GetByteCount), SiteLink.Fit("{not json", 5, SiteLink.SizeRule.Utf8));
    }

    [Fact]
    public void Measuring_a_big_pull_allocates_a_small_part_of_its_size()
    {
        var rich = Rich();
        // Twenty times its frames: a long pull's file, a few MB.
        var r = rich.WithFrames(Enumerable.Range(0, 20).SelectMany(k => rich.Frames.Select(f => f with { T = f.T + k * 120 })), true);
        var size = SiteLink.Measure(r, SiteLink.SizeRule.Utf8);
        Assert.True(size > 1_000_000, $"the pull is {size} bytes: too small to tell");
        var before = GC.GetAllocatedBytesForCurrentThread();
        SiteLink.Measure(r, SiteLink.SizeRule.Server);
        var made = GC.GetAllocatedBytesForCurrentThread() - before;
        // The writer hands its bytes on every 64 KB: never a buffer of the whole file (before 0.7.7 a measure of this one
        // allocated about 15 MB; now under 1 MB, the events' order and the combat block's).
        Assert.True(made < size / 3, $"measuring {size} bytes allocated {made}");
    }

    [Fact]
    public void A_kept_pull_is_written_straight_to_its_file_byte_for_byte()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rp-thin-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RecordingStore(folder);
            store.Load();
            var r = Rich();
            var info = store.Save(r);
            Assert.Equal(r.ToJson(), File.ReadAllText(Path.Combine(folder, info.File), new UTF8Encoding(false)));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
