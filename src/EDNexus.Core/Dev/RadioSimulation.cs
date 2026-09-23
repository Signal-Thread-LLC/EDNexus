using EDNexus.Core.Journal;
using EDNexus.Core.Radio;

namespace EDNexus.Core.Dev;

/// <summary>
/// Dev sample source for the Space Radio card. The radio isn't journal-driven, so this emits a
/// synthetic, EDNexus-only <see cref="EventName"/> line that only <see cref="SimulatedRadioPlayer"/>
/// listens for. Each draw steps to the next status in <see cref="Cycle"/> — so repeated 🎲 clicks
/// walk the card through buffering, playing, a buffer underrun, paused, error and stopped — with a
/// random station, volume, mute and (for errors) failure message. Nothing here touches LibVLC, the
/// network, or settings.
/// </summary>
public sealed class RadioSampleSource : JournalSampleSource
{
    /// <summary>Name of the synthetic event. Deliberately not a Frontier event name, so no real tracker reacts to it.</summary>
    public const string EventName = "EDNexusRadioSample";

    /// <summary>The status sequence successive samples step through: connect, play, underrun, pause, fail, stop.</summary>
    public static IReadOnlyList<RadioPlaybackStatus> Cycle { get; } = new[]
    {
        RadioPlaybackStatus.Buffering,
        RadioPlaybackStatus.Playing,
        RadioPlaybackStatus.Buffering,   // buffer underrun mid-stream
        RadioPlaybackStatus.Paused,
        RadioPlaybackStatus.Error,
        RadioPlaybackStatus.Stopped,
    };

    private static readonly string[] Errors =
    {
        "The stream could not be played (network error or invalid stream).",
        "Connection to the stream server timed out.",
        "The stream server returned HTTP 503 (service unavailable).",
        "Radio engine unavailable: libvlc could not be loaded.",
    };

    private int _next;

    public override string CardKey => "radio";
    public override string DisplayName => "Space Radio";

    public override IReadOnlyList<string> Sample(Random rng)
    {
        var status = Cycle[_next];
        _next = (_next + 1) % Cycle.Count;

        var station = Pick(rng, RadioStationCatalog.Stations);
        return new[]
        {
            Event(EventName, o =>
            {
                o["Status"] = status.ToString();
                o["Station"] = station.Id;
                o["Volume"] = rng.Next(0, 101);
                o["Muted"] = rng.Next(5) == 0;
                if (status == RadioPlaybackStatus.Error) o["Error"] = Pick(rng, Errors);
            }),
        };
    }
}

/// <summary>
/// An in-memory <see cref="IRadioPlayer"/> for developer mode. Its state is set by
/// <see cref="RadioSampleSource"/> events arriving on the bus, and the card's controls act on it with
/// the same transitions as the real player (play/pause goes through
/// <see cref="RadioPlayerService.ToggleActionFor"/>, and playing a station goes Buffering then
/// Playing) — but it never opens an audio device or a stream, and never persists anything, so the
/// saved <c>RadioWasPlaying</c> is untouched.
/// </summary>
public sealed class SimulatedRadioPlayer : IRadioPlayer
{
    private static readonly TimeSpan DefaultConnectDelay = TimeSpan.FromMilliseconds(800);

    private readonly object _gate = new();
    private readonly TimeSpan _connectDelay;
    private int _generation; // bumped by every state change, so a stale "connected" can't overwrite a newer state
    private RadioStation? _station;
    private RadioPlaybackStatus _status = RadioPlaybackStatus.Stopped;
    private int _volume = 50;
    private bool _muted;
    private bool _enabled;
    private string? _lastError;

    /// <param name="connectDelay">
    /// How long a simulated station "buffers" before it reports Playing (default 800 ms). Zero
    /// connects synchronously, which keeps tests deterministic.
    /// </param>
    public SimulatedRadioPlayer(TimeSpan? connectDelay = null)
        => _connectDelay = connectDelay ?? DefaultConnectDelay;

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Listen for <see cref="RadioSampleSource"/> events on <paramref name="bus"/>.</summary>
    public void Attach(JournalEventBus bus) => bus.Subscribe(RadioSampleSource.EventName, Apply);

