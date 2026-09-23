using System.Text.Json;

namespace EDNexus.Ebs.Tests;

public sealed class SqliteChannelStateStoreTests : IDisposable
{
    private readonly TempEbsDataDirectory _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void TryGet_returns_false_for_a_channel_that_never_published()
    {
        Assert.False(_data.CreateChannelStateStore().TryGet("channel-1", out _));
    }

    [Fact]
    public void The_last_published_state_survives_a_restart()
    {
        var store = _data.CreateChannelStateStore();
        store.Set("channel-1", JsonSerializer.SerializeToElement(new { system = "Sol", credits = 1 }));
        store.Set("channel-1", JsonSerializer.SerializeToElement(new { system = "Shinrarta Dezhra", credits = 2 }));
        store.Set("channel-2", JsonSerializer.SerializeToElement(new { system = "Colonia" }));

        var restarted = _data.CreateChannelStateStore();

        Assert.True(restarted.TryGet("channel-1", out var state));
        Assert.Equal("Shinrarta Dezhra", state.GetProperty("system").GetString());
        Assert.Equal(2, state.GetProperty("credits").GetInt32());
        Assert.True(restarted.TryGet("channel-2", out var other));
        Assert.Equal("Colonia", other.GetProperty("system").GetString());
    }

    [Fact]
    public void Returned_state_outlives_the_underlying_document()
    {
        var store = _data.CreateChannelStateStore();
        store.Set("channel-1", JsonSerializer.SerializeToElement(new { nested = new[] { 1, 2, 3 } }));

        Assert.True(store.TryGet("channel-1", out var state));
        GC.Collect();

        Assert.Equal(3, state.GetProperty("nested").GetArrayLength());
    }
}
