using EDNexus.Core.Colonisation;
using Xunit;

namespace EDNexus.Tests;

public class CommodityNameTests
{
    [Theory]
    [InlineData("$aluminium_name;", "aluminium")]
    [InlineData("$Aluminium_name;", "aluminium")]   // contribution events capitalise the symbol
    [InlineData("aluminium", "aluminium")]          // bare cargo symbol
    [InlineData("Aluminium", "aluminium")]          // localised label
    [InlineData("$fruitandvegetables_name;", "fruitandvegetables")]
    [InlineData("Fruit and Vegetables", "fruitandvegetables")]  // spaces stripped
    [InlineData("Non-Lethal Weapons", "nonlethalweapons")]      // punctuation stripped
    public void Canonicalize_collapses_all_name_forms_to_one_key(string input, string expected)
        => Assert.Equal(expected, CommodityName.Canonicalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Canonicalize_returns_empty_for_blank(string? input)
        => Assert.Equal(string.Empty, CommodityName.Canonicalize(input));

    [Theory]
    [InlineData("drones")]          // journal symbol
    [InlineData("$drones_name;")]   // wrapped symbol
    [InlineData("Limpet")]          // localised cargo label
    [InlineData("Limpets")]
    public void IsLimpet_recognises_every_form_limpets_arrive_in(string input)
        => Assert.True(CommodityName.IsLimpet(input));

    [Theory]
    [InlineData("Painite")]
    [InlineData("$lowtemperaturediamond_name;")]
    [InlineData(null)]
    [InlineData("")]
    public void IsLimpet_is_false_for_real_cargo(string? input)
        => Assert.False(CommodityName.IsLimpet(input));
}
