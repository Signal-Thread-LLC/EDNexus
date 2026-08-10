using CommunityToolkit.Mvvm.ComponentModel;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>Where the commander is: system, body, and docked/in-flight status.</summary>
public sealed partial class LocationCardViewModel : CardViewModel
{
    public LocationCardViewModel(DashboardContext context) : base(context, "location", "LOCATION", 452) { }

    [ObservableProperty] private string _systemName = "—";
    [ObservableProperty] private string _body = "—";
    [ObservableProperty] private string _locationStatus = "—";

    public override void Update(CommanderState s)
    {
        SystemName = s.StarSystem ?? "—";
        Body = s.Body ?? "—";
        // StationDisplayName, not StationName: on the commander's own carrier the journal reports
        // only the callsign, and "Docked · K7Q-B3L" tells them nothing they don't already know.
        LocationStatus = s.Docked ? $"Docked · {s.StationDisplayName}" : "In flight";
    }
}
