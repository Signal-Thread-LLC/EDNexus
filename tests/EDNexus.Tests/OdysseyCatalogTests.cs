using System.Linq;
using EDNexus.Core.Odyssey;
using Xunit;

namespace EDNexus.Tests;

public class OdysseyCatalogTests
{
    private readonly OdysseyCatalog _catalog = OdysseyCatalog.Default;

    [Fact]
    public void All_resources_load()
    {
        Assert.NotEmpty(_catalog.Suits);
        Assert.NotEmpty(_catalog.Weapons);
        Assert.NotEmpty(_catalog.Materials);
        Assert.NotEmpty(_catalog.SuitMods);
        Assert.NotEmpty(_catalog.WeaponMods);
        Assert.NotEmpty(_catalog.Engineers);
    }

    [Fact]
    public void Flight_suit_has_no_upgrade_path()
    {
        var flightSuit = _catalog.Suit("flightsuit");
        Assert.NotNull(flightSuit);
        Assert.False(flightSuit!.IsUpgradeable);
        Assert.Empty(flightSuit.GradeSteps);
    }

    [Fact]
    public void Every_suit_grade_step_material_resolves_to_a_known_onfoot_material()
    {
        foreach (var suit in _catalog.Suits)
        foreach (var step in suit.GradeSteps)
        foreach (var m in step.Materials)
            Assert.True(_catalog.Material(m.Symbol) is not null,
                $"Suit '{suit.Id}' G{step.Grade} references unknown material '{m.Symbol}'.");
    }

    [Fact]
    public void Every_weapon_grade_step_material_resolves_to_a_known_onfoot_material()
    {
        foreach (var weapon in _catalog.Weapons)
        foreach (var step in weapon.GradeSteps)
        foreach (var m in step.Materials)
            Assert.True(_catalog.Material(m.Symbol) is not null,
                $"Weapon '{weapon.Id}' G{step.Grade} references unknown material '{m.Symbol}'.");
    }

    [Fact]
    public void Every_mod_material_and_engineer_resolves()
    {
        foreach (var mod in _catalog.SuitMods.Concat(_catalog.WeaponMods))
        {
            foreach (var m in mod.Materials)
                Assert.True(_catalog.Material(m.Symbol) is not null,
                    $"Modification '{mod.Id}' references unknown material '{m.Symbol}'.");

            Assert.NotEmpty(mod.EngineerIds);
            foreach (var id in mod.EngineerIds)
                Assert.True(_catalog.Engineer(id) is not null,
                    $"Modification '{mod.Id}' references unknown engineer '{id}'.");
        }
    }

    [Fact]
    public void Material_categories_are_one_of_the_four_onfoot_inventories()
    {
        string[] valid = { "Item", "Component", "Data", "Consumable" };
        foreach (var m in _catalog.Materials)
            Assert.Contains(m.Category, valid);
    }

    [Theory]
    [InlineData("tacticalsuit_class3", "dominator")]
    [InlineData("utilitysuit_class1", "maverick")]
    [InlineData("explorationsuit_class5", "artemis")]
    [InlineData("flightsuit", "flightsuit")]
    public void SuitBySymbolPrefix_resolves_the_journal_suit_symbol(string journalSymbol, string expectedId)
    {
        var suit = _catalog.SuitBySymbolPrefix(journalSymbol);
        Assert.NotNull(suit);
        Assert.Equal(expectedId, suit!.Id);
    }

    [Fact]
    public void Dominator_grade5_step_is_cumulative_and_matches_the_wiki()
    {
        var dominator = _catalog.Suit("dominator");
        Assert.NotNull(dominator);
        var g5 = dominator!.Step(5);
        Assert.NotNull(g5);
        Assert.Equal(7_500_000, g5!.Credits);
        Assert.Equal(5, g5.Materials.Single(m => m.Symbol == "suitschematic").Count);
        Assert.Equal(12, g5.Materials.Single(m => m.Symbol == "titaniumplating").Count);
    }
}
