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
/// A "galaxy plotter" request: plot a plain jump route from <see cref="From"/> to <see cref="To"/>
/// using the ship's actual FSD physics, with neutron boosting turned off by default
/// (<see cref="UseSupercharge"/>). Unlike the neutron plotter, Spansh needs the drive's real
/// parameters here so it can model fuel burn per jump; the caller derives them from the journal
/// Loadout. Distances/masses are in light years and tonnes.
/// </summary>
public sealed class SpanshGalaxyRouteQuery
{
    public required string From { get; init; }
    public required string To { get; init; }

    /// <summary>FSD optimal mass (t), after any engineering — the bigger it is relative to ship mass, the further the jump.</summary>
    public required double OptimalMass { get; init; }

    /// <summary>Unladen ship mass (t): hull + modules, excluding fuel and cargo.</summary>
    public required double BaseMass { get; init; }

    /// <summary>Main fuel tank capacity (t).</summary>
    public required double TankSize { get; init; }

    /// <summary>Reserve tank capacity (t).</summary>
    public double ReserveSize { get; init; }

    /// <summary>FSD linear fuel constant for the drive's rating.</summary>
    public required double FuelMultiplier { get; init; }

    /// <summary>FSD fuel power constant for the drive's size.</summary>
    public required double FuelPower { get; init; }

    /// <summary>Maximum fuel burned in a single jump (t), after any engineering.</summary>
    public required double MaxFuelPerJump { get; init; }

    /// <summary>Flat jump-range bonus from a Guardian FSD booster (ly); 0 when none is fitted.</summary>
    public double RangeBoost { get; init; }

    /// <summary>Cargo carried (t) — heavier ships jump shorter.</summary>
    public double Cargo { get; init; }

    /// <summary>Whether to super-charge (neutron boost) along the way. Off by default — that is the whole point of this query.</summary>
    public bool UseSupercharge { get; init; }

    /// <summary>Spansh routing algorithm; "optimistic" matches the site's default.</summary>
    public string Algorithm { get; init; } = "optimistic";
}

/// <summary>
/// A fleet-carrier route request: plot the 500 ly carrier hops from <see cref="From"/> to
/// <see cref="To"/>. Carriers never neutron-boost; the plotter instead reports the tritium each hop
/// burns and where it can be restocked.
/// </summary>
public sealed class SpanshFleetCarrierRouteQuery
{
    public required string From { get; init; }
    public required string To { get; init; }

    /// <summary>Non-tritium cargo aboard (t): it eats into the carrier's mass budget and shortens hops.</summary>
    public double CapacityUsed { get; init; }

    /// <summary>
    /// When true, Spansh works out the minimum tritium the trip needs and — on routes longer than one
    /// tank — models a top-up at every stop, so <c>fuel_in_tank</c> comes back pinned at the tank
    /// maximum (useless for showing burn). When false it assumes a full tank at departure and reports the
    /// tank draining hop by hop, flagging where a restock is due. False is the default so the plot shows
    /// tritium actually being consumed.
    /// </summary>
    public bool CalculateStartingFuel { get; init; }
}

/// <summary>
/// One waypoint on a plotted route: the system to jump to, how many jumps it takes to reach it from
/// the previous waypoint (a neutron boost covers several plain jumps' worth of distance in one),
/// whether it is a neutron star to scoop-boost at, and the along-route distances. The fuel fields are
/// populated by the galaxy and fleet-carrier plotters (ship fuel / tritium in tonnes) and left null by
/// the neutron plotter, which does not report fuel.
/// </summary>
public sealed record SpanshRouteWaypoint(
    string System,
    int Jumps,
    bool IsNeutron,
    double DistanceJumpedLy,
    double DistanceRemainingLy,
    double? FuelUsed = null,
    double? FuelInTank = null,
    bool IsScoopable = false,
    bool MustRestock = false,
    double? RestockAmount = null,
    bool HasIcyRing = false);

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
