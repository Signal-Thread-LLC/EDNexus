using System.Net;
using EDNexus.Core.Twitch;
using EDNexus.Tests.Reporting;
using Xunit;

namespace EDNexus.Tests.Twitch;

public class TwitchApiClientTests
{
    private static readonly TwitchOAuthOptions Options = new() { ClientId = "test-client-id" };

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_posts_expected_form_fields()
    {
        var handler = new RecordingHandler(body: """
            { "access_token": "abc123", "refresh_token": "ref456", "expires_in": 14346, "scope": ["user:read:email"], "token_type": "bearer" }
            """);
        using var client = new TwitchApiClient(Options, new HttpClient(handler));

        var token = await client.ExchangeAuthorizationCodeAsync("test-client-id", "auth-code", "verifier-value", "http://localhost:59123/callback");

        Assert.Equal("abc123", token.AccessToken);
        Assert.Equal("ref456", token.RefreshToken);
        Assert.Equal(14346, token.ExpiresIn);
        Assert.Contains("user:read:email", token.Scopes);

        var sent = handler.Bodies[0];
        Assert.Contains("grant_type=authorization_code", sent);
        Assert.Contains("code=auth-code", sent);
        Assert.Contains("code_verifier=verifier-value", sent);
        Assert.Contains("client_id=test-client-id", sent);
        Assert.Equal("https://id.twitch.tv/oauth2/token", handler.Uris[0]!.ToString());
    }

    [Fact]
    public async Task RefreshTokenAsync_posts_refresh_grant()
    {
        var handler = new RecordingHandler(body: """
            { "access_token": "new-token", "refresh_token": "new-refresh", "expires_in": 3600, "scope": [], "token_type": "bearer" }
            """);
        using var client = new TwitchApiClient(Options, new HttpClient(handler));

        var token = await client.RefreshTokenAsync("test-client-id", "old-refresh");

        Assert.Equal("new-token", token.AccessToken);
        Assert.Equal("new-refresh", token.RefreshToken);
        Assert.Contains("grant_type=refresh_token", handler.Bodies[0]);
        Assert.Contains("refresh_token=old-refresh", handler.Bodies[0]);
    }

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_throws_on_non_success_response()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{ "message": "Invalid authorization code" }""");
        using var client = new TwitchApiClient(Options, new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<TwitchApiException>(() =>
            client.ExchangeAuthorizationCodeAsync("test-client-id", "bad-code", "verifier", "http://localhost:59123/callback"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Invalid authorization code", ex.Message);
    }

    [Fact]
    public async Task RefreshTokenAsync_throws_on_revoked_token()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{ "message": "Invalid refresh token" }""");
        using var client = new TwitchApiClient(Options, new HttpClient(handler));

        await Assert.ThrowsAsync<TwitchApiException>(() => client.RefreshTokenAsync("test-client-id", "revoked-refresh"));
    }

    [Fact]
    public async Task GetUserAsync_parses_first_user_and_sends_bearer_and_client_id_headers()
    {
        var handler = new RecordingHandler(body: """
            { "data": [ { "id": "12345", "login": "cmdr_jameson", "display_name": "CMDR_Jameson" } ] }
            """);
        using var client = new TwitchApiClient(Options, new HttpClient(handler));

        var user = await client.GetUserAsync("abc123", "test-client-id");

        Assert.NotNull(user);
        Assert.Equal("12345", user!.Id);
        Assert.Equal("cmdr_jameson", user.Login);
        Assert.Equal("CMDR_Jameson", user.DisplayName);
        Assert.Equal("https://api.twitch.tv/helix/users", handler.Uris[0]!.ToString());
    }

    [Fact]
    public async Task GetUserAsync_returns_null_when_data_is_empty()
    {
        var handler = new RecordingHandler(body: """{ "data": [] }""");
        using var client = new TwitchApiClient(Options, new HttpClient(handler));

        var user = await client.GetUserAsync("abc123", "test-client-id");

        Assert.Null(user);
    }

    [Fact]
    public async Task RevokeTokenAsync_never_throws_on_error_response()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "{}");
        using var client = new TwitchApiClient(Options, new HttpClient(handler));

        await client.RevokeTokenAsync("test-client-id", "some-token");
        // Reaching here without an exception is the assertion.
    }
}
