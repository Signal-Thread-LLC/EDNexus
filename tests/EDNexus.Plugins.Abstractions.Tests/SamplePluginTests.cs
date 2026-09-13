namespace EDNexus.Plugins.Abstractions.Tests;

public class SamplePluginTests
{
    [Fact]
    public void Initialize_WiresEventsAndStoresState()
    {
        var context = new FakePluginContext
        {
            State = new FakeCommanderState { LastUpdated = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
        };
        var plugin = new SamplePlugin();

        plugin.Initialize(context);

        Assert.True(plugin.Initialized);
        Assert.Same(context, plugin.LastContext);
        Assert.Equal("2026-01-01T00:00:00.0000000+00:00", context.Storage.GetString("last-run"));
    }

    [Fact]
    public void Subscribe_ReceivesPublishedEvent()
    {
        var context = new FakePluginContext();
        var plugin = new SamplePlugin();
        plugin.Initialize(context);

        context.Events.Publish(new FakeJournalEvent("FSDJump", DateTimeOffset.UtcNow, isHistorical: true));

        Assert.Contains(((FakePluginLog)context.Log).Messages, m => m.Contains("historical=True"));
    }

    [Fact]
    public void Shutdown_MarksPluginStopped()
    {
        var plugin = new SamplePlugin();
        plugin.Initialize(new FakePluginContext());

        plugin.Shutdown();

        Assert.True(plugin.ShutDown);
    }
}
