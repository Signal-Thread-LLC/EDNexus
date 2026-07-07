using EDNexus.App.Telemetry;
using EDNexus.Core.Settings;

namespace EDNexus.App;

/// <summary>Process-wide services created in <c>Program.Main</c> and read by the Avalonia app.</summary>
public sealed class Bootstrap
{
    public SettingsStore Store { get; }
    public AppSettings Settings { get; }
    public CrashReporting Crash { get; }

    /// <summary>Runtime developer-tools state (not persisted; off every launch).</summary>
    public DeveloperOptions Dev { get; } = new();

    public Bootstrap(SettingsStore store, AppSettings settings, CrashReporting crash)
    {
        Store = store;
        Settings = settings;
        Crash = crash;
    }

    /// <summary>Persist the current consent choice and start/stop reporting to match.</summary>
    public void ApplyCrashReportingChoice(bool enabled)
    {
        Settings.CrashReportingEnabled = enabled;
        Store.Save(Settings);
        if (enabled) Crash.TryStart(Settings);
        else Crash.Stop();
    }

    /// <summary>
    /// Persist the EDDN/Inara opt-in choices. The reporters read these flags live, so no restart is
    /// needed for the change to take effect.
    /// </summary>
    public void ApplyReportingChoice(bool eddnEnabled, bool inaraEnabled, string inaraApiKey)
    {
        Settings.Reporting.Eddn.Enabled = eddnEnabled;
        Settings.Reporting.Inara.Enabled = inaraEnabled;
        Settings.Reporting.Inara.ApiKey = inaraApiKey.Trim();
        Store.Save(Settings);
    }

    /// <summary>Persist the user's auto-update preference.</summary>
    public void ApplyAutoDownloadChoice(bool enabled)
    {
        Settings.AutoDownloadUpdates = enabled;
        Store.Save(Settings);
    }
}
