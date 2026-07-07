using Avalonia;
using EDNexus.App.Telemetry;
using EDNexus.Core.Settings;

namespace EDNexus.App;

internal static class Program
{
    /// <summary>Process-wide services, created before the UI so startup crashes can be reported.</summary>
    public static Bootstrap Services { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        var store = new SettingsStore();
        var settings = store.Load();

        // Dispose flushes any pending Sentry events on exit. TryStart is a no-op unless the user
        // has previously opted in AND a DSN is present in this build.
        using var crash = new CrashReporting();
        crash.TryStart(settings);

        Services = new Bootstrap(store, settings, crash);

        // Start a non-blocking background auto-update check only if the user opted in.
        if (settings.AutoDownloadUpdates)
        {
            _ = System.Threading.Tasks.Task.Run(() => EDNexus.App.Services.AutoUpdateService.CheckForUpdatesAsync());
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
