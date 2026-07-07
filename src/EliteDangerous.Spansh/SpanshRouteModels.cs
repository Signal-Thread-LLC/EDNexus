namespace EliteDangerous.Spansh;

/// <summary>
/// A neutron-highway route request: plot the fewest-jumps path from <see cref="From"/> to
/// <see cref="To"/> for a ship with the given jump <see cref="RangeLy"/>, boosting off neutron stars.
/// </summary>
/// <param name="RangeLy">The ship's laden jump range, in light years.</param>
/// <param name="Efficiency">
/// Spansh's route "efficiency" 0–100: how far the plotter may detour to chain neutron boosts. 60 is
/// Spansh's own default — higher packs more boosts (fewer jumps) at the cost of a longer path.
/// </param>
public sealed class SpanshRouteQuery
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required double RangeLy { get; init; }
    public int Efficiency { get; init; } = 60;
}

/// <summary>
/// One waypoint on a plotted route: the system to jump to, how many jumps it takes to reach it from
/// the previous waypoint (a neutron boost covers several plain jumps' worth of distance in one),
/// whether it is a neutron star to scoop-boost at, and the along-route distances.
/// </summary>
public sealed record SpanshRouteWaypoint(
    string System,
    int Jumps,
    bool IsNeutron,
    double DistanceJumpedLy,
    double DistanceRemainingLy);

/// <summary>
/// The parsed result of a route plot. Mirrors <see cref="SpanshStationsResult"/>: transport, HTTP and
/// timed-out-job failures never throw — they surface as <see cref="IsOk"/> false with an
/// <see cref="Error"/> so callers can degrade gracefully.
/// </summary>
public sealed class SpanshRouteResult
{
    /// <summary>True when the job completed and a route parsed cleanly.</summary>
    public bool IsOk { get; init; }

    /// <summary>Failure detail when <see cref="IsOk"/> is false; null on success.</summary>
    public string? Error { get; init; }

    /// <summary>The ordered waypoints, source first, destination last. Empty on failure.</summary>
    public IReadOnlyList<SpanshRouteWaypoint> Waypoints { get; init; } = Array.Empty<SpanshRouteWaypoint>();

    public static SpanshRouteResult Ok(IReadOnlyList<SpanshRouteWaypoint> waypoints)
        => new() { IsOk = true, Waypoints = waypoints };

    public static SpanshRouteResult Failure(string message)
        => new() { IsOk = false, Error = message };
}
