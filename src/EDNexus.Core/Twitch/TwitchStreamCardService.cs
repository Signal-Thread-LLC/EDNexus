using System.ComponentModel;
using EDNexus.Core.State;

namespace EDNexus.Core.Twitch;

/// <summary>
/// Background feature module that mirrors the live commander picture onto the broadcaster's Twitch
/// extension, by publishing <see cref="StreamCardSnapshot"/>s to the EBS. A read-only consumer of
/// <see cref="CommanderState"/> — like every feature module it never mutates state, only
/// <c>StateTracker</c> does that (see AGENTS.md, "one writer").
/// </summary>
/// <remarks>
/// <para>
/// Journal events arrive in bursts — a single hyperspace jump touches system, body, fuel and cargo —
/// while Twitch's PubSub quota (and the EBS's own per-channel rate limit) allows roughly one message
/// every couple of seconds. So updates are coalesced rather than dropped: change notifications only
/// mark the card dirty, and a single pump loop publishes the newest snapshot at most once per
/// <see cref="MinInterval"/>. Viewers always converge on current state without the app ever exceeding
/// the quota.
/// </para>
/// <para>
/// Nothing is published unless the commander has logged in and switched the card on — both are read
/// through the token callback, so either one going away simply leaves the pump idle. A <c>401</c>
/// stops publishing and raises <see cref="ReauthRequired"/>: a revoked grant will never recover by
/// retrying, and hammering the EBS with a dead token is exactly what its rate limiter exists to
/// punish. Publishing resumes on its own once a different token is offered — i.e. once the commander
/// has logged in again.
/// </para>
/// </remarks>
public sealed class TwitchStreamCardService : IDisposable
{
    /// <summary>
    /// Floor between two publishes. The EBS defaults to one state update per channel every two
    /// seconds; five leaves headroom for a commander whose EBS is configured tighter, and is still far
    /// faster than a viewer can read the card.
    /// </summary>
    public static readonly TimeSpan DefaultMinInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <see cref="CommanderState"/> properties worth a republish. Deliberately not every property:
    /// <c>LastUpdated</c> ticks on virtually every journal line and would defeat the deduplication
    /// below.
    /// </summary>
    private static readonly string[] RelevantProperties =
    {
        nameof(CommanderState.Name),
        nameof(CommanderState.Balance),
        nameof(CommanderState.Ship),
        nameof(CommanderState.ShipName),
        nameof(CommanderState.ShipIdent),
        nameof(CommanderState.StarSystem),
        nameof(CommanderState.Body),
        nameof(CommanderState.Docked),
        nameof(CommanderState.StationName),
        nameof(CommanderState.StationType),
        nameof(CommanderState.CarrierName),
        nameof(CommanderState.CarrierCallsign),
        nameof(CommanderState.CarrierFuel),
        nameof(CommanderState.CarrierJumpRange),
        nameof(CommanderState.CarrierPendingSystem),
        nameof(CommanderState.CarrierPendingDeparture),
        nameof(CommanderState.FuelMain),
        nameof(CommanderState.FuelCapacity),
        nameof(CommanderState.CargoTons),
        nameof(CommanderState.Fsd),
    };

    private readonly CommanderState _state;
    private readonly StreamCardSources _sources;
    private readonly IStreamStateApiClient _client;
    private readonly Func<string> _endpoint;
    private readonly Func<string?> _token;
    private readonly Func<StreamCardVisibility> _visibility;
    private readonly Func<bool> _isSuppressed;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _minInterval;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _dirty = new(0, 1);
    private readonly Task _pump;

    private string? _lastPublishedFingerprint;
    private bool _stoppedForReauth;
    /// <summary>The token that earned the 401, so a later login with a different one can resume.</summary>
    private string? _rejectedToken;
    private bool _disposed;

    /// <summary>Raised after every publish attempt, successful or not, for the UI's status line and logs.</summary>
    public event Action<StreamStatePublishResult>? PublishCompleted;

