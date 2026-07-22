using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using System.Diagnostics;
using System.IO;

namespace EDNexus.App.Views;

public partial class SettingsWindow : Window
{
    private readonly Bootstrap? _boot;

    // Parameterless ctor for the XAML previewer / designer.
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(Bootstrap boot) : this()
    {
        _boot = boot;
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
}

