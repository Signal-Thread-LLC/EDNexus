using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EDNexus.Tests.Reporting;   // reuse the shared RecordingHandler test double
using EliteDangerous.Spansh;
using Xunit;

namespace EDNexus.Tests.Trade;

public class SpanshClientTests
{
    private static readonly SpanshClientOptions Options = new()
    {
        SoftwareName = "EDNexus.Tests",
        SoftwareVersion = "1.0.0",
    };

    // A stations/search reply shaped per Spansh's documented schema.
    private const string Response = """
    { "count": 2, "results": [
        { "system_name":"Diaguandri", "name":"Ray Gateway", "distance":0.0, "market_updated_at":"2026-07-05T12:00:00Z",
          "market":[
            { "commodity":"Gold", "sell_price":47000, "buy_price":47500, "demand":0, "supply":1200 },
            { "commodity":"Painite", "sell_price":410000, "buy_price":0, "demand":1500, "supply":0 }
          ] },
        { "system_name":"Alioth", "name":"Golden Gate", "distance":31.5, "market_updated_at":"2026-07-06T09:30:00Z",
          "market":[
            { "commodity":"Painite", "sell_price":455000, "buy_price":0, "demand":800, "supply":0 }
          ] }
      ] }
    """;

    private static SpanshClient NewClient(RecordingHandler handler) => new(Options, new HttpClient(handler));

    [Fact]
    public async Task Parses_stations_and_their_market_lines()
    {
        using var client = NewClient(new RecordingHandler(body: Response));

        var result = await client.SearchStationsAsync(new SpanshStationQuery
        {
            CommodityName = "Painite",
            ReferenceSystem = "Diaguandri",
        });

        Assert.True(result.IsOk);
        Assert.Equal(2, result.Stations.Count);

        var ray = result.Stations[0];
        Assert.Equal("Ray Gateway", ray.StationName);
        Assert.Equal("Diaguandri", ray.SystemName);
        Assert.Equal(0.0, ray.DistanceLy);
        Assert.Equal(new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero), ray.MarketUpdated);
        Assert.Equal(2, ray.Commodities.Count);

        var painite = Assert.Single(ray.Commodities, c => c.Name == "Painite");
        Assert.Equal(410000, painite.SellPrice);
        Assert.Equal(1500, painite.Demand);
    }

    [Fact]
    public async Task Request_carries_reference_system_commodity_and_side()
    {
        var handler = new RecordingHandler(body: Response);
        using var client = NewClient(handler);

        await client.SearchStationsAsync(new SpanshStationQuery
        {
            CommodityName = "Painite",
            ReferenceSystem = "Sol",
            WantsDemand = true,
        });

        var sent = handler.Bodies[0];
        Assert.Contains("\"reference_system\":\"Sol\"", sent);
        Assert.Contains("Painite", sent);
        Assert.Contains("demand", sent);     // selling → filter on the station's demand side
    }

    [Fact]
    public async Task Non_success_status_is_a_transport_error_not_an_exception()
    {
        using var client = NewClient(new RecordingHandler(HttpStatusCode.ServiceUnavailable, "nope"));

        var result = await client.SearchStationsAsync(new SpanshStationQuery
        {
            CommodityName = "Painite",
            ReferenceSystem = "Sol",
        });

        Assert.False(result.IsOk);
        Assert.Contains("503", result.Error);
        Assert.Empty(result.Stations);
    }

    [Fact]
    public async Task Reshaped_response_yields_ok_with_no_stations()
    {
        using var client = NewClient(new RecordingHandler(body: """{ "unexpected": true }"""));

        var result = await client.SearchStationsAsync(new SpanshStationQuery
        {
            CommodityName = "Painite",
            ReferenceSystem = "Sol",
        });

        Assert.True(result.IsOk);
        Assert.Empty(result.Stations);
    }
}
