using EDNexus.Core.Settings;
using LibVLCSharp.Shared;

namespace EDNexus.Core.Radio;

/// <summary>
/// Background audio playback for the built-in radio stations (<see cref="RadioStationCatalog"/>),
/// backed by LibVLC. Owns its own derived state (current station, playback status, volume, mute) and
/// — when constructed with a <see cref="SettingsStore"/> — persists user choices to
/// <c>settings.json</c> as they change and restores them on <see cref="RestoreAsync"/>.
/// </summary>
/// <remarks>
/// All playback operations (initializing the engine, loading media, changing volume/mute) run on a
/// background thread via <see cref="Task.Run(Action)"/> so a caller on the Avalonia UI thread never
/// blocks on the network or on native LibVLC calls. LibVLC itself is loaded lazily on first use so
/// constructing this service (e.g. for the CLI harness, which never touches audio) has no cost and
/// cannot throw even if no native VLC runtime is present on the machine — a missing/broken native
/// library surfaces as <see cref="RadioPlaybackStatus.Error"/> instead of a crash.
/// </remarks>
public sealed class RadioPlayerService : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly AppSettings? _settings;
    private readonly SettingsStore? _store;

    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private RadioStation? _station;
    private RadioPlaybackStatus _status = RadioPlaybackStatus.Stopped;
    private int _volume = 50;
    private bool _muted;
    private bool _enabled;
    private string? _lastError;
    private bool _disposed;

    /// <summary>Raised after any playback state, station, volume, or mute change.</summary>
    public event Action? Changed;

    /// <param name="settings">
    /// When supplied, seeds the initial station/volume/mute/enabled state and every subsequent
    /// change is persisted back into it (and saved via <paramref name="store"/>, if present).
    /// </param>
    /// <param name="store">Used to write <paramref name="settings"/> to disk after each change.</param>
    public RadioPlayerService(AppSettings? settings = null, SettingsStore? store = null)
    {
        _settings = settings;
        _store = store;

        var radio = settings?.Radio;
        if (radio is not null)
        {
            _enabled = radio.RadioEnabled;
            _volume = Math.Clamp(radio.RadioVolume, 0, 100);
            _muted = radio.RadioMute;
            _station = RadioStationCatalog.Find(radio.RadioLastStation);
        }
    }

    /// <summary>A point-in-time snapshot of everything the UI needs to render the player.</summary>
    public RadioPlayerSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return new RadioPlayerSnapshot(_enabled, _station, _status, _volume, _muted, _lastError);
        }
    }

    /// <summary>
    /// Resumes whatever was persisted from a previous session: if the radio was left enabled with a
    /// station tuned, starts playing it (respecting the saved volume/mute). Safe to call once at app
    /// startup; a no-op if the radio was never enabled or no station was saved. Never throws — a
    /// failed resume just leaves the player in <see cref="RadioPlaybackStatus.Error"/>.
    /// </summary>
    public async Task RestoreAsync(CancellationToken ct = default)
    {
        bool enabled;
        RadioStation? station;
        lock (_gate) { enabled = _enabled; station = _station; }

        if (!enabled || station is null) return;
        await PlayAsync(station.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Turns the radio feature on/off. Turning it off stops any current playback.</summary>
    public async Task SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        lock (_gate) _enabled = enabled;
        Persist();

        if (!enabled) await StopAsync(ct).ConfigureAwait(false);
        else RaiseChanged();
    }

    /// <summary>Loads and plays the given station (by <see cref="RadioStation.Id"/>), replacing whatever is currently playing.</summary>
    public Task PlayAsync(string stationId, CancellationToken ct = default)
    {
        var station = RadioStationCatalog.Find(stationId);
        if (station is null) return Task.CompletedTask;

        lock (_gate)
        {
            _station = station;
            _enabled = true;
        }
        Persist();

        return Task.Run(() => PlayStationCore(station), ct);
    }

    /// <summary>Resumes the currently-loaded station (or restarts it if stopped). No-op if none is tuned.</summary>
    public Task PlayAsync(CancellationToken ct = default)
    {
        RadioStation? station;
        lock (_gate) station = _station;
        return station is null ? Task.CompletedTask : Task.Run(() => PlayStationCore(station), ct);
    }

    /// <summary>
    /// Toggles between playing and paused: pauses if currently playing, otherwise resumes the tuned
    /// station (or starts the first catalog station if none has been tuned yet). This is the single
    /// entry point a hardware "Play/Pause" media key should call.
    /// </summary>
    public Task TogglePlayPauseAsync(CancellationToken ct = default)
    {
        RadioPlaybackStatus status;
        RadioStation? station;
        lock (_gate) { status = _status; station = _station; }

        if (status == RadioPlaybackStatus.Playing) return PauseAsync(ct);
        return PlayAsync(station?.Id ?? RadioStationCatalog.Stations[0].Id, ct);
    }

    /// <summary>Advances to and plays the next station in the catalog (wrapping). This is what a hardware "Next" media key should call.</summary>
    public Task NextStationAsync(CancellationToken ct = default)
    {
        string? current;
        lock (_gate) current = _station?.Id;
        return PlayAsync(RadioStationCatalog.Next(current).Id, ct);
    }

    /// <summary>Goes back to and plays the previous station in the catalog (wrapping). This is what a hardware "Previous" media key should call.</summary>
    public Task PreviousStationAsync(CancellationToken ct = default)
    {
        string? current;
        lock (_gate) current = _station?.Id;
        return PlayAsync(RadioStationCatalog.Previous(current).Id, ct);
    }

    /// <summary>Pauses playback, leaving the current station loaded.</summary>
    public Task PauseAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            try
            {
                lock (_gate) _mediaPlayer?.Pause();
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
            }
        }, ct);

    /// <summary>Stops playback and releases the loaded media.</summary>
    public Task StopAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            try
            {
                lock (_gate)
                {
                    _mediaPlayer?.Stop();
                    _status = RadioPlaybackStatus.Stopped;
                }
                RaiseChanged();
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
            }
        }, ct);

    /// <summary>Sets output volume (0-100), applying it immediately if the engine is initialized.</summary>
    public Task SetVolumeAsync(int volume, CancellationToken ct = default)
    {
        var clamped = Math.Clamp(volume, 0, 100);
        lock (_gate) _volume = clamped;
        Persist();

        return Task.Run(() =>
        {
            try
            {
                lock (_gate)
                {
                    if (_mediaPlayer is not null) _mediaPlayer.Volume = clamped;
                }
                RaiseChanged();
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
            }
        }, ct);
    }

    /// <summary>Mutes/unmutes output, applying it immediately if the engine is initialized.</summary>
    public Task SetMuteAsync(bool muted, CancellationToken ct = default)
    {
        lock (_gate) _muted = muted;
        Persist();

        return Task.Run(() =>
        {
            try
            {
                lock (_gate)
                {
                    if (_mediaPlayer is not null) _mediaPlayer.Mute = muted;
                }
                RaiseChanged();
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
            }
        }, ct);
    }

    /// <summary>Runs on a background thread: lazily brings up LibVLC and starts streaming the given station.</summary>
    private void PlayStationCore(RadioStation station)
    {
        try
        {
            var player = EnsureEngine();
            if (player is null) return; // EnsureEngine already recorded the error.

            lock (_gate) _status = RadioPlaybackStatus.Buffering;
            RaiseChanged();

            using var media = new Media(_libVlc!, new Uri(station.StreamUrl));
            player.Play(media);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    /// <summary>
    /// Lazily initializes the LibVLC engine and media player, wiring their events into our status.
    /// Returns null (having already recorded the error) if the native runtime isn't available —
    /// e.g. no system libvlc on Linux, or a corrupt install — so callers can bail without crashing.
    /// </summary>
    private MediaPlayer? EnsureEngine()
    {
        lock (_gate)
        {
            if (_mediaPlayer is not null) return _mediaPlayer;

            try
            {
                LibVLCSharp.Shared.Core.Initialize();
                _libVlc = new LibVLC(enableDebugLogs: false);
                var player = new MediaPlayer(_libVlc)
                {
                    Volume = _volume,
                    Mute = _muted,
                };
                player.Playing += (_, _) => { lock (_gate) { _status = RadioPlaybackStatus.Playing; _lastError = null; } RaiseChanged(); };
                player.Paused += (_, _) => { lock (_gate) _status = RadioPlaybackStatus.Paused; RaiseChanged(); };
                player.Stopped += (_, _) => { lock (_gate) _status = RadioPlaybackStatus.Stopped; RaiseChanged(); };
                player.Buffering += (_, _) => { lock (_gate) if (_status != RadioPlaybackStatus.Playing) _status = RadioPlaybackStatus.Buffering; RaiseChanged(); };
                player.EncounteredError += (_, _) => SetError("The stream could not be played (network error or invalid stream).");

                _mediaPlayer = player;
                return player;
            }
            catch (Exception ex)
            {
                // Missing/broken native libvlc, unsupported platform, etc. — never let this take
                // the process down; the radio card should just show an error state.
                _lastError = $"Radio engine unavailable: {ex.Message}";
                _status = RadioPlaybackStatus.Error;
                _libVlc = null;
                _mediaPlayer = null;
                return null;
            }
        }
    }

    private void SetError(string message)
    {
        lock (_gate)
        {
            _status = RadioPlaybackStatus.Error;
            _lastError = message;
        }
        RaiseChanged();
    }

    /// <summary>Writes the current station/volume/mute/enabled state back into settings and saves, if wired up.</summary>
    private void Persist()
    {
        if (_settings is null) return;
        lock (_gate)
        {
            _settings.Radio.RadioEnabled = _enabled;
            _settings.Radio.RadioLastStation = _station?.Id;
            _settings.Radio.RadioVolume = _volume;
            _settings.Radio.RadioMute = _muted;
        }
        _store?.Save(_settings);
    }

    private void RaiseChanged() => Changed?.Invoke();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            try { _mediaPlayer?.Stop(); } catch { /* best effort */ }
            _mediaPlayer?.Dispose();
            _libVlc?.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
