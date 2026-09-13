using System.Text.Json;
using System.Threading.RateLimiting;
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
builder.Services.AddHttpClient<ITwitchPubSubClient, TwitchPubSubClient>();

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

app.MapPost("/api/update-state", async (
        HttpRequest httpRequest,
        UpdateStateRequest body,
        ITwitchExtensionJwtService jwtService,
        IChannelStateStore stateStore,
        ITwitchPubSubClient pubSubClient,
        IOptions<EbsOptions> ebsOptions,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var authResult = TryAuthenticateBroadcaster(httpRequest, jwtService);
        if (!authResult.IsValid || authResult.Claims is not { } claims)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrEmpty(claims.ChannelId))
        {
            return Results.Problem("Token is missing a channel_id claim.", statusCode: StatusCodes.Status401Unauthorized);
        }

        httpRequest.HttpContext.Items["ChannelId"] = claims.ChannelId;

        PubSubBroadcastRequest pubSubRequest;
        try
        {
            pubSubRequest = PubSubBroadcastRequest.Create(claims.ChannelId, body.State, ebsOptions.Value.MaxStatePayloadBytes);
        }
        catch (PubSubPayloadTooLargeException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        stateStore.Set(claims.ChannelId, body.State);

        var published = await pubSubClient.BroadcastAsync(claims.ChannelId, body.State, cancellationToken).ConfigureAwait(false);
        if (!published)
        {
            logger.LogWarning("Failed to publish state update for channel {ChannelId} to Twitch PubSub.", claims.ChannelId);
            return Results.Problem("Failed to publish the update to Twitch PubSub.", statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new
        {
            published = true,
            channelId = claims.ChannelId,
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

static TwitchJwtValidationResult TryAuthenticateBroadcaster(HttpRequest request, ITwitchExtensionJwtService jwtService)
{
    var header = request.Headers.Authorization.ToString();
    if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return TwitchJwtValidationResult.Failure("Missing or malformed Authorization header.");
    }

    var token = header["Bearer ".Length..].Trim();
    var result = jwtService.Validate(token);
    if (!result.IsValid || result.Claims is null)
    {
        return result;
    }

    if (!result.Claims.IsBroadcaster)
    {
        return TwitchJwtValidationResult.Failure("Only the channel broadcaster may submit state updates.");
    }

    return result;
}

/// <summary>Entry point marker used by <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program;
