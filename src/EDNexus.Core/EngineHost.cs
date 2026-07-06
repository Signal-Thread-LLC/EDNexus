using EDNexus.Core.Journal;
using EDNexus.Core.State;

namespace EDNexus.Core;

/// <summary>
/// Bundles the engine wiring — event bus, commander state, journal watcher — and manages its
/// background lifetime. Both the UI and the CLI construct one of these instead of assembling the
/// pieces by hand.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly StateTracker _tracker;
    private readonly JournalWatcher? _watcher;
    private Task? _runTask;

    public JournalEventBus Bus { get; } = new();
    public CommanderState State { get; } = new();
    public string? JournalDirectory { get; }
    public bool JournalFound => JournalDirectory is not null;

    public EngineHost(string? journalDir = null)
    {
        JournalDirectory = journalDir ?? JournalPaths.Resolve();
        _tracker = new StateTracker(Bus, State);
        if (JournalDirectory is not null)
            _watcher = new JournalWatcher(JournalDirectory, Bus);
    }

    /// <summary>Warm state from the latest journal, then watch live on a background task.</summary>
    public void Start()
    {
        if (_watcher is null) return;
        _watcher.Replay();
        _runTask = Task.Run(() => _watcher.RunAsync(_cts.Token));
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _runTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { /* cancellation */ }
        _cts.Dispose();
    }
}
