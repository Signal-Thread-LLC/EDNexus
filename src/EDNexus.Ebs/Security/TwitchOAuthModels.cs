using System.Text.Json.Serialization;

namespace EDNexus.Ebs.Security;

/// <summary>Token grant returned by Twitch's <c>/oauth2/token</c> endpoint (authorization-code exchange or refresh).</summary>
public sealed record TwitchTokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; init; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
    [JsonPropertyName("scope")] public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();
    [JsonPropertyName("token_type")] public string TokenType { get; init; } = "";
}

/// <summary>A single entry from Twitch's Helix <c>GET /users</c> response.</summary>
public sealed record TwitchUser
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = "";
}

/// <summary>Thrown when Twitch rejects a token/user request the EBS makes on the broadcaster's behalf (bad code, revoked refresh token, etc.).</summary>
public sealed class TwitchOAuthException : Exception
{
    public int? StatusCode { get; }

    public TwitchOAuthException(string message, int? statusCode = null) : base(message) => StatusCode = statusCode;
}
