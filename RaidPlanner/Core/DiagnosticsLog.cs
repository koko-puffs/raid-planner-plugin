using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaidPlanner.Core;

/// <summary>
/// Settings → Recording → <i>Diagnostics</i> (since 0.4.0; off, and off again each time the plugin loads): while
/// on, the raw values behind each reading the plugin hasn't been able to check in the game (the [verify] notes:
/// a cast stopped, a hit's bonus % and flags, an item's action type, the Raise and Weakness statuses' sources, the
/// enmity list, the limit break gauge, the head signs, your own recasts, where a ground action was placed…) are
/// written to a plain-text file next to the recordings, <c>diagnostics-&lt;date&gt;.log</c>, one line per reading:
/// the time, the zone, the pull (and how far into it), what was read and its numbers. The file holds character
/// names (the party's, once a pull, and whoever a line is about).
///
/// Cheap: off, the game side asks one bool (<see cref="On"/>) and does nothing else; on, lines are gathered in
/// memory and written on a worker at a pull's end, a zone change, the plugin unloading, turning it off, or past
/// <see cref="FlushAt"/> characters. A file stops at <see cref="FileCap"/> bytes: it's moved aside to
/// <c>diagnostics-&lt;date&gt;.1.log</c> (one older part kept) and a new one begun; only the newest
/// <see cref="KeepFiles"/> such files are kept. Nothing here ever throws: a line that can't be written is lost.
/// The game side's busiest readings (every hit) ask <see cref="Room"/> before they make their line, so a line that
/// wouldn't be kept isn't made at all: past <see cref="MostBuffered"/> characters gathered or still waiting for the
/// disk, lines are dropped (and counted in the file) rather than piled up in memory.
/// Game-free (the game side makes the lines), so it's tested.
/// </summary>
public sealed class DiagnosticsLog : IDisposable
{
    /// <summary>A file's most bytes before it's moved aside (5 MB).</summary>
    public const long DefaultFileCap = 5L * 1024 * 1024;

    /// <summary>Characters gathered before they're written whatever else happens.</summary>
    public const int FlushAt = 64 * 1024;

    /// <summary>Characters gathered or waiting for the disk at most (when the disk won't take them): past it lines are dropped.</summary>
    public const int MostBuffered = 4 * FlushAt;

    /// <summary>How many diagnostics files are kept in the folder (the newest).</summary>
    public const int KeepFiles = 6;

    public const string Prefix = "diagnostics-";

    public const string Extension = ".log";

    private readonly string folder;
    private readonly Func<DateTime> clock;
    private readonly StringBuilder buffer = new();
    private readonly object gate = new();
    private Task writing = Task.CompletedTask;
    private volatile bool on;
    private int dropped;

    /// <summary>Characters handed to the worker and not yet written (under <see cref="gate"/>).</summary>
    private int pending;

    /// <summary>Characters gathered plus <see cref="pending"/>: what <see cref="Room"/> reads without the lock.</summary>
    private volatile int held;

