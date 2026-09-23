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
    public async Task Volume_changes_apply_at_once_but_the_disk_write_waits_and_is_flushed_on_dispose()
    {
        using var temp = new TempRadioSettings(wasPlaying: false);
        var radio = new RadioPlayerService(temp.Settings, temp.Store, saveDelay: TimeSpan.FromHours(1));

        // A slider drag: many steps in quick succession.
        for (var v = 10; v <= 70; v += 10) await radio.SetVolumeAsync(v);

        Assert.Equal(70, radio.Snapshot.Volume);            // the player has it now
        Assert.Equal(70, temp.Settings.Radio.RadioVolume);  // and so do the in-memory settings
        Assert.Equal(50, temp.Reload().RadioVolume);        // but it hasn't hit the disk yet

        radio.Dispose();                                    // app shutdown

        Assert.Equal(70, temp.Reload().RadioVolume);
    }

    [Fact]
    public async Task A_debounced_volume_write_lands_once_the_changes_go_quiet()
    {
        using var temp = new TempRadioSettings(wasPlaying: false);
        using var radio = new RadioPlayerService(temp.Settings, temp.Store, saveDelay: TimeSpan.FromMilliseconds(50));

        await radio.SetVolumeAsync(33);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (temp.Reload().RadioVolume != 33 && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.Equal(33, temp.Reload().RadioVolume);
    }

    [Fact]
    public async Task The_delayed_volume_save_runs_on_the_settings_owner_not_the_timer_thread()
    {
        using var temp = new TempRadioSettings(wasPlaying: false);
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var radio = new RadioPlayerService(temp.Settings, temp.Store,
            saveDelay: TimeSpan.FromMilliseconds(10), postSave: posted.Enqueue);

        await radio.SetVolumeAsync(64);

        // The timer fires, but only hands the save to the owner; nothing is written from its thread.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (posted.IsEmpty && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(posted.TryDequeue(out var save));
        Assert.Equal(50, temp.Reload().RadioVolume);

        save!();   // the owner (the UI thread, in the app) runs it
        Assert.Equal(64, temp.Reload().RadioVolume);
    }

    [Fact]
    public async Task A_failed_delayed_save_is_retried_rather_than_dropped()
    {
        // Point the store at a path that is currently a directory, so every write fails.
        var path = Path.Combine(Path.GetTempPath(), $"ednexus-radio-test-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(path);
        try
        {
            var store = new SettingsStore(path);
            var settings = new AppSettings();
            var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
            using var radio = new RadioPlayerService(settings, store,
                saveDelay: TimeSpan.FromMilliseconds(10), postSave: posted.Enqueue);

            await radio.SetVolumeAsync(42);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (posted.IsEmpty && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.True(posted.TryDequeue(out var firstTry));
            firstTry!();   // fails: the path is a directory
            Assert.False(File.Exists(path));

            // The write became possible again; the save must still be pending and re-armed.
            Directory.Delete(path);
            deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(path) && DateTime.UtcNow < deadline)
            {
                if (posted.TryDequeue(out var retry)) retry();
                else await Task.Delay(10);
            }

            Assert.Equal(42, new SettingsStore(path).Load().Radio.RadioVolume);
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Concurrent_saves_to_one_store_are_serialized()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ednexus-settings-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            var settings = new AppSettings();
            var failures = 0;

            Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
            {
                if (!store.TrySave(settings)) Interlocked.Increment(ref failures);
            });

            Assert.Equal(0, failures);
            Assert.NotNull(new SettingsStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task An_immediate_save_also_covers_a_pending_volume_change()
    {
        using var temp = new TempRadioSettings(wasPlaying: false);
        using var radio = new RadioPlayerService(temp.Settings, temp.Store, saveDelay: TimeSpan.FromHours(1));

        await radio.SetVolumeAsync(25);
        await radio.SetMuteAsync(true);   // saved immediately, carrying the volume with it

        var saved = temp.Reload();
        Assert.Equal(25, saved.RadioVolume);
        Assert.True(saved.RadioMute);
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

    // --- #140: the radio must not start on launch unless it was playing when the app closed. ---

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]  // was paused/stopped at shutdown (the #140 case)
    [InlineData(false, true, true, false)]  // radio feature off
    [InlineData(true, true, false, false)]  // no station tuned
    public void ShouldResumeOnLaunch_requires_enabled_playing_and_a_station(
        bool enabled, bool wasPlaying, bool hasStation, bool expected)
    {
        var radio = new RadioSettings
        {
            RadioEnabled = enabled,
            RadioWasPlaying = wasPlaying,
            RadioLastStation = hasStation ? RadioStationCatalog.Stations[0].Id : null,
        };

        Assert.Equal(expected, RadioPlayerService.ShouldResumeOnLaunch(radio));
    }

    [Fact]
    public void ShouldResumeOnLaunch_is_false_for_missing_settings_or_retired_station()
    {
        Assert.False(RadioPlayerService.ShouldResumeOnLaunch(null));
        Assert.False(RadioPlayerService.ShouldResumeOnLaunch(new RadioSettings()));
        Assert.False(RadioPlayerService.ShouldResumeOnLaunch(new RadioSettings
        {
            RadioEnabled = true,
            RadioWasPlaying = true,
            RadioLastStation = "retired-station",
        }));
    }

    [Fact]
    public async Task RestoreAsync_stays_silent_when_radio_was_enabled_but_not_playing()
    {
        // Settings as left by a session where the user played a station and then paused it —
        // or by a pre-#140 build, which never wrote RadioWasPlaying.
        var settings = new AppSettings
        {
            Radio = new RadioSettings
            {
                RadioEnabled = true,
                RadioLastStation = RadioStationCatalog.Stations[0].Id,
            },
        };
        using var radio = new RadioPlayerService(settings);

        await radio.RestoreAsync();

        // Had it tried to play, the status would have moved to Buffering/Playing (or Error if no libvlc).
        Assert.Equal(RadioPlaybackStatus.Stopped, radio.Snapshot.Status);
        Assert.Null(radio.Snapshot.LastError);
    }

    [Fact]
    public void Legacy_settings_without_RadioWasPlaying_load_as_not_playing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ednexus-radio-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path,
                """{ "Radio": { "RadioEnabled": true, "RadioLastStation": "radio-sidewinder", "RadioVolume": 40 } }""");

            var settings = new SettingsStore(path).Load();

            Assert.True(settings.Radio.RadioEnabled);
            Assert.False(settings.Radio.RadioWasPlaying);
            Assert.False(RadioPlayerService.ShouldResumeOnLaunch(settings.Radio));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A throwaway settings file, seeded as a session that was playing when it closed.</summary>
    private sealed class TempRadioSettings : IDisposable
    {
        public TempRadioSettings(bool wasPlaying = true)
        {
            Store = new SettingsStore(Path.Combine(Path.GetTempPath(), $"ednexus-radio-test-{Guid.NewGuid():N}.json"));
            Settings = new AppSettings
            {
                Radio = new RadioSettings
                {
                    RadioEnabled = true,
                    RadioWasPlaying = wasPlaying,
                    RadioLastStation = RadioStationCatalog.Stations[0].Id,
                },
            };
            Store.Save(Settings);
        }

        public SettingsStore Store { get; }
        public AppSettings Settings { get; }

        /// <summary>What the next launch would read back off disk.</summary>
        public RadioSettings Reload() => new SettingsStore(Store.Path).Load().Radio;

        public void Dispose() => File.Delete(Store.Path);
    }

    [Fact]
    public async Task PauseAsync_clears_and_persists_the_resume_flag()
    {
        using var temp = new TempRadioSettings();
        using var radio = new RadioPlayerService(temp.Settings, temp.Store);

        await radio.PauseAsync();

        var saved = temp.Reload();
        Assert.False(saved.RadioWasPlaying);
        Assert.True(saved.RadioEnabled); // pausing doesn't turn the feature off
        Assert.False(RadioPlayerService.ShouldResumeOnLaunch(saved));
    }

    [Fact]
    public async Task StopAsync_clears_and_persists_the_resume_flag()
    {
        using var temp = new TempRadioSettings();
        using var radio = new RadioPlayerService(temp.Settings, temp.Store);

        await radio.StopAsync();

        Assert.False(temp.Reload().RadioWasPlaying);
    }

    [Fact]
    public async Task SetEnabledAsync_false_clears_and_persists_the_resume_flag()
    {
        using var temp = new TempRadioSettings();
        using var radio = new RadioPlayerService(temp.Settings, temp.Store);

        await radio.SetEnabledAsync(false);

        var saved = temp.Reload();
        Assert.False(saved.RadioEnabled);
        Assert.False(saved.RadioWasPlaying);
    }

    // Without a native libvlc in the test run these playback calls end in Error — that's fine, they
    // still record the user's intent to listen, which is what's being checked.
    public static TheoryData<string> PlayActions => new() { "play-id", "play-current", "next", "previous", "toggle" };

    [Theory]
    [MemberData(nameof(PlayActions))]
    public async Task Play_actions_set_and_persist_the_resume_flag(string action)
    {
        using var temp = new TempRadioSettings(wasPlaying: false);
        using var radio = new RadioPlayerService(temp.Settings, temp.Store);
        Assert.Equal(RadioPlaybackStatus.Stopped, radio.Snapshot.Status); // so "toggle" resumes

        await (action switch
        {
            "play-id" => radio.PlayAsync(RadioStationCatalog.Stations[1].Id),
            "play-current" => radio.PlayAsync(),
            "next" => radio.NextStationAsync(),
            "previous" => radio.PreviousStationAsync(),
            "toggle" => radio.TogglePlayPauseAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        });

        Assert.True(temp.Reload().RadioWasPlaying);
    }

    [Theory]
    [InlineData(RadioPlaybackStatus.Playing, RadioToggleAction.Pause)]
    [InlineData(RadioPlaybackStatus.Buffering, RadioToggleAction.Stop)]
    [InlineData(RadioPlaybackStatus.Error, RadioToggleAction.Stop)]
    [InlineData(RadioPlaybackStatus.Paused, RadioToggleAction.Play)]
    [InlineData(RadioPlaybackStatus.Stopped, RadioToggleAction.Play)]
    public void ToggleActionFor_maps_each_status(RadioPlaybackStatus status, RadioToggleAction expected)
        => Assert.Equal(expected, RadioPlayerService.ToggleActionFor(status));

    [Fact]
    public async Task Toggle_on_a_failed_stream_stops_and_clears_the_resume_flag()
    {
        // Review follow-up to #140: a stream that errors must not stay flagged to autoplay, and
        // pressing play/pause on it must cancel it rather than retrying.
        using var temp = new TempRadioSettings(wasPlaying: false);
        using var radio = new RadioPlayerService(temp.Settings, temp.Store);

        await radio.PlayAsync(RadioStationCatalog.Stations[0].Id);
        Assert.Equal(RadioPlaybackStatus.Error, radio.Snapshot.Status); // no native libvlc under test
        Assert.True(temp.Reload().RadioWasPlaying);

        await radio.TogglePlayPauseAsync();

        Assert.Equal(RadioPlaybackStatus.Stopped, radio.Snapshot.Status);
        Assert.False(temp.Reload().RadioWasPlaying);
    }

    [Fact]
    public void Dispose_at_shutdown_keeps_the_resume_flag()
    {
        // Closing the app while the radio plays is exactly the case that *should* resume next launch.
        var settings = new AppSettings
        {
            Radio = new RadioSettings
            {
                RadioEnabled = true,
                RadioWasPlaying = true,
                RadioLastStation = RadioStationCatalog.Stations[0].Id,
            },
        };
        var radio = new RadioPlayerService(settings);

        radio.Dispose();

        Assert.True(settings.Radio.RadioWasPlaying);
        Assert.True(RadioPlayerService.ShouldResumeOnLaunch(settings.Radio));
    }
}
