using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EDNexus.Core.Settings;
using System.Diagnostics;
using System.IO;

namespace EDNexus.App.Views;

public partial class SettingsWindow : Window
{
    private readonly Bootstrap? _boot;

    // The live dashboard, so the layout controls drive the real cards rather than a copy. Null when
    // the window is opened without one (the designer, and the pre-layout call sites).
    private readonly ViewModels.MainWindowViewModel? _dashboard;

    // Parameterless ctor for the XAML previewer / designer.
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(Bootstrap boot, ViewModels.MainWindowViewModel? dashboard = null) : this()
    {
        _boot = boot;
        _dashboard = dashboard;
        DashboardSection.IsVisible = dashboard is not null;
        if (dashboard is not null) CardList.ItemsSource = dashboard.Cards;
        CrashToggle.IsChecked = boot.Settings.CrashReportingEnabled == true;
        EddnToggle.IsChecked = boot.Settings.Reporting.Eddn.Enabled;
        InaraToggle.IsChecked = boot.Settings.Reporting.Inara.Enabled;
        InaraApiKey.Text = boot.Settings.Reporting.Inara.ApiKey;
        AutoDownloadToggle.IsChecked = boot.Settings.AutoDownloadUpdates;

        // The whole section disappears when the dev tools are compiled out / disabled.
        DevSection.IsVisible = boot.Dev.Available;
        DevModeToggle.IsChecked = boot.Dev.Enabled;

        UpdateStatus();
        UpdateVersionAndUpdateLine();
    }

    private void UpdateStatus()
    {
        if (_boot is null) return;
        var active = _boot.Crash.IsActive;
        var chosen = _boot.Settings.CrashReportingEnabled;
        StatusLine.Text = chosen switch
        {
            true when active => "Status: reporting active.",
            true => "Status: enabled, but no DSN is configured in this build — nothing will be sent.",
            _ => "Status: reporting is off.",
        };
    }

    private void UpdateVersionAndUpdateLine()
    {
        try
        {
            var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(Program).Assembly.Location).ProductVersion;
            VersionLine.Text = ver ?? "(unknown)";
        }
        catch
        {
            VersionLine.Text = "(unknown)";
        }

        try
        {
            // If the updater has already downloaded an update, show the path; otherwise indicate not available.
            var updatePath = EDNexus.App.Services.AutoUpdateService2.LastDownloadedPath;
            if (!string.IsNullOrEmpty(updatePath))
            {
                UpdateLine.Text = "Downloaded: " + Path.GetFileName(updatePath);
            }
            else
            {
                UpdateLine.Text = "No update downloaded.";
            }
        }
        catch
        {
            UpdateLine.Text = "(unknown)";
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_boot is not null)
        {
            _boot.ApplyCrashReportingChoice(CrashToggle.IsChecked == true);
            _boot.ApplyReportingChoice(
                EddnToggle.IsChecked == true,
                InaraToggle.IsChecked == true,
                InaraApiKey.Text ?? string.Empty);
            _boot.ApplyAutoDownloadChoice(AutoDownloadToggle.IsChecked == true);
            _boot.Dev.Enabled = DevModeToggle.IsChecked == true; // runtime-only; not persisted
            UpdateStatus();
            UpdateVersionAndUpdateLine();
            System.Diagnostics.Trace.TraceInformation("Settings: saved by user");
        }
        Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnRevealApiKeyChanged(object? sender, RoutedEventArgs e)
        => InaraApiKey.RevealPassword = RevealApiKey.IsChecked == true;

    private void OnOpenLogs(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EDNexus", "logs");
            Directory.CreateDirectory(dir);
            var psi = new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch
        {
            // Best-effort; failure to open explorer must not crash the settings dialog.
        }
    }

