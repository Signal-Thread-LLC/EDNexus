namespace EDNexus.Ebs.Options;

/// <summary>General service configuration for the Extension Backend Service itself.</summary>
public sealed class EbsOptions
{
    /// <summary>Configuration section name this type binds to.</summary>
    public const string SectionName = "Ebs";

    /// <summary>
    /// The HTTP port Kestrel listens on when no <c>ASPNETCORE_URLS</c>/<c>Kestrel</c> configuration
    /// is supplied. Defaults to 8787.
    /// </summary>
    public int Port { get; set; } = 8787;

    /// <summary>
    /// The maximum size, in bytes, of a serialized state payload the EBS will accept and forward to
    /// Twitch PubSub. Twitch enforces a hard 5 KiB limit on the PubSub message; we default to a
    /// slightly smaller value to leave headroom for the JSON envelope Twitch adds.
    /// </summary>
    public int MaxStatePayloadBytes { get; set; } = 5000;

    /// <summary>Maximum number of state updates accepted per broadcaster channel per window.</summary>
    public int UpdateStateRateLimit { get; set; } = 1;

    /// <summary>The rate limit window, in seconds, applied to <see cref="UpdateStateRateLimit"/>.</summary>
    public int UpdateStateRateLimitWindowSeconds { get; set; } = 2;
}
