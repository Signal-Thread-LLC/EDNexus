using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EDNexus.Core.Dev;
using EDNexus.Core.Journal;
using EDNexus.Core.Twitch;
using Xunit;

namespace EDNexus.Tests.Dev;

public class TwitchSampleSourceTests
{
    [Fact]
    public void Card_key_and_display_name_identify_the_twitch_card()
    {
        var source = new TwitchSampleSource();

        Assert.Equal("twitch", source.CardKey);
        Assert.Equal("Twitch Integration", source.DisplayName);
    }

    [Fact]
    public void Every_sample_starts_with_an_oauth_redirect_event()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var source = new TwitchSampleSource();
            var lines = source.Sample(new Random(seed));

            Assert.NotEmpty(lines);
            var first = JsonDocument.Parse(lines[0]).RootElement;
            Assert.Equal("TwitchOAuthRedirect", first.GetProperty("event").GetString());
        }
    }

    [Fact]
    public void A_failed_redirect_short_circuits_the_rest_of_the_sequence()
    {
        // Find a seed that produces an unauthorized redirect (~15% chance per Sample()).
        for (var seed = 0; seed < 200; seed++)
        {
            var lines = new TwitchSampleSource().Sample(new Random(seed));
            var first = JsonDocument.Parse(lines[0]).RootElement;
            if (first.GetProperty("Success").GetBoolean()) continue;

            Assert.Single(lines);
            Assert.True(first.TryGetProperty("ErrorReason", out _));
            return;
        }

        Assert.Fail("Expected at least one failed OAuth redirect across 200 seeds.");
    }

    [Fact]
    public void A_successful_redirect_is_followed_by_a_token_renewal_and_a_game_event_burst()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var lines = new TwitchSampleSource().Sample(new Random(seed));
            var first = JsonDocument.Parse(lines[0]).RootElement;
            if (!first.GetProperty("Success").GetBoolean()) continue;

            var events = lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("event").GetString()).ToList();

            Assert.Equal("TwitchTokenRenewed", events[1]);
            // A burst of ordinary game events (system jumps / exobiology scans) follows, so the
            // state-update serializer and its rate-limiter get exercised as if mid-stream.
            var burst = events.Skip(2).TakeWhile(e => e is "FSDJump" or "ScanOrganic").ToList();
            Assert.InRange(burst.Count, 3, 7);
            return;
        }

        Assert.Fail("Expected at least one successful OAuth redirect across 50 seeds.");
    }

    [Fact]
    public void Throttle_and_expiry_events_appear_across_enough_samples()
    {
        var sawThrottle = false;
        var sawExpiry = false;

        for (var seed = 0; seed < 300 && !(sawThrottle && sawExpiry); seed++)
        {
            var lines = new TwitchSampleSource().Sample(new Random(seed));
            var events = lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("event").GetString()).ToList();
            sawThrottle |= events.Contains("TwitchApiThrottled");
            sawExpiry |= events.Contains("TwitchTokenExpired");
        }

        Assert.True(sawThrottle, "Expected at least one TwitchApiThrottled event across 300 seeds.");
        Assert.True(sawExpiry, "Expected at least one TwitchTokenExpired event across 300 seeds.");
    }

    [Fact]
    public void A_throttle_event_carries_a_429_status_and_a_retry_after_hint()
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var lines = new TwitchSampleSource().Sample(new Random(seed));
            var throttle = lines
                .Select(l => JsonDocument.Parse(l).RootElement)
                .FirstOrDefault(e => e.GetProperty("event").GetString() == "TwitchApiThrottled");

            if (throttle.ValueKind == JsonValueKind.Undefined) continue;

            Assert.Equal(429, throttle.GetProperty("StatusCode").GetInt32());
            Assert.True(throttle.GetProperty("RetryAfterSeconds").GetInt32() > 0);
            return;
        }

        Assert.Fail("Expected at least one TwitchApiThrottled event across 300 seeds.");
    }

    [Fact]
    public void Developer_mode_routes_twitch_events_through_the_real_bus_without_crashing()
    {
        var dev = new DeveloperMode();
        var bus = new JournalEventBus();
        var seen = new List<string>();
        Exception? handlerError = null;
        bus.HandlerError += (_, ex) => handlerError = ex;
        bus.SubscribeAny(e => seen.Add(e.Event));

        dev.Randomize(bus, new Random(7), cardKey: "twitch");

        Assert.Null(handlerError);
        Assert.Contains("TwitchOAuthRedirect", seen);
    }

    [Fact]
    public void Every_emitted_line_parses_as_a_valid_journal_entry()
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var lines = new TwitchSampleSource().Sample(new Random(seed));
            foreach (var line in lines)
                Assert.True(JournalEntry.TryParse(line, historical: false, out _), $"Failed to parse: {line}");
        }
    }
}
