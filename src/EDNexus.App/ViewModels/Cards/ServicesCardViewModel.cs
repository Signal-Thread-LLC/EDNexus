using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.Core.Dev;
using EDNexus.Core.State;
using EDNexus.Core.Stations;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Nearest services: where to go for a material trader, shipyard, Vista Genomics and the rest.
/// Backed by the live Spansh station search, or an offline sample generator while developer mode is on.
/// </summary>
/// <remarks>
/// The flavour selector is the point of the card rather than a detail. "Nearest material trader" is
/// the wrong question — a trader deals in exactly one of Raw, Manufactured or Encoded, so a commander
/// who flies to the nearest one has a 2-in-3 chance of a wasted trip.
/// </remarks>
public sealed partial class ServicesCardViewModel : CardViewModel
{
    private SampleStationServiceFinder? _sampleFinder;

    public ServicesCardViewModel(DashboardContext context) : base(context, "services", "NEAREST SERVICES", 452)
    {
        _selectedService = StationServices.Default;
        SyncFlavours();
    }

    /// <summary>No dev-mode journal sample source feeds this card, so it has no 🎲 reshuffle.</summary>
    public override bool CanRandomize => false;

    private IStationServiceFinder Finder =>
        Context.DevEnabled ? _sampleFinder ??= new SampleStationServiceFinder(Context.Rng) : Context.Host.StationServices;

    /// <summary>Every service the card can look up.</summary>
    public ObservableCollection<StationServiceKind> Services { get; } = new(StationServices.All);

    /// <summary>Flavours of the selected service, empty when it has none.</summary>
    public ObservableCollection<string> Flavours { get; } = new();

    [ObservableProperty] private StationServiceKind _selectedService;
    [ObservableProperty] private string? _selectedFlavour;
    [ObservableProperty] private string _referenceSystem = "";
    [ObservableProperty] private bool _requireLargePad;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _hasResults;

    /// <summary>True when the selected service has flavours, so the picker is worth showing.</summary>
    public bool HasFlavours => SelectedService.HasFlavours;

    /// <summary>What the selected service is for — the card's one line of guidance.</summary>
    public string ServiceHint => SelectedService.Hint ?? "";

    public ObservableCollection<ServiceResultLine> Results { get; } = new();

    partial void OnSelectedServiceChanged(StationServiceKind value)
    {
        SyncFlavours();
        OnPropertyChanged(nameof(HasFlavours));
        OnPropertyChanged(nameof(ServiceHint));
        // The previous answer was for a different question; showing it under a new heading would lie.
        Results.Clear();
        HasResults = false;
        Status = "";
    }

    /// <summary>Rebuild the flavour list for the selected service and default to its first entry.</summary>
    private void SyncFlavours()
    {
        Flavours.Clear();
        foreach (var flavour in SelectedService.Flavours) Flavours.Add(flavour);
        SelectedFlavour = Flavours.Count > 0 ? Flavours[0] : null;
    }

    /// <summary>Pre-fill the reference system from live state, but never clobber what was typed.</summary>
    public override void Update(CommanderState s)
    {
        if (ReferenceSystem.Length == 0 && s.StarSystem is { Length: > 0 } sys) ReferenceSystem = sys;
    }

    public override void Reset()
    {
        Results.Clear();
        HasResults = false;
        Status = "";
    }

    [RelayCommand]
    private async Task Search()
    {
        var reference = ReferenceSystem.Trim();
        if (reference.Length == 0)
        {
            Status = "Enter the system to search from (usually your current one).";
            return;
        }

        var service = SelectedService;
        var flavour = service.HasFlavours ? SelectedFlavour : null;
        var label = flavour is null ? service.Label : $"{flavour} {service.Label}";

        Busy = true;
        HasResults = false;
        Results.Clear();
        Status = $"Nearest {label} to {reference} …";

        try
        {
            var found = await Finder.FindAsync(
                new StationServiceQuery(service, reference, flavour, RequireLargePad), CancellationToken.None);

            if (found.Count == 0)
            {
                Status = RequireLargePad
                    ? $"No {label} found nearby with a large pad."
                    : $"No {label} found near {reference}.";
                return;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var r in found) Results.Add(new ServiceResultLine(r, now));

            HasResults = true;
            Status = $"{found.Count} found · via {Finder.SourceName}";
        }
        catch (Exception ex)
        {
            Status = "Service lookup failed: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task CopySystem(string? system)
    {
        if (string.IsNullOrWhiteSpace(system)) return;
        await CopyToClipboardAsync(system);
        Status = $"Copied “{system}”.";
    }
}

/// <summary>One row on the services card: where the station is and what the trip actually costs.</summary>
public sealed class ServiceResultLine
{
    public ServiceResultLine(StationServiceResult r, DateTimeOffset now)
    {
        System = r.System;
        Station = r.Station;
        Distance = $"{r.DistanceLy:0.0} ly";
        Flavour = r.Flavour ?? "";
        HasFlavour = r.Flavour is { Length: > 0 };
        IsFarFromEntry = r.IsFarFromEntry;
        Arrival = FormatArrival(r.DistanceToArrivalLs);

        var pad = r.HasLargePad ? "L pad" : "no L pad";
        var kind = r.IsPlanetary ? "surface" : r.StationType ?? "station";
        Detail = $"{kind} · {pad}";
        Age = HumanizeAge(r.Age(now));
    }

    public string System { get; }
    public string Station { get; }
    public string Distance { get; }

    /// <summary>Supercruise distance from the entry point — often the real cost of the trip.</summary>
    public string Arrival { get; }

    /// <summary>True when the station is a long supercruise in, so the row can warn.</summary>
    public bool IsFarFromEntry { get; }

    /// <summary>Which flavour this station offers, for services that have them.</summary>
    public string Flavour { get; }

    public bool HasFlavour { get; }

    /// <summary>Station type and landing-pad availability.</summary>
    public string Detail { get; }

    /// <summary>How long ago the source last saw this station.</summary>
    public string Age { get; }

    // Light seconds are unreadable past a few thousand; switch to the units commanders actually use.
    private static string FormatArrival(double ls) => ls switch
    {
        < 1 => "at entry",
        < 1_000 => $"{ls:N0} Ls",
        < 100_000 => $"{ls / 1_000:0.#}k Ls",
        _ => $"{ls / 1_000:N0}k Ls",
    };

    private static string HumanizeAge(TimeSpan? age) => age switch
    {
        null => "",
        { TotalHours: < 24 } a => $"{a.TotalHours:0}h ago",
        var a => $"{a.Value.TotalDays:0}d ago",
    };
}
