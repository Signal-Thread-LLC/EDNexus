using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>The cargo hold: a summary line and the per-commodity breakdown.</summary>
public sealed partial class CargoCardViewModel : CardViewModel
{
    private string _signature = "";

    public CargoCardViewModel(DashboardContext context) : base(context, "cargo", "CARGO HOLD", 452) { }

    [ObservableProperty] private string _cargoSummary = "—";

    public ObservableCollection<CargoLine> Cargo { get; } = new();

    public override void Update(CommanderState s)
    {
        CargoSummary = $"{s.CargoTons:0} t · {s.Cargo.Count} commodities";

        // Rebuild the list only when the hold actually changes, so steady-state ticks don't churn it.
        var signature = string.Join("|", s.Cargo.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));
        if (signature == _signature) return;
        _signature = signature;

        Cargo.Clear();
        foreach (var kv in s.Cargo.OrderByDescending(k => k.Value))
            Cargo.Add(new CargoLine(kv.Key, kv.Value));
    }

    public override void Reset()
    {
        _signature = "";
        Cargo.Clear();
    }
}

public sealed record CargoLine(string Name, int Count);
