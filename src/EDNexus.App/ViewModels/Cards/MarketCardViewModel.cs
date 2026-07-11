using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>The docked station's commodity market, valued against the current hold.</summary>
public sealed partial class MarketCardViewModel : CardViewModel
{
    private string _signature = "";

    public MarketCardViewModel(DashboardContext context) : base(context, "market", "MARKET", 920) { }

    [ObservableProperty] private bool _hasMarket;
    [ObservableProperty] private string _marketTitle = "—";
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
        MarketTitle = snap.StationName ?? snap.StarSystem ?? "Station market";
        var sellable = snap.Commodities.Count(c => c.Sellable);
        MarketSummary = $"{snap.Commodities.Count} commodities · {sellable} the station buys";

        var valuation = snap.ValuateHold(s.Cargo);
        MarketHoldValue = valuation.Count > 0 ? $"{snap.HoldValue(s.Cargo):N0} cr" : "—";

        // Prefer valuing the hold; when nothing aboard sells here, fall back to the station's best sells.
        var signature = valuation.Count > 0
            ? "hold|" + snap.MarketId + "|" + string.Join("|", valuation.Select(i => $"{i.Name}:{i.Units}:{i.UnitPrice}"))
            : "sells|" + snap.MarketId + "|" + string.Join("|", snap.Sellable.Take(12).Select(c => $"{c.Name}:{c.SellPrice}:{c.Demand}"));
        if (signature == _signature) return;
        _signature = signature;

        MarketRows.Clear();
        if (valuation.Count > 0)
        {
            MarketListHeader = "YOUR HOLD, SOLD HERE";
            foreach (var i in valuation)
                MarketRows.Add(new MarketLine(
                    i.Name, $"{i.Units:N0} t", $"{i.UnitPrice:N0} cr", $"{i.Total:N0} cr", FormatVsMean(i.VsMean), i.VsMean >= 0));
        }
        else
        {
            MarketListHeader = "BEST SELLS HERE";
            foreach (var c in snap.Sellable.Take(12))
                MarketRows.Add(new MarketLine(
                    c.Name, $"{c.Demand:N0} dmd", $"{c.SellPrice:N0} cr", "", FormatVsMean(c.SellVsMean), c.SellVsMean >= 0));
        }
    }

    public override void Reset()
    {
        _signature = "";
        MarketRows.Clear();
    }

    private static string FormatVsMean(int vsMean) => vsMean >= 0 ? $"+{vsMean:N0}" : $"−{Math.Abs(vsMean):N0}";
}

/// <summary>
/// One row on the market card: a commodity with a <see cref="Qty"/> (tons in hold, or station
/// demand), the station's <see cref="Unit"/> price, an optional line <see cref="Total"/>, and how
/// the price compares to the galactic mean (<see cref="VsMean"/>, coloured by <see cref="Good"/>).
/// </summary>
public sealed record MarketLine(
    string Name, string Qty, string Unit, string Total, string VsMean, bool Good)
{
    /// <summary>Inverse of <see cref="Good"/>, so the XAML can colour a below-mean price without a converter.</summary>
    public bool Bad => !Good;
}
