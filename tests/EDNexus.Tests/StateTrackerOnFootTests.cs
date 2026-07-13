using EDNexus.Core.Journal;
using EDNexus.Core.State;
using Xunit;

namespace EDNexus.Tests;

public class StateTrackerOnFootTests
{
    private static (JournalEventBus bus, CommanderState state) NewTracker()
    {
        var bus = new JournalEventBus();
        var state = new CommanderState();
        _ = new StateTracker(bus, state);
        return (bus, state);
    }

    private static void Publish(JournalEventBus bus, string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var entry), "sample JSON failed to parse");
        bus.Publish(entry);
    }

    [Fact]
    public void ShipLocker_populates_all_four_onfoot_inventories_keyed_by_raw_symbol()
    {
        var (bus, state) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-01T00:00:00Z", "event":"ShipLocker",
          "Items":[ { "Name":"suitschematic", "Name_Localised":"Suit Schematic", "Count":3 } ],
          "Components":[ { "Name":"graphene", "Count":9 } ],
          "Consumables":[ { "Name":"medkit", "Count":2 } ],
          "Data":[ { "Name":"manufacturinginstructions", "Name_Localised":"Manufacturing Instructions", "Count":4 } ] }
        """);

        // Keyed by the raw (not localised) symbol, matching the Odyssey catalog's material keys.
        Assert.Equal(3, state.OnFoot.Items["suitschematic"]);
        Assert.Equal(9, state.OnFoot.Components["graphene"]);
        Assert.Equal(2, state.OnFoot.Consumables["medkit"]);
        Assert.Equal(4, state.OnFoot.Data["manufacturinginstructions"]);
    }

    [Fact]
    public void ShipLocker_without_arrays_does_not_wipe_existing_inventory()
    {
        var (bus, state) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-01T00:00:00Z", "event":"ShipLocker",
          "Items":[ { "Name":"suitschematic", "Count":3 } ] }
        """);

        // A later ShipLocker event that omits Components must not clear it.
        Publish(bus, """{ "timestamp":"2026-07-01T00:01:00Z", "event":"ShipLocker" }""");

        Assert.Equal(3, state.OnFoot.Items["suitschematic"]);
    }

    [Fact]
    public void BackpackChange_applies_added_and_removed_deltas()
    {
        var (bus, state) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-01T00:00:00Z", "event":"BackpackChange",
          "Added":[ { "Name":"graphene", "Count":5, "Type":"Component" } ] }
        """);
        Assert.Equal(5, state.OnFoot.Components["graphene"]);

        Publish(bus, """
        { "timestamp":"2026-07-01T00:01:00Z", "event":"BackpackChange",
          "Removed":[ { "Name":"graphene", "Count":2, "Type":"Component" } ] }
        """);
        Assert.Equal(3, state.OnFoot.Components["graphene"]);
    }

    [Fact]
    public void BackpackChange_removal_never_goes_negative()
    {
        var (bus, state) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-01T00:00:00Z", "event":"BackpackChange",
          "Removed":[ { "Name":"graphene", "Count":5, "Type":"Component" } ] }
        """);

        Assert.Equal(0, state.OnFoot.Components["graphene"]);
    }

    [Theory]
    [InlineData("tacticalsuit_class3", "Dominator Suit", 3)]
    [InlineData("utilitysuit_class1", "Maverick Suit", 1)]
    [InlineData("flightsuit", "Flight Suit", 1)]
    public void SuitLoadout_sets_suit_name_symbol_and_class(string symbol, string localised, int expectedGrade)
    {
        var (bus, state) = NewTracker();
        Publish(bus, $$"""
        { "timestamp":"2026-07-01T00:00:00Z", "event":"SuitLoadout",
          "SuitID":123, "SuitName":"{{symbol}}", "SuitName_Localised":"{{localised}}", "LoadoutID":1, "LoadoutName":"Loadout 1" }
        """);

        Assert.Equal(symbol, state.SuitSymbol);
        Assert.Equal(localised, state.SuitName);
        Assert.Equal(expectedGrade, state.SuitClass);
    }

    [Fact]
    public void SwitchSuitLoadout_updates_the_current_suit()
    {
        var (bus, state) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-01T00:00:00Z", "event":"SuitLoadout",
          "SuitID":1, "SuitName":"tacticalsuit_class1", "SuitName_Localised":"Dominator Suit", "LoadoutID":1, "LoadoutName":"A" }
        """);
        Assert.Equal(1, state.SuitClass);

        Publish(bus, """
        { "timestamp":"2026-07-01T00:01:00Z", "event":"SwitchSuitLoadout",
          "SuitID":2, "SuitName":"explorationsuit_class4", "SuitName_Localised":"Artemis Suit", "LoadoutID":2, "LoadoutName":"B" }
        """);

        Assert.Equal("Artemis Suit", state.SuitName);
        Assert.Equal(4, state.SuitClass);
    }
}
