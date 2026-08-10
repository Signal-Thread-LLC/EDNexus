namespace EDNexus.Core.State;

/// <summary>
/// Turns the station name the journal reports into the one a commander recognises.
/// </summary>
/// <remarks>
/// For a fleet carrier the journal's <c>StationName</c> is the callsign (e.g. <c>K7Q-B3L</c>), never
/// the name the owner gave it — that only ever arrives on <c>CarrierStats</c>, which reports the
/// commander's <b>own</b> carrier. So the name may only be substituted when the docked callsign is
/// our carrier's: every other carrier in the galaxy also reports a bare callsign, and labelling one
/// of those with our carrier's name would be actively wrong.
/// </remarks>
public static class StationDisplay
{
    /// <summary>
    /// The friendly name for <paramref name="stationName"/> — the carrier's name when the station is
    /// the commander's own fleet carrier and that name is known, otherwise the station name unchanged.
    /// </summary>
    public static string? Resolve(string? stationName, string? carrierName, string? carrierCallsign)
        => IsOwnCarrier(stationName, carrierCallsign) && carrierName is { Length: > 0 }
            ? carrierName
            : stationName;

    /// <summary>
    /// Whether <paramref name="stationName"/> is the commander's own fleet carrier — i.e. the docked
    /// callsign matches the one <c>CarrierStats</c> reported. True even when the carrier's name is
    /// not yet known, since the identity holds either way.
    /// </summary>
    public static bool IsOwnCarrier(string? stationName, string? carrierCallsign)
        => stationName is { Length: > 0 }
           && carrierCallsign is { Length: > 0 }
           && string.Equals(stationName, carrierCallsign, StringComparison.OrdinalIgnoreCase);
}
