using System.Text.Json;
using System.Threading.RateLimiting;
using EDNexus.Ebs.Endpoints;
using EDNexus.Ebs.Models;
using EDNexus.Ebs.Options;
using EDNexus.Ebs.Security;
using EDNexus.Ebs.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<TwitchEbsOptions>()
    .Bind(builder.Configuration.GetSection(TwitchEbsOptions.SectionName));
builder.Services
    .AddOptions<EbsOptions>()
    .Bind(builder.Configuration.GetSection(EbsOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITwitchExtensionJwtService, TwitchExtensionJwtService>();
builder.Services.AddSingleton<IChannelStateStore, InMemoryChannelStateStore>();
builder.Services.AddSingleton<IBroadcasterTokenStore, InMemoryBroadcasterTokenStore>();
builder.Services.AddHttpClient<ITwitchPubSubClient, TwitchPubSubClient>();
builder.Services.AddHttpClient<ITwitchOAuthClient, TwitchOAuthClient>();
builder.Services.AddHostedService<TwitchTokenRefreshBackgroundService>();

// Rate limiting: throttle state updates and initial-state reads per broadcaster channel, so a
// misbehaving desktop client (or a burst of viewers) cannot exceed Twitch's own PubSub quota or
// exhaust EBS resources. Falls back to partitioning by remote IP when no channel id is known yet.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("update-state", httpContext =>
    {
        var ebsOptions = httpContext.RequestServices.GetRequiredService<IOptions<EbsOptions>>().Value;
        var partitionKey = httpContext.Items.TryGetValue("ChannelId", out var channelId) && channelId is string id
            ? id
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, ebsOptions.UpdateStateRateLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, ebsOptions.UpdateStateRateLimitWindowSeconds)),
            QueueLimit = 0,
        });
    });

    options.AddPolicy("initial-state", httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromSeconds(10),
            QueueLimit = 0,
        });
    });
});

// Only fall back to the configured Ebs:Port when nothing else (ASPNETCORE_URLS, --urls, launch
// profile, etc.) has already told Kestrel what to bind to.
var urlsAlreadyConfigured = builder.Configuration["urls"] is not null
    || Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is not null;
if (!urlsAlreadyConfigured && !builder.Environment.IsEnvironment("Testing"))
{
    var configuredPort = builder.Configuration.GetSection(EbsOptions.SectionName).Get<EbsOptions>()?.Port ?? 8787;
    builder.WebHost.UseUrls($"http://0.0.0.0:{configuredPort}");
}

var app = builder.Build();

app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapOAuthEndpoints();

app.MapPost("/api/update-state", async (
        HttpRequest httpRequest,
        UpdateStateRequest body,
        IBroadcasterTokenStore tokenStore,
        IChannelStateStore stateStore,
        ITwitchPubSubClient pubSubClient,
        IOptions<EbsOptions> ebsOptions,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        // Authenticated by the long-lived, EBS-issued token minted at POST /oauth/token — not a
        // Twitch/Extension JWT. The channel id is resolved server-side from the token, never taken
        // from the request body, so a compromised client cannot spoof another broadcaster's channel.
        if (!TryAuthenticateBroadcaster(httpRequest, tokenStore, out var channelId, out var failure))
        {
            return failure!;
        }

        httpRequest.HttpContext.Items["ChannelId"] = channelId;

        PubSubBroadcastRequest pubSubRequest;
        try
        {
            pubSubRequest = PubSubBroadcastRequest.Create(channelId, body.State, ebsOptions.Value.MaxStatePayloadBytes);
        }
        catch (PubSubPayloadTooLargeException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        stateStore.Set(channelId, body.State);

        var published = await pubSubClient.BroadcastAsync(channelId, body.State, cancellationToken).ConfigureAwait(false);
        if (!published)
        {
            logger.LogWarning("Failed to publish state update for channel {ChannelId} to Twitch PubSub.", channelId);
            return Results.Problem("Failed to publish the update to Twitch PubSub.", statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new
        {
            published = true,
            channelId,
            bytes = System.Text.Encoding.UTF8.GetByteCount(pubSubRequest.Message),
        });
    })
    .RequireRateLimiting("update-state");

app.MapGet("/api/initial-state/{channelId}", (string channelId, IChannelStateStore stateStore) =>
    {
        if (!stateStore.TryGet(channelId, out var state))
        {
            return Results.NotFound(new { message = "No state has been published for this channel yet." });
        }

        return Results.Ok(state);
    })
    .RequireRateLimiting("initial-state");

app.Run();

static bool TryAuthenticateBroadcaster(HttpRequest request, IBroadcasterTokenStore tokenStore, out string channelId, out IResult? failure)
{
    channelId = "";
    var header = request.Headers.Authorization.ToString();
    if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        failure = Results.Unauthorized();
        return false;
    }

    var token = header["Bearer ".Length..].Trim();
    if (!tokenStore.TryGetByToken(token, out var record))
    {
        failure = Results.Unauthorized();
        return false;
    }

    if (!record.IsTwitchGrantValid)
    {
        failure = Results.Problem(
            "The underlying Twitch grant is no longer valid — please log in again.",
            statusCode: StatusCodes.Status401Unauthorized);
        return false;
    }

    channelId = record.ChannelId;
    failure = null;
    return true;
}

/// <summary>Entry point marker used by <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program;
