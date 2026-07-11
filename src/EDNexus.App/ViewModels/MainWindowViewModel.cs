using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.App.Views;
using EDNexus.Core;
using EDNexus.Core.Dev;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Dashboard shell. Owns the engine host, the refresh timer, and the collection of self-contained
/// <see cref="CardViewModel"/>s; each card pulls its own slice from the state snapshot every tick.
/// The engine mutates state on a background thread, so rather than binding to it directly we pull a
/// snapshot onto the UI thread a few times a second — simple, and it coalesces event bursts into
/// steady updates.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly Bootstrap _boot;
    private readonly DeveloperMode _dev = new();
    private readonly Random _rng = new();
    private readonly DashboardContext _context;
    private EngineHost _host;
    private DispatcherTimer? _timer;

    public MainWindowViewModel(Bootstrap boot)
    {
        _boot = boot;
        _host = BuildHost();
        _context = new DashboardContext(() => _host, () => _boot.Dev.Enabled, _rng);
        Cards = new ObservableCollection<CardViewModel>
        {
            new LocationCardViewModel(_context),
            new ShipCardViewModel(_context),
            new MaterialsCardViewModel(_context),
            new CargoCardViewModel(_context),
            new RouteCardViewModel(_context),
            new TradeCardViewModel(_context),
            new ColonisationCardViewModel(_context),
            new MarketCardViewModel(_context),
        };

        DevMode = _boot.Dev.Enabled;
        JournalStatus = _host.JournalFound
            ? $"● Watching  {_host.JournalDirectory}"
            : "✕ Journal folder not found — set EDNEXUS_JOURNAL_DIR";
        RefreshPrivacyStatus();

        // Listen for background updater notifications so the UI can show a bottom update bar.
        EDNexus.App.Services.AutoUpdateService.UpdateDownloaded += path =>
        {
            // Marshal to the UI thread
            Dispatcher.UIThread.Post(() =>
            {
                UpdatePath = path;
                UpdateAvailable = true;
                System.Diagnostics.Trace.TraceInformation($"UI: UpdateDownloaded event received (AutoUpdateService) path={path}");
            });
        };

        // Also listen to the improved updater (AutoUpdateService2) used at startup.
        EDNexus.App.Services.AutoUpdateService2.UpdateDownloaded += path =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdatePath = path;
                UpdateAvailable = true;
                System.Diagnostics.Trace.TraceInformation($"UI: UpdateDownloaded event received (AutoUpdateService2) path={path}");
            });
        };    }

    /// <summary>The dashboard cards, in display order.</summary>
    public ObservableCollection<CardViewModel> Cards { get; }

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
    [ObservableProperty] private string _lastUpdated = "—";
    [ObservableProperty] private bool _devMode;

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

    [RelayCommand]
    private void OpenUpdateFolder()
    {
        if (string.IsNullOrEmpty(UpdatePath)) return;
        try
        {
            var dir = Path.GetDirectoryName(UpdatePath) ?? Path.GetTempPath();
            var psi = new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch { }
    }

    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (string.IsNullOrEmpty(UpdatePath)) return;
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var dlg = new EDNexus.App.Views.ConfirmInstallWindow();
        dlg.SetFilePath(UpdatePath);
        if (owner is null)
        {
            // No owner (rare in tests). Show non-modal confirmation and abort install — safer than auto-running.
            dlg.Show();
            return;
        }

        var result = await dlg.ShowDialog<bool>(owner);
        if (!result) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = UpdatePath,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch { }
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
        foreach (var card in Cards) card.Reset();
        Refresh();
    }

    private void Refresh()
    {
        var s = _host.State;
        CommanderName = s.Name ?? "—";
        Balance = s.Balance.ToString("N0") + " cr";
        LastUpdated = s.LastUpdated == default ? "—" : s.LastUpdated.LocalDateTime.ToString("HH:mm:ss");

        foreach (var card in Cards) card.Update(s);
    }
}
