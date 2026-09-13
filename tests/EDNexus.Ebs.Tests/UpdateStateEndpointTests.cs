using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EDNexus.Ebs.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EDNexus.Ebs.Tests;

/// <summary>
/// End-to-end coverage for <c>POST /api/update-state</c> and <c>GET /api/initial-state/{channelId}</c>
/// via <see cref="WebApplicationFactory{TEntryPoint}"/> — properties the unit tests around individual
/// services alone cannot exercise: the actual auth-failure status codes for the EBS-issued
/// broadcaster-token scheme, that the channel id used for the broadcast/rate-limit comes only from
/// the verified token (never the request body), and the CORS policy on the browser-facing endpoint.
/// </summary>
public class UpdateStateEndpointTests : IClassFixture<UpdateStateEndpointTests.Factory>
{
    private readonly Factory _factory;

    public UpdateStateEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task UpdateState_without_authorization_header_is_unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/update-state", new { state = new { foo = "bar" } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateState_rejects_an_unknown_token()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-real-token");

        var response = await client.PostAsJsonAsync("/api/update-state", new { state = new { foo = "bar" } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateState_rejects_a_token_whose_underlying_Twitch_grant_was_invalidated()
    {
        var record = _factory.TokenStore.IssueToken("chan-invalid-grant", "CMDR", "access", "refresh", DateTimeOffset.UtcNow.AddHours(4));
        _factory.TokenStore.MarkTwitchGrantInvalid("chan-invalid-grant");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", record.Token);

        var response = await client.PostAsJsonAsync("/api/update-state", new { state = new { foo = "bar" } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateState_accepts_a_valid_broadcaster_token_and_broadcasts_to_its_own_channel()
    {
        var record = _factory.TokenStore.IssueToken("chan-valid", "CMDR", "access", "refresh", DateTimeOffset.UtcNow.AddHours(4));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", record.Token);

        var response = await client.PostAsJsonAsync("/api/update-state", new { state = new { foo = "bar" } });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("chan-valid", body.GetProperty("channelId").GetString());
        Assert.True(_factory.PubSubClient.BroadcastCalls.ContainsKey("chan-valid"));
    }

    [Fact]
    public async Task Distinct_channels_get_independent_rate_limit_budgets_instead_of_sharing_one_ip_bucket()
    {
        // Regression test for the bug where the partition-key callback ran before the endpoint had a
        // chance to record the authenticated channel id in HttpContext.Items, so every request fell
        // back to partitioning by RemoteIpAddress — which TestServer reports as null/"unknown" for
        // every in-process request, meaning every channel used to share exactly one bucket.
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ebs:UpdateStateRateLimit"] = "1",
                ["Ebs:UpdateStateRateLimitWindowSeconds"] = "60",
            })));
        var tokenStore = factory.Services.GetRequiredService<IBroadcasterTokenStore>();
        var recordA = tokenStore.IssueToken("chan-a", "A", "access", "refresh", DateTimeOffset.UtcNow.AddHours(4));
        var recordB = tokenStore.IssueToken("chan-b", "B", "access", "refresh", DateTimeOffset.UtcNow.AddHours(4));

        var clientA = factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new("Bearer", recordA.Token);
        var clientB = factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new("Bearer", recordB.Token);

        var firstA = await clientA.PostAsJsonAsync("/api/update-state", new { state = new { foo = "bar" } });
        var firstB = await clientB.PostAsJsonAsync("/api/update-state", new { state = new { foo = "bar" } });
        var secondA = await clientA.PostAsJsonAsync("/api/update-state", new { state = new { foo = "bar" } });

        Assert.Equal(HttpStatusCode.OK, firstA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstB.StatusCode); // would be 429 under the old IP-fallback bug
        Assert.Equal(HttpStatusCode.TooManyRequests, secondA.StatusCode); // channel A's own budget is still enforced
    }

    [Fact]
    public async Task InitialState_allows_the_extension_iframe_origin()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/initial-state/chan-1");
        request.Headers.Add("Origin", "https://abc123.ext-twitch.tv");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task InitialState_does_not_allow_an_untrusted_origin()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/initial-state/chan-1");
        request.Headers.Add("Origin", "https://evil.example.com");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public FakeTwitchPubSubClient PubSubClient { get; } = new();

        public IBroadcasterTokenStore TokenStore => Services.GetRequiredService<IBroadcasterTokenStore>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Twitch:ExtensionSecret"] = "c3VwZXItc2VjcmV0LWV4dGVuc2lvbi1rZXktMTIzNA==",
                ["Twitch:ClientId"] = "test-client-id",
                ["Twitch:ExtensionId"] = "test-extension-id",
                // Generous by default so unrelated tests sharing this fixture's single server instance
                // don't throttle each other (they'd otherwise all share one "unknown" IP bucket for
                // any request that never reaches a valid channel id). The dedicated rate-limit test
                // above spins up its own factory with a deliberately tight limit.
                ["Ebs:UpdateStateRateLimit"] = "1000",
                ["Ebs:UpdateStateRateLimitWindowSeconds"] = "1",
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITwitchPubSubClient>();
                services.AddSingleton<ITwitchPubSubClient>(PubSubClient);
            });
        }
    }
}

/// <summary>Records every broadcast call instead of making a real Helix API request.</summary>
public sealed class FakeTwitchPubSubClient : ITwitchPubSubClient
{
    public Dictionary<string, int> BroadcastCalls { get; } = new();

    public Task<bool> BroadcastAsync(string broadcasterId, JsonElement state, CancellationToken cancellationToken)
    {
        BroadcastCalls[broadcasterId] = BroadcastCalls.GetValueOrDefault(broadcasterId) + 1;
        return Task.FromResult(true);
    }
}
