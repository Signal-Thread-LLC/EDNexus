namespace EDNexus.Core.Exobio;

/// <summary>One organic species and what Vista Genomics pays for a completed sample of it.</summary>
/// <param name="Symbol">Journal codex symbol, e.g. <c>$Codex_Ent_Bacterial_05_Name;</c>.</param>
/// <param name="Value">Base payout in credits for a full three-sample analysis.</param>
/// <param name="SampleDistanceMeters">The genus's colony range — metres between samples. 0 when unknown.</param>
public sealed record BioSpecies(
    string GenusSymbol,
    string Genus,
    string Symbol,
    string Name,
    long Value,
    int SampleDistanceMeters = 0)
{
    /// <summary>
    /// What the sample pays if it is the first ever logged for this species. Being first pays a
    /// bonus of four times the base on top of the base itself — five times the value in total.
    /// </summary>
    public long FirstLoggedValue => Value * 5;
}

/// <summary>A genus and the species within it, for range estimates before a species is identified.</summary>
/// <param name="SampleDistanceMeters">
/// The genus's colony range: how far apart samples must be taken before the Artemis sampler accepts
/// the next one as genetically distinct.
/// </param>
public sealed record BioGenus(
    string Symbol,
    string Name,
    int SampleDistanceMeters,
    IReadOnlyList<BioSpecies> Species)
{
    public long MinValue => Species.Count == 0 ? 0 : Species.Min(s => s.Value);
    public long MaxValue => Species.Count == 0 ? 0 : Species.Max(s => s.Value);
}

/// <summary>Identifies a body across events: the journal reports these two on every bio event.</summary>
public readonly record struct BodyKey(long SystemAddress, int BodyId);

/// <summary>
/// Planetary environment physics captured from a journal <c>Scan</c> event — what the prediction
/// engine needs to guess which species can grow on a body before anyone lands.
/// </summary>
/// <param name="PlanetClass">Journal planet class, e.g. "High metal content body".</param>
/// <param name="Atmosphere">Journal atmosphere description, e.g. "thin carbon dioxide atmosphere". Empty for none.</param>
/// <param name="AtmosphereType">Journal atmosphere type, e.g. "CarbonDioxide". Null when the event omits it.</param>
/// <param name="SurfaceGravityG">Surface gravity in g (the journal reports m/s²). 0 when unknown.</param>
/// <param name="SurfaceTemperatureK">Surface temperature in kelvin. 0 when unknown.</param>
/// <param name="WasDiscovered">
/// Whether another commander had already scanned the body. An undiscovered body is the heuristic the
/// community tools use for the first-logged 5× bonus.
/// </param>
/// <param name="Volcanism">Journal volcanism description, e.g. "minor water magma volcanism". Null or empty for none.</param>
public sealed record BodyEnvironment(
    string PlanetClass,
    string Atmosphere,
    string? AtmosphereType,
    double SurfaceGravityG,
    double SurfaceTemperatureK,
    bool Landable,
    bool WasDiscovered,
    string? Volcanism = null);

/// <summary>
/// A candidate biological species predicted from the body's planetary environment.
/// </summary>
/// <param name="BaseValue">Vista Genomics base payout for the species.</param>
/// <param name="EstimatedValue">Expected payout: the base, or five times it when <paramref name="IsFirstDiscovery"/>.</param>
public sealed record BioPrediction(
    BioSpecies Species,
    BioGenus Genus,
    int SampleDistanceMeters,
    long BaseValue,
    long EstimatedValue,
    bool IsFirstDiscovery);

