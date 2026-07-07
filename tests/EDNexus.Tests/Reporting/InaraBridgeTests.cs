using EDNexus.Core.Journal;
using EDNexus.Core.Reporting;
using EDNexus.Core.Settings;
using EliteDangerous.Inara;
using Xunit;

namespace EDNexus.Tests.Reporting;

public class InaraBridgeTests
{
    private static readonly InaraClientOptions ClientOptions = new()
    {
        AppName = "T", AppVersion = "1", IsBeingDeveloped = true,
    };

    private static AppSettings EnabledSettings()
    {
        var s = new AppSettings();
        s.Reporting.Inara.Enabled = true;
        s.Reporting.Inara.ApiKey = "key";
        return s;
    }

    private static JournalEntry Live(string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var e));
        return e;
    }

    // Builds a bridge with zero debounce/throttle so triggers fire immediately in tests.
    private static (InaraBridge, RecordingHandler) NewBridge(JournalEventBus bus, AppSettings settings, Func<bool>? isSuppressed = null)
    {
        var handler = new RecordingHandler(body: """{ "header": { "eventStatus": 200 } }""");
        var client = new InaraClient(ClientOptions, new HttpClient(handler));
        var bridge = new InaraBridge(bus, settings, client, TimeSpan.Zero, TimeSpan.Zero, isSuppressed);
        return (bridge, handler);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return;
            await Task.Delay(15);
        }
    }

    [Fact]
    public async Task Accumulating_events_do_not_send_without_a_trigger()
    {
        var bus = new JournalEventBus();
        var settings = EnabledSettings();
        var (bridge, handler) = NewBridge(bus, settings);
        await using var _ = bridge;

        // Commander sets identity but is not a flush trigger; ranks/loadout only accumulate.
        bus.Publish(Live("""{ "timestamp": "2020-01-01T00:00:00Z", "event": "Commander", "Name": "Jameson" }"""));
        bus.Publish(Live("""{ "timestamp": "2020-01-01T00:00:01Z", "event": "Loadout", "Ship": "python", "ShipID": 7 }"""));
        bus.Publish(Live("""{ "timestamp": "2020-01-01T00:00:02Z", "event": "Rank", "Combat": 3 }"""));

        await Task.Delay(150);
        Assert.Equal(0, handler.CallCount);   // nothing sent — no trigger occurred
    }

    [Fact]
    public async Task Docking_flushes_the_batch()
    {
        var bus = new JournalEventBus();
        var (bridge, handler) = NewBridge(bus, EnabledSettings());
        await using var _ = bridge;

        bus.Publish(Live("""{ "timestamp": "2020-01-01T00:00:00Z", "event": "Commander", "Name": "Jameson" }"""));
        bus.Publish(Live("""
        { "timestamp": "2020-01-01T00:10:00Z", "event": "Docked",
          "StarSystem": "Sol", "StationName": "Abraham Lincoln", "MarketID": 128666762 }
        """));

        await WaitForAsync(() => handler.CallCount >= 1);
        Assert.True(handler.CallCount >= 1);
        Assert.Contains("addCommanderTravelDock", handler.Bodies[0]);
        Assert.Contains("Abraham Lincoln", handler.Bodies[0]);
    }

    [Fact]
    public async Task Fsd_jump_flushes_a_travel_event()
    {
        var bus = new JournalEventBus();
        var (bridge, handler) = NewBridge(bus, EnabledSettings());
        await using var _ = bridge;

        bus.Publish(Live("""{ "timestamp": "2020-01-01T00:00:00Z", "event": "LoadGame", "Commander": "Jameson", "FID": "F1", "Credits": 100 }"""));
        bus.Publish(Live("""
        { "timestamp": "2020-01-01T00:20:00Z", "event": "FSDJump",
          "StarSystem": "Alpha Centauri", "StarPos": [3.03, -0.09, 3.15], "JumpDist": 4.38 }
        """));

        await WaitForAsync(() => handler.Bodies.Any(b => b.Contains("addCommanderTravelFSDJump")));
        Assert.Contains(handler.Bodies, b => b.Contains("addCommanderTravelFSDJump") && b.Contains("Alpha Centauri"));
    }

    [Fact]
    public async Task Suppressed_reporter_never_sends_even_when_enabled()
    {
        var bus = new JournalEventBus();
        // Reporter is fully enabled, but developer mode is active — nothing must go out.
        var (bridge, handler) = NewBridge(bus, EnabledSettings(), isSuppressed: () => true);
        await using var _ = bridge;

        bus.Publish(Live("""{ "timestamp": "2020-01-01T00:00:00Z", "event": "LoadGame", "Commander": "Jameson", "FID": "F1", "Credits": 100 }"""));
        bus.Publish(Live("""{ "timestamp": "2020-01-01T00:10:00Z", "event": "Docked", "StarSystem": "Sol", "StationName": "X", "MarketID": 1 }"""));

        await Task.Delay(150);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Disabled_reporter_never_sends()
    {
        var bus = new JournalEventBus();
        var settings = new AppSettings();   // Inara disabled by default
        var (bridge, handler) = NewBridge(bus, settings);
        await using var _ = bridge;

        bus.Publish(Live("""{ "timestamp": "2020-01-01T00:00:00Z", "event": "LoadGame", "Commander": "Jameson", "FID": "F1", "Credits": 100 }"""));
        bus.Publish(Live("""{ "timestamp": "2020-01-01T00:10:00Z", "event": "Docked", "StarSystem": "Sol", "StationName": "X", "MarketID": 1 }"""));

        await Task.Delay(150);
        Assert.Equal(0, handler.CallCount);
    }
}
