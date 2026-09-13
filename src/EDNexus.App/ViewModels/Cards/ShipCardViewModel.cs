using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.Core.Ship;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>The active ship and its main-tank fuel level.</summary>
public sealed partial class ShipCardViewModel : CardViewModel
{
    public ShipCardViewModel(DashboardContext context) : base(context, "ship", "SHIP", 452) { }

    [ObservableProperty] private string _ship = "—";
    [ObservableProperty] private string _fuel = "—";
    [ObservableProperty] private double _fuelFraction;
    [ObservableProperty] private string? _coriolisStatus;

    public override void Update(CommanderState s)
    {
        Ship = FormatShip(s);
        Fuel = s.FuelCapacity > 0 ? $"{s.FuelMain:0.0} / {s.FuelCapacity:0.0} t" : $"{s.FuelMain:0.0} t";
        FuelFraction = s.FuelCapacity > 0 ? Math.Clamp(s.FuelMain / s.FuelCapacity, 0, 1) : 0;
    }

    /// <summary>Open the current build in Coriolis, so it can be inspected or shared as a link.</summary>
    [RelayCommand]
    private void OpenCoriolisBuild()
    {
        var url = CoriolisShareLink.Build(Context.Host.State.LastLoadoutJson);
        if (url is null)
        {
            CoriolisStatus = "No ship loadout seen yet.";
            return;
        }

        OpenUrl(url);
        CoriolisStatus = "Opened in Coriolis.";
    }

    /// <summary>Copy the current build's Coriolis link, for pasting elsewhere.</summary>
    [RelayCommand]
    private async Task CopyCoriolisLink()
    {
        var url = CoriolisShareLink.Build(Context.Host.State.LastLoadoutJson);
        if (url is null)
        {
            CoriolisStatus = "No ship loadout seen yet.";
            return;
        }

        await CopyToClipboardAsync(url);
        CoriolisStatus = "Coriolis link copied.";
    }

    private static string FormatShip(CommanderState s)
    {
        if (string.IsNullOrEmpty(s.Ship)) return "—";
        var label = string.IsNullOrEmpty(s.ShipName) ? s.Ship : $"{s.Ship} · {s.ShipName}";
        return string.IsNullOrEmpty(s.ShipIdent) ? label : $"{label}  [{s.ShipIdent}]";
    }
}
