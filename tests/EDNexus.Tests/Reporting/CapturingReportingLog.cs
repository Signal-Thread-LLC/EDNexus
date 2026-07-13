using EDNexus.Core.Reporting;

namespace EDNexus.Tests.Reporting;

/// <summary>An <see cref="IReportingLog"/> that keeps every record in memory for assertions.</summary>
internal sealed class CapturingReportingLog : IReportingLog
{
    private readonly object _gate = new();
    private readonly List<ReportingUpload> _records = new();

    public void Record(ReportingUpload upload)
    {
        lock (_gate) _records.Add(upload);
    }

    /// <summary>A snapshot of the records captured so far.</summary>
    public IReadOnlyList<ReportingUpload> Records
    {
        get { lock (_gate) return _records.ToList(); }
    }
}
