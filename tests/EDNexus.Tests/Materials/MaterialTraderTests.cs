using System.Collections.Generic;
using System.Linq;
using EDNexus.Core.Engineering;
using EDNexus.Core.Materials;
using Xunit;

namespace EDNexus.Tests.Materials;

public class MaterialTraderTests
{
    private static MaterialInfo Mat(string symbol) =>
        EngineeringCatalog.Default.Material(symbol) ?? throw new KeyNotFoundException(symbol);

    // Raw group 1 is the carbon column: carbon (G1) → vanadium (G2) → niobium (G3) → yttrium (G4).
    // Raw group 4 is the iron column: iron (G1) → zinc (G2) → tin (G3) → selenium (G4).

    [Fact]
    public void Climbing_a_grade_in_the_same_group_costs_six_for_one()
    {
        var quote = MaterialTrader.Quote(Mat("carbon"), Mat("vanadium"), wanted: 1, held: 300);
        Assert.True(quote.Possible);
        Assert.Equal(6, quote.Cost);
        Assert.True(quote.Affordable);
    }

    [Fact]
    public void Dropping_a_grade_in_the_same_group_pays_three_for_one()
    {
        // One niobium yields three vanadium.
        var quote = MaterialTrader.Quote(Mat("niobium"), Mat("vanadium"), wanted: 3, held: 10);
        Assert.Equal(1, quote.Cost);
    }

    [Fact]
    public void Crossing_to_another_group_at_the_same_grade_costs_six_for_one()
    {
        var quote = MaterialTrader.Quote(Mat("carbon"), Mat("iron"), wanted: 1, held: 300);
        Assert.Equal(6, quote.Cost);
    }

    [Fact]
    public void Crossing_a_group_and_dropping_a_grade_multiplies_the_two_rates()
    {
        // Six grade-3 buy one grade-3 in the other group, which drops to three grade-2:
        // two source units per unit received.
        var quote = MaterialTrader.Quote(Mat("niobium"), Mat("zinc"), wanted: 3, held: 50);
        Assert.Equal(6, quote.Cost);
        Assert.Equal(2.0, MaterialTrader.RatePerUnit(Mat("niobium"), Mat("zinc")));
    }

    [Fact]
    public void Climbing_two_grades_compounds_the_upgrade_rate()
    {
        Assert.Equal(36, MaterialTrader.Quote(Mat("carbon"), Mat("niobium"), wanted: 1, held: 300).Cost);
    }

    [Fact]
    public void Fractional_costs_round_up_because_traders_deal_in_whole_units()
    {
        // One niobium yields three vanadium, so a single vanadium still costs a whole niobium.
        Assert.Equal(1, MaterialTrader.Quote(Mat("niobium"), Mat("vanadium"), wanted: 1, held: 10).Cost);
        Assert.Equal(2, MaterialTrader.Quote(Mat("niobium"), Mat("vanadium"), wanted: 4, held: 10).Cost);
    }

    [Fact]
    public void No_trader_swaps_across_categories()
    {
        var quote = MaterialTrader.Quote(Mat("carbon"), Mat("shieldemitters"), wanted: 1, held: 300);
        Assert.False(quote.Possible);
        Assert.Equal(0, quote.Cost);
        Assert.Contains("raw", quote.Impossible!);
    }

    [Fact]
    public void Trading_a_material_for_itself_is_rejected()
    {
        Assert.False(MaterialTrader.Quote(Mat("carbon"), Mat("carbon"), wanted: 5, held: 300).Possible);
    }

    [Fact]
    public void Asking_for_nothing_is_rejected()
    {
        Assert.False(MaterialTrader.Quote(Mat("carbon"), Mat("vanadium"), wanted: 0, held: 300).Possible);
    }

    [Fact]
    public void A_quote_is_unaffordable_when_the_hold_is_short()
    {
        var quote = MaterialTrader.Quote(Mat("carbon"), Mat("vanadium"), wanted: 1, held: 5);
        Assert.True(quote.Possible);
        Assert.Equal(6, quote.Cost);
        Assert.False(quote.Affordable);
    }

    [Fact]
    public void Best_sources_ranks_the_cheapest_affordable_trade_first()
    {
        var held = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["niobium"] = 50,    // same group, one grade above vanadium — cheapest
            ["carbon"] = 300,    // same group, one grade below — 6 per unit
            ["zinc"] = 200,      // other group, same grade — 6 per unit
        };

        var best = MaterialTrader.BestSources(Mat("vanadium"), wanted: 3, held);

        Assert.NotEmpty(best);
        Assert.Equal("niobium", best[0].Source.Symbol);
        Assert.Equal(1, best[0].Cost);
        Assert.All(best, q => Assert.True(q.Affordable));
        Assert.True(best.Zip(best.Skip(1)).All(p => p.First.Cost <= p.Second.Cost));
    }

    [Fact]
    public void Best_sources_ignores_materials_the_commander_cannot_cover()
    {
        var held = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase) { ["carbon"] = 2 };

        // Six carbon are needed for one vanadium and only two are held.
        Assert.Empty(MaterialTrader.BestSources(Mat("vanadium"), wanted: 1, held));
    }
}
