using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.App.Views;
using EDNexus.Core;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Mirror of <see cref="CommanderState"/> for the dashboard. The engine mutates state on a
/// background thread, so rather than binding to it directly we pull a snapshot onto the UI
/// thread a few times a second — simple, and it coalesces event bursts into steady updates.
/// </summary>
public sealed partial class MainWindowViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private readonly EngineHost _host;
    private readonly Bootstrap _boot;
    private DispatcherTimer? _timer;
    private string _cargoSignature = "";

    public MainWindowViewModel(EngineHost host, Bootstrap boot)
    {
        _host = host;
        _boot = boot;
        JournalStatus = host.JournalFound
            ? $"● Watching  {host.JournalDirectory}"
            : "✕ Journal folder not found — set EDNEXUS_JOURNAL_DIR";
        RefreshPrivacyStatus();
    }

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    [ObservableProperty] private string _journalStatus = "";
    [ObservableProperty] private string _privacyStatus = "";
    [ObservableProperty] private string _commanderName = "—";
    [ObservableProperty] private string _balance = "0 cr";
    [ObservableProperty] private string _ship = "—";
    [ObservableProperty] private string _systemName = "—";
    [ObservableProperty] private string _body = "—";
    [ObservableProperty] private string _locationStatus = "—";
    [ObservableProperty] private string _fuel = "—";
    [ObservableProperty] private double _fuelFraction;
    [ObservableProperty] private string _cargoSummary = "—";
    [ObservableProperty] private string _materialsSummary = "—";
    [ObservableProperty] private string _rawMaterials = "0";
    [ObservableProperty] private string _manufacturedMaterials = "0";
    [ObservableProperty] private string _encodedMaterials = "0";
    [ObservableProperty] private string _lastUpdated = "—";

    public ObservableCollection<CargoLine> Cargo { get; } = new();

    [RelayCommand]
    private async Task OpenSettings()
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var dialog = new SettingsWindow(_boot);
        if (owner is not null) await dialog.ShowDialog(owner);
        else dialog.Show();
        RefreshPrivacyStatus();
    }

    private void RefreshPrivacyStatus()
        => PrivacyStatus = _boot.Crash.IsActive ? "crash reporting on" : "crash reporting off";

    private void Refresh()
    {
        var s = _host.State;
        CommanderName = s.Name ?? "—";
        Balance = s.Balance.ToString("N0") + " cr";
        Ship = FormatShip(s);
        SystemName = s.StarSystem ?? "—";
        Body = s.Body ?? "—";
        LocationStatus = s.Docked ? $"Docked · {s.StationName}" : "In flight";
        Fuel = s.FuelCapacity > 0 ? $"{s.FuelMain:0.0} / {s.FuelCapacity:0.0} t" : $"{s.FuelMain:0.0} t";
        FuelFraction = s.FuelCapacity > 0 ? Math.Clamp(s.FuelMain / s.FuelCapacity, 0, 1) : 0;
        CargoSummary = $"{s.CargoTons:0} t · {s.Cargo.Count} commodities";

        var m = s.Materials;
        RawMaterials = m.Raw.Values.Sum().ToString("N0");
        ManufacturedMaterials = m.Manufactured.Values.Sum().ToString("N0");
        EncodedMaterials = m.Encoded.Values.Sum().ToString("N0");
        MaterialsSummary = $"{m.TotalCount:N0} total";
        LastUpdated = s.LastUpdated == default ? "—" : s.LastUpdated.LocalDateTime.ToString("HH:mm:ss");

        SyncCargo(s);
    }

    private void SyncCargo(CommanderState s)
    {
        var signature = string.Join("|", s.Cargo.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));
        if (signature == _cargoSignature) return;
        _cargoSignature = signature;

        Cargo.Clear();
        foreach (var kv in s.Cargo.OrderByDescending(k => k.Value))
            Cargo.Add(new CargoLine(kv.Key, kv.Value));
    }

    private static string FormatShip(CommanderState s)
    {
        if (string.IsNullOrEmpty(s.Ship)) return "—";
        var label = string.IsNullOrEmpty(s.ShipName) ? s.Ship : $"{s.Ship} · {s.ShipName}";
        return string.IsNullOrEmpty(s.ShipIdent) ? label : $"{label}  [{s.ShipIdent}]";
    }
}

public sealed record CargoLine(string Name, int Count);
