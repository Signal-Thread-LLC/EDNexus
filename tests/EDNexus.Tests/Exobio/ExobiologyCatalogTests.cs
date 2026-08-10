using System.Linq;
using EDNexus.Core.Exobio;
using Xunit;

namespace EDNexus.Tests.Exobio;

public class ExobiologyCatalogTests
{
    private static ExobiologyCatalog Catalog => ExobiologyCatalog.Default;

    [Fact]
    public void The_embedded_catalog_loads_every_genus_and_species()
    {
        Assert.Equal(22, Catalog.Genera.Count);
        Assert.Equal(118, Catalog.Species.Count);
        Assert.All(Catalog.Species, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.StartsWith("$Codex_Ent_", s.Symbol);
            Assert.True(s.Value > 0, $"{s.Name} has no value");
        });
    }

    [Theory]
    // Spot-checks across the value range, cross-verified between BioScan, StratumFinder and ed-dsn.
    [InlineData("$Codex_Ent_Stratum_07_Name;", "Stratum Tectonicas", 19010800)]
    [InlineData("$Codex_Ent_Bacterial_05_Name;", "Bacterium Vesicula", 1000000)]
    [InlineData("$Codex_Ent_Aleoids_03_Name;", "Aleoida Spica", 3385200)]
    [InlineData("$Codex_Ent_Conchas_04_Name;", "Concha Biconcavis", 19010800)]
    public void Known_species_resolve_by_symbol_with_the_right_value(string symbol, string name, long value)
    {
        var species = Catalog.SpeciesBySymbol(symbol);
        Assert.NotNull(species);
        Assert.Equal(name, species!.Name);
        Assert.Equal(value, species.Value);
    }

    [Fact]
    public void Species_also_resolve_by_their_localised_name()
    {
        var byName = Catalog.SpeciesByName("Osseus Pellebantus");
        Assert.NotNull(byName);
        Assert.Equal(9739000, byName!.Value);
    }

    [Fact]
    public void Resolve_prefers_the_symbol_but_falls_back_to_the_name()
    {
        Assert.Equal("Stratum Tectonicas", Catalog.Resolve("$Codex_Ent_Stratum_07_Name;", null)?.Name);
        Assert.Equal("Stratum Tectonicas", Catalog.Resolve(null, "Stratum Tectonicas")?.Name);
        Assert.Null(Catalog.Resolve("$Codex_Ent_Not_A_Species;", "Nothing At All"));
    }

    [Fact]
    public void First_logged_pays_five_times_the_base_value()
    {
        var tectonicas = Catalog.SpeciesByName("Stratum Tectonicas")!;
        Assert.Equal(19010800, tectonicas.Value);
        Assert.Equal(95054000, tectonicas.FirstLoggedValue);
    }

    [Fact]
    public void A_genus_spans_the_value_range_of_its_species()
    {
        var bacterium = Catalog.Genus("$Codex_Ent_Bacterial_Genus_Name;");
        Assert.NotNull(bacterium);
        Assert.Equal("Bacterium", bacterium!.Name);
        Assert.Equal(13, bacterium.Species.Count);
        Assert.Equal(1000000, bacterium.MinValue);
        Assert.Equal(8418000, bacterium.MaxValue);
    }

    [Fact]
    public void Every_species_belongs_to_a_genus_that_the_catalog_indexes()
    {
        // SAASignalsFound reports genus symbols; an unindexed one would silently value a body at zero.
        Assert.All(Catalog.Species, s => Assert.NotNull(Catalog.Genus(s.GenusSymbol)));
    }

    [Fact]
    public void Species_symbols_are_unique_except_for_the_single_species_genera()
    {
        // Bark Mound and Amphora Plant reuse their genus symbol as the species symbol; everything
        // else must be distinct or ScanOrganic would resolve to the wrong payout.
        var duplicates = Catalog.Species
            .GroupBy(s => s.Symbol, System.StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }
}
