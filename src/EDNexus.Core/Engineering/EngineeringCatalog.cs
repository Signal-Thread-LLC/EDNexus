using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EDNexus.Core.Engineering;

/// <summary>
/// The static engineering reference data — engineers, materials and blueprints — loaded once from
/// the embedded JSON resources and indexed for lookup. This is pure reference data; it never touches
/// the live commander state (the <see cref="EngineeringTracker"/> joins the two).
/// </summary>
public sealed class EngineeringCatalog
{
    private static readonly Lazy<EngineeringCatalog> Lazy = new(Load);

    /// <summary>Shared, lazily-loaded catalog. Reference data is immutable, so one copy is safe to share.</summary>
    public static EngineeringCatalog Default => Lazy.Value;

    private readonly Dictionary<string, Engineer> _engineersById;
    private readonly Dictionary<string, MaterialInfo> _materialsBySymbol;
    private readonly Dictionary<string, Blueprint> _blueprintsById;

    private EngineeringCatalog(IReadOnlyList<Engineer> engineers, IReadOnlyList<MaterialInfo> materials, IReadOnlyList<Blueprint> blueprints)
    {
        Engineers = engineers;
        Materials = materials;
        Blueprints = blueprints;
        _engineersById = engineers.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        _materialsBySymbol = materials.ToDictionary(m => m.Symbol, StringComparer.OrdinalIgnoreCase);
        _blueprintsById = blueprints.ToDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<Engineer> Engineers { get; }
    public IReadOnlyList<MaterialInfo> Materials { get; }
    public IReadOnlyList<Blueprint> Blueprints { get; }

    public Engineer? Engineer(string id) => _engineersById.GetValueOrDefault(id);
    public MaterialInfo? Material(string symbol) => _materialsBySymbol.GetValueOrDefault(symbol);
    public Blueprint? Blueprint(string id) => _blueprintsById.GetValueOrDefault(id);

    /// <summary>Look up an engineer by the exact in-game name the journal reports (case-insensitive).</summary>
    public Engineer? EngineerByName(string name)
        => Engineers.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static EngineeringCatalog Load()
    {
        var engineers = ReadResource<List<EngineerDto>>("engineers.json")
            .Select(d => new Engineer(d.Id, d.Name, d.System, d.Base, d.Unlock, d.Specialities ?? new())).ToList();
        var materials = ReadResource<List<MaterialInfo>>("materials.json");
        var blueprints = ReadResource<List<BlueprintDto>>("blueprints.json")
            .Select(d => new Blueprint(
                d.Id, d.Name, d.Module,
                (d.Grades ?? new()).Select(g => new BlueprintGrade(g.Grade, g.EngineerIds ?? new(), g.Ingredients ?? new())).ToList()))
            .ToList();
        return new EngineeringCatalog(engineers, materials, blueprints);
    }

    private static T ReadResource<T>(string fileName)
    {
        var asm = typeof(EngineeringCatalog).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded engineering resource '{fileName}' not found.");
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Engineering resource '{fileName}' deserialized to null.");
    }

    // DTOs decouple the JSON shape from the public records (e.g. "grade" vs BlueprintGrade.GradeValue).
    private sealed record EngineerDto(string Id, string Name, string System, string Base, string Unlock, List<string>? Specialities);
    private sealed record BlueprintDto(string Id, string Name, string Module, List<BlueprintGradeDto>? Grades);
    private sealed record BlueprintGradeDto(int Grade, List<string>? EngineerIds, List<string>? Ingredients);
}
