namespace EDNexus.Core.Twitch;

/// <summary>
/// Coarse connection state for the (not-yet-implemented) Twitch OAuth flow (#41) and Extension
/// Backend Service bridge (#40). Kept here, decoupled from the real services, purely so the
/// developer-mode simulator (and its future card/view-model) can reason about a shared vocabulary
/// without depending on either service landing first.
/// </summary>
public enum TwitchOAuthStatus
{
    /// <summary>No broadcaster session; the commander has never linked Twitch or has since unlinked it.</summary>
    Disconnected,

    /// <summary>The loopback listener is up and waiting on the browser redirect.</summary>
    Connecting,

    /// <summary>Tokens obtained and valid; state updates can be pushed to the EBS.</summary>
    Connected,

    /// <summary>The access token was renewed via the refresh token ahead of expiry.</summary>
    Renewed,

    /// <summary>The Twitch Helix API (or the EBS bridging it) is rate-limiting this broadcaster.</summary>
    RateLimited,

    /// <summary>The access/refresh token pair is no longer valid; re-authorization is required.</summary>
    Expired,
}
