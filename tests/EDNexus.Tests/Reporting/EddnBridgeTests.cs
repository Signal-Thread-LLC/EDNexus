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
}
