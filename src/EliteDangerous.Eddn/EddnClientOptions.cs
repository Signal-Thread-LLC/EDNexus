namespace EliteDangerous.Eddn;

/// <summary>
/// Static configuration for an <see cref="EddnUploader"/> — the bits that identify the sending
/// application and never change between messages. The per-commander / per-event values
/// (uploaderID, game version, current system) live on <see cref="EddnState"/> instead.
/// </summary>
public sealed class EddnClientOptions
{
    /// <summary>A unique, stable name for the uploading application (e.g. "EDNexus").</summary>
    public required string SoftwareName { get; init; }

    /// <summary>The uploading application's version. Bump when message content changes.</summary>
    public required string SoftwareVersion { get; init; }

    /// <summary>The EDDN upload endpoint. Overridable for tests; defaults to the live relay.</summary>
    public string UploadEndpoint { get; init; } = "https://eddn.edcd.io:4430/upload/";

    /// <summary>When true, gzip the request body and set <c>Content-Encoding: gzip</c>.</summary>
    public bool UseGzip { get; init; }

    /// <summary>
    /// How long to wait before the single retry of a transient upload failure. Defaults to the
    /// EDDN-recommended minimum of one minute; tests can shorten it.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(1);
}
