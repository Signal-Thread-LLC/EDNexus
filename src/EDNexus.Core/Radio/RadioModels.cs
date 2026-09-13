namespace EDNexus.Core.Radio;

/// <summary>
/// One configured internet radio station: a display name plus the live stream URL LibVLC is pointed
/// at directly (all six shipped stations are plain MP3/Icecast streams — no playlist resolution
/// needed).
/// </summary>
/// <param name="Id">Stable identifier persisted in <see cref="Settings.RadioSettings.LastStationId"/>.</param>
/// <param name="Name">Display name shown in the UI.</param>
/// <param name="StreamUrl">Direct MP3/Icecast stream URL.</param>
/// <param name="Description">Short blurb about the station's content.</param>
/// <param name="SourceUrl">The station's own website, for attribution.</param>
public sealed record RadioStation(string Id, string Name, string StreamUrl, string Description, string SourceUrl);

/// <summary>
/// Playback lifecycle of the radio player. <see cref="Buffering"/> covers both the initial connect and
/// any mid-stream stall the backend reports; <see cref="Error"/> means the backend gave up (bad URL,
/// unreachable host, decode failure) and playback stopped.
/// </summary>
public enum RadioPlaybackState
{
    Stopped,
    Buffering,
    Playing,
    Paused,
    Error,
}

/// <summary>Immutable snapshot of <see cref="RadioPlayerService"/>'s current state, for the UI to bind to.</summary>
/// <param name="Station">The tuned-in station, or null before anything has ever played.</param>
/// <param name="State">Current playback lifecycle state.</param>
/// <param name="Volume">Volume, 0–100.</param>
/// <param name="Mute">Whether playback is muted.</param>
/// <param name="Enabled">Master on/off switch for the radio feature.</param>
/// <param name="LastError">The backend's error message from the most recent failure, if <see cref="State"/> is <see cref="RadioPlaybackState.Error"/>.</param>
public sealed record RadioPlayerSnapshot(
    RadioStation? Station,
    RadioPlaybackState State,
    int Volume,
    bool Mute,
    bool Enabled,
    string? LastError);
