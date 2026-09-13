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

    private static readonly TwitchOAuthOptions Options = new() { EbsBaseUrl = "http://localhost:8787", LoginTimeout = TimeSpan.FromSeconds(5) };

    private static string ExtractState(string url) => Regex.Match(url, "state=([^&]+)").Groups[1].Value;

    private (AppSettings Settings, SettingsStore Store) NewStore()
    {
        var store = new SettingsStore(SettingsPath);
        return (store.Load(), store);
    }

    [Fact]
    public async Task LoginAsync_persists_the_EBS_token_and_broadcaster_identity_on_success()
    {
        var (settings, store) = NewStore();
        var browser = new FakeBrowserLauncher();
        var api = new FakeEbsAuthApiClient
        {
            OnExchange = (code, verifier, redirect) =>
            {
                Assert.Equal("auth-code-123", code);
                Assert.NotEmpty(verifier);
                Assert.Equal("http://localhost:59123/callback", redirect);
                return new EbsTokenResponse { Token = "ebs-token-1", ChannelId = "999", Username = "CMDR_Jameson" };
            },
        };
        var listener = new FakeCallbackListener(browser, () => new Dictionary<string, string>
        {
            ["code"] = "auth-code-123",
            ["state"] = ExtractState(browser.LastUrl ?? ""),
        });

        var service = new TwitchAuthService(settings, store, Options, api, browser, listener);
        var result = await service.LoginAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("CMDR_Jameson", result.Username);
        Assert.Equal("999", result.ChannelId);
        Assert.Equal(1, api.ExchangeCalls);

        Assert.True(service.IsLoggedIn);
        Assert.Equal("ebs-token-1", service.Token);
        Assert.Equal("999", settings.Twitch.ChannelId);
        Assert.Equal("CMDR_Jameson", settings.Twitch.Username);

        // Round-trips through disk, like the rest of AppSettings.
        var reloaded = new SettingsStore(SettingsPath).Load();
        Assert.Equal("ebs-token-1", reloaded.Twitch.Token);
        Assert.Equal("999", reloaded.Twitch.ChannelId);
    }

    [Fact]
    public async Task LoginAsync_opens_the_EBS_authorize_url_with_pkce_params_and_no_direct_Twitch_call()
    {
        var (settings, store) = NewStore();
        var browser = new FakeBrowserLauncher();
        var api = new FakeEbsAuthApiClient
        {
            OnExchange = (_, _, _) => new EbsTokenResponse { Token = "t", ChannelId = "1", Username = "X" },
        };
        var listener = new FakeCallbackListener(browser, () => new Dictionary<string, string>
        {
            ["code"] = "code",
            ["state"] = ExtractState(browser.LastUrl ?? ""),
        });

        var service = new TwitchAuthService(settings, store, Options, api, browser, listener);
        await service.LoginAsync();

        Assert.NotNull(browser.LastUrl);
        Assert.StartsWith("http://localhost:8787/oauth/authorize?", browser.LastUrl);
        Assert.Contains("response_type=code", browser.LastUrl);
        Assert.Contains("code_challenge_method=S256", browser.LastUrl);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A59123%2Fcallback", browser.LastUrl);
        Assert.DoesNotContain("twitch.tv", browser.LastUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_fails_closed_on_state_mismatch_without_exchanging_the_code()
    {
        var (settings, store) = NewStore();
        var browser = new FakeBrowserLauncher();
        var api = new FakeEbsAuthApiClient();
        var listener = new FakeCallbackListener(new Dictionary<string, string> { ["code"] = "code", ["state"] = "not-the-real-state" });

        var service = new TwitchAuthService(settings, store, Options, api, browser, listener);
        var result = await service.LoginAsync();

        Assert.Equal(TwitchAuthStatus.Error, result.Status);
        Assert.Contains("State mismatch", result.Error);
        Assert.Equal(0, api.ExchangeCalls);
        Assert.False(service.IsLoggedIn);
    }

    [Fact]
    public async Task LoginAsync_reports_denial_when_the_commander_declines_on_the_EBS_hosted_consent_page()
    {
        var (settings, store) = NewStore();
        var browser = new FakeBrowserLauncher();
        var listener = new FakeCallbackListener(new Dictionary<string, string>
        {
            ["error"] = "access_denied",
            ["error_description"] = "The user denied you access",
        });

        var service = new TwitchAuthService(settings, store, Options, new FakeEbsAuthApiClient(), browser, listener);
        var result = await service.LoginAsync();

        Assert.Equal(TwitchAuthStatus.Denied, result.Status);
        Assert.Equal("The user denied you access", result.Error);
        Assert.False(service.IsLoggedIn);
    }

    [Fact]
    public async Task LoginAsync_reports_timeout_when_no_callback_arrives()
    {
        var (settings, store) = NewStore();
        var options = new TwitchOAuthOptions { EbsBaseUrl = "http://localhost:8787", LoginTimeout = TimeSpan.FromMilliseconds(50) };
        var listener = new FakeCallbackListener(new OperationCanceledException());

        var service = new TwitchAuthService(settings, store, options, new FakeEbsAuthApiClient(), new FakeBrowserLauncher(), listener);
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

        var service = new TwitchAuthService(settings, store, Options, new FakeEbsAuthApiClient(), new FakeBrowserLauncher(), listener);
        var result = await service.LoginAsync(cts.Token);

        Assert.Equal(TwitchAuthStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task LoginAsync_surfaces_EBS_token_exchange_failures_as_an_error_result()
    {
        var (settings, store) = NewStore();
        var browser = new FakeBrowserLauncher();
        var api = new FakeEbsAuthApiClient
        {
            OnExchange = (_, _, _) => throw new EbsAuthApiException("PKCE verifier did not match", 400),
        };
        var listener = new FakeCallbackListener(browser, () => new Dictionary<string, string>
        {
            ["code"] = "code",
            ["state"] = ExtractState(browser.LastUrl ?? ""),
        });

        var service = new TwitchAuthService(settings, store, Options, api, browser, listener);
        var result = await service.LoginAsync();

        Assert.Equal(TwitchAuthStatus.Error, result.Status);
        Assert.Contains("PKCE verifier did not match", result.Error);
        Assert.False(service.IsLoggedIn);
    }

    [Fact]
    public async Task LogoutAsync_revokes_the_token_with_the_EBS_and_clears_the_session()
    {
        var (settings, store) = NewStore();
        settings.Twitch.Token = "ebs-token-1";
        settings.Twitch.ChannelId = "1";
        settings.Twitch.Username = "CMDR";

        var api = new FakeEbsAuthApiClient();
        var service = new TwitchAuthService(settings, store, Options, api, new FakeBrowserLauncher(), new FakeCallbackListener(new Dictionary<string, string>()));

        await service.LogoutAsync();

        Assert.Equal(1, api.RevokeCalls);
        Assert.Equal("ebs-token-1", api.LastRevokedToken);
        Assert.False(service.IsLoggedIn);
        Assert.Null(settings.Twitch.Token);
        Assert.Null(settings.Twitch.Username);
    }

    [Fact]
    public async Task LogoutAsync_clears_local_state_even_if_the_EBS_revoke_call_throws()
    {
        var (settings, store) = NewStore();
        settings.Twitch.Token = "ebs-token-1";
        settings.Twitch.ChannelId = "1";

        var service = new TwitchAuthService(settings, store, Options, new ThrowingRevokeClient(), new FakeBrowserLauncher(), new FakeCallbackListener(new Dictionary<string, string>()));

        await service.LogoutAsync();

        Assert.False(service.IsLoggedIn);
        Assert.Null(settings.Twitch.Token);
    }

    private sealed class ThrowingRevokeClient : IEbsAuthApiClient
    {
        public Task<EbsTokenResponse> ExchangeCodeAsync(string tokenEndpoint, string code, string codeVerifier, string redirectUri, CancellationToken ct = default) =>
            throw new InvalidOperationException("not used");

        public Task RevokeAsync(string revokeEndpoint, string token, CancellationToken ct = default) =>
            throw new HttpRequestException("EBS unreachable");
    }
}
