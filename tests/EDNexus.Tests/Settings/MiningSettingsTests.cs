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
    public void A_fresh_settings_file_has_no_threshold_known_prices_or_session_totals()
    {
        var settings = new SettingsStore(Path).Load();

        Assert.Equal(0, settings.Mining.MinValueThreshold);
        Assert.Empty(settings.Mining.KnownPrices);
        Assert.Null(settings.Mining.SessionDate);
        Assert.Equal(0, settings.Mining.SessionValue);
        Assert.Null(settings.Mining.LastSessionDate);
    }

    [Fact]
    public void The_daily_session_totals_round_trip_through_disk()
    {
        var store = new SettingsStore(Path);
        var settings = store.Load();
        settings.Mining.SessionDate = "2026-09-09";
        settings.Mining.SessionValue = 128_500;
        settings.Mining.SessionUnits = 14;
        settings.Mining.LastSessionDate = "2026-09-08";
        settings.Mining.LastSessionValue = 96_000;
        settings.Mining.LastSessionUnits = 11;
        store.Save(settings);

        var reloaded = new SettingsStore(Path).Load();

        Assert.Equal("2026-09-09", reloaded.Mining.SessionDate);
        Assert.Equal(128_500, reloaded.Mining.SessionValue);
        Assert.Equal(14, reloaded.Mining.SessionUnits);
        Assert.Equal("2026-09-08", reloaded.Mining.LastSessionDate);
        Assert.Equal(96_000, reloaded.Mining.LastSessionValue);
        Assert.Equal(11, reloaded.Mining.LastSessionUnits);
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
