using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace RaidPlanner.Core;

/// <summary>
/// A kept pull, as the window lists it without reading its whole file: its file, duty, pull,
/// when, how long, how it ended, its mechanics, whether the party's places are in it, and the
/// site's id for it once it's been sent (or uploaded: since 0.6.0 an upload's id is the same kind); since 0.6.0 why
/// the site's logs refused it for good (<paramref name="UploadRefused"/>: never uploaded again); since 0.7.7 how many of the
/// party it kept (<paramref name="PartyCount"/>; null for a pull listed by an older version, until its file is read).
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
    DateTimeOffset? SentAt = null,
    string? UploadRefused = null,
    int? PartyCount = null)
{
    public string Name => Words.PullName(Duty, Pull);
}

/// <summary>
/// The pulls the plugin keeps: one file each in its folder (the plugin's own, under Dalamud's
/// config), an index of them (index.json) so the list opens without reading every file, and the
/// marks that can't be made again from the files (marks.json: which were sent, which the site's logs
/// refused, and which couldn't be deleted yet). The newest first; past <see cref="Keep"/> the oldest
/// sent ones go, and only then the oldest unsent (since 0.6.0 those still to be uploaded are held, up
/// to <see cref="MostHeld"/>). Writes go to a temporary file, flushed to the disk, then put in place,
/// so a crash or a power cut mid-write never leaves half a file. Safe to call from any thread;
/// <see cref="All"/> and <see cref="Find"/> never wait (the window reads them every frame). Since 0.7.7 the list is
/// changed under its lock in memory only, and the index, the marks and the files let go of are written after, one
/// writer at a time and always the latest list (<see cref="Persist()"/>), so a change never waits on the disk behind another.
/// </summary>
public sealed class RecordingStore
{
    public const string IndexFile = "index.json";

    public const string MarksFile = "marks.json";

    /// <summary>The most pulls kept while some are still to be uploaded (<see cref="Waiting"/>): past it the oldest go even so.</summary>
    public const int MostHeld = 200;

    /// <summary>
    /// Since 0.6.0: a pull still to be uploaded (Upload my logs; set by the uploader, asked on any thread). Past
    /// <see cref="Keep"/>, such pulls aren't let go of until there are <see cref="MostHeld"/>.
    /// </summary>
    public Func<RecordingInfo, bool>? Waiting { get; set; }

    /// <summary>The most kept while some are still to be uploaded (<see cref="MostHeld"/>; lower in tests).</summary>
    public int HoldUpTo { get; set; } = MostHeld;

    /// <summary>Tests only: called while a pull's file is written (its temporary file open, not in place yet).</summary>
    internal Action? WhileWriting { get; set; }

    /// <summary>The list's lock: held for changes in memory (and the startup's load), never for the index's or marks' writes.</summary>
    private readonly object gate = new();

    /// <summary>
    /// The disk's lock (since 0.7.7): the index's and the marks' writes and the deletes, one at a time. Taken before
    /// <see cref="gate"/> when both are held, never after.
    /// </summary>
    private readonly object disk = new();

    private List<RecordingInfo> items = new();

    /// <summary>Bumped (under <see cref="gate"/>) on every change the index or the marks would show.</summary>
    private long changes;

    /// <summary>The change last written to both the index and the marks (under <see cref="disk"/>).</summary>
    private long written = -1;

    /// <summary>Files let go of and still to delete (under <see cref="gate"/>; in <see cref="gone"/> too until they are).</summary>
    private readonly List<string> dropping = new();

