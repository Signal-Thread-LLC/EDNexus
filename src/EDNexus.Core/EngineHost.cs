using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using EDNexus.Core.Colonisation;
using EDNexus.Core.Journal;
using EDNexus.Core.Market;
using EDNexus.Core.Reporting;
using EDNexus.Core.Settings;
using EDNexus.Core.State;
using EDNexus.Core.Trade;
using EliteDangerous.Spansh;

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
    private readonly ReporterHost? _reporters;
    private readonly HttpClient _http;
    private Task? _runTask;

    public JournalEventBus Bus { get; } = new();
    public CommanderState State { get; } = new();
    public ColonisationTracker Colonisation { get; }
    public MarketTracker Market { get; }

    /// <summary>Cross-station "best price nearby" lookups. Backed by Spansh; swappable via <see cref="ITradeSearch"/>.</summary>
    public ITradeSearch Trade { get; }

    public string? JournalDirectory { get; }
    public bool JournalFound => JournalDirectory is not null;

    /// <param name="journalDir">Journal folder, or null to auto-detect.</param>
    /// <param name="settings">
    /// When supplied, wires the EDDN/Inara data reporters (still gated on their per-service opt-in).
    /// The CLI passes null, so its replay-only runs never transmit.
    /// </param>
    public EngineHost(string? journalDir = null, AppSettings? settings = null)
    {
        JournalDirectory = journalDir ?? JournalPaths.Resolve();
        _tracker = new StateTracker(Bus, State);
        Colonisation = new ColonisationTracker(Bus, State);
        Market = new MarketTracker(Bus, State);

        // Shared client for outbound trade lookups. The EDDN/Inara reporters own their own client
        // inside ReporterHost, so this one is dedicated to the read-side (Spansh) queries.
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EDNexus", ResolveVersion()));
        var cacheDir = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath())!, "cache", "trade");
        Trade = new SpanshTradeSearch(
            new SpanshClient(new SpanshClientOptions { SoftwareName = "EDNexus", SoftwareVersion = ResolveVersion() }, _http),
            new DiskResponseCache(cacheDir, TimeSpan.FromHours(6)));

        if (settings is not null)
            _reporters = new ReporterHost(Bus, settings, ResolveVersion(), IsDevelopmentBuild);
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
        // Flush any queued reports before tearing down the shared HttpClient.
        try { _reporters?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { /* best effort */ }
        _cts.Dispose();
        _http.Dispose();
    }

    private static string ResolveVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(EngineHost).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = info ?? asm.GetName().Version?.ToString() ?? "0.0.0";
        var plus = version.IndexOf('+');   // strip any "+<gitsha>" build-metadata suffix
        return plus >= 0 ? version[..plus] : version;
    }

    private static bool IsDevelopmentBuild =>
#if DEBUG
        true;
#else
        false;
#endif
}
