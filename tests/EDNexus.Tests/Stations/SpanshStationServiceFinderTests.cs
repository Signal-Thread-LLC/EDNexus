using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using EDNexus.Core.Stations;
using EDNexus.Core.Trade;
using EDNexus.Tests.Reporting;   // reuse the shared RecordingHandler test double
using EliteDangerous.Spansh;
using Xunit;

namespace EDNexus.Tests.Stations;

public class SpanshStationServiceFinderTests
{
    // Shaped after a real /stations/search response, trimmed to the fields the finder reads.
    private const string Response = """
    { "count": 2, "results": [
        { "system_name":"Sirius", "name":"Patterson Enterprise", "distance":8.58,
          "distance_to_arrival":963.04, "type":"Coriolis Starport", "is_planetary":false,
          "has_large_pad":true, "material_trader":"Manufactured", "updated_at":"2026-08-17T23:28:41Z" },
        { "system_name":"61 Cygni", "name":"Broglie Terminal", "distance":11.4,
          "distance_to_arrival":18450.0, "type":"Outpost", "is_planetary":false,
          "has_large_pad":false, "material_trader":"Raw", "updated_at":"2026-08-16T10:00:00Z" }
      ] }
    """;

    private static readonly SpanshClientOptions Options = new()
    {
        SoftwareName = "EDNexus.Tests",
        SoftwareVersion = "1.0.0",
    };

    private static SpanshStationServiceFinder NewFinder(RecordingHandler handler, IResponseCache? cache = null)
        => new(new SpanshClient(Options, new HttpClient(handler)), cache);

    private static StationServiceQuery Query(
        string serviceId = "material-trader", string? flavour = null, bool largePad = false) =>
        new(StationServices.ById(serviceId)!, "Sol", flavour, largePad);

    [Fact]
    public async Task Maps_stations_nearest_first_with_the_detail_the_card_shows()
    {
        var finder = NewFinder(new RecordingHandler(body: Response));

        var results = await finder.FindAsync(Query());

        Assert.Equal(2, results.Count);
        var first = results[0];
        Assert.Equal("Sirius", first.System);
        Assert.Equal("Patterson Enterprise", first.Station);
        Assert.Equal(8.58, first.DistanceLy, 2);
        Assert.Equal("Coriolis Starport", first.StationType);
        Assert.True(first.HasLargePad);
        Assert.Equal("Manufactured", first.Flavour);
        // Source order is nearest-first and must be preserved, not re-sorted.
        Assert.Equal("61 Cygni", results[1].System);
    }

    [Fact]
    public async Task A_station_deep_in_the_system_is_flagged_as_far_from_entry()
    {
        var finder = NewFinder(new RecordingHandler(body: Response));

        var results = await finder.FindAsync(Query());

        Assert.False(results[0].IsFarFromEntry);   // 963 Ls - incidental
        Assert.True(results[1].IsFarFromEntry);    // 18,450 Ls - a trip in its own right
    }

    [Fact]
    public async Task The_service_name_and_reference_system_are_sent_to_the_source()
    {
        var handler = new RecordingHandler(body: Response);
        var finder = NewFinder(handler);

        await finder.FindAsync(Query("vista-genomics"));

        var sent = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Equal("Sol", sent.GetProperty("reference_system").GetString());
        Assert.Equal("Vista Genomics",
            sent.GetProperty("filters").GetProperty("services")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_flavour_becomes_the_sources_own_subtype_filter()
    {
        var handler = new RecordingHandler(body: Response);
        var finder = NewFinder(handler);

        await finder.FindAsync(Query(flavour: "Raw"));

        var filters = JsonDocument.Parse(handler.Bodies[0]).RootElement.GetProperty("filters");
        Assert.Equal("Raw", filters.GetProperty("material_trader").GetProperty("value")[0].GetString());
    }

    [Fact]
    public async Task A_flavour_on_a_service_that_has_none_is_ignored_rather_than_sent()
    {
        var handler = new RecordingHandler(body: Response);
        var finder = NewFinder(handler);

        // A stale setting could carry "Raw" over to a service with no flavours; sending it would
        // filter every result out.
        await finder.FindAsync(Query("shipyard", flavour: "Raw"));

        var filters = JsonDocument.Parse(handler.Bodies[0]).RootElement.GetProperty("filters");
        Assert.False(filters.TryGetProperty("material_trader", out _));
    }

    [Fact]
    public async Task The_large_pad_filter_is_only_sent_when_asked_for()
    {
        var handler = new RecordingHandler(body: Response);
        var finder = NewFinder(handler);

        await finder.FindAsync(Query());
        Assert.False(JsonDocument.Parse(handler.Bodies[0]).RootElement
            .GetProperty("filters").TryGetProperty("has_large_pad", out _));

        await finder.FindAsync(Query("outfitting", largePad: true));
        Assert.True(JsonDocument.Parse(handler.Bodies[1]).RootElement
            .GetProperty("filters").GetProperty("has_large_pad").GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task A_transport_failure_yields_no_results_and_is_not_cached()
    {
        var cache = new MemoryCache();
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, "nope");
        var finder = NewFinder(handler, cache);

        var results = await finder.FindAsync(Query());

        Assert.Empty(results);
        // Caching a failure would serve "nothing here" for the whole TTL; the next try must retry.
        Assert.Empty(cache.Entries);
    }

    [Fact]
    public async Task A_repeat_lookup_is_served_from_cache_without_a_second_request()
    {
        var cache = new MemoryCache();
        var handler = new RecordingHandler(body: Response);
        var finder = NewFinder(handler, cache);

        var first = await finder.FindAsync(Query());
        var second = await finder.FindAsync(Query());

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(first.Select(r => r.Station), second.Select(r => r.Station));
    }

    [Fact]
    public async Task Different_flavours_and_pad_filters_are_cached_separately()
    {
        var cache = new MemoryCache();
        var handler = new RecordingHandler(body: Response);
        var finder = NewFinder(handler, cache);

        await finder.FindAsync(Query(flavour: "Raw"));
        await finder.FindAsync(Query(flavour: "Encoded"));
        await finder.FindAsync(Query(flavour: "Raw", largePad: true));

        // Three genuinely different questions - none may answer another.
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task An_empty_reference_system_asks_nothing_of_the_source()
    {
        var handler = new RecordingHandler(body: Response);
        var finder = NewFinder(handler);

        var results = await finder.FindAsync(
            new StationServiceQuery(StationServices.Default, ReferenceSystem: ""));

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task A_malformed_response_degrades_to_no_results_rather_than_throwing()
    {
        var finder = NewFinder(new RecordingHandler(body: "{ this is not json"));

        var results = await finder.FindAsync(Query());

        Assert.Empty(results);
    }

    [Fact]
    public void Every_catalogued_service_declares_its_flavours_consistently()
    {
        foreach (var service in StationServices.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(service.SourceName));
            // A subtype field and its flavours only make sense together.
            Assert.Equal(service.SubtypeField is not null, service.HasFlavours);
        }

        Assert.NotNull(StationServices.ById("material-trader"));
        Assert.Null(StationServices.ById("no-such-service"));
    }

    /// <summary>Minimal in-memory cache so the tests can assert on what was and wasn't stored.</summary>
    private sealed class MemoryCache : IResponseCache
    {
        public Dictionary<string, string> Entries { get; } = new();
        public string? Get(string key) => Entries.TryGetValue(key, out var v) ? v : null;
        public void Put(string key, string body) => Entries[key] = body;
    }
}
