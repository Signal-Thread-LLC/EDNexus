using EDNexus.Core.Journal;
using EDNexus.Core.State;
using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting.Bridge;

/// <summary>
/// The read-only adapter between the engine and plugins: turns one engine's
/// <see cref="JournalEventBus"/> and <see cref="CommanderState"/> into the SDK's
/// <see cref="IPluginEvents"/> and <see cref="IReadOnlyCommanderState"/>, one
/// <see cref="PluginBridgeSession"/> per plugin. The plugin loader builds each plugin's
/// <see cref="IPluginContext"/> from a session's <see cref="PluginBridgeSession.Events"/> and
/// <see cref="PluginBridgeSession.State"/>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing a session hands out is, wraps publicly, or can be cast back to an engine type: plugins
/// get <see cref="IJournalEvent"/> views, a state view with no setters, and frozen copies of the
/// collections. The bridge adds no mutation path — <c>StateTracker</c> stays the only writer.
/// (This is least-privilege plumbing, not a sandbox: in-process code can always use reflection.)
/// </para>
/// <para>
/// <b>Dispatch model.</b> Plugin handlers never run on the journal thread. Each plugin gets its own
/// bounded queue and background thread (see <see cref="IPluginEvents"/>), fed after every engine
/// subscriber has handled the entry. A handler that throws is reported through
/// <see cref="PluginBridgeOptions.HandlerError"/> with the plugin's id and delivery carries on;
/// a handler that blocks only delays its own plugin.
/// </para>
/// <para>
/// A bridge is bound to one bus. The app rebuilds its <c>EngineHost</c> (and so its bus) when
/// leaving developer mode or resetting to live, so the loader must dispose its sessions and attach
/// new ones to the new host's bridge.
/// </para>
/// </remarks>
public sealed class PluginBridge
{
    private readonly JournalEventBus _bus;
    private readonly CommanderState _state;
    private readonly PluginBridgeOptions _options;

    /// <param name="bus">The engine bus to observe. The bridge only ever subscribes to it.</param>
    /// <param name="state">The engine's commander state. The bridge only ever reads it.</param>
    /// <param name="options">Dispatch and developer-mode options; defaults when null.</param>
    public PluginBridge(JournalEventBus bus, CommanderState state, PluginBridgeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(state);
        if (options is not null && options.QueueCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "QueueCapacity must be at least 1.");
        _bus = bus;
        _state = state;
        _options = options ?? new PluginBridgeOptions();
    }

    /// <summary>
    /// Creates the event feed and state view for one plugin. Capabilities decide what is wired:
    /// without <see cref="PluginCapabilities.Events"/> the feed refuses every subscription, and
    /// without <see cref="PluginCapabilities.State"/> the state view refuses every read, both with
    /// <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    /// <param name="manifest">The plugin's validated manifest; its id attributes errors.</param>
    /// <param name="grantedCapabilities">
    /// The capabilities the user has granted, or null to grant exactly what the manifest declares.
    /// Capabilities not declared by the manifest are ignored even if granted.
    /// </param>
    public PluginBridgeSession Attach(PluginManifest manifest, IEnumerable<string>? grantedCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var granted = new HashSet<string>(manifest.Capabilities, StringComparer.Ordinal);
        if (grantedCapabilities is not null)
            granted.IntersectWith(grantedCapabilities);

        // Developer mode feeds the bus fabricated events. EDDN, Inara, Discord and the Twitch card all
        // go silent while it is on, so a plugin that can phone home is held to the same rule: it sees
        // no simulated events and an empty commander. Plugins that cannot phone home still see
        // everything, so their cards stay exercisable, with each event flagged IsSimulated.
        var networked = granted.Contains(PluginCapabilities.Network);
        var isSimulated = SafePredicate(_options.IsSimulated);
        var onError = _options.HandlerError ?? (static _ => { });

        PluginEvents? events = granted.Contains(PluginCapabilities.Events)
            ? new PluginEvents(_bus, manifest.Id, networked, isSimulated, onError, _options.QueueCapacity)
            : null;

        IReadOnlyCommanderState state = granted.Contains(PluginCapabilities.State)
            ? new CommanderStateView(_state, networked ? isSimulated : static () => false)
            : new DeniedCommanderState(manifest.Id);

        return new PluginBridgeSession(manifest.Id, events, events ?? (IPluginEvents)new DeniedPluginEvents(manifest.Id), state);
    }

