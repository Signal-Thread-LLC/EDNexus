using System.Net.Http;
using System.Net.Http.Headers;
using EDNexus.Core.Colonisation;
using EDNexus.Core.Journal;
using EDNexus.Core.Market;
using EDNexus.Core.Settings;
using EDNexus.Core.State;
using EDNexus.Core.Trade;

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
    private readonly HttpClient _http;
    private Task? _runTask;

    public JournalEventBus Bus { get; } = new();
    public CommanderState State { get; } = new();
    public ColonisationTracker Colonisation { get; }
    public MarketTracker Market { get; }

    /// <summary>Cross-station "best price nearby" lookups, backed by the Spansh aggregator.</summary>
    public ITradeSearch Trade { get; }

    public string? JournalDirectory { get; }
    public bool JournalFound => JournalDirectory is not null;

    public EngineHost(string? journalDir = null)
    {
        JournalDirectory = journalDir ?? JournalPaths.Resolve();
        _tracker = new StateTracker(Bus, State);
        Colonisation = new ColonisationTracker(Bus, State);
        Market = new MarketTracker(Bus, State);

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("EDNexus", typeof(EngineHost).Assembly.GetName().Version?.ToString() ?? "0.1"));
        // Trade responses are cached on disk beside the app's settings so repeat lookups skip the
        // network (Spansh data changes slowly relative to a play session).
        var cacheDir = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath())!, "cache", "trade");
        Trade = new SpanshTradeSearch(_http, new DiskResponseCache(cacheDir, TimeSpan.FromHours(6)));

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
        _http.Dispose();
    }
}
