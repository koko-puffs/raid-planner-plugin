using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
/// config), an index of them (index.json) so the list opens without reading every file, and the
/// marks that can't be made again from the files (marks.json: which were sent, and which couldn't
/// be deleted yet). The newest first; past <see cref="Keep"/> the oldest sent ones go, and only
/// then the oldest unsent. Writes go to a temporary file, flushed to the disk, then put in place,
/// so a crash or a power cut mid-write never leaves half a file. Safe to call from any thread;
/// <see cref="All"/> and <see cref="Find"/> never wait (the window reads them every frame).
/// </summary>
public sealed class RecordingStore
{
    public const string IndexFile = "index.json";

    public const string MarksFile = "marks.json";

    /// <summary>How many sent pulls the site keeps waiting for a person (its newest; older ones go there).</summary>
    public const int SiteKeeps = 30;

    private readonly object gate = new();
    private List<RecordingInfo> items = new();

    /// <summary>The list as it was last changed, handed out without taking the lock.</summary>
    private volatile IReadOnlyList<RecordingInfo> snapshot = Array.Empty<RecordingInfo>();

    /// <summary>Files deleted (or pruned) that the system wouldn't let go yet: tried again at the next load, never listed.</summary>
    private readonly HashSet<string> gone = new(StringComparer.OrdinalIgnoreCase);

