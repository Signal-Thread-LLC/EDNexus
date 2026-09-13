using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
/// via <see cref="WebApplicationFactory{TEntryPoint}"/> — the security-critical properties the unit
/// tests around <c>TwitchExtensionJwtService</c> alone cannot exercise: the actual auth-failure status
/// codes, that the channel id used for the broadcast/rate-limit comes only from the verified token,
/// and the CORS policy on the browser-facing endpoint.
/// </summary>
public class UpdateStateEndpointTests : IClassFixture<UpdateStateEndpointTests.Factory>
{
    private const string SecretBase64 = "c3VwZXItc2VjcmV0LWV4dGVuc2lvbi1rZXktMTIzNA=="; // "super-secret-extension-key-1234"

    private readonly Factory _factory;

    public UpdateStateEndpointTests(Factory factory) => _factory = factory;

    private static string EncodeToken(object payload, byte[] key)
    {
        string Segment(object value)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        var headerSegment = Segment(new { alg = "HS256", typ = "JWT" });
        var payloadSegment = Segment(payload);
        var signingInput = Encoding.ASCII.GetBytes($"{headerSegment}.{payloadSegment}");
        var signature = HMACSHA256.HashData(key, signingInput);
        var signatureSegment = Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{headerSegment}.{payloadSegment}.{signatureSegment}";
    }

    private static string Token(string? channelId, byte[] key, string role = "broadcaster") =>
        EncodeToken(new
        {
            exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
            channel_id = channelId,
            user_id = channelId,
            opaque_user_id = channelId is null ? null : $"U{channelId}",
            role,
        }, key);

    [Fact]
    public async Task UpdateState_without_authorization_header_is_unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/update-state", new { state = new { foo = "bar" } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateState_rejects_non_broadcaster_role()
    {
        var key = Convert.FromBase64String(SecretBase64);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("chan-1", key, role: "viewer"));

        var response = await client.PostAsJsonAsync("/api/update-state", new { state = new { foo = "bar" } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateState_rejects_token_missing_channel_id_claim()
    {
        var key = Convert.FromBase64String(SecretBase64);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(channelId: null, key));

        var response = await client.PostAsJsonAsync("/api/update-state", new { state = new { foo = "bar" } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateState_accepts_valid_broadcaster_token_and_broadcasts_to_its_own_channel()
    {
        var key = Convert.FromBase64String(SecretBase64);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("chan-valid", key));

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
        var key = Convert.FromBase64String(SecretBase64);
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ebs:UpdateStateRateLimit"] = "1",
                ["Ebs:UpdateStateRateLimitWindowSeconds"] = "60",
            })));

        var clientA = factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new("Bearer", Token("chan-a", key));
        var clientB = factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new("Bearer", Token("chan-b", key));

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

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Twitch:ExtensionSecret"] = SecretBase64,
                ["Twitch:ClientId"] = "test-client-id",
                ["Twitch:ExtensionId"] = "test-extension-id",
                // Generous by default so unrelated tests sharing this fixture's single server instance
                // don't throttle each other (they'd otherwise all share one "unknown" IP bucket for
                // any request that never reaches a valid channel id). The dedicated rate-limit test
                // below spins up its own factory with a deliberately tight limit.
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
