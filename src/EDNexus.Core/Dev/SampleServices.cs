using EDNexus.Core.Routes;
using EDNexus.Core.Trade;

namespace EDNexus.Core.Dev;

/// <summary>
/// An offline <see cref="IRoutePlotter"/> for developer mode: it fabricates a plausible neutron route
/// between the requested systems (random intermediate hops from the sample pool, a scatter of neutron
/// boosts) so the route card is fully exercisable without the game running or the Spansh API.
/// </summary>
public sealed class SampleRoutePlotter : IRoutePlotter
{
    private readonly Random _rng;

    public string SourceName => "Spansh (dev)";

    public SampleRoutePlotter(Random rng) => _rng = rng;

    public Task<RoutePlan?> PlotAsync(RoutePlotRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.To) || request.JumpRangeLy <= 0)
            return Task.FromResult<RoutePlan?>(null);

        var waypointCount = _rng.Next(4, 10);
        var totalDistance = Math.Round(request.JumpRangeLy * 4 * waypointCount * (0.8 + _rng.NextDouble() * 0.6), 1);
        var remaining = totalDistance;

        var hops = new List<RouteHop> { new(request.From.Trim(), 0, false, 0, totalDistance) };
        for (var i = 1; i <= waypointCount; i++)
        {
            var last = i == waypointCount;
            var isNeutron = !last && _rng.Next(100) < 70;                 // most highway hops end on a neutron
            var leg = last ? remaining : Math.Round(remaining / (waypointCount - i + 1) * (0.7 + _rng.NextDouble() * 0.6), 1);
            leg = Math.Min(leg, remaining);
            remaining = Math.Round(Math.Max(0, remaining - leg), 1);

            // A neutron boost covers ~4× range, so it condenses several plain jumps into one waypoint.
            var jumps = isNeutron ? 1 : Math.Max(1, (int)Math.Ceiling(leg / request.JumpRangeLy));
            var system = last ? request.To.Trim() : SamplePools.Pick(_rng, SamplePools.Systems) + $" {(char)('A' + _rng.Next(26))}{_rng.Next(1, 99)}";
            hops.Add(new RouteHop(system, jumps, isNeutron, leg, remaining));
        }

        return Task.FromResult<RoutePlan?>(new RoutePlan(request.From.Trim(), request.To.Trim(), hops));
    }
}

/// <summary>
/// An offline <see cref="ITradeSearch"/> for developer mode: it fabricates plausible station quotes
/// near the reference system for whatever commodity is asked about, so the trade-search card is fully
/// exercisable without the game running or the Spansh API.
/// </summary>
public sealed class SampleTradeSearch : ITradeSearch
{
    private readonly Random _rng;

    public string SourceName => "Spansh (dev)";

    public SampleTradeSearch(Random rng) => _rng = rng;

    public Task<IReadOnlyList<TradeStationQuote>> SearchAsync(TradeQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Commodity) || string.IsNullOrWhiteSpace(query.ReferenceSystem))
            return Task.FromResult<IReadOnlyList<TradeStationQuote>>(Array.Empty<TradeStationQuote>());

        var basePrice = _rng.Next(2_000, 400_000);
        var quotes = new List<TradeStationQuote>();
        var count = Math.Min(query.MaxResults, _rng.Next(4, 9));
        var distance = 0.0;

        for (var i = 0; i < count; i++)
        {
            distance = i == 0 ? Math.Round(_rng.NextDouble() * 8, 2) : Math.Round(distance + _rng.NextDouble() * 40, 2);
            // Nearer stations quote a touch worse; the best price tends to be a hop or two out — so the
            // "vs nearest" trade-off the card exists to surface actually shows up.
            var price = (int)Math.Round(basePrice * (0.85 + _rng.NextDouble() * 0.4));
            var quantity = _rng.Next(200, 30_000);
            quotes.Add(new TradeStationQuote(
                SamplePools.Pick(_rng, SamplePools.Systems),
                SamplePools.Pick(_rng, SamplePools.Stations),
                distance, price, quantity,
                DateTimeOffset.UtcNow.AddHours(-_rng.Next(1, 72))));
        }

        return Task.FromResult<IReadOnlyList<TradeStationQuote>>(quotes);
    }
}
