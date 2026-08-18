namespace EliteDangerous.Spansh;

/// <summary>
/// A "nearest station with this service" search: find stations near <see cref="ReferenceSystem"/>
/// offering <see cref="ServiceName"/>, nearest first.
/// </summary>
/// <remarks>
/// <see cref="Subtype"/> narrows the services the game splits into flavours — a Material Trader deals
/// in exactly one of Raw / Manufactured / Encoded, and a Technology Broker in Guardian or Human tech.
/// Sending the wrong flavour is the difference between a useful answer and a wasted trip, so it maps
/// to Spansh's own <c>material_trader</c> / <c>technology_broker</c> filters rather than being
/// filtered client-side.
/// </remarks>
public sealed class SpanshServiceQuery
{
    /// <summary>Spansh service name, e.g. "Material Trader" or "Vista Genomics".</summary>
    public required string ServiceName { get; init; }

    /// <summary>System to measure distance from — usually where the commander is now.</summary>
    public required string ReferenceSystem { get; init; }

    /// <summary>Spansh field the <see cref="Subtype"/> filters on, e.g. "material_trader". Null when unused.</summary>
    public string? SubtypeField { get; init; }

    /// <summary>Flavour of the service, e.g. "Raw" or "Guardian". Null means any.</summary>
    public string? Subtype { get; init; }

    /// <summary>Restrict to stations a large ship can dock at.</summary>
    public bool RequireLargePad { get; init; }

    public int MaxResults { get; init; } = 10;
}

/// <summary>
/// One station offering the requested service: where it is, how far, and the docking detail that
/// decides whether the trip is worth making.
/// </summary>
/// <param name="SystemName">System the station is in.</param>
/// <param name="StationName">Station name.</param>
/// <param name="DistanceLy">Distance from the reference system, in light years.</param>
/// <param name="DistanceToArrivalLs">Supercruise distance from the system's entry point, in light seconds.</param>
/// <param name="StationType">Station type, e.g. "Coriolis Starport".</param>
/// <param name="IsPlanetary">True for surface ports, which need a different approach.</param>
/// <param name="HasLargePad">Whether a large pad is available.</param>
/// <param name="Subtype">The service flavour this station actually offers, when it has one.</param>
/// <param name="Updated">When Spansh last saw this station.</param>
public sealed record SpanshServiceStation(
    string SystemName,
    string StationName,
    double DistanceLy,
    double DistanceToArrivalLs,
    string? StationType,
    bool IsPlanetary,
    bool HasLargePad,
    string? Subtype,
    DateTimeOffset? Updated);

/// <summary>
/// The parsed result of a service search. Follows the same no-throw convention as the rest of the
/// client: transport and HTTP failures surface as <see cref="IsOk"/> false with an <see cref="Error"/>.
/// </summary>
public sealed class SpanshServicesResult
{
    public bool IsOk { get; init; }
    public string? Error { get; init; }

    /// <summary>Matching stations, nearest first. Empty on failure or no match.</summary>
    public IReadOnlyList<SpanshServiceStation> Stations { get; init; } = Array.Empty<SpanshServiceStation>();

    public static SpanshServicesResult Ok(IReadOnlyList<SpanshServiceStation> stations)
        => new() { IsOk = true, Stations = stations };

    public static SpanshServicesResult TransportError(string message)
        => new() { IsOk = false, Error = message };
}
