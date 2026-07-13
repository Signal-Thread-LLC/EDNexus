using System.Text.Json;
using System.Text.Json.Serialization;

namespace EDNexus.Core.Odyssey;

/// <summary>
/// The static Odyssey (on-foot) reference data — suits, weapons, modifications, on-foot materials and
/// their engineers — loaded once from the embedded JSON resources and indexed for lookup. Mirrors
/// <see cref="Engineering.EngineeringCatalog"/>'s shape for the ship side.
/// </summary>
public sealed class OdysseyCatalog
{
    private static readonly Lazy<OdysseyCatalog> Lazy = new(Load);

    /// <summary>Shared, lazily-loaded catalog. Reference data is immutable, so one copy is safe to share.</summary>
    public static OdysseyCatalog Default => Lazy.Value;

    private readonly Dictionary<string, Suit> _suitsById;
    private readonly Dictionary<string, Weapon> _weaponsById;
    private readonly Dictionary<string, OnFootMaterial> _materialsBySymbol;
    private readonly Dictionary<string, Modification> _suitModsById;
    private readonly Dictionary<string, Modification> _weaponModsById;
    private readonly Dictionary<string, OdysseyEngineer> _engineersById;

    private OdysseyCatalog(
        IReadOnlyList<Suit> suits, IReadOnlyList<Weapon> weapons, IReadOnlyList<OnFootMaterial> materials,
        IReadOnlyList<Modification> suitMods, IReadOnlyList<Modification> weaponMods, IReadOnlyList<OdysseyEngineer> engineers)
    {
        Suits = suits;
        Weapons = weapons;
        Materials = materials;
        SuitMods = suitMods;
        WeaponMods = weaponMods;
        Engineers = engineers;
        _suitsById = suits.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        _weaponsById = weapons.ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);
        _materialsBySymbol = materials.ToDictionary(m => m.Symbol, StringComparer.OrdinalIgnoreCase);
        _suitModsById = suitMods.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        _weaponModsById = weaponMods.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        _engineersById = engineers.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<Suit> Suits { get; }
    public IReadOnlyList<Weapon> Weapons { get; }
    public IReadOnlyList<OnFootMaterial> Materials { get; }
    public IReadOnlyList<Modification> SuitMods { get; }
    public IReadOnlyList<Modification> WeaponMods { get; }
    public IReadOnlyList<OdysseyEngineer> Engineers { get; }

    public Suit? Suit(string id) => _suitsById.GetValueOrDefault(id);
    public Weapon? Weapon(string id) => _weaponsById.GetValueOrDefault(id);
    public OnFootMaterial? Material(string symbol) => _materialsBySymbol.GetValueOrDefault(symbol);
    public Modification? SuitMod(string id) => _suitModsById.GetValueOrDefault(id);
    public Modification? WeaponMod(string id) => _weaponModsById.GetValueOrDefault(id);
    public OdysseyEngineer? Engineer(string id) => _engineersById.GetValueOrDefault(id);

    /// <summary>Find the suit whose journal <c>SuitName</c> prefix matches (e.g. "tacticalsuit_class3" → Dominator).</summary>
    public Suit? SuitBySymbolPrefix(string suitSymbol)
        => Suits.FirstOrDefault(s => suitSymbol.StartsWith(s.SuitSymbol, StringComparison.OrdinalIgnoreCase));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static OdysseyCatalog Load()
    {
        var suits = ReadResource<List<SuitDto>>("suits.json")
            .Select(d => new Suit(d.Id, d.Name, d.SuitSymbol, d.ModSlotsByGrade ?? new(), MapSteps(d.GradeSteps))).ToList();
        var weapons = ReadResource<List<WeaponDto>>("weapons.json")
            .Select(d => new Weapon(d.Id, d.Name, d.ModSlotsByGrade ?? new(), MapSteps(d.GradeSteps))).ToList();
        var materials = ReadResource<List<OnFootMaterial>>("onfoot-materials.json");
        var suitMods = ReadResource<List<ModDto>>("suit-mods.json").Select(MapMod).ToList();
        var weaponMods = ReadResource<List<ModDto>>("weapon-mods.json").Select(MapMod).ToList();
        var engineers = ReadResource<List<OdysseyEngineer>>("odyssey-engineers.json");
        return new OdysseyCatalog(suits, weapons, materials, suitMods, weaponMods, engineers);
    }

    private static List<GradeStep> MapSteps(List<GradeStepDto>? steps) =>
        (steps ?? new()).Select(s => new GradeStep(
            s.Grade, s.Credits,
            (s.Materials ?? new()).Select(m => new MaterialCost(m.Symbol, m.Count)).ToList(),
            s.CreditsEstimated)).ToList();

    private static Modification MapMod(ModDto d) => new(
        d.Id, d.Name, d.AppliesTo, d.Effect, d.EngineerIds ?? new(), d.Credits,
        (d.Materials ?? new()).Select(m => new MaterialCost(m.Symbol, m.Count)).ToList());

    private static T ReadResource<T>(string fileName)
    {
        var asm = typeof(OdysseyCatalog).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded Odyssey resource '{fileName}' not found.");
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Odyssey resource '{fileName}' deserialized to null.");
    }

    // DTOs decouple the JSON shape from the public records.
    private sealed record SuitDto(string Id, string Name, string SuitSymbol, List<int>? ModSlotsByGrade, List<GradeStepDto>? GradeSteps);
    private sealed record WeaponDto(string Id, string Name, List<int>? ModSlotsByGrade, List<GradeStepDto>? GradeSteps);
    private sealed record GradeStepDto(int Grade, long Credits, List<MaterialCostDto>? Materials, bool CreditsEstimated = false);
    private sealed record MaterialCostDto(string Symbol, int Count);
    private sealed record ModDto(string Id, string Name, string AppliesTo, string Effect, List<string>? EngineerIds, long Credits, List<MaterialCostDto>? Materials);
}