    /// <summary>
    /// Pulls being saved (their file's name taken, the file not in place yet; under <see cref="gate"/>): a load meanwhile never
    /// recovers their temporary file or cleans up their empty one.
    /// </summary>
    private readonly HashSet<string> saving = new(StringComparer.OrdinalIgnoreCase);

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
        lock (disk)
        {
            bool writeIndex, writeMarks;
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
                // Let go of before this load and not deleted yet: still gone.
                foreach (var file in dropping) gone.Add(file);
                foreach (var path in SafeFiles("*.tmp")) Recover(path);

                // Files the index doesn't know (copied in, or the index lost) are read once.
                var known = list.Select(i => i.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var path in SafeFiles("*" + Words.Extension))
                {
                    var file = Path.GetFileName(path);
                    if (known.Contains(file) || gone.Contains(file) || saving.Contains(file)) continue;
                    try
                    {
                        // Since 0.7.7, an empty file is deleted: a name taken by a save that never finished (a crash before its file was put in place).
                        if (new FileInfo(path).Length == 0)
                        {
                            TryDelete(path);
                            continue;
                        }
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
                    if (list[i].UploadRefused == null && marks.Refused.TryGetValue(list[i].File, out var why))
                        list[i] = list[i] with { UploadRefused = why };
                }
                var count = list.Count;
                items = list
                    .Where(i => !gone.Contains(i.File) && File.Exists(Path.Combine(Folder, i.File)))
                    .GroupBy(i => i.File, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .OrderByDescending(i => i.RecordedAt).ToList();
                writeIndex = !fromIndex || items.Count != count;
                writeMarks = marksChanged;
                Changed();
            }
            if (writeIndex || writeMarks) Persist(writeIndex, writeMarks);
        }
    }

    /// <summary>
    /// A temporary file left by a crash between its flush and its move: a whole recording is put in
    /// place when its own file is missing or empty (the name was taken, the move never came); anything
    /// else is deleted. Since 0.7.7 one a save is still writing (<see cref="saving"/>) is left alone.
    /// Under both locks (no save's last steps, no index or marks write, meanwhile).
    /// </summary>
    private void Recover(string temp)
    {
        try
        {
            var target = temp[..^".tmp".Length];
            if (saving.Contains(Path.GetFileName(target))) return;
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
        new(file, r.Duty.Name, r.Pull, r.RecordedAt, r.Duration, r.Outcome, r.MechanicCount, r.PartyPositions, PartyCount: r.Party.Count);

    /// <summary>
    /// Keeps a pull: writes its file and puts it first in the list; past <see cref="Keep"/>, the oldest
    /// go (sent ones first). Returns its entry. The file is written outside the lock, so the window
    /// never waits on the disk.
    /// </summary>
    public RecordingInfo Save(Recording r)
    {
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
            // Until it's in place, a load meanwhile leaves its temporary file and its empty one alone.
            saving.Add(file);
        }
        try
        {
            // Written straight to the file (since 0.7.7): never the whole file as a string, its bytes, and a buffer of each.
            WriteAtomically(Path.Combine(Folder, file), fs =>
            {
                using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = false });
                r.Write(w);
                WhileWriting?.Invoke();
            });
        }
        catch
        {
            TryDelete(Path.Combine(Folder, file));
            lock (gate) saving.Remove(file);
            throw;
        }
        var info = new RecordingInfo(file, r.Duty.Name, r.Pull, r.RecordedAt, r.Duration, r.Outcome, mechanics, r.PartyPositions,
            PartyCount: r.Party.Count);
        lock (gate)
        {
            saving.Remove(file);
            // The load at startup may have read the file in already.
            items.RemoveAll(i => string.Equals(i.File, file, StringComparison.OrdinalIgnoreCase));
            items.Insert(0, info);
            Prune();
            Changed();
        }
        Persist();
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

    /// <summary>Lets a pull go: out of the list, its file deleted, the index and marks written (all before it returns).</summary>
    public void Delete(RecordingInfo info)
    {
        Remove(info);
        Persist();
    }

    /// <summary>
    /// Since 0.7.7, for the window: a pull is out of the list at once (<see cref="All"/> no longer has it), and its file's
    /// delete and the index's and marks' writes are done on a worker, in turn with every other write. Until then it's
    /// marked gone, so a crash meanwhile still lets it go at the next load. Never waits on the disk.
    /// </summary>
    public Task DeleteLater(RecordingInfo info)
    {
        Remove(info);
        return Task.Run(Persist);
    }

