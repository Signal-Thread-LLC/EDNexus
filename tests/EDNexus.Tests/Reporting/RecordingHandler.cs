using System.Net;

namespace EDNexus.Tests.Reporting;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that records every request body and returns a scripted
/// response, so the reporting clients can be exercised without touching the network.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<int, (HttpStatusCode Status, string Body)> _responder;
    private int _count;

    public List<string> Bodies { get; } = new();
    public List<Uri?> Uris { get; } = new();
    public int CallCount => _count;

    public RecordingHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
        : this(_ => (status, body)) { }

    public RecordingHandler(Func<int, (HttpStatusCode, string)> responder) => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Bodies) { Bodies.Add(body); Uris.Add(request.RequestUri); }
        var n = Interlocked.Increment(ref _count);
        var (status, respBody) = _responder(n);
        return new HttpResponseMessage(status) { Content = new StringContent(respBody) };
    }
}
