namespace EliteDangerous.Edsm;

/// <summary>
/// Static configuration for an <see cref="EdsmClient"/> — the sending-application identity used for
/// the <c>User-Agent</c>, and the API base address. Per-query values (system name, radius) travel on
/// the individual call arguments instead.
/// </summary>
public sealed class EdsmClientOptions
{
    /// <summary>A unique, stable name for the calling application (e.g. "EDNexus"), sent as the User-Agent product.</summary>
    public required string SoftwareName { get; init; }

    /// <summary>The calling application's version, sent as the User-Agent product version.</summary>
    public required string SoftwareVersion { get; init; }

    /// <summary>The EDSM API base address (no trailing slash needed). Overridable for tests.</summary>
    public string BaseUrl { get; init; } = "https://www.edsm.net";
}
