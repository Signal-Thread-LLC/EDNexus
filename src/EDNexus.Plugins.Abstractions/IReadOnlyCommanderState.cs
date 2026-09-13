namespace EDNexus.Plugins.Abstractions;

/// <summary>
/// A read-only snapshot of the commander's current state, mirroring the host's internal
/// <c>CommanderState</c> read model. Only the host's <c>StateTracker</c> may mutate the
/// underlying state; plugins only ever see this read-only view.
/// </summary>
/// <remarks>
/// This is a minimal, stable surface for Phase 11's initial SDK contract. Additional read-only
/// projections (cargo, materials, on-foot inventory, etc.) land alongside the storage/dashboard
/// work that consumes them, without breaking existing plugins built against this interface.
/// </remarks>
public interface IReadOnlyCommanderState
{
    /// <summary>The commander's name, or <see langword="null"/> until known.</summary>
    string? Name { get; }

    /// <summary>Current credit balance.</summary>
    long Balance { get; }

    /// <summary>The current ship type (internal symbol), or <see langword="null"/> until known.</summary>
    string? Ship { get; }

    /// <summary>The commander-assigned name of the current ship, or <see langword="null"/> if unset.</summary>
    string? ShipName { get => null; }

    /// <summary>The current star system, or <see langword="null"/> until known.</summary>
    string? StarSystem { get; }

    /// <summary>The current body within <see cref="StarSystem"/>, or <see langword="null"/> when not near one.</summary>
    string? Body { get => null; }

    /// <summary>Whether the commander is currently docked at a station or carrier.</summary>
    bool Docked { get; }

    /// <summary>
    /// The docked station's display name (a commander's own fleet carrier resolves to its given
    /// name rather than its callsign), or <see langword="null"/> when not docked.
    /// </summary>
    string? StationDisplayName { get => null; }

    /// <summary>UTC timestamp of the last event that updated this state.</summary>
    DateTimeOffset LastUpdated { get; }
}
