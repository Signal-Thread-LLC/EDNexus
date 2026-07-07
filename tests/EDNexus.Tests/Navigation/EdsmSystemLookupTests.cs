using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using EDNexus.Core.Navigation;
using EDNexus.Core.Trade;
using EDNexus.Tests.Reporting;   // reuse the shared RecordingHandler test double
using EliteDangerous.Edsm;
using Xunit;

namespace EDNexus.Tests.Navigation;

public class EdsmSystemLookupTests
{
    private static readonly EdsmClientOptions Options = new()
    {
        SoftwareName = "EDNexus.Tests",
        SoftwareVersion = "1.0.0",
    };

    private static EdsmSystemLookup NewLookup(RecordingHandler handler, IResponseCache? cache = null)
        => new(new EdsmClient(Options, new HttpClient(handler)), cache);

    [Fact]
    public async Task Maps_a_system_and_caches_its_position()
    {
        var handler = new RecordingHandler(body: """{ "name": "Sol", "coords": { "x": 1, "y": 2, "z": 3 } }""");
        var lookup = NewLookup(handler, new InMemoryCache());

        var first = await lookup.GetSystemAsync("Sol");
        var second = await lookup.GetSystemAsync("Sol");

        Assert.Equal("Sol", first!.Name);
        Assert.Equal(new SystemCoords(1, 2, 3), first.Coords);
        Assert.Equal(1, handler.CallCount);            // second served from cache
        Assert.Equal(first.Coords, second!.Coords);    // round-trips through the cache intact
    }

    [Fact]
    public async Task Computes_straight_line_distance_between_two_systems()
    {
        // Sol at origin, target at (3,4,0) → distance 5.
        var handler = new RecordingHandler(n => (System.Net.HttpStatusCode.OK, n == 1
            ? """{ "name": "Sol", "coords": { "x": 0, "y": 0, "z": 0 } }"""
            : """{ "name": "Target", "coords": { "x": 3, "y": 4, "z": 0 } }"""));
        var lookup = NewLookup(handler);

        var distance = await lookup.DistanceBetweenAsync("Sol", "Target");

        Assert.NotNull(distance);
        Assert.Equal(5.0, distance!.Value, precision: 3);
    }

    [Fact]
    public async Task Distance_is_null_when_a_system_is_unknown()
    {
        var handler = new RecordingHandler(n => (System.Net.HttpStatusCode.OK, n == 1
            ? """{ "name": "Sol", "coords": { "x": 0, "y": 0, "z": 0 } }"""
            : "[]"));   // second system unknown
        var lookup = NewLookup(handler);

        Assert.Null(await lookup.DistanceBetweenAsync("Sol", "Nowhere"));
    }

    private sealed class InMemoryCache : IResponseCache
    {
        private readonly Dictionary<string, string> _store = new();
        public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public void Put(string key, string body) => _store[key] = body;
    }
}