    private void Remove(RecordingInfo info)
    {
        lock (gate)
        {
            items.RemoveAll(i => string.Equals(i.File, info.File, StringComparison.OrdinalIgnoreCase));
            Forget(info.File);
            Changed();
        }
    }

    /// <summary>Notes that a pull was sent to the site (its id there), so the list says so.</summary>
    public RecordingInfo? MarkSent(RecordingInfo info, string id, DateTimeOffset at)
    {
        RecordingInfo marked;
        lock (gate)
        {
            var i = items.FindIndex(x => string.Equals(x.File, info.File, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return null;
            items[i] = items[i] with { SentId = id, SentAt = at };
            Changed();
            marked = items[i];
        }
        Persist();
        return marked;
    }

    /// <summary>Notes that the site's logs refused a pull for good (since 0.6.0; why, in words): it's never uploaded again.</summary>
    public RecordingInfo? MarkRefused(RecordingInfo info, string why)
    {
        RecordingInfo marked;
        lock (gate)
        {
            var i = items.FindIndex(x => string.Equals(x.File, info.File, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return null;
            items[i] = items[i] with { UploadRefused = string.IsNullOrWhiteSpace(why) ? "Refused." : why.Trim() };
            Changed();
            marked = items[i];
        }
        Persist();
        return marked;
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
            Changed();
        }
        Persist();
    }

    /// <summary>Under <see cref="gate"/>: the list as it is now, handed out, and a change to write.</summary>
    private void Changed()
    {
        snapshot = items.ToArray();
        Version++;
        changes++;
    }

    /// <summary>
    /// Since 0.7.7: the files let go of are deleted, then the index and the marks are written as the list is now; one
    /// writer at a time (<see cref="disk"/>), and a writer whose change a later one already wrote writes nothing, so
    /// writes never go out of order. Called after the change, outside <see cref="gate"/>, so the list never waits on the disk.
    /// </summary>
    private void Persist() => Persist(true, true);

    private void Persist(bool index, bool marks)
    {
        lock (disk)
        {
            DeleteDropped();
            IReadOnlyList<RecordingInfo> list;
            string[] goneNow;
            long change;
            lock (gate)
            {
                change = changes;
                list = snapshot;
                goneNow = gone.ToArray();
            }
            if (change <= written) return;
            // The marks first: they're what can't be made again from the files.
            if (marks) WriteMarks(Folder, list, goneNow);
            if (index) WriteIndex(Folder, list);
            if (index && marks) written = change;
        }
    }

    /// <summary>Under <see cref="disk"/>: deletes the files let go of; one the system won't let go yet stays gone (tried again at the next load).</summary>
    private void DeleteDropped()
    {
        string[] files;
        lock (gate)
        {
            if (dropping.Count == 0) return;
            files = dropping.ToArray();
            dropping.Clear();
        }
        foreach (var file in files)
        {
            if (!TryDelete(Path.Combine(Folder, file))) continue;
            lock (gate)
                if (gone.Remove(file)) changes++;
        }
    }

    /// <summary>
    /// Past <see cref="Keep"/>: the oldest pull the site has (sent or uploaded) goes first, and only
    /// then the oldest of the rest, so a run of other pulls doesn't take one you haven't sent while one
    /// that's safely on the site could go instead (since 0.6.0 the site keeps them all: before, only a
    /// person's newest 30 sends). A pull still to be uploaded (<see cref="Waiting"/>) isn't let go of
    /// until there are <see cref="MostHeld"/> (so a night offline loses nothing), and then the oldest goes.
    /// </summary>
    private void Prune()
    {
        var keep = Math.Max(1, Keep);
        var waiting = Waiting;
        while (items.Count > keep)
        {
            var at = items.FindLastIndex(i => i.SentId != null);
            if (at < 0)
            {
                at = waiting == null ? items.Count - 1 : items.FindLastIndex(i => !Held(waiting, i));
                // Every one left is still to be uploaded: held, up to the most.
                if (at < 0)
                {
                    if (items.Count <= Math.Max(keep, HoldUpTo)) break;
                    at = items.Count - 1;
                }
            }
            var file = items[at].File;
            items.RemoveAt(at);
            Forget(file);
        }
    }

    private static bool Held(Func<RecordingInfo, bool> waiting, RecordingInfo i)
    {
        if (i.SentId != null || i.UploadRefused != null) return false;
        try
        {
            return waiting(i);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// Under <see cref="gate"/>: a file let go of. Marked gone and deleted by the next <see cref="Persist()"/> (since 0.7.7: never
    /// under the lock); one the system won't let go yet (held open somewhere) stays marked, to go at the next load.
    /// </summary>
    private void Forget(string file)
    {
        gone.Add(file);
        dropping.Add(file);
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

    private static void WriteIndex(string folder, IReadOnlyList<RecordingInfo> list)
    {
        var arr = new JsonArray();
        foreach (var i in list)
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
            if (i.UploadRefused != null) o["uploadRefused"] = i.UploadRefused;
            // Since 0.7.7 (an older index hasn't it: unknown until the file's read).
            if (i.PartyCount is { } party) o["partyCount"] = party;
            arr.Add(o);
        }
        try
        {
            WriteAtomically(Path.Combine(folder, IndexFile), arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
                    sentAt,
                    n["uploadRefused"]?.GetValue<string>(),
                    // Since 0.7.7; missing (an older version's index) or not a count: unknown, never the entry left out.
                    n["partyCount"] is JsonValue pv && pv.TryGetValue<int>(out var party) && party >= 0 ? party : null));
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

    // ---------- marks.json: what was sent, what the logs refused, and what couldn't be deleted yet ----------

    private sealed record Marks(Dictionary<string, (string Id, DateTimeOffset? At)> Sent, List<string> Gone, Dictionary<string, string> Refused);

    private Marks ReadMarks()
    {
        var marks = new Marks(new Dictionary<string, (string, DateTimeOffset?)>(StringComparer.OrdinalIgnoreCase), new List<string>(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
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
            if (o["refused"] is JsonObject refused)
            {
                foreach (var (file, value) in refused)
                    if (IsOwnName(file) && value is JsonValue rv && rv.TryGetValue<string>(out var why) && !string.IsNullOrWhiteSpace(why))
                        marks.Refused[file] = why.Length > 300 ? why[..300] : why;
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

    private static void WriteMarks(string folder, IReadOnlyList<RecordingInfo> list, IEnumerable<string> gone)
    {
        var sent = new JsonObject();
        foreach (var i in list)
        {
            if (i.SentId == null) continue;
            var o = new JsonObject { ["id"] = i.SentId };
            if (i.SentAt is { } at) o["at"] = at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            sent[i.File] = o;
        }
        var refused = new JsonObject();
        foreach (var i in list)
            if (i.UploadRefused != null) refused[i.File] = i.UploadRefused;
        var goneList = new JsonArray();
        foreach (var g in gone) goneList.Add(g);
        try
        {
            WriteAtomically(Path.Combine(folder, MarksFile), new JsonObject { ["sent"] = sent, ["refused"] = refused, ["gone"] = goneList }.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in the index too; written again at the next change.
        }
    }

    /// <summary>Writes a file whole or not at all: to a temporary file, flushed to the disk, then put in place.</summary>
    private static void WriteAtomically(string path, string text) =>
        WriteAtomically(path, fs =>
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            fs.Write(bytes, 0, bytes.Length);
        });

    /// <summary>Writes a file whole or not at all, by <paramref name="write"/> (to a temporary file, flushed to the disk, then put in place).</summary>
    private static void WriteAtomically(string path, Action<Stream> write)
    {
        var temp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                write(fs);
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
