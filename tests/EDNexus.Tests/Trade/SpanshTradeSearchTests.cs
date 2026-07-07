using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using EDNexus.Core.Trade;
using EDNexus.Tests.Reporting;   // reuse the shared RecordingHandler test double
using EliteDangerous.Spansh;
using Xunit;

namespace EDNexus.Tests.Trade;

public class SpanshTradeSearchTests
{
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

    private static readonly SpanshClientOptions Options = new()
    {
        SoftwareName = "EDNexus.Tests",
        SoftwareVersion = "1.0.0",
    };

    private static SpanshTradeSearch NewSearch(RecordingHandler handler, IResponseCache? cache = null)
        => new(new SpanshClient(Options, new HttpClient(handler)), cache);

    [Fact]
    public async Task Maps_stations_to_quotes_nearest_first_with_sell_price_and_demand()
    {
        var search = NewSearch(new RecordingHandler(body: Response));

        // Query with a case/spacing-variant name to prove canonical matching against Spansh's label.
        var quotes = await search.SearchAsync(new TradeQuery("painite", "Diaguandri"));

        Assert.Equal(2, quotes.Count);
        Assert.Equal("Ray Gateway", quotes[0].Station);    // nearest first, as Spansh ordered
        Assert.Equal(0.0, quotes[0].DistanceLy);
        Assert.Equal(410000, quotes[0].Price);             // Painite sell price, not Gold's
        Assert.Equal(1500, quotes[0].Quantity);            // demand when selling
        Assert.Equal("Golden Gate", quotes[1].Station);
        Assert.Equal(455000, quotes[1].Price);
    }

    [Fact]
    public async Task Buy_mode_reads_buy_price_and_supply()
    {
        var search = NewSearch(new RecordingHandler(body: Response));

        var quotes = await search.SearchAsync(new TradeQuery("gold", "Diaguandri", TradeMode.Buy));

        var gold = Assert.Single(quotes);
        Assert.Equal("Ray Gateway", gold.Station);
        Assert.Equal(47500, gold.Price);       // buy_price
        Assert.Equal(1200, gold.Quantity);     // supply
    }

    [Fact]
    public async Task Commodity_no_station_lists_yields_no_quotes()
    {
        var search = NewSearch(new RecordingHandler(body: Response));

        var quotes = await search.SearchAsync(new TradeQuery("tritium", "Diaguandri"));

        Assert.Empty(quotes);
    }

    [Fact]
    public async Task Cached_answer_is_reused_and_the_network_is_hit_only_once()
    {
        var handler = new RecordingHandler(body: Response);
        var search = NewSearch(handler, new InMemoryCache());

        await search.SearchAsync(new TradeQuery("painite", "Diaguandri"));
        var second = await search.SearchAsync(new TradeQuery("painite", "Diaguandri"));

        Assert.Equal(1, handler.CallCount);        // second served from cache
        Assert.Equal("Ray Gateway", second[0].Station);   // round-trips through the cache intact
    }

    private sealed class InMemoryCache : IResponseCache
    {
        private readonly Dictionary<string, string> _store = new();
        public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public void Put(string key, string body) => _store[key] = body;
    }
}
