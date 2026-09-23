using EDNexus.Core.Dev;
using EDNexus.Core.Journal;
using EDNexus.Core.Radio;
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
        var sim = new SimulatedRadioPlayer(connectDelay: TimeSpan.Zero);

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
        var sim = new SimulatedRadioPlayer(connectDelay: TimeSpan.Zero);
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
    public async Task Playing_a_station_buffers_before_it_goes_live()
    {
        var sim = new SimulatedRadioPlayer(connectDelay: TimeSpan.FromMilliseconds(50));
        var seen = new List<RadioPlaybackStatus>();
        var live = new TaskCompletionSource();
        sim.Changed += () =>
        {
            var status = sim.Snapshot.Status;
            lock (seen) seen.Add(status);
            if (status == RadioPlaybackStatus.Playing) live.TrySetResult();
        };

        await sim.PlayAsync(RadioStationCatalog.Stations[0].Id);
        Assert.Equal(RadioPlaybackStatus.Buffering, sim.Snapshot.Status);   // returns while still connecting

        await live.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (seen) Assert.Equal(new[] { RadioPlaybackStatus.Buffering, RadioPlaybackStatus.Playing }, seen);
    }

    [Fact]
    public async Task Stopping_while_buffering_is_not_overridden_by_the_late_connect()
    {
        var sim = new SimulatedRadioPlayer(connectDelay: TimeSpan.FromMilliseconds(50));

        await sim.PlayAsync(RadioStationCatalog.Stations[0].Id);
        await sim.TogglePlayPauseAsync();   // Buffering → Stop
        await Task.Delay(200);

        Assert.Equal(RadioPlaybackStatus.Stopped, sim.Snapshot.Status);
    }
}

/// <summary>An <see cref="IRadioPlayer"/> that records every call, standing in for the real LibVLC player.</summary>
internal sealed class RecordingRadioPlayer : IRadioPlayer
{
    public List<string> Calls { get; } = new();
    public event Action? Changed;
    public RadioPlayerSnapshot Snapshot { get; } = new(true, RadioStationCatalog.Stations[0], RadioPlaybackStatus.Stopped, 50, false, null);

    public void RaiseChanged() => Changed?.Invoke();

    private Task Record(string call) { Calls.Add(call); return Task.CompletedTask; }
    public Task TogglePlayPauseAsync(CancellationToken ct = default) => Record("toggle");
    public Task PlayAsync(string stationId, CancellationToken ct = default) => Record($"play:{stationId}");
    public Task NextStationAsync(CancellationToken ct = default) => Record("next");
    public Task PreviousStationAsync(CancellationToken ct = default) => Record("previous");
    public Task SetVolumeAsync(int volume, CancellationToken ct = default) => Record($"volume:{volume}");
    public Task SetMuteAsync(bool muted, CancellationToken ct = default) => Record($"mute:{muted}");
}

public class RadioPlayerSelectorTests
{
    [Fact]
    public void Outside_developer_mode_the_real_player_is_active()
    {
        var real = new RecordingRadioPlayer();
        var selector = new RadioPlayerSelector(real, devEnabled: () => false, devToolsAvailable: true);
        selector.AttachSimulation(new JournalEventBus());

        Assert.Same(real, selector.Active);
        Assert.False(selector.IsSimulated);
    }

    [Fact]
    public async Task In_developer_mode_every_control_goes_to_the_simulation_and_never_the_real_player()
    {
        var real = new RecordingRadioPlayer();
        var dev = true;
        var selector = new RadioPlayerSelector(real, () => dev, devToolsAvailable: true);
        var bus = new JournalEventBus();
        selector.AttachSimulation(bus);

        Assert.True(selector.IsSimulated);
        Assert.Same(selector.Simulated, selector.Active);

        // Everything the card and title bar can do, plus 🎲 rolls through the real bus.
        var devMode = new DeveloperMode();
        var rng = new Random(48);
        for (var i = 0; i < RadioSampleSource.Cycle.Count; i++)
        {
            devMode.Randomize(bus, rng, cardKey: "radio");
            await selector.Active.TogglePlayPauseAsync();
            await selector.Active.PlayAsync(RadioStationCatalog.Stations[1].Id);
            await selector.Active.NextStationAsync();
            await selector.Active.PreviousStationAsync();
            await selector.Active.SetVolumeAsync(rng.Next(0, 101));
            await selector.Active.SetMuteAsync(rng.Next(2) == 0);
        }

        Assert.Empty(real.Calls);

        // Leaving developer mode hands control straight back to the real player.
        dev = false;
        await selector.Active.TogglePlayPauseAsync();
        Assert.Equal(new[] { "toggle" }, real.Calls);
    }

    [Fact]
    public void Without_dev_tools_there_is_no_simulation_even_if_dev_mode_is_on()
    {
        var real = new RecordingRadioPlayer();
        var selector = new RadioPlayerSelector(real, devEnabled: () => true, devToolsAvailable: false);
        selector.AttachSimulation(new JournalEventBus());

        Assert.Null(selector.Simulated);
        Assert.False(selector.IsSimulated);
        Assert.Same(real, selector.Active);
    }

    [Fact]
    public void Reattaching_to_a_rebuilt_bus_starts_a_fresh_simulation_that_hears_the_new_bus_only()
    {
        var real = new RecordingRadioPlayer();
        var selector = new RadioPlayerSelector(real, devEnabled: () => true, devToolsAvailable: true);
        var oldBus = new JournalEventBus();
        selector.AttachSimulation(oldBus);
        new DeveloperMode().Randomize(oldBus, new Random(1), cardKey: "radio");
        Assert.True(selector.Simulated!.Snapshot.Enabled);

        var newBus = new JournalEventBus();
        selector.AttachSimulation(newBus);
        Assert.False(selector.Simulated!.Snapshot.Enabled);   // fabricated state discarded

        var changes = 0;
        selector.Changed += () => changes++;
        new DeveloperMode().Randomize(oldBus, new Random(2), cardKey: "radio");
        Assert.Equal(0, changes);                               // the stale sim no longer reaches the UI
        new DeveloperMode().Randomize(newBus, new Random(3), cardKey: "radio");
        Assert.Equal(1, changes);
        Assert.True(selector.Simulated!.Snapshot.Enabled);
    }

    [Fact]
    public void Changed_forwards_the_real_player_too()
    {
        var real = new RecordingRadioPlayer();
        var selector = new RadioPlayerSelector(real, devEnabled: () => false, devToolsAvailable: true);
        var changes = 0;
        selector.Changed += () => changes++;

        real.RaiseChanged();

        Assert.Equal(1, changes);
    }
}
