namespace EDNexus.Ebs.Security;

/// <summary>The outcome of validating an inbound Twitch Extension JWT.</summary>
public sealed record TwitchJwtValidationResult
{
    private TwitchJwtValidationResult(bool isValid, string? error, TwitchExtensionClaims? claims)
    {
        IsValid = isValid;
        Error = error;
        Claims = claims;
    }

    /// <summary>Whether the token's signature and standard claims are valid.</summary>
    public bool IsValid { get; }

    /// <summary>A human-readable failure reason, populated when <see cref="IsValid"/> is false.</summary>
    public string? Error { get; }

    /// <summary>The decoded claims, populated when <see cref="IsValid"/> is true.</summary>
    public TwitchExtensionClaims? Claims { get; }

    /// <summary>Builds a successful result.</summary>
    public static TwitchJwtValidationResult Success(TwitchExtensionClaims claims) => new(true, null, claims);

    /// <summary>Builds a failed result with the given reason.</summary>
    public static TwitchJwtValidationResult Failure(string error) => new(false, error, null);
}
