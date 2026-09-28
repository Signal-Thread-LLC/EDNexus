using EDNexus.Core.Settings;
using EDNexus.Core.Twitch;
using Xunit;

namespace EDNexus.Tests.Twitch;

public class EbsCleanupQueueTests : IDisposable
{
    private const string ClearEndpoint = "https://ebs.example.com/api/update-state";
    private const string RevokeEndpoint = "https://ebs.example.com/oauth/revoke";

    private readonly string _root = Directory.CreateTempSubdirectory("ednexus-cleanup-").FullName;
    private readonly FakeStreamStateApiClient _state = new();
    private readonly ScriptedRevokeClient _auth = new();

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private SettingsStore Store => new(Path.Combine(_root, "settings.json"));

    private (AppSettings Settings, EbsCleanupQueue Queue) NewQueue()
    {
        var store = Store;
        var settings = store.Load();
        return (settings, new EbsCleanupQueue(settings, store, _state, _auth));
    }

    [Fact]
    public void An_enqueued_clear_is_on_disk_before_anything_is_sent()
    {
        var (_, queue) = NewQueue();

        queue.Enqueue(EbsCleanupKind.ClearCard, ClearEndpoint, "ebs-token");

        // What a restarted app would find.
        var reloaded = Store.Load().Twitch.PendingCleanups;
        var entry = Assert.Single(reloaded);
        Assert.Equal(EbsCleanupKind.ClearCard, entry.Kind);
        Assert.Equal(ClearEndpoint, entry.Endpoint);
        Assert.Equal("ebs-token", entry.Token);
        Assert.Empty(_state.Clears);
    }

    [Fact]
    public void Enqueueing_the_same_request_twice_keeps_one()
    {
        var (_, queue) = NewQueue();

        queue.Enqueue(EbsCleanupKind.ClearCard, ClearEndpoint, "ebs-token");
        queue.Enqueue(EbsCleanupKind.ClearCard, ClearEndpoint, "ebs-token");

        Assert.Single(queue.Pending);
    }

    [Fact]
    public async Task A_failed_clear_stays_queued_until_the_EBS_acknowledges_it()
    {
        var (_, queue) = NewQueue();
        queue.Enqueue(EbsCleanupKind.ClearCard, ClearEndpoint, "ebs-token");

        _state.RespondToClear = () => new StreamStatePublishResult(StreamStatePublishStatus.Failed, "EBS unreachable");
        Assert.Equal(1, await queue.RetryPendingAsync());
        Assert.Single(Store.Load().Twitch.PendingCleanups);

        _state.RespondToClear = () => StreamStatePublishResult.ClearedOk;
        Assert.Equal(0, await queue.RetryPendingAsync());
        Assert.Empty(Store.Load().Twitch.PendingCleanups);
        Assert.Equal(new[] { "ebs-token", "ebs-token" }, _state.Clears.ToArray());
    }

    [Fact]
    public async Task A_rejected_token_ends_the_retries()
    {
        var (_, queue) = NewQueue();
        queue.Enqueue(EbsCleanupKind.ClearCard, ClearEndpoint, "revoked-token");

        // The EBS no longer knows the token: it was revoked (which cleared the card) or never
        // issued there. Retrying cannot change that.
        _state.RespondToClear = () => new StreamStatePublishResult(StreamStatePublishStatus.Unauthorized, "HTTP 401");

        Assert.Equal(0, await queue.RetryPendingAsync());
    }

    [Fact]
    public async Task A_rate_limited_clear_is_retried()
    {
        var (_, queue) = NewQueue();
        queue.Enqueue(EbsCleanupKind.ClearCard, ClearEndpoint, "ebs-token");
        _state.RespondToClear = () => new StreamStatePublishResult(StreamStatePublishStatus.RateLimited, "HTTP 429");

        Assert.Equal(1, await queue.RetryPendingAsync());
    }

