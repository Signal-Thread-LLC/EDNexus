using EDNexus.Core.Radio;
using EDNexus.Core.Settings;
using Xunit;

namespace EDNexus.Tests.Radio;

public class RadioStationCatalogTests
{
    [Fact]
    public void Next_from_null_starts_at_first_station()
        => Assert.Equal(RadioStationCatalog.Stations[0].Id, RadioStationCatalog.Next(null).Id);

    [Fact]
    public void Previous_from_null_starts_at_first_station()
        => Assert.Equal(RadioStationCatalog.Stations[0].Id, RadioStationCatalog.Previous(null).Id);

    [Fact]
    public void Next_advances_by_one_and_wraps_to_the_start()
    {
        var stations = RadioStationCatalog.Stations;
        for (var i = 0; i < stations.Count - 1; i++)
            Assert.Equal(stations[i + 1].Id, RadioStationCatalog.Next(stations[i].Id).Id);

        Assert.Equal(stations[0].Id, RadioStationCatalog.Next(stations[^1].Id).Id);
    }

    [Fact]
    public void Previous_goes_back_by_one_and_wraps_to_the_end()
    {
        var stations = RadioStationCatalog.Stations;
        for (var i = 1; i < stations.Count; i++)
            Assert.Equal(stations[i - 1].Id, RadioStationCatalog.Previous(stations[i].Id).Id);

        Assert.Equal(stations[^1].Id, RadioStationCatalog.Previous(stations[0].Id).Id);
    }

    [Fact]
    public void Find_returns_null_for_unknown_or_missing_id()
    {
        Assert.Null(RadioStationCatalog.Find(null));
        Assert.Null(RadioStationCatalog.Find("not-a-real-station"));
    }
}

public class RadioPlayerServiceTests
{
    [Fact]
    public void Constructing_without_settings_never_throws_and_starts_stopped()
    {
        using var radio = new RadioPlayerService();
        var snapshot = radio.Snapshot;

        Assert.False(snapshot.Enabled);
        Assert.Null(snapshot.Station);
        Assert.Equal(RadioPlaybackStatus.Stopped, snapshot.Status);
    }

    [Fact]
    public void Constructing_with_settings_seeds_the_last_saved_state()
    {
        var settings = new AppSettings
        {
            Radio = new RadioSettings
            {
                RadioEnabled = true,
                RadioLastStation = RadioStationCatalog.Stations[1].Id,
                RadioVolume = 77,
                RadioMute = true,
            },
        };

        using var radio = new RadioPlayerService(settings);
        var snapshot = radio.Snapshot;

        Assert.True(snapshot.Enabled);
        Assert.Equal(RadioStationCatalog.Stations[1].Id, snapshot.Station?.Id);
        Assert.Equal(77, snapshot.Volume);
        Assert.True(snapshot.Muted);
    }

    [Fact]
    public async Task SetVolumeAsync_clamps_to_0_100_and_persists()
    {
        var settings = new AppSettings();
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), $"ednexus-radio-test-{Guid.NewGuid():N}.json"));
        using var radio = new RadioPlayerService(settings, store);

        await radio.SetVolumeAsync(500);
        Assert.Equal(100, radio.Snapshot.Volume);
        Assert.Equal(100, settings.Radio.RadioVolume);

        await radio.SetVolumeAsync(-20);
        Assert.Equal(0, radio.Snapshot.Volume);
        Assert.Equal(0, settings.Radio.RadioVolume);
    }

    [Fact]
    public async Task SetMuteAsync_updates_snapshot_and_persists()
    {
        var settings = new AppSettings();
        using var radio = new RadioPlayerService(settings);

        await radio.SetMuteAsync(true);
        Assert.True(radio.Snapshot.Muted);
        Assert.True(settings.Radio.RadioMute);
    }

    [Fact]
    public async Task SetEnabledAsync_false_stops_playback_and_persists()
    {
        var settings = new AppSettings();
        using var radio = new RadioPlayerService(settings);

        await radio.SetEnabledAsync(true);
        Assert.True(settings.Radio.RadioEnabled);

        await radio.SetEnabledAsync(false);
        Assert.False(settings.Radio.RadioEnabled);
        Assert.Equal(RadioPlaybackStatus.Stopped, radio.Snapshot.Status);
    }

    [Fact]
    public async Task RestoreAsync_is_a_no_op_when_nothing_was_previously_enabled()
    {
        using var radio = new RadioPlayerService();
        await radio.RestoreAsync();

        Assert.Equal(RadioPlaybackStatus.Stopped, radio.Snapshot.Status);
    }
}
