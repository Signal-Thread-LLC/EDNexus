using EDNexus.Ebs.Services;
using Microsoft.Data.Sqlite;

namespace EDNexus.Ebs.Tests;

public sealed class SqliteBroadcasterTokenStoreTests : IDisposable
{
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly TempEbsDataDirectory _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void IssueToken_TryGetByToken_round_trips()
    {
        var store = _data.CreateTokenStore();
        var expiry = DateTimeOffset.UtcNow.AddHours(4);

        var record = store.IssueToken("channel-1", "CMDR_Jameson", "twitch-access", "twitch-refresh", expiry);

        Assert.NotEmpty(record.Token);
        Assert.True(store.TryGetByToken(record.Token, out var found));
        Assert.Equal(record.Token, found.Token);
        Assert.Equal("channel-1", found.ChannelId);
        Assert.Equal("CMDR_Jameson", found.Username);
        Assert.Equal("twitch-access", found.TwitchAccessToken);
        Assert.Equal("twitch-refresh", found.TwitchRefreshToken);
        Assert.Equal(expiry, found.TwitchExpiresAtUtc);
        Assert.True(found.IsTwitchGrantValid);
        Assert.Null(found.LastRefreshedAtUtc);
    }

    [Fact]
    public void TryGetByToken_returns_false_for_an_unknown_token()
    {
        Assert.False(_data.CreateTokenStore().TryGetByToken("not-a-real-token", out _));
    }

    [Fact]
    public void An_issued_token_survives_a_restart()
    {
        var created = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var expiry = created.AddHours(4);
        var token = _data.CreateTokenStore(new FakeTimeProvider(created))
            .IssueToken("channel-1", "CMDR", "twitch-access", "twitch-refresh", expiry).Token;

        var restarted = _data.CreateTokenStore();

        Assert.True(restarted.TryGetByToken(token, out var found));
        Assert.Equal("channel-1", found.ChannelId);
        Assert.Equal("twitch-access", found.TwitchAccessToken);
        Assert.Equal("twitch-refresh", found.TwitchRefreshToken);
        Assert.Equal(expiry, found.TwitchExpiresAtUtc);
        Assert.Equal(created, found.CreatedAtUtc);
        Assert.True(found.IsTwitchGrantValid);
    }

    [Fact]
    public void IssueToken_invalidates_any_prior_token_for_the_same_channel_across_a_restart()
    {
        var store = _data.CreateTokenStore();
        var first = store.IssueToken("channel-1", "CMDR", "a1", "r1", DateTimeOffset.UtcNow.AddHours(1));
        var second = store.IssueToken("channel-1", "CMDR", "a2", "r2", DateTimeOffset.UtcNow.AddHours(1));

        var restarted = _data.CreateTokenStore();

        Assert.False(restarted.TryGetByToken(first.Token, out _));
        Assert.True(restarted.TryGetByToken(second.Token, out var found));
        Assert.Equal("a2", found.TwitchAccessToken);
        Assert.Single(restarted.GetAllTokens());
    }

    [Fact]
    public void UpdateTwitchTokens_is_durable_and_revalidates_the_grant()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = _data.CreateTokenStore(time);
        var record = store.IssueToken("channel-1", "CMDR", "old-access", "old-refresh", time.Now.AddMinutes(5));
        store.MarkTwitchGrantInvalid("channel-1");

        var newExpiry = time.Now.AddHours(4);
        store.UpdateTwitchTokens("channel-1", "new-access", "new-refresh", newExpiry);

