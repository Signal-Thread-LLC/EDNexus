namespace EDNexus.Ebs.Options;

/// <summary>
/// Configuration required to authenticate against Twitch's Extensions platform and to sign/verify
/// the JWTs exchanged with the desktop client and the Twitch Helix API.
/// </summary>
/// <remarks>
/// Bound from the <c>Twitch</c> configuration section. In production these values should come from
/// environment variables (<c>Twitch__ClientId</c>, <c>Twitch__ExtensionId</c>,
/// <c>Twitch__ExtensionSecret</c>) or a secret store — never committed to source control.
/// </remarks>
public sealed class TwitchEbsOptions
{
    /// <summary>Configuration section name this type binds to.</summary>
    public const string SectionName = "Twitch";

    /// <summary>The Twitch application Client ID associated with the extension.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The Twitch Extension ID (Client ID of the extension itself), used in PubSub payloads.</summary>
    public string ExtensionId { get; set; } = string.Empty;

    /// <summary>
    /// The base64-encoded Extension Secret issued by Twitch, used both to verify JWTs signed by
    /// Twitch (broadcaster/viewer config-page JWTs) and to sign outbound JWTs the EBS uses to call
    /// the Helix PubSub API on the extension's behalf.
    /// </summary>
    public string ExtensionSecret { get; set; } = string.Empty;

    /// <summary>
    /// Optional clock skew (seconds) tolerated when validating the <c>exp</c>/<c>nbf</c> claims of
    /// inbound JWTs. Defaults to 30 seconds.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>Lifetime (seconds) of JWTs the EBS mints to call the Helix PubSub API. Defaults to 180s.</summary>
    public int OutboundTokenLifetimeSeconds { get; set; } = 180;

    /// <summary>
    /// The Twitch application's Client Secret. The EBS is the registered Twitch application for the
    /// OAuth Authorization Code flow (<c>/oauth/authorize</c> + <c>/oauth/callback</c>); this secret
    /// never leaves the EBS. Populate from <c>Twitch__ClientSecret</c> or a secret store — never
    /// commit it to source control.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The redirect URI registered with Twitch for this application's Authorization Code flow. Must
    /// exactly match what's configured in the Twitch developer console (e.g.
    /// <c>https://ebs.example.com/oauth/callback</c>).
    /// </summary>
    public string OAuthRedirectUri { get; set; } = "https://ednexus.signal-and-thread.com/oauth/callback";

    /// <summary>Scopes requested from Twitch during login — the minimal set needed to identify the broadcaster.</summary>
    public IReadOnlyList<string> OAuthScopes { get; set; } = new[] { "user:read:email" };

    /// <summary>Twitch's OAuth authorization endpoint. Overridable for tests against a fake Twitch server.</summary>
    public string AuthorizationEndpoint { get; set; } = "https://id.twitch.tv/oauth2/authorize";

    /// <summary>Twitch's OAuth token endpoint. Overridable for tests against a fake Twitch server.</summary>
    public string TokenEndpoint { get; set; } = "https://id.twitch.tv/oauth2/token";

    /// <summary>Twitch's OAuth token revocation endpoint. Overridable for tests against a fake Twitch server.</summary>
    public string RevokeTokenEndpoint { get; set; } = "https://id.twitch.tv/oauth2/revoke";

    /// <summary>Twitch's Helix users endpoint, used to identify the broadcaster after login. Overridable for tests.</summary>
    public string UsersEndpoint { get; set; } = "https://api.twitch.tv/helix/users";
}
