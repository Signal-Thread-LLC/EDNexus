using System.Collections.Generic;
using System.Linq;
using EDNexus.Core.Journal;
using EDNexus.Core.Ranks;
using Xunit;

namespace EDNexus.Tests.Ranks;

public class RankTrackerTests
{
    private static (JournalEventBus Bus, RankTracker Tracker) NewTracker()
    {
        var bus = new JournalEventBus();
        return (bus, new RankTracker(bus));
    }

    private static void Publish(JournalEventBus bus, string json, bool historical = false)
    {
        Assert.True(JournalEntry.TryParse(json, historical, out var entry));
        bus.Publish(entry);
    }

    /// <summary>A full Rank snapshot, as the game writes it at session start.</summary>
    private static string Rank(int combat = 0, int trade = 0, int explore = 0, int exo = 0, int soldier = 0) =>
        $$"""
        { "timestamp": "2026-08-17T12:00:00Z", "event": "Rank", "Combat": {{combat}}, "Trade": {{trade}},
          "Explore": {{explore}}, "Soldier": {{soldier}}, "Exobiologist": {{exo}}, "Empire": 3,
          "Federation": 5, "CQC": 0 }
        """;

    private static string Progress(int combat = 0, int trade = 0, int explore = 0, int exo = 0, int soldier = 0) =>
        $$"""
        { "timestamp": "2026-08-17T12:00:00Z", "event": "Progress", "Combat": {{combat}}, "Trade": {{trade}},
          "Explore": {{explore}}, "Soldier": {{soldier}}, "Exobiologist": {{exo}}, "Empire": 44,
          "Federation": 12, "CQC": 0 }
        """;

