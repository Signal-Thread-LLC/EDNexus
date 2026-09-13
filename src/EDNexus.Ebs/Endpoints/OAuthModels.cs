using System.Text.Json.Serialization;

namespace EDNexus.Ebs.Endpoints;

/// <summary>The body of a <c>POST /oauth/token</c> request: the code from the loopback redirect plus the PKCE verifier that proves it.</summary>
public sealed record OAuthTokenExchangeRequest(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("code_verifier")] string? CodeVerifier,
    [property: JsonPropertyName("redirect_uri")] string? RedirectUri);

/// <summary>The long-lived, per-broadcaster token handed back to the desktop client on a successful <c>POST /oauth/token</c>.</summary>
public sealed record OAuthTokenIssuedResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("channelId")] string ChannelId,
    [property: JsonPropertyName("username")] string Username);