    /// <param name="folder">Where the recordings are (the plugin's config folder's recordings folder).</param>
    /// <param name="clock">The local time (the tests' own).</param>
    public DiagnosticsLog(string folder, Func<DateTime>? clock = null)
    {
        this.folder = folder;
        this.clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>A file's most bytes before it's moved aside (<see cref="DefaultFileCap"/>; the tests' smaller).</summary>
    public long FileCap { get; init; } = DefaultFileCap;

    /// <summary>
    /// Where a line is: the zone (its id and name), the pull going on (0 none) and the seconds into it (NaN none). Asked
    /// once a line, only while on; set by the game side.
    /// </summary>
    public Func<(uint Territory, string Zone, int Pull, double At)>? Context { get; set; }

    /// <summary>Lines are being written (the one question the game side asks on its hot paths).</summary>
    public bool On
    {
        get => on;
        set
        {
            if (value == on) return;
            if (value)
            {
                on = true;
                WriteText("diagnostics", "on");
            }
            else
            {
                WriteText("diagnostics", "off");
                on = false;
                Flush();
            }
        }
    }

    /// <summary>
    /// Would a line be kept now: on, and room for it (<see cref="MostBuffered"/>, with what's still waiting for the disk)?
    /// Asked before a line is made on a busy path, so nothing is formatted for a line that would be dropped. While on and
    /// full, the line asked about is counted as lost (the file says how many). Cheap: two reads, nothing allocated.
    /// </summary>
    public bool Room()
    {
        if (!on) return false;
        if (held < MostBuffered) return true;
        lock (gate) dropped++;
        return false;
    }

    /// <summary>What the worker waits on before it writes (the tests' slow disk), or null.</summary>
    internal System.Threading.ManualResetEventSlim? HoldWrites { get; init; }

    /// <summary>The file today's lines go to.</summary>
    public string FileFor(DateTime day) => Path.Combine(folder, Prefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Extension);

    /// <summary>One reading (only while on): what it is (a short word) and its numbers, formatted the invariant way.</summary>
    public void Write(string kind, FormattableString text)
    {
        if (!on) return;
        try
        {
            WriteText(kind, text.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception)
        {
            // A line that can't be made is lost, never the frame.
        }
    }

    /// <summary>One reading (only while on): what it is and its text as it is.</summary>
    public void WriteText(string kind, string text)
    {
        if (!on) return;
        try
        {
            var (territory, zone, pull, at) = Context?.Invoke() ?? (0u, string.Empty, 0, double.NaN);
            var line = new StringBuilder(96 + text.Length)
                .Append(clock().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(" | zone ").Append(territory.ToString(CultureInfo.InvariantCulture));
            if (zone.Length > 0) line.Append(' ').Append(zone);
            line.Append(" | ");
            if (pull > 0)
            {
                line.Append("pull ").Append(pull.ToString(CultureInfo.InvariantCulture));
                if (double.IsFinite(at)) line.Append(" @ ").Append(at.ToString("0.00", CultureInfo.InvariantCulture));
            }
            else
            {
                line.Append("no pull");
            }
            line.Append(" | ").Append(kind).Append(" | ").Append(text.Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
            var full = false;
            lock (gate)
            {
                // Full as Room tells it (the line that reaches the cap is kept).
                if (buffer.Length + pending >= MostBuffered)
                {
                    dropped++;
                    return;
                }
                buffer.Append(line);
                held = buffer.Length + pending;
                full = buffer.Length >= FlushAt;
            }
            if (full) Flush();
        }
        catch (Exception)
        {
            // Never into the game.
        }
    }

    /// <summary>
    /// What's gathered goes to the file, on a worker (after any write still going): a pull's end, a zone change, turning it
    /// off, the plugin unloading. Nothing gathered: nothing done.
    /// </summary>
    public void Flush()
    {
        try
        {
            string text;
            int lost;
            lock (gate)
            {
                if (buffer.Length == 0 && dropped == 0) return;
                text = buffer.ToString();
                buffer.Clear();
                lost = dropped;
                dropped = 0;
            }
            if (lost > 0) text += $"{clock().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} | diagnostics | {lost} lines lost (the file couldn't be written fast enough)\n";
            var day = clock();
            lock (gate)
            {
                // Held until the worker has written it (or failed to).
                pending += text.Length;
                held = buffer.Length + pending;
                writing = writing.ContinueWith(_ => Written(day, text), TaskScheduler.Default);
            }
        }
        catch (Exception)
        {
            // Lost, never thrown.
        }
    }

    /// <summary>Waits (a little at most) for the lines handed to the worker to be written: the tests, and unloading.</summary>
    public bool WaitForWrites(TimeSpan most)
    {
        try
        {
            Task w;
            lock (gate) w = writing;
            return w.Wait(most);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>On the worker: the text written (<see cref="Append"/>), and no longer held, whatever happened.</summary>
    private void Written(DateTime day, string text)
    {
        try
        {
            HoldWrites?.Wait();
            Append(day, text);
        }
        catch (Exception)
        {
            // Lost.
        }
        finally
        {
            lock (gate)
            {
                pending -= text.Length;
                held = buffer.Length + pending;
            }
        }
    }

    /// <summary>Lines added to the day's file: moved aside first when they'd take it past <see cref="FileCap"/>; old files let go.</summary>
    private void Append(DateTime day, string text)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var path = FileFor(day);
            var bytes = Encoding.UTF8.GetByteCount(text);
            var info = new FileInfo(path);
            if (info.Exists && info.Length + bytes > FileCap)
                File.Move(path, Path.ChangeExtension(path, null) + ".1" + Extension, true);
            File.AppendAllText(path, text, new UTF8Encoding(false));
            Prune();
        }
        catch (Exception)
        {
            // The disk said no: these lines are lost.
        }
    }

    /// <summary>Only the newest <see cref="KeepFiles"/> diagnostics files stay.</summary>
    private void Prune()
    {
        try
        {
            var files = Directory.GetFiles(folder, Prefix + "*" + Extension).Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            foreach (var f in files.Skip(KeepFiles))
            {
                try
                {
                    f.Delete();
                }
                catch (Exception)
                {
                    // Tried again at the next write.
                }
            }
        }
        catch (Exception)
        {
            // Not listed: nothing let go.
        }
    }

    /// <summary>Everything gathered written (waiting a moment for it), and off.</summary>
    public void Dispose()
    {
        On = false;
        Flush();
        WaitForWrites(TimeSpan.FromSeconds(2));
    }
}
