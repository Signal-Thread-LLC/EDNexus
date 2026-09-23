using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EDNexus.Core.Twitch;

/// <summary>Why a <see cref="StreamCardSnapshot"/> publish did not reach viewers.</summary>
public enum StreamStatePublishStatus
{
    /// <summary>The EBS accepted the snapshot and relayed it to Twitch PubSub.</summary>
    Published,

    /// <summary>
    /// The EBS rejected the token (<c>401</c>) — either it was never valid or the underlying Twitch
    /// grant has been revoked. The commander has to log in again; retrying will not help.
    /// </summary>
    Unauthorized,

    /// <summary>The snapshot exceeded Twitch's 5 KiB message cap (<c>413</c>).</summary>
    TooLarge,

    /// <summary>The per-channel rate limit was hit (<c>429</c>). Back off and send the next snapshot instead.</summary>
    RateLimited,

    /// <summary>Anything else: the EBS was unreachable, timed out, or Twitch PubSub itself failed.</summary>
    Failed,
}

/// <param name="Status">Whether the snapshot reached viewers, and if not, why not.</param>
/// <param name="Error">Detail for logging/diagnostics. Null on success.</param>
public sealed record StreamStatePublishResult(StreamStatePublishStatus Status, string? Error = null)
{
    public bool IsSuccess => Status == StreamStatePublishStatus.Published;

    /// <summary>
    /// True when retrying the same credential is pointless — the commander must log in again. The
    /// publisher stops sending on this rather than hammering the EBS with a dead token.
    /// </summary>
    public bool RequiresReauth => Status == StreamStatePublishStatus.Unauthorized;

    public static readonly StreamStatePublishResult Ok = new(StreamStatePublishStatus.Published);
}

/// <summary>
/// Publishes commander snapshots to the EBS's <c>POST /api/update-state</c>, which relays them to
/// viewers over Twitch Extensions PubSub. Pure transport: no throttling, no state, no policy — those
/// belong to <see cref="TwitchStreamCardService"/>.
/// </summary>
public interface IStreamStateApiClient
{
    /// <summary>
    /// Sends one snapshot. The broadcaster channel is never part of the request: the EBS resolves it
    /// server-side from <paramref name="token"/>, so this client cannot publish to a channel the
    /// commander does not own.
    /// </summary>
    /// <param name="updateStateEndpoint">Absolute URL of the EBS's <c>/api/update-state</c>.</param>
    /// <param name="token">The long-lived, EBS-issued broadcaster token from the login flow.</param>
    /// <param name="snapshot">What viewers should see.</param>
    Task<StreamStatePublishResult> PublishAsync(
        string updateStateEndpoint, string token, StreamCardSnapshot snapshot, CancellationToken ct = default);
}

/// <summary>Default <see cref="IStreamStateApiClient"/> over a real (or injected, for tests) <see cref="HttpClient"/>.</summary>
public sealed class StreamStateApiClient : IStreamStateApiClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public StreamStateApiClient(HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<StreamStatePublishResult> PublishAsync(
        string updateStateEndpoint, string token, StreamCardSnapshot snapshot, CancellationToken ct = default)
    {
        // The EBS's body shape is { "state": <payload> } — see EDNexus.Ebs UpdateStateRequest.
        var body = JsonSerializer.Serialize(
            new StateEnvelope(snapshot), StreamCardSnapshot.SerializerOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, updateStateEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A stream card is best-effort telemetry: an unreachable EBS must never surface as an
            // unhandled exception on the commander's machine mid-session.
            return new StreamStatePublishResult(StreamStatePublishStatus.Failed, ex.Message);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return StreamStatePublishResult.Ok;

            var status = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => StreamStatePublishStatus.Unauthorized,
                HttpStatusCode.RequestEntityTooLarge => StreamStatePublishStatus.TooLarge,
                HttpStatusCode.TooManyRequests => StreamStatePublishStatus.RateLimited,
                _ => StreamStatePublishStatus.Failed,
            };

            var detail = await SafeReadAsync(response, ct).ConfigureAwait(false);
            return new StreamStatePublishResult(status, $"HTTP {(int)response.StatusCode} — {detail}");
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { return response.ReasonPhrase ?? ""; }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    /// <summary>Mirrors the EBS's <c>UpdateStateRequest</c> body: the snapshot under a <c>state</c> property.</summary>
    private sealed record StateEnvelope(
        [property: System.Text.Json.Serialization.JsonPropertyName("state")] StreamCardSnapshot State);
}
