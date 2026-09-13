namespace EDNexus.Core.Twitch;

/// <summary>
/// Configuration for the Twitch OAuth 2.0 Authorization Code + PKCE flow. Twitch's endpoints and the
/// default loopback redirect are fixed; only <see cref="ClientId"/> (registered per-app on the Twitch
/// developer console) needs to be supplied by the host.
/// </summary>
public sealed class TwitchOAuthOptions
{
    /// <summary>The app's Twitch client id. Public clients like this one use PKCE instead of a client secret.</summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Loopback redirect registered with the Twitch app. Must exactly match what's configured in the
    /// Twitch developer console.
    /// </summary>
    public string RedirectUri { get; init; } = "http://localhost:59123/callback";

    /// <summary>Scopes requested — minimal set needed to identify the broadcaster and drive an extension overlay.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = new[] { "user:read:email" };

    public string AuthorizationEndpoint { get; init; } = "https://id.twitch.tv/oauth2/authorize";
    public string TokenEndpoint { get; init; } = "https://id.twitch.tv/oauth2/token";
    public string RevokeEndpoint { get; init; } = "https://id.twitch.tv/oauth2/revoke";
    public string UsersEndpoint { get; init; } = "https://api.twitch.tv/helix/users";

    /// <summary>How long to wait for the commander to complete the browser flow before giving up.</summary>
    public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>Refresh proactively once the access token has less than this long left before it expires.</summary>
    public TimeSpan RefreshBuffer { get; init; } = TimeSpan.FromMinutes(5);
}
