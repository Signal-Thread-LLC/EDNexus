using System.Net;
using EDNexus.Core.Journal;
using EDNexus.Core.Reporting;
using EDNexus.Core.Settings;
using EliteDangerous.Eddn;
using Xunit;

namespace EDNexus.Tests.Reporting;

public class EddnBridgeTests
{
    private static readonly EddnClientOptions Options = new()
    {
        SoftwareName = "T", SoftwareVersion = "1", RetryDelay = TimeSpan.Zero,
    };

    private static AppSettings EnabledSettings()
    {
        var s = new AppSettings();
        s.Reporting.Eddn.Enabled = true;
        return s;
    }

    private static JournalEntry Live(string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var e));
        return e;
    }

    private static (EddnBridge, RecordingHandler) NewBridge(JournalEventBus bus, AppSettings settings, Func<bool>? isSuppressed)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var uploader = new EddnUploader(Options, new HttpClient(handler));
        var bridge = new EddnBridge(bus, settings, uploader, new EddnJournalTransformer(Options), isSuppressed);
        return (bridge, handler);
    }

    private static (EddnBridge, RecordingHandler) NewBridge(
        JournalEventBus bus, AppSettings settings, IReportingLog log, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new RecordingHandler(status);
        var uploader = new EddnUploader(Options, new HttpClient(handler));
        var bridge = new EddnBridge(bus, settings, uploader, new EddnJournalTransformer(Options), isSuppressed: () => false, log);
        return (bridge, handler);
    }

    private const string Jump = """
    { "timestamp": "2020-01-01T00:00:00Z", "event": "FSDJump",
      "StarSystem": "Sol", "SystemAddress": 1, "StarPos": [0.0, 0.0, 0.0] }
    """;

    [Fact]
    public async Task Live_event_uploads_when_not_suppressed()
    {
        var bus = new JournalEventBus();
        var (bridge, handler) = NewBridge(bus, EnabledSettings(), isSuppressed: () => false);
        await using var _ = bridge;

        bus.Publish(Live(Jump));
        await bridge.DisposeAsync();   // flushes the uploader

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Suppressed_bridge_uploads_nothing_even_when_enabled()
    {
        var bus = new JournalEventBus();
        // EDDN is enabled, but developer mode is active: no upload may occur.
        var (bridge, handler) = NewBridge(bus, EnabledSettings(), isSuppressed: () => true);
        await using var _ = bridge;

        bus.Publish(Live(Jump));
        await bridge.DisposeAsync();

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Successful_upload_is_recorded()
    {
        var bus = new JournalEventBus();
        var log = new CapturingReportingLog();
        var (bridge, _) = NewBridge(bus, EnabledSettings(), log);

        bus.Publish(Live(Jump));
        await bridge.DisposeAsync();   // flushes the uploader, which fires Completed

        var record = Assert.Single(log.Records);
        Assert.Equal(ReportingTarget.Eddn, record.Target);
        Assert.True(record.Success);
        Assert.Contains("journal", record.Summary);      // the $schemaRef of a journal message
        Assert.Contains("200", record.Status);
        Assert.Null(record.Payload);                     // payload logging is off by default
        Assert.Null(record.Error);
    }

    [Fact]
    public async Task Rejected_upload_records_the_failure()
    {
        var bus = new JournalEventBus();
        var log = new CapturingReportingLog();
        // 400 is a permanent EDDN reject (bad schema/version) — exactly what a validation log must show.
        var (bridge, _) = NewBridge(bus, EnabledSettings(), log, HttpStatusCode.BadRequest);

        bus.Publish(Live(Jump));
        await bridge.DisposeAsync();

        var record = Assert.Single(log.Records);
        Assert.False(record.Success);
        Assert.Contains("400", record.Status);
    }

    [Fact]
    public async Task Payload_logging_redacts_the_commander_name()
    {
        var bus = new JournalEventBus();
        var settings = EnabledSettings();
        settings.Reporting.LogPayloads = true;
        var log = new CapturingReportingLog();
        var (bridge, _) = NewBridge(bus, settings, log);

        // The commander name warms EDDN state and becomes the header uploaderID on the next upload.
        bus.Publish(Live("""{ "timestamp": "2020-01-01T00:00:00Z", "event": "Commander", "Name": "Jameson" }"""));
        bus.Publish(Live(Jump));
        await bridge.DisposeAsync();

        var record = Assert.Single(log.Records);
        Assert.NotNull(record.Payload);
        Assert.Contains("[redacted]", record.Payload);
        Assert.DoesNotContain("Jameson", record.Payload);   // the raw name must never reach the log
    }
}