/// <summary>
/// What is known about one body's biology: how many signals the scanners reported, which
/// genera a surface (DSS) mapping revealed, and candidate species predicted from planetary physics.
/// </summary>
/// <param name="SignalCount">Biological signal count reported by FSS or DSS. 0 when none.</param>
/// <param name="Genera">Genera the DSS identified. Empty after an FSS-only pass.</param>
/// <param name="Predictions">Candidate species predicted from planetary physics, highest payout first.</param>
/// <param name="Environment">Planetary physics from the body's Scan event, if seen.</param>
public sealed record BodyBioSignals(
    BodyKey Key,
    string BodyName,
    int SignalCount,
    IReadOnlyList<BioGenus> Genera,
    bool Mapped,
    IReadOnlyList<BioPrediction>? Predictions = null,
    BodyEnvironment? Environment = null)
{
    public IReadOnlyList<BioPrediction> Predictions { get; init; } = Predictions ?? Array.Empty<BioPrediction>();

    /// <summary>Whether the body was undiscovered when scanned, earning the 5× first-discovery bonus.</summary>
    public bool IsUndiscovered => Environment?.WasDiscovered == false;

    /// <summary>
    /// Lowest and highest the body's signals could be worth. After a DSS pass this sums each named
    /// genus, narrowed to its predicted species when the body's physics are known. Before one, it
    /// takes the cheapest and richest <see cref="SignalCount"/> candidate genera from the prediction.
    /// Null when neither the genera nor the physics are known — a bare FSS count says nothing about value.
    /// </summary>
    public (long Min, long Max)? ValueRange
    {
        get
        {
            var byGenus = Predictions
                .GroupBy(p => p.Genus.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (Min: g.Min(p => p.EstimatedValue), Max: g.Max(p => p.EstimatedValue)),
                    StringComparer.OrdinalIgnoreCase);

            if (Genera.Count > 0)
            {
                long min = 0, max = 0;
                foreach (var genus in Genera)
                {
                    var (gMin, gMax) = byGenus.TryGetValue(genus.Symbol, out var r) ? r : (genus.MinValue, genus.MaxValue);
                    min += gMin;
                    max += gMax;
                }
                return (min, max);
            }

            if (byGenus.Count == 0 || SignalCount <= 0) return null;

            var n = Math.Min(SignalCount, byGenus.Count);
            return (
                byGenus.Values.Select(r => r.Min).OrderBy(v => v).Take(n).Sum(),
                byGenus.Values.Select(r => r.Max).OrderByDescending(v => v).Take(n).Sum());
        }
    }
}

/// <summary>
/// A species being sampled on a body. The Artemis suit takes three samples before the data is
/// complete and sellable; this tracks how far along that run is.
/// </summary>
/// <param name="Samples">Samples taken so far, 1–3.</param>
/// <param name="SampleDistanceMeters">Distance to move before the next sample counts, from the genus. 0 when the genus is unknown.</param>
public sealed record OrganicScan(
    BodyKey Key,
    string BodyName,
    BioSpecies? Species,
    string SpeciesName,
    string GenusName,
    int Samples,
    DateTimeOffset Updated,
    int SampleDistanceMeters = 0)
{
    /// <summary>The three-sample run is finished and the data can be sold.</summary>
    public bool Complete => Samples >= 3;

    /// <summary>"2/3" — the progression the commander sees on the suit.</summary>
    public string Progress => $"{Math.Min(Samples, 3)}/3";

    /// <summary>Base payout once complete, or 0 for a species the catalog doesn't know.</summary>
    public long Value => Species?.Value ?? 0;
}

/// <summary>
/// The running exobiology tally for this play session: what has been analysed but not yet sold,
/// and what selling has actually paid out.
/// </summary>
/// <param name="Pending">Completed scans not yet sold — the value riding on the current trip.</param>
/// <param name="SoldValue">Credits actually received from Vista Genomics this session.</param>
/// <param name="SoldBonus">First-logged bonuses included in <paramref name="SoldValue"/>.</param>
public sealed record ExobiologySession(
    IReadOnlyList<OrganicScan> Pending,
    long SoldValue,
    long SoldBonus,
    int SoldCount)
{
    /// <summary>Estimated credits sitting in the sampler, waiting for a Vista Genomics terminal.</summary>
    public long PendingValue => Pending.Sum(p => p.Value);
}
