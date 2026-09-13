using System.Net;
using System.Text;

namespace EDNexus.Core.Twitch;

/// <summary>
/// Waits for the single HTTP redirect Twitch sends back to the app's loopback redirect URI, and
/// returns its query-string parameters (<c>code</c>/<c>state</c>, or <c>error</c>/<c>error_description</c>
/// on denial). One instance serves exactly one login attempt.
/// </summary>
public interface IOAuthCallbackListener
{
    /// <summary>
    /// Starts listening on <paramref name="redirectUri"/> and returns the query parameters of the
    /// first request to its path. Cancelling <paramref name="ct"/> (e.g. on timeout) stops the
    /// listener and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> WaitForCallbackAsync(Uri redirectUri, CancellationToken ct);
}

/// <summary>
/// Real implementation backed by <see cref="HttpListener"/> — a temporary local HTTP server bound
/// only to loopback, torn down as soon as the callback (or a cancellation) arrives.
/// </summary>
public sealed class LoopbackOAuthCallbackListener : IOAuthCallbackListener
{
    public async Task<IReadOnlyDictionary<string, string>> WaitForCallbackAsync(Uri redirectUri, CancellationToken ct)
    {
        var prefix = new UriBuilder(redirectUri) { Path = "/", Query = null, Fragment = null }.Uri.ToString();

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not bind the local OAuth callback listener on {prefix} — is another instance of EDNexus already running?", ex);
        }

        await using var registration = ct.Register(() =>
        {
            try { listener.Stop(); } catch { /* best-effort teardown */ }
        });

        try
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }

                if (!string.Equals(context.Request.Url?.AbsolutePath, redirectUri.AbsolutePath, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                var parameters = ParseQuery(context.Request.Url!.Query);
                await RespondAsync(context, parameters.ContainsKey("error")).ConfigureAwait(false);
                return parameters;
            }
        }
        finally
        {
            try { listener.Stop(); } catch { /* already stopped/disposed */ }
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, bool isError)
    {
        var title = isError ? "Twitch login cancelled" : "Twitch login complete";
        var body = isError
            ? "Something went wrong or the request was cancelled. You can close this tab and return to EDNexus."
            : "You're all set — you can close this tab and return to EDNexus.";
        var html = $"""
            <!DOCTYPE html>
            <html><head><title>{title}</title></head>
            <body style="font-family: sans-serif; background:#141414; color:#e0a030; text-align:center; padding-top: 10vh;">
            <h2>{title}</h2><p>{body}</p>
            </body></html>
            """;

        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        try
        {
            await context.Response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    /// <summary>Minimal query-string parser — avoids a dependency on System.Web for a handful of known keys.</summary>
    internal static Dictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            var key = idx >= 0 ? pair[..idx] : pair;
            var value = idx >= 0 ? pair[(idx + 1)..] : "";
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        return result;
    }
}
