namespace EDNexus.Plugins.Abstractions;

/// <summary>
/// Static identity and metadata for a plugin, supplied by the host when it constructs the
/// plugin's <see cref="IPluginContext"/>. Parsed from the plugin's manifest file by the host
/// loader (see the plugin host issue) — this record is just the shape the SDK exposes.
/// </summary>
/// <param name="Id">A stable, unique identifier for the plugin (e.g. reverse-DNS or a slug).</param>
/// <param name="Name">The plugin's human-readable display name.</param>
/// <param name="Version">The plugin's own version (not the SDK version), as semver text.</param>
/// <param name="SdkVersion">
/// The <see cref="PluginSdk"/> contract version the plugin was built against, as <c>"major.minor"</c>.
/// </param>
/// <param name="Author">The plugin author's name, or <see langword="null"/> if not specified.</param>
/// <param name="Description">A short description of what the plugin does, or <see langword="null"/>.</param>
public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string SdkVersion,
    string? Author = null,
    string? Description = null);
