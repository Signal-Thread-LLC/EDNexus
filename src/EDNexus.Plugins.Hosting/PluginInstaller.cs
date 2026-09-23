using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting;

/// <summary>The outcome of <see cref="PluginInstaller.Install"/>.</summary>
public sealed class PluginInstallResult
{
    private PluginInstallResult(PluginManifest? manifest, string? directory, IReadOnlyList<string> errors)
    {
        Manifest = manifest;
        Directory = directory;
        Errors = errors;
    }

    /// <summary>The installed plugin's manifest, or <see langword="null"/> on failure.</summary>
    public PluginManifest? Manifest { get; }

    /// <summary>The folder the plugin was installed into, or <see langword="null"/> on failure.</summary>
    public string? Directory { get; }

    /// <summary>Why installation failed. Empty on success.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Whether the plugin was installed.</summary>
    public bool Succeeded => Errors.Count == 0;

    /// <summary>All <see cref="Errors"/> joined into one line, for logs and UI.</summary>
    public string ErrorSummary => string.Join("; ", Errors);

    internal static PluginInstallResult Success(PluginManifest manifest, string directory) => new(manifest, directory, []);

    internal static PluginInstallResult Failure(IReadOnlyList<string> errors) => new(null, null, errors);

    internal static PluginInstallResult Failure(string error) => new(null, null, [error]);
}

/// <summary>
/// Installs a <c>.ednplugin</c> package into the plugins root: validate the whole package, extract
/// it into a staging folder, then move it into place as <c>&lt;root&gt;/&lt;id&gt;/</c>. Nothing is
/// written under <c>&lt;id&gt;</c> unless the package is valid, and a failed replace restores the
/// previous version. This only lays files down — loading is the plugin host's job (#55).
/// </summary>
public static class PluginInstaller
{
    /// <summary>Prefix of transient folders under the plugins root (never a valid plugin id).</summary>
    internal const string StagingPrefix = ".staging-";

    private const string BackupPrefix = ".replaced-";

    /// <summary>Installs the package at <paramref name="packagePath"/> into <paramref name="pluginsRoot"/>.</summary>
    /// <param name="packagePath">Path of the <c>.ednplugin</c> file.</param>
    /// <param name="pluginsRoot">The plugins root (see <see cref="PluginPaths.Resolve()"/>).</param>
    /// <param name="replaceExisting">
    /// Whether to replace an already-installed plugin with the same id. When false, installing a
    /// duplicate id is rejected.
    /// </param>
    /// <param name="limits">Package limits; <see cref="PluginPackageLimits.Default"/> when omitted.</param>
    public static PluginInstallResult Install(
        string packagePath, string pluginsRoot, bool replaceExisting = false, PluginPackageLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(packagePath);
        ArgumentNullException.ThrowIfNull(pluginsRoot);

        var inspection = PluginPackage.Inspect(packagePath, limits);
        if (!inspection.IsValid)
            return PluginInstallResult.Failure(inspection.Errors);
        var id = inspection.Manifest!.Id;

        string root;
        string target;
        try
        {
            root = Path.GetFullPath(pluginsRoot);
            target = PluginPaths.PluginDirectory(root, id);
            System.IO.Directory.CreateDirectory(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return PluginInstallResult.Failure($"plugins folder '{pluginsRoot}' is not usable: {ex.Message}");
        }

        var exists = System.IO.Directory.Exists(target);
        if (exists && !replaceExisting)
            return PluginInstallResult.Failure($"a plugin with id \"{id}\" is already installed");

        // Extract into a sibling staging folder (same volume, so the final move is atomic).
        var staging = Path.Combine(root, StagingPrefix + Guid.NewGuid().ToString("N"));
        var extracted = PluginPackage.ExtractTo(packagePath, staging, limits);
        if (!extracted.IsValid)
            return PluginInstallResult.Failure(extracted.Errors);

        // The package was re-validated while extracting; make sure it is still the same plugin.
        if (!string.Equals(extracted.Manifest!.Id, id, StringComparison.Ordinal))
        {
            PluginPackage.TryDeleteDirectory(staging);
            return PluginInstallResult.Failure("package changed while it was being installed");
        }

        string? backup = null;
        try
        {
            if (exists)
            {
                backup = Path.Combine(root, BackupPrefix + Guid.NewGuid().ToString("N"));
                System.IO.Directory.Move(target, backup);
            }

            System.IO.Directory.Move(staging, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (backup is not null && !System.IO.Directory.Exists(target))
            {
                try { System.IO.Directory.Move(backup, target); backup = null; }
                catch (Exception restoreEx) when (restoreEx is IOException or UnauthorizedAccessException) { }
            }
            PluginPackage.TryDeleteDirectory(staging);
            return PluginInstallResult.Failure($"could not move plugin into place: {ex.Message}");
        }

        if (backup is not null)
            PluginPackage.TryDeleteDirectory(backup);

        return PluginInstallResult.Success(extracted.Manifest, target);
    }
}
