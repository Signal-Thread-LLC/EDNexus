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
/// previous version; if the process dies mid-replace, <see cref="RecoverInterrupted"/> restores it
/// on the next start. This only lays files down — loading is the plugin host's job (#55).
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
        => InstallCore(packagePath, pluginsRoot, replaceExisting, limits, System.IO.Directory.Move);

    /// <summary>As <see cref="Install"/>, with an injectable directory-move step so tests can fail it.</summary>
    internal static PluginInstallResult InstallCore(
        string packagePath, string pluginsRoot, bool replaceExisting, PluginPackageLimits? limits,
        Action<string, string> moveDirectory)
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

        // Replace = two renames. If the process dies between them, <id> is missing and the old
        // version sits in ".replaced-<id>.<guid>"; RecoverInterrupted puts it back on next start.
        string? backup = null;
        try
        {
            if (exists)
            {
                backup = Path.Combine(root, BackupName(id));
                moveDirectory(target, backup);
            }

            moveDirectory(staging, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var message = $"could not move plugin into place: {ex.Message}";
            if (backup is not null && !System.IO.Directory.Exists(target))
            {
                try
                {
                    moveDirectory(backup, target);
                }
                catch (Exception restoreEx) when (restoreEx is IOException or UnauthorizedAccessException)
                {
                    // Leave the backup where it is (RecoverInterrupted will retry) and say where.
                    message += $"; restoring the previous version also failed ({restoreEx.Message}) — it is preserved at '{backup}'";
                }
            }
            PluginPackage.TryDeleteDirectory(staging);
            return PluginInstallResult.Failure(message);
        }

        if (backup is not null)
            PluginPackage.TryDeleteDirectory(backup);

        return PluginInstallResult.Success(extracted.Manifest, target);
    }

    /// <summary>
    /// Repairs the plugins root after an install was interrupted (crash, power loss, kill):
    /// restores a <c>.replaced-*</c> backup when its plugin folder is missing, deletes backups
    /// whose replacement completed, and deletes leftover <c>.staging-*</c> and <c>.extract-*</c>
    /// folders.
    /// <para>
    /// Call once when the host starts, before discovering plugins and before any install — it
    /// must not run concurrently with <see cref="Install"/> on the same root. Never throws for
    /// filesystem problems; they are reported in <see cref="PluginRecoveryResult.Errors"/>.
    /// </para>
    /// </summary>
    /// <param name="pluginsRoot">The plugins root (see <see cref="PluginPaths.Resolve()"/>).</param>
    public static PluginRecoveryResult RecoverInterrupted(string pluginsRoot)
    {
        ArgumentNullException.ThrowIfNull(pluginsRoot);
        var restored = new List<string>();
        var removed = new List<string>();
        var errors = new List<string>();

        string root;
        string[] entries;
        try
        {
            root = Path.GetFullPath(pluginsRoot);
            if (!System.IO.Directory.Exists(root))
                return new PluginRecoveryResult(restored, removed, errors);
            entries = System.IO.Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            errors.Add($"plugins folder '{pluginsRoot}' could not be read: {ex.Message}");
            return new PluginRecoveryResult(restored, removed, errors);
        }

        // Stale staging folders (the installer's, and PluginPackage.ExtractTo's sibling extraction
        // folders) are never needed: the operation that owned them did not finish.
        foreach (var dir in entries.Where(d => Path.GetFileName(d) is var name
                     && (name.StartsWith(StagingPrefix, StringComparison.Ordinal)
                         || name.StartsWith(PluginPackage.ExtractStagingPrefix, StringComparison.Ordinal))))
            Remove(dir);

        // Backups, newest first per id, so the most recent previous version wins a restore.
        var backups = entries
            .Select(d => (Path: d, Id: TryParseBackupId(Path.GetFileName(d))))
            .Where(b => b.Id is not null)
            .OrderByDescending(b => SafeLastWrite(b.Path));
        foreach (var (path, id) in backups)
        {
            var target = Path.Combine(root, id!);
            if (System.IO.Directory.Exists(target))
            {
                Remove(path); // the replacement completed; this is the superseded version
                continue;
            }
            if (File.Exists(target))
            {
                // A stray file is not proof the replace finished, and this may be the only copy
                // of the plugin: keep the backup and let the user sort it out.
                errors.Add($"could not restore '{path}': a file is in the way at '{target}'; the backup was kept");
                continue;
            }

            try
            {
                System.IO.Directory.Move(path, target);
                restored.Add(id!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"could not restore '{path}' to '{target}': {ex.Message}");
            }
        }

        return new PluginRecoveryResult(restored, removed, errors);

        void Remove(string dir)
        {
            PluginPackage.TryDeleteDirectory(dir);
            if (System.IO.Directory.Exists(dir))
                errors.Add($"could not delete leftover folder '{dir}'");
            else
                removed.Add(Path.GetFileName(dir));
        }
    }

    /// <summary><c>.replaced-&lt;id&gt;.&lt;guid&gt;</c> — the id is recoverable from the name alone.</summary>
    internal static string BackupName(string id) => BackupPrefix + id + "." + Guid.NewGuid().ToString("N");

    /// <summary>The plugin id encoded in a backup folder name, or <see langword="null"/> if it isn't one.</summary>
    internal static string? TryParseBackupId(string? folderName)
    {
        if (folderName is null || !folderName.StartsWith(BackupPrefix, StringComparison.Ordinal))
            return null;
        var rest = folderName[BackupPrefix.Length..];
        var dot = rest.LastIndexOf('.');
        if (dot <= 0 || !Guid.TryParseExact(rest[(dot + 1)..], "N", out _))
            return null;
        var id = rest[..dot];
        return PluginManifestParser.IsValidId(id) ? id : null;
    }

    private static DateTime SafeLastWrite(string path)
    {
        try { return System.IO.Directory.GetLastWriteTimeUtc(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }
}

/// <summary>What <see cref="PluginInstaller.RecoverInterrupted"/> did.</summary>
/// <param name="Restored">Ids whose previous version was moved back into place.</param>
/// <param name="Removed">Names of leftover staging/backup folders that were deleted.</param>
/// <param name="Errors">Problems that could not be fixed (the folders involved are left as-is).</param>
public sealed record PluginRecoveryResult(
    IReadOnlyList<string> Restored,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Errors);
