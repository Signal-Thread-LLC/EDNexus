namespace EDNexus.Core.Trade;

/// <summary>Which side of a market the search is after.</summary>
public enum TradeMode
{
    /// <summary>Stations that <b>buy</b> the commodity from the commander (offload the hold).</summary>
    Sell,

    /// <summary>Stations that <b>sell</b> the commodity to the commander (source cargo).</summary>
    Buy,
}

/// <summary>
/// A request for the best nearby markets for one commodity: the commodity name in any form, the
/// system to measure distance from, and which side of the trade is wanted.
/// </summary>
/// <param name="Commodity">Commodity name in any journal form (symbol or localised label).</param>
/// <param name="ReferenceSystem">System to sort/measure distance from — usually the current system.</param>
public sealed record TradeQuery(
    string Commodity,
    string ReferenceSystem,
    TradeMode Mode = TradeMode.Sell,
    int MaxResults = 10);

/// <summary>
/// One ranked result: a station, how far its system is from the reference, the quoted
/// <see cref="Price"/>, and the available <see cref="Quantity"/> (demand when selling, supply when
/// buying). <see cref="MarketUpdated"/> is when the backing dataset last saw this station's market.
/// </summary>
public sealed record TradeStationQuote(
    string System,
    string Station,
    double DistanceLy,
    int Price,
    int Quantity,
    DateTimeOffset? MarketUpdated)
{
    /// <summary>Age of the price data, if the source reported when it was last updated.</summary>
    public TimeSpan? Age(DateTimeOffset now) =>
        MarketUpdated is { } t ? now - t : null;
}

/// <summary>
/// A market data provider that answers "where can I buy/sell this commodity near here". Backed by an
/// external aggregator (Spansh); implementations are expected to be network-bound and cancellable.
/// </summary>
public interface ITradeSearch
{
    /// <summary>Human-readable name of the backing data source, e.g. "Spansh".</summary>
    string SourceName { get; }

    /// <summary>
    /// Return the best matching stations for <paramref name="query"/>, nearest first. An empty list
    /// means no station matched — or the lookup failed transiently; the backing client never throws
    /// for network/HTTP problems, matching the reporting clients' convention.
    /// </summary>
    Task<IReadOnlyList<TradeStationQuote>> SearchAsync(TradeQuery query, CancellationToken ct = default);
}
