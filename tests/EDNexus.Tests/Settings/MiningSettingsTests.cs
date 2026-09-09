using EDNexus.Core.Settings;
using Xunit;

namespace EDNexus.Tests.Settings;

public class MiningSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ednexus-mining-settings-").FullName;
    private string Path => System.IO.Path.Combine(_root, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void A_fresh_settings_file_has_no_threshold_or_known_prices()
    {
        var settings = new SettingsStore(Path).Load();

        Assert.Equal(0, settings.Mining.MinValueThreshold);
        Assert.Empty(settings.Mining.KnownPrices);
    }

    [Fact]
    public void The_threshold_and_learned_prices_round_trip_through_disk()
    {
        var store = new SettingsStore(Path);
        var settings = store.Load();
        settings.Mining.MinValueThreshold = 50000;
        settings.Mining.KnownPrices["alexandrite"] = 400000;
        settings.Mining.KnownPrices["painite"] = 44000;
        store.Save(settings);

        var reloaded = new SettingsStore(Path).Load();

        Assert.Equal(50000, reloaded.Mining.MinValueThreshold);
        Assert.Equal(400000, reloaded.Mining.KnownPrices["alexandrite"]);
        Assert.Equal(44000, reloaded.Mining.KnownPrices["painite"]);
    }
}
