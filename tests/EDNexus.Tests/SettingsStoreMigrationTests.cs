using EDNexus.Core.Settings;
using Xunit;

namespace EDNexus.Tests;

public class SettingsStoreMigrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ednexus-migrate-").FullName;

    private string NewPath => Path.Combine(_root, "local", "EDNexus", "settings.json");
    private string LegacyPath => Path.Combine(_root, "docs", "EDNexus", "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Moves_legacy_file_and_removes_emptied_legacy_folder()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
        File.WriteAllText(LegacyPath, """{"InstallId":"abc123"}""");

        SettingsStore.TryMigrateLegacyFile(NewPath, LegacyPath);

        Assert.Contains("abc123", File.ReadAllText(NewPath));
        Assert.False(File.Exists(LegacyPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(LegacyPath)));
    }

    [Fact]
    public void Keeps_other_files_in_the_legacy_folder()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
        File.WriteAllText(LegacyPath, "{}");
        var sibling = Path.Combine(Path.GetDirectoryName(LegacyPath)!, "notes.txt");
        File.WriteAllText(sibling, "keep me");

        SettingsStore.TryMigrateLegacyFile(NewPath, LegacyPath);

        Assert.True(File.Exists(NewPath));
        Assert.True(File.Exists(sibling));
        Assert.True(Directory.Exists(Path.GetDirectoryName(LegacyPath)));
    }

    [Fact]
    public void Never_overwrites_an_existing_settings_file()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NewPath)!);
        File.WriteAllText(NewPath, """{"InstallId":"current"}""");
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
        File.WriteAllText(LegacyPath, """{"InstallId":"stale"}""");

        SettingsStore.TryMigrateLegacyFile(NewPath, LegacyPath);

        Assert.Contains("current", File.ReadAllText(NewPath));
        Assert.True(File.Exists(LegacyPath)); // untouched when nothing migrates
    }

    [Fact]
    public void NoOp_when_no_legacy_file_exists()
    {
        SettingsStore.TryMigrateLegacyFile(NewPath, LegacyPath);

        Assert.False(File.Exists(NewPath));
    }
}
