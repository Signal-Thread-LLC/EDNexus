namespace EDNexus.Core.Discord;

/// <summary>
/// Rate-limits Discord presence updates to at most one per <see cref="MinInterval"/>, per Discord's
/// Rich Presence guidance. Pure decision logic — no timers, no I/O — so
/// <see cref="DiscordPresenceService"/>'s scheduling can be exercised deterministically in tests via
/// an injected clock instead of real elapsed wall-clock time.
/// </summary>
public sealed class PresenceThrottle
{
    private readonly TimeSpan _minInterval;
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset? _lastSentAt;

    public PresenceThrottle(TimeSpan minInterval, Func<DateTimeOffset>? clock = null)
    {
        _minInterval = minInterval;
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    public TimeSpan MinInterval => _minInterval;

    /// <summary>
    /// If a send is allowed right now, records this moment as the last send and returns true.
    /// Otherwise leaves state untouched and returns false — call <see cref="TimeUntilNextSend"/> to
    /// find out how long to wait before trying again.
    /// </summary>
    public bool TryAcquire()
    {
        var now = _clock();
        if (_lastSentAt is { } last && now - last < _minInterval) return false;
        _lastSentAt = now;
        return true;
    }

    /// <summary>How long until a send would be allowed. <see cref="TimeSpan.Zero"/> means "now".</summary>
    public TimeSpan TimeUntilNextSend()
    {
        if (_lastSentAt is not { } last) return TimeSpan.Zero;
        var elapsed = _clock() - last;
        return elapsed >= _minInterval ? TimeSpan.Zero : _minInterval - elapsed;
    }
}
