using EDNexus.Ebs.Options;
using EDNexus.Ebs.Security;
using EDNexus.Ebs.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EDNexus.Ebs.Tests;

public class TwitchTokenRefreshBackgroundServiceTests
{
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeTwitchOAuthClient : ITwitchOAuthClient
    {
        public Func<string, TwitchTokenResponse>? OnRefresh { get; set; }
        public int RefreshCalls { get; private set; }

        public Task<TwitchTokenResponse> ExchangeAuthorizationCodeAsync(string code, string redirectUri, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TwitchTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
        {
            RefreshCalls++;
            var result = OnRefresh?.Invoke(refreshToken) ?? throw new InvalidOperationException("OnRefresh not configured");
            return Task.FromResult(result);
        }

        public Task<TwitchUser?> GetUserAsync(string accessToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeTokenAsync(string token, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static TwitchTokenRefreshBackgroundService CreateService(
        IBroadcasterTokenStore store,
        ITwitchOAuthClient twitch,
        FakeTimeProvider time,
        EbsOptions? options = null) =>
        new(store, twitch, Microsoft.Extensions.Options.Options.Create(options ?? new EbsOptions()), time, NullLogger<TwitchTokenRefreshBackgroundService>.Instance);

    [Fact]
    public async Task RefreshDueTokensAsync_refreshes_a_token_nearing_expiry()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryBroadcasterTokenStore(time);
        var record = store.IssueToken("channel-1", "CMDR", "old-access", "old-refresh", time.Now.AddMinutes(10));

        var twitch = new FakeTwitchOAuthClient
        {
            OnRefresh = refreshToken =>
            {
                Assert.Equal("old-refresh", refreshToken);
                return new TwitchTokenResponse { AccessToken = "new-access", RefreshToken = "new-refresh", ExpiresIn = 14400 };
            },
        };
        var options = new EbsOptions { TwitchTokenRefreshBufferMinutes = 60 }; // 10 minutes left is within the 60-minute buffer
        var service = CreateService(store, twitch, time, options);

        await service.RefreshDueTokensAsync(CancellationToken.None);

        Assert.Equal(1, twitch.RefreshCalls);
        Assert.True(store.TryGetByToken(record.Token, out var updated));
        Assert.Equal("new-access", updated.TwitchAccessToken);
        Assert.Equal("new-refresh", updated.TwitchRefreshToken);
        Assert.True(updated.IsTwitchGrantValid);
    }

    [Fact]
    public async Task RefreshDueTokensAsync_does_not_refresh_a_token_that_is_not_yet_within_the_buffer()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryBroadcasterTokenStore(time);
        store.IssueToken("channel-1", "CMDR", "access", "refresh", time.Now.AddHours(5));

        var twitch = new FakeTwitchOAuthClient();
        var options = new EbsOptions { TwitchTokenRefreshBufferMinutes = 60 };
        var service = CreateService(store, twitch, time, options);

        await service.RefreshDueTokensAsync(CancellationToken.None);

        Assert.Equal(0, twitch.RefreshCalls);
    }

    [Fact]
    public async Task RefreshDueTokensAsync_marks_the_grant_invalid_when_Twitch_rejects_the_refresh()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryBroadcasterTokenStore(time);
        var record = store.IssueToken("channel-1", "CMDR", "access", "revoked-refresh", time.Now.AddMinutes(1));

        var twitch = new FakeTwitchOAuthClient { OnRefresh = _ => throw new TwitchOAuthException("invalid refresh token", 400) };
        var options = new EbsOptions { TwitchTokenRefreshBufferMinutes = 60 };
        var service = CreateService(store, twitch, time, options);

        await service.RefreshDueTokensAsync(CancellationToken.None);

        Assert.True(store.TryGetByToken(record.Token, out var updated));
        Assert.False(updated.IsTwitchGrantValid);
    }

    [Fact]
    public async Task RefreshDueTokensAsync_skips_tokens_already_marked_invalid()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryBroadcasterTokenStore(time);
        store.IssueToken("channel-1", "CMDR", "access", "refresh", time.Now.AddMinutes(1));
        store.MarkTwitchGrantInvalid("channel-1");

        var twitch = new FakeTwitchOAuthClient();
        var options = new EbsOptions { TwitchTokenRefreshBufferMinutes = 60 };
        var service = CreateService(store, twitch, time, options);

        await service.RefreshDueTokensAsync(CancellationToken.None);

        Assert.Equal(0, twitch.RefreshCalls);
    }

    [Fact]
    public async Task RefreshDueTokensAsync_continues_past_one_channels_failure_to_refresh_the_rest()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryBroadcasterTokenStore(time);
        store.IssueToken("channel-fails", "A", "access", "bad-refresh", time.Now.AddMinutes(1));
        var goodRecord = store.IssueToken("channel-ok", "B", "access", "good-refresh", time.Now.AddMinutes(1));

        var twitch = new FakeTwitchOAuthClient
        {
            OnRefresh = refreshToken => refreshToken == "bad-refresh"
                ? throw new TwitchOAuthException("nope", 400)
                : new TwitchTokenResponse { AccessToken = "new-access", RefreshToken = "new-refresh", ExpiresIn = 3600 },
        };
        var options = new EbsOptions { TwitchTokenRefreshBufferMinutes = 60 };
        var service = CreateService(store, twitch, time, options);

        await service.RefreshDueTokensAsync(CancellationToken.None);

        Assert.Equal(2, twitch.RefreshCalls);
        Assert.True(store.TryGetByToken(goodRecord.Token, out var updated));
        Assert.True(updated.IsTwitchGrantValid);
        Assert.Equal("new-access", updated.TwitchAccessToken);
    }
}
