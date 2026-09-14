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

    [Fact]
    public void Every_genus_has_a_positive_sample_distance()
    {
        Assert.All(Catalog.Genera, g =>
        {
            Assert.True(g.SampleDistanceMeters > 0, $"{g.Name} has non-positive sample distance");
            Assert.All(g.Species, s => Assert.Equal(g.SampleDistanceMeters, s.SampleDistanceMeters));
        });
    }

    [Theory]
    [InlineData("Stratum", 500)]
    [InlineData("Bacterium", 500)]
    [InlineData("Tubus", 800)]
    [InlineData("Frutexa", 150)]
    [InlineData("Electricae", 1000)]
    [InlineData("Tussock", 200)]
    [InlineData("Cactoida", 300)]
    [InlineData("Clypeus", 150)]
    public void Genera_have_canonical_minimum_sample_distances(string genusName, int expectedDistance)
    {
        var genus = Catalog.Genera.FirstOrDefault(g => g.Name == genusName);
        Assert.NotNull(genus);
        Assert.Equal(expectedDistance, genus!.SampleDistanceMeters);
    }

    [Fact]
    public void Predict_finds_stratum_tectonicas_on_undiscovered_hmc_body()
    {
        var env = new BodyEnvironment(
            PlanetClass: "High metal content body",
            Atmosphere: "carbon dioxide atmosphere",
            AtmosphereType: "CarbonDioxide",
            SurfaceGravityG: 0.35,
            SurfaceTemperatureK: 210.0,
            Landable: true,
            WasDiscovered: false);

        var predictions = Catalog.Predict(env);
        Assert.NotEmpty(predictions);

        var tectonicas = predictions.FirstOrDefault(p => p.Species.Name == "Stratum Tectonicas");
        Assert.NotNull(tectonicas);
        Assert.Equal(500, tectonicas!.SampleDistanceMeters);
        Assert.Equal(19010800, tectonicas.BaseValue);
        Assert.Equal(95054000, tectonicas.EstimatedValue); // 5x first discovery bonus
        Assert.True(tectonicas.IsFirstDiscovery);
    }

    [Fact]
    public void Predict_finds_electricae_on_cold_icy_body()
    {
        var env = new BodyEnvironment(
            PlanetClass: "Icy body",
            Atmosphere: "neon atmosphere",
            AtmosphereType: "Neon",
            SurfaceGravityG: 0.15,
            SurfaceTemperatureK: 75.0,
            Landable: true,
            WasDiscovered: true);

        var predictions = Catalog.Predict(env);
        Assert.NotEmpty(predictions);

        var electricae = predictions.FirstOrDefault(p => p.Genus.Name == "Electricae");
        Assert.NotNull(electricae);
        Assert.Equal(1000, electricae!.SampleDistanceMeters);
        Assert.False(electricae.IsFirstDiscovery);
        Assert.Equal(electricae.BaseValue, electricae.EstimatedValue);
    }

    [Fact]
    public void Predict_restricts_candidates_when_confirmed_genera_supplied()
    {
        var env = new BodyEnvironment(
            PlanetClass: "High metal content body",
            Atmosphere: "carbon dioxide atmosphere",
            AtmosphereType: "CarbonDioxide",
            SurfaceGravityG: 0.35,
            SurfaceTemperatureK: 210.0,
            Landable: true,
            WasDiscovered: false);

        var bacteriumGenus = Catalog.Genera.First(g => g.Name == "Bacterium");
        var predictions = Catalog.Predict(env, new[] { bacteriumGenus });

        Assert.NotEmpty(predictions);
        Assert.All(predictions, p => Assert.Equal("Bacterium", p.Genus.Name));
    }

    [Fact]
    public void Predict_returns_empty_for_non_landable_body()
    {
        var env = new BodyEnvironment(
            PlanetClass: "High metal content body",
            Atmosphere: "carbon dioxide atmosphere",
            AtmosphereType: "CarbonDioxide",
            SurfaceGravityG: 0.35,
            SurfaceTemperatureK: 210.0,
            Landable: false,
            WasDiscovered: false);

        var predictions = Catalog.Predict(env);
        Assert.Empty(predictions);
    }

    // --- Spawn rules. ---

    private static BodyEnvironment World(
        string planetClass = "High metal content body",
        string atmosphereType = "CarbonDioxide",
        string? atmosphere = null,
        double gravityG = 0.1,
        double temperatureK = 180,
        bool wasDiscovered = true,
        string? volcanism = null)
        => new(planetClass, atmosphere ?? $"thin {atmosphereType} atmosphere", atmosphereType,
               gravityG, temperatureK, Landable: true, wasDiscovered, volcanism);

    private static string[] GeneraOf(IEnumerable<BioPrediction> predictions)
        => predictions.Select(p => p.Genus.Name).Distinct().OrderBy(n => n).ToArray();

    [Fact]
    public void Every_rule_names_a_real_genus_or_species()
    {
        var genera = Catalog.Genera.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var species = Catalog.Species.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(ExobiologyPredictionRules.RuledGenera, g => Assert.Contains(g, genera));
        Assert.All(ExobiologyPredictionRules.RuledSpecies, s => Assert.Contains(s, species));
    }

    [Fact]
    public void A_body_without_an_atmosphere_predicts_nothing()
    {
        Assert.Empty(Catalog.Predict(World(atmosphereType: "None", atmosphere: "")));
    }

    [Fact]
    public void Horizons_era_genera_are_never_predicted_from_physics_alone()
    {
        // Their spawn hinges on star class, nebulae or nearby sites a planet scan can't see.
        var horizons = new[] { "Amphora Plant", "Anemone", "Bark Mound", "Brain Tree", "Crystalline Shards", "Sinuous Tubers", "Radicoida" };
        var worlds = new[]
        {
            World(),
            World(planetClass: "Rocky body", atmosphereType: "Ammonia"),
            World(planetClass: "Icy body", atmosphereType: "Neon", temperatureK: 70),
            World(planetClass: "Rocky ice body", atmosphereType: "Argon", temperatureK: 120, volcanism: "minor water magma volcanism"),
        };

        foreach (var world in worlds)
            Assert.Empty(GeneraOf(Catalog.Predict(world)).Intersect(horizons));
    }

    [Fact]
    public void Gravity_above_the_low_g_cap_leaves_only_the_heavyweight_genera()
    {
        var light = GeneraOf(Catalog.Predict(World(gravityG: 0.1, temperatureK: 180)));
        Assert.Contains("Tubus", light);
        Assert.Contains("Aleoida", light);

        var heavy = GeneraOf(Catalog.Predict(World(gravityG: 0.6, temperatureK: 180)));
        Assert.Equal(new[] { "Bacterium", "Stratum" }, heavy);
    }

    [Fact]
    public void Temperature_bounds_exclude_a_genus_outside_its_band()
    {
        Assert.Contains("Tubus", GeneraOf(Catalog.Predict(World(temperatureK: 175))));
        Assert.DoesNotContain("Tubus", GeneraOf(Catalog.Predict(World(temperatureK: 230))));
        Assert.DoesNotContain("Stratum", GeneraOf(Catalog.Predict(World(temperatureK: 150))));
    }

    [Fact]
    public void Unknown_temperature_and_gravity_never_exclude_a_genus()
    {
        var genera = GeneraOf(Catalog.Predict(World(gravityG: 0, temperatureK: 0)));
        Assert.Contains("Tubus", genera);
        Assert.Contains("Stratum", genera);
    }

    [Fact]
    public void The_atmosphere_decides_which_species_of_a_genus_are_candidates()
    {
        var ammonia = Catalog.Predict(World(planetClass: "Rocky body", atmosphereType: "Ammonia", temperatureK: 170))
            .Select(p => p.Species.Name).ToList();

        Assert.Contains("Bacterium Alcyoneum", ammonia);
        Assert.DoesNotContain("Bacterium Aurasus", ammonia);   // carbon dioxide only
        Assert.Contains("Tussock Catena", ammonia);
        Assert.DoesNotContain("Tussock Virgam", ammonia);      // water only
        Assert.DoesNotContain("Stratum Tectonicas", ammonia);  // high metal content bodies only
    }

    [Fact]
    public void The_prose_atmosphere_is_read_when_the_type_is_missing()
    {
        var env = new BodyEnvironment("Icy body", "thin argon-rich atmosphere", AtmosphereType: null,
            SurfaceGravityG: 0.1, SurfaceTemperatureK: 90, Landable: true, WasDiscovered: true);

        var species = Catalog.Predict(env).Select(p => p.Species.Name).ToList();
        Assert.Contains("Fonticulua Upupam", species);
        Assert.DoesNotContain("Fonticulua Campestris", species);   // plain argon, not argon-rich
    }

    [Fact]
    public void Fumerola_needs_volcanism()
    {
        Assert.DoesNotContain("Fumerola", GeneraOf(Catalog.Predict(World(planetClass: "Icy body", atmosphereType: "Argon", temperatureK: 100))));
        Assert.Contains("Fumerola", GeneraOf(Catalog.Predict(World(planetClass: "Icy body", atmosphereType: "Argon", temperatureK: 100,
            volcanism: "minor water magma volcanism"))));
    }

    [Fact]
    public void A_confirmed_genus_the_rules_cannot_place_still_lists_its_species()
    {
        // A DSS pass is authoritative. Every Fonticulua species is ruled icy-only, so on this metal
        // world the rules reject them all — but a mapped genus must never vanish from the card.
        var fonticulua = Catalog.Genera.First(g => g.Name == "Fonticulua");

        var predictions = Catalog.Predict(World(), new[] { fonticulua });

        Assert.Equal(fonticulua.Species.Count, predictions.Count);
        Assert.All(predictions, p => Assert.Equal(500, p.SampleDistanceMeters));
    }

    [Fact]
    public void A_confirmed_genus_with_no_spawn_rule_is_listed_once_mapped()
    {
        var anemone = Catalog.Genera.First(g => g.Name == "Anemone");

        var predictions = Catalog.Predict(World(), new[] { anemone });

        Assert.Equal(anemone.Species.Count, predictions.Count);
    }

    [Fact]
    public void Predictions_are_ordered_richest_first()
    {
        var values = Catalog.Predict(World(wasDiscovered: false)).Select(p => p.EstimatedValue).ToList();
        Assert.Equal(values.OrderByDescending(v => v), values);
    }
}
