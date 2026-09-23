using EDNexus.Core.Dev;
using EDNexus.Core.Journal;
using EDNexus.Core.Radio;
using EDNexus.Core.Settings;
using Xunit;

namespace EDNexus.Tests.Dev;

public class RadioSimulationTests
{
    private static Random Seeded() => new(20260923);

    private static (JournalEventBus Bus, SimulatedRadioPlayer Sim) Wired()
    {
        var bus = new JournalEventBus();
        var sim = new SimulatedRadioPlayer();
        sim.Attach(bus);
        return (bus, sim);
    }

    private static void Publish(JournalEventBus bus, string line)
    {
        Assert.True(JournalEntry.TryParse(line, historical: false, out var entry));
        bus.Publish(entry);
    }

    [Fact]
    public void Radio_sample_source_is_registered_in_developer_mode()
        => Assert.Contains(new DeveloperMode().Sources, s => s is RadioSampleSource && s.CardKey == "radio");

    [Fact]
    public void Successive_dice_rolls_cycle_the_card_through_every_state()
    {
        var (bus, sim) = Wired();
        var dev = new DeveloperMode();
        var rng = Seeded();

        var seen = new List<RadioPlaybackStatus>();
        for (var i = 0; i < RadioSampleSource.Cycle.Count; i++)
        {
            dev.Randomize(bus, rng, cardKey: "radio");
            var snap = sim.Snapshot;
            seen.Add(snap.Status);

            Assert.True(snap.Enabled);
            Assert.NotNull(snap.Station);
            Assert.InRange(snap.Volume, 0, 100);
            Assert.Equal(snap.Status == RadioPlaybackStatus.Error, !string.IsNullOrEmpty(snap.LastError));
        }

        Assert.Equal(RadioSampleSource.Cycle, seen);
        foreach (var status in Enum.GetValues<RadioPlaybackStatus>())
            Assert.Contains(status, seen);

        // And it wraps around for the next lap.
        dev.Randomize(bus, rng, cardKey: "radio");
        Assert.Equal(RadioSampleSource.Cycle[0], sim.Snapshot.Status);
    }

    [Fact]
    public void Randomize_all_reaches_the_simulated_player()
    {
        var (bus, sim) = Wired();
        new DeveloperMode().Randomize(bus, Seeded());
        Assert.Equal(RadioSampleSource.Cycle[0], sim.Snapshot.Status);
    }

    [Fact]
    public void Malformed_or_unknown_samples_are_ignored_without_throwing()
    {
        var (bus, sim) = Wired();
        var errors = 0;
        bus.HandlerError += (_, _) => errors++;

        Publish(bus, """{"timestamp":"2026-09-23T12:00:00Z","event":"EDNexusRadioSample"}""");
        Publish(bus, """{"timestamp":"2026-09-23T12:00:00Z","event":"EDNexusRadioSample","Status":"Exploding"}""");
        Publish(bus, """{"timestamp":"2026-09-23T12:00:00Z","event":"EDNexusRadioSample","Status":"42"}""");
        Publish(bus, """{"timestamp":"2026-09-23T12:00:00Z","event":"EDNexusRadioSample","Status":7}""");

        Assert.Equal(0, errors);
        Assert.Equal(RadioPlaybackStatus.Stopped, sim.Snapshot.Status);
        Assert.False(sim.Snapshot.Enabled);

        // A valid status with junk in every other field applies the status and keeps the rest.
        Publish(bus, """{"timestamp":"2026-09-23T12:00:00Z","event":"EDNexusRadioSample","Status":"playing","Station":"nope","Volume":"loud","Muted":"yes"}""");
        Assert.Equal(0, errors);
        var snap = sim.Snapshot;
        Assert.Equal(RadioPlaybackStatus.Playing, snap.Status);
        Assert.Null(snap.Station);
        Assert.Equal(50, snap.Volume);
        Assert.False(snap.Muted);
    }

