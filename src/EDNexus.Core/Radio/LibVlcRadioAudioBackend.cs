using LibVLCSharp.Shared;

namespace EDNexus.Core.Radio;

/// <summary>
/// Real audio backend: wraps a <see cref="LibVLCSharp"/> <see cref="LibVLC"/> engine and
/// <see cref="MediaPlayer"/> to actually stream a station. All LibVLC callbacks arrive on its own
/// background thread, so <see cref="IRadioAudioBackend.StateChanged"/> is never raised from the caller's
/// thread — exactly the "off the UI thread" requirement the feature needs.
/// </summary>
/// <remarks>
/// Construction touches native code (<c>Core.Initialize()</c> + <c>new LibVLC()</c>), which throws when
/// the native libvlc runtime isn't installed/deployed (headless CI, a Linux box without the system
/// package, the CLI harness). Callers should catch and fall back to <see cref="NullRadioAudioBackend"/>
/// — see <c>EngineHost</c>'s construction of <see cref="RadioPlayerService"/>.
/// </remarks>
public sealed class LibVlcRadioAudioBackend : IRadioAudioBackend
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private Media? _media;
    private bool _disposed;

    public event Action<RadioPlaybackState, string?>? StateChanged;

    public LibVlcRadioAudioBackend()
    {
        // Qualified: "Core" would otherwise resolve to the EDNexus.Core namespace this file lives in.
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC(enableDebugLogs: false);
        _player = new MediaPlayer(_libVlc);

        _player.Playing += (_, _) => Raise(RadioPlaybackState.Playing);
        _player.Paused += (_, _) => Raise(RadioPlaybackState.Paused);
        _player.Stopped += (_, _) => Raise(RadioPlaybackState.Stopped);
        _player.Buffering += (_, e) => { if (e.Cache < 100) Raise(RadioPlaybackState.Buffering); };
        _player.EncounteredError += (_, _) => Raise(RadioPlaybackState.Error, "Playback error — the stream could not be reached or decoded.");
    }

    public void Play(string streamUrl)
    {
        Stop();

        _media = new Media(_libVlc, new Uri(streamUrl));
        _player.Play(_media);
    }

    public void Pause()
    {
        if (_player.CanPause) _player.Pause();
    }

    public void Resume()
    {
        if (!_player.IsPlaying) _player.Play();
    }

    public void Stop()
    {
        if (_player.IsPlaying || _player.State == VLCState.Paused) _player.Stop();
        _media?.Dispose();
        _media = null;
    }

    public void SetVolume(int volume) => _player.Volume = Math.Clamp(volume, 0, 100);

    public void SetMute(bool mute) => _player.Mute = mute;

    private void Raise(RadioPlaybackState state, string? error = null) => StateChanged?.Invoke(state, error);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _media?.Dispose();
        _player.Dispose();
        _libVlc.Dispose();
    }
}
