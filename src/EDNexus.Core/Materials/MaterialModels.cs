using EDNexus.Core.Engineering;

namespace EDNexus.Core.Materials;

/// <summary>
/// One material joined to what the commander is carrying: the reference entry plus the held count,
/// the cap it counts against, and how close to full it is.
/// </summary>
public sealed record MaterialStock(MaterialInfo Info, int Held)
{
    public string Symbol => Info.Symbol;
    public string Name => Info.Name;
    public string Category => Info.Category;
    public int Grade => Info.Grade;
    public int Cap => Info.Cap;

    /// <summary>Room left before the cap rejects further pickups. Never negative.</summary>
    public int Spare => Math.Max(0, Cap - Held);

    /// <summary>Fill level, 0–1, for a progress bar.</summary>
    public double Fraction => Cap == 0 ? 0 : Math.Clamp((double)Held / Cap, 0, 1);

    /// <summary>
    /// At the cap, so the game silently discards further pickups of it — the thing a commander
    /// most wants flagged, because it turns farming runs into wasted time.
    /// </summary>
    public bool IsFull => Held >= Cap;
}

/// <summary>A material inventory category, with its stocks and how full it is overall.</summary>
public sealed record MaterialCategoryView(
    string Category,
    IReadOnlyList<MaterialStock> Stocks)
{
    /// <summary>Materials actually held (count &gt; 0), richest first.</summary>
    public IReadOnlyList<MaterialStock> Held { get; } =
        Stocks.Where(s => s.Held > 0).OrderByDescending(s => s.Grade).ThenByDescending(s => s.Held).ToList();

    public int TotalHeld => Stocks.Sum(s => s.Held);
    public int FullCount => Stocks.Count(s => s.IsFull);
}

/// <summary>
/// What a material trader would charge to obtain <see cref="Wanted"/> units of <see cref="Target"/>
/// by handing over <see cref="Source"/>.
/// </summary>
/// <param name="Cost">Units of the source material consumed. 0 when the trade is impossible.</param>
/// <param name="Affordable">Whether the commander currently holds <see cref="Cost"/> of the source.</param>
public sealed record TradeQuote(
    MaterialInfo Source,
    MaterialInfo Target,
    int Wanted,
    int Cost,
    bool Affordable,
    string? Impossible = null)
{
    /// <summary>True when a trader can do this swap at all (same category, and a rate exists).</summary>
    public bool Possible => Impossible is null;
}
