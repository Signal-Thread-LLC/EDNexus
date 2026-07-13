using EDNexus.Core.Journal;
using EDNexus.Core.Settings;
using EliteDangerous.Eddn;
using EliteDangerous.Inara;

namespace EDNexus.Core.Reporting;

/// <summary>
/// Owns the outbound reporting stack: a shared <see cref="HttpClient"/> plus the EDDN and Inara
/// bridges. Constructed by <see cref="EngineHost"/> when settings are available. Both reporters are
/// always wired to the bus but gated on their per-service opt-in flag, read live from settings, so
/// toggling a service in the UI takes effect without a restart.
/// </summary>
public sealed class ReporterHost : IAsyncDisposable
{
    private const string AppName = "EDNexus";

    private readonly HttpClient _http = new();
    private readonly EddnBridge _eddn;
    private readonly InaraBridge _inara;

    /// <param name="isSuppressed">
    /// When it returns true, both reporters go silent — used to pause all outbound streaming while
    /// developer mode is fabricating events, so synthetic data never reaches EDDN or Inara.
    /// </param>
    public ReporterHost(JournalEventBus bus, AppSettings settings, string appVersion, bool isBeingDeveloped,
        Func<bool>? isSuppressed = null, IReportingLog? log = null)
    {
        var eddnOptions = new EddnClientOptions { SoftwareName = AppName, SoftwareVersion = appVersion };
        _eddn = new EddnBridge(bus, settings, new EddnUploader(eddnOptions, _http), new EddnJournalTransformer(eddnOptions), isSuppressed, log);

        var inaraOptions = new InaraClientOptions { AppName = AppName, AppVersion = appVersion, IsBeingDeveloped = isBeingDeveloped };
        _inara = new InaraBridge(bus, settings, new InaraClient(inaraOptions, _http), isSuppressed, log);
    }

    public async ValueTask DisposeAsync()
    {
        await _eddn.DisposeAsync().ConfigureAwait(false);
        await _inara.DisposeAsync().ConfigureAwait(false);
        _http.Dispose();
    }
}
