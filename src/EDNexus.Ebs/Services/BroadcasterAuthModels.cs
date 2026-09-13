namespace EDNexus.Ebs.Services;

/// <summary>
/// A pending OAuth session created by <c>GET /oauth/authorize</c> and consumed by
/// <c>GET /oauth/callback</c>: the desktop client's own loopback redirect/state, and the PKCE code
/// challenge it committed to, carried across the EBS↔Twitch leg of the flow via Twitch's own
/// <c>state</c> parameter (set to this session's id).
/// </summary>
/// <param name="DesktopRedirectUri">The desktop's loopback listener URI, to redirect back to once the EBS is done.</param>
/// <param name="DesktopState">The desktop's own CSRF state token, echoed back verbatim so its listener can validate it.</param>
/// <param name="CodeChallenge">The PKCE S256 code challenge the desktop committed to when it started the flow.</param>
/// <param name="ExpiresAtUtc">When this session stops being redeemable.</param>
public sealed record OAuthPendingSession(string DesktopRedirectUri, string DesktopState, string CodeChallenge, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// A one-time authorization code minted by <c>/oauth/callback</c> after the EBS has already
/// completed the real Twitch handshake, redeemed by the desktop at <c>POST /oauth/token</c> (with
/// the matching PKCE verifier) for the long-lived broadcaster token.
/// </summary>
/// <param name="ChannelId">The broadcaster's identified Twitch user/channel id.</param>
/// <param name="Username">The broadcaster's Twitch display name (or login, if no display name).</param>
/// <param name="TwitchAccessToken">The broadcaster's Twitch access token, held only server-side.</param>
/// <param name="TwitchRefreshToken">The broadcaster's Twitch refresh token, held only server-side.</param>
/// <param name="TwitchExpiresAtUtc">When <paramref name="TwitchAccessToken"/> expires.</param>
/// <param name="CodeChallenge">The PKCE code challenge from the originating session, that the exchange must satisfy.</param>
/// <param name="ExpiresAtUtc">When this authorization code stops being redeemable.</param>
public sealed record PendingBroadcasterAuth(
    string ChannelId,
    string Username,
    string TwitchAccessToken,
    string TwitchRefreshToken,
    DateTimeOffset TwitchExpiresAtUtc,
    string CodeChallenge,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// The long-lived, per-broadcaster credential the EBS hands to the desktop client, and the
/// underlying Twitch grant it wraps. The desktop client only ever sees <see cref="Token"/> — never
/// <see cref="TwitchAccessToken"/>/<see cref="TwitchRefreshToken"/>.
/// </summary>
public sealed class BroadcasterToken
{
    /// <summary>The opaque, long-lived bearer token handed to the desktop client.</summary>
    public required string Token { get; init; }

    /// <summary>The broadcaster's Twitch user/channel id.</summary>
    public required string ChannelId { get; init; }

    /// <summary>The broadcaster's Twitch display name (or login, if no display name).</summary>
    public string Username { get; set; } = "";

    public string TwitchAccessToken { get; set; } = "";
    public string TwitchRefreshToken { get; set; } = "";
    public DateTimeOffset TwitchExpiresAtUtc { get; set; }

    /// <summary>
    /// False once the background refresh loop has confirmed the underlying Twitch grant no longer
    /// works (e.g. the commander revoked access from their Twitch settings). While false,
    /// <c>/api/update-state</c> rejects the token so the desktop client knows to re-prompt login.
    /// </summary>
    public bool IsTwitchGrantValid { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastRefreshedAtUtc { get; set; }
}
