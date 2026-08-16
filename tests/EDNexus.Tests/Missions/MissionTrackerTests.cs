using System.Linq;
using EDNexus.Core.Journal;
using EDNexus.Core.Missions;
using Xunit;

namespace EDNexus.Tests.Missions;

public class MissionTrackerTests
{
    private static (JournalEventBus Bus, MissionTracker Tracker) NewTracker()
    {
        var bus = new JournalEventBus();
        return (bus, new MissionTracker(bus));
    }

    private static void Publish(JournalEventBus bus, string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var entry));
        bus.Publish(entry);
    }

    /// <summary>A massacre mission as the game writes it, with the fields the card depends on.</summary>
    private static string Massacre(
        long id, string giver, string target = "Kremata Blue Society", int kills = 42,
        long reward = 1_000_000, string station = "Vonarburg Terminal", string system = "Kremata") =>
        $$"""
        { "timestamp": "2026-08-16T12:00:00Z", "event": "MissionAccepted",
          "Faction": "{{giver}}", "Name": "Mission_MassacreWing",
          "LocalisedName": "Kill {{kills}} Pirates", "TargetType": "$MissionUtil_FactionTag_Pirate;",
          "TargetType_Localised": "Pirates", "TargetFaction": "{{target}}", "KillCount": {{kills}},
          "DestinationSystem": "{{system}}", "DestinationStation": "{{station}}",
          "Expiry": "2026-08-23T12:00:00Z", "Wing": true, "Influence": "++", "Reputation": "++",
          "Reward": {{reward}}, "MissionID": {{id}} }
        """;

    [Fact]
    public void An_accepted_mission_is_held_with_the_detail_the_card_needs()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, Massacre(1, "Union of Kremata Front"));

        var mission = Assert.Single(tracker.Active);
        Assert.Equal(1, mission.MissionId);
        Assert.Equal("Kill 42 Pirates", mission.Title);
        Assert.Equal("Union of Kremata Front", mission.GiverFaction);
        Assert.Equal("Kremata Blue Society", mission.TargetFaction);
        Assert.Equal("Pirates", mission.TargetType);      // the localised form, not the $tag;
        Assert.Equal(42, mission.KillCount);
        Assert.Equal(1_000_000, mission.Reward);
        Assert.True(mission.IsWing);
        Assert.True(mission.IsKillMission);
        Assert.False(mission.ReadyToTurnIn);
    }

    [Theory]
    [InlineData("MissionCompleted")]
    [InlineData("MissionAbandoned")]
    [InlineData("MissionFailed")]
    public void A_mission_that_ends_any_way_stops_being_held(string endEvent)
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Massacre(1, "Union of Kremata Front"));

        Publish(bus, $$"""{ "timestamp": "2026-08-16T13:00:00Z", "event": "{{endEvent}}", "MissionID": 1 }""");

        Assert.Empty(tracker.Active);
        Assert.Equal(0, tracker.ActiveCount);
    }

    [Fact]
    public void Missions_against_the_same_target_form_one_stack()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Massacre(1, "Union of Kremata Front", kills: 42, reward: 1_000_000));
        Publish(bus, Massacre(2, "Kremata Purple Council", kills: 30, reward: 800_000));
        Publish(bus, Massacre(3, "Kremata Jet Comms", target: "Some Other Gang", kills: 12));

        var stack = tracker.Stacks.First();

        Assert.Equal("Kremata Blue Society", stack.TargetFaction);
        Assert.Equal("Pirates", stack.TargetType);
        Assert.Equal(2, stack.Missions.Count);
        Assert.Equal(new[] { "Kremata Purple Council", "Union of Kremata Front" }, stack.GiverFactions);
        Assert.Equal(1_800_000, stack.TotalReward);
        Assert.Equal(2, tracker.Stacks.Count);   // the other target is its own stack
    }

    [Fact]
    public void A_stack_reports_the_kills_it_actually_takes_not_the_sum_of_its_missions()
    {
        // The whole point of stacking: one kill counts for every mission at once, so clearing the
        // stack costs the largest mission, not the total.
        var (bus, tracker) = NewTracker();
        Publish(bus, Massacre(1, "A", kills: 42));
        Publish(bus, Massacre(2, "B", kills: 30));
        Publish(bus, Massacre(3, "C", kills: 18));

        var stack = Assert.Single(tracker.Stacks);

        Assert.Equal(90, stack.TotalKills);    // what the missions add up to
        Assert.Equal(42, stack.KillsToClear);  // what you actually have to fly
    }

    [Fact]
    public void A_mission_with_no_target_faction_is_not_stackable()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, """
        { "timestamp": "2026-08-16T12:00:00Z", "event": "MissionAccepted", "MissionID": 7,
          "Faction": "Union of Kremata Front", "LocalisedName": "Deliver 30 Beer", "Reward": 50000,
          "DestinationSystem": "Kremata", "DestinationStation": "Vonarburg Terminal" }
        """);

        Assert.Single(tracker.Active);
        Assert.Empty(tracker.Stacks);
    }

    [Fact]
    public void A_redirect_marks_the_mission_ready_and_moves_its_hand_in()
    {
        // A redirect is the only signal the journal gives that a kill mission's objective is done.
        var (bus, tracker) = NewTracker();
        Publish(bus, Massacre(1, "Union of Kremata Front"));

        Publish(bus, """
        { "timestamp": "2026-08-16T14:00:00Z", "event": "MissionRedirected", "MissionID": 1,
          "NewDestinationSystem": "Kremata", "NewDestinationStation": "Bloch Enterprise" }
        """);

        var mission = Assert.Single(tracker.Active);
        Assert.True(mission.ReadyToTurnIn);
        Assert.Equal("Bloch Enterprise", mission.DestinationStation);
    }

    [Fact]
    public void A_redirect_for_a_mission_we_never_saw_accepted_is_ignored()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, """
        { "timestamp": "2026-08-16T14:00:00Z", "event": "MissionRedirected", "MissionID": 99,
          "NewDestinationStation": "Bloch Enterprise" }
        """);

        Assert.Empty(tracker.Active);
    }

    [Fact]
    public void Hand_ins_group_by_station_with_the_ready_ones_first()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Massacre(1, "A", station: "Vonarburg Terminal"));
        Publish(bus, Massacre(2, "B", station: "Vonarburg Terminal"));
        Publish(bus, Massacre(3, "C", station: "Bloch Enterprise"));
        Publish(bus, """
        { "timestamp": "2026-08-16T14:00:00Z", "event": "MissionRedirected", "MissionID": 3,
          "NewDestinationSystem": "Kremata", "NewDestinationStation": "Bloch Enterprise" }
        """);

        var groups = tracker.TurnIns;

        Assert.Equal("Bloch Enterprise", groups[0].Station);   // wholly ready, so it sorts first
        Assert.True(groups[0].AllReady);
        Assert.Equal("Vonarburg Terminal", groups[1].Station);
        Assert.Equal(2, groups[1].Missions.Count);
        Assert.False(groups[1].AllReady);
    }

    [Fact]
    public void Missions_group_by_the_faction_that_issued_them_for_the_bgs_view()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Massacre(1, "Union of Kremata Front"));
        Publish(bus, Massacre(2, "Union of Kremata Front"));
        Publish(bus, Massacre(3, "Kremata Purple Council"));

        var byGiver = tracker.ByGiver;

        Assert.Equal("Union of Kremata Front", byGiver[0].Faction);
        Assert.Equal(2, byGiver[0].Missions.Count);
        Assert.Equal("Kremata Purple Council", byGiver[1].Faction);
    }

    [Fact]
    public void The_startup_snapshot_prunes_missions_that_ended_while_we_were_closed()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Massacre(1, "A"));
        Publish(bus, Massacre(2, "B"));

        Publish(bus, """
        { "timestamp": "2026-08-16T15:00:00Z", "event": "Missions",
          "Active": [ { "MissionID": 2, "Name": "Mission_MassacreWing", "Expires": 86400 } ],
          "Failed": [], "Complete": [] }
        """);

        Assert.Equal(2, Assert.Single(tracker.Active).MissionId);
    }

    [Fact]
    public void The_startup_snapshot_never_invents_a_mission_it_has_no_detail_for()
    {
        // The snapshot carries ids and little else — no faction, target or reward — so adding from
        // it would put a blank line on the card.
        var (bus, tracker) = NewTracker();

        Publish(bus, """
        { "timestamp": "2026-08-16T15:00:00Z", "event": "Missions",
          "Active": [ { "MissionID": 555, "Name": "Mission_MassacreWing", "Expires": 86400 } ],
          "Failed": [], "Complete": [] }
        """);

        Assert.Empty(tracker.Active);
    }

    [Fact]
    public void Bounties_tally_per_victim_faction_as_a_running_count_for_a_stack()
    {
        var (bus, tracker) = NewTracker();

        for (var i = 0; i < 3; i++)
            Publish(bus, """
            { "timestamp": "2026-08-16T16:00:00Z", "event": "Bounty", "VictimFaction": "Kremata Blue Society",
              "TotalReward": 120000 }
            """);
        Publish(bus, """
        { "timestamp": "2026-08-16T16:05:00Z", "event": "Bounty", "VictimFaction": "Some Other Gang" }
        """);

        Assert.Equal(3, tracker.KillsLoggedFor("Kremata Blue Society"));
        Assert.Equal(1, tracker.KillsLoggedFor("Some Other Gang"));
        Assert.Equal(0, tracker.KillsLoggedFor("Never Shot At"));
        Assert.Equal(0, tracker.KillsLoggedFor(null));
    }

    [Fact]
    public void Clearing_forgets_missions_and_kill_tallies()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Massacre(1, "A"));
        Publish(bus, """{ "timestamp": "2026-08-16T16:00:00Z", "event": "Bounty", "VictimFaction": "X" }""");

        tracker.Clear();

        Assert.Empty(tracker.Active);
        Assert.Equal(0, tracker.KillsLoggedFor("X"));
    }

    [Fact]
    public void Re_accepting_the_same_mission_id_replaces_rather_than_duplicates()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Massacre(1, "A", kills: 42));
        Publish(bus, Massacre(1, "A", kills: 50));

        Assert.Equal(50, Assert.Single(tracker.Active).KillCount);
    }
}
