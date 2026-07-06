using System.Net;
using System.Text.Json.Nodes;
using EliteDangerous.Eddn;
using Xunit;

namespace EDNexus.Tests.Reporting;

public class EddnUploaderTests
{
    private static EddnMessage SampleMessage() => new()
    {
        SchemaRef = EddnSchemas.Journal,
        Envelope = new JsonObject { ["$schemaRef"] = EddnSchemas.Journal, ["message"] = new JsonObject() },
    };

    private static EddnClientOptions FastOptions() => new()
    {
        SoftwareName = "T",
        SoftwareVersion = "1",
        RetryDelay = TimeSpan.Zero,
    };

    [Fact]
    public async Task Enqueue_uploads_the_message_body()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        await using var uploader = new EddnUploader(FastOptions(), new HttpClient(handler));

        uploader.Enqueue(SampleMessage());
        await uploader.FlushAsync();

        Assert.Equal(1, handler.CallCount);
        Assert.Contains(EddnSchemas.Journal, handler.Bodies[0]);
    }

    [Fact]
    public async Task Bad_request_is_not_retried()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "rejected");
        EddnUploadResult? seen = null;
        await using var uploader = new EddnUploader(FastOptions(), new HttpClient(handler));
        uploader.Completed += r => seen = r;

        uploader.Enqueue(SampleMessage());
        await uploader.FlushAsync();

        Assert.Equal(1, handler.CallCount);        // 400 => permanent reject, no second attempt
        Assert.False(seen!.Success);
        Assert.Equal(HttpStatusCode.BadRequest, seen.Status);
    }

    [Fact]
    public async Task Transient_failure_is_retried_once()
    {
        // First call fails with 500, second succeeds.
        var handler = new RecordingHandler(n => n == 1
            ? (HttpStatusCode.InternalServerError, "err")
            : (HttpStatusCode.OK, "{}"));
        await using var uploader = new EddnUploader(FastOptions(), new HttpClient(handler));

        uploader.Enqueue(SampleMessage());
        await uploader.FlushAsync();

        Assert.Equal(2, handler.CallCount);
    }
}
