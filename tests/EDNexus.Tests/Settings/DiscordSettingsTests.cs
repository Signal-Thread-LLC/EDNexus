using EDNexus.Core.Settings;
using Xunit;

namespace EDNexus.Tests.Settings;

public class DiscordSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ednexus-discord-settings-").FullName;
    private string Path => System.IO.Path.Combine(_root, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void A_fresh_settings_file_leaves_rich_presence_off()
    {
        var settings = new SettingsStore(Path).Load();

        Assert.False(settings.Discord.Enabled);
    }

    [Fact]
    public void A_settings_file_written_before_discord_existed_loads_with_rich_presence_off()
    {
        File.WriteAllText(Path, """{ "CrashReportingEnabled": false, "InstallId": "abc" }""");

        var settings = new SettingsStore(Path).Load();

        Assert.False(settings.Discord.Enabled);
    }

    [Fact]
    public void A_saved_true_is_kept_rather_than_reset_to_the_new_default()
    {
        // No migration: whatever the settings file already says wins over the default.
        File.WriteAllText(Path, """{ "InstallId": "abc", "Discord": { "Enabled": true } }""");

        var settings = new SettingsStore(Path).Load();

        Assert.True(settings.Discord.Enabled);
    }
}
