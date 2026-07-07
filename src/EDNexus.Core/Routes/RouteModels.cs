namespace EDNexus.Core.Routes;

/// <summary>
/// A request to plot a neutron-highway route: from where, to where, and the ship's laden jump range.
/// <see cref="Efficiency"/> is Spansh's 0–100 detour tolerance (higher = more neutron boosts, fewer
/// jumps, longer path); 60 matches Spansh's own default.
/// </summary>
public sealed record RoutePlotRequest(string From, string To, double JumpRangeLy, int Efficiency = 60);

/// <summary>
/// One waypoint on a plotted route: the system to jump to, how many jumps it takes from the previous
/// waypoint (a neutron boost condenses several plain jumps into one), whether it is a neutron star to
/// boost off, and how far along the route this leaves the commander.
/// </summary>
public sealed record RouteHop(
    string System,
    int Jumps,
    bool IsNeutron,
    double DistanceJumpedLy,
    double DistanceRemainingLy);

/// <summary>
/// A plotted route: its ordered <see cref="Hops"/> (source first, destination last) plus the totals
/// derived from them, so the UI can step through the hops and show progress.
/// </summary>
public sealed record RoutePlan(string From, string To, IReadOnlyList<RouteHop> Hops)
{
    /// <summary>Total in-game jumps across the whole route (neutron boosts counted as they occur).</summary>
    public int TotalJumps => Hops.Sum(h => h.Jumps);

    /// <summary>Number of waypoints excluding the origin — i.e. how many systems there are to travel to.</summary>
    public int WaypointCount => Math.Max(0, Hops.Count - 1);

    /// <summary>Neutron scoop-boost stars along the way.</summary>
    public int NeutronCount => Hops.Count(h => h.IsNeutron);
}

/// <summary>
/// Plots long-distance routes for the app. Backed by an external plotter (Spansh); implementations
/// are network-bound and cancellable, and return null when no route could be plotted (unknown system,
/// out-of-range ship, or a transient failure) rather than throwing.
/// </summary>
public interface IRoutePlotter
{
    /// <summary>Human-readable name of the backing data source, e.g. "Spansh".</summary>
    string SourceName { get; }

    /// <summary>
    /// Plot the route for <paramref name="request"/>, or null when none could be produced. The backing
    /// client never throws for network/HTTP problems, matching the reporting clients' convention.
    /// </summary>
    Task<RoutePlan?> PlotAsync(RoutePlotRequest request, CancellationToken ct = default);
}