    [Fact]
    public void Rank_and_progress_snapshots_give_each_ladder_its_name_and_percent()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, Rank(combat: 6, trade: 4, explore: 2, exo: 3, soldier: 5));
        Publish(bus, Progress(combat: 37, trade: 90, explore: 5, exo: 61, soldier: 20));

        Assert.True(tracker.HasData);
        Assert.Equal("Dangerous", tracker[RankKind.Combat].Name);
        Assert.Equal(37, tracker[RankKind.Combat].Percent);
        Assert.Equal("Merchant", tracker[RankKind.Trade].Name);
        Assert.Equal("Scout", tracker[RankKind.Explore].Name);
        Assert.Equal("Collector", tracker[RankKind.Exobiologist].Name);
        // The journal field is "Soldier" but the rank is shown as Mercenary.
        Assert.Equal("Mercenary", tracker[RankKind.Mercenary].Label);
        Assert.Equal("Warrior", tracker[RankKind.Mercenary].Name);
    }

    [Fact]
    public void Every_tracked_ladder_is_reported_even_before_any_event_arrives()
    {
        var (_, tracker) = NewTracker();

        var all = tracker.All;

        Assert.False(tracker.HasData);
        Assert.Equal(5, all.Count);
        Assert.Equal(
            new[] { "Combat", "Trade", "Explorer", "Exobiologist", "Mercenary" },
            all.Select(r => r.Label));
        Assert.All(all, r => Assert.Equal(0, r.Index));
    }

    [Fact]
    public void Elite_tiers_are_named_and_the_top_of_the_ladder_is_flagged_maxed()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, Rank(combat: 8, trade: 10, explore: 13));

        Assert.Equal("Elite", tracker[RankKind.Combat].Name);
        Assert.True(tracker[RankKind.Combat].IsElite);
        Assert.False(tracker[RankKind.Combat].IsMaxed);

        Assert.Equal("Elite II", tracker[RankKind.Trade].Name);

        Assert.Equal("Elite V", tracker[RankKind.Explore].Name);
        Assert.True(tracker[RankKind.Explore].IsMaxed);
    }

    [Fact]
    public void A_promotion_raises_the_rank_and_restarts_the_bar()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Rank(combat: 6));
        Publish(bus, Progress(combat: 98));

        Publish(bus, """
            { "timestamp": "2026-08-17T12:05:00Z", "event": "Promotion", "Combat": 7 }
            """);

        var combat = tracker[RankKind.Combat];
        Assert.Equal("Deadly", combat.Name);
        // The game does not reliably re-send Progress after a promotion, so the bar resets here.
        Assert.Equal(0, combat.Percent);
    }

    [Fact]
    public void A_promotion_notifies_only_the_rank_that_moved()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Rank(combat: 6, trade: 4));

        var fired = new List<RankProgress>();
        tracker.Promoted += fired.Add;

        Publish(bus, """
            { "timestamp": "2026-08-17T12:05:00Z", "event": "Promotion", "Trade": 5 }
            """);

        var promotion = Assert.Single(fired);
        Assert.Equal(RankKind.Trade, promotion.Kind);
        Assert.Equal("Broker", promotion.Name);
        // Combat was not in the payload, so it must be left exactly where it was.
        Assert.Equal(6, tracker[RankKind.Combat].Index);
    }

    [Fact]
    public void A_replayed_promotion_updates_state_without_firing_a_callout()
    {
        var (bus, tracker) = NewTracker();

        var fired = new List<RankProgress>();
        tracker.Promoted += fired.Add;

        Publish(bus, """
            { "timestamp": "2026-08-10T09:00:00Z", "event": "Promotion", "Exobiologist": 4 }
            """, historical: true);

        Assert.Empty(fired);
        Assert.Equal("Cataloguer", tracker[RankKind.Exobiologist].Name);
    }

    [Fact]
    public void Ranks_missing_from_an_older_journal_keep_their_last_known_value()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Rank(combat: 5, soldier: 3));

        // A pre-Odyssey style payload with no Soldier/Exobiologist fields at all.
        Publish(bus, """
            { "timestamp": "2026-08-17T12:10:00Z", "event": "Rank", "Combat": 6, "Trade": 1,
              "Explore": 2, "Empire": 3, "Federation": 5, "CQC": 0 }
            """);

        Assert.Equal(6, tracker[RankKind.Combat].Index);
        Assert.Equal(3, tracker[RankKind.Mercenary].Index);
    }

    [Fact]
    public void An_unknown_rank_index_is_reported_rather_than_pinned_to_the_top()
    {
        var (bus, tracker) = NewTracker();

        // If Frontier adds a tier, inventing "Elite V" for it would be a lie.
        Publish(bus, Rank(combat: 14));

        Assert.Equal("Rank 14", tracker[RankKind.Combat].Name);
    }

    [Fact]
    public void A_progress_percent_outside_the_expected_range_is_clamped()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, Progress(combat: 240, trade: -5));

        Assert.Equal(100, tracker[RankKind.Combat].Percent);
        Assert.Equal(0, tracker[RankKind.Trade].Percent);
    }

    [Fact]
    public void Malformed_and_unrelated_events_do_not_throw_or_change_anything()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Rank(combat: 4));

        Publish(bus, """
            { "timestamp": "2026-08-17T12:00:00Z", "event": "Rank", "Combat": "Expert" }
            """);
        Publish(bus, """
            { "timestamp": "2026-08-17T12:00:00Z", "event": "Progress", "Combat": null }
            """);
        Publish(bus, """
            { "timestamp": "2026-08-17T12:00:00Z", "event": "FSDJump", "StarSystem": "Sol" }
            """);

        Assert.Equal(4, tracker[RankKind.Combat].Index);
        Assert.Equal("Expert", tracker[RankKind.Combat].Name);
    }

    [Fact]
    public void Changed_fires_only_when_a_value_actually_moves()
    {
        var (bus, tracker) = NewTracker();
        var ticks = 0;
        tracker.Changed += () => ticks++;

        Publish(bus, Rank(combat: 4));
        Publish(bus, Rank(combat: 4));   // identical snapshot — the dashboard should not churn

        Assert.Equal(1, ticks);
    }
}
