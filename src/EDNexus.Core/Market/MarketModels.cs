using EDNexus.Core.Colonisation;

namespace EDNexus.Core.Market;

/// <summary>
/// One commodity line in a station's market: the price the station will <see cref="BuyPrice"/> it
/// to the commander for, the price it will <see cref="SellPrice"/> pay when the commander offloads
/// it, the galactic <see cref="MeanPrice"/>, and the available <see cref="Stock"/> / <see cref="Demand"/>.
/// </summary>
/// <param name="Name">Localised display name, e.g. "Low Temperature Diamonds".</param>
/// <param name="Symbol">Canonical key (see <see cref="CommodityName"/>) for cross-referencing cargo.</param>
public sealed record MarketCommodity(
    string Name, string Symbol, string Category,
    int BuyPrice, int SellPrice, int MeanPrice,
    int Stock, int Demand, bool Rare)
{
    /// <summary>The station buys this from the commander — it can be offloaded here for credits.</summary>
    public bool Sellable => SellPrice > 0 && Demand > 0;

    /// <summary>The station sells this to the commander — it can be bought here.</summary>
    public bool Buyable => BuyPrice > 0 && Stock > 0;

    /// <summary>Sell price relative to the galactic mean (positive = a good place to offload).</summary>
    public int SellVsMean => SellPrice - MeanPrice;
}

/// <summary>
/// A market snapshot keyed by its market id: where the station is and every commodity line it
/// quoted. Instances are immutable; <see cref="MarketTracker"/> replaces them wholesale as new
/// <c>Market</c> events arrive (and folds <c>MarketBuy</c>/<c>MarketSell</c> deltas in between).
/// </summary>
public sealed class MarketSnapshot
{
    public required long MarketId { get; init; }

    public string? StationName { get; init; }
    public string? StarSystem { get; init; }
    public DateTimeOffset Updated { get; init; }

    public IReadOnlyList<MarketCommodity> Commodities { get; init; } = Array.Empty<MarketCommodity>();

    /// <summary>Commodities the station will buy from the commander, best sell price first.</summary>
    public IEnumerable<MarketCommodity> Sellable =>
        Commodities.Where(c => c.Sellable).OrderByDescending(c => c.SellPrice);

    /// <summary>Commodities the station sells to the commander, cheapest first.</summary>
    public IEnumerable<MarketCommodity> Buyable =>
        Commodities.Where(c => c.Buyable).OrderBy(c => c.BuyPrice);

    /// <summary>Find a commodity by any of its journal name forms, or null if this market doesn't list it.</summary>
    public MarketCommodity? Find(string? commodityName)
    {
        var key = CommodityName.Canonicalize(commodityName);
        return key.Length != 0 && BuildIndex().TryGetValue(key, out var c) ? c : null;
    }

    /// <summary>
    /// Value the current cargo hold against this market: every held commodity the station will buy,
    /// priced and totalled, most valuable line first. Commodities the station won't take are omitted.
    /// </summary>
    /// <param name="cargo">Cargo hold as commodity-name → tons, in any journal name form.</param>
    public IReadOnlyList<MarketSaleItem> ValuateHold(IReadOnlyDictionary<string, int>? cargo)
    {
        if (cargo is null) return Array.Empty<MarketSaleItem>();
        var index = BuildIndex();

        var hold = new Dictionary<string, int>();
        foreach (var (name, count) in cargo)
        {
            var key = CommodityName.Canonicalize(name);
            if (key.Length == 0 || count <= 0) continue;
            hold[key] = hold.TryGetValue(key, out var existing) ? existing + count : count;
        }

        var list = new List<MarketSaleItem>();
        foreach (var (key, units) in hold)
        {
            if (!index.TryGetValue(key, out var line) || line.SellPrice <= 0) continue;
            list.Add(new MarketSaleItem(line.Name, units, line.SellPrice, line.MeanPrice));
        }

        return list.OrderByDescending(i => i.Total).ToList();
    }

    /// <summary>
    /// Index the board by every canonical name form so a hold entry matches whether the game stored
    /// it as the internal symbol (<c>lowtemperaturediamond</c>) or the localised label
    /// (<c>Low Temperature Diamonds</c>) — the two don't canonicalise to the same key.
    /// </summary>
    private Dictionary<string, MarketCommodity> BuildIndex()
    {
        var index = new Dictionary<string, MarketCommodity>();
        foreach (var c in Commodities)
        {
            if (c.Symbol.Length != 0) index[c.Symbol] = c;
            var localised = CommodityName.Canonicalize(c.Name);
            if (localised.Length != 0) index.TryAdd(localised, c);
        }
        return index;
    }

    /// <summary>Total credits the current hold would fetch if fully sold here.</summary>
    public long HoldValue(IReadOnlyDictionary<string, int>? cargo) =>
        ValuateHold(cargo).Sum(i => i.Total);
}

/// <summary>
/// A hold-valuation row: <see cref="Units"/> tons of a commodity the market will buy at
/// <see cref="UnitPrice"/> each, and how that unit price compares to the galactic mean.
/// </summary>
public sealed record MarketSaleItem(string Name, int Units, int UnitPrice, int MeanPrice)
{
    /// <summary>Credits this line fetches if the whole quantity is sold.</summary>
    public long Total => (long)Units * UnitPrice;

    /// <summary>Unit price relative to the galactic mean (positive = above average).</summary>
    public int VsMean => UnitPrice - MeanPrice;
}
