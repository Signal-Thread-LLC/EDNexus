using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EDNexus.Core.Market;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>The docked station's commodity market, valued against the current hold.</summary>
public sealed partial class MarketCardViewModel : CardViewModel
{
    private string _signature = "";

    public MarketCardViewModel(DashboardContext context) : base(context, "market", "MARKET", 920) { }

    [ObservableProperty] private bool _hasMarket;
    [ObservableProperty] private string _marketTitle = "—";
    /// <summary>Callsign hint for a carrier title; null everywhere else so no empty tooltip pops up.</summary>
    [ObservableProperty] private string? _marketTitleTip;
    [ObservableProperty] private string _marketSummary = "";
    [ObservableProperty] private string _marketHoldValue = "—";
    [ObservableProperty] private string _marketListHeader = "";

    public ObservableCollection<MarketLine> MarketRows { get; } = new();

    /// <summary>Inverse of <see cref="HasMarket"/>, for the empty-state hint's visibility.</summary>
    public bool NoMarket => !HasMarket;

    partial void OnHasMarketChanged(bool value) => OnPropertyChanged(nameof(NoMarket));

    public override void Update(CommanderState s)
    {
        var snap = Context.Host.Market.Current;
        if (snap is null)
        {
            if (HasMarket) { HasMarket = false; MarketRows.Clear(); _signature = ""; }
            return;
        }

        HasMarket = true;
        SetTitle(snap.StationName, snap.StarSystem, s);
        var sellable = snap.Commodities.Count(c => c.Sellable);
        MarketSummary = $"{snap.Commodities.Count} commodities · {sellable} the station buys";

        var valuation = snap.ValuateHold(s.Cargo);
        MarketHoldValue = valuation.Count > 0 ? $"{snap.HoldValue(s.Cargo):N0} cr" : "—";

        // Fleet carriers don't get a real galactic-average price from the game (Market.json reports
        // MeanPrice as 0 for carrier orders), so fall back to whatever a real station has taught us
        // for that commodity, then to a Spansh-derived estimate, rather than showing the raw price as
        // if it were the delta.
        var known = Context.GetMiningSettings().KnownPrices;
        var estimator = Context.DevEnabled ? null : Context.Host.CommodityMeanPrices;

        var rows = valuation.Count > 0
            ? valuation.Select(i =>
              {
                  var (text, good) = ResolveVsMean(i.UnitPrice, i.MeanPrice, i.Symbol, i.Name, known, estimator);
                  return new MarketLine(i.Name, $"{i.Units:N0} t", $"{i.UnitPrice:N0} cr", $"{i.Total:N0} cr", text, good);
              }).ToList()
            : snap.Sellable.Take(12).Select(c =>
              {
                  var (text, good) = ResolveVsMean(c.SellPrice, c.MeanPrice, c.Symbol, c.Name, known, estimator);
                  return new MarketLine(c.Name, $"{c.Demand:N0} dmd", $"{c.SellPrice:N0} cr", "", text, good);
              }).ToList();

        // The resolved VsMean text is folded into the signature (not just price/units) so a background
        // estimate landing after this tick still invalidates the cache and refreshes the row.
        var signature = (valuation.Count > 0 ? "hold|" : "sells|") + snap.MarketId + "|" +
            string.Join("|", rows.Select(r => $"{r.Name}:{r.Qty}:{r.Unit}:{r.Total}:{r.VsMean}"));
        if (signature == _signature) return;
        _signature = signature;

        MarketListHeader = valuation.Count > 0 ? "YOUR HOLD, SOLD HERE" : "BEST SELLS HERE";
        MarketRows.Clear();
        foreach (var row in rows) MarketRows.Add(row);
    }

    /// <summary>No known galactic mean (typically a carrier order this build hasn't seen at a real
    /// station or resolved via Spansh yet) renders as a plain dash rather than a misleading "delta"
    /// that's really the full price. <paramref name="estimator"/> is null in developer mode, where
    /// sample data always carries a real mean anyway and nothing should ever hit the network.</summary>
    private static (string Text, bool? Good) ResolveVsMean(
        int price, int meanPrice, string symbol, string displayName,
        IReadOnlyDictionary<string, int> known, CommodityMeanPriceEstimator? estimator)
    {
        if (meanPrice > 0) return FormatVsMean(price, meanPrice);
        if (known.TryGetValue(symbol, out var kp) && kp > 0) return FormatVsMean(price, kp);

        if (estimator is not null)
        {
            if (estimator.TryGet(symbol) is { } estimate) return FormatVsMean(price, estimate);
            estimator.RequestEstimate(symbol, displayName);
        }

        return ("—", null);
    }

    /// <summary>
    /// Name the market's station the way the commander does. A fleet carrier reports only its
    /// callsign, so substitute the carrier's name when it is theirs — and pair it with the system,
    /// since unlike a station a carrier moves and "which one is this" means "where is it now".
    /// The callsign stays reachable in the tooltip.
    /// </summary>
    private void SetTitle(string? stationName, string? starSystem, CommanderState s)
    {
        if (StationDisplay.IsOwnCarrier(stationName, s.CarrierCallsign))
        {
            var name = StationDisplay.Resolve(stationName, s.CarrierName, s.CarrierCallsign);
            MarketTitle = starSystem is { Length: > 0 } sys ? $"{name} ({sys})" : name ?? "Station market";
            MarketTitleTip = $"Fleet carrier {stationName}";
            return;
        }

        MarketTitle = stationName ?? starSystem ?? "Station market";
        MarketTitleTip = null;
    }

    public override void Reset()
    {
        _signature = "";
        MarketRows.Clear();
    }

    private static (string Text, bool? Good) FormatVsMean(int price, int meanPrice)
    {
        if (meanPrice <= 0) return ("—", null);
        var diff = price - meanPrice;
        return (diff >= 0 ? $"+{diff:N0}" : $"−{Math.Abs(diff):N0}", diff >= 0);
    }
}

/// <summary>
/// One row on the market card: a commodity with a <see cref="Qty"/> (tons in hold, or station
/// demand), the station's <see cref="Unit"/> price, an optional line <see cref="Total"/>, and how
/// the price compares to the galactic mean (<see cref="VsMean"/>, coloured by <see cref="Good"/> —
/// null when no galactic mean is known at all, e.g. an unseen carrier commodity).
/// </summary>
public sealed record MarketLine(
    string Name, string Qty, string Unit, string Total, string VsMean, bool? IsGood)
{
    /// <summary>Above the galactic mean — colour it favourably.</summary>
    public bool Good => IsGood == true;

    /// <summary>Below the galactic mean.</summary>
    public bool Bad => IsGood == false;

    /// <summary>No galactic mean known for this commodity yet (e.g. an unseen carrier order).</summary>
    public bool Unknown => IsGood is null;
}
