using EDNexus.Core.Settings;

namespace EDNexus.Core.Radio;

/// <summary>
/// Feature service for the background radio player. Owns the current station, playback state, volume
/// and mute — all derived state a UI card would bind to — and drives an <see cref="IRadioAudioBackend"/>
/// to actually move audio. Every playback command (<see cref="Play"/>, <see cref="Pause"/>,
/// <see cref="Resume"/>, <see cref="Stop"/>, <see cref="SetVolume"/>, <see cref="SetMute"/>) returns
/// immediately; the backend is responsible for keeping the caller's thread (the Avalonia UI thread, in
/// practice) unblocked.
/// </summary>
/// <remarks>
/// This is not fed by the journal — Elite Dangerous has no concept of a radio — so it does not sit
/// behind <see cref="Journal.JournalEventBus"/> like the other feature trackers. It follows the same
/// "one writer" shape as <see cref="EDNexus.Core.State.StateTracker"/> in spirit: only this class
/// mutates its own state, and it is the only thing that writes <see cref="RadioSettings"/>.
/// </remarks>
public sealed class RadioPlayerService : IDisposable
{
    private readonly IRadioAudioBackend _backend;
    private readonly AppSettings? _settings;
    private readonly SettingsStore? _store;
    private readonly object _gate = new();

    private RadioStation? _station;
    private RadioPlaybackState _state = RadioPlaybackState.Stopped;
    private int _volume;
    private bool _mute;
    private bool _enabled;
    private string? _lastError;

    /// <summary>Raised after any observable change to the snapshot returned by <see cref="Snapshot"/>.</summary>
    public event Action? Changed;

    /// <param name="backend">The audio engine to drive. Callers choose the concrete backend so tests and
    /// developer mode can inject <see cref="NullRadioAudioBackend"/> instead of a real media engine.</param>
    /// <param name="settings">
    /// When supplied, the last station/volume/mute/enabled state is restored from it on construction and
    /// every change is written straight back via <paramref name="store"/>. Both null (as the test
    /// default) means playback still works, just without persistence.
    /// </param>
    public RadioPlayerService(IRadioAudioBackend backend, AppSettings? settings = null, SettingsStore? store = null)
    {
        _backend = backend;
        _settings = settings;
        _store = store;
        _backend.StateChanged += OnBackendStateChanged;

        var saved = settings?.Radio;
        _volume = Math.Clamp(saved?.Volume ?? 50, 0, 100);
        _mute = saved?.Mute ?? false;
        _enabled = saved?.Enabled ?? false;
        _station = RadioStationCatalog.Find(saved?.LastStationId);

        _backend.SetVolume(_volume);
        _backend.SetMute(_mute);
    }

    /// <summary>The shipped station registry, for populating a station picker.</summary>
    public IReadOnlyList<RadioStation> Stations => RadioStationCatalog.Stations;

    /// <summary>Current state as an immutable snapshot, safe to read from any thread.</summary>
    public RadioPlayerSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return new RadioPlayerSnapshot(_station, _state, _volume, _mute, _enabled, _lastError);
        }
    }

    /// <summary>
    /// Turn the radio feature on or off. Disabling stops playback immediately; it does not clear the
    /// last-tuned station, so re-enabling can resume where it left off.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        bool changed;
        lock (_gate)
        {
            changed = _enabled != enabled;
            _enabled = enabled;
        }
        if (!enabled) _backend.Stop();
        if (changed) Persist();
        Changed?.Invoke();
    }

    /// <summary>Tune to and start playing <paramref name="stationId"/>. Unknown ids are ignored.</summary>
    public void Play(string stationId)
    {
        var station = RadioStationCatalog.Find(stationId);
        if (station is null) return;

        lock (_gate)
        {
            _station = station;
            _enabled = true;
            _state = RadioPlaybackState.Buffering;
            _lastError = null;
        }
        Persist();
        Changed?.Invoke();
        _backend.Play(station.StreamUrl);
    }

    /// <summary>Pause the current station. A no-op when nothing is tuned in.</summary>
    public void Pause()
    {
        if (Snapshot.Station is null) return;
        _backend.Pause();
    }

    /// <summary>Resume a paused station.</summary>
    public void Resume()
    {
        if (Snapshot.Station is null) return;
        _backend.Resume();
    }

    /// <summary>Stop playback outright (as opposed to pausing).</summary>
    public void Stop()
    {
        _backend.Stop();
    }

    /// <summary>Set output volume, 0–100 (values outside that range are clamped).</summary>
    public void SetVolume(int volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        lock (_gate) _volume = volume;
        _backend.SetVolume(volume);
        Persist();
        Changed?.Invoke();
    }

    /// <summary>Mute or unmute without changing the stored volume.</summary>
    public void SetMute(bool mute)
    {
        lock (_gate) _mute = mute;
        _backend.SetMute(mute);
        Persist();
        Changed?.Invoke();
    }

    private void OnBackendStateChanged(RadioPlaybackState state, string? error)
    {
        lock (_gate)
        {
            _state = state;
            _lastError = state == RadioPlaybackState.Error ? error : null;
        }
        Changed?.Invoke();
    }

    private void Persist()
    {
        if (_settings is null || _store is null) return;
        RadioSettings snapshot;
        lock (_gate)
        {
            snapshot = _settings.Radio;
            snapshot.Enabled = _enabled;
            snapshot.LastStationId = _station?.Id;
            snapshot.Volume = _volume;
            snapshot.Mute = _mute;
        }
        _store.Save(_settings);
    }

    public void Dispose()
    {
        _backend.StateChanged -= OnBackendStateChanged;
        _backend.Dispose();
    }
}