    /// <summary>
    /// Wraps the developer-mode predicate so it never throws onto the journal thread; a predicate
    /// that fails is treated as "simulated" (fail closed, the same way the reporters would).
    /// </summary>
    private static Func<bool> SafePredicate(Func<bool>? predicate)
    {
        if (predicate is null) return static () => false;
        return () =>
        {
            try { return predicate(); }
            catch { return true; }
        };
    }
}

/// <summary>Options for a <see cref="PluginBridge"/>.</summary>
public sealed class PluginBridgeOptions
{
    /// <summary>
    /// Live predicate: true while the bus is carrying developer-mode (fabricated) events. Evaluated on
    /// the journal thread as each event is published, so keep it cheap. The app passes the same
    /// predicate it gives <c>EngineHost</c> as <c>reportingSuppressed</c>.
    /// </summary>
    public Func<bool>? IsSimulated { get; init; }

    /// <summary>
    /// Receives every exception a plugin handler throws, tagged with the plugin's id, so the host
    /// can log it and quarantine a repeat offender. Called on the plugin's dispatch thread.
    /// Exceptions it throws are swallowed.
    /// </summary>
    public Action<PluginHandlerError>? HandlerError { get; init; }

    /// <summary>
    /// How many undelivered events a plugin may fall behind before the oldest are dropped. The default
    /// comfortably holds a startup replay of a long session's journal.
    /// </summary>
    public int QueueCapacity { get; init; } = 8192;
}

/// <summary>A plugin event handler threw.</summary>
/// <param name="PluginId">The owning plugin's manifest id.</param>
/// <param name="EventName">The journal event being delivered.</param>
/// <param name="Exception">What the handler threw.</param>
public sealed record PluginHandlerError(string PluginId, string EventName, Exception Exception);

/// <summary>
/// One plugin's attachment to a <see cref="PluginBridge"/>: the <see cref="IPluginEvents"/> and
/// <see cref="IReadOnlyCommanderState"/> to put in its <see cref="IPluginContext"/>, plus delivery
/// diagnostics. Dispose it when the plugin unloads: that removes every handler the plugin
/// registered (so the bus holds no references into its <c>AssemblyLoadContext</c>), discards any
/// queued events and stops its dispatch thread.
/// </summary>
public sealed class PluginBridgeSession : IDisposable
{
    private readonly PluginEvents? _dispatcher;

    internal PluginBridgeSession(string pluginId, PluginEvents? dispatcher, IPluginEvents events, IReadOnlyCommanderState state)
    {
        PluginId = pluginId;
        _dispatcher = dispatcher;
        Events = events;
        State = state;
    }

    /// <summary>The plugin this session belongs to.</summary>
    public string PluginId { get; }

    /// <summary>The plugin's event feed, for <see cref="IPluginContext.Events"/>.</summary>
    public IPluginEvents Events { get; }

    /// <summary>The plugin's read-only state view, for <see cref="IPluginContext.State"/>.</summary>
    public IReadOnlyCommanderState State { get; }

    /// <summary>Events dropped because the plugin fell <see cref="PluginBridgeOptions.QueueCapacity"/> behind.</summary>
    public long DroppedEventCount => _dispatcher?.DroppedEventCount ?? 0;

    /// <summary>Handler invocations that threw, for quarantine decisions.</summary>
    public long HandlerErrorCount => _dispatcher?.HandlerErrorCount ?? 0;

    /// <summary>Events queued for the plugin but not yet delivered.</summary>
    public int PendingEventCount => _dispatcher?.PendingCount ?? 0;

    /// <summary>
    /// After <see cref="Dispose"/>, waits up to <paramref name="timeout"/> for the dispatch thread to
    /// finish the handler it may be running. Returns false if the plugin is stuck in a handler.
    /// </summary>
    public bool WaitForDispatchExit(TimeSpan timeout) => _dispatcher?.WaitForExit(timeout) ?? true;

    /// <inheritdoc />
    public void Dispose() => _dispatcher?.Dispose();
}
