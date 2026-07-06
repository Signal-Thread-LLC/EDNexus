using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EDNexus.Core.Trade;
using Xunit;

namespace EDNexus.Tests;

public class SpanshTradeSearchTests
{
    /// <summary>Captures the outgoing request and returns a canned response, so no network is touched.</summary>
    private sealed class StubMessageHandler : HttpMessageHandler
    {
        private readonly string _body;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public int Calls { get; private set; }

        public StubMessageHandler(string body) => _body = body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body) };
        }
    }

    // A stations/search response shaped per Spansh's documented schema: two stations, each with a
    // small market that includes Painite (the commodity under test) and some noise.
    private const string SellResponse = """
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

    private static SpanshTradeSearch NewSearch(StubMessageHandler handler, IResponseCache? cache = null)
        => new(new HttpClient(handler), cache, baseUrl: "https://example.test/api");

    [Fact]
    public async Task Search_maps_stations_to_quotes_preserving_nearest_first_order()
    {
        var handler = new StubMessageHandler(SellResponse);
        var search = NewSearch(handler);

        var quotes = await search.SearchAsync(new TradeQuery("painite", "Diaguandri"));

        Assert.Equal(2, quotes.Count);
        Assert.Equal("Ray Gateway", quotes[0].Station);    // nearest first, as returned
        Assert.Equal(0.0, quotes[0].DistanceLy);
        Assert.Equal(410000, quotes[0].Price);             // Painite sell price, not Gold's
        Assert.Equal(1500, quotes[0].Quantity);            // demand when selling

        Assert.Equal("Golden Gate", quotes[1].Station);
        Assert.Equal(31.5, quotes[1].DistanceLy);
        Assert.Equal(455000, quotes[1].Price);
        Assert.Equal(new DateTimeOffset(2026, 7, 6, 9, 30, 0, TimeSpan.Zero), quotes[1].MarketUpdated);
    }

    [Fact]
    public async Task Search_posts_to_the_stations_endpoint_with_the_reference_system()
    {
        var handler = new StubMessageHandler(SellResponse);
        var search = NewSearch(handler);

        await search.SearchAsync(new TradeQuery("painite", "Sol", TradeMode.Sell, MaxResults: 5));

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith("/stations/search", handler.LastRequest.RequestUri!.AbsoluteUri);
        Assert.Contains("\"reference_system\":\"Sol\"", handler.LastRequestBody);
        Assert.Contains("Painite".ToLowerInvariant(), handler.LastRequestBody!.ToLowerInvariant());
    }

    [Fact]
    public async Task Buy_mode_reads_buy_price_and_supply()
    {
        // Same station, queried on the buy side: Gold is supplied here.
        var handler = new StubMessageHandler(SellResponse);
        var search = NewSearch(handler);

        var quotes = await search.SearchAsync(new TradeQuery("gold", "Diaguandri", TradeMode.Buy));

        var gold = Assert.Single(quotes);
        Assert.Equal("Ray Gateway", gold.Station);
        Assert.Equal(47500, gold.Price);       // buy_price
        Assert.Equal(1200, gold.Quantity);     // supply
    }

    [Fact]
    public async Task Commodity_the_station_does_not_list_yields_no_quote()
    {
        var handler = new StubMessageHandler(SellResponse);
        var search = NewSearch(handler);

        var quotes = await search.SearchAsync(new TradeQuery("tritium", "Diaguandri"));

        Assert.Empty(quotes);
    }

    [Fact]
    public async Task A_reshaped_response_degrades_to_empty_rather_than_throwing()
    {
        var handler = new StubMessageHandler("""{ "unexpected": true }""");
        var search = NewSearch(handler);

        var quotes = await search.SearchAsync(new TradeQuery("painite", "Sol"));

        Assert.Empty(quotes);
    }

    [Fact]
    public async Task Cached_response_is_reused_and_the_network_is_hit_only_once()
    {
        var handler = new StubMessageHandler(SellResponse);
        var cache = new InMemoryCache();
        var search = NewSearch(handler, cache);

        await search.SearchAsync(new TradeQuery("painite", "Diaguandri"));
        await search.SearchAsync(new TradeQuery("painite", "Diaguandri"));

        Assert.Equal(1, handler.Calls);   // second call served from cache
    }

    private sealed class InMemoryCache : IResponseCache
    {
        private readonly Dictionary<string, string> _store = new();
        public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public void Put(string key, string body) => _store[key] = body;
    }
}
