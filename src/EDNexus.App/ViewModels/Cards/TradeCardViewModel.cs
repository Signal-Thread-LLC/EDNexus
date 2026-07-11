using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.Core.Dev;
using EDNexus.Core.State;
using EDNexus.Core.Trade;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Trade finder: the best nearby stations to sell a commodity from the hold, or to source one.
/// Backed by the live Spansh search, or an offline sample generator while developer mode is on.
/// </summary>
public sealed partial class TradeCardViewModel : CardViewModel
{
    private SampleTradeSearch? _sampleTrade;

    public TradeCardViewModel(DashboardContext context) : base(context, "trade", "TRADE FINDER", 452) { }

    /// <summary>No dev-mode sample source feeds this card, so it has no 🎲 reshuffle.</summary>
    public override bool CanRandomize => false;

    private ITradeSearch Trader => Context.DevEnabled ? _sampleTrade ??= new SampleTradeSearch(Context.Rng) : Context.Host.Trade;

    [ObservableProperty] private string _tradeCommodity = "";
    [ObservableProperty] private string _tradeReferenceSystem = "";
    [ObservableProperty] private bool _tradeSelling = true;   // true = offload the hold; false = source cargo
    [ObservableProperty] private string _tradeStatus = "";
    [ObservableProperty] private bool _tradeBusy;
    [ObservableProperty] private bool _tradeHasResults;

    public ObservableCollection<TradeResultLine> TradeResults { get; } = new();

    /// <summary>
    /// Pre-fill the reference system and commodity from live state, but only while a field is still
    /// empty — never clobber what the commander has typed.
    /// </summary>
    public override void Update(CommanderState s)
    {
        if (TradeReferenceSystem.Length == 0 && s.StarSystem is { Length: > 0 } sys) TradeReferenceSystem = sys;
        if (TradeCommodity.Length == 0 && !s.Cargo.IsEmpty)
            TradeCommodity = s.Cargo.OrderByDescending(k => k.Value).First().Key;
    }

    [RelayCommand]
    private async Task SearchTrade()
    {
        var commodity = TradeCommodity.Trim();
        var reference = TradeReferenceSystem.Trim();
        if (commodity.Length == 0)
        {
            TradeStatus = "Enter a commodity to look up.";
            return;
        }
        if (reference.Length == 0)
        {
            TradeStatus = "Enter the system to search from (usually your current one).";
            return;
        }

        var mode = TradeSelling ? TradeMode.Sell : TradeMode.Buy;
        TradeBusy = true;
        TradeHasResults = false;
        TradeResults.Clear();
        TradeStatus = TradeSelling ? $"Best places to sell {commodity} near {reference} …"
                                   : $"Best places to buy {commodity} near {reference} …";
        try
        {
            var quotes = await Trader.SearchAsync(new TradeQuery(commodity, reference, mode), CancellationToken.None);
            if (quotes.Count == 0)
            {
                TradeStatus = $"No station found {(TradeSelling ? "buying" : "selling")} {commodity} nearby.";
                return;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var q in quotes)
                TradeResults.Add(new TradeResultLine(
                    q.System, q.Station,
                    $"{q.DistanceLy:0.0} ly",
                    $"{q.Price:N0} cr",
                    q.Quantity > 0 ? $"{q.Quantity:N0} {(TradeSelling ? "dmd" : "sup")}" : "",
                    HumanizeAge(q.Age(now))));

            TradeHasResults = true;
            TradeStatus = $"{quotes.Count} stations · via {Trader.SourceName}";
        }
        catch (Exception ex)
        {
            TradeStatus = "Trade search failed: " + ex.Message;
        }
        finally
        {
            TradeBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopySystem(string? system)
    {
        if (string.IsNullOrWhiteSpace(system)) return;
        await CopyToClipboardAsync(system);
        TradeStatus = $"Copied “{system}”.";
    }

    private static string HumanizeAge(TimeSpan? age) => age switch
    {
        null => "",
        { TotalMinutes: < 60 } a => $"{a.TotalMinutes:0}m ago",
        { TotalHours: < 24 } a => $"{a.TotalHours:0}h ago",
        var a => $"{a.Value.TotalDays:0}d ago",
    };
}

/// <summary>
/// One row on the trade card: a station, how far its system is from the reference, the quoted price,
/// the available quantity (demand when selling, supply when buying), and how stale the price is.
/// </summary>
public sealed record TradeResultLine(
    string System, string Station, string Distance, string Price, string Quantity, string Age);
