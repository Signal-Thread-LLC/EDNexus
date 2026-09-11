using System.Linq;
using EDNexus.Core.CommunityGoals;
using EDNexus.Core.Journal;
using Xunit;

namespace EDNexus.Tests.CommunityGoals;

public class CommunityGoalTrackerTests
{
    private static (JournalEventBus Bus, CommunityGoalTracker Tracker) NewTracker()
    {
        var bus = new JournalEventBus();
        return (bus, new CommunityGoalTracker(bus));
    }

    private static void Publish(JournalEventBus bus, string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var entry));
        bus.Publish(entry);
    }

    /// <summary>A CommunityGoal snapshot as the game writes it, with the fields the card depends on.</summary>
    private static string Snapshot(
        long cgid = 12345, string title = "Battle for the Core", string system = "Colonia",
        string station = "Someone's Platform", long total = 1_500_000, long mine = 25_000,
        string tierReached = "Tier 3", bool includeOptional = true) =>
        $$"""
        { "timestamp": "2026-08-16T12:00:00Z", "event": "CommunityGoal",
          "CurrentGoals": [
            {
              "CGID": {{cgid}}, "Title": "{{title}}", "SystemName": "{{system}}",
              "MarketName": "{{station}}", "Expiry": "2026-08-23T12:00:00Z", "IsComplete": false,
              "CurrentTotal": {{total}}, "PlayerContribution": {{mine}}
              {{(includeOptional ? $$""", "NumContributors": 3000, "TopRankSize": 10, "TopTier": { "Name": "Tier 5", "Bonus": "5000000" }, "TierReached": "{{tierReached}}", "PlayerInTopRank": false, "PlayerPercentileBand": 25""" : "")}}
            }
          ]
        }
        """;

    [Fact]
    public void A_snapshot_is_tracked_with_the_detail_the_card_needs()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, Snapshot());

        var goal = Assert.Single(tracker.Active);
        Assert.Equal(12345, goal.CGID);
        Assert.Equal("Battle for the Core", goal.Title);
        Assert.Equal("Colonia", goal.System);
        Assert.Equal("Someone's Platform", goal.Station);
        Assert.Equal(1_500_000, goal.CurrentTotal);
        Assert.Equal(25_000, goal.PlayerContribution);
        Assert.Equal(3000, goal.NumContributors);
        Assert.Equal(10, goal.TopRankSize);
        Assert.Equal("Tier 5", goal.TopTierName);
        Assert.Equal("5000000", goal.TopTierBonus);
        Assert.Equal("Tier 3", goal.TierReached);
        Assert.False(goal.PlayerInTopRank);
        Assert.Equal(25, goal.PlayerPercentileBand);
        Assert.False(goal.Joined);
        Assert.False(goal.Rewarded);
        Assert.NotNull(goal.Expiry);
    }

    [Fact]
    public void A_snapshot_with_optional_fields_missing_does_not_throw_and_leaves_them_null()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, Snapshot(includeOptional: false));

        var goal = Assert.Single(tracker.Active);
        Assert.Null(goal.NumContributors);
        Assert.Null(goal.TopRankSize);
        Assert.Null(goal.TopTierName);
        Assert.Null(goal.TopTierBonus);
        Assert.Null(goal.TierReached);
        Assert.Null(goal.PlayerInTopRank);
        Assert.Null(goal.PlayerPercentileBand);
    }

    [Fact]
    public void Joining_marks_a_known_goal_as_joined()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot());

        Publish(bus, """
        { "timestamp": "2026-08-16T13:00:00Z", "event": "CommunityGoalJoin",
          "CGID": 12345, "Name": "Battle for the Core", "System": "Colonia" }
        """);

        Assert.True(Assert.Single(tracker.Active).Joined);
    }

    [Fact]
    public void Joining_a_goal_never_seen_in_a_snapshot_creates_a_bare_entry()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, """
        { "timestamp": "2026-08-16T13:00:00Z", "event": "CommunityGoalJoin",
          "CGID": 999, "Name": "Some New Goal", "System": "Deciat" }
        """);

        var goal = Assert.Single(tracker.Active);
        Assert.Equal(999, goal.CGID);
        Assert.Equal("Some New Goal", goal.Title);
        Assert.Equal("Deciat", goal.System);
        Assert.True(goal.Joined);
    }

    [Fact]
    public void A_later_snapshot_preserves_join_state_the_snapshot_itself_never_carries()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot());
        Publish(bus, """
        { "timestamp": "2026-08-16T13:00:00Z", "event": "CommunityGoalJoin",
          "CGID": 12345, "Name": "Battle for the Core", "System": "Colonia" }
        """);

        Publish(bus, Snapshot(mine: 40_000));

        var goal = Assert.Single(tracker.Active);
        Assert.True(goal.Joined);
        Assert.Equal(40_000, goal.PlayerContribution);
    }

    [Fact]
    public void Reupserting_the_same_cgid_replaces_rather_than_duplicates()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot(mine: 25_000));
        Publish(bus, Snapshot(mine: 60_000));

        Assert.Equal(60_000, Assert.Single(tracker.Active).PlayerContribution);
    }

    [Fact]
    public void A_second_goal_in_the_same_snapshot_is_tracked_separately()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, """
        { "timestamp": "2026-08-16T12:00:00Z", "event": "CommunityGoal",
          "CurrentGoals": [
            { "CGID": 1, "Title": "Goal One", "SystemName": "Sol", "CurrentTotal": 100, "PlayerContribution": 0 },
            { "CGID": 2, "Title": "Goal Two", "SystemName": "Deciat", "CurrentTotal": 200, "PlayerContribution": 5 }
          ]
        }
        """);

        Assert.Equal(2, tracker.Active.Count);
        Assert.Contains(tracker.Active, g => g.CGID == 1 && g.Title == "Goal One");
        Assert.Contains(tracker.Active, g => g.CGID == 2 && g.Title == "Goal Two");
    }

    [Fact]
    public void Discarding_removes_the_goal_entirely()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot());

        Publish(bus, """
        { "timestamp": "2026-08-16T14:00:00Z", "event": "CommunityGoalDiscard",
          "CGID": 12345, "Name": "Battle for the Core", "System": "Colonia" }
        """);

        Assert.Empty(tracker.Active);
    }

    [Fact]
    public void Discarding_an_unknown_goal_is_a_no_op()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, """
        { "timestamp": "2026-08-16T14:00:00Z", "event": "CommunityGoalDiscard",
          "CGID": 404, "Name": "Ghost Goal", "System": "Nowhere" }
        """);

        Assert.Empty(tracker.Active);
    }

    [Fact]
    public void A_reward_marks_the_goal_complete_and_keeps_it_visible()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot());

        Publish(bus, """
        { "timestamp": "2026-08-16T15:00:00Z", "event": "CommunityGoalReward",
          "CGID": 12345, "Name": "Battle for the Core", "System": "Colonia", "Reward": 500000, "Bonus": 25000 }
        """);

        var goal = Assert.Single(tracker.Active);
        Assert.True(goal.Rewarded);
        Assert.True(goal.IsComplete);
        Assert.Equal(500000, goal.RewardAmount);
        Assert.Equal(25000, goal.RewardBonus);
    }

    [Fact]
    public void A_reward_with_no_bonus_field_is_recorded_without_one()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot());

        Publish(bus, """
        { "timestamp": "2026-08-16T15:00:00Z", "event": "CommunityGoalReward",
          "CGID": 12345, "Name": "Battle for the Core", "System": "Colonia", "Reward": 500000 }
        """);

        var goal = Assert.Single(tracker.Active);
        Assert.Equal(500000, goal.RewardAmount);
        Assert.Null(goal.RewardBonus);
    }

    [Fact]
    public void A_reward_for_a_goal_never_seen_creates_a_rewarded_entry()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, """
        { "timestamp": "2026-08-16T15:00:00Z", "event": "CommunityGoalReward",
          "CGID": 777, "Name": "Unseen Goal", "System": "Maia", "Reward": 10000 }
        """);

        var goal = Assert.Single(tracker.Active);
        Assert.True(goal.Rewarded);
        Assert.True(goal.IsComplete);
        Assert.Equal(10000, goal.RewardAmount);
    }

    [Fact]
    public void Clearing_forgets_every_goal()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot());

        tracker.Clear();

        Assert.Empty(tracker.Active);
    }

    [Fact]
    public void Unknown_events_and_malformed_snapshots_never_throw()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, """{ "timestamp": "2026-08-16T12:00:00Z", "event": "Music" }""");
        Publish(bus, """{ "timestamp": "2026-08-16T12:00:00Z", "event": "CommunityGoal" }""");
        Publish(bus, """{ "timestamp": "2026-08-16T12:00:00Z", "event": "CommunityGoal", "CurrentGoals": [] }""");
        Publish(bus, """{ "timestamp": "2026-08-16T12:00:00Z", "event": "CommunityGoal", "CurrentGoals": [ { "Title": "No id" } ] }""");

        Assert.Empty(tracker.Active);
    }
}
