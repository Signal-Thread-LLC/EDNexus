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
}
