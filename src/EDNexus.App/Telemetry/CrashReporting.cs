using System.Reflection;
using EDNexus.Core.Journal;
using EDNexus.Core.Settings;
using EDNexus.Core.Telemetry;
using Sentry;

namespace EDNexus.App.Telemetry;

/// <summary>
/// Opt-in, anonymized crash &amp; error reporting via Sentry. Nothing is sent unless BOTH are true:
/// the user has consented (<see cref="AppSettings.CrashReportingEnabled"/>), and a DSN was baked in
/// at release build time (or supplied via the dev env var). Every outgoing event is run through a
/// <see cref="PiiScrubber"/> and stripped of user/host identity.
/// </summary>
public sealed class CrashReporting : IDisposable
{
    private IDisposable? _sentry;
    private PiiScrubber _scrubber = new();
    private string _installId = "";

    public bool IsActive { get; private set; }

    /// <summary>
    /// Start reporting if consent is granted and a DSN is available; otherwise no-op and return false.
    /// <paramref name="extraSensitive"/> adds literals to redact (e.g. the CMDR name once known).
    /// </summary>
    public bool TryStart(AppSettings settings, IEnumerable<string>? extraSensitive = null)
    {
        if (IsActive) return true;
        if (settings.CrashReportingEnabled != true) return false;

        var dsn = SentryConfig.ResolveDsn();
        if (string.IsNullOrWhiteSpace(dsn)) return false;

        _installId = settings.InstallId;
        _scrubber = BuildScrubber(extraSensitive);

        _sentry = SentrySdk.Init(o =>
        {
            o.Dsn = dsn;
            o.SendDefaultPii = false;         // no IP address, OS user, or machine name
            o.AttachStacktrace = true;
            o.AutoSessionTracking = true;     // release health — aggregate only, no PII
            o.Release = AppVersion();
            o.Environment = "production";
            o.MaxBreadcrumbs = 50;
            o.SetBeforeSend(ScrubEvent);
            o.SetBeforeBreadcrumb(ScrubBreadcrumb);
        });

        SentrySdk.ConfigureScope(s => s.User = new SentryUser { Id = _installId });
        IsActive = true;
        return true;
    }

    /// <summary>Stop reporting and flush. Called on opt-out and on shutdown.</summary>
    public void Stop()
    {
        _sentry?.Dispose();
        _sentry = null;
        IsActive = false;
    }

    /// <summary>Report a handled exception (only when active).</summary>
    public void Capture(Exception ex)
    {
        if (IsActive) SentrySdk.CaptureException(ex);
    }

    /// <summary>Forward journal-event handler errors to the reporter.</summary>
    public void Attach(JournalEventBus bus) => bus.HandlerError += (_, ex) => Capture(ex);

    public void Dispose() => Stop();

    private static PiiScrubber BuildScrubber(IEnumerable<string>? extra)
    {
        var sensitive = new List<string>
        {
            Environment.UserName,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            JournalPaths.Resolve() ?? "",
        };
        if (extra is not null) sensitive.AddRange(extra);
        return new PiiScrubber(sensitive);
    }

    private SentryEvent ScrubEvent(SentryEvent e, SentryHint hint)
    {
        e.ServerName = null; // never send the hostname

        if (e.Message is { } m)
            e.Message = new SentryMessage { Message = _scrubber.Scrub(m.Message), Formatted = _scrubber.Scrub(m.Formatted) };

        if (e.SentryExceptions is { } exceptions)
        {
            foreach (var ex in exceptions)
            {
                ex.Value = _scrubber.Scrub(ex.Value);
                var frames = ex.Stacktrace?.Frames;
                if (frames is null) continue;
                foreach (var frame in frames)
                {
                    frame.FileName = _scrubber.Scrub(frame.FileName);
                    frame.AbsolutePath = _scrubber.Scrub(frame.AbsolutePath);
                }
            }
        }

        // Keep only the anonymous correlation id on the user.
        e.User.Id = _installId;
        e.User.Username = null;
        e.User.Email = null;
        e.User.IpAddress = null;
        return e;
    }

    private Breadcrumb ScrubBreadcrumb(Breadcrumb b, SentryHint hint)
    {
        var scrubbed = _scrubber.Scrub(b.Message);
        if (scrubbed == b.Message) return b; // common case: nothing sensitive, keep as-is
        return new Breadcrumb(scrubbed ?? string.Empty, b.Type ?? string.Empty, b.Data, b.Category, b.Level);
    }

    private static string AppVersion()
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
}
