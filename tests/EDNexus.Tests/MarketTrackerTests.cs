using System.Collections.Generic;
using System.Linq;
using EDNexus.Core.Journal;
using EDNexus.Core.Market;
using EDNexus.Core.State;
using Xunit;

namespace EDNexus.Tests;

public class MarketTrackerTests
{
    private static (JournalEventBus bus, CommanderState state, MarketTracker tracker) NewTracker()
    {
        var bus = new JournalEventBus();
        var state = new CommanderState();
        var tracker = new MarketTracker(bus, state);
        return (bus, state, tracker);
    }

    private static void Publish(JournalEventBus bus, string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var entry), "sample JSON failed to parse");
        bus.Publish(entry);
    }

    // A station that buys diamonds (demand) and painite, and sells gold (supply).
    private const string Market = """
    { "timestamp":"2026-06-03T00:47:02Z", "event":"Market", "MarketID":3228024320,
      "StationName":"Ray Gateway", "StarSystem":"Diaguandri",
      "Items":[
        { "Name":"$lowtemperaturediamond_name;", "Name_Localised":"Low Temperature Diamonds", "Category_Localised":"Minerals",
          "BuyPrice":0, "SellPrice":900000, "MeanPrice":800000, "Stock":0, "Demand":150, "Rare":false },
        { "Name":"$painite_name;", "Name_Localised":"Painite", "Category_Localised":"Minerals",
          "BuyPrice":0, "SellPrice":40000, "MeanPrice":45000, "Stock":0, "Demand":900, "Rare":false },
        { "Name":"$gold_name;", "Name_Localised":"Gold", "Category_Localised":"Metals",
          "BuyPrice":47355, "SellPrice":0, "MeanPrice":47009, "Stock":1200, "Demand":0, "Rare":false }
      ] }
    """;

    [Fact]
    public void Market_event_populates_the_current_snapshot()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Market);

        var snap = tracker.Current;
        Assert.NotNull(snap);
        Assert.Equal(3228024320, snap!.MarketId);
        Assert.Equal("Ray Gateway", snap.StationName);
        Assert.Equal("Diaguandri", snap.StarSystem);
        Assert.Equal(3, snap.Commodities.Count);

        var gold = snap.Find("gold");
        Assert.NotNull(gold);
        Assert.True(gold!.Buyable);          // station supplies gold
        Assert.False(gold.Sellable);         // no demand for gold here
        Assert.Equal(1200, gold.Stock);
    }

    [Fact]
    public void Sellable_orders_by_best_sell_price_first()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Market);

        var sellable = tracker.Current!.Sellable.ToList();
        Assert.Equal(2, sellable.Count);                        // diamonds + painite; gold excluded
        Assert.Equal("Low Temperature Diamonds", sellable[0].Name);
        Assert.DoesNotContain(sellable, c => c.Name == "Gold");
    }

    [Fact]
    public void Valuate_hold_prices_cargo_the_station_buys_and_totals_it()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Market);

        // Cargo as StateTracker actually keys it — the localised label, whose canonical form differs
        // from the market symbol ("Low Temperature Diamonds" → ...diamonds vs $lowtemperaturediamond).
        var cargo = new Dictionary<string, int>
        {
            ["Low Temperature Diamonds"] = 10,
            ["Painite"] = 4,
            ["gold"] = 100,   // station has no demand → should be omitted
        };

        var snap = tracker.Current!;
        var valuation = snap.ValuateHold(cargo);

        Assert.Equal(2, valuation.Count);                       // gold dropped
        Assert.Equal("Low Temperature Diamonds", valuation[0].Name); // biggest total first
        Assert.Equal(10 * 900000L, valuation[0].Total);
        Assert.Equal(100000, valuation[0].VsMean);              // 900k sell - 800k mean

        var painite = Assert.Single(valuation, i => i.Name == "Painite");
        Assert.Equal(-5000, painite.VsMean);                    // below the galactic mean

        Assert.Equal(10 * 900000L + 4 * 40000L, snap.HoldValue(cargo));
    }

    [Fact]
    public void MarketSell_reduces_demand_live_on_the_current_board()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Market);

        Publish(bus, """
        { "timestamp":"2026-06-03T01:00:00Z", "event":"MarketSell", "MarketID":3228024320,
          "Type":"painite", "Count":300, "SellPrice":40000, "TotalSale":12000000, "AvgPricePaid":0 }
        """);

        var painite = tracker.Current!.Find("painite");
        Assert.Equal(600, painite!.Demand);   // 900 - 300
    }

    [Fact]
    public void MarketBuy_reduces_stock_live_on_the_current_board()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Market);

        Publish(bus, """
        { "timestamp":"2026-06-03T01:00:00Z", "event":"MarketBuy", "MarketID":3228024320,
          "Type":"gold", "Count":200, "BuyPrice":47355, "TotalCost":9471000 }
        """);

        var gold = tracker.Current!.Find("gold");
        Assert.Equal(1000, gold!.Stock);      // 1200 - 200
    }

    [Fact]
    public void Transaction_for_a_different_market_is_ignored()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Market);

        Publish(bus, """
        { "timestamp":"2026-06-03T01:00:00Z", "event":"MarketSell", "MarketID":999,
          "Type":"painite", "Count":300, "SellPrice":40000, "TotalSale":12000000 }
        """);

        Assert.Equal(900, tracker.Current!.Find("painite")!.Demand);   // unchanged
    }

    [Fact]
    public void Market_event_without_items_does_not_clobber_the_snapshot()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Market);

        // A header-only Market event (defers the board to Market.json) must not wipe the good snapshot.
        Publish(bus, """
        { "timestamp":"2026-06-03T02:00:00Z", "event":"Market", "MarketID":3228024320,
          "StationName":"Ray Gateway", "StarSystem":"Diaguandri" }
        """);

        Assert.Equal(3, tracker.Current!.Commodities.Count);
    }

    [Fact]
    public void Later_market_snapshot_replaces_the_prior_one()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Market);

        Publish(bus, """
        { "timestamp":"2026-06-03T03:00:00Z", "event":"Market", "MarketID":3999999999,
          "StationName":"Jameson Memorial", "StarSystem":"Shinrarta Dezhra",
          "Items":[
            { "Name":"$tritium_name;", "Name_Localised":"Tritium", "Category_Localised":"Chemicals",
              "BuyPrice":0, "SellPrice":50000, "MeanPrice":42000, "Stock":0, "Demand":500, "Rare":false }
          ] }
        """);

        var snap = tracker.Current!;
        Assert.Equal(3999999999, snap.MarketId);
        Assert.Equal("Jameson Memorial", snap.StationName);
        Assert.Single(snap.Commodities);
    }
}
