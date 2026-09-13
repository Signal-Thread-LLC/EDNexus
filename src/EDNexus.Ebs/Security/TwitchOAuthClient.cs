using System.Net.Http.Headers;
using System.Text.Json;
using EDNexus.Ebs.Options;
using Microsoft.Extensions.Options;

namespace EDNexus.Ebs.Security;

/// <summary>Default <see cref="ITwitchOAuthClient"/> backed by a real (or injected, for tests) <see cref="HttpClient"/>.</summary>
public sealed class TwitchOAuthClient : ITwitchOAuthClient
{
    private readonly HttpClient _http;
    private readonly TwitchEbsOptions _options;

    public TwitchOAuthClient(HttpClient http, IOptions<TwitchEbsOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public Task<TwitchTokenResponse> ExchangeAuthorizationCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        };
        return PostTokenAsync(form, ct);
    }

    public Task<TwitchTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };
        return PostTokenAsync(form, ct);
    }

    public async Task<TwitchUser?> GetUserAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.UsersEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", _options.ClientId);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new TwitchOAuthException($"Twitch rejected the user lookup: HTTP {(int)response.StatusCode} — {text}", (int)response.StatusCode);

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                return data[0].Deserialize<TwitchUser>();
            return null;
        }
        catch (JsonException ex)
        {
            throw new TwitchOAuthException($"Unparseable Twitch user response: {ex.Message}");
        }
    }

    public async Task RevokeTokenAsync(string token, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["client_id"] = _options.ClientId, ["token"] = token };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(_options.RevokeTokenEndpoint, content, ct).ConfigureAwait(false);
        // Best-effort: a revoke failing (e.g. already-revoked token) shouldn't block clearing local state.
        _ = response;
    }

    private async Task<TwitchTokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(_options.TokenEndpoint, content, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new TwitchOAuthException($"Twitch rejected the token request: HTTP {(int)response.StatusCode} — {text}", (int)response.StatusCode);

        try
        {
            var token = JsonSerializer.Deserialize<TwitchTokenResponse>(text);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
                throw new TwitchOAuthException("Twitch returned an empty token response.");
            return token;
        }
        catch (JsonException ex)
        {
            throw new TwitchOAuthException($"Unparseable Twitch token response: {ex.Message}");
        }
    }
}
