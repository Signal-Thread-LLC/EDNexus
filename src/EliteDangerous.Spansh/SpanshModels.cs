namespace EliteDangerous.Spansh;

/// <summary>
/// A station search: find stations near <see cref="ReferenceSystem"/> whose market has the wanted
/// side of <see cref="CommodityName"/>. <see cref="WantsDemand"/> selects stations that <b>buy</b>
/// the commodity (have demand); when false, stations that <b>sell</b> it (have supply).
/// </summary>
public sealed class SpanshStationQuery
{
    public required string CommodityName { get; init; }
    public required string ReferenceSystem { get; init; }
    public bool WantsDemand { get; init; } = true;
    public int MaxResults { get; init; } = 10;
}

/// <summary>One commodity line from a station's market, exactly as Spansh reports it.</summary>
public sealed record SpanshCommodity(string Name, int BuyPrice, int SellPrice, int Supply, int Demand);

/// <summary>
/// A station returned by the search: where it is, how far its system sits from the reference, when
/// its market was last seen, and the market lines Spansh returned for it.
/// </summary>
public sealed record SpanshStation(
    string SystemName,
    string StationName,
    double DistanceLy,
    DateTimeOffset? MarketUpdated,
    IReadOnlyList<SpanshCommodity> Commodities);

/// <summary>
/// The parsed result of a station search. Mirrors the reporting clients' convention: transport and
/// HTTP failures never throw — they surface as <see cref="IsOk"/> false with an <see cref="Error"/>
/// so callers can degrade gracefully.
/// </summary>
public sealed class SpanshStationsResult
{
    /// <summary>True when the request reached Spansh and parsed cleanly.</summary>
    public bool IsOk { get; init; }

    /// <summary>Failure detail when <see cref="IsOk"/> is false; null on success.</summary>
    public string? Error { get; init; }

    /// <summary>Matching stations, nearest first (as Spansh ordered them). Empty on failure or no match.</summary>
    public IReadOnlyList<SpanshStation> Stations { get; init; } = Array.Empty<SpanshStation>();

    public static SpanshStationsResult Ok(IReadOnlyList<SpanshStation> stations)
        => new() { IsOk = true, Stations = stations };

    /// <summary>A purely transport/HTTP failure — the query never produced usable data.</summary>
    public static SpanshStationsResult TransportError(string message)
        => new() { IsOk = false, Error = message };
}
