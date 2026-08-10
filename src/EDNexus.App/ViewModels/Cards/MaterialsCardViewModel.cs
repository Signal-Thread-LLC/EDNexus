using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.Core.Engineering;
using EDNexus.Core.Materials;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Material stocks. Shows one category at a time — every material in it with its grade, how full
/// it is against the per-grade cap, and which ones have hit that cap. Selecting a row asks the
/// material trader what it would cost to top that material up from what the commander already has.
/// </summary>
public sealed partial class MaterialsCardViewModel : CardViewModel
{
    private string _signature = "";
    private string? _selectedSymbol;

    public MaterialsCardViewModel(DashboardContext context) : base(context, "materials", "MATERIALS", 452) { }

    public string[] CategoryChoices { get; } = MaterialInventory.Categories.ToArray();

    [ObservableProperty] private string _selectedCategory = "Raw";

    [ObservableProperty] private string _rawMaterials = "0";
    [ObservableProperty] private string _manufacturedMaterials = "0";
    [ObservableProperty] private string _encodedMaterials = "0";
    [ObservableProperty] private string _materialsSummary = "—";
    [ObservableProperty] private string _capWarning = "";
    [ObservableProperty] private bool _hasCapWarning;

    /// <summary>Materials in the selected category, most-held first.</summary>
    public ObservableCollection<MaterialStockRow> Rows { get; } = new();

    // --- Trader helper, driven by the selected row. ---
    [ObservableProperty] private bool _hasTrade;
    [ObservableProperty] private string _tradeTitle = "";
    [ObservableProperty] private string _tradeSubtitle = "";

    /// <summary>Cheapest affordable swaps for the selected material, best first.</summary>
    public ObservableCollection<TradeOptionRow> TradeOptions { get; } = new();

    partial void OnSelectedCategoryChanged(string value)
    {
        _signature = "";      // force a rebuild against the new category
        ClearTrade();
    }

    public override void Update(CommanderState s)
    {
        var m = s.Materials;
        RawMaterials = m.Raw.Values.Sum().ToString("N0");
        ManufacturedMaterials = m.Manufactured.Values.Sum().ToString("N0");
        EncodedMaterials = m.Encoded.Values.Sum().ToString("N0");
        MaterialsSummary = $"{m.TotalCount:N0} total";

        // Capped materials are the actionable bit: until they come down, further pickups are binned.
        var capped = MaterialInventory.AtCap(s);
        HasCapWarning = capped.Count > 0;
        CapWarning = capped.Count switch
        {
            0 => "",
            1 => $"{capped[0].Name} is at its cap — further pickups are discarded",
            var n => $"{n} materials are at their cap — trade them down before farming more",
        };

        var view = MaterialInventory.Category(s, SelectedCategory);
        var signature = SelectedCategory + "|" + string.Join("|", view.Stocks.Select(x => $"{x.Symbol}:{x.Held}"));
        if (signature != _signature)
        {
            _signature = signature;
            Rows.Clear();
            foreach (var stock in view.Stocks.OrderByDescending(x => x.Held).ThenBy(x => x.Grade))
                Rows.Add(new MaterialStockRow(
                    stock.Symbol,
                    stock.Name,
                    $"G{stock.Grade}",
                    $"{stock.Held:N0} / {stock.Cap:N0}",
                    stock.Fraction,
                    stock.IsFull,
                    stock.Info.Source));
        }

        if (_selectedSymbol is not null) RefreshTrade(s, _selectedSymbol);
    }

    /// <summary>Ask the trader how to top up the clicked material from the current inventory.</summary>
    [RelayCommand]
    private void SelectMaterial(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) { ClearTrade(); return; }
        if (_selectedSymbol == symbol) { ClearTrade(); return; }   // clicking again closes it

        _selectedSymbol = symbol;
        RefreshTrade(Context.Host.State, symbol);
    }

    private void RefreshTrade(CommanderState s, string symbol)
    {
        var target = EngineeringCatalog.Default.Material(symbol);
        if (target is null) { ClearTrade(); return; }

        var held = MaterialInventory.CountsFor(s, target.Category);
        var have = held.GetValueOrDefault(symbol);

        // Ask for a useful amount rather than a token single unit: enough to reach the cap, but
        // capped itself at ten so the quote stays something a commander would actually pay.
        var wanted = Math.Clamp(target.Cap - have, 1, 10);
        var options = MaterialTrader.BestSources(target, wanted, held);

        HasTrade = true;
        TradeTitle = $"Trade for {wanted} × {target.Name}";
        TradeSubtitle = options.Count == 0
            ? have >= target.Cap
                ? "Already at the cap — nothing to trade for."
                : "Nothing in the hold covers this trade yet."
            : $"holding {have:N0} of {target.Cap:N0}";

        TradeOptions.Clear();
        foreach (var o in options)
            TradeOptions.Add(new TradeOptionRow(
                o.Source.Name,
                $"G{o.Source.Grade}",
                $"{o.Cost:N0}",
                held.GetValueOrDefault(o.Source.Symbol).ToString("N0")));
    }

    private void ClearTrade()
    {
        _selectedSymbol = null;
        HasTrade = false;
        TradeOptions.Clear();
    }

    public override void Reset()
    {
        _signature = "";
        Rows.Clear();
        ClearTrade();
    }
}

/// <summary>
/// One material row: how many are held against the per-grade cap, a fill bar, and whether it is
/// <see cref="IsFull"/> — the flag that says further pickups of it are being thrown away.
/// </summary>
public sealed record MaterialStockRow(
    string Symbol, string Name, string Grade, string Held, double Fraction, bool IsFull, string Source);

/// <summary>One trader swap: what to hand over, and how many of it the trade costs.</summary>
public sealed record TradeOptionRow(string Name, string Grade, string Cost, string Held);
