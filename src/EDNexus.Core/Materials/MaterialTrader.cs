using EDNexus.Core.Engineering;

namespace EDNexus.Core.Materials;

/// <summary>
/// Works out what a material trader charges to swap one material for another.
/// </summary>
/// <remarks>
/// Traders deal in one category only — a raw trader will not touch encoded data — and price every
/// swap by multiplying three independent factors:
/// <list type="bullet">
///   <item>crossing to a different group within the category costs 6 for 1;</item>
///   <item>each grade climbed costs 6 for 1;</item>
///   <item>each grade dropped pays out 3 for 1.</item>
/// </list>
/// So grade 3 in one group down to grade 2 in another is 6 ÷ 3 = 2 source units per unit received.
/// Fractional results are rounded up: the trader deals in whole units.
/// </remarks>
public static class MaterialTrader
{
    private const int CrossGroupRate = 6;
    private const int UpgradeRate = 6;
    private const int DowngradeRate = 3;

    /// <summary>
    /// Price <paramref name="wanted"/> units of <paramref name="target"/> paid for in
    /// <paramref name="source"/>, given how many of the source the commander
    /// <paramref name="held"/>.
    /// </summary>
    public static TradeQuote Quote(MaterialInfo source, MaterialInfo target, int wanted, int held)
    {
        if (wanted <= 0)
            return new TradeQuote(source, target, wanted, 0, true, "Ask for at least one unit.");

        if (string.Equals(source.Symbol, target.Symbol, StringComparison.OrdinalIgnoreCase))
            return new TradeQuote(source, target, wanted, 0, true, "That is already the material you have.");

        if (!string.Equals(source.Category, target.Category, StringComparison.OrdinalIgnoreCase))
            return new TradeQuote(source, target, wanted, 0, false,
                $"No trader swaps {source.Category.ToLowerInvariant()} for {target.Category.ToLowerInvariant()}.");

        var cost = (int)Math.Ceiling(wanted * RatePerUnit(source, target));
        return new TradeQuote(source, target, wanted, cost, held >= cost);
    }

    /// <summary>How many units of <paramref name="source"/> buy a single unit of <paramref name="target"/>.</summary>
    public static double RatePerUnit(MaterialInfo source, MaterialInfo target)
    {
        double rate = SameGroup(source, target) ? 1 : CrossGroupRate;

        var steps = target.Grade - source.Grade;
        for (var i = 0; i < steps; i++) rate *= UpgradeRate;        // climbing costs
        for (var i = 0; i < -steps; i++) rate /= DowngradeRate;     // dropping pays out

        return rate;
    }

    private static bool SameGroup(MaterialInfo a, MaterialInfo b)
        => a.Group.Length > 0 && string.Equals(a.Group, b.Group, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The cheapest materials to hand over for <paramref name="wanted"/> units of
    /// <paramref name="target"/>, considering only what the commander actually holds enough of.
    /// Cheapest first, so the top row is the trade to make.
    /// </summary>
    public static IReadOnlyList<TradeQuote> BestSources(
        MaterialInfo target, int wanted, IReadOnlyDictionary<string, int> held, int limit = 5)
    {
        var quotes = new List<TradeQuote>();
        foreach (var candidate in EngineeringCatalog.Default.Materials)
        {
            if (string.Equals(candidate.Symbol, target.Symbol, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(candidate.Category, target.Category, StringComparison.OrdinalIgnoreCase)) continue;

            var have = held.GetValueOrDefault(candidate.Symbol);
            if (have <= 0) continue;

            var quote = Quote(candidate, target, wanted, have);
            if (quote.Possible && quote.Affordable) quotes.Add(quote);
        }

        return quotes
            .OrderBy(q => q.Cost)
            .ThenBy(q => q.Source.Grade)   // at equal cost, spend commons rather than rares
            .Take(limit)
            .ToList();
    }
}
