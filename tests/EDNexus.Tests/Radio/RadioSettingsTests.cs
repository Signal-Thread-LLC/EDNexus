using EDNexus.Core.Radio;
using EDNexus.Core.Settings;
using Xunit;

namespace EDNexus.Tests.Radio;

public class RadioSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ednexus-radio-settings-").FullName;
    private string Path => System.IO.Path.Combine(_root, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void A_fresh_settings_file_defaults_radio_to_off_at_half_volume_unmuted_with_no_station()
    {
        var settings = new SettingsStore(Path).Load();

        Assert.False(settings.Radio.Enabled);
        Assert.Null(settings.Radio.LastStationId);
        Assert.Equal(50, settings.Radio.Volume);
        Assert.False(settings.Radio.Mute);
    }

    [Fact]
    public void Playing_a_station_persists_it_as_the_last_selected_station_and_marks_radio_enabled()
    {
        var store = new SettingsStore(Path);
        var settings = store.Load();
        var service = new RadioPlayerService(new FakeRadioAudioBackend(), settings, store);

        service.Play("somafm-mission-control");

        var reloaded = new SettingsStore(Path).Load();
        Assert.True(reloaded.Radio.Enabled);
        Assert.Equal("somafm-mission-control", reloaded.Radio.LastStationId);
    }

    [Fact]
    public void Volume_and_mute_changes_round_trip_through_disk()
    {
        var store = new SettingsStore(Path);
        var settings = store.Load();
        var service = new RadioPlayerService(new FakeRadioAudioBackend(), settings, store);

        service.SetVolume(72);
        service.SetMute(true);

        var reloaded = new SettingsStore(Path).Load();
        Assert.Equal(72, reloaded.Radio.Volume);
        Assert.True(reloaded.Radio.Mute);
    }

    [Fact]
    public void A_new_service_restores_the_last_station_volume_and_mute_from_saved_settings()
    {
        var store = new SettingsStore(Path);
        var settings = store.Load();
        settings.Radio.Enabled = true;
        settings.Radio.LastStationId = "hutton-orbital-radio";
        settings.Radio.Volume = 33;
        settings.Radio.Mute = true;
        store.Save(settings);

        var reloadedSettings = new SettingsStore(Path).Load();
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend, reloadedSettings, store);

        var snap = service.Snapshot;
        Assert.True(snap.Enabled);
        Assert.Equal("hutton-orbital-radio", snap.Station?.Id);
        Assert.Equal(33, snap.Volume);
        Assert.True(snap.Mute);
        // Restored volume/mute are pushed into the backend at construction, not just kept in memory.
        Assert.Equal(33, backend.LastVolume);
        Assert.Equal(true, backend.LastMute);
    }

    [Fact]
    public void Disabling_the_radio_persists_enabled_as_false_while_keeping_the_last_station()
    {
        var store = new SettingsStore(Path);
        var settings = store.Load();
        var service = new RadioPlayerService(new FakeRadioAudioBackend(), settings, store);
        service.Play("simulator-radio");

        service.SetEnabled(false);

        var reloaded = new SettingsStore(Path).Load();
        Assert.False(reloaded.Radio.Enabled);
        Assert.Equal("simulator-radio", reloaded.Radio.LastStationId);
    }
}
