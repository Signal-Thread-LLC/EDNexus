namespace EDNexus.Core.Radio;

/// <summary>
/// No-op audio backend: simulates the state transitions a real backend would report, synchronously and
/// without ever touching the network. Used as the automatic fallback when the native media engine
/// isn't available (e.g. the CLI harness, or a machine missing the LibVLC runtime) and by developer
/// mode / unit tests to exercise <see cref="RadioPlayerService"/> deterministically.
/// </summary>
public sealed class NullRadioAudioBackend : IRadioAudioBackend
{
    private bool _paused;

    public event Action<RadioPlaybackState, string?>? StateChanged;

    public void Play(string streamUrl)
    {
        _paused = false;
        // A real stream buffers briefly before it starts — mirror that transition so UI/tests that
        // key off Buffering→Playing behave the same against either backend.
        StateChanged?.Invoke(RadioPlaybackState.Buffering, null);
        StateChanged?.Invoke(RadioPlaybackState.Playing, null);
    }

    public void Pause()
    {
        _paused = true;
        StateChanged?.Invoke(RadioPlaybackState.Paused, null);
    }

    public void Resume()
    {
        if (!_paused) return;
        _paused = false;
        StateChanged?.Invoke(RadioPlaybackState.Playing, null);
    }

    public void Stop()
    {
        _paused = false;
        StateChanged?.Invoke(RadioPlaybackState.Stopped, null);
    }

    public void SetVolume(int volume)
    {
        // Nothing to drive — RadioPlayerService is the source of truth for the persisted value.
    }

    public void SetMute(bool mute)
    {
    }

    public void Dispose()
    {
    }
}