    private async void OnCheckNow(object? sender, RoutedEventArgs e)
    {
        try
        {
            CheckNowButton.IsEnabled = false;
            UpdateLine.Text = "Checking for updates...";
            System.Diagnostics.Trace.TraceInformation("Settings: user initiated update check");
            var res = await EDNexus.App.Services.AutoUpdateService2.CheckForUpdatesAsync();
            System.Diagnostics.Trace.TraceInformation($"Settings: update check result Found={res.Found}, Message={res.Message}, Verified={res.Verified}");
            if (res.Found)
            {
                if (res.Path is not null)
                    UpdateLine.Text = res.Verified ? $"Downloaded & verified" : $"Downloaded (unverified)";
                else
                    UpdateLine.Text = res.Message;
            }
            else
            {
                UpdateLine.Text = $"No update: {res.Message}";
            }
            UpdateVersionAndUpdateLine();
        }
        catch (Exception ex)
        {
            UpdateLine.Text = $"Check failed: {ex.Message}";
            System.Diagnostics.Trace.TraceWarning($"Settings: update check failed: {ex}");
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    // --- Dashboard layout ---

    private void OnMoveCardUp(object? sender, RoutedEventArgs e) => MoveCard(sender, up: true);

    private void OnMoveCardDown(object? sender, RoutedEventArgs e) => MoveCard(sender, up: false);

    /// <summary>
    /// The row's card id rides on the button's Tag, because the reorder commands live on the
    /// dashboard rather than the card being moved.
    /// </summary>
    private void MoveCard(object? sender, bool up)
    {
        if (_dashboard is null || sender is not Control { Tag: string id }) return;

        if (up) _dashboard.MoveCardUpCommand.Execute(id);
        else _dashboard.MoveCardDownCommand.Execute(id);

        RebindCardList();
    }

    private void OnResetLayout(object? sender, RoutedEventArgs e)
    {
        if (_dashboard is null) return;
        _dashboard.ResetLayoutCommand.Execute(null);
        RebindCardList();
        ShowLayoutStatus("Layout reset to defaults.");
    }

    /// <summary>
    /// Write the arrangement to a file the commander chooses. Only the layout travels — the Inara
    /// key and install id stay in local app data, which is why settings themselves are not synced.
    /// </summary>
    private async void OnExportLayout(object? sender, RoutedEventArgs e)
    {
        if (_dashboard is null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export dashboard layout",
            SuggestedFileName = DashboardLayoutFile.DefaultFileName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { LayoutFileType },
        });
        if (file is null) return;   // cancelled

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_dashboard.ExportLayoutJson());
            ShowLayoutStatus($"Layout exported to {file.Name}.");
        }
        catch (Exception ex)
        {
            ShowLayoutStatus($"Export failed: {ex.Message}");
        }
    }

    private async void OnImportLayout(object? sender, RoutedEventArgs e)
    {
        if (_dashboard is null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import dashboard layout",
            AllowMultiple = false,
            FileTypeFilter = new[] { LayoutFileType },
        });
        if (files.Count == 0) return;   // cancelled

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();

            if (_dashboard.ImportLayoutJson(json))
            {
                RebindCardList();
                ShowLayoutStatus($"Layout imported from {files[0].Name}.");
            }
            else
            {
                ShowLayoutStatus("That file is not an EDNexus dashboard layout — nothing changed.");
            }
        }
        catch (Exception ex)
        {
            ShowLayoutStatus($"Import failed: {ex.Message}");
        }
    }

    private static FilePickerFileType LayoutFileType => new("EDNexus dashboard layout")
    {
        Patterns = new[] { "*.json" },
    };

    /// <summary>The dashboard rebuilds its collection to reorder, so rebind to show the new order.</summary>
    private void RebindCardList()
    {
        if (_dashboard is null) return;
        CardList.ItemsSource = null;
        CardList.ItemsSource = _dashboard.Cards;
    }

    private void ShowLayoutStatus(string message)
    {
        LayoutStatus.Text = message;
        LayoutStatus.IsVisible = true;
    }
}
