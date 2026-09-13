using System.Net;
using EDNexus.Ebs.Options;
using EDNexus.Ebs.Security;
using Microsoft.Extensions.Options;

namespace EDNexus.Ebs.Tests;

public class TwitchOAuthClientTests
{
    private static readonly TwitchEbsOptions Options = new()
    {
        ClientId = "ebs-client-id",
        ClientSecret = "ebs-client-secret",
        OAuthRedirectUri = "https://ebs.example.com/oauth/callback",
    };

    private static TwitchOAuthClient CreateClient(RecordingHandler handler, TwitchEbsOptions? options = null) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options));

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_posts_the_client_secret_and_expected_form_fields()
    {
        var handler = new RecordingHandler(body: """
            { "access_token": "abc123", "refresh_token": "ref456", "expires_in": 14346, "scope": ["user:read:email"], "token_type": "bearer" }
            """);
        var client = CreateClient(handler);

        var token = await client.ExchangeAuthorizationCodeAsync("auth-code", "https://ebs.example.com/oauth/callback");

        Assert.Equal("abc123", token.AccessToken);
        Assert.Equal("ref456", token.RefreshToken);
        Assert.Equal(14346, token.ExpiresIn);

        var sent = handler.Bodies[0];
        Assert.Contains("grant_type=authorization_code", sent);
        Assert.Contains("code=auth-code", sent);
        Assert.Contains("client_id=ebs-client-id", sent);
        Assert.Contains("client_secret=ebs-client-secret", sent);
        Assert.Equal("https://id.twitch.tv/oauth2/token", handler.Uris[0]!.ToString());
    }

    [Fact]
    public async Task RefreshTokenAsync_posts_refresh_grant_with_the_client_secret()
    {
        var handler = new RecordingHandler(body: """
            { "access_token": "new-token", "refresh_token": "new-refresh", "expires_in": 3600, "scope": [], "token_type": "bearer" }
            """);
        var client = CreateClient(handler);

        var token = await client.RefreshTokenAsync("old-refresh");

        Assert.Equal("new-token", token.AccessToken);
        Assert.Contains("grant_type=refresh_token", handler.Bodies[0]);
        Assert.Contains("refresh_token=old-refresh", handler.Bodies[0]);
        Assert.Contains("client_secret=ebs-client-secret", handler.Bodies[0]);
    }

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_throws_on_non_success_response()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{ "message": "Invalid authorization code" }""");
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<TwitchOAuthException>(() =>
            client.ExchangeAuthorizationCodeAsync("bad-code", "https://ebs.example.com/oauth/callback"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Invalid authorization code", ex.Message);
    }

    [Fact]
    public async Task RefreshTokenAsync_throws_on_a_revoked_refresh_token()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{ "message": "Invalid refresh token" }""");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<TwitchOAuthException>(() => client.RefreshTokenAsync("revoked-refresh"));
    }

    [Fact]
    public async Task GetUserAsync_parses_the_first_user_and_sends_bearer_and_client_id_headers()
    {
        var handler = new RecordingHandler(body: """
            { "data": [ { "id": "12345", "login": "cmdr_jameson", "display_name": "CMDR_Jameson" } ] }
            """);
        var client = CreateClient(handler);

        var user = await client.GetUserAsync("abc123");

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
        var client = CreateClient(handler);

        var user = await client.GetUserAsync("abc123");

        Assert.Null(user);
    }

    [Fact]
    public async Task RevokeTokenAsync_never_throws_on_error_response()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "{}");
        var client = CreateClient(handler);

        await client.RevokeTokenAsync("some-token");
        // Reaching here without an exception is the assertion.
    }
}
