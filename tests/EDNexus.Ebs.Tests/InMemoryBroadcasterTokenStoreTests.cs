using EDNexus.Ebs.Services;

namespace EDNexus.Ebs.Tests;

public class InMemoryBroadcasterTokenStoreTests
{
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void CreateSession_TryConsumeSession_round_trips_and_is_single_use()
    {
        var store = new InMemoryBroadcasterTokenStore();
        var sessionId = store.CreateSession("http://localhost:59123/callback", "desktop-state", "challenge-abc", TimeSpan.FromMinutes(10));

        Assert.True(store.TryConsumeSession(sessionId, out var session));
        Assert.Equal("http://localhost:59123/callback", session.DesktopRedirectUri);
        Assert.Equal("desktop-state", session.DesktopState);
        Assert.Equal("challenge-abc", session.CodeChallenge);

        // Second consume of the same id must fail — sessions are single-use.
        Assert.False(store.TryConsumeSession(sessionId, out _));
    }

    [Fact]
    public void TryConsumeSession_returns_false_for_an_unknown_id()
    {
        var store = new InMemoryBroadcasterTokenStore();

        Assert.False(store.TryConsumeSession("does-not-exist", out _));
    }

    [Fact]
    public void TryConsumeSession_returns_false_once_the_TTL_has_elapsed()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryBroadcasterTokenStore(time);
        var sessionId = store.CreateSession("http://localhost:59123/callback", "state", "challenge", TimeSpan.FromMinutes(1));

        time.Now += TimeSpan.FromMinutes(2);

        Assert.False(store.TryConsumeSession(sessionId, out _));
    }

    [Fact]
    public void CreateAuthorizationCode_TryConsumeAuthorizationCode_round_trips_and_is_single_use()
    {
        var store = new InMemoryBroadcasterTokenStore();
        var pending = new PendingBroadcasterAuth("channel-1", "CMDR", "twitch-access", "twitch-refresh", DateTimeOffset.UtcNow.AddHours(4), "challenge-abc", default);
        var code = store.CreateAuthorizationCode(pending, TimeSpan.FromSeconds(60));

        Assert.True(store.TryConsumeAuthorizationCode(code, out var auth));
        Assert.Equal("channel-1", auth.ChannelId);
        Assert.Equal("challenge-abc", auth.CodeChallenge);

        Assert.False(store.TryConsumeAuthorizationCode(code, out _));
    }

    [Fact]
    public void TryConsumeAuthorizationCode_returns_false_once_the_TTL_has_elapsed()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryBroadcasterTokenStore(time);
        var pending = new PendingBroadcasterAuth("channel-1", "CMDR", "a", "r", DateTimeOffset.UtcNow.AddHours(4), "challenge", default);
        var code = store.CreateAuthorizationCode(pending, TimeSpan.FromSeconds(30));

        time.Now += TimeSpan.FromSeconds(31);

        Assert.False(store.TryConsumeAuthorizationCode(code, out _));
    }

    [Fact]
    public void IssueToken_TryGetByToken_round_trips()
    {
        var store = new InMemoryBroadcasterTokenStore();

        var record = store.IssueToken("channel-1", "CMDR_Jameson", "twitch-access", "twitch-refresh", DateTimeOffset.UtcNow.AddHours(4));

        Assert.NotEmpty(record.Token);
        Assert.True(store.TryGetByToken(record.Token, out var found));
        Assert.Equal("channel-1", found.ChannelId);
        Assert.Equal("CMDR_Jameson", found.Username);
        Assert.True(found.IsTwitchGrantValid);
    }

    [Fact]
    public void TryGetByToken_returns_false_for_an_unknown_token()
    {
        var store = new InMemoryBroadcasterTokenStore();

        Assert.False(store.TryGetByToken("not-a-real-token", out _));
    }

    [Fact]
    public void IssueToken_invalidates_any_prior_token_for_the_same_channel()
    {
        var store = new InMemoryBroadcasterTokenStore();
        var first = store.IssueToken("channel-1", "CMDR", "a1", "r1", DateTimeOffset.UtcNow.AddHours(1));

        var second = store.IssueToken("channel-1", "CMDR", "a2", "r2", DateTimeOffset.UtcNow.AddHours(1));

        Assert.False(store.TryGetByToken(first.Token, out _));
        Assert.True(store.TryGetByToken(second.Token, out var found));
        Assert.Equal(second.Token, found.Token);
    }

    [Fact]
    public void UpdateTwitchTokens_updates_the_grant_and_keeps_it_marked_valid()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryBroadcasterTokenStore(time);
        var record = store.IssueToken("channel-1", "CMDR", "old-access", "old-refresh", DateTimeOffset.UtcNow.AddMinutes(5));

        var newExpiry = DateTimeOffset.UtcNow.AddHours(4);
        store.UpdateTwitchTokens("channel-1", "new-access", "new-refresh", newExpiry);

        Assert.True(store.TryGetByToken(record.Token, out var found));
        Assert.Equal("new-access", found.TwitchAccessToken);
        Assert.Equal("new-refresh", found.TwitchRefreshToken);
        Assert.Equal(newExpiry, found.TwitchExpiresAtUtc);
        Assert.True(found.IsTwitchGrantValid);
        Assert.NotNull(found.LastRefreshedAtUtc);
    }

    [Fact]
    public void MarkTwitchGrantInvalid_flags_the_record_so_it_can_be_rejected_by_callers()
    {
        var store = new InMemoryBroadcasterTokenStore();
        var record = store.IssueToken("channel-1", "CMDR", "a", "r", DateTimeOffset.UtcNow.AddHours(1));

        store.MarkTwitchGrantInvalid("channel-1");

        Assert.True(store.TryGetByToken(record.Token, out var found));
        Assert.False(found.IsTwitchGrantValid);
    }

    [Fact]
    public void GetAllTokens_returns_every_issued_token()
    {
        var store = new InMemoryBroadcasterTokenStore();
        store.IssueToken("channel-1", "A", "a", "r", DateTimeOffset.UtcNow.AddHours(1));
        store.IssueToken("channel-2", "B", "a", "r", DateTimeOffset.UtcNow.AddHours(1));

        var all = store.GetAllTokens();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void Revoke_removes_the_token_and_returns_true_only_when_it_existed()
    {
        var store = new InMemoryBroadcasterTokenStore();
        var record = store.IssueToken("channel-1", "CMDR", "a", "r", DateTimeOffset.UtcNow.AddHours(1));

        Assert.True(store.Revoke(record.Token));
        Assert.False(store.TryGetByToken(record.Token, out _));
        Assert.False(store.Revoke(record.Token));
    }
}
