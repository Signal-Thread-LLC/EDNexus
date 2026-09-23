namespace EDNexus.Core.Twitch;

/// <summary>
/// Configuration for the desktop↔EBS login flow. The desktop app never talks to Twitch directly —
/// it authenticates against the EBS's own <c>/oauth/authorize</c> and <c>/oauth/token</c> endpoints,
/// and the EBS performs the real Twitch Authorization Code handshake server-side.
/// </summary>
public sealed class TwitchOAuthOptions
{
    /// <summary>
    /// Base URL of the EBS instance this app logs into (e.g. <c>https://ednexus.signal-and-thread.com</c>, or
    /// <c>http://localhost:8787</c> for a local EBS instance).
    /// </summary>
    public required string EbsBaseUrl { get; init; }

    /// <summary>
    /// Loopback redirect the desktop's temporary local listener binds to. Only ever seen by the EBS
    /// (never registered with Twitch) — it does not need to match anything on Twitch's side.
    /// </summary>
    public string RedirectUri { get; init; } = "http://localhost:59123/callback";

    /// <summary>How long to wait for the commander to complete the browser flow before giving up.</summary>
    public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>The EBS's authorization endpoint, derived from <see cref="EbsBaseUrl"/>.</summary>
    public string AuthorizeEndpoint => $"{EbsBaseUrl.TrimEnd('/')}/oauth/authorize";

    /// <summary>The EBS's PKCE code/token exchange endpoint, derived from <see cref="EbsBaseUrl"/>.</summary>
    public string TokenEndpoint => $"{EbsBaseUrl.TrimEnd('/')}/oauth/token";

    /// <summary>The EBS's best-effort logout/revoke endpoint, derived from <see cref="EbsBaseUrl"/>.</summary>
    public string RevokeEndpoint => $"{EbsBaseUrl.TrimEnd('/')}/oauth/revoke";

    /// <summary>
    /// The EBS's state-publish endpoint, derived from <see cref="EbsBaseUrl"/>. Where
    /// <see cref="TwitchStreamCardService"/> POSTs each <see cref="StreamCardSnapshot"/> for relay to
    /// viewers over Twitch Extensions PubSub.
    /// </summary>
    public string UpdateStateEndpoint => $"{EbsBaseUrl.TrimEnd('/')}/api/update-state";
}
