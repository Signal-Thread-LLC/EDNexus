namespace EliteDangerous.Spansh;

/// <summary>
/// Static configuration for a <see cref="SpanshClient"/> — the sending-application identity used for
/// the <c>User-Agent</c>, and the API base address. Per-query values (commodity, reference system)
/// travel on <see cref="SpanshStationQuery"/> instead.
/// </summary>
public sealed class SpanshClientOptions
{
    /// <summary>A unique, stable name for the calling application (e.g. "EDNexus"), sent as the User-Agent product.</summary>
    public required string SoftwareName { get; init; }

    /// <summary>The calling application's version, sent as the User-Agent product version.</summary>
    public required string SoftwareVersion { get; init; }

    /// <summary>The Spansh API base address (no trailing slash needed). Overridable for tests.</summary>
    public string BaseUrl { get; init; } = "https://spansh.co.uk/api";

    /// <summary>
    /// Spansh route plots run as a background job: the POST returns a job id and the answer is polled.
    /// This is the spacing between polls — it doubles as basic rate-limit etiquette. Set to
    /// <see cref="TimeSpan.Zero"/> in tests to make polling immediate.
    /// </summary>
    public TimeSpan RoutePollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How many times to poll a route job before giving up with a timeout error. With the default
    /// 2 s interval this is roughly a three-minute ceiling — long routes genuinely take a while.
    /// </summary>
    public int RoutePollAttempts { get; init; } = 90;
}