    /// <inheritdoc />
    public RadioPlayerSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return new RadioPlayerSnapshot(_enabled, _station, _status, _volume, _muted, _lastError);
        }
    }

    /// <summary>
    /// Fold one sample event into the simulated state. Parsed defensively: an unrecognised status
    /// drops the event, and any other missing or malformed field just keeps its current value.
    /// </summary>
    public void Apply(JournalEntry e)
    {
        if (!Enum.TryParse<RadioPlaybackStatus>(e.GetString("Status"), ignoreCase: true, out var status)
            || !Enum.IsDefined(status))
            return;

        lock (_gate)
        {
            _generation++;
            _status = status;
            _enabled = true;
            if (RadioStationCatalog.Find(e.GetString("Station")) is { } station) _station = station;
            if (e.GetInt64("Volume") is long volume) _volume = (int)Math.Clamp(volume, 0, 100);
            if (e.GetBool("Muted") is bool muted) _muted = muted;
            _lastError = status == RadioPlaybackStatus.Error
                ? e.GetString("Error") ?? "The stream could not be played."
                : null;
        }
        RaiseChanged();
    }

    /// <inheritdoc />
    public Task TogglePlayPauseAsync(CancellationToken ct = default)
    {
        RadioPlaybackStatus status;
        RadioStation? station;
        lock (_gate) { status = _status; station = _station; }

        return RadioPlayerService.ToggleActionFor(status) switch
        {
            RadioToggleAction.Pause => SetStatus(RadioPlaybackStatus.Paused),
            RadioToggleAction.Stop => SetStatus(RadioPlaybackStatus.Stopped),
            _ => PlayAsync(station?.Id ?? RadioStationCatalog.Stations[0].Id, ct),
        };
    }

    /// <summary>
    /// Tunes to the station and reports Buffering, then Playing once the connect delay passes — as
    /// the real player does when LibVLC reports the stream has started. Returns once buffering has
    /// begun, like the real player, so the UI's play button isn't held busy while it "connects".
    /// </summary>
    public Task PlayAsync(string stationId, CancellationToken ct = default)
    {
        var station = RadioStationCatalog.Find(stationId);
        if (station is null) return Task.CompletedTask;

        int generation;
        lock (_gate)
        {
            generation = ++_generation;
            _station = station;
            _enabled = true;
            _status = RadioPlaybackStatus.Buffering;
            _lastError = null;
        }
        RaiseChanged();

        _ = ConnectAsync(generation);
        return Task.CompletedTask;
    }

    private async Task ConnectAsync(int generation)
    {
        if (_connectDelay > TimeSpan.Zero) await Task.Delay(_connectDelay).ConfigureAwait(false);

        lock (_gate)
        {
            // Stopped, paused, retuned or re-sampled meanwhile: that newer state wins.
            if (generation != _generation || _status != RadioPlaybackStatus.Buffering) return;
            _status = RadioPlaybackStatus.Playing;
        }
        RaiseChanged();
    }

    /// <inheritdoc />
    public Task NextStationAsync(CancellationToken ct = default)
    {
        string? current;
        lock (_gate) current = _station?.Id;
        return PlayAsync(RadioStationCatalog.Next(current).Id, ct);
    }

    /// <inheritdoc />
    public Task PreviousStationAsync(CancellationToken ct = default)
    {
        string? current;
        lock (_gate) current = _station?.Id;
        return PlayAsync(RadioStationCatalog.Previous(current).Id, ct);
    }

    /// <inheritdoc />
    public Task SetVolumeAsync(int volume, CancellationToken ct = default)
    {
        lock (_gate) _volume = Math.Clamp(volume, 0, 100);
        RaiseChanged();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetMuteAsync(bool muted, CancellationToken ct = default)
    {
        lock (_gate) _muted = muted;
        RaiseChanged();
        return Task.CompletedTask;
    }

    private Task SetStatus(RadioPlaybackStatus status)
    {
        lock (_gate)
        {
            _generation++;
            _status = status;
            _lastError = null;
        }
        RaiseChanged();
        return Task.CompletedTask;
    }

    private void RaiseChanged() => Changed?.Invoke();
}
