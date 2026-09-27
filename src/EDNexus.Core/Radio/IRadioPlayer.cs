namespace EDNexus.Core.Radio;

/// <summary>
/// The transport surface the radio UI drives: a state snapshot plus the play/pause, station, volume,
/// and mute operations. <see cref="RadioPlayerService"/> is the real (LibVLC-backed, persisted)
/// implementation; developer mode swaps in <see cref="Dev.SimulatedRadioPlayer"/>, which never opens
/// an audio device or the network and never writes settings.
/// </summary>
public interface IRadioPlayer
{
    /// <summary>Raised after any playback state, station, volume, or mute change.</summary>
    event Action? Changed;

    /// <summary>A point-in-time snapshot of everything the UI needs to render the player.</summary>
    RadioPlayerSnapshot Snapshot { get; }

    /// <summary>
    /// The single play/pause control: what it does from the current status is defined by
    /// <see cref="RadioPlayerService.ToggleActionFor"/>.
    /// </summary>
    Task TogglePlayPauseAsync(CancellationToken ct = default);

    /// <summary>Tunes to and plays the station with the given <see cref="RadioStation.Id"/>; unknown ids are ignored.</summary>
    Task PlayAsync(string stationId, CancellationToken ct = default);

    /// <summary>Advances to and plays the next catalog station (wrapping).</summary>
    Task NextStationAsync(CancellationToken ct = default);

    /// <summary>Goes back to and plays the previous catalog station (wrapping).</summary>
    Task PreviousStationAsync(CancellationToken ct = default);

    /// <summary>Sets output volume, clamped to 0-100.</summary>
    Task SetVolumeAsync(int volume, CancellationToken ct = default);

    /// <summary>Mutes or unmutes output.</summary>
    Task SetMuteAsync(bool muted, CancellationToken ct = default);
}
