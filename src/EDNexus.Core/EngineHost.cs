using System.Reflection;
using EDNexus.Core.Colonisation;
using EDNexus.Core.Journal;
using EDNexus.Core.Reporting;
using EDNexus.Core.Settings;
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
    private readonly ReporterHost? _reporters;
    private Task? _runTask;

    public JournalEventBus Bus { get; } = new();
    public CommanderState State { get; } = new();
    public ColonisationTracker Colonisation { get; }
    public string? JournalDirectory { get; }
    public bool JournalFound => JournalDirectory is not null;

    /// <param name="journalDir">Journal folder, or null to auto-detect.</param>
    /// <param name="settings">
    /// When supplied, wires the EDDN/Inara data reporters (still gated on their per-service opt-in).
    /// The CLI passes null, so its replay-only runs never transmit.
    /// </param>
    /// <param name="reportingSuppressed">
    /// Optional live predicate; while it returns true the reporters go silent. The app wires this to
    /// developer mode so fabricated events never reach EDDN or Inara.
    /// </param>
    public EngineHost(string? journalDir = null, AppSettings? settings = null, Func<bool>? reportingSuppressed = null)
    {
        JournalDirectory = journalDir ?? JournalPaths.Resolve();
        _tracker = new StateTracker(Bus, State);
        Colonisation = new ColonisationTracker(Bus, State);
        if (settings is not null)
            _reporters = new ReporterHost(Bus, settings, ResolveVersion(), IsDevelopmentBuild, reportingSuppressed);
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
