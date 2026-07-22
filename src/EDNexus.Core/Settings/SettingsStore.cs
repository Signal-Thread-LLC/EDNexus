using System.Text.Json;

namespace EDNexus.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON under the per-user app-data folder.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly bool _migrateLegacy;

    public SettingsStore(string? path = null)
    {
        _path = path ?? DefaultPath();
        _migrateLegacy = path is null;
    }

    public string Path => _path;

    public static string DefaultPath()
    {
        // Settings live under per-user local app data (LocalAppData\EDNexus on Windows;
        // ~/.local/share/EDNexus on Linux), alongside the logs — deliberately NOT the install
        // directory (read-only for normal users) and NOT Documents, which is commonly
        // cloud-synced (OneDrive Known Folder Move): that would copy the settings — including
        // the Inara API key — off-machine and invite sync conflicts on a frequently
        // rewritten file.
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EDNexus");
        return System.IO.Path.Combine(dir, "settings.json");
    }

    /// <summary>Where settings lived before moving out of Documents (see <see cref="DefaultPath"/>).</summary>
    public static string LegacyDefaultPath()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EDNexus");
        return System.IO.Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        if (_migrateLegacy) TryMigrateLegacyFile(_path, LegacyDefaultPath());

        AppSettings settings;
        try
        {
            settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            // Corrupt/unreadable settings should never brick startup — fall back to defaults.
            settings = new AppSettings();
        }

        // Assign a stable anonymous id on first load and persist it.
        if (string.IsNullOrWhiteSpace(settings.InstallId))
        {
            settings.InstallId = Guid.NewGuid().ToString("N");
            Save(settings);
        }
        return settings;
    }

    /// <summary>
    /// One-time move of the settings file from the pre-existing Documents location to
    /// <paramref name="path"/>. Runs only when the store uses the default path and no file exists
    /// there yet. The legacy file (and its folder, when left empty) is removed so the settings
    /// stop syncing to cloud storage. Best-effort: any failure leaves the legacy file in place.
    /// </summary>
    internal static void TryMigrateLegacyFile(string path, string legacyPath)
    {
        try
        {
            if (File.Exists(path) || !File.Exists(legacyPath)) return;

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Copy via a temp name + rename so a crash mid-copy can't leave a truncated
            // settings.json that would shadow the still-intact legacy file on the next run.
            var tmp = path + ".migrating";
            File.Copy(legacyPath, tmp, overwrite: true);
            File.Move(tmp, path);
            File.Delete(legacyPath);

            var legacyDir = System.IO.Path.GetDirectoryName(legacyPath);
            if (!string.IsNullOrEmpty(legacyDir) && Directory.Exists(legacyDir)
                && Directory.GetFileSystemEntries(legacyDir).Length == 0)
                Directory.Delete(legacyDir);
        }
        catch
        {
            // The legacy file survives any failure here, so nothing is lost — at worst the
            // migration retries on the next launch.
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // Best-effort; a failed save must not crash the app.
        }
    }
}
