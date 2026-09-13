namespace EDNexus.Ebs.Security;

/// <summary>
/// The EBS's side of the real Twitch OAuth 2.0 Authorization Code flow: the EBS is the registered
/// Twitch application (holds the client id + secret), so unlike the desktop client this talks to
/// <c>id.twitch.tv</c>/<c>api.twitch.tv</c> directly. The desktop app never sees any of this.
/// </summary>
public interface ITwitchOAuthClient
{
    /// <summary>Exchanges an authorization code Twitch handed the EBS's own <c>/oauth/callback</c> for a Twitch token grant.</summary>
    Task<TwitchTokenResponse> ExchangeAuthorizationCodeAsync(string code, string redirectUri, CancellationToken ct = default);

    /// <summary>Refreshes a broadcaster's Twitch access token using their stored refresh token.</summary>
    Task<TwitchTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Looks up the broadcaster's Twitch identity (user id + login) using their access token.</summary>
    Task<TwitchUser?> GetUserAsync(string accessToken, CancellationToken ct = default);

    /// <summary>Best-effort revocation of a Twitch token (access or refresh) with Twitch.</summary>
    Task RevokeTokenAsync(string token, CancellationToken ct = default);
}
