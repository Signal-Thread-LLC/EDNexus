using System.Text.Json;
using System.Text.Json.Serialization;

namespace EDNexus.Core.Reporting;

/// <summary>Which outbound reporter an <see cref="ReportingUpload"/> record came from.</summary>
public enum ReportingTarget { Eddn, Inara }

/// <summary>
/// A record of one outbound upload attempt, written for troubleshooting and validation: what went
/// out (<see cref="Summary"/> — the EDDN schema, or the Inara event list), whether the relay accepted
/// it (<see cref="Success"/> / <see cref="Status"/>), the error text on failure, and — only when
/// payload logging is enabled — the (redacted) JSON that was sent.
/// </summary>
public sealed record ReportingUpload(
    DateTimeOffset Timestamp,
    ReportingTarget Target,
    string Summary,
    bool Success,
    string Status,
    string? Error = null,
    string? Payload = null);

/// <summary>Sink that records every EDDN/Inara upload attempt. Implementations must never throw.</summary>
public interface IReportingLog
{
    void Record(ReportingUpload upload);
}

/// <summary>
/// An <see cref="IReportingLog"/> that appends one JSON object per line (JSONL) to a log file, so the
/// history can be tailed or parsed after the fact. Writes are serialised and best-effort — a failure
/// to log is swallowed so it can never break the upload pump. The file is rotated to <c>.old</c> once
/// it passes <see cref="_maxBytes"/> so it can't grow without bound.
/// </summary>
public sealed class FileReportingLog : IReportingLog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public FileReportingLog(string path, long maxBytes = 5 * 1024 * 1024)
    {
        _path = path;
        _maxBytes = maxBytes;
    }

    public void Record(ReportingUpload upload)
    {
        try
        {
            var line = JsonSerializer.Serialize(upload, Json);
            lock (_gate)
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                RotateIfNeeded();
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging is diagnostic only; a disk/permission problem must never disrupt reporting.
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < _maxBytes) return;
            var backup = _path + ".old";
            File.Delete(backup);          // no-op when absent
            File.Move(_path, backup);
        }
        catch
        {
            // If rotation fails we simply keep appending; still better than losing the write.
        }
    }
}
