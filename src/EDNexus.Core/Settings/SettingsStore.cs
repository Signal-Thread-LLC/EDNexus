using System.Text.Json;

namespace EDNexus.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON under the per-user app-data folder.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsStore(string? path = null)
        => _path = path ?? DefaultPath();

    public string Path => _path;

    public static string DefaultPath()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EDNexus");
        return System.IO.Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
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
