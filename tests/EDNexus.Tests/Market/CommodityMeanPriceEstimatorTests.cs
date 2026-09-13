using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EDNexus.Core.Market;
using EDNexus.Core.Trade;
using Xunit;

namespace EDNexus.Tests.Market;

public class CommodityMeanPriceEstimatorTests
{
    [Fact]
    public async Task Resolve_averages_nonzero_quotes_from_the_trade_search()
    {
        var trade = new FakeTradeSearch(new List<TradeStationQuote>
        {
            new("Diaguandri", "Ray Gateway", 0, 100_000, 500, null),
            new("Alioth", "Golden Gate", 31.5, 120_000, 300, null),
            new("Sol", "Abraham Lincoln", 0, 0, 0, null),   // a zero/junk price never pulls the average down
        });
        var estimator = new CommodityMeanPriceEstimator(trade);

        var mean = await estimator.ResolveAsync("painite", "Painite");

        Assert.Equal(110_000, mean);
        Assert.Equal(110_000, estimator.TryGet("painite"));
    }

    [Fact]
    public async Task No_quotes_leaves_the_commodity_unresolved()
    {
        var trade = new FakeTradeSearch(new List<TradeStationQuote>());
        var estimator = new CommodityMeanPriceEstimator(trade);

        Assert.Null(await estimator.ResolveAsync("unobtainium", "Unobtainium"));
        Assert.Null(estimator.TryGet("unobtainium"));
    }

    [Fact]
    public async Task Once_resolved_the_trade_search_is_never_queried_again()
    {
        var trade = new FakeTradeSearch(new List<TradeStationQuote>
        {
            new("Diaguandri", "Ray Gateway", 0, 100_000, 500, null),
        });
        var estimator = new CommodityMeanPriceEstimator(trade);

        await estimator.ResolveAsync("painite", "Painite");
        await estimator.ResolveAsync("painite", "Painite");

        Assert.Equal(1, trade.CallCount);
    }

    [Fact]
    public async Task RequestEstimate_resolves_in_the_background_and_TryGet_picks_it_up()
    {
        var trade = new FakeTradeSearch(new List<TradeStationQuote>
        {
            new("Diaguandri", "Ray Gateway", 0, 250_000, 500, null),
        });
        var estimator = new CommodityMeanPriceEstimator(trade);

        estimator.RequestEstimate("lowtemperaturediamond", "Low Temperature Diamonds");

        // The fetch runs on a background task; wait for it deterministically via the fake's own signal
        // rather than a fixed sleep.
        await trade.Completed.Task;
        await Task.Delay(20); // let the estimator finish writing the cache after the awaited call returns

        Assert.Equal(250_000, estimator.TryGet("lowtemperaturediamond"));
    }

    private sealed class FakeTradeSearch : ITradeSearch
    {
        private readonly IReadOnlyList<TradeStationQuote> _quotes;
        public int CallCount { get; private set; }
        public TaskCompletionSource Completed { get; } = new();

        public FakeTradeSearch(IReadOnlyList<TradeStationQuote> quotes) => _quotes = quotes;

        public string SourceName => "Fake";

        public Task<IReadOnlyList<TradeStationQuote>> SearchAsync(TradeQuery query, CancellationToken ct = default)
        {
            CallCount++;
            Completed.TrySetResult();
            return Task.FromResult(_quotes);
        }
    }
}
