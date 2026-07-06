using System.Text;

namespace EDNexus.Core.Journal;

/// <summary>
/// Tails the Elite Dangerous journal folder and republishes everything onto a
/// <see cref="JournalEventBus"/>:
/// <list type="bullet">
/// <item>the newest <c>Journal.*.log</c>, following appends and rolling over to new files;</item>
/// <item>the sidecar status files (<c>Status.json</c>, <c>Cargo.json</c>, <c>Market.json</c>, …)
/// whenever they change on disk.</item>
/// </list>
/// Polling (rather than <see cref="FileSystemWatcher"/>) is deliberate: the game holds the
/// journal open and flushes irregularly, and polling with shared read access is the approach
/// proven robust by existing tools.
/// </summary>
public sealed class JournalWatcher
{
    // Sidecar files the game rewrites in place. Each already contains an "event" field.
    private static readonly string[] StatusFiles =
    {
        "Status.json", "Cargo.json", "Market.json", "NavRoute.json", "Backpack.json",
        "ShipLocker.json", "Outfitting.json", "Shipyard.json", "ModulesInfo.json", "FCMaterials.json",
    };

    private readonly string _dir;
    private readonly JournalEventBus _bus;
    private readonly TimeSpan _pollInterval;

    private string? _currentFile;
    private long _position;
    private string _partial = string.Empty;
    private readonly Dictionary<string, DateTime> _statusStamps = new(StringComparer.OrdinalIgnoreCase);

    public JournalWatcher(string journalDir, JournalEventBus bus, TimeSpan? pollInterval = null)
    {
        _dir = journalDir;
        _bus = bus;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public string Directory => _dir;

    /// <summary>
    /// Replays the latest journal file plus current sidecar files (all marked historical) to
    /// rebuild state, then leaves the read position at end-of-file so <see cref="RunAsync"/>
    /// continues seamlessly. Call once before <see cref="RunAsync"/> for a warm start.
    /// </summary>
    public void Replay()
    {
        var latest = LatestJournal();
        if (latest is null) return;

        _currentFile = latest;
        foreach (var line in ReadLinesShared(latest))
            if (JournalEntry.TryParse(line, historical: true, out var e))
                _bus.Publish(e);
        _position = SafeLength(latest);
        _partial = string.Empty;

        foreach (var sf in StatusFiles)
            EmitStatusFile(sf, historical: true);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // If Replay() wasn't called, begin live at the end of the newest file so we don't
        // re-emit history as if it were happening now.
        if (_currentFile is null)
        {
            _currentFile = LatestJournal();
            _position = _currentFile is not null ? SafeLength(_currentFile) : 0;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                PumpJournal();
                foreach (var sf in StatusFiles)
                    EmitStatusFile(sf, historical: false);
            }
            catch (IOException) { /* transient sharing violation — retry next tick */ }
            catch (UnauthorizedAccessException) { }

            try { await Task.Delay(_pollInterval, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private void PumpJournal()
    {
        var latest = LatestJournal();
        if (latest is null) return;

        if (!string.Equals(latest, _currentFile, StringComparison.OrdinalIgnoreCase))
        {
            // Drain the last lines of the previous file before switching.
            if (_currentFile is not null) ReadNewLines(_currentFile);
            _currentFile = latest;
            _position = 0;
            _partial = string.Empty;
        }

        // _currentFile == latest at this point in both branches.
        ReadNewLines(latest);
    }

    private void ReadNewLines(string path)
    {
        long length;
        try
        {
            if (!File.Exists(path)) return;
            length = new FileInfo(path).Length;
        }
        catch { return; }

        // Defensive: if the file shrank (shouldn't happen for a journal), restart from 0.
        if (length < _position) { _position = 0; _partial = string.Empty; }
        if (length == _position) return;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(_position, SeekOrigin.Begin);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        var text = reader.ReadToEnd();
        _position = fs.Position;

        var buffer = _partial + text;
        int start = 0, nl;
        while ((nl = buffer.IndexOf('\n', start)) >= 0)
        {
            var line = buffer.Substring(start, nl - start).TrimEnd('\r');
            start = nl + 1;
            if (JournalEntry.TryParse(line, historical: false, out var e))
                _bus.Publish(e);
        }
        _partial = buffer[start..];
    }

    private void EmitStatusFile(string name, bool historical)
    {
        var path = Path.Combine(_dir, name);
        DateTime stamp;
        try
        {
            if (!File.Exists(path)) return;
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch { return; }

        if (_statusStamps.TryGetValue(name, out var prev) && prev == stamp) return;
        _statusStamps[name] = stamp;

        string content;
        try { content = ReadAllTextShared(path); }
        catch { return; }
        if (string.IsNullOrWhiteSpace(content)) return;

        if (JournalEntry.TryParse(content, historical, out var e))
            _bus.Publish(e);
    }

    private string? LatestJournal()
    {
        try
        {
            var files = new DirectoryInfo(_dir).GetFiles("Journal.*.log");
            if (files.Length == 0) return null;
            // The active file has the newest write time; tie-break on the name (whose embedded
            // timestamp sorts chronologically) to stay stable right after a rollover.
            return files
                .OrderBy(f => f.LastWriteTimeUtc)
                .ThenBy(f => f.Name, StringComparer.Ordinal)
                .Last().FullName;
        }
        catch { return null; }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) is not null)
            yield return line;
    }

    private static string ReadAllTextShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
