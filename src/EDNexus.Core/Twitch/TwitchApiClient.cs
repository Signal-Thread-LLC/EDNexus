using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EDNexus.Core.Twitch;

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

/// <summary>Thrown when Twitch rejects a token/user request (bad code, revoked refresh token, etc.).</summary>
public sealed class TwitchApiException : Exception
{
    public int? StatusCode { get; }

    public TwitchApiException(string message, int? statusCode = null) : base(message) => StatusCode = statusCode;
}

/// <summary>Transport for the Twitch OAuth/Helix calls the auth flow needs. Pure transport — no token storage or policy.</summary>
public interface ITwitchApiClient
{
    Task<TwitchTokenResponse> ExchangeAuthorizationCodeAsync(string clientId, string code, string codeVerifier, string redirectUri, CancellationToken ct = default);
    Task<TwitchTokenResponse> RefreshTokenAsync(string clientId, string refreshToken, CancellationToken ct = default);
    Task<TwitchUser?> GetUserAsync(string accessToken, string clientId, CancellationToken ct = default);
    Task RevokeTokenAsync(string clientId, string token, CancellationToken ct = default);
}

/// <summary>Default <see cref="ITwitchApiClient"/> backed by a real (or injected, for tests) <see cref="HttpClient"/>.</summary>
public sealed class TwitchApiClient : ITwitchApiClient, IDisposable
{
    private readonly TwitchOAuthOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public TwitchApiClient(TwitchOAuthOptions options, HttpClient? http = null)
    {
        _options = options;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
    }

    public async Task<TwitchTokenResponse> ExchangeAuthorizationCodeAsync(string clientId, string code, string codeVerifier, string redirectUri, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        };
        return await PostTokenAsync(form, ct).ConfigureAwait(false);
    }

    public async Task<TwitchTokenResponse> RefreshTokenAsync(string clientId, string refreshToken, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };
        return await PostTokenAsync(form, ct).ConfigureAwait(false);
    }

    public async Task<TwitchUser?> GetUserAsync(string accessToken, string clientId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.UsersEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", clientId);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new TwitchApiException($"Twitch rejected the user lookup: HTTP {(int)response.StatusCode} — {text}", (int)response.StatusCode);

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                return data[0].Deserialize<TwitchUser>();
            return null;
        }
        catch (JsonException ex)
        {
            throw new TwitchApiException($"Unparseable Twitch user response: {ex.Message}");
        }
    }

    public async Task RevokeTokenAsync(string clientId, string token, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["client_id"] = clientId, ["token"] = token };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(_options.RevokeEndpoint, content, ct).ConfigureAwait(false);
        // Best-effort: a revoke failing (e.g. already-revoked token) shouldn't block clearing local state.
        _ = response;
    }

    private async Task<TwitchTokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(_options.TokenEndpoint, content, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new TwitchApiException($"Twitch rejected the token request: HTTP {(int)response.StatusCode} — {text}", (int)response.StatusCode);

        try
        {
            var token = JsonSerializer.Deserialize<TwitchTokenResponse>(text);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
                throw new TwitchApiException("Twitch returned an empty token response.");
            return token;
        }
        catch (JsonException ex)
        {
            throw new TwitchApiException($"Unparseable Twitch token response: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
