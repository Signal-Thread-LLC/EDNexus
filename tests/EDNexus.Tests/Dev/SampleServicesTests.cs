using System;
using System.Linq;
using System.Threading.Tasks;
using EDNexus.Core.Dev;
using EDNexus.Core.Routes;
using EDNexus.Core.Trade;
using Xunit;

namespace EDNexus.Tests.Dev;

public class SampleServicesTests
{
    [Fact]
    public async Task Sample_route_starts_at_from_ends_at_to_and_has_boosts()
    {
        var plotter = new SampleRoutePlotter(new Random(1));

        var plan = await plotter.PlotAsync(new RoutePlotRequest("Sol", "Colonia", 48));

        Assert.NotNull(plan);
        Assert.Equal("Sol", plan!.Hops[0].System);
        Assert.Equal("Colonia", plan.Hops[^1].System);
        Assert.True(plan.WaypointCount >= 1);
        Assert.Equal(0, plan.Hops[0].Jumps);                 // origin costs no jump
        Assert.True(plan.Hops[^1].DistanceRemainingLy == 0); // arrives at the destination
    }

    [Fact]
    public async Task Sample_route_rejects_an_empty_or_rangeless_request()
    {
        var plotter = new SampleRoutePlotter(new Random(1));

        Assert.Null(await plotter.PlotAsync(new RoutePlotRequest("", "Colonia", 48)));
        Assert.Null(await plotter.PlotAsync(new RoutePlotRequest("Sol", "Colonia", 0)));
    }

    [Fact]
    public async Task Sample_trade_returns_priced_quotes_sorted_outward()
    {
        var search = new SampleTradeSearch(new Random(2));

        var quotes = await search.SearchAsync(new TradeQuery("Painite", "Sol"));

        Assert.NotEmpty(quotes);
        Assert.All(quotes, q => Assert.True(q.Price > 0));
        // Distances are generated non-decreasing, nearest first — like the real Spansh ordering.
        var distances = quotes.Select(q => q.DistanceLy).ToList();
        Assert.Equal(distances.OrderBy(d => d), distances);
    }

    [Fact]
    public async Task Sample_trade_needs_a_commodity_and_reference_system()
    {
        var search = new SampleTradeSearch(new Random(2));

        Assert.Empty(await search.SearchAsync(new TradeQuery("", "Sol")));
        Assert.Empty(await search.SearchAsync(new TradeQuery("Painite", "")));
    }
}
