using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EDNexus.Ebs.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace EDNexus.Ebs.Services;

/// <summary>
/// Durable <see cref="IBroadcasterTokenStore"/> backed by <see cref="EbsDatabase"/>: long-lived
/// broadcaster tokens and the Twitch grants they wrap survive crashes, restarts and redeploys, so a
/// commander stays logged in. Nothing secret is written in the clear: the EBS bearer token is stored
/// only as a SHA-256 hash, and the Twitch access/refresh tokens are ASP.NET Core Data Protection
/// ciphertext. Pending OAuth sessions and one-time codes stay in memory (see <see cref="OAuthPendingState"/>).
/// </summary>
/// <remarks>
/// Records returned here are snapshots: change a grant through <see cref="UpdateTwitchTokens"/> /
/// <see cref="MarkTwitchGrantInvalid"/>, never by mutating a returned <see cref="BroadcasterToken"/>.
/// </remarks>
public sealed class SqliteBroadcasterTokenStore : IBroadcasterTokenStore
{
    /// <summary>Data Protection purpose string for the Twitch token columns. Changing it orphans every stored grant.</summary>
    public const string ProtectorPurpose = "EDNexus.Ebs.BroadcasterTokenStore.TwitchTokens.v1";

    private const string SelectColumns =
        "channel_id, username, twitch_access_token, twitch_refresh_token, twitch_expires_at, is_twitch_grant_valid, created_at, last_refreshed_at";

    private readonly EbsDatabase _database;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqliteBroadcasterTokenStore> _logger;
    private readonly OAuthPendingState _pending;

    public SqliteBroadcasterTokenStore(
        EbsDatabase database,
        IDataProtectionProvider dataProtection,
        TimeProvider? timeProvider = null,
        ILogger<SqliteBroadcasterTokenStore>? logger = null)
    {
        _database = database;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SqliteBroadcasterTokenStore>.Instance;
        _pending = new OAuthPendingState(_timeProvider);
    }

    /// <inheritdoc />
    public string CreateSession(string desktopRedirectUri, string desktopState, string codeChallenge, TimeSpan ttl) =>
        _pending.CreateSession(desktopRedirectUri, desktopState, codeChallenge, ttl);

    /// <inheritdoc />
    public bool TryConsumeSession(string sessionId, out OAuthPendingSession session) => _pending.TryConsumeSession(sessionId, out session);

    /// <inheritdoc />
    public string CreateAuthorizationCode(PendingBroadcasterAuth auth, TimeSpan ttl) => _pending.CreateAuthorizationCode(auth, ttl);

    /// <inheritdoc />
    public bool TryConsumeAuthorizationCode(string code, out PendingBroadcasterAuth auth) => _pending.TryConsumeAuthorizationCode(code, out auth);

    /// <inheritdoc />
    public BroadcasterToken IssueToken(string channelId, string username, string twitchAccessToken, string twitchRefreshToken, DateTimeOffset twitchExpiresAtUtc)
    {
        var record = new BroadcasterToken
        {
            Token = OpaqueToken.Generate(),
            ChannelId = channelId,
            Username = username,
            TwitchAccessToken = twitchAccessToken,
            TwitchRefreshToken = twitchRefreshToken,
            TwitchExpiresAtUtc = twitchExpiresAtUtc,
            CreatedAtUtc = _timeProvider.GetUtcNow(),
        };

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        // channel_id is the primary key, so REPLACE drops any prior row (and with it the prior token's
        // hash) in the same statement — re-authenticating invalidates the old token atomically.
        command.CommandText =
            """
            INSERT OR REPLACE INTO broadcaster_tokens
                (channel_id, token_hash, username, twitch_access_token, twitch_refresh_token, twitch_expires_at, is_twitch_grant_valid, created_at, last_refreshed_at)
            VALUES ($channel, $hash, $username, $access, $refresh, $expires, 1, $created, NULL);
            """;
        command.Parameters.AddWithValue("$channel", channelId);
        command.Parameters.AddWithValue("$hash", HashToken(record.Token));
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$access", _protector.Protect(twitchAccessToken));
        command.Parameters.AddWithValue("$refresh", _protector.Protect(twitchRefreshToken));
        command.Parameters.AddWithValue("$expires", FormatTimestamp(twitchExpiresAtUtc));
        command.Parameters.AddWithValue("$created", FormatTimestamp(record.CreatedAtUtc));
        command.ExecuteNonQuery();

        return record;
    }

