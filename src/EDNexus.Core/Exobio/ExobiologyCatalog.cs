using System.Text.Json;

namespace EDNexus.Core.Exobio;

/// <summary>
/// The static exobiology reference data — every genus, its species, and the Vista Genomics payout
/// for each — loaded once from the embedded JSON resource and indexed by journal codex symbol.
/// Mirrors <see cref="Engineering.EngineeringCatalog"/>'s shape.
/// </summary>
public sealed class ExobiologyCatalog
{
    private static readonly Lazy<ExobiologyCatalog> Lazy = new(Load);

    /// <summary>Shared, lazily-loaded catalog. Reference data is immutable, so one copy is safe to share.</summary>
    public static ExobiologyCatalog Default => Lazy.Value;

    private readonly Dictionary<string, BioGenus> _generaBySymbol;
    private readonly Dictionary<string, BioSpecies> _speciesBySymbol;
    private readonly Dictionary<string, BioSpecies> _speciesByName;

    private ExobiologyCatalog(IReadOnlyList<BioGenus> genera)
    {
        Genera = genera;
        Species = genera.SelectMany(g => g.Species).ToList();
        _generaBySymbol = genera.ToDictionary(g => g.Symbol, StringComparer.OrdinalIgnoreCase);
        // Bark Mound and Amphora Plant reuse the genus symbol for their single species, so the
        // species index can collide with itself — first entry wins rather than throwing.
        _speciesBySymbol = new Dictionary<string, BioSpecies>(StringComparer.OrdinalIgnoreCase);
        _speciesByName = new Dictionary<string, BioSpecies>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Species)
        {
            _speciesBySymbol.TryAdd(s.Symbol, s);
            _speciesByName.TryAdd(s.Name, s);
        }
    }

    public IReadOnlyList<BioGenus> Genera { get; }
    public IReadOnlyList<BioSpecies> Species { get; }

    /// <summary>Look up a genus by its journal symbol (<c>$Codex_Ent_Bacterial_Genus_Name;</c>).</summary>
    public BioGenus? Genus(string? symbol)
        => symbol is { Length: > 0 } ? _generaBySymbol.GetValueOrDefault(symbol) : null;

    /// <summary>Look up a species by its journal symbol (<c>$Codex_Ent_Bacterial_05_Name;</c>).</summary>
    public BioSpecies? SpeciesBySymbol(string? symbol)
        => symbol is { Length: > 0 } ? _speciesBySymbol.GetValueOrDefault(symbol) : null;

    /// <summary>Look up a species by its display name ("Bacterium Vesicula"), for localised events.</summary>
    public BioSpecies? SpeciesByName(string? name)
        => name is { Length: > 0 } ? _speciesByName.GetValueOrDefault(name) : null;

    /// <summary>
    /// Resolve a species from whichever identifiers an event carries — symbol first, since it is
    /// language-independent, falling back to the localised name.
    /// </summary>
    public BioSpecies? Resolve(string? symbol, string? name)
        => SpeciesBySymbol(symbol) ?? SpeciesByName(name);

    /// <summary>The most valuable species known, richest first — the targets worth detouring for.</summary>
    public IEnumerable<BioSpecies> MostValuable(int limit = 10)
        => Species.OrderByDescending(s => s.Value).Take(limit);

    /// <summary>
    /// Predict the species that can grow on a body from its planetary physics, richest first —
    /// fully offline, from <see cref="ExobiologyPredictionRules"/>.
    /// </summary>
    /// <param name="env">The body's physics, from its journal <c>Scan</c>.</param>
    /// <param name="confirmedGenera">
    /// Genera a DSS pass named. The DSS is authoritative, so these replace the genus-level guess and
    /// only the species within them are filtered; a confirmed genus the rules can't place still lists
    /// every species rather than vanishing.
    /// </param>
    public IReadOnlyList<BioPrediction> Predict(BodyEnvironment env, IReadOnlyList<BioGenus>? confirmedGenera = null)
    {
        var confirmed = confirmedGenera is { Count: > 0 };
        if (!confirmed && !env.Landable)
            return Array.Empty<BioPrediction>();

        var conditions = ExobiologyPredictionRules.Classify(env);
        var isFirstDiscovery = !env.WasDiscovered;

        var genera = confirmed
            ? confirmedGenera!.DistinctBy(g => g.Symbol, StringComparer.OrdinalIgnoreCase)
            : Genera.Where(g => ExobiologyPredictionRules.GenusMatches(g, conditions));

        var predictions = new List<BioPrediction>();
        foreach (var genus in genera)
        {
            var species = genus.Species.Where(s => ExobiologyPredictionRules.SpeciesMatches(s, conditions)).ToList();
            if (species.Count == 0 && confirmed)
                species = genus.Species.ToList();

            foreach (var s in species)
                predictions.Add(new BioPrediction(
                    s,
                    genus,
                    genus.SampleDistanceMeters,
                    s.Value,
                    isFirstDiscovery ? s.FirstLoggedValue : s.Value,
                    isFirstDiscovery));
        }

        return predictions
            .OrderByDescending(p => p.EstimatedValue)
            .ThenBy(p => p.Species.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static ExobiologyCatalog Load()
    {
        var genera = ReadResource<List<GenusDto>>("exobiology-species.json")
            .Select(g =>
            {
                var distance = g.SampleDistance.GetValueOrDefault();
                var species = (g.Species ?? new())
                    .Select(s => new BioSpecies(g.GenusSymbol, g.Genus, s.Symbol, s.Name, s.Value, distance))
                    .ToList();
                return new BioGenus(g.GenusSymbol, g.Genus, distance, species);
            })
            .ToList();
        return new ExobiologyCatalog(genera);
    }

    private static T ReadResource<T>(string fileName)
    {
        var asm = typeof(ExobiologyCatalog).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded exobiology resource '{fileName}' not found.");
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Exobiology resource '{fileName}' deserialized to null.");
    }

    // DTOs decouple the JSON shape from the public records.
    private sealed record GenusDto(string GenusSymbol, string Genus, int? SampleDistance, List<SpeciesDto>? Species);
    private sealed record SpeciesDto(string Symbol, string Name, long Value);
}
