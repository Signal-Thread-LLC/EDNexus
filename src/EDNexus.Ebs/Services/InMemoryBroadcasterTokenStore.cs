using System.Collections.Concurrent;
using EDNexus.Ebs.Security;

namespace EDNexus.Ebs.Services;

/// <summary>
/// Process-local, in-memory implementation of <see cref="IBroadcasterTokenStore"/>. Sufficient for a
/// single EBS instance; a multi-instance deployment needs a real shared store (e.g. a database or
/// Redis) instead — see the README for the production caveat this carries.
/// </summary>
public sealed class InMemoryBroadcasterTokenStore : IBroadcasterTokenStore
{
    private readonly ConcurrentDictionary<string, OAuthPendingSession> _sessions = new();
    private readonly ConcurrentDictionary<string, PendingBroadcasterAuth> _pendingAuth = new();
    private readonly ConcurrentDictionary<string, BroadcasterToken> _tokensByToken = new();
    private readonly ConcurrentDictionary<string, string> _tokenByChannel = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryBroadcasterTokenStore(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public string CreateSession(string desktopRedirectUri, string desktopState, string codeChallenge, TimeSpan ttl)
    {
        var sessionId = OpaqueToken.Generate(OpaqueToken.ShortLivedByteLength);
        _sessions[sessionId] = new OAuthPendingSession(desktopRedirectUri, desktopState, codeChallenge, _timeProvider.GetUtcNow() + ttl);
        return sessionId;
    }

    /// <inheritdoc />
    public bool TryConsumeSession(string sessionId, out OAuthPendingSession session)
    {
        if (!_sessions.TryRemove(sessionId, out var found) || found.ExpiresAtUtc < _timeProvider.GetUtcNow())
        {
            session = null!;
            return false;
        }

        session = found;
        return true;
    }

    /// <inheritdoc />
    public string CreateAuthorizationCode(PendingBroadcasterAuth auth, TimeSpan ttl)
    {
        var code = OpaqueToken.Generate(OpaqueToken.ShortLivedByteLength);
        _pendingAuth[code] = auth with { ExpiresAtUtc = _timeProvider.GetUtcNow() + ttl };
        return code;
    }

    /// <inheritdoc />
    public bool TryConsumeAuthorizationCode(string code, out PendingBroadcasterAuth auth)
    {
        if (!_pendingAuth.TryRemove(code, out var found) || found.ExpiresAtUtc < _timeProvider.GetUtcNow())
        {
            auth = null!;
            return false;
        }

        auth = found;
        return true;
    }

    /// <inheritdoc />
    public BroadcasterToken IssueToken(string channelId, string username, string twitchAccessToken, string twitchRefreshToken, DateTimeOffset twitchExpiresAtUtc)
    {
        // Re-authenticating invalidates any prior token for this channel — one live credential per broadcaster.
        if (_tokenByChannel.TryRemove(channelId, out var previousToken))
        {
            _tokensByToken.TryRemove(previousToken, out _);
        }

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

        _tokensByToken[record.Token] = record;
        _tokenByChannel[channelId] = record.Token;
        return record;
    }

    /// <inheritdoc />
    public bool TryGetByToken(string token, out BroadcasterToken record) => _tokensByToken.TryGetValue(token, out record!);

    /// <inheritdoc />
    public void UpdateTwitchTokens(string channelId, string twitchAccessToken, string twitchRefreshToken, DateTimeOffset twitchExpiresAtUtc)
    {
        if (!_tokenByChannel.TryGetValue(channelId, out var token) || !_tokensByToken.TryGetValue(token, out var record))
            return;

        record.TwitchAccessToken = twitchAccessToken;
        record.TwitchRefreshToken = twitchRefreshToken;
        record.TwitchExpiresAtUtc = twitchExpiresAtUtc;
        record.IsTwitchGrantValid = true;
        record.LastRefreshedAtUtc = _timeProvider.GetUtcNow();
    }

    /// <inheritdoc />
    public void MarkTwitchGrantInvalid(string channelId)
    {
        if (_tokenByChannel.TryGetValue(channelId, out var token) && _tokensByToken.TryGetValue(token, out var record))
            record.IsTwitchGrantValid = false;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<BroadcasterToken> GetAllTokens() => _tokensByToken.Values.ToList();

    /// <inheritdoc />
    public bool Revoke(string token)
    {
        if (!_tokensByToken.TryRemove(token, out var record))
            return false;

        _tokenByChannel.TryRemove(record.ChannelId, out _);
        return true;
    }
}
