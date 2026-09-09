namespace EDNexus.Core.Mining;

/// <summary>
/// One material a prospector limpet found in an asteroid, and how much of the rock it makes up.
/// </summary>
/// <param name="Name">Display name (localised where the journal gives one).</param>
/// <param name="Symbol">Canonical key (see <see cref="Colonisation.CommodityName"/>) for pricing lookups.</param>
/// <param name="Proportion">Percentage (0–100) of the asteroid's mass this material accounts for.</param>
public sealed record ProspectedMaterial(string Name, string Symbol, double Proportion);

/// <summary>
/// One <c>ProspectedAsteroid</c> result: everything the limpet found in that rock. The journal draws
/// no distinction between a laser-minable surface deposit and one exposed by a sub-surface displacement
/// missile — both simply appear in <see cref="Materials"/> — so the one distinction EDNexus can make
/// from the data is core mining (<see cref="HasMotherlode"/>) versus everything else.
/// </summary>
/// <param name="Content">Overall material-content quality: "Low", "Medium", "High", or "" if unknown.</param>
/// <param name="Remaining">Percentage (0–100) of the asteroid's mass left to mine.</param>
public sealed record ProspectResult(
    DateTimeOffset Timestamp,
    IReadOnlyList<ProspectedMaterial> Materials,
    string? MotherlodeName,
    string? MotherlodeSymbol,
    string Content,
    double Remaining)
{
    /// <summary>True when this rock has a deep-core seam requiring seismic charges to crack open.</summary>
    public bool HasMotherlode => !string.IsNullOrEmpty(MotherlodeSymbol);
}

/// <summary>
/// One <c>MiningRefined</c> event: the refinery finished converting fragments into a single unit
/// (one tonne) of cargo. The journal fires one of these per unit, with no quantity field.
/// </summary>
public sealed record RefinedUnit(DateTimeOffset Timestamp, string Symbol, string Name);
