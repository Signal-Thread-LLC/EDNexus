namespace EDNexus.Core.Exobio;

/// <summary>
/// The offline spawn rules behind <see cref="ExobiologyCatalog.Predict"/>: which planet classes,
/// atmospheres, temperatures and gravities each genus (and, where the atmosphere decides it, each
/// species) grows under. Distilled from the community's published Odyssey biology research — the
/// same conditions tools like Observatory BioInsights and EDMC-BioScan check.
/// </summary>
/// <remarks>
/// The rules are deliberately permissive: a missing candidate costs the commander a surprise, but a
/// wrong exclusion hides a real payout. Species temperature bands are left out for that reason, and
/// the Horizons-era genera (Anemone, Brain Tree, Bark Mound, …) are never predicted from physics
/// alone because they hinge on star class, nebulae or nearby sites a planet <c>Scan</c> can't see —
/// they appear only once a DSS pass names them.
/// </remarks>
internal static class ExobiologyPredictionRules
{
    /// <summary>Odyssey flora outside these genera tolerate at most this much gravity.</summary>
    private const double LowGravityCapG = 0.27;

    [Flags]
    internal enum Body
    {
        None = 0,
        Rocky = 1,
        HighMetal = 2,
        MetalRich = 4,
        Icy = 8,
        RockyIce = 16,
        AnyRocky = Rocky | HighMetal | MetalRich,
        AnyIcy = Icy | RockyIce,
    }

    [Flags]
    internal enum Atmo
    {
        None = 0,
        CarbonDioxide = 1 << 0,
        SulphurDioxide = 1 << 1,
        Ammonia = 1 << 2,
        Water = 1 << 3,
        Methane = 1 << 4,
        Nitrogen = 1 << 5,
        Oxygen = 1 << 6,
        Argon = 1 << 7,
        ArgonRich = 1 << 8,
        Neon = 1 << 9,
        NeonRich = 1 << 10,
        Helium = 1 << 11,
        Other = 1 << 12,
        AnyThin = ~None,
    }

    /// <summary>Conditions a genus or species needs. Unset bounds don't constrain.</summary>
    private sealed record Rule(
        Body Bodies,
        Atmo Atmospheres,
        double MinTempK = 0,
        double MaxTempK = double.MaxValue,
        double MaxGravityG = double.MaxValue,
        bool RequiresVolcanism = false);

