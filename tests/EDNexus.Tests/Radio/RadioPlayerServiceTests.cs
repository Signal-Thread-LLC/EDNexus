using EDNexus.Core.Radio;
using Xunit;

namespace EDNexus.Tests.Radio;

public class RadioPlayerServiceTests
{
    [Fact]
    public void A_fresh_service_starts_stopped_with_default_volume_and_no_station()
    {
        var service = new RadioPlayerService(new FakeRadioAudioBackend());

        var snap = service.Snapshot;
        Assert.Null(snap.Station);
        Assert.Equal(RadioPlaybackState.Stopped, snap.State);
        Assert.Equal(50, snap.Volume);
        Assert.False(snap.Mute);
        Assert.False(snap.Enabled);
    }

    [Fact]
    public void Play_tunes_the_station_marks_it_enabled_and_forwards_the_stream_url_to_the_backend()
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);

        service.Play("hutton-orbital-radio");

        Assert.Equal("https://quincy.torontocast.com/hutton", backend.LastPlayedUrl);
        Assert.Equal("Hutton Orbital Radio", service.Snapshot.Station?.Name);
        Assert.True(service.Snapshot.Enabled);
        Assert.Equal(RadioPlaybackState.Buffering, service.Snapshot.State);   // set optimistically before the backend reports back
    }

    [Fact]
    public void Play_with_an_unknown_station_id_is_ignored()
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);
        backend.Calls.Clear();   // construction pushes the restored volume/mute onto the backend

        service.Play("does-not-exist");

        Assert.Empty(backend.Calls);
        Assert.Null(service.Snapshot.Station);
    }

    [Fact]
    public void The_backends_reported_state_flows_through_to_the_snapshot()
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);
        service.Play("radio-sidewinder");

        backend.RaiseState(RadioPlaybackState.Playing);
        Assert.Equal(RadioPlaybackState.Playing, service.Snapshot.State);

        backend.RaiseState(RadioPlaybackState.Error, "connection refused");
        Assert.Equal(RadioPlaybackState.Error, service.Snapshot.State);
        Assert.Equal("connection refused", service.Snapshot.LastError);

        // A subsequent non-error state clears the stale error message.
        backend.RaiseState(RadioPlaybackState.Playing);
        Assert.Null(service.Snapshot.LastError);
    }

    [Fact]
    public void Pause_then_resume_round_trips_through_the_backend()
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);
        service.Play("radio-sidewinder");
        backend.RaiseState(RadioPlaybackState.Playing);

        service.Pause();
        Assert.Contains(nameof(IRadioAudioBackend.Pause), backend.Calls);
        backend.RaiseState(RadioPlaybackState.Paused);
        Assert.Equal(RadioPlaybackState.Paused, service.Snapshot.State);

        service.Resume();
        Assert.Contains(nameof(IRadioAudioBackend.Resume), backend.Calls);
        backend.RaiseState(RadioPlaybackState.Playing);
        Assert.Equal(RadioPlaybackState.Playing, service.Snapshot.State);
    }

    [Fact]
    public void Pause_and_resume_are_no_ops_before_anything_has_ever_played()
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);
        backend.Calls.Clear();   // construction pushes the restored volume/mute onto the backend

        service.Pause();
        service.Resume();

        Assert.Empty(backend.Calls);
    }

    [Fact]
    public void Stop_always_reaches_the_backend_and_the_reported_state_lands_in_the_snapshot()
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);
        service.Play("radio-sidewinder");
        backend.RaiseState(RadioPlaybackState.Playing);

        service.Stop();

        Assert.Contains(nameof(IRadioAudioBackend.Stop), backend.Calls);
        backend.RaiseState(RadioPlaybackState.Stopped);
        Assert.Equal(RadioPlaybackState.Stopped, service.Snapshot.State);
    }

    [Fact]
    public void Changing_station_while_playing_swaps_the_stream_without_requiring_an_explicit_stop()
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);
        service.Play("radio-sidewinder");
        backend.RaiseState(RadioPlaybackState.Playing);

        service.Play("simulator-radio");

        Assert.Equal("https://simulatorradio.stream/stream.mp3", backend.LastPlayedUrl);
        Assert.Equal("Simulator Radio", service.Snapshot.Station?.Name);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(75, 75)]
    [InlineData(100, 100)]
    [InlineData(500, 100)]
    public void SetVolume_clamps_to_0_100_and_forwards_the_clamped_value(int requested, int expected)
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);

        service.SetVolume(requested);

        Assert.Equal(expected, service.Snapshot.Volume);
        Assert.Equal(expected, backend.LastVolume);
    }

    [Fact]
    public void SetMute_forwards_to_the_backend_and_updates_the_snapshot()
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);

        service.SetMute(true);

        Assert.True(service.Snapshot.Mute);
        Assert.Equal(true, backend.LastMute);

        service.SetMute(false);
        Assert.False(service.Snapshot.Mute);
        Assert.Equal(false, backend.LastMute);
    }

    [Fact]
    public void SetEnabled_false_stops_playback_and_leaves_the_last_station_remembered()
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);
        service.Play("radio-sidewinder");

        service.SetEnabled(false);

        Assert.Contains(nameof(IRadioAudioBackend.Stop), backend.Calls);
        Assert.False(service.Snapshot.Enabled);
        Assert.Equal("radio-sidewinder", service.Snapshot.Station?.Id);   // remembered, not cleared
    }

    [Fact]
    public void Changed_fires_on_every_command_and_on_backend_reported_state_changes()
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);
        var raises = 0;
        service.Changed += () => raises++;

        service.Play("radio-sidewinder");
        Assert.Equal(1, raises);

        backend.RaiseState(RadioPlaybackState.Playing);
        Assert.Equal(2, raises);

        service.SetVolume(10);
        Assert.Equal(3, raises);

        service.SetMute(true);
        Assert.Equal(4, raises);
    }

    [Fact]
    public void Dispose_unhooks_from_and_disposes_the_backend()
    {
        var backend = new FakeRadioAudioBackend();
        var service = new RadioPlayerService(backend);

        service.Dispose();

        Assert.True(backend.Disposed);
        // A state raised after dispose must not reach a service that already unsubscribed.
        backend.RaiseState(RadioPlaybackState.Playing);
    }
}