    [Fact]
    public async Task A_revoke_is_retried_while_the_EBS_is_unreachable()
    {
        var (_, queue) = NewQueue();
        queue.Enqueue(EbsCleanupKind.Revoke, RevokeEndpoint, "ebs-token");

        _auth.Fail = true;
        Assert.Equal(1, await queue.RetryPendingAsync());

        _auth.Fail = false;
        Assert.Equal(0, await queue.RetryPendingAsync());
        Assert.Equal(RevokeEndpoint, _auth.LastEndpoint);
        Assert.Equal("ebs-token", _auth.LastToken);
    }

    [Fact]
    public async Task A_cleartext_endpoint_is_dropped_without_sending_the_token()
    {
        var (_, queue) = NewQueue();
        queue.Enqueue(EbsCleanupKind.Revoke, "http://ebs.example.com/oauth/revoke", "ebs-token");

        Assert.Equal(0, await queue.RetryPendingAsync());
        Assert.Null(_auth.LastToken);
    }

    [Fact]
    public void Complete_drops_only_an_acknowledged_clear()
    {
        var (_, queue) = NewQueue();
        var entry = queue.Enqueue(EbsCleanupKind.ClearCard, ClearEndpoint, "ebs-token");

        queue.Complete(entry, new StreamStatePublishResult(StreamStatePublishStatus.Failed, "timeout"));
        Assert.Single(queue.Pending);

        queue.Complete(entry, StreamStatePublishResult.ClearedOk);
        Assert.Empty(queue.Pending);
        Assert.Empty(Store.Load().Twitch.PendingCleanups);
    }

    [Fact]
    public async Task A_clear_left_over_from_a_previous_run_is_sent_on_start()
    {
        var (_, first) = NewQueue();
        first.Enqueue(EbsCleanupKind.ClearCard, ClearEndpoint, "ebs-token");
        first.Dispose();

        // The next launch.
        var (settings, queue) = NewQueue();
        using (queue)
        {
            queue.Start();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (settings.Twitch.PendingCleanups.Count > 0 && DateTime.UtcNow < deadline)
                await Task.Delay(20);
        }

        Assert.Empty(Store.Load().Twitch.PendingCleanups);
        Assert.Single(_state.Clears);
    }

    [Fact]
    public async Task A_clear_still_failing_after_a_day_is_given_up_but_a_revoke_is_not()
    {
        var time = new FakeTime(DateTimeOffset.UtcNow);
        var store = Store;
        var queue = new EbsCleanupQueue(store.Load(), store, _state, _auth, time);
        queue.Enqueue(EbsCleanupKind.ClearCard, ClearEndpoint, "ebs-token");
        queue.Enqueue(EbsCleanupKind.Revoke, RevokeEndpoint, "ebs-token");
        _state.RespondToClear = () => new StreamStatePublishResult(StreamStatePublishStatus.Failed, "HTTP 502");
        _auth.Fail = true;

        time.Now += TimeSpan.FromHours(23);
        Assert.Equal(2, await queue.RetryPendingAsync());

        // Past the EBS's own snapshot age limit a clear achieves nothing; a revoke still matters.
        time.Now += TimeSpan.FromHours(2);
        Assert.Equal(1, await queue.RetryPendingAsync());
        Assert.Equal(EbsCleanupKind.Revoke, Assert.Single(queue.Pending).Kind);
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ScriptedRevokeClient : IEbsAuthApiClient
    {
        public bool Fail { get; set; }
        public string? LastEndpoint { get; private set; }
        public string? LastToken { get; private set; }

        public Task<EbsTokenResponse> ExchangeCodeAsync(string tokenEndpoint, string code, string codeVerifier, string redirectUri, CancellationToken ct = default) =>
            throw new InvalidOperationException("not used");

        public Task RevokeAsync(string revokeEndpoint, string token, CancellationToken ct = default)
        {
            LastEndpoint = revokeEndpoint;
            LastToken = token;
            return Fail ? throw new HttpRequestException("EBS unreachable") : Task.CompletedTask;
        }
    }
}
