using System.Linq;
using EDNexus.Core.Engineering;
using EDNexus.Core.Journal;
using EDNexus.Core.State;
using Xunit;

namespace EDNexus.Tests;

public class EngineeringTrackerOdysseyTests
{
    private static (JournalEventBus bus, CommanderState state, EngineeringTracker tracker) NewTracker()
    {
        var bus = new JournalEventBus();
        var state = new CommanderState();
        _ = new StateTracker(bus, state);   // populates SuitSymbol/SuitClass/OnFoot from the same bus, like EngineHost does
        var tracker = new EngineeringTracker(bus);
        return (bus, state, tracker);
    }

    private static void Publish(JournalEventBus bus, string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var entry), "sample JSON failed to parse");
        bus.Publish(entry);
    }

    [Fact]
    public void BuildSuitUpgradePlan_from_grade1_sums_all_four_steps_and_joins_held_stock()
    {
        var (_, state, tracker) = NewTracker();

        // No SuitLoadout seen yet → plan should assume grade 1.
        state.OnFoot.Items["suitschematic"] = 12;
        state.OnFoot.Components["titaniumplating"] = 5;   // short of the 28 total needed (2+5+9+12)

        var plan = tracker.BuildSuitUpgradePlan("dominator", 5, state);

        Assert.NotNull(plan);
        Assert.Equal(1, plan!.FromGrade);
        Assert.Equal(5, plan.ToGrade);
        Assert.Equal(600000 + 2250000 + 4500000 + 7500000, plan.TotalCredits);
        Assert.False(plan.CreditsEstimated);

        var schematic = Assert.Single(plan.Materials, m => m.Symbol == "suitschematic");
        Assert.Equal(1 + 2 + 4 + 5, schematic.Needed);   // cumulative across all 4 steps
        Assert.Equal(12, schematic.Held);
        Assert.True(schematic.Satisfied);

        var plating = Assert.Single(plan.Materials, m => m.Symbol == "titaniumplating");
        Assert.Equal(2 + 5 + 9 + 12, plating.Needed);
        Assert.Equal(5, plating.Held);
        Assert.False(plating.Satisfied);
        Assert.Equal(plating.Needed - 5, plating.Shortfall);
    }

    [Fact]
    public void BuildSuitUpgradePlan_starts_from_the_equipped_suits_current_grade()
    {
        var (bus, state, tracker) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-01T00:00:00Z", "event":"SuitLoadout",
          "SuitID":1, "SuitName":"tacticalsuit_class3", "SuitName_Localised":"Dominator Suit", "LoadoutID":1, "LoadoutName":"A" }
        """);

        var plan = tracker.BuildSuitUpgradePlan("dominator", 5, state);

        Assert.NotNull(plan);
        Assert.Equal(3, plan!.FromGrade);
        // Only the grade-4 and grade-5 steps should count, not grade 2/3.
        Assert.Equal(4500000 + 7500000, plan.TotalCredits);
        var schematic = Assert.Single(plan.Materials, m => m.Symbol == "suitschematic");
        Assert.Equal(4 + 5, schematic.Needed);
    }

    [Fact]
    public void BuildSuitUpgradePlan_ignores_current_grade_of_a_different_equipped_suit()
    {
        var (bus, state, tracker) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-01T00:00:00Z", "event":"SuitLoadout",
          "SuitID":1, "SuitName":"utilitysuit_class4", "SuitName_Localised":"Maverick Suit", "LoadoutID":1, "LoadoutName":"A" }
        """);

        // Pinning the Dominator while wearing a Maverick should still plan from grade 1.
        var plan = tracker.BuildSuitUpgradePlan("dominator", 5, state);

        Assert.NotNull(plan);
        Assert.Equal(1, plan!.FromGrade);
    }

    [Fact]
    public void BuildSuitUpgradePlan_returns_null_for_the_flight_suit()
    {
        var (_, state, tracker) = NewTracker();
        Assert.Null(tracker.BuildSuitUpgradePlan("flightsuit", 5, state));
    }

    [Fact]
    public void BuildSuitUpgradePlan_returns_null_for_unknown_suit_or_grade()
    {
        var (_, state, tracker) = NewTracker();
        Assert.Null(tracker.BuildSuitUpgradePlan("does_not_exist", 5, state));
        Assert.Null(tracker.BuildSuitUpgradePlan("dominator", 9, state));
    }

    [Fact]
    public void BuildSuitUpgradePlan_resolves_mods_for_the_target_grades_slot_count()
    {
        var (bus, state, tracker) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-01T00:00:00Z", "event":"EngineerProgress", "Engineers":[
          { "Engineer":"Rosa Dayette", "EngineerID":400001, "Progress":"Unlocked", "Rank":3, "RankProgress":0 }
        ] }
        """);

        var plan = tracker.BuildSuitUpgradePlan("dominator", 5, state);

        Assert.NotNull(plan);
        Assert.NotEmpty(plan!.Mods);
        var batteryMod = Assert.Single(plan.Mods, m => m.Modification.Id == "improved_battery_capacity");
        Assert.Equal("Rosa Dayette", batteryMod.Engineer!.Name);
        Assert.True(batteryMod.EngineerUnlocked);
    }

    [Fact]
    public void BuildWeaponUpgradePlan_always_plans_from_grade1()
    {
        var (_, state, tracker) = NewTracker();

        var plan = tracker.BuildWeaponUpgradePlan("karma_ar50", 3, state);

        Assert.NotNull(plan);
        Assert.Equal(1, plan!.FromGrade);
        Assert.Equal(3, plan.ToGrade);
        Assert.Equal(500000 + 1875000, plan.TotalCredits);
    }

    [Fact]
    public void BuildWeaponUpgradePlan_returns_null_for_unknown_weapon()
    {
        var (_, state, tracker) = NewTracker();
        Assert.Null(tracker.BuildWeaponUpgradePlan("does_not_exist", 5, state));
    }
}
