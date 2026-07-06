using EDNexus.App.Telemetry;
using EDNexus.Core.Settings;

namespace EDNexus.App;

/// <summary>Process-wide services created in <c>Program.Main</c> and read by the Avalonia app.</summary>
public sealed class Bootstrap
{
    public SettingsStore Store { get; }
    public AppSettings Settings { get; }
    public CrashReporting Crash { get; }

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
}
