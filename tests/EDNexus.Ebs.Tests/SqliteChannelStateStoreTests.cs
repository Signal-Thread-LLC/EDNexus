using System.Text.Json;

namespace EDNexus.Ebs.Tests;

public sealed class SqliteChannelStateStoreTests : IDisposable
{
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

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
    public void Remove_is_durable()
    {
        _data.CreateChannelStateStore().Set("channel-1", JsonSerializer.SerializeToElement(new { system = "Sol" }));

        _data.CreateChannelStateStore().Remove("channel-1");
        _data.CreateChannelStateStore().Remove("never-published"); // no-op, not an error

        Assert.False(_data.CreateChannelStateStore().TryGet("channel-1", out _));
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

    [Fact]
    public void A_snapshot_older_than_the_max_age_is_not_served_and_is_pruned()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var maxAge = TimeSpan.FromHours(24);
        _data.CreateChannelStateStore(time, maxAge).Set("channel-1", JsonSerializer.SerializeToElement(new { system = "Sol" }));

        time.Now += TimeSpan.FromHours(23);
        Assert.True(_data.CreateChannelStateStore(time, maxAge).TryGet("channel-1", out _));

        // A clear that never reached the EBS must not keep the card public forever.
        time.Now += TimeSpan.FromHours(2);
        Assert.False(_data.CreateChannelStateStore(time, maxAge).TryGet("channel-1", out _));

        // Pruned, not just hidden: lifting the limit does not bring it back.
        Assert.False(_data.CreateChannelStateStore(time).TryGet("channel-1", out _));
    }

    [Fact]
    public void Publishing_prunes_other_channels_expired_snapshots()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var maxAge = TimeSpan.FromHours(24);
        var store = _data.CreateChannelStateStore(time, maxAge);
        store.Set("abandoned", JsonSerializer.SerializeToElement(new { system = "Sol" }));

        time.Now += TimeSpan.FromDays(2);
        store.Set("active", JsonSerializer.SerializeToElement(new { system = "Colonia" }));

        // Read without a limit, so only a real delete explains the absence.
        var unlimited = _data.CreateChannelStateStore(time);
        Assert.False(unlimited.TryGet("abandoned", out _));
        Assert.True(unlimited.TryGet("active", out _));
    }
}
