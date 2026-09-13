using System.Collections.Concurrent;
using EDNexus.Core.Colonisation;
using EDNexus.Core.Trade;

namespace EDNexus.Core.Market;

/// <summary>
/// Fills in a galactic-mean price for commodities the game itself never quotes one for. Frontier only
/// attaches <c>MeanPrice</c> to station markets — a fleet carrier's buy/sell orders report it as 0 in
/// the journal — so this averages real station listings from <see cref="ITradeSearch"/> (Spansh) as a
/// stand-in. It's an estimate, not the fixed constant Frontier uses internally, but it converges close
/// enough to be useful and gets more accurate as more stations report the commodity over time.
/// </summary>
/// <remarks>
/// <see cref="TryGet"/> is synchronous and never blocks — callers on a UI tick need an instant answer —
/// so resolution happens in the background via <see cref="RequestEstimate"/>, and a caller simply asks
/// again on its next tick once the value exists. Once a commodity resolves it is never re-queried: the
/// underlying figure is a fixed per-commodity constant, so there's nothing to track drifting.
/// </remarks>
public sealed class CommodityMeanPriceEstimator
{
    /// <summary>
    /// A fixed, well-known hub rather than the commander's current system: the estimate is meant to be
    /// location-independent, and a stable query keeps the trade search's own on-disk cache reusable
    /// across sessions instead of re-querying every time the commander moves.
    /// </summary>
    private const string ReferenceSystem = "Sol";
    private const int SampleSize = 20;

    private readonly ITradeSearch _trade;
    private readonly ConcurrentDictionary<string, int> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();

    public CommodityMeanPriceEstimator(ITradeSearch trade)
    {
        _trade = trade;
    }

    /// <summary>The estimate for this commodity, if one has resolved yet. Never triggers a lookup itself.</summary>
    public int? TryGet(string symbol)
    {
        var key = CommodityName.Canonicalize(symbol);
        return key.Length != 0 && _cache.TryGetValue(key, out var price) ? price : null;
    }

    /// <summary>
    /// Kick off a background estimate for <paramref name="displayName"/> if none is cached and none is
    /// already in flight. Fire-and-forget by design: the caller's next tick picks up the result once
    /// <see cref="TryGet"/> starts returning it. Failures are silent — the commodity just stays
    /// unresolved and the next request tries again.
    /// </summary>
    public void RequestEstimate(string symbol, string displayName)
    {
        var key = CommodityName.Canonicalize(symbol);
        if (key.Length == 0 || _cache.ContainsKey(key) || !_inFlight.TryAdd(key, 0)) return;

        _ = Task.Run(async () =>
        {
            try { await ResolveAsync(symbol, displayName).ConfigureAwait(false); }
            catch { /* best-effort: leave it uncached so the next request tries again */ }
            finally { _inFlight.TryRemove(key, out _); }
        });
    }

    /// <summary>
    /// Query and average real station listings for <paramref name="displayName"/>, caching the result
    /// under its canonical symbol. Exposed directly (as well as through <see cref="RequestEstimate"/>)
    /// so callers that can await — tests included — don't need to poll <see cref="TryGet"/>.
    /// </summary>
    public async Task<int?> ResolveAsync(string symbol, string displayName, CancellationToken ct = default)
    {
        var key = CommodityName.Canonicalize(symbol);
        if (key.Length == 0) return null;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var quotes = await _trade.SearchAsync(
            new TradeQuery(displayName, ReferenceSystem, TradeMode.Sell, SampleSize), ct).ConfigureAwait(false);
        var prices = quotes.Select(q => q.Price).Where(p => p > 0).ToList();
        if (prices.Count == 0) return null;

        var mean = (int)Math.Round(prices.Average());
        _cache[key] = mean;
        return mean;
    }
}
