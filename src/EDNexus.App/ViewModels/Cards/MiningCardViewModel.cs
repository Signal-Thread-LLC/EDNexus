using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.App.Services;
using EDNexus.Core.Mining;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Mining session helper: every prospected asteroid this session, with each material — and any
/// deep-core motherlode — called out when its galactic-average price clears the commander's own
/// threshold. Learns those average prices passively from every station market visited (Frontier does
/// not expose the figure anywhere else), so the highlight gets more complete the more the commander
/// plays rather than depending on a static, drift-prone price table.
/// </summary>
/// <remarks>
/// The journal's <c>ProspectedAsteroid</c> event does not distinguish a laser-minable surface deposit
/// from one exposed by a sub-surface displacement missile — both simply appear in its material list —
/// so "Deposit" below covers either; only a deep-core motherlode is separately identifiable.
/// </remarks>
public sealed partial class MiningCardViewModel : CardViewModel
{
    private string _signature = "";
    private long _lastLearnedMarketId;
    private DateTimeOffset _lastLearnedMarketUpdate;
    private DateTimeOffset? _lastSeenProspectTimestamp;

    public MiningCardViewModel(DashboardContext context) : base(context, "mining", "MINING", 452)
        => ThresholdText = Context.GetMiningSettings().MinValueThreshold is > 0 and var t ? t.ToString(CultureInfo.InvariantCulture) : "";

    [ObservableProperty] private string _thresholdText = "";
    [ObservableProperty] private string _latestSummary = "Prospect a rock to see what it's carrying.";
    [ObservableProperty] private bool _latestWorthMining;
    [ObservableProperty] private string _sessionSummary = "";

    public ObservableCollection<ProspectRow> Prospects { get; } = new();

    partial void OnThresholdTextChanged(string value)
    {
        var credits = ParseCredits(value);
        Context.SaveMiningThreshold(credits);
        RebuildFromHistory();   // re-flag every row against the new threshold immediately
    }

    public override void Update(CommanderState s)
    {
        LearnFromCurrentMarket();

        var mining = Context.Host.Mining;
        var latestTimestamp = mining.Latest?.Timestamp;
        var isNewProspect = latestTimestamp is not null && latestTimestamp != _lastSeenProspectTimestamp;
        _lastSeenProspectTimestamp = latestTimestamp;

        var signature = $"{mining.History.Count}|{latestTimestamp:o}|{ThresholdText}";
        if (signature != _signature)
        {
            _signature = signature;
            RebuildFromHistory();
        }

        // Only a genuinely new find rings the chime — editing the threshold can also flip the latest
        // row's verdict, but that is a re-evaluation of an old find, not a new one to announce.
        if (isNewProspect && Prospects.Count > 0 && Prospects[0].AnyWorthMining)
            MiningAlertSound.Play();
    }

    public override void Reset()
    {
        _signature = "";
        _lastLearnedMarketId = 0;
        _lastSeenProspectTimestamp = null;
        Prospects.Clear();
        LatestSummary = "Prospect a rock to see what it's carrying.";
        LatestWorthMining = false;
        SessionSummary = "";
    }

    /// <summary>Wipe this session's prospected history — the field data itself, not the learned prices or threshold.</summary>
    [RelayCommand]
    private void ClearHistory()
    {
        Context.Host.Mining.Clear();
        Prospects.Clear();
        LatestSummary = "Prospect a rock to see what it's carrying.";
        LatestWorthMining = false;
        SessionSummary = "";
        _signature = "";
        _lastSeenProspectTimestamp = null;
    }

    private void RebuildFromHistory()
    {
        var known = Context.GetMiningSettings().KnownPrices;
        var threshold = ParseCredits(ThresholdText);
        var history = Context.Host.Mining.History;

        Prospects.Clear();
        // Newest first, so the rock the commander is looking at right now is always on top.
        for (var i = history.Count - 1; i >= 0; i--)
            Prospects.Add(ToRow(history[i], known, threshold));

        if (Prospects.Count > 0)
        {
            var latest = Prospects[0];
            LatestWorthMining = latest.AnyWorthMining;
            LatestSummary = latest.AnyWorthMining
                ? "Worth mining: " + string.Join(", ", latest.WorthMiningNames)
                : threshold <= 0
                    ? "Set a credit threshold below to start highlighting worthwhile finds."
                    : "Nothing in this rock clears your threshold.";
        }

        var hits = Prospects.Count(p => p.AnyWorthMining);
        SessionSummary = history.Count == 0 ? ""
            : $"{history.Count} prospected · {hits} worth mining · {known.Count} prices known";
    }

