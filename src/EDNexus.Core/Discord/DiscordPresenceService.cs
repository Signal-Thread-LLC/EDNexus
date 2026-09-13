using System.ComponentModel;
using EDNexus.Core.State;

namespace EDNexus.Core.Discord;

/// <summary>
/// Background feature module that mirrors <see cref="CommanderState"/> onto Discord Rich Presence.
/// A read-only consumer of <see cref="CommanderState"/> — like every feature module it never mutates
/// state, only <see cref="StateTracker"/> does that (see AGENTS.md, "one writer").
/// </summary>
/// <remarks>
/// Discord asks integrations not to push more than one presence update in a short window. Rather than
/// drop updates that land inside that window, changes are coalesced: the latest computed payload is
/// remembered and, if the throttle is currently closed, a single delayed send is scheduled for the
/// moment it reopens — so the commander's presence always catches up to the newest state without ever
/// exceeding the rate limit.
/// </remarks>
public sealed class DiscordPresenceService : IDisposable
{
    /// <summary>
    /// Placeholder Discord application (Client) ID. Replace with EDNexus's own application, created at
    /// https://discord.com/developers/applications, before shipping — this id has no art assets
    /// registered against it, so large/small images will simply not render until it is replaced.
    /// </summary>
    public const string DefaultApplicationId = "0000000000000000000";

    /// <summary>Discord's own guidance: don't push presence updates more than once every 15 seconds.</summary>
    public static readonly TimeSpan DefaultMinInterval = TimeSpan.FromSeconds(15);

    private static readonly string[] RelevantProperties =
    {
        nameof(CommanderState.StarSystem),
        nameof(CommanderState.Body),
        nameof(CommanderState.Docked),
        nameof(CommanderState.StationName),
        nameof(CommanderState.CarrierName),
        nameof(CommanderState.Ship),
        nameof(CommanderState.ShipIdent),
        nameof(CommanderState.CargoTons),
        nameof(CommanderState.Name),
    };

    private readonly CommanderState _state;
    private readonly IDiscordRpcClient _client;
    private readonly PresenceThrottle _throttle;
    private readonly Func<bool> _isSuppressed;
    private readonly Func<DateTimeOffset> _clock;
    private readonly DateTimeOffset _sessionStartedAt;
    private readonly object _gate = new();

    private string? _lastSystem;
    private DateTimeOffset _systemEnteredAt;
    private DiscordPresencePayload? _lastComputed;
    private CancellationTokenSource? _pending;
    private bool _disposed;

    /// <param name="state">The live commander picture to mirror. Never written to.</param>
    /// <param name="client">
    /// The Discord transport. Pass a <see cref="DiscordRpcClientAdapter"/> for the real integration or
    /// <see cref="NoOpDiscordRpcClient"/> to disable it outright (still safe to construct either way).
    /// </param>
    /// <param name="isSuppressed">
    /// Optional live predicate; while it returns true no presence is computed or sent. Wired to
    /// developer mode so fabricated sample data never reaches a commander's real Discord profile.
    /// </param>
    public DiscordPresenceService(CommanderState state, IDiscordRpcClient client, Func<bool>? isSuppressed = null)
        : this(state, client, DefaultMinInterval, isSuppressed, null) { }

    /// <summary>Test-only constructor: a shortened throttle window and/or a controllable clock.</summary>
    internal DiscordPresenceService(
        CommanderState state, IDiscordRpcClient client, TimeSpan minInterval,
        Func<bool>? isSuppressed = null, Func<DateTimeOffset>? clock = null)
    {
        _state = state;
        _client = client;
        _isSuppressed = isSuppressed ?? (static () => false);
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        _throttle = new PresenceThrottle(minInterval, _clock);

        _sessionStartedAt = _clock();
        _systemEnteredAt = _sessionStartedAt;
        _lastSystem = state.StarSystem;

        // Never let a failed/unsupported connection surface as an exception during construction.
        try { _client.TryInitialize(); } catch { /* graceful fallback */ }

        _state.PropertyChanged += OnStateChanged;

        // Push whatever the state already knows (e.g. warmed from a replayed journal) immediately.
        RequestUpdate();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not { } name || Array.IndexOf(RelevantProperties, name) < 0) return;

        if (name == nameof(CommanderState.StarSystem) && !string.Equals(_state.StarSystem, _lastSystem, StringComparison.OrdinalIgnoreCase))
        {
            _lastSystem = _state.StarSystem;
            _systemEnteredAt = _clock();
        }

        RequestUpdate();
    }

    private void RequestUpdate()
    {
        if (_isSuppressed()) return;

        lock (_gate)
        {
            if (_disposed) return;

            var payload = DiscordPresenceMapper.Map(_state, _sessionStartedAt, _systemEnteredAt);
            if (_lastComputed is not null && payload.Equals(_lastComputed)) return;
            _lastComputed = payload;

            if (_throttle.TryAcquire())
            {
                Send(payload);
                return;
            }

            SchedulePendingLocked(_throttle.TimeUntilNextSend());
        }
    }

    private void Send(DiscordPresencePayload payload)
    {
        try { _client.SetPresence(payload); } catch { /* graceful fallback: never throw */ }
    }

    /// <summary>Caller must hold <see cref="_gate"/>.</summary>
    private void SchedulePendingLocked(TimeSpan delay)
    {
        _pending?.Cancel();
        var cts = new CancellationTokenSource();
        _pending = cts;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(delay, cts.Token).ConfigureAwait(false); }
            catch (TaskCanceledException) { return; }

            lock (_gate)
            {
                if (_disposed || cts.IsCancellationRequested) return;
                if (_lastComputed is { } latest && _throttle.TryAcquire()) Send(latest);
            }
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending?.Cancel();
        }

        _state.PropertyChanged -= OnStateChanged;
        try { _client.Clear(); } catch { }
        try { _client.Dispose(); } catch { }
    }
}
