using EDNexus.Core.Journal;
using EDNexus.Core.Radio;

namespace EDNexus.Core.Dev;

/// <summary>
/// Decides which <see cref="IRadioPlayer"/> the radio UI (title-bar transport and Space Radio card)
/// acts on. That's the real player normally, and a <see cref="SimulatedRadioPlayer"/> while developer
/// mode is on, so no UI control can start real audio or rewrite the saved radio settings in dev
/// mode. Paths that must always hit the real player (the Settings enable toggle, resume-on-launch)
/// use <see cref="Real"/> directly.
/// </summary>
public sealed class RadioPlayerSelector
{
    private readonly Func<bool> _devEnabled;
    private readonly bool _devToolsAvailable;
    private SimulatedRadioPlayer? _simulated;

    /// <param name="real">The real player.</param>
    /// <param name="devEnabled">Live predicate: is developer mode switched on right now?</param>
    /// <param name="devToolsAvailable">
    /// Whether the dev tools exist in this build (<see cref="FeatureFlags.DeveloperTools"/> by
    /// default). When false no simulation is ever created, so <see cref="Active"/> is always the real player.
    /// </param>
    public RadioPlayerSelector(IRadioPlayer real, Func<bool> devEnabled, bool? devToolsAvailable = null)
    {
        Real = real;
        _devEnabled = devEnabled;
        _devToolsAvailable = devToolsAvailable ?? FeatureFlags.DeveloperTools;
        Real.Changed += RaiseChanged;
    }

    /// <summary>The real player, whatever mode the app is in.</summary>
    public IRadioPlayer Real { get; }

    /// <summary>The developer-mode simulation, or null when the dev tools aren't available or no bus is attached yet.</summary>
    public SimulatedRadioPlayer? Simulated => _simulated;

    /// <summary>True while the UI should show and drive the simulation.</summary>
    public bool IsSimulated => _simulated is not null && _devEnabled();

    /// <summary>The player the UI's controls should act on right now.</summary>
    public IRadioPlayer Active => IsSimulated ? _simulated! : Real;

    /// <summary>Raised when either player changes. Handlers may run on a background thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Start a fresh simulation listening on <paramref name="bus"/>, where the 🎲 publishes
    /// <see cref="RadioSampleSource"/> events. Call it whenever the engine host (and so the bus) is
    /// rebuilt. Does nothing when the dev tools aren't available.
    /// </summary>
    public void AttachSimulation(JournalEventBus bus)
    {
        if (!_devToolsAvailable) return;

        if (_simulated is not null) _simulated.Changed -= RaiseChanged;
        var sim = new SimulatedRadioPlayer();
        sim.Attach(bus);
        sim.Changed += RaiseChanged;
        _simulated = sim;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke();
}
