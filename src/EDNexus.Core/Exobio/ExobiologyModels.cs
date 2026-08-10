namespace EDNexus.Core.Exobio;

/// <summary>One organic species and what Vista Genomics pays for a completed sample of it.</summary>
/// <param name="Symbol">Journal codex symbol, e.g. <c>$Codex_Ent_Bacterial_05_Name;</c>.</param>
/// <param name="Value">Base payout in credits for a full three-sample analysis.</param>
public sealed record BioSpecies(
    string GenusSymbol,
    string Genus,
    string Symbol,
    string Name,
    long Value)
{
    /// <summary>
    /// What the sample pays if it is the first ever logged for this species. Being first pays a
    /// bonus of four times the base on top of the base itself — five times the value in total.
    /// </summary>
    public long FirstLoggedValue => Value * 5;
}

/// <summary>A genus and the species within it, for range estimates before a species is identified.</summary>
public sealed record BioGenus(string Symbol, string Name, IReadOnlyList<BioSpecies> Species)
{
    public long MinValue => Species.Count == 0 ? 0 : Species.Min(s => s.Value);
    public long MaxValue => Species.Count == 0 ? 0 : Species.Max(s => s.Value);
}

/// <summary>Identifies a body across events: the journal reports these two on every bio event.</summary>
public readonly record struct BodyKey(long SystemAddress, int BodyId);

/// <summary>
/// What is known about one body's biology: how many signals the scanners reported, and which
/// genera a surface (DSS) mapping revealed. An FSS pass gives the count alone; only the DSS
/// names the genera.
/// </summary>
/// <param name="SignalCount">Biological signal count reported by FSS or DSS. 0 when none.</param>
/// <param name="Genera">Genera the DSS identified. Empty after an FSS-only pass.</param>
public sealed record BodyBioSignals(
    BodyKey Key,
    string BodyName,
    int SignalCount,
    IReadOnlyList<BioGenus> Genera,
    bool Mapped)
{
    /// <summary>
    /// Lowest and highest the body's signals could be worth, from the genera the DSS named. Null
    /// until a DSS pass identifies them — an FSS count alone says nothing about value.
    /// </summary>
    public (long Min, long Max)? ValueRange => Genera.Count == 0
        ? null
        : (Genera.Sum(g => g.MinValue), Genera.Sum(g => g.MaxValue));
}

/// <summary>
/// A species being sampled on a body. The Artemis suit takes three samples before the data is
/// complete and sellable; this tracks how far along that run is.
/// </summary>
/// <param name="Samples">Samples taken so far, 1–3.</param>
public sealed record OrganicScan(
    BodyKey Key,
    string BodyName,
    BioSpecies? Species,
    string SpeciesName,
    string GenusName,
    int Samples,
    DateTimeOffset Updated)
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
