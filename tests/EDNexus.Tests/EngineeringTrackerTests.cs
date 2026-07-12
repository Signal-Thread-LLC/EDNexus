using System.Linq;
using EDNexus.Core.Engineering;
using EDNexus.Core.Journal;
using EDNexus.Core.State;
using Xunit;

namespace EDNexus.Tests;

public class EngineeringTrackerTests
{
    private static (JournalEventBus bus, CommanderState state, EngineeringTracker tracker) NewTracker()
    {
        var bus = new JournalEventBus();
        var state = new CommanderState();
        var tracker = new EngineeringTracker(bus);
        return (bus, state, tracker);
    }

    private static void Publish(JournalEventBus bus, string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var entry), "sample JSON failed to parse");
        bus.Publish(entry);
    }

    [Fact]
    public void Snapshot_populates_unlocked_ranks_and_ignores_known()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-01T00:00:00Z", "event":"EngineerProgress", "Engineers":[
          { "Engineer":"Felicity Farseer", "EngineerID":300100, "Progress":"Unlocked", "RankProgress":0, "Rank":5 },
          { "Engineer":"Elvira Martuuk", "EngineerID":300160, "Progress":"Known" }
        ] }
        """);

        Assert.Equal(5, tracker.UnlockedRanks["Felicity Farseer"]);
        Assert.False(tracker.UnlockedRanks.ContainsKey("Elvira Martuuk"));   // "Known" is not usable yet
    }

    [Fact]
    public void Single_engineer_update_form_is_applied()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-01T00:00:00Z", "event":"EngineerProgress",
          "Engineer":"Professor Palin", "EngineerID":300220, "Progress":"Unlocked", "Rank":3, "RankProgress":0 }
        """);

        Assert.Equal(3, tracker.UnlockedRanks["Professor Palin"]);
    }

    [Fact]
    public void BuildPlan_prefers_unlocked_engineer_and_joins_held_materials()
    {
        var (bus, state, tracker) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-01T00:00:00Z", "event":"EngineerProgress", "Engineers":[
          { "Engineer":"Felicity Farseer", "EngineerID":300100, "Progress":"Unlocked", "RankProgress":0, "Rank":5 }
        ] }
        """);

        // Seed the material inventories the same way StateTracker keys them (journal symbols).
        state.Materials.Raw["arsenic"] = 7;
        state.Materials.Encoded["dataminedwake"] = 3;
        // chemicalmanipulators deliberately absent → should report Held 0 / not covered.

        var plan = tracker.BuildPlan("fsd_increased_range", 5, state);

        Assert.NotNull(plan);
        Assert.Equal("Felicity Farseer", plan!.Engineer!.Name);
        Assert.True(plan.EngineerUnlocked);
        Assert.Equal(3, plan.Materials.Count);

        var arsenic = Assert.Single(plan.Materials, m => m.Symbol == "arsenic");
        Assert.Equal(7, arsenic.Held);
        Assert.True(arsenic.HasAny);
        Assert.Equal("Raw", arsenic.Category);

        var manip = Assert.Single(plan.Materials, m => m.Symbol == "chemicalmanipulators");
        Assert.Equal(0, manip.Held);
        Assert.False(manip.HasAny);
    }

    [Fact]
    public void BuildPlan_returns_null_for_unknown_blueprint_or_grade()
    {
        var (_, state, tracker) = NewTracker();
        Assert.Null(tracker.BuildPlan("does_not_exist", 5, state));
        Assert.Null(tracker.BuildPlan("fsd_increased_range", 9, state));
    }

    [Fact]
    public void BuildPlan_falls_back_to_first_engineer_when_none_unlocked()
    {
        var (_, state, tracker) = NewTracker();

        var plan = tracker.BuildPlan("fsd_increased_range", 5, state);

        Assert.NotNull(plan);
        Assert.NotNull(plan!.Engineer);          // still suggests where to go
        Assert.False(plan.EngineerUnlocked);     // but flags it as not yet unlocked
    }
}
