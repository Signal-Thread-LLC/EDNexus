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
    .Bind(builder.Configuration.GetSection(TwitchEbsOptions.SectionName))
    // Fail fast at startup rather than throwing a raw FormatException from inside a request handler
    // (TwitchExtensionJwtService.CreateExternalServiceToken) the first time a state update comes in.
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.ExtensionSecret) && IsValidBase64(o.ExtensionSecret),
        "Twitch:ExtensionSecret must be set to a non-empty, valid base64 string.")
    .ValidateOnStart();
builder.Services
    .AddOptions<EbsOptions>()
    .Bind(builder.Configuration.GetSection(EbsOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
// Still needed by TwitchPubSubClient to sign the EBS's own OUTBOUND JWT for the Helix PubSub call —
// Validate() (inbound JWT verification) is no longer used now that /api/update-state authenticates
// via the EBS-issued long-lived broadcaster token (IBroadcasterTokenStore) instead of a Twitch
// Extension JWT.
builder.Services.AddSingleton<ITwitchExtensionJwtService, TwitchExtensionJwtService>();
builder.Services.AddSingleton<IChannelStateStore, InMemoryChannelStateStore>();
builder.Services.AddSingleton<IBroadcasterTokenStore, InMemoryBroadcasterTokenStore>();
builder.Services.AddHttpClient<ITwitchPubSubClient, TwitchPubSubClient>(client =>
{
    // A hanging Helix call shouldn't be able to tie up a request indefinitely (the default
    // HttpClient timeout is 100s); combined with the per-channel rate limit below, this bounds how
    // long a single misbehaving/slow call can hold resources.
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<ITwitchOAuthClient, TwitchOAuthClient>();
builder.Services.AddHostedService<TwitchTokenRefreshBackgroundService>();

// CORS: only the extension's own Twitch-hosted iframe (https://*.ext-twitch.tv) — plus, for local
// development, the Twitch Developer Rig or any origin explicitly listed in Ebs:AdditionalAllowedFrontendOrigins
// — may read GET /api/initial-state/{channelId} from a browser context.
var additionalFrontendOrigins = new HashSet<string>(
    builder.Configuration.GetSection(EbsOptions.SectionName).Get<EbsOptions>()?.AdditionalAllowedFrontendOrigins ?? [],
    StringComparer.OrdinalIgnoreCase);
builder.Services.AddCors(options =>
{
    options.AddPolicy("extension-frontend", policy => policy
        .SetIsOriginAllowed(origin => IsAllowedFrontendOrigin(origin, additionalFrontendOrigins))
        .WithMethods("GET")
        .AllowAnyHeader());
});

// Rate limiting: throttle state updates and initial-state reads per broadcaster channel, so a
// misbehaving desktop client (or a burst of viewers) cannot exceed Twitch's own PubSub quota or
// exhaust EBS resources. The channel id is authenticated by AuthenticateBroadcasterMiddleware, which
// runs BEFORE UseRateLimiter — the partition-key callback below runs at routing time, before the
// endpoint delegate's body, so the channel id cannot come from anything set inside the handler
// itself (that would silently degrade every request to per-IP partitioning instead of per-channel).
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

app.UseCors("extension-frontend");

// Authenticates /api/update-state (via the EBS-issued long-lived broadcaster token — see
// IBroadcasterTokenStore) and stores the verified channel id in HttpContext.Items BEFORE
// UseRateLimiter runs its partition-key callback (rate-limiting middleware evaluates the policy at
// routing time — before the endpoint delegate's body executes — so setting Items from inside the
// handler, as this used to do, was always too late to affect partitioning for that same request).
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals("/api/update-state", StringComparison.OrdinalIgnoreCase))
    {
        var tokenStore = context.RequestServices.GetRequiredService<IBroadcasterTokenStore>();
        if (TryAuthenticateBroadcaster(context.Request, tokenStore, out var channelId, out var failure))
        {
            context.Items["ChannelId"] = channelId;
        }
        else
        {
            context.Items["BroadcasterAuthFailure"] = failure;
        }
    }

    await next().ConfigureAwait(false);
});

app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapOAuthEndpoints();

app.MapPost("/api/update-state", async (
        HttpRequest httpRequest,
        UpdateStateRequest body,
        IChannelStateStore stateStore,
        ITwitchPubSubClient pubSubClient,
        IOptions<EbsOptions> ebsOptions,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        // Authenticated by the middleware above (long-lived, EBS-issued token minted at
        // POST /oauth/token — not a Twitch/Extension JWT). The channel id is resolved server-side
        // from the token, never taken from the request body, so a compromised client cannot spoof
        // another broadcaster's channel.
        if (httpRequest.HttpContext.Items["ChannelId"] is not string channelId)
        {
            return (IResult?)httpRequest.HttpContext.Items["BroadcasterAuthFailure"] ?? Results.Unauthorized();
        }

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
    .RequireRateLimiting("initial-state")
    .RequireCors("extension-frontend");

app.Run();

static bool IsValidBase64(string value)
{
    try
    {
        Convert.FromBase64String(value);
        return true;
    }
    catch (FormatException)
    {
        return false;
    }
}

static bool IsAllowedFrontendOrigin(string origin, HashSet<string> additionalOrigins)
{
    if (additionalOrigins.Contains(origin))
    {
        return true;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
    {
        return false;
    }

    return uri.Host.Equals("ext-twitch.tv", StringComparison.OrdinalIgnoreCase)
        || uri.Host.EndsWith(".ext-twitch.tv", StringComparison.OrdinalIgnoreCase);
}

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
