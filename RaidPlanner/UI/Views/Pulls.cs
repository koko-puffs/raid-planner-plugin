using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The Pulls tab (0.8.0, the mock-ups' plug-main rows): every kept pull, the newest first, by night ("Tonight · 12
/// pulls", "Last night", "Friday night", a date), each a row (its number, its length in mono, how long ago and where
/// it ended, its outcome, a tick once uploaded, a chevron) opening the pull. A pull's facts (the phase and mechanic it
/// ended at, its deaths) are read from its file once, on a worker, one file at a time and never while a pull goes.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>A pull's facts once read (null when its file couldn't be).</summary>
    private sealed record GistState(PullGist? Gist);

    private readonly ConcurrentDictionary<string, GistState> gists = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RecordingInfo> gistsWanted = new(StringComparer.OrdinalIgnoreCase);
    private int gistsReading;

    /// <summary>
    /// A kept pull's facts, if read; otherwise asks for them (with <paramref name="want"/>), read on a worker one file
    /// at a time, never while a pull is recorded or followed (its reads would cost the game a frame).
    /// </summary>
    private PullGist? GistOf(RecordingInfo info, bool want = true)
    {
        if (gists.TryGetValue(info.File, out var g)) return g.Gist;
        if (!want || recorder.InPull || site.Closed) return null;
        gistsWanted.TryAdd(info.File, info);
        if (Interlocked.CompareExchange(ref gistsReading, 1, 0) == 0) _ = Task.Run(ReadGists);
        return null;
    }

    private void ReadGists()
    {
        try
        {
            while (!site.Closed && !recorder.InPull)
            {
                var file = gistsWanted.Keys.FirstOrDefault();
                if (file == null || !gistsWanted.TryRemove(file, out var info)) break;
                // Read meanwhile (its own screen), or deleted.
                if (gists.ContainsKey(file) || store.Find(file) == null) continue;
                try
                {
                    var r = store.Read(info);
                    gists[file] = new GistState(PullGist.Of(r, PullView.Build(r)));
                }
                catch (Exception ex)
                {
                    Services.Log.Warning(ex, "Raid Codex couldn't read a pull's facts");
                    gists[file] = new GistState(null);
                }
            }
        }
        finally
        {
            Volatile.Write(ref gistsReading, 0);
        }
    }

    private void DrawPulls()
    {
        Frame.BeginHeader();
        NavTabs();
        Frame.EndHeader();
        Frame.BeginBody("##pulls", null);
        var all = store.All;
        if (all.Count == 0)
        {
            DrawNoPulls();
            Frame.EndBody();
            return;
        }
        Txt.Para(new[] { Run.Note($"Every pull kept on this PC, the newest first. Past {config.Keep}, the oldest go, those on your Raid Codex first.") }, 19.5f);
        var nights = PullNights.Group(LocalTimes(all), DateTime.Now);
        foreach (var night in nights)
        {
            Kit.Gap(18);
            var at = Kit.At;
            Kit.Eyebrow(Kit.Dl, at, night.Heading, null, Kit.Width, Palette.Muted);
            Kit.Reserve(at, Kit.Width, Kit.EyebrowH);
            Kit.Gap(8);
            var rows = night.Items.Select(i => all[i]).ToList();
            // The duty most of the night was in isn't said on each row; another is.
            var main = rows.GroupBy(r => r.Duty, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).First().Key;
            DrawPullRows($"##night-{night.Key:yyyyMMdd}", rows, BestOf(all, night), main);
        }
        Frame.EndBody();
    }

    /// <summary>No pulls kept: the Boss asleep and a line.</summary>
    private void DrawNoPulls()
    {
        var dl = Kit.Dl;
        var w = Kit.Width;
        Kit.Gap(30);
        var at = Kit.At;
        Boss.Empty(dl, new Vector2(at.X + w / 2, at.Y + S(48)), 68, BossMood.Asleep, 96);
        Kit.Reserve(at, w, S(96));
        Kit.Gap(8.8f);
        var cy = Kit.At;
        Kit.Reserve(cy, w, Kit.Callout(dl, cy, "No pulls yet", 17, w, 2, 1.05f, center: true));
        Kit.Gap(8.8f);
        Txt.Note(config.AutoRecord ? "Each pull in a raid or trial is kept here by itself, the newest first." : "Recording is off: turn it on in Settings › Recording.", center: true);
    }
}
