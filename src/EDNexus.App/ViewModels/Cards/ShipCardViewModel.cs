using CommunityToolkit.Mvvm.ComponentModel;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>The active ship and its main-tank fuel level.</summary>
public sealed partial class ShipCardViewModel : CardViewModel
{
    public ShipCardViewModel(DashboardContext context) : base(context, "ship", "SHIP", 452) { }

    [ObservableProperty] private string _ship = "—";
    [ObservableProperty] private string _fuel = "—";
    [ObservableProperty] private double _fuelFraction;

    public override void Update(CommanderState s)
    {
        Ship = FormatShip(s);
        Fuel = s.FuelCapacity > 0 ? $"{s.FuelMain:0.0} / {s.FuelCapacity:0.0} t" : $"{s.FuelMain:0.0} t";
        FuelFraction = s.FuelCapacity > 0 ? Math.Clamp(s.FuelMain / s.FuelCapacity, 0, 1) : 0;
    }

    private static string FormatShip(CommanderState s)
    {
        if (string.IsNullOrEmpty(s.Ship)) return "—";
        var label = string.IsNullOrEmpty(s.ShipName) ? s.Ship : $"{s.Ship} · {s.ShipName}";
        return string.IsNullOrEmpty(s.ShipIdent) ? label : $"{label}  [{s.ShipIdent}]";
    }
}
