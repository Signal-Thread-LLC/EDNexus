using System.Text.Json;
using System.Text.Json.Serialization;
using EDNexus.Core.Reporting;
using Xunit;

namespace EDNexus.Tests.Reporting;

public class FileReportingLogTests
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "ednexus-tests", Guid.NewGuid().ToString("N"), "reporting.log");

    [Fact]
    public void Records_are_appended_as_one_json_line_each()
    {
        var path = TempPath();
        try
        {
            var log = new FileReportingLog(path);
            log.Record(new ReportingUpload(DateTimeOffset.UnixEpoch, ReportingTarget.Eddn, "schema-a", true, "200 OK"));
            log.Record(new ReportingUpload(DateTimeOffset.UnixEpoch, ReportingTarget.Inara, "2 event(s)", false, "400", Error: "bad key"));

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);

            // Each line is a self-contained JSON object (JSONL) that round-trips.
            var first = JsonSerializer.Deserialize<ReportingUpload>(lines[0], ReadOptions);
            Assert.Equal(ReportingTarget.Eddn, first!.Target);
            Assert.True(first.Success);
            Assert.Contains("\"Target\":\"Inara\"", lines[1]);   // enum written as a name, not a number
            Assert.Contains("bad key", lines[1]);
        }
        finally
        {
            TryCleanup(path);
        }
    }

    [Fact]
    public void Log_rotates_to_old_once_it_passes_the_size_cap()
    {
        var path = TempPath();
        try
        {
            var log = new FileReportingLog(path, maxBytes: 200);
            for (var i = 0; i < 40; i++)
                log.Record(new ReportingUpload(DateTimeOffset.UnixEpoch, ReportingTarget.Eddn, $"schema-{i}", true, "200 OK"));

            // Once the primary file passed 200 bytes it was rolled to .old, so both exist and the
            // primary is small again — the log can never grow without bound.
            Assert.True(File.Exists(path));
            Assert.True(File.Exists(path + ".old"));
            Assert.True(new FileInfo(path).Length < 4096);
        }
        finally
        {
            TryCleanup(path);
            TryCleanup(path + ".old");
        }
    }

    private static void TryCleanup(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { }
    }
}
