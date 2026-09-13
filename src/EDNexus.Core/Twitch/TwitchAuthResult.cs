namespace EDNexus.Core.Twitch;

/// <summary>Outcome of a <see cref="TwitchAuthService.LoginAsync"/> attempt.</summary>
public enum TwitchAuthStatus
{
    /// <summary>Login completed and tokens were persisted.</summary>
    Success,

    /// <summary>The caller's <c>CancellationToken</c> was cancelled before the flow completed.</summary>
    Cancelled,

    /// <summary>The commander declined the authorization request on Twitch's consent page.</summary>
    Denied,

    /// <summary>No callback arrived within <see cref="TwitchOAuthOptions.LoginTimeout"/>.</summary>
    Timeout,

    /// <summary>Anything else — network failure, bad response, state mismatch, etc. See <see cref="TwitchAuthResult.Error"/>.</summary>
    Error,
}

/// <summary>Result of an authorization attempt: either the identified broadcaster, or why it failed.</summary>
public sealed record TwitchAuthResult(TwitchAuthStatus Status, string? Username = null, string? UserId = null, string? Error = null)
{
    public bool IsSuccess => Status == TwitchAuthStatus.Success;

    public static TwitchAuthResult Ok(string username, string userId) => new(TwitchAuthStatus.Success, username, userId);

    public static TwitchAuthResult Failed(TwitchAuthStatus status, string? error = null) => new(status, Error: error);
}
