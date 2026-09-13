namespace EDNexus.Ebs.Services;

/// <summary>
/// Owns every piece of server-side state for the EBS-mediated Twitch OAuth flow: pending
/// authorize↔callback sessions, one-time authorization codes, and the long-lived per-broadcaster
/// tokens (and the underlying Twitch grants they wrap) handed out to desktop clients.
/// </summary>
public interface IBroadcasterTokenStore
{
    /// <summary>Records a new pending session for the desktop↔EBS leg of the flow. Returns the session id (used as Twitch's <c>state</c>).</summary>
    string CreateSession(string desktopRedirectUri, string desktopState, string codeChallenge, TimeSpan ttl);

    /// <summary>Retrieves and removes a pending session (single-use — a replayed Twitch callback can't reuse it). False if missing/expired.</summary>
    bool TryConsumeSession(string sessionId, out OAuthPendingSession session);

    /// <summary>Mints a one-time authorization code for the desktop to redeem at <c>/oauth/token</c>. Returns the code.</summary>
    string CreateAuthorizationCode(PendingBroadcasterAuth auth, TimeSpan ttl);

    /// <summary>Retrieves and removes a pending authorization (single-use). False if missing/expired.</summary>
    bool TryConsumeAuthorizationCode(string code, out PendingBroadcasterAuth auth);

    /// <summary>Mints (or re-mints, invalidating any prior token for the channel) the long-lived broadcaster token.</summary>
    BroadcasterToken IssueToken(string channelId, string username, string twitchAccessToken, string twitchRefreshToken, DateTimeOffset twitchExpiresAtUtc);

    /// <summary>Looks up a broadcaster by their long-lived EBS token, as presented to <c>/api/update-state</c>.</summary>
    bool TryGetByToken(string token, out BroadcasterToken record);

    /// <summary>Updates the stored Twitch grant for a channel after a successful background refresh.</summary>
    void UpdateTwitchTokens(string channelId, string twitchAccessToken, string twitchRefreshToken, DateTimeOffset twitchExpiresAtUtc);

    /// <summary>Marks a channel's underlying Twitch grant as no longer valid (e.g. a failed refresh), so <c>/api/update-state</c> starts rejecting it.</summary>
    void MarkTwitchGrantInvalid(string channelId);

    /// <summary>All currently-issued broadcaster tokens, for the background refresh loop to walk.</summary>
    IReadOnlyCollection<BroadcasterToken> GetAllTokens();

    /// <summary>Revokes (removes) a broadcaster's long-lived token, e.g. on logout.</summary>
    bool Revoke(string token);
}
