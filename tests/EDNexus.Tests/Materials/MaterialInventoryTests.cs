using System.Linq;
using EDNexus.Core.Engineering;
using EDNexus.Core.Materials;
using EDNexus.Core.State;
using Xunit;

namespace EDNexus.Tests.Materials;

public class MaterialInventoryTests
{
    [Theory]
    [InlineData("carbon", 1, 300)]
    [InlineData("vanadium", 2, 250)]
    [InlineData("niobium", 3, 200)]
    [InlineData("yttrium", 4, 150)]
    [InlineData("militarygradealloys", 5, 100)]
    public void The_storage_cap_follows_the_grade(string symbol, int grade, int cap)
    {
        var info = EngineeringCatalog.Default.Material(symbol);
        Assert.NotNull(info);
        Assert.Equal(grade, info!.Grade);
        Assert.Equal(cap, info.Cap);
    }

    [Fact]
    public void The_catalog_covers_every_ship_material_with_a_trader_group()
    {
        var materials = EngineeringCatalog.Default.Materials;
        Assert.Equal(137, materials.Count);
        Assert.All(materials, m =>
        {
            Assert.InRange(m.Grade, 1, 5);
            Assert.False(string.IsNullOrWhiteSpace(m.Group), $"{m.Symbol} has no trader group");
            Assert.Contains(m.Category, MaterialInventory.Categories);
        });
    }

    [Fact]
    public void Raw_materials_form_the_seven_by_four_trader_grid()
    {
        var raw = EngineeringCatalog.Default.Materials.Where(m => m.Category == "Raw").ToList();
        Assert.Equal(28, raw.Count);
        Assert.Equal(7, raw.Select(m => m.Group).Distinct().Count());
        Assert.All(raw.GroupBy(m => m.Group), g => Assert.Equal(4, g.Count()));
    }

    [Fact]
    public void A_category_view_lists_every_material_and_the_held_counts()
    {
        var state = new CommanderState();
        state.Materials.Raw["carbon"] = 120;
        state.Materials.Raw["vanadium"] = 250;   // at cap

        var view = MaterialInventory.Category(state, "Raw");

        Assert.Equal(28, view.Stocks.Count);
        Assert.Equal(370, view.TotalHeld);
        Assert.Equal(2, view.Held.Count);        // only the two actually carried

        var carbon = view.Stocks.Single(s => s.Symbol == "carbon");
        Assert.Equal(120, carbon.Held);
        Assert.Equal(300, carbon.Cap);
        Assert.Equal(180, carbon.Spare);
        Assert.False(carbon.IsFull);
        Assert.Equal(0.4, carbon.Fraction, 3);
    }

    [Fact]
    public void A_material_at_its_cap_is_flagged_as_full_with_no_room_left()
    {
        var state = new CommanderState();
        state.Materials.Raw["vanadium"] = 250;

        var vanadium = MaterialInventory.Category(state, "Raw").Stocks.Single(s => s.Symbol == "vanadium");
        Assert.True(vanadium.IsFull);
        Assert.Equal(0, vanadium.Spare);
        Assert.Equal(1.0, vanadium.Fraction);
    }

    [Fact]
    public void Over_cap_holdings_do_not_produce_negative_room_or_a_bar_past_full()
    {
        var state = new CommanderState();
        state.Materials.Raw["vanadium"] = 999;   // defensive: the game should never report this

        var vanadium = MaterialInventory.Category(state, "Raw").Stocks.Single(s => s.Symbol == "vanadium");
        Assert.Equal(0, vanadium.Spare);
        Assert.Equal(1.0, vanadium.Fraction);
    }

    [Fact]
    public void At_cap_lists_the_capped_materials_rarest_first_across_all_categories()
    {
        var state = new CommanderState();
        state.Materials.Raw["carbon"] = 300;              // grade 1
        state.Materials.Raw["yttrium"] = 150;             // grade 4
        state.Materials.Manufactured["militarygradealloys"] = 100;  // grade 5
        state.Materials.Encoded["bulkscandata"] = 12;     // not full

        var capped = MaterialInventory.AtCap(state);

        Assert.Equal(3, capped.Count);
        Assert.Equal(new[] { 5, 4, 1 }, capped.Select(s => s.Grade));
    }

    [Fact]
    public void All_returns_the_three_categories_in_game_order()
    {
        var views = MaterialInventory.All(new CommanderState());
        Assert.Equal(new[] { "Raw", "Manufactured", "Encoded" }, views.Select(v => v.Category));
    }
}
