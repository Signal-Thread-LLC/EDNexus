namespace EliteDangerous.Inara;

/// <summary>
/// Static configuration for an <see cref="InaraClient"/> — the application-identity fields that go
/// in every request header. The per-commander credentials travel separately in <see cref="InaraIdentity"/>.
/// </summary>
public sealed class InaraClientOptions
{
    /// <summary>The sending application's name (e.g. "EDNexus").</summary>
    public required string AppName { get; init; }

    /// <summary>The sending application's version.</summary>
    public required string AppVersion { get; init; }

    /// <summary>
    /// True while the app is in development. Inara uses this to segregate test traffic from real
    /// commander stats, so leave it true for non-release builds.
    /// </summary>
    public bool IsBeingDeveloped { get; init; }

    /// <summary>The Inara API endpoint. Overridable for tests; defaults to the live v1 endpoint.</summary>
    public string Endpoint { get; init; } = "https://inara.cz/inapi/v1/";
}
