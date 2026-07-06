using Avalonia.Controls;
using Avalonia.Interactivity;

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
        UpdateStatus();
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

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_boot is not null)
        {
            _boot.ApplyCrashReportingChoice(CrashToggle.IsChecked == true);
            UpdateStatus();
        }
        Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
