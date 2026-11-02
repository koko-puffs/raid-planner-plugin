using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RaidPlanner.Core;

/// <summary>
/// A kept pull, as the window lists it without reading its whole file: its file, duty, pull,
/// when, how long, how it ended, its mechanics, whether the party's places are in it, and the
/// site's id for it once it's been sent.
/// </summary>
public sealed record RecordingInfo(
    string File,
    string Duty,
    int Pull,
    DateTimeOffset RecordedAt,
    double Duration,
    Outcome Outcome,
    int Mechanics,
    bool PartyPositions,
    string? SentId = null,
    DateTimeOffset? SentAt = null)
{
    public string Name => Words.PullName(Duty, Pull);
}

/// <summary>
/// The pulls the plugin keeps: one file each in its folder (the plugin's own, under Dalamud's
/// config), and an index of them (index.json) so the list opens without reading every file. The
/// newest first; past <see cref="Keep"/> the oldest go. Writes go to a temporary file first, so a
/// crash mid-write never leaves half a file. Safe to call from any thread.
/// </summary>
public sealed class RecordingStore
{
    public const string IndexFile = "index.json";

    private readonly object gate = new();
    private List<RecordingInfo> items = new();

    public RecordingStore(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary>How many pulls are kept (Settings).</summary>
    public int Keep { get; set; } = 20;

    /// <summary>Bumped on every change, so the window knows to read the list again.</summary>
    public int Version { get; private set; }

    public IReadOnlyList<RecordingInfo> All
    {
        get
        {
            lock (gate) return items.ToList();
        }
    }

    /// <summary>Reads the index (or, without one, every recording in the folder). Never throws: a bad file is left out.</summary>
    public void Load()
    {
        lock (gate)
        {
            Directory.CreateDirectory(Folder);
            var index = Path.Combine(Folder, IndexFile);
            var list = new List<RecordingInfo>();
            var fromIndex = false;
            if (File.Exists(index))
            {
                try
                {
                    list = ReadIndex(File.ReadAllText(index));
                    fromIndex = true;
                }
                catch (Exception ex) when (ex is IOException or JsonException or FormatException or InvalidOperationException or UnauthorizedAccessException)
                {
                    list = new List<RecordingInfo>();
                }
            }
            // Files the index doesn't know (copied in, or the index lost) are read once.
            var known = list.Select(i => i.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(Folder, "*" + Words.Extension))
            {
                var file = Path.GetFileName(path);
                if (known.Contains(file)) continue;
                try
                {
                    list.Add(InfoOf(file, Recording.Parse(File.ReadAllText(path))));
                }
                catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
                {
                    // Not a recording: left alone.
                }
            }
            items = list.Where(i => File.Exists(Path.Combine(Folder, i.File))).OrderByDescending(i => i.RecordedAt).ToList();
            if (!fromIndex || items.Count != list.Count) WriteIndex();
            Version++;
        }
    }

    public static RecordingInfo InfoOf(string file, Recording r) =>
        new(file, r.Duty.Name, r.Pull, r.RecordedAt, r.Duration, r.Outcome, r.MechanicCount, r.PartyPositions);

    /// <summary>Keeps a pull: writes its file and puts it first in the list; the oldest past <see cref="Keep"/> go. Returns its entry.</summary>
    public RecordingInfo Save(Recording r)
    {
        var json = r.ToJson();
        lock (gate)
        {
            Directory.CreateDirectory(Folder);
            var stem = Words.FileStem(r.Duty.Name, r.Pull, r.RecordedAt);
            var file = stem + Words.Extension;
            for (var n = 2; File.Exists(Path.Combine(Folder, file)); n++) file = $"{stem} ({n}){Words.Extension}";
            WriteAtomically(Path.Combine(Folder, file), json);
            var info = InfoOf(file, r);
            items.Insert(0, info);
            Prune();
            WriteIndex();
            Version++;
            return info;
        }
    }

    /// <summary>A kept pull's whole recording.</summary>
    public Recording Read(RecordingInfo info) => Recording.Parse(File.ReadAllText(Path.Combine(Folder, info.File)));

    /// <summary>The path of a kept pull's file.</summary>
    public string PathOf(RecordingInfo info) => Path.Combine(Folder, info.File);

    public void Delete(RecordingInfo info)
    {
        lock (gate)
        {
            items.RemoveAll(i => string.Equals(i.File, info.File, StringComparison.OrdinalIgnoreCase));
            TryDelete(Path.Combine(Folder, info.File));
            WriteIndex();
            Version++;
        }
    }

    /// <summary>Notes that a pull was sent to the site (its id there), so the list says so.</summary>
    public RecordingInfo? MarkSent(RecordingInfo info, string id, DateTimeOffset at)
    {
        lock (gate)
        {
            var i = items.FindIndex(x => string.Equals(x.File, info.File, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return null;
            items[i] = items[i] with { SentId = id, SentAt = at };
            WriteIndex();
            Version++;
            return items[i];
        }
    }

    /// <summary>The entry for a file now (it may have been sent since it was read).</summary>
    public RecordingInfo? Find(string file)
    {
        lock (gate) return items.FirstOrDefault(x => string.Equals(x.File, file, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Sets how many are kept, letting the oldest go now if there are more.</summary>
    public void SetKeep(int keep)
    {
        lock (gate)
        {
            Keep = Math.Clamp(keep, 1, 200);
            if (items.Count <= Keep) return;
            Prune();
            WriteIndex();
            Version++;
        }
    }

    private void Prune()
    {
        while (items.Count > Math.Max(1, Keep))
        {
            var last = items[^1];
            items.RemoveAt(items.Count - 1);
            TryDelete(Path.Combine(Folder, last.File));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Open somewhere else: it stays, out of the list.
        }
    }

    private void WriteIndex()
    {
        var arr = new JsonArray();
        foreach (var i in items)
        {
            var o = new JsonObject
            {
                ["file"] = i.File,
                ["duty"] = i.Duty,
                ["pull"] = i.Pull,
                ["recordedAt"] = i.RecordedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ["duration"] = Math.Round(i.Duration, 2),
                ["outcome"] = Words.OutcomeKey(i.Outcome),
                ["mechanics"] = i.Mechanics,
                ["partyPositions"] = i.PartyPositions,
            };
            if (i.SentId != null) o["sentId"] = i.SentId;
            if (i.SentAt is { } at) o["sentAt"] = at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            arr.Add(o);
        }
        try
        {
            WriteAtomically(Path.Combine(Folder, IndexFile), arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The list is made again from the files next time.
        }
    }

    public static List<RecordingInfo> ReadIndex(string json)
    {
        var list = new List<RecordingInfo>();
        if (JsonNode.Parse(json) is not JsonArray arr) throw new FormatException("Not an index.");
        foreach (var n in arr.OfType<JsonObject>())
        {
            var file = n["file"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(file) || file.Contains('/') || file.Contains('\\')) continue;
            DateTimeOffset.TryParse(n["recordedAt"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at);
            DateTimeOffset? sentAt = DateTimeOffset.TryParse(n["sentAt"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var s) ? s : null;
            list.Add(new RecordingInfo(
                file,
                n["duty"]?.GetValue<string>() ?? "Unknown duty",
                n["pull"]?.GetValue<int>() ?? 1,
                at,
                n["duration"]?.GetValue<double>() ?? 0,
                Words.OutcomeFromKey(n["outcome"]?.GetValue<string>()),
                n["mechanics"]?.GetValue<int>() ?? 0,
                n["partyPositions"]?.GetValue<bool>() ?? false,
                n["sentId"]?.GetValue<string>(),
                sentAt));
        }
        return list;
    }

    private static void WriteAtomically(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, true);
    }
}
