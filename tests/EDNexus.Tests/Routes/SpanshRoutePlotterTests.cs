using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EDNexus.Core.Routes;
using EDNexus.Core.Trade;
using EDNexus.Tests.Reporting;   // reuse the shared RecordingHandler test double
using EliteDangerous.Spansh;
using Xunit;

namespace EDNexus.Tests.Routes;

public class SpanshRoutePlotterTests
{
    private static readonly SpanshClientOptions Options = new()
    {
        SoftwareName = "EDNexus.Tests",
        SoftwareVersion = "1.0.0",
        RoutePollInterval = TimeSpan.Zero,
    };

    private const string Submit = """{ "job": "j1", "status": "queued" }""";
    private const string Ready = """
    { "status": "ok", "result": { "system_jumps": [
        { "system": "Sol",     "jumps": 0, "distance_jumped": 0,   "distance_left": 400, "neutron_star": false },
        { "system": "Jackson", "jumps": 4, "distance_jumped": 250, "distance_left": 150, "neutron_star": true  },
        { "system": "Colonia", "jumps": 2, "distance_jumped": 150, "distance_left": 0,   "neutron_star": false } ] } }
    """;

    private static SpanshRoutePlotter NewPlotter(RecordingHandler handler, IResponseCache? cache = null)
        => new(new SpanshClient(Options, new HttpClient(handler)), cache);

    [Fact]
    public async Task Maps_waypoints_and_derives_totals()
    {
        var plotter = NewPlotter(new RecordingHandler(n => (HttpStatusCode.OK, n == 1 ? Submit : Ready)));

        var plan = await plotter.PlotAsync(new RoutePlotRequest("Sol", "Colonia", 48));

        Assert.NotNull(plan);
        Assert.Equal(3, plan!.Hops.Count);
        Assert.Equal(2, plan.WaypointCount);           // hops excluding origin
        Assert.Equal(6, plan.TotalJumps);              // 0 + 4 + 2
        Assert.Equal(1, plan.NeutronCount);
        Assert.True(plan.Hops[1].IsNeutron);
    }

    [Fact]
    public async Task Empty_or_bad_request_is_null_without_touching_the_network()
    {
        var handler = new RecordingHandler(body: Ready);
        var plotter = NewPlotter(handler);

        Assert.Null(await plotter.PlotAsync(new RoutePlotRequest("", "Colonia", 48)));
        Assert.Null(await plotter.PlotAsync(new RoutePlotRequest("Sol", "Colonia", 0)));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task A_failed_plot_is_null_and_not_cached_so_it_retries()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, "down");
        var cache = new InMemoryCache();
        var plotter = NewPlotter(handler, cache);

        Assert.Null(await plotter.PlotAsync(new RoutePlotRequest("Sol", "Colonia", 48)));
        Assert.Empty(cache.Keys);   // nothing cached — a later attempt will hit the network again
    }

    [Fact]
    public async Task Cached_route_is_reused_and_the_job_runs_only_once()
    {
        var handler = new RecordingHandler(n => (HttpStatusCode.OK, n == 1 ? Submit : Ready));
        var plotter = NewPlotter(handler, new InMemoryCache());
        var request = new RoutePlotRequest("Sol", "Colonia", 48);

        var first = await plotter.PlotAsync(request);
        var callsAfterFirst = handler.CallCount;
        var second = await plotter.PlotAsync(request);

        Assert.Equal(callsAfterFirst, handler.CallCount);   // second served entirely from cache
        Assert.Equal(first!.TotalJumps, second!.TotalJumps);
        Assert.Equal("Colonia", second.Hops[^1].System);
    }

    private sealed class InMemoryCache : IResponseCache
    {
        private readonly Dictionary<string, string> _store = new();
        public IReadOnlyCollection<string> Keys => _store.Keys;
        public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public void Put(string key, string body) => _store[key] = body;
    }
}
