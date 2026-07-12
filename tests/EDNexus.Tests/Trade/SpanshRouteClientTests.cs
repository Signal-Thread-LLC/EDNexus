using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EDNexus.Tests.Reporting;   // reuse the shared RecordingHandler test double
using EliteDangerous.Spansh;
using Xunit;

namespace EDNexus.Tests.Trade;

public class SpanshRouteClientTests
{
    private static SpanshClientOptions Options(int attempts = 90) => new()
    {
        SoftwareName = "EDNexus.Tests",
        SoftwareVersion = "1.0.0",
        RoutePollInterval = TimeSpan.Zero,   // don't actually sleep between polls under test
        RoutePollAttempts = attempts,
    };

    private const string Submit = """{ "job": "job-123", "status": "queued" }""";
    private const string Queued = """{ "status": "queued" }""";
    private const string Ready = """
    { "status": "ok", "result": { "system_jumps": [
        { "system": "Sol",    "jumps": 0, "distance_jumped": 0,   "distance_left": 500, "neutron_star": false },
        { "system": "Sirius", "jumps": 3, "distance_jumped": 200, "distance_left": 300, "neutron_star": true  },
        { "system": "Colonia","jumps": 2, "distance_jumped": 300, "distance_left": 0,   "neutron_star": false }
    ] } }
    """;

    private static SpanshRouteQuery Query => new() { From = "Sol", To = "Colonia", RangeLy = 48.5 };

    [Fact]
    public async Task Submits_then_polls_to_completion_and_parses_waypoints()
    {
        // Call 1 = submit (job id), call 2 = still queued, call 3 = the finished route.
        var handler = new RecordingHandler(n => (HttpStatusCode.OK, n switch { 1 => Submit, 2 => Queued, _ => Ready }));
        using var client = new SpanshClient(Options(), new HttpClient(handler));

        var result = await client.PlotRouteAsync(Query);

        Assert.True(result.IsOk);
        Assert.Equal(3, result.Waypoints.Count);
        Assert.Equal("Sol", result.Waypoints[0].System);
        Assert.Equal("Colonia", result.Waypoints[^1].System);

        var sirius = result.Waypoints[1];
        Assert.True(sirius.IsNeutron);
        Assert.Equal(3, sirius.Jumps);
        Assert.Equal(300, sirius.DistanceRemainingLy);
        Assert.Equal(3, handler.CallCount);   // one submit + two polls
    }

    [Fact]
    public async Task Submit_request_carries_endpoints_and_ship_range()
    {
        var handler = new RecordingHandler(n => (HttpStatusCode.OK, n == 1 ? Submit : Ready));
        using var client = new SpanshClient(Options(), new HttpClient(handler));

        await client.PlotRouteAsync(new SpanshRouteQuery { From = "Sol", To = "Colonia", RangeLy = 48.5, Efficiency = 60 });

        var submitUrl = handler.Uris[0]!.ToString();
        Assert.Contains("/route?", submitUrl);
        Assert.Contains("from=Sol", submitUrl);
        Assert.Contains("to=Colonia", submitUrl);
        Assert.Contains("range=48.5", submitUrl);
        Assert.Contains("efficiency=60", submitUrl);
        // The poll hits the results endpoint with the returned job id.
        Assert.Contains("/results/job-123", handler.Uris[1]!.ToString());
    }

