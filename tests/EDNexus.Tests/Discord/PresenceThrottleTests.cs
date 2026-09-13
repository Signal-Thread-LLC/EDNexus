using EDNexus.Core.Discord;
using Xunit;

namespace EDNexus.Tests.Discord;

public class PresenceThrottleTests
{
    [Fact]
    public void First_acquire_always_succeeds()
    {
        var throttle = new PresenceThrottle(TimeSpan.FromSeconds(15), () => DateTimeOffset.UnixEpoch);

        Assert.True(throttle.TryAcquire());
    }

    [Fact]
    public void Second_acquire_within_the_window_is_refused()
    {
        var now = DateTimeOffset.UnixEpoch;
        var throttle = new PresenceThrottle(TimeSpan.FromSeconds(15), () => now);

        Assert.True(throttle.TryAcquire());

        now = now.AddSeconds(5);   // still inside the 15s window
        Assert.False(throttle.TryAcquire());
    }

    [Fact]
    public void Acquire_succeeds_again_once_the_window_has_fully_elapsed()
    {
        var now = DateTimeOffset.UnixEpoch;
        var throttle = new PresenceThrottle(TimeSpan.FromSeconds(15), () => now);

        Assert.True(throttle.TryAcquire());

        now = now.AddSeconds(15);   // exactly at the boundary
        Assert.True(throttle.TryAcquire());
    }

    [Fact]
    public void Time_until_next_send_is_zero_before_any_send_has_happened()
    {
        var throttle = new PresenceThrottle(TimeSpan.FromSeconds(15), () => DateTimeOffset.UnixEpoch);

        Assert.Equal(TimeSpan.Zero, throttle.TimeUntilNextSend());
    }

    [Fact]
    public void Time_until_next_send_reports_the_remaining_wait()
    {
        var now = DateTimeOffset.UnixEpoch;
        var throttle = new PresenceThrottle(TimeSpan.FromSeconds(15), () => now);
        throttle.TryAcquire();

        now = now.AddSeconds(4);

        Assert.Equal(TimeSpan.FromSeconds(11), throttle.TimeUntilNextSend());
    }

    [Fact]
    public void Time_until_next_send_is_zero_once_the_window_has_elapsed()
    {
        var now = DateTimeOffset.UnixEpoch;
        var throttle = new PresenceThrottle(TimeSpan.FromSeconds(15), () => now);
        throttle.TryAcquire();

        now = now.AddSeconds(20);

        Assert.Equal(TimeSpan.Zero, throttle.TimeUntilNextSend());
    }

    [Fact]
    public void A_refused_acquire_does_not_reset_the_window()
    {
        var now = DateTimeOffset.UnixEpoch;
        var throttle = new PresenceThrottle(TimeSpan.FromSeconds(15), () => now);
        throttle.TryAcquire();

        now = now.AddSeconds(5);
        Assert.False(throttle.TryAcquire());   // refused; must not move the last-sent marker

        now = now.AddSeconds(10);   // 15s from the original send, not from the refusal
        Assert.True(throttle.TryAcquire());
    }
}
