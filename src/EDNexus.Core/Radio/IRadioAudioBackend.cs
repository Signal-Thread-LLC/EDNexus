namespace EDNexus.Core.Radio;

/// <summary>
/// The audio engine <see cref="RadioPlayerService"/> drives — plays a single stream URL at a time.
/// Kept behind an interface (per the cross-platform-first convention: OS/engine-specific behaviour
/// goes behind an interface with a no-op fallback) so:
/// <list type="bullet">
/// <item>the real implementation (<c>LibVlcRadioAudioBackend</c>) can wrap a native media engine that
/// may not be installed on every machine, without that failure taking the app down;</item>
/// <item>tests and developer mode can swap in <see cref="NullRadioAudioBackend"/> to exercise every
/// playback state transition without opening a single network socket.</item>
/// </list>
/// Every method is expected to return immediately — any blocking I/O (connecting to the stream,
/// buffering) happens on the backend's own worker thread, never the caller's.
/// </summary>
public interface IRadioAudioBackend : IDisposable
{
    /// <summary>
    /// Raised whenever the backend's playback state changes, including the terminal
    /// <see cref="RadioPlaybackState.Error"/> transition. <c>error</c> carries a short message
    /// only when the new state is <see cref="RadioPlaybackState.Error"/>.
    /// </summary>
    event Action<RadioPlaybackState, string?>? StateChanged;

    /// <summary>Start (or restart) playback of <paramref name="streamUrl"/>. Stops whatever was playing first.</summary>
    void Play(string streamUrl);

    /// <summary>Pause the current stream. A no-op when nothing is playing.</summary>
    void Pause();

    /// <summary>Resume a paused stream. A no-op when nothing is paused.</summary>
    void Resume();

    /// <summary>Stop playback and release the current stream connection.</summary>
    void Stop();

    /// <summary>Set output volume, 0–100. Values outside that range are clamped by the caller.</summary>
    void SetVolume(int volume);

    /// <summary>Mute or unmute without changing <see cref="SetVolume"/>'s level.</summary>
    void SetMute(bool mute);
}
