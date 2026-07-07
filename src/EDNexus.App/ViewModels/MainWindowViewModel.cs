using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.App.Views;
using EDNexus.Core;
using EDNexus.Core.Dev;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Mirror of <see cref="CommanderState"/> for the dashboard. The engine mutates state on a
/// background thread, so rather than binding to it directly we pull a snapshot onto the UI
/// thread a few times a second — simple, and it coalesces event bursts into steady updates.
/// </summary>
public sealed partial class MainWindowViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject, IDisposable
{
    private readonly Bootstrap _boot;
    private readonly DeveloperMode _dev = new();
    private readonly Random _rng = new();
    private EngineHost _host;
    private DispatcherTimer? _timer;
    private string _cargoSignature = "";
    private string _shoppingSignature = "";

    public MainWindowViewModel(Bootstrap boot)
    {
        _boot = boot;
        _host = BuildHost();
        JournalStatus = _host.JournalFound
            ? $"● Watching  {_host.JournalDirectory}"
            : "✕ Journal folder not found — set EDNEXUS_JOURNAL_DIR";
        RefreshPrivacyStatus();
    }

    /// <summary>Create a fresh engine host and wire crash reporting to its bus.</summary>
    private EngineHost BuildHost()
    {
        // Passing settings wires the EDDN/Inara reporters (still gated on their per-service opt-in).
        // While developer mode is on, reporting is paused so fabricated events never reach EDDN/Inara.
        var host = new EngineHost(settings: _boot.Settings, reportingSuppressed: () => _boot.Dev.Enabled);
        _boot.Crash.Attach(host.Bus); // report journal handler errors
        return host;
    }

    public void Start()
    {
        _host.Start();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    public void Dispose() => _host.Dispose();

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

    [ObservableProperty] private bool _devMode;

    [ObservableProperty] private bool _hasColonisation;
    [ObservableProperty] private string _colonisationTitle = "—";
    [ObservableProperty] private string _colonisationStatus = "—";
    [ObservableProperty] private string _colonisationSummary = "";
    [ObservableProperty] private double _colonisationProgress;

    public ObservableCollection<CargoLine> Cargo { get; } = new();
    public ObservableCollection<ShoppingLine> ShoppingList { get; } = new();

    /// <summary>Inverse of <see cref="HasColonisation"/>, for the empty-state hint's visibility.</summary>
    public bool NoColonisation => !HasColonisation;

    partial void OnHasColonisationChanged(bool value) => OnPropertyChanged(nameof(NoColonisation));

    [RelayCommand]
    private async Task OpenSettings()
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var dialog = new SettingsWindow(_boot);
        if (owner is not null) await dialog.ShowDialog(owner);
        else dialog.Show();
        RefreshPrivacyStatus();
        DevMode = _boot.Dev.Enabled; // reflect a dev-mode toggle made in the settings dialog
    }

    private void RefreshPrivacyStatus()
        => PrivacyStatus = _boot.Crash.IsActive ? "crash reporting on" : "crash reporting off";

    // --- Developer mode: fabricate random-but-valid state through the real event pipeline. ---

    /// <summary>Reshuffle a single card by publishing its sample events onto the live bus.</summary>
    [RelayCommand]
    private void RandomizeCard(string cardKey)
    {
        _dev.Randomize(_host.Bus, _rng, cardKey);
        Refresh();
    }

    /// <summary>Reshuffle every card at once.</summary>
    [RelayCommand]
    private void RandomizeAll()
    {
        _dev.Randomize(_host.Bus, _rng);
        Refresh();
    }

    /// <summary>Discard fabricated state and re-warm from the real journal by rebuilding the engine.</summary>
    [RelayCommand]
    private void ResetToLive()
    {
        _host.Dispose();
        _host = BuildHost();
        _host.Start();
        _cargoSignature = "";
        _shoppingSignature = "";
        Refresh();
    }

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
        SyncColonisation(s);
    }

    private void SyncColonisation(CommanderState s)
    {
        var site = _host.Colonisation.ActiveSite;
        if (site is null)
        {
            if (HasColonisation) { HasColonisation = false; ShoppingList.Clear(); _shoppingSignature = ""; }
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
        if (signature == _shoppingSignature) return;
        _shoppingSignature = signature;

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

/// <param name="HoldNote">"✓ 648 in hold" / "648 in hold" / "" — highlights what's already aboard.</param>
/// <param name="Fraction">Delivery progress for this commodity (0..1), for the per-row bar.</param>
public sealed record ShoppingLine(
    string Name, string Remaining, string ToBuy, string HoldNote, bool Carrying, double Fraction);