    /// <summary>
    /// Raised when the EBS rejects the token. Publishing stays stopped until a different token is
    /// offered, so the UI should use this to prompt the commander to log in again.
    /// </summary>
    public event Action? ReauthRequired;

    /// <param name="state">The live commander picture to mirror. Never written to.</param>
    /// <param name="sources">Feature trackers the richer card sections are drawn from.</param>
    /// <param name="client">Transport to the EBS.</param>
    /// <param name="updateStateEndpoint">
    /// Reads the absolute URL of the EBS's <c>/api/update-state</c>. A live callback, like the two
    /// below, so pointing the app at a different EBS does not need a restart.
    /// </param>
    /// <param name="token">
    /// Reads the current EBS-issued broadcaster token. A live callback rather than a value so logging
    /// in or out — or switching the card off — takes effect without rebuilding the service; null or
    /// blank means "nothing to publish", and the pump stays idle.
    /// </param>
    /// <param name="visibility">
    /// Reads what the broadcaster currently agrees to show. Also a live callback so toggling a section
    /// off in settings takes effect on the next publish.
    /// </param>
    /// <param name="isSuppressed">
    /// Optional live predicate; while it returns true nothing is computed or published. Wired to
    /// developer mode so fabricated sample data never reaches a real audience.
    /// </param>
    public TwitchStreamCardService(
        CommanderState state,
        StreamCardSources sources,
        IStreamStateApiClient client,
        Func<string> updateStateEndpoint,
        Func<string?> token,
        Func<StreamCardVisibility>? visibility = null,
        Func<bool>? isSuppressed = null)
        : this(state, sources, client, updateStateEndpoint, token, visibility, isSuppressed, DefaultMinInterval, null) { }

    /// <summary>Test-only constructor: a shortened publish interval and/or a controllable clock.</summary>
    internal TwitchStreamCardService(
        CommanderState state,
        StreamCardSources sources,
        IStreamStateApiClient client,
        Func<string> updateStateEndpoint,
        Func<string?> token,
        Func<StreamCardVisibility>? visibility,
        Func<bool>? isSuppressed,
        TimeSpan minInterval,
        Func<DateTimeOffset>? clock)
    {
        _state = state;
        _sources = sources;
        _client = client;
        _endpoint = updateStateEndpoint;
        _token = token;
        _visibility = visibility ?? (static () => StreamCardVisibility.Default);
        _isSuppressed = isSuppressed ?? (static () => false);
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        _minInterval = minInterval;

        _state.PropertyChanged += OnStateChanged;
        _state.CargoChanged += MarkDirty;
        if (_sources.Ranks is { } ranks) ranks.Changed += MarkDirty;
        if (_sources.Exobiology is { } exo) exo.Changed += MarkDirty;
        if (_sources.Mining is { } mining) mining.Changed += MarkDirty;
        if (_sources.Missions is { } missions) missions.Changed += MarkDirty;

        _pump = Task.Run(() => PumpAsync(_cts.Token));

        // Publish whatever the state already knows (e.g. warmed from a replayed journal) right away,
        // so a viewer who opens the card before the commander does anything still sees the card.
        MarkDirty();
    }

    /// <summary>The floor between two publishes this service was built with.</summary>
    public TimeSpan MinInterval => _minInterval;

    /// <summary>
    /// True once a <c>401</c> has stopped publishing. Clears itself the first time a different token
    /// is offered — i.e. once the commander has logged in again.
    /// </summary>
    public bool StoppedForReauth => Volatile.Read(ref _stoppedForReauth);