    private static readonly Dictionary<string, Rule> GenusRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Aleoida"] = new(Body.AnyRocky, Atmo.CarbonDioxide | Atmo.Ammonia, MinTempK: 175, MaxTempK: 270, MaxGravityG: LowGravityCapG),
        ["Bacterium"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.AnyThin),
        ["Cactoida"] = new(Body.AnyRocky, Atmo.CarbonDioxide | Atmo.Ammonia | Atmo.Water, MinTempK: 160, MaxGravityG: LowGravityCapG),
        ["Clypeus"] = new(Body.AnyRocky, Atmo.CarbonDioxide | Atmo.Water, MinTempK: 190, MaxGravityG: LowGravityCapG),
        ["Concha"] = new(Body.AnyRocky, Atmo.CarbonDioxide | Atmo.Ammonia | Atmo.Water | Atmo.Nitrogen, MaxGravityG: LowGravityCapG),
        ["Electricae"] = new(Body.Icy, Atmo.Argon | Atmo.ArgonRich | Atmo.Neon | Atmo.NeonRich | Atmo.Helium, MinTempK: 50, MaxTempK: 150, MaxGravityG: LowGravityCapG),
        ["Fonticulua"] = new(Body.AnyIcy, Atmo.Argon | Atmo.ArgonRich | Atmo.Neon | Atmo.NeonRich | Atmo.Methane | Atmo.Nitrogen | Atmo.Oxygen, MaxGravityG: LowGravityCapG),
        ["Frutexa"] = new(Body.AnyRocky, Atmo.CarbonDioxide | Atmo.SulphurDioxide | Atmo.Ammonia | Atmo.Water, MaxGravityG: LowGravityCapG),
        ["Fumerola"] = new(Body.AnyIcy | Body.AnyRocky, Atmo.AnyThin, MaxGravityG: LowGravityCapG, RequiresVolcanism: true),
        ["Fungoida"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.CarbonDioxide | Atmo.Ammonia | Atmo.Methane | Atmo.Argon | Atmo.Nitrogen | Atmo.Water, MaxGravityG: LowGravityCapG),
        ["Osseus"] = new(Body.AnyRocky | Body.RockyIce, Atmo.CarbonDioxide | Atmo.Ammonia | Atmo.Water | Atmo.Argon | Atmo.ArgonRich | Atmo.Methane | Atmo.Nitrogen, MaxGravityG: LowGravityCapG),
        ["Recepta"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.CarbonDioxide | Atmo.SulphurDioxide, MaxGravityG: LowGravityCapG),
        ["Stratum"] = new(Body.AnyRocky, Atmo.CarbonDioxide | Atmo.SulphurDioxide | Atmo.Ammonia | Atmo.Water | Atmo.Oxygen | Atmo.Methane, MinTempK: 165),
        ["Tubus"] = new(Body.AnyRocky, Atmo.CarbonDioxide | Atmo.Ammonia, MinTempK: 160, MaxTempK: 190, MaxGravityG: 0.15),
        ["Tussock"] = new(Body.AnyRocky, Atmo.CarbonDioxide | Atmo.SulphurDioxide | Atmo.Ammonia | Atmo.Water | Atmo.Argon | Atmo.Methane, MaxGravityG: LowGravityCapG),
    };

    /// <summary>
    /// Per-species refinements, keyed by catalog name, for the species the atmosphere (or planet
    /// class) actually decides. Species not listed inherit their genus rule.
    /// </summary>
    private static readonly Dictionary<string, Rule> SpeciesRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Aleoida Arcus"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Aleoida Coronamus"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Aleoida Gravis"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Aleoida Laminiae"] = new(Body.AnyRocky, Atmo.Ammonia),
        ["Aleoida Spica"] = new(Body.AnyRocky, Atmo.Ammonia),

        ["Bacterium Acies"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.Neon | Atmo.NeonRich),
        ["Bacterium Alcyoneum"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.Ammonia),
        ["Bacterium Aurasus"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.CarbonDioxide),
        ["Bacterium Bullaris"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.Methane),
        ["Bacterium Cerbrus"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.Water | Atmo.SulphurDioxide),
        ["Bacterium Informem"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.Nitrogen),
        ["Bacterium Nebulus"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.Helium),
        ["Bacterium Vesicula"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.Argon),
        ["Bacterium Volu"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.Oxygen),

        ["Cactoida Cortexum"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Cactoida Lapis"] = new(Body.AnyRocky, Atmo.Ammonia),
        ["Cactoida Peperatis"] = new(Body.AnyRocky, Atmo.Ammonia),
        ["Cactoida Pullulanta"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Cactoida Vermis"] = new(Body.AnyRocky, Atmo.Water),

        ["Concha Aureolas"] = new(Body.AnyRocky, Atmo.Ammonia),
        ["Concha Biconcavis"] = new(Body.AnyRocky, Atmo.Nitrogen),
        ["Concha Labiata"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Concha Renibus"] = new(Body.AnyRocky, Atmo.CarbonDioxide | Atmo.Water),

        ["Fonticulua Campestris"] = new(Body.AnyIcy, Atmo.Argon),
        ["Fonticulua Digitos"] = new(Body.AnyIcy, Atmo.Methane),
        ["Fonticulua Fluctus"] = new(Body.AnyIcy, Atmo.Oxygen),
        ["Fonticulua Lapida"] = new(Body.AnyIcy, Atmo.Nitrogen),
        ["Fonticulua Segmentatus"] = new(Body.AnyIcy, Atmo.Neon | Atmo.NeonRich),
        ["Fonticulua Upupam"] = new(Body.AnyIcy, Atmo.ArgonRich),

        ["Frutexa Acus"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Frutexa Collum"] = new(Body.AnyRocky, Atmo.SulphurDioxide),
        ["Frutexa Fera"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Frutexa Flabellum"] = new(Body.AnyRocky, Atmo.Ammonia),
        ["Frutexa Flammasis"] = new(Body.AnyRocky, Atmo.Ammonia),
        ["Frutexa Metallicum"] = new(Body.HighMetal, Atmo.CarbonDioxide | Atmo.Ammonia | Atmo.Water),
        ["Frutexa Sponsae"] = new(Body.AnyRocky, Atmo.Water),

        ["Fungoida Bullarum"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.Argon | Atmo.Nitrogen),
        ["Fungoida Gelata"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.CarbonDioxide | Atmo.Water),
        ["Fungoida Setisis"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.Ammonia | Atmo.Methane),
        ["Fungoida Stabitis"] = new(Body.AnyRocky | Body.AnyIcy, Atmo.CarbonDioxide | Atmo.Water),

        ["Osseus Cornibus"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Osseus Fractus"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Osseus Pellebantus"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Osseus Pumice"] = new(Body.AnyRocky | Body.RockyIce, Atmo.Argon | Atmo.ArgonRich | Atmo.Methane | Atmo.Nitrogen),
        ["Osseus Spiralis"] = new(Body.AnyRocky, Atmo.Ammonia),

        ["Stratum Araneamus"] = new(Body.AnyRocky, Atmo.SulphurDioxide),
        ["Stratum Cucumisis"] = new(Body.AnyRocky, Atmo.SulphurDioxide | Atmo.CarbonDioxide),
        ["Stratum Excutitus"] = new(Body.AnyRocky, Atmo.SulphurDioxide | Atmo.CarbonDioxide),
        ["Stratum Frigus"] = new(Body.AnyRocky, Atmo.Ammonia | Atmo.Water | Atmo.SulphurDioxide),
        ["Stratum Laminamus"] = new(Body.AnyRocky, Atmo.Ammonia),
        ["Stratum Limaxus"] = new(Body.AnyRocky, Atmo.SulphurDioxide | Atmo.CarbonDioxide),
        ["Stratum Tectonicas"] = new(Body.HighMetal, Atmo.AnyThin),

        ["Tubus Cavas"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tubus Compagibus"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tubus Conifer"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tubus Rosarium"] = new(Body.AnyRocky, Atmo.Ammonia),
        ["Tubus Sororibus"] = new(Body.HighMetal, Atmo.CarbonDioxide | Atmo.Ammonia),

        ["Tussock Albata"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tussock Capillum"] = new(Body.AnyRocky, Atmo.Argon | Atmo.Methane),
        ["Tussock Caputus"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tussock Catena"] = new(Body.AnyRocky, Atmo.Ammonia),
        ["Tussock Cultro"] = new(Body.AnyRocky, Atmo.Ammonia),
        ["Tussock Divisa"] = new(Body.AnyRocky, Atmo.Ammonia),
        ["Tussock Ignis"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tussock Pennata"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tussock Pennatis"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tussock Propagito"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tussock Serrati"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tussock Stigmasis"] = new(Body.AnyRocky, Atmo.SulphurDioxide),
        ["Tussock Triticum"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tussock Ventusa"] = new(Body.AnyRocky, Atmo.CarbonDioxide),
        ["Tussock Virgam"] = new(Body.AnyRocky, Atmo.Water),
    };

    /// <summary>Genus names with a spawn rule — exposed so tests can check each names a catalog genus.</summary>
    internal static IEnumerable<string> RuledGenera => GenusRules.Keys;

    /// <summary>Species names with a refinement — exposed so tests can check each names a catalog species.</summary>
    internal static IEnumerable<string> RuledSpecies => SpeciesRules.Keys;

    /// <summary>A body's physics reduced to the terms the rules are written in.</summary>
    internal readonly record struct Conditions(Body Body, Atmo Atmosphere, double TemperatureK, double GravityG, bool HasVolcanism);

    internal static Conditions Classify(BodyEnvironment env) => new(
        ParseBody(env.PlanetClass),
        ParseAtmosphere(env.AtmosphereType, env.Atmosphere),
        env.SurfaceTemperatureK,
        env.SurfaceGravityG,
        env.Volcanism is { Length: > 0 } v && !v.Contains("no volcanism", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a genus can grow here from physics alone. Genera without a rule (the Horizons-era
    /// ones, and any genus added to the catalog later) are never predicted — only a DSS names them.
    /// </summary>
    internal static bool GenusMatches(BioGenus genus, Conditions c)
        => GenusRules.TryGetValue(genus.Name, out var rule) && Matches(rule, c);

    /// <summary>Whether a species within an eligible genus matches. Unlisted species inherit the genus.</summary>
    internal static bool SpeciesMatches(BioSpecies species, Conditions c)
        => !SpeciesRules.TryGetValue(species.Name, out var rule) || Matches(rule, c);

    private static bool Matches(Rule rule, Conditions c)
    {
        // Every Odyssey genus needs a (thin) atmosphere; landable bodies never have a thick one.
        if (c.Atmosphere == Atmo.None) return false;
        if ((rule.Bodies & c.Body) == 0) return false;
        if ((rule.Atmospheres & c.Atmosphere) == 0) return false;
        if (rule.RequiresVolcanism && !c.HasVolcanism) return false;

        // Zero means the event didn't report it; an unknown value must not exclude anything.
        if (c.TemperatureK > 0 && (c.TemperatureK < rule.MinTempK || c.TemperatureK > rule.MaxTempK)) return false;
        if (c.GravityG > 0 && c.GravityG > rule.MaxGravityG) return false;
        return true;
    }

    private static Body ParseBody(string planetClass)
    {
        var p = planetClass.ToLowerInvariant();
        if (p.Contains("rocky ice")) return Body.RockyIce;
        if (p.Contains("icy")) return Body.Icy;
        if (p.Contains("high metal")) return Body.HighMetal;
        if (p.Contains("metal rich")) return Body.MetalRich;
        if (p.Contains("rocky")) return Body.Rocky;
        return Body.None;
    }

    /// <summary>
    /// Read the atmosphere from <c>AtmosphereType</c> ("ArgonRich"), falling back to the prose
    /// <c>Atmosphere</c> field ("thin argon-rich atmosphere") when the type is missing.
    /// </summary>
    internal static Atmo ParseAtmosphere(string? atmosphereType, string atmosphere)
    {
        var key = atmosphereType is { Length: > 0 } t && !t.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? t
            : atmosphere;
        key = key.ToLowerInvariant()
            .Replace("atmosphere", "")
            .Replace("thin", "")
            .Replace("hot", "")
            .Replace("-", "")
            .Replace(" ", "");

        if (key.Length == 0 || key == "none" || key == "no") return Atmo.None;

        // "Rich" variants are only distinct for argon and neon; carbon dioxide-rich and water-rich
        // grow the same flora as their plain forms.
        return key switch
        {
            _ when key.StartsWith("carbondioxide") => Atmo.CarbonDioxide,
            _ when key.StartsWith("sulphurdioxide") || key.StartsWith("sulfurdioxide") => Atmo.SulphurDioxide,
            _ when key.StartsWith("ammonia") => Atmo.Ammonia,
            _ when key.StartsWith("water") => Atmo.Water,
            _ when key.StartsWith("methane") => Atmo.Methane,
            _ when key.StartsWith("nitrogen") => Atmo.Nitrogen,
            _ when key.StartsWith("oxygen") => Atmo.Oxygen,
            "argonrich" => Atmo.ArgonRich,
            _ when key.StartsWith("argon") => Atmo.Argon,
            "neonrich" => Atmo.NeonRich,
            _ when key.StartsWith("neon") => Atmo.Neon,
            _ when key.StartsWith("helium") => Atmo.Helium,
            _ => Atmo.Other,
        };
    }
}
