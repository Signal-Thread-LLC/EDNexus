using System.Collections.Concurrent;
using EDNexus.Ebs.Security;

namespace EDNexus.Ebs.Services;

/// <summary>
/// The short-lived half of <see cref="IBroadcasterTokenStore"/>: pending authorize↔callback sessions
/// (minutes) and one-time authorization codes (seconds). Deliberately process-local in every store
/// implementation — a restart mid-login only means the commander clicks "Log in" again, whereas
/// persisting pending authorization codes would write Twitch refresh tokens to disk for a window
/// measured in seconds.
/// </summary>
internal sealed class OAuthPendingState
{
    private readonly ConcurrentDictionary<string, OAuthPendingSession> _sessions = new();
    private readonly ConcurrentDictionary<string, PendingBroadcasterAuth> _pendingAuth = new();
    private readonly TimeProvider _timeProvider;

    public OAuthPendingState(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public string CreateSession(string desktopRedirectUri, string desktopState, string codeChallenge, TimeSpan ttl)
    {
        var sessionId = OpaqueToken.Generate(OpaqueToken.ShortLivedByteLength);
        _sessions[sessionId] = new OAuthPendingSession(desktopRedirectUri, desktopState, codeChallenge, _timeProvider.GetUtcNow() + ttl);
        return sessionId;
    }

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

    public string CreateAuthorizationCode(PendingBroadcasterAuth auth, TimeSpan ttl)
    {
        var code = OpaqueToken.Generate(OpaqueToken.ShortLivedByteLength);
        _pendingAuth[code] = auth with { ExpiresAtUtc = _timeProvider.GetUtcNow() + ttl };
        return code;
    }

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
}
