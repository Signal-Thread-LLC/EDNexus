using EDNexus.Core.Routes;
using EDNexus.Core.Trade;

namespace EDNexus.Core.Dev;

/// <summary>
/// An offline <see cref="IRoutePlotter"/> for developer mode: it fabricates a plausible route between
/// the requested systems for whichever <see cref="RouteMode"/> is asked — a neutron highway with a
/// scatter of boosts, a plain no-boost hop list, or a 500 ly tritium-fuelled fleet-carrier run — so the
/// route card is fully exercisable without the game running or the Spansh API.
/// </summary>
public sealed class SampleRoutePlotter : IRoutePlotter
{
    private readonly Random _rng;

    public string SourceName => "Spansh (dev)";

    public SampleRoutePlotter(Random rng) => _rng = rng;

    public Task<RoutePlan?> PlotAsync(RoutePlotRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.To))
            return Task.FromResult<RoutePlan?>(null);

        var plan = request.Mode switch
        {
            RouteMode.FleetCarrier => CarrierPlan(request),
            RouteMode.NoBoost => PlainPlan(request),
            _ => NeutronPlan(request),
        };
        return Task.FromResult(plan);
    }

    private RoutePlan? NeutronPlan(RoutePlotRequest request)
    {
        if (request.JumpRangeLy <= 0) return null;

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
            var system = last ? request.To.Trim() : SampleSystem();
            hops.Add(new RouteHop(system, jumps, isNeutron, leg, remaining));
        }

        return new RoutePlan(request.From.Trim(), request.To.Trim(), hops, RouteMode.NeutronHighway);
    }

    /// <summary>A plain no-boost run: one jump per hop at roughly the ship's range, with ship fuel burnt each jump.</summary>
    private RoutePlan? PlainPlan(RoutePlotRequest request)
    {
        var range = request.Ship?.JumpRangeAt((request.Ship.BaseMass) + request.Ship.TankSize) ?? request.JumpRangeLy;
        if (range <= 0) range = request.JumpRangeLy > 0 ? request.JumpRangeLy : 30;
        var maxFuel = request.Ship?.MaxFuelPerJump ?? 5;
        var tank = request.Ship?.TankSize ?? 32;

        var jumpCount = _rng.Next(6, 16);
        var remaining = Math.Round(range * jumpCount * (0.85 + _rng.NextDouble() * 0.2), 1);
        var fuel = tank;

        var hops = new List<RouteHop> { new(request.From.Trim(), 0, false, 0, remaining, FuelUsed: 0, FuelInTank: fuel, IsScoopable: true) };
        for (var i = 1; i <= jumpCount; i++)
        {
            var last = i == jumpCount;
            var leg = last ? remaining : Math.Round(Math.Min(remaining, range * (0.75 + _rng.NextDouble() * 0.25)), 1);
            remaining = Math.Round(Math.Max(0, remaining - leg), 1);

            var burn = Math.Round(Math.Min(maxFuel, leg / range * maxFuel), 2);
            var scoopable = _rng.Next(100) < 60;
            fuel = Math.Round(fuel - burn, 2);
            var refuel = fuel < maxFuel;
            if (refuel && scoopable) fuel = tank;                          // topped up at a scoopable star

            var system = last ? request.To.Trim() : SampleSystem();
            hops.Add(new RouteHop(system, 1, false, leg, remaining,
                FuelUsed: burn, FuelInTank: fuel, IsScoopable: scoopable, MustRestock: refuel && !scoopable));
        }

        return new RoutePlan(request.From.Trim(), request.To.Trim(), hops, RouteMode.NoBoost);
    }

    /// <summary>A fleet-carrier run: fixed 500 ly hops burning tritium, with the odd icy-ring restock point.</summary>
    private RoutePlan CarrierPlan(RoutePlotRequest request)
    {
        const double hopLy = 500;
        var jumpCount = _rng.Next(3, 9);
        var remaining = Math.Round(hopLy * jumpCount * (0.6 + _rng.NextDouble() * 0.4), 1);
        var fuel = 1000.0;                                                 // full tritium reserve

        var hops = new List<RouteHop> { new(request.From.Trim(), 0, false, 0, remaining, FuelUsed: 0, FuelInTank: fuel, HasIcyRing: true) };
        for (var i = 1; i <= jumpCount; i++)
        {
            var last = i == jumpCount;
            var leg = last ? remaining : Math.Round(Math.Min(remaining, hopLy), 1);
            remaining = Math.Round(Math.Max(0, remaining - leg), 1);

            var burn = Math.Round(80 + leg / hopLy * 40, 0);              // ~80–120 t tritium per 500 ly hop
            fuel = Math.Round(Math.Max(0, fuel - burn), 0);
            var icy = _rng.Next(100) < 50;
            var restock = fuel < 200;
            if (restock && icy) fuel = 1000;

            var system = last ? request.To.Trim() : SampleSystem();
            hops.Add(new RouteHop(system, 1, false, leg, remaining,
                FuelUsed: burn, FuelInTank: fuel, MustRestock: restock, RestockAmount: restock ? 1000 - fuel : null, HasIcyRing: icy));
        }

        return new RoutePlan(request.From.Trim(), request.To.Trim(), hops, RouteMode.FleetCarrier);
    }

    private string SampleSystem() => SamplePools.Pick(_rng, SamplePools.Systems) + $" {(char)('A' + _rng.Next(26))}{_rng.Next(1, 99)}";
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