    [Fact]
    public void Sample_volume_is_clamped()
    {
        var (bus, sim) = Wired();
        Publish(bus, """{"timestamp":"2026-09-23T12:00:00Z","event":"EDNexusRadioSample","Status":"Playing","Volume":900}""");
        Assert.Equal(100, sim.Snapshot.Volume);
        Publish(bus, """{"timestamp":"2026-09-23T12:00:00Z","event":"EDNexusRadioSample","Status":"Playing","Volume":-5}""");
        Assert.Equal(0, sim.Snapshot.Volume);
    }

    [Fact]
    public async Task Toggle_follows_the_same_transitions_as_the_real_player()
    {
        var sim = new SimulatedRadioPlayer();

        // Stopped with nothing tuned → plays the first station.
        await sim.TogglePlayPauseAsync();
        Assert.Equal(RadioPlaybackStatus.Playing, sim.Snapshot.Status);
        Assert.Equal(RadioStationCatalog.Stations[0].Id, sim.Snapshot.Station?.Id);

        // Playing → pause; paused → resume the same station.
        await sim.TogglePlayPauseAsync();
        Assert.Equal(RadioPlaybackStatus.Paused, sim.Snapshot.Status);
        await sim.TogglePlayPauseAsync();
        Assert.Equal(RadioPlaybackStatus.Playing, sim.Snapshot.Status);
        Assert.Equal(RadioStationCatalog.Stations[0].Id, sim.Snapshot.Station?.Id);
    }

    [Theory]
    [InlineData("Buffering")]
    [InlineData("Error")]
    public async Task Toggle_from_buffering_or_error_stops(string status)
    {
        var (bus, sim) = Wired();
        Publish(bus, $$"""{"timestamp":"2026-09-23T12:00:00Z","event":"EDNexusRadioSample","Status":"{{status}}","Error":"x"}""");

        await sim.TogglePlayPauseAsync();

        Assert.Equal(RadioPlaybackStatus.Stopped, sim.Snapshot.Status);
        Assert.Null(sim.Snapshot.LastError);
    }

    [Fact]
    public async Task Station_volume_and_mute_controls_update_the_simulated_state()
    {
        var sim = new SimulatedRadioPlayer();
        var changes = 0;
        sim.Changed += () => changes++;

        var target = RadioStationCatalog.Stations[2];
        await sim.PlayAsync(target.Id);
        Assert.Equal(target, sim.Snapshot.Station);
        Assert.Equal(RadioPlaybackStatus.Playing, sim.Snapshot.Status);

        await sim.PlayAsync("not-a-station");   // ignored
        Assert.Equal(target, sim.Snapshot.Station);

        await sim.NextStationAsync();
        Assert.Equal(RadioStationCatalog.Next(target.Id), sim.Snapshot.Station);
        await sim.PreviousStationAsync();
        Assert.Equal(target, sim.Snapshot.Station);

        await sim.SetVolumeAsync(250);
        Assert.Equal(100, sim.Snapshot.Volume);
        await sim.SetMuteAsync(true);
        Assert.True(sim.Snapshot.Muted);

        Assert.True(changes >= 5);
    }

    [Fact]
    public async Task Simulation_never_touches_the_real_player_or_its_saved_resume_intent()
    {
        var settings = new AppSettings();
        settings.Radio.RadioEnabled = true;
        settings.Radio.RadioWasPlaying = true;
        settings.Radio.RadioLastStation = RadioStationCatalog.Stations[1].Id;
        settings.Radio.RadioVolume = 40;
        using var real = new RadioPlayerService(settings);

        var (bus, sim) = Wired();
        var dev = new DeveloperMode();
        var rng = Seeded();
        for (var i = 0; i < RadioSampleSource.Cycle.Count * 2; i++)
        {
            dev.Randomize(bus, rng, cardKey: "radio");
            await sim.TogglePlayPauseAsync();
            await sim.SetVolumeAsync(rng.Next(0, 101));
            await sim.SetMuteAsync(rng.Next(2) == 0);
        }

        Assert.True(settings.Radio.RadioWasPlaying);
        Assert.True(settings.Radio.RadioEnabled);
        Assert.Equal(RadioStationCatalog.Stations[1].Id, settings.Radio.RadioLastStation);
        Assert.Equal(40, settings.Radio.RadioVolume);
        Assert.Equal(RadioPlaybackStatus.Stopped, real.Snapshot.Status);   // nothing was started
    }
}