    /// <inheritdoc />
    public bool TryGetByToken(string token, out BroadcasterToken record)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM broadcaster_tokens WHERE token_hash = $hash;";
        command.Parameters.AddWithValue("$hash", HashToken(token));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            record = null!;
            return false;
        }

        record = ReadRecord(reader, token);
        return true;
    }

    /// <inheritdoc />
    public void UpdateTwitchTokens(string channelId, string twitchAccessToken, string twitchRefreshToken, DateTimeOffset twitchExpiresAtUtc)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE broadcaster_tokens
            SET twitch_access_token = $access, twitch_refresh_token = $refresh, twitch_expires_at = $expires,
                is_twitch_grant_valid = 1, last_refreshed_at = $refreshed
            WHERE channel_id = $channel;
            """;
        command.Parameters.AddWithValue("$channel", channelId);
        command.Parameters.AddWithValue("$access", _protector.Protect(twitchAccessToken));
        command.Parameters.AddWithValue("$refresh", _protector.Protect(twitchRefreshToken));
        command.Parameters.AddWithValue("$expires", FormatTimestamp(twitchExpiresAtUtc));
        command.Parameters.AddWithValue("$refreshed", FormatTimestamp(_timeProvider.GetUtcNow()));
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void MarkTwitchGrantInvalid(string channelId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE broadcaster_tokens SET is_twitch_grant_valid = 0 WHERE channel_id = $channel;";
        command.Parameters.AddWithValue("$channel", channelId);
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only a hash of each bearer token is stored, so <see cref="BroadcasterToken.Token"/> is empty on
    /// the records returned here. Enumerate by <see cref="BroadcasterToken.ChannelId"/>.
    /// </remarks>
    public IReadOnlyCollection<BroadcasterToken> GetAllTokens()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM broadcaster_tokens;";

        using var reader = command.ExecuteReader();
        var records = new List<BroadcasterToken>();
        while (reader.Read())
            records.Add(ReadRecord(reader, token: ""));
        return records;
    }

    /// <inheritdoc />
    public bool Revoke(string token)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM broadcaster_tokens WHERE token_hash = $hash;";
        command.Parameters.AddWithValue("$hash", HashToken(token));
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// SHA-256 of the bearer token, hex-encoded. Unsalted is fine: the token is 32 random bytes, so
    /// there's nothing to brute-force or look up — the hash only has to keep a leaked database file
    /// from handing out working credentials.
    /// </summary>
    internal static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private BroadcasterToken ReadRecord(SqliteDataReader reader, string token)
    {
        var channelId = reader.GetString(0);
        var isGrantValid = reader.GetInt64(5) != 0;
        string access = "", refresh = "";

        try
        {
            access = _protector.Unprotect(reader.GetString(2));
            refresh = _protector.Unprotect(reader.GetString(3));
        }
        catch (CryptographicException ex)
        {
            // The Data Protection key ring that encrypted this grant is gone (lost/rotated-out key
            // directory). The grant is unrecoverable; surface it as invalid so /api/update-state
            // answers 401 and the desktop client prompts the commander to log in again.
            _logger.LogWarning(ex, "Could not decrypt the stored Twitch grant for channel {ChannelId}; treating it as invalid until the broadcaster re-authenticates.", channelId);
            isGrantValid = false;
        }

        return new BroadcasterToken
        {
            Token = token,
            ChannelId = channelId,
            Username = reader.GetString(1),
            TwitchAccessToken = access,
            TwitchRefreshToken = refresh,
            TwitchExpiresAtUtc = ParseTimestamp(reader.GetString(4)),
            IsTwitchGrantValid = isGrantValid,
            CreatedAtUtc = ParseTimestamp(reader.GetString(6)),
            LastRefreshedAtUtc = reader.IsDBNull(7) ? null : ParseTimestamp(reader.GetString(7)),
        };
    }

    private static string FormatTimestamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
