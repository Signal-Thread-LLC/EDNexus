using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>Active colonisation construction site: progress and the outstanding shopping list.</summary>
public sealed partial class ColonisationCardViewModel : CardViewModel
{
    private string _signature = "";

    public ColonisationCardViewModel(DashboardContext context) : base(context, "colonisation", "COLONISATION", 920) { }

    [ObservableProperty] private bool _hasColonisation;
    [ObservableProperty] private string _colonisationTitle = "—";
    [ObservableProperty] private string _colonisationStatus = "—";
    [ObservableProperty] private string _colonisationSummary = "";
    [ObservableProperty] private double _colonisationProgress;

    public ObservableCollection<ShoppingLine> ShoppingList { get; } = new();

    /// <summary>Inverse of <see cref="HasColonisation"/>, for the empty-state hint's visibility.</summary>
    public bool NoColonisation => !HasColonisation;

    partial void OnHasColonisationChanged(bool value) => OnPropertyChanged(nameof(NoColonisation));

    public override void Update(CommanderState s)
    {
        var site = Context.Host.Colonisation.ActiveSite;
        if (site is null)
        {
            if (HasColonisation) { HasColonisation = false; ShoppingList.Clear(); _signature = ""; }
            return;
        }

        HasColonisation = true;
        ColonisationTitle = site.StationName ?? site.StarSystem ?? "Construction site";
        ColonisationProgress = Math.Clamp(site.Progress, 0, 1);
        ColonisationStatus = site.Complete ? "Complete"
            : site.Failed ? "Failed"
            : $"{site.Progress * 100:0.#}%";
        ColonisationSummary =
            $"{site.CompletedCount}/{site.Resources.Count} commodities · {site.TotalRemaining:N0} t remaining";

        var list = site.BuildShoppingList(s.Cargo);
        var signature = site.MarketId + "|" + site.Progress.ToString("0.####") + "|"
            + string.Join("|", list.Select(i => $"{i.Name}:{i.Remaining}:{i.InHold}"));
        if (signature == _signature) return;
        _signature = signature;

        ShoppingList.Clear();
        foreach (var i in list)
        {
            var hold = i.InHold <= 0 ? ""
                : i.CoveredByHold ? $"✓ {i.Carrying:N0} in hold"
                : $"{i.Carrying:N0} in hold";
            ShoppingList.Add(new ShoppingLine(
                i.Name, i.Remaining.ToString("N0"), i.StillNeeded.ToString("N0"), hold, i.InHold > 0, i.Fraction));
        }
    }

    public override void Reset()
    {
        _signature = "";
        ShoppingList.Clear();
    }
}

/// <param name="HoldNote">"✓ 648 in hold" / "648 in hold" / "" — highlights what's already aboard.</param>
/// <param name="Fraction">Delivery progress for this commodity (0..1), for the per-row bar.</param>
public sealed record ShoppingLine(
    string Name, string Remaining, string ToBuy, string HoldNote, bool Carrying, double Fraction);