    [Fact]
    public async Task Failed_submit_is_a_failure_not_an_exception()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, "nope");
        using var client = new SpanshClient(Options(), new HttpClient(handler));

        var result = await client.PlotRouteAsync(Query);

        Assert.False(result.IsOk);
        Assert.Contains("503", result.Error);
    }

    [Fact]
    public async Task Job_error_status_surfaces_as_failure()
    {
        var handler = new RecordingHandler(n => (HttpStatusCode.OK,
            n == 1 ? Submit : """{ "status": "error", "error": "no route found" }"""));
        using var client = new SpanshClient(Options(), new HttpClient(handler));

        var result = await client.PlotRouteAsync(Query);

        Assert.False(result.IsOk);
        Assert.Contains("no route found", result.Error);
    }

    [Fact]
    public async Task Gives_up_with_timeout_when_job_never_finishes()
    {
        // Submit, then "queued" forever — the attempt budget must bound the wait.
        var handler = new RecordingHandler(n => (HttpStatusCode.OK, n == 1 ? Submit : Queued));
        using var client = new SpanshClient(Options(attempts: 3), new HttpClient(handler));

        var result = await client.PlotRouteAsync(Query);

        Assert.False(result.IsOk);
        Assert.Contains("timed out", result.Error);
    }

    // --- Galaxy plotter (no neutron boost) ---

    private const string GalaxyReady = """
    { "status": "ok", "result": { "jumps": [
        { "name": "Sol",      "distance": 0,     "distance_to_destination": 374, "has_neutron": false, "is_scoopable": false, "fuel_used": 0,    "fuel_in_tank": 32, "must_refuel": false },
        { "name": "LHS 1541", "distance": 49.9,  "distance_to_destination": 324, "has_neutron": false, "is_scoopable": true,  "fuel_used": 4.96, "fuel_in_tank": 27, "must_refuel": true  },
        { "name": "HR 1183",  "distance": 324.1, "distance_to_destination": 0,   "has_neutron": false, "is_scoopable": true,  "fuel_used": 5,    "fuel_in_tank": 22, "must_refuel": false } ] } }
    """;

    private static SpanshGalaxyRouteQuery GalaxyQuery => new()
    {
        From = "Sol", To = "HR 1183",
        OptimalMass = 1050, BaseMass = 280, TankSize = 32, ReserveSize = 0.77,
        FuelMultiplier = 0.012, FuelPower = 2.45, MaxFuelPerJump = 5, RangeBoost = 10.5, Cargo = 0,
    };

    [Fact]
    public async Task Galaxy_route_submits_ship_physics_with_boost_off_and_parses_fuel()
    {
        var handler = new RecordingHandler(n => (HttpStatusCode.OK, n == 1 ? Submit : GalaxyReady));
        using var client = new SpanshClient(Options(), new HttpClient(handler));

        var result = await client.PlotGalaxyRouteAsync(GalaxyQuery);

        Assert.Contains("/generic/route", handler.Uris[0]!.ToString());
        var body = handler.Bodies[0];
        Assert.Contains("source=Sol", body);
        Assert.Contains("use_supercharge=0", body);         // the whole point: no neutron boosting
        Assert.Contains("optimal_mass=1050", body);
        Assert.Contains("max_fuel_per_jump=5", body);
        Assert.Contains("range_boost=10.5", body);

        Assert.True(result.IsOk);
        Assert.Equal(3, result.Waypoints.Count);
        Assert.Equal(0, result.Waypoints[0].Jumps);         // origin
        Assert.Equal(1, result.Waypoints[1].Jumps);         // every real hop is a single plain jump
        Assert.False(result.Waypoints[1].IsNeutron);
        Assert.Equal(4.96, result.Waypoints[1].FuelUsed);
        Assert.True(result.Waypoints[1].IsScoopable);
        Assert.True(result.Waypoints[1].MustRestock);       // must_refuel
    }

    // --- Fleet-carrier plotter ---

    private const string CarrierReady = """
    { "status": "ok", "result": { "jumps": [
        { "name": "Sol",     "distance": 0,   "distance_to_destination": 374, "fuel_used": 0,  "fuel_in_tank": 52, "has_icy_ring": false, "must_restock": 1, "restock_amount": 52 },
        { "name": "HR 1183", "distance": 374, "distance_to_destination": 0,   "fuel_used": 52, "fuel_in_tank": 0,  "has_icy_ring": true,  "must_restock": 0, "restock_amount": 0  } ] } }
    """;

    [Fact]
    public async Task Fleet_carrier_route_submits_capacity_and_parses_tritium()
    {
        var handler = new RecordingHandler(n => (HttpStatusCode.OK, n == 1 ? Submit : CarrierReady));
        using var client = new SpanshClient(Options(), new HttpClient(handler));

        var result = await client.PlotFleetCarrierRouteAsync(new SpanshFleetCarrierRouteQuery
        {
            From = "Sol", To = "HR 1183", CapacityUsed = 120, CalculateStartingFuel = true,
        });

        Assert.Contains("/fleetcarrier/route", handler.Uris[0]!.ToString());
        var body = handler.Bodies[0];
        Assert.Contains("source=Sol", body);
        Assert.Contains("capacity_used=120", body);
        Assert.Contains("calculate_starting_fuel=1", body);

        Assert.True(result.IsOk);
        Assert.Equal(2, result.Waypoints.Count);
        var hop = result.Waypoints[1];
        Assert.Equal(52, hop.FuelUsed);                     // tritium burnt this 374 ly hop
        Assert.Equal(0, hop.FuelInTank);
        Assert.True(hop.HasIcyRing);
        Assert.False(result.Waypoints[1].IsNeutron);
        Assert.True(result.Waypoints[0].MustRestock);       // must_restock: 1 on the origin
    }
}
