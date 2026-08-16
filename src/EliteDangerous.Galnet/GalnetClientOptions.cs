namespace EliteDangerous.Galnet;

/// <summary>
/// Static configuration for a <see cref="GalnetClient"/> — the sending-application identity used for
/// the <c>User-Agent</c>, and the feed address.
/// </summary>
public sealed class GalnetClientOptions
{
    /// <summary>A unique, stable name for the calling application (e.g. "EDNexus"), sent as the User-Agent product.</summary>
    public required string SoftwareName { get; init; }

    /// <summary>The calling application's version, sent as the User-Agent product version.</summary>
    public required string SoftwareVersion { get; init; }

    /// <summary>The Galnet RSS address. Overridable for tests and for the localised feeds.</summary>
    public string FeedUrl { get; init; } = "https://community.elitedangerous.com/galnet-rss";
}
