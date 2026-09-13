using EDNexus.Core.Radio;

namespace EDNexus.Tests.Radio;

/// <summary>
/// Fully controllable test double for <see cref="IRadioAudioBackend"/>: records every call so tests can
/// assert on them, and lets the test drive <see cref="RaiseState"/> to simulate what a real backend
/// would report (including error/buffering transitions <see cref="NullRadioAudioBackend"/> never
/// produces).
/// </summary>
public sealed class FakeRadioAudioBackend : IRadioAudioBackend
{
    public List<string> Calls { get; } = new();
    public string? LastPlayedUrl { get; private set; }
    public int? LastVolume { get; private set; }
    public bool? LastMute { get; private set; }
    public bool Disposed { get; private set; }

    public event Action<RadioPlaybackState, string?>? StateChanged;

    public void Play(string streamUrl)
    {
        Calls.Add(nameof(Play));
        LastPlayedUrl = streamUrl;
    }

    public void Pause() => Calls.Add(nameof(Pause));

    public void Resume() => Calls.Add(nameof(Resume));

    public void Stop() => Calls.Add(nameof(Stop));

    public void SetVolume(int volume)
    {
        Calls.Add(nameof(SetVolume));
        LastVolume = volume;
    }

    public void SetMute(bool mute)
    {
        Calls.Add(nameof(SetMute));
        LastMute = mute;
    }

    /// <summary>Simulates the backend reporting a state change, as a real one would from its own thread.</summary>
    public void RaiseState(RadioPlaybackState state, string? error = null) => StateChanged?.Invoke(state, error);

    public void Dispose() => Disposed = true;
}