    /// <summary>
    /// The snapshot this service would publish right now. Exposed for the settings UI's "what viewers
    /// will see" preview, and for diagnostics — computing one has no side effects.
    /// </summary>
    /// <param name="visibility">
    /// Sections to map against, for previewing a choice the commander has not saved yet. Defaults to
    /// the visibility currently in force.
    /// </param>
    public StreamCardSnapshot Preview(StreamCardVisibility? visibility = null) =>
        StreamCardMapper.Map(_state, _sources, visibility ?? _visibility(), _clock());

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && Array.IndexOf(RelevantProperties, name) >= 0) MarkDirty();
    }

    /// <summary>
    /// Marks the card as needing a republish. Called from journal-processing threads, so it does no
    /// work beyond releasing the pump's signal — never any I/O, and never a lock the bus could contend.
    /// </summary>
    private void MarkDirty()
    {
        // A service stopped for reauth stays asleep until the commander logs in again — at which
        // point the token they are publishing with is a different one, and the pump can resume.
        if (_disposed || (StoppedForReauth && _token() == Volatile.Read(ref _rejectedToken))) return;
        try { _dirty.Release(); }
        catch (SemaphoreFullException) { /* already dirty — the pump will pick up the newest state */ }
        catch (ObjectDisposedException) { /* raced with Dispose */ }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await _dirty.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            var backoff = _minInterval;
            try
            {
                var published = await PublishIfChangedAsync(ct).ConfigureAwait(false);
                if (published is { Status: StreamStatePublishStatus.RateLimited or StreamStatePublishStatus.Failed })
                {
                    // Give a struggling EBS (or a tighter-than-expected rate limit) room to recover
                    // rather than retrying at the floor interval.
                    backoff = _minInterval * 2;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Best-effort telemetry: never let a card failure take down the engine's task.
                Raise(new StreamStatePublishResult(StreamStatePublishStatus.Failed, ex.Message));
            }

            // Rate-limit floor. Any change arriving during this wait has already re-armed the signal,
            // so the next iteration publishes the newest state immediately after it elapses.
            try { await Task.Delay(backoff, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Returns the publish result, or null when there was nothing to send.</summary>
    private async Task<StreamStatePublishResult?> PublishIfChangedAsync(CancellationToken ct)
    {
        if (_isSuppressed()) return null;

        var token = _token();
        if (string.IsNullOrWhiteSpace(token)) return null;

        if (StoppedForReauth)
        {
            if (token == Volatile.Read(ref _rejectedToken)) return null;
            // A fresh login: clear the latch and give the new credential a try.
            Volatile.Write(ref _rejectedToken, null);
            Volatile.Write(ref _stoppedForReauth, false);
        }

        var snapshot = StreamCardMapper.Map(_state, _sources, _visibility(), _clock());

        // Deduplicate on content, not on the timestamp the snapshot carries — otherwise a journal
        // line that changes nothing a viewer can see would still spend a publish from the quota.
        var fingerprint = snapshot.ContentFingerprint();
        if (fingerprint == _lastPublishedFingerprint) return null;

        var result = await _client.PublishAsync(_endpoint(), token!, snapshot, ct).ConfigureAwait(false);

        if (result.IsSuccess) _lastPublishedFingerprint = fingerprint;

        if (result.RequiresReauth)
        {
            Volatile.Write(ref _rejectedToken, token);
            Volatile.Write(ref _stoppedForReauth, true);
            try { ReauthRequired?.Invoke(); } catch { /* never let a handler break the pump */ }
        }

        Raise(result);
        return result;
    }

    private void Raise(StreamStatePublishResult result)
    {
        try { PublishCompleted?.Invoke(result); } catch { /* never let a handler break the pump */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _state.PropertyChanged -= OnStateChanged;
        _state.CargoChanged -= MarkDirty;
        if (_sources.Ranks is { } ranks) ranks.Changed -= MarkDirty;
        if (_sources.Exobiology is { } exo) exo.Changed -= MarkDirty;
        if (_sources.Mining is { } mining) mining.Changed -= MarkDirty;
        if (_sources.Missions is { } missions) missions.Changed -= MarkDirty;

        _cts.Cancel();
        try { _pump.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { /* cancellation */ }

        _cts.Dispose();
        _dirty.Dispose();
    }
}