    private static ProspectRow ToRow(ProspectResult r, IReadOnlyDictionary<string, int> known, int threshold)
    {
        var materials = r.Materials.Select(m => ToMaterialRow(m.Name, m.Symbol, m.Proportion, known, threshold)).ToList();

        ProspectMaterialRow? motherlode = null;
        if (r.HasMotherlode)
            motherlode = ToMaterialRow(r.MotherlodeName ?? r.MotherlodeSymbol!, r.MotherlodeSymbol!, null, known, threshold);

        return new ProspectRow(
            Time: r.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
            ContentLabel: r.Content switch
            {
                "High" => "High material content",
                "Medium" => "Medium material content",
                "Low" => "Low material content",
                _ => "",
            },
            Remaining: r.Remaining < 99.5 ? $"{r.Remaining:N0}% remaining" : "",
            Motherlode: motherlode,
            Materials: materials);
    }

    private static ProspectMaterialRow ToMaterialRow(
        string name, string symbol, double? proportion, IReadOnlyDictionary<string, int> known, int threshold)
    {
        var hasPrice = known.TryGetValue(symbol, out var price) && price > 0;
        var worthMining = hasPrice && threshold > 0 && price >= threshold;
        var proportionText = proportion is { } p ? $"{p:N0}%" : "";
        var priceText = hasPrice ? $"{price:N0} cr" : "price unknown";
        return new ProspectMaterialRow(name, proportionText, priceText, worthMining);
    }

    /// <summary>Absorb every commodity's galactic-average price out of the docked market, if any.</summary>
    private void LearnFromCurrentMarket()
    {
        var market = Context.Host.Market.Current;
        if (market is null || market.MarketId == _lastLearnedMarketId && market.Updated == _lastLearnedMarketUpdate)
            return;

        _lastLearnedMarketId = market.MarketId;
        _lastLearnedMarketUpdate = market.Updated;
        Context.LearnCommodityPrices(market.Commodities.Where(c => c.MeanPrice > 0).Select(c => (c.Symbol, c.MeanPrice)));
    }

    private static int ParseCredits(string text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;
}

/// <summary>One prospected asteroid, as the mining card renders it.</summary>
public sealed class ProspectRow
{
    public string Time { get; }
    public string ContentLabel { get; }
    public bool ShowContent => ContentLabel.Length > 0;
    public string Remaining { get; }
    public bool ShowRemaining => Remaining.Length > 0;
    public ProspectMaterialRow? Motherlode { get; }
    public bool HasMotherlode => Motherlode is not null;
    public IReadOnlyList<ProspectMaterialRow> Materials { get; }
    public bool AnyWorthMining => (Motherlode?.WorthMining ?? false) || Materials.Any(m => m.WorthMining);

    /// <summary>Names worth calling out, deep-core motherlode first — feeds the card's headline summary.</summary>
    public IEnumerable<string> WorthMiningNames =>
        (Motherlode is { WorthMining: true } m ? new[] { $"{m.Name} (deep core)" } : Array.Empty<string>())
        .Concat(Materials.Where(x => x.WorthMining).Select(x => x.Name));

    public ProspectRow(string Time, string ContentLabel, string Remaining, ProspectMaterialRow? Motherlode, IReadOnlyList<ProspectMaterialRow> Materials)
    {
        this.Time = Time;
        this.ContentLabel = ContentLabel;
        this.Remaining = Remaining;
        this.Motherlode = Motherlode;
        this.Materials = Materials;
    }
}

/// <summary>One material within a prospected rock (or its motherlode), with its price verdict.</summary>
public sealed class ProspectMaterialRow
{
    public string Name { get; }
    public string ProportionText { get; }
    public bool ShowProportion => ProportionText.Length > 0;
    public string PriceText { get; }
    public bool WorthMining { get; }

    public ProspectMaterialRow(string name, string proportionText, string priceText, bool worthMining)
    {
        Name = name;
        ProportionText = proportionText;
        PriceText = priceText;
        WorthMining = worthMining;
    }
}
