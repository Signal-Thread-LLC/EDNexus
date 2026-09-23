namespace EDNexus.Plugins.Abstractions;

/// <summary>
/// Static identity and metadata for a plugin, supplied by the host when it constructs the
/// plugin's <see cref="IPluginContext"/>. Parsed and validated from the plugin's
/// <c>plugin.json</c> by the host (<c>EDNexus.Plugins.Hosting.PluginManifestParser</c>) — this
/// record is just the shape the SDK exposes. See <c>src/EDNexus.Plugins.Hosting/PLUGIN_FORMAT.md</c>
/// for the on-disk schema.
/// </summary>
/// <param name="Id">
/// A stable, unique identifier for the plugin in lowercase reverse-DNS form
/// (e.g. <c>com.acme.jumpcounter</c>). It doubles as the plugin's install folder name.
/// </param>
/// <param name="Name">The plugin's human-readable display name.</param>
/// <param name="Version">The plugin's own version (not the SDK version), as SemVer 2.0 text.</param>
/// <param name="SdkVersion">
/// The <see cref="PluginSdk"/> contract version the plugin was built against, as <c>"major.minor"</c>.
/// The plugin is compatible with any host SDK of the same major and an equal-or-newer minor
/// (see <see cref="PluginSdk.IsCompatible"/>).
/// </param>
/// <param name="Author">The plugin author's name, or <see langword="null"/> if not specified.</param>
/// <param name="Description">A short description of what the plugin does, or <see langword="null"/>.</param>
public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string SdkVersion,
    string? Author = null,
    string? Description = null)
{
    /// <summary>
    /// The minimum EDNexus application version (SemVer) the plugin requires, or
    /// <see langword="null"/> when it runs on any app version that supports its SDK version.
    /// </summary>
    public string? MinAppVersion { get; init; }

    /// <summary>
    /// Path of the plugin's entry assembly, relative to the plugin folder, using <c>/</c> as the
    /// separator (e.g. <c>Acme.JumpCounter.dll</c>). Empty only for manifests not produced by
    /// the host parser (e.g. test doubles).
    /// </summary>
    public string EntryAssembly { get; init; } = string.Empty;

    /// <summary>
    /// Full CLR name of the type in <see cref="EntryAssembly"/> that implements
    /// <see cref="IEDNexusPlugin"/> (e.g. <c>Acme.JumpCounter.JumpCounterPlugin</c>).
    /// </summary>
    public string EntryType { get; init; } = string.Empty;

    private readonly IReadOnlyList<string> _capabilities = Array.Empty<string>();

    /// <summary>
    /// The capabilities the plugin declares it needs — a de-duplicated subset of
    /// <see cref="PluginCapabilities.All"/>. The host only wires the matching parts of
    /// <see cref="IPluginContext"/> for granted capabilities.
    /// </summary>
    /// <remarks>
    /// The setter stores a private, read-only copy: this record reaches plugin code through
    /// <see cref="IPluginContext.Manifest"/>, so neither the caller's collection nor a downcast of
    /// the returned list can be used to add a capability after validation.
    /// </remarks>
    public IReadOnlyList<string> Capabilities
    {
        get => _capabilities;
        init => _capabilities = value is null || value.Count == 0
            ? Array.Empty<string>()
            : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>Whether the manifest declares <paramref name="capability"/> (ordinal match).</summary>
    /// <param name="capability">One of the <see cref="PluginCapabilities"/> constants.</param>
    public bool Declares(string capability) => Capabilities.Contains(capability, StringComparer.Ordinal);

    /// <summary>
    /// Value equality over every field, comparing <see cref="Capabilities"/> element-wise (in order)
    /// rather than by reference, so two parses of the same <c>plugin.json</c> are equal.
    /// </summary>
    public bool Equals(PluginManifest? other)
        => other is not null
           && string.Equals(Id, other.Id, StringComparison.Ordinal)
           && string.Equals(Name, other.Name, StringComparison.Ordinal)
           && string.Equals(Version, other.Version, StringComparison.Ordinal)
           && string.Equals(SdkVersion, other.SdkVersion, StringComparison.Ordinal)
           && string.Equals(Author, other.Author, StringComparison.Ordinal)
           && string.Equals(Description, other.Description, StringComparison.Ordinal)
           && string.Equals(MinAppVersion, other.MinAppVersion, StringComparison.Ordinal)
           && string.Equals(EntryAssembly, other.EntryAssembly, StringComparison.Ordinal)
           && string.Equals(EntryType, other.EntryType, StringComparison.Ordinal)
           && Capabilities.SequenceEqual(other.Capabilities, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Version, StringComparer.Ordinal);
        hash.Add(SdkVersion, StringComparer.Ordinal);
        hash.Add(Author, StringComparer.Ordinal);
        hash.Add(Description, StringComparer.Ordinal);
        hash.Add(MinAppVersion, StringComparer.Ordinal);
        hash.Add(EntryAssembly, StringComparer.Ordinal);
        hash.Add(EntryType, StringComparer.Ordinal);
        foreach (var capability in Capabilities)
            hash.Add(capability, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