    public RecordingStore(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary>How many pulls are kept (Settings).</summary>
    public int Keep { get; set; } = 20;

    /// <summary>Bumped on every change, so the window knows to read the list again.</summary>
    public int Version { get; private set; }

    public IReadOnlyList<RecordingInfo> All => snapshot;

    /// <summary>Reads the index (or, without one, every recording in the folder). Never throws: a bad file or entry is left out.</summary>
    public void Load()
    {
        lock (gate)
        {
            Directory.CreateDirectory(Folder);
            var index = Path.Combine(Folder, IndexFile);
            var list = new List<RecordingInfo>();
            var fromIndex = false;
            try
            {
                if (File.Exists(index))
                {
                    list = ReadIndex(File.ReadAllText(index));
                    fromIndex = true;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                list = new List<RecordingInfo>();
            }
            var marks = ReadMarks();
            gone.Clear();
            foreach (var file in marks.Gone)
            {
                if (!TryDelete(Path.Combine(Folder, file))) gone.Add(file);
            }
            var marksChanged = gone.Count != marks.Gone.Count;
            foreach (var path in SafeFiles("*.tmp")) Recover(path);

            // Files the index doesn't know (copied in, or the index lost) are read once.
            var known = list.Select(i => i.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in SafeFiles("*" + Words.Extension))
            {
                var file = Path.GetFileName(path);
                if (known.Contains(file) || gone.Contains(file)) continue;
                try
                {
                    list.Add(InfoOf(file, Recording.Parse(File.ReadAllText(path))));
                    known.Add(file);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Not a recording (or one that can't be read): left alone, out of the list.
                }
            }
            // The sent marks are kept apart from the index, so a lost index doesn't lose them.
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].SentId == null && marks.Sent.TryGetValue(list[i].File, out var sent))
                    list[i] = list[i] with { SentId = sent.Id, SentAt = sent.At };
            }
            var count = list.Count;
            items = list
                .Where(i => !gone.Contains(i.File) && File.Exists(Path.Combine(Folder, i.File)))
                .GroupBy(i => i.File, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderByDescending(i => i.RecordedAt).ToList();
            if (!fromIndex || items.Count != count) WriteIndex();
            if (marksChanged) WriteMarks();
            Changed();
        }
    }

    /// <summary>
    /// A temporary file left by a crash between its flush and its move: a whole recording is put in
    /// place when its own file is missing or empty (the name was taken, the move never came); anything
    /// else is deleted.
    /// </summary>
    private static void Recover(string temp)
    {
        try
        {
            var target = temp[..^".tmp".Length];
            if (target.EndsWith(Words.Extension, StringComparison.OrdinalIgnoreCase)
                && (!File.Exists(target) || new FileInfo(target).Length == 0))
            {
                Recording.Parse(File.ReadAllText(temp));
                File.Move(temp, target, true);
                return;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Not a whole recording: let go of below.
        }
        TryDelete(temp);
    }

    private IEnumerable<string> SafeFiles(string pattern)
    {
        try
        {
            return Directory.GetFiles(Folder, pattern);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public static RecordingInfo InfoOf(string file, Recording r) =>
        new(file, r.Duty.Name, r.Pull, r.RecordedAt, r.Duration, r.Outcome, r.MechanicCount, r.PartyPositions);

    /// <summary>
    /// Keeps a pull: writes its file and puts it first in the list; past <see cref="Keep"/>, the oldest
    /// go (sent ones first). Returns its entry. The file is written outside the lock, so the window
    /// never waits on the disk.
    /// </summary>
    public RecordingInfo Save(Recording r)
    {
        var json = r.ToJson();
        var mechanics = r.MechanicCount;
        string file;
        lock (gate)
        {
            Directory.CreateDirectory(Folder);
            var stem = Words.FileStem(r.Duty.Name, r.Pull, r.RecordedAt);
            file = stem + Words.Extension;
            // The name is taken now (an empty file), so another save at the same moment can't take it too.
            for (var n = 2; !Reserve(Path.Combine(Folder, file)); n++)
            {
                if (n > 999) throw new IOException("No free name for the pull's file.");
                file = $"{stem} ({n}){Words.Extension}";
            }
        }
        try
        {
            WriteAtomically(Path.Combine(Folder, file), json);
        }
        catch
        {
            TryDelete(Path.Combine(Folder, file));
            throw;
        }
        var info = new RecordingInfo(file, r.Duty.Name, r.Pull, r.RecordedAt, r.Duration, r.Outcome, mechanics, r.PartyPositions);
        lock (gate)
        {
            // The load at startup may have read the file in already.
            items.RemoveAll(i => string.Equals(i.File, file, StringComparison.OrdinalIgnoreCase));
            items.Insert(0, info);
            Prune();
            WriteIndex();
            WriteMarks();
            Changed();
        }
        return info;
    }

    private static bool Reserve(string path)
    {
        try
        {
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { }
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
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
            Forget(info.File);
            WriteIndex();
            WriteMarks();
            Changed();
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
            WriteMarks();
            WriteIndex();
            Changed();
            return items[i];
        }
    }

    /// <summary>The entry for a file now (it may have been sent since it was read).</summary>
    public RecordingInfo? Find(string file)
    {
        foreach (var x in snapshot)
            if (string.Equals(x.File, file, StringComparison.OrdinalIgnoreCase)) return x;
        return null;
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
            WriteMarks();
            Changed();
        }
    }

    private void Changed()
    {
        snapshot = items.ToArray();
        Version++;
    }

    /// <summary>
    /// Past <see cref="Keep"/>: the oldest pull the site still has goes first, and only then the
    /// oldest of the rest, so a run of other pulls doesn't take one you haven't sent while one that's
    /// safely on the site could go instead. The site keeps a person's newest
    /// <see cref="SiteKeeps"/> sends, so a sent pull older than those counts as unsent here.
    /// </summary>
    private void Prune()
    {
        var keep = Math.Max(1, Keep);
        while (items.Count > keep)
        {
            var onSite = items.Where(i => i.SentId != null).OrderByDescending(i => i.SentAt ?? i.RecordedAt).Take(SiteKeeps)
                .Select(i => i.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var at = items.FindLastIndex(i => onSite.Contains(i.File));
            if (at < 0) at = items.Count - 1;
            var file = items[at].File;
            items.RemoveAt(at);
            Forget(file);
        }
    }

    /// <summary>A file let go of: deleted, or (held open somewhere) marked to go at the next load.</summary>
    private void Forget(string file)
    {
        if (!TryDelete(Path.Combine(Folder, file))) gone.Add(file);
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
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
                ["duration"] = double.IsFinite(i.Duration) ? Math.Round(i.Duration, 2) : 0,
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
            // The list is made again from the files next time (the sent marks are in marks.json).
        }
    }

    /// <summary>The index's entries; one that can't be read is left out, not the whole index.</summary>
    public static List<RecordingInfo> ReadIndex(string json)
    {
        var list = new List<RecordingInfo>();
        if (JsonNode.Parse(json) is not JsonArray arr) throw new FormatException("Not an index.");
        foreach (var n in arr.OfType<JsonObject>())
        {
            try
            {
                var file = n["file"]?.GetValue<string>();
                if (!IsOwnName(file)) continue;
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
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException or ArgumentException)
            {
                // An entry of the wrong shape: the file is read again instead.
            }
        }
        return list;
    }

    /// <summary>
    /// A name the store would give a pull's file: a plain file name in its folder (no folder, no drive,
    /// nothing a file name can't hold) ending in the recording's extension. Anything else in the index
    /// or the marks is ignored, so they can never point outside the folder or at the index itself.
    /// </summary>
    public static bool IsOwnName([NotNullWhen(true)] string? file) =>
        !string.IsNullOrWhiteSpace(file)
        && file.EndsWith(Words.Extension, StringComparison.OrdinalIgnoreCase)
        && file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && file.IndexOfAny(new[] { '/', '\\', ':' }) < 0
        && Path.GetFileName(file) == file;

    // ---------- marks.json: what was sent, and what couldn't be deleted yet ----------

    private sealed record Marks(Dictionary<string, (string Id, DateTimeOffset? At)> Sent, List<string> Gone);

    private Marks ReadMarks()
    {
        var marks = new Marks(new Dictionary<string, (string, DateTimeOffset?)>(StringComparer.OrdinalIgnoreCase), new List<string>());
        try
        {
            var path = Path.Combine(Folder, MarksFile);
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject o) return marks;
            if (o["sent"] is JsonObject sent)
            {
                foreach (var (file, value) in sent)
                {
                    if (value is not JsonObject v || v["id"] is not JsonValue idv || !idv.TryGetValue<string>(out var id)) continue;
                    DateTimeOffset? at = v["at"] is JsonValue av && av.TryGetValue<string>(out var a)
                        && DateTimeOffset.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
                    marks.Sent[file] = (id, at);
                }
            }
            if (o["gone"] is JsonArray goneList)
            {
                foreach (var g in goneList)
                {
                    if (g is JsonValue gv && gv.TryGetValue<string>(out var file) && IsOwnName(file))
                        marks.Gone.Add(file);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Unreadable: no marks (the index still has the sent ones, as a rule).
        }
        return marks;
    }

    private void WriteMarks()
    {
        var sent = new JsonObject();
        foreach (var i in items)
        {
            if (i.SentId == null) continue;
            var o = new JsonObject { ["id"] = i.SentId };
            if (i.SentAt is { } at) o["at"] = at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            sent[i.File] = o;
        }
        var goneList = new JsonArray();
        foreach (var g in gone) goneList.Add(g);
        try
        {
            WriteAtomically(Path.Combine(Folder, MarksFile), new JsonObject { ["sent"] = sent, ["gone"] = goneList }.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in the index too; written again at the next change.
        }
    }

    /// <summary>Writes a file whole or not at all: to a temporary file, flushed to the disk, then put in place.</summary>
    private static void WriteAtomically(string path, string text)
    {
        var temp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally
        {
            TryDelete(temp);
        }
    }
}
