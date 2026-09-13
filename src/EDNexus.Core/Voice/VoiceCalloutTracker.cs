using EDNexus.Core.Colonisation;
using EDNexus.Core.Exobio;
using EDNexus.Core.Journal;
using EDNexus.Core.State;

namespace EDNexus.Core.Voice;

/// <summary>
/// Feature service that turns key journal moments into spoken callouts: low fuel, a completed
/// exobiology sample run, and a colonisation shopping-list commodity fully covered by the cargo
/// hold. It never speaks itself — it only decides *when* a callout should fire and hands the text to
/// whoever is listening (an <see cref="IVoice"/> in the UI layer, or a unit test). It reads
/// <see cref="CommanderState"/>, <see cref="ExobiologyTracker"/> and <see cref="ColonisationTracker"/>
/// but never mutates any of them.
/// </summary>
public sealed class VoiceCalloutTracker
{
    private readonly CommanderState _state;
    private readonly ExobiologyTracker _exobiology;
    private readonly ColonisationTracker _colonisation;
    private readonly object _gate = new();

    private bool _fuelLow;
    private readonly HashSet<string> _announcedScans = new();
    private readonly Dictionary<string, bool> _shoppingCovered = new();

    /// <summary>Fraction of fuel capacity at/below which a low-fuel callout fires. Default 25%.</summary>
    public double FuelLowThreshold { get; set; } = 0.25;

    /// <summary>
    /// Raised whenever a callout fires. Never raised for events replayed at startup
    /// (<see cref="JournalEntry.IsHistorical"/>) — those just warm up the "already announced" state
    /// silently, the same way the rest of the engine treats a replay as catch-up rather than news.
    /// </summary>
    public event Action<VoiceCallout>? CalloutRaised;

    public VoiceCalloutTracker(JournalEventBus bus, CommanderState state, ExobiologyTracker exobiology, ColonisationTracker colonisation)
    {
        _state = state;
        _exobiology = exobiology;
        _colonisation = colonisation;

        bus.Subscribe("Status", OnStatus);
        bus.Subscribe("ScanOrganic", OnScanOrganic);
        bus.Subscribe("Cargo", OnCargo);
    }

    /// <summary>
    /// Fuel dropping to or below <see cref="FuelLowThreshold"/> of capacity. Fires once per drop below
    /// the line and rearms once refuelled back above it, so a long haul running on fumes doesn't repeat
    /// the callout on every status tick.
    /// </summary>
    private void OnStatus(JournalEntry e)
    {
        if (_state.FuelCapacity <= 0) return;
        var fraction = _state.FuelMain / _state.FuelCapacity;
        var low = fraction <= FuelLowThreshold;

        bool justCrossed;
        lock (_gate)
        {
            justCrossed = low && !_fuelLow;
            _fuelLow = low;
        }

        if (justCrossed && !e.IsHistorical)
            Raise(VoiceCalloutKind.FuelLow, $"Fuel low: {_state.FuelMain:N1} of {_state.FuelCapacity:N1} tonnes.");
    }

    /// <summary>
    /// The suit's "Analyse" step closes a three-sample run. <see cref="ExobiologyTracker"/> is wired
    /// to the same event ahead of this tracker (both subscribe to "ScanOrganic", and the bus invokes
    /// subscribers in subscription order), so by the time this handler runs its
    /// <see cref="ExobiologyTracker.Scans"/> already reflects the completed run. The scan just updated
    /// by this exact event is identified by body key + timestamp, rather than by species name, since
    /// the event's own species text can differ from the catalog's canonical name the tracker settles on.
    /// </summary>
    private void OnScanOrganic(JournalEntry e)
    {
        if (!string.Equals(e.GetString("ScanType"), "Analyse", StringComparison.OrdinalIgnoreCase)) return;
        if (BodyKeyOf(e) is not BodyKey key) return;

        var scan = _exobiology.Scans.FirstOrDefault(s => s.Complete && s.Key == key && s.Updated == e.Timestamp);
        if (scan is null) return;

        var announceKey = $"{key.SystemAddress}:{key.BodyId}:{scan.SpeciesName}";
        bool firstTime;
        lock (_gate) firstTime = _announcedScans.Add(announceKey);
        if (!firstTime) return;

        if (!e.IsHistorical)
            Raise(VoiceCalloutKind.ScanComplete, $"Scan complete: {scan.SpeciesName}.");
    }

    /// <summary>
    /// Mirrors <see cref="ExobiologyTracker"/>'s own body key: the sampler names the body via
    /// <c>Body</c> (an id number), paired with <c>SystemAddress</c>.
    /// </summary>
    private static BodyKey? BodyKeyOf(JournalEntry e)
    {
        if (e.GetInt64("SystemAddress") is not long system) return null;
        var bodyId = e.GetInt64("BodyID") ?? e.GetInt64("Body");
        return bodyId is long id ? new BodyKey(system, (int)id) : null;
    }

    /// <summary>
    /// A cargo change that fully covers an outstanding colonisation shopping-list commodity —
    /// announced once per commodity per site, and re-armed if the requirement grows again (a later
    /// depot snapshot raising the required amount past what's aboard).
    /// </summary>
    private void OnCargo(JournalEntry e)
    {
        var site = _colonisation.ActiveSite;
        if (site is null) return;

        foreach (var item in site.BuildShoppingList(_state.Cargo))
        {
            var key = $"{site.MarketId}:{item.Name}";
            var covered = item.CoveredByHold;

            bool justCovered;
            lock (_gate)
            {
                _shoppingCovered.TryGetValue(key, out var wasCovered);
                justCovered = covered && !wasCovered;
                _shoppingCovered[key] = covered;
            }

            if (justCovered && !e.IsHistorical)
                Raise(VoiceCalloutKind.ShoppingListItemAcquired, $"{item.Name} acquired — enough aboard for the build.");
        }
    }

    private void Raise(VoiceCalloutKind kind, string text) => CalloutRaised?.Invoke(new VoiceCallout(kind, text));
}