        Assert.True(_data.CreateTokenStore().TryGetByToken(record.Token, out var found));
        Assert.Equal("new-access", found.TwitchAccessToken);
        Assert.Equal("new-refresh", found.TwitchRefreshToken);
        Assert.Equal(newExpiry, found.TwitchExpiresAtUtc);
        Assert.True(found.IsTwitchGrantValid);
        Assert.Equal(time.Now, found.LastRefreshedAtUtc);
    }

    [Fact]
    public void MarkTwitchGrantInvalid_is_durable()
    {
        var record = _data.CreateTokenStore().IssueToken("channel-1", "CMDR", "a", "r", DateTimeOffset.UtcNow.AddHours(1));

        _data.CreateTokenStore().MarkTwitchGrantInvalid("channel-1");

        Assert.True(_data.CreateTokenStore().TryGetByToken(record.Token, out var found));
        Assert.False(found.IsTwitchGrantValid);
    }

    [Fact]
    public void GetAllTokens_returns_every_grant_after_a_restart_keyed_by_channel()
    {
        var store = _data.CreateTokenStore();
        store.IssueToken("channel-1", "A", "a1", "r1", DateTimeOffset.UtcNow.AddHours(1));
        store.IssueToken("channel-2", "B", "a2", "r2", DateTimeOffset.UtcNow.AddHours(1));

        var all = _data.CreateTokenStore().GetAllTokens().OrderBy(t => t.ChannelId).ToList();

        Assert.Equal(["channel-1", "channel-2"], all.Select(t => t.ChannelId));
        Assert.Equal(["r1", "r2"], all.Select(t => t.TwitchRefreshToken));
        // Only a hash of the bearer token is stored, so it can't be recovered when enumerating.
        Assert.All(all, t => Assert.Equal("", t.Token));
    }

    [Fact]
    public void Revoke_is_durable_and_returns_true_only_when_the_token_existed()
    {
        var record = _data.CreateTokenStore().IssueToken("channel-1", "CMDR", "a", "r", DateTimeOffset.UtcNow.AddHours(1));

        Assert.True(_data.CreateTokenStore().Revoke(record.Token));

        var restarted = _data.CreateTokenStore();
        Assert.False(restarted.TryGetByToken(record.Token, out _));
        Assert.False(restarted.Revoke(record.Token));
        Assert.Empty(restarted.GetAllTokens());
    }

    [Fact]
    public void Nothing_secret_is_written_to_the_database_in_the_clear()
    {
        const string access = "twitch-access-PLAINTEXT-MARKER";
        const string refresh = "twitch-refresh-PLAINTEXT-MARKER";
        var store = _data.CreateTokenStore();
        var record = store.IssueToken("channel-1", "CMDR", access, refresh, DateTimeOffset.UtcNow.AddHours(1));
        store.UpdateTwitchTokens("channel-1", access + "-2", refresh + "-2", DateTimeOffset.UtcNow.AddHours(2));

        var onDisk = _data.ReadAllDatabaseBytes();

        Assert.Contains("channel-1", onDisk); // sanity: we are actually reading the rows
        Assert.DoesNotContain("PLAINTEXT-MARKER", onDisk);
        Assert.DoesNotContain(record.Token, onDisk);
        Assert.Contains(SqliteBroadcasterTokenStore.HashToken(record.Token), onDisk);
    }

    [Fact]
    public void A_grant_whose_encryption_key_was_lost_reads_back_as_invalid_instead_of_throwing()
    {
        var record = _data.CreateTokenStore().IssueToken("channel-1", "CMDR", "a", "r", DateTimeOffset.UtcNow.AddHours(1));

        var withFreshKeyRing = _data.CreateTokenStore(keysPath: Path.Combine(_data.Path, "some-other-key-ring"));

        Assert.True(withFreshKeyRing.TryGetByToken(record.Token, out var found));
        Assert.False(found.IsTwitchGrantValid);
        Assert.Equal("", found.TwitchRefreshToken);
        Assert.Single(withFreshKeyRing.GetAllTokens());
    }

    [Fact]
    public void Pending_sessions_and_codes_still_work_and_are_single_use()
    {
        var store = _data.CreateTokenStore();
        var sessionId = store.CreateSession("http://localhost:59123/callback", "desktop-state", "challenge", TimeSpan.FromMinutes(10));
        var code = store.CreateAuthorizationCode(
            new PendingBroadcasterAuth("channel-1", "CMDR", "a", "r", DateTimeOffset.UtcNow.AddHours(4), "challenge", "http://localhost:59123/callback", default),
            TimeSpan.FromSeconds(60));

        Assert.True(store.TryConsumeSession(sessionId, out var session));
        Assert.Equal("desktop-state", session.DesktopState);
        Assert.False(store.TryConsumeSession(sessionId, out _));
        Assert.True(store.TryConsumeAuthorizationCode(code, out var auth));
        Assert.Equal("channel-1", auth.ChannelId);
        Assert.False(store.TryConsumeAuthorizationCode(code, out _));
    }

    [Fact]
    public void Opening_a_database_from_a_newer_schema_version_fails_fast()
    {
        _data.OpenDatabase();
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(_data.Path, EbsDatabase.FileName)}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {EbsDatabase.SchemaVersion + 1};";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() => _data.OpenDatabase());
    }
}
