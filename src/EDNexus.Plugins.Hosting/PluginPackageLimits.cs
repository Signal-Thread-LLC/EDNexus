namespace EDNexus.Plugins.Hosting;

/// <summary>
/// Size limits enforced when reading a <c>.ednplugin</c> package, so a hostile or corrupt archive
/// (zip bomb, millions of entries) is rejected before it can exhaust disk or memory. The defaults
/// are generous for a managed plugin plus its dependencies.
/// </summary>
public sealed record PluginPackageLimits
{
    /// <summary>The default limits.</summary>
    public static PluginPackageLimits Default { get; } = new();

    /// <summary>Largest package file accepted, in bytes (default 64 MiB).</summary>
    public long MaxPackageBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Most archive entries (files + directories) accepted (default 2,000).</summary>
    public int MaxEntries { get; init; } = 2_000;

    /// <summary>Largest single uncompressed entry, in bytes (default 128 MiB).</summary>
    public long MaxEntryBytes { get; init; } = 128L * 1024 * 1024;

    /// <summary>Largest total uncompressed size of all entries, in bytes (default 256 MiB).</summary>
    public long MaxTotalUncompressedBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>
    /// Highest uncompressed:compressed ratio accepted for entries larger than
    /// <see cref="CompressionRatioThresholdBytes"/> (default 100:1) — a zip-bomb heuristic.
    /// </summary>
    public int MaxCompressionRatio { get; init; } = 100;

    /// <summary>Entries at or below this uncompressed size are exempt from the ratio check (default 1 MiB).</summary>
    public long CompressionRatioThresholdBytes { get; init; } = 1024 * 1024;
}
