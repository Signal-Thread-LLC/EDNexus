using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace EliteDangerous.Eddn;

/// <summary>The result of a single upload attempt, surfaced to the optional observer for logging.</summary>
public sealed record EddnUploadResult(bool Success, HttpStatusCode? Status, string SchemaRef, string? Error);

/// <summary>
/// Uploads <see cref="EddnMessage"/> envelopes to the EDDN relay off the caller's thread. Messages
/// are sent one at a time in submission order so a slow network never blocks the journal pump.
/// Follows the EDDN retry rules: a 400/426 is a permanent reject (dropped, never retried); other
/// failures get one retry after <see cref="EddnClientOptions.RetryDelay"/> before being dropped.
/// </summary>
public sealed class EddnUploader : IAsyncDisposable
{
    private readonly EddnClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>Raised after every attempt (success or failure). Never throws back into the pump.</summary>
    public event Action<EddnUploadResult>? Completed;

    public EddnUploader(EddnClientOptions options, HttpClient? http = null)
    {
        _options = options;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
    }

    /// <summary>Queues a message for upload. Returns immediately; the send happens in the background.</summary>
    public void Enqueue(EddnMessage message)
    {
        var bytes = message.ToUtf8Bytes();
        var schemaRef = message.SchemaRef;
        lock (_gate)
        {
            var prev = _tail;
            _tail = ChainAsync(prev, bytes, schemaRef);
        }
    }

    /// <summary>Awaits the currently-queued uploads (used on shutdown and in tests).</summary>
    public Task FlushAsync()
    {
        lock (_gate) return _tail;
    }

    private async Task ChainAsync(Task previous, byte[] body, string schemaRef)
    {
        try { await previous.ConfigureAwait(false); }
        catch { /* a prior failure must not stall the chain */ }

        var result = await SendWithRetryAsync(body, schemaRef).ConfigureAwait(false);
        try { Completed?.Invoke(result); } catch { /* observer must never break the pump */ }
    }

    private async Task<EddnUploadResult> SendWithRetryAsync(byte[] body, string schemaRef)
    {
        var first = await TrySendAsync(body, schemaRef).ConfigureAwait(false);
        if (first.Success || first.Status is HttpStatusCode.BadRequest or HttpStatusCode.UpgradeRequired)
            return first; // success, or a permanent reject we must not retry

        try { await Task.Delay(_options.RetryDelay).ConfigureAwait(false); }
        catch { return first; }

        return await TrySendAsync(body, schemaRef).ConfigureAwait(false);
    }

    private async Task<EddnUploadResult> TrySendAsync(byte[] body, string schemaRef)
    {
        try
        {
            using var content = BuildContent(body);
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.UploadEndpoint)
            {
                Version = HttpVersion.Version11,
                Content = content,
            };
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new EddnUploadResult(true, response.StatusCode, schemaRef, null)
                : new EddnUploadResult(false, response.StatusCode, schemaRef,
                    await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return new EddnUploadResult(false, null, schemaRef, ex.Message);
        }
    }

    private HttpContent BuildContent(byte[] body)
    {
        if (!_options.UseGzip)
        {
            var raw = new ByteArrayContent(body);
            raw.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return raw;
        }

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(body, 0, body.Length);
        var content = new ByteArrayContent(ms.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");
        return content;
    }

    public async ValueTask DisposeAsync()
    {
        try { await FlushAsync().ConfigureAwait(false); }
        catch { /* best effort */ }
        if (_ownsHttp) _http.Dispose();
    }
}
