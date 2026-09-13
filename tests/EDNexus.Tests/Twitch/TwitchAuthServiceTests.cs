using System.Text.RegularExpressions;
using EDNexus.Core.Settings;
using EDNexus.Core.Twitch;
using Xunit;

namespace EDNexus.Tests.Twitch;

public class TwitchAuthServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ednexus-twitch-settings-").FullName;
    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static readonly TwitchOAuthOptions Options = new() { ClientId = "test-client-id", LoginTimeout = TimeSpan.FromSeconds(5) };

    private static string ExtractState(string url) => Regex.Match(url, "state=([^&]+)").Groups[1].Value;

    private (AppSettings Settings, SettingsStore Store) NewStore()
    {
        var store = new SettingsStore(SettingsPath);
        return (store.Load(), store);
    }

    [Fact]
    public async Task LoginAsync_persists_tokens_and_user_on_success()
    {
        var (settings, store) = NewStore();
        var browser = new NoOpBrowserLauncher();
        var api = new FakeTwitchApiClient
        {
            OnExchange = (clientId, code, verifier, redirect) =>
            {
                Assert.Equal("test-client-id", clientId);
                Assert.Equal("auth-code-123", code);
                Assert.Equal("http://localhost:59123/callback", redirect);
                return new TwitchTokenResponse { AccessToken = "access-1", RefreshToken = "refresh-1", ExpiresIn = 3600, Scopes = new[] { "user:read:email" } };
            },
            OnGetUser = (token, clientId) =>
            {
                Assert.Equal("access-1", token);
                return new TwitchUser { Id = "999", Login = "cmdr_jameson", DisplayName = "CMDR_Jameson" };
            },
        };
        var listener = new FakeCallbackListener(() => new Dictionary<string, string>
        {
            ["code"] = "auth-code-123",
            ["state"] = ExtractState(browser.LastUrl ?? ""),
        });

        var service = new TwitchAuthService(settings, store, Options, api, browser, listener);
        var result = await service.LoginAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("CMDR_Jameson", result.Username);
        Assert.Equal("999", result.UserId);
        Assert.Equal(1, api.ExchangeCalls);

        Assert.True(service.IsLoggedIn);
        Assert.Equal("access-1", service.AccessToken);
        Assert.Equal("999", settings.Twitch.UserId);
        Assert.Equal("CMDR_Jameson", settings.Twitch.Username);
        Assert.Equal("refresh-1", settings.Twitch.RefreshToken);

        // Round-trips through disk, like the rest of AppSettings.
        var reloaded = new SettingsStore(SettingsPath).Load();
        Assert.Equal("access-1", reloaded.Twitch.AccessToken);
        Assert.Equal("999", reloaded.Twitch.UserId);
    }

    [Fact]
    public async Task LoginAsync_opens_the_authorize_url_with_pkce_and_scope_params()
    {
        var (settings, store) = NewStore();
        var browser = new NoOpBrowserLauncher();
        var api = new FakeTwitchApiClient
        {
            OnExchange = (_, _, _, _) => new TwitchTokenResponse { AccessToken = "a", RefreshToken = "r", ExpiresIn = 100 },
            OnGetUser = (_, _) => new TwitchUser { Id = "1", Login = "x", DisplayName = "X" },
        };
        var listener = new FakeCallbackListener(() => new Dictionary<string, string>
        {
            ["code"] = "code",
            ["state"] = ExtractState(browser.LastUrl ?? ""),
        });

        var service = new TwitchAuthService(settings, store, Options, api, browser, listener);
        await service.LoginAsync();

        Assert.NotNull(browser.LastUrl);
        Assert.StartsWith("https://id.twitch.tv/oauth2/authorize?", browser.LastUrl);
        Assert.Contains("client_id=test-client-id", browser.LastUrl);
        Assert.Contains("response_type=code", browser.LastUrl);
        Assert.Contains("code_challenge_method=S256", browser.LastUrl);
        Assert.Contains("scope=user%3Aread%3Aemail", browser.LastUrl);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A59123%2Fcallback", browser.LastUrl);
    }

    [Fact]
    public async Task LoginAsync_fails_closed_on_state_mismatch_without_exchanging_the_code()
    {
        var (settings, store) = NewStore();
        var browser = new NoOpBrowserLauncher();
        var api = new FakeTwitchApiClient();
        var listener = new FakeCallbackListener(new Dictionary<string, string> { ["code"] = "code", ["state"] = "not-the-real-state" });

        var service = new TwitchAuthService(settings, store, Options, api, browser, listener);
        var result = await service.LoginAsync();

        Assert.Equal(TwitchAuthStatus.Error, result.Status);
        Assert.Contains("State mismatch", result.Error);
        Assert.Equal(0, api.ExchangeCalls);
        Assert.False(service.IsLoggedIn);
    }

    [Fact]
    public async Task LoginAsync_reports_denial_when_the_commander_declines_on_Twitch()
    {
        var (settings, store) = NewStore();
        var browser = new NoOpBrowserLauncher();
        var listener = new FakeCallbackListener(() => new Dictionary<string, string>
        {
            ["error"] = "access_denied",
            ["error_description"] = "The user denied you access",
        });

        var service = new TwitchAuthService(settings, store, Options, new FakeTwitchApiClient(), browser, listener);
        var result = await service.LoginAsync();

        Assert.Equal(TwitchAuthStatus.Denied, result.Status);
        Assert.Equal("The user denied you access", result.Error);
        Assert.False(service.IsLoggedIn);
    }

    [Fact]
    public async Task LoginAsync_reports_timeout_when_no_callback_arrives()
    {
        var (settings, store) = NewStore();
        var options = new TwitchOAuthOptions { ClientId = "c", LoginTimeout = TimeSpan.FromMilliseconds(50) };
        var listener = new FakeCallbackListener(new OperationCanceledException());

        var service = new TwitchAuthService(settings, store, options, new FakeTwitchApiClient(), new NoOpBrowserLauncher(), listener);
        var result = await service.LoginAsync();

        Assert.Equal(TwitchAuthStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task LoginAsync_reports_cancelled_when_the_caller_cancels()
    {
        var (settings, store) = NewStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var listener = new FakeCallbackListener(new OperationCanceledException());

        var service = new TwitchAuthService(settings, store, Options, new FakeTwitchApiClient(), new NoOpBrowserLauncher(), listener);
        var result = await service.LoginAsync(cts.Token);

        Assert.Equal(TwitchAuthStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task EnsureValidTokenAsync_does_not_refresh_a_token_that_is_still_fresh()
    {
        var (settings, store) = NewStore();
        settings.Twitch.AccessToken = "still-good";
        settings.Twitch.RefreshToken = "refresh";
        settings.Twitch.ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1);

        var api = new FakeTwitchApiClient();
        var service = new TwitchAuthService(settings, store, Options, api, new NoOpBrowserLauncher(), new FakeCallbackListener(new Dictionary<string, string>()));

        var valid = await service.EnsureValidTokenAsync();

        Assert.True(valid);
        Assert.Equal(0, api.RefreshCalls);
        Assert.Equal("still-good", settings.Twitch.AccessToken);
    }

    [Fact]
    public async Task EnsureValidTokenAsync_refreshes_a_token_nearing_expiry_and_persists_the_new_one()
    {
        var (settings, store) = NewStore();
        settings.Twitch.AccessToken = "old-token";
        settings.Twitch.RefreshToken = "old-refresh";
        settings.Twitch.UserId = "1";
        settings.Twitch.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1); // inside the default 5-minute buffer

        var api = new FakeTwitchApiClient
        {
            OnRefresh = (clientId, refreshToken) =>
            {
                Assert.Equal("test-client-id", clientId);
                Assert.Equal("old-refresh", refreshToken);
                return new TwitchTokenResponse { AccessToken = "new-token", RefreshToken = "new-refresh", ExpiresIn = 3600 };
            },
        };
        var service = new TwitchAuthService(settings, store, Options, api, new NoOpBrowserLauncher(), new FakeCallbackListener(new Dictionary<string, string>()));

        var valid = await service.EnsureValidTokenAsync();

        Assert.True(valid);
        Assert.Equal(1, api.RefreshCalls);
        Assert.Equal("new-token", settings.Twitch.AccessToken);
        Assert.Equal("new-refresh", settings.Twitch.RefreshToken);
        Assert.True(settings.Twitch.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(30));

        var reloaded = new SettingsStore(SettingsPath).Load();
        Assert.Equal("new-token", reloaded.Twitch.AccessToken);
    }

    [Fact]
    public async Task EnsureValidTokenAsync_clears_the_session_when_the_refresh_token_is_revoked()
    {
        var (settings, store) = NewStore();
        settings.Twitch.AccessToken = "old-token";
        settings.Twitch.RefreshToken = "revoked-refresh";
        settings.Twitch.UserId = "1";
        settings.Twitch.Username = "CMDR";
        settings.Twitch.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(1);

        var api = new FakeTwitchApiClient { OnRefresh = (_, _) => throw new TwitchApiException("invalid refresh token", 400) };
        var service = new TwitchAuthService(settings, store, Options, api, new NoOpBrowserLauncher(), new FakeCallbackListener(new Dictionary<string, string>()));

        var valid = await service.EnsureValidTokenAsync();

        Assert.False(valid);
        Assert.False(service.IsLoggedIn);
        Assert.Null(settings.Twitch.AccessToken);
        Assert.Null(settings.Twitch.RefreshToken);
        Assert.Null(settings.Twitch.UserId);
    }

    [Fact]
    public async Task EnsureValidTokenAsync_returns_false_when_never_logged_in()
    {
        var (settings, store) = NewStore();
        var service = new TwitchAuthService(settings, store, Options, new FakeTwitchApiClient(), new NoOpBrowserLauncher(), new FakeCallbackListener(new Dictionary<string, string>()));

        Assert.False(await service.EnsureValidTokenAsync());
    }

    [Fact]
    public async Task LogoutAsync_revokes_the_token_and_clears_the_session()
    {
        var (settings, store) = NewStore();
        settings.Twitch.AccessToken = "access-1";
        settings.Twitch.RefreshToken = "refresh-1";
        settings.Twitch.UserId = "1";
        settings.Twitch.Username = "CMDR";

        var api = new FakeTwitchApiClient();
        var service = new TwitchAuthService(settings, store, Options, api, new NoOpBrowserLauncher(), new FakeCallbackListener(new Dictionary<string, string>()));

        await service.LogoutAsync();

        Assert.Equal(1, api.RevokeCalls);
        Assert.Equal("access-1", api.LastRevokedToken);
        Assert.False(service.IsLoggedIn);
        Assert.Null(settings.Twitch.AccessToken);
        Assert.Null(settings.Twitch.Username);
    }
}
