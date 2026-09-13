using System.Collections.Generic;
using EDNexus.Core.Colonisation;
using EDNexus.Core.Exobio;
using EDNexus.Core.Journal;
using EDNexus.Core.State;
using EDNexus.Core.Voice;
using Xunit;

namespace EDNexus.Tests.Voice;

public class VoiceCalloutTrackerTests
{
    private static (JournalEventBus Bus, CommanderState State, VoiceCalloutTracker Voice, List<VoiceCallout> Raised) NewTracker()
    {
        var bus = new JournalEventBus();
        var state = new CommanderState();
        _ = new StateTracker(bus, state);       // fills FuelCapacity/FuelMain/Cargo, as EngineHost does
        var exobiology = new ExobiologyTracker(bus, state);
        var colonisation = new ColonisationTracker(bus, state);
        var voice = new VoiceCalloutTracker(bus, state, exobiology, colonisation);

        var raised = new List<VoiceCallout>();
        voice.CalloutRaised += raised.Add;
        return (bus, state, voice, raised);
    }

    private static void Publish(JournalEventBus bus, string json, bool historical = false)
    {
        Assert.True(JournalEntry.TryParse(json, historical, out var entry), "sample JSON failed to parse");
        bus.Publish(entry);
    }

    // --- Fuel low ---

    [Fact]
    public void Fuel_dropping_to_a_quarter_of_capacity_raises_one_callout()
    {
        var (bus, _, _, raised) = NewTracker();

        Publish(bus, """{ "timestamp":"2026-01-01T00:00:00Z", "event":"Loadout", "FuelCapacity":{"Main":32} }""");
        Publish(bus, """{ "timestamp":"2026-01-01T00:00:01Z", "event":"Status", "Fuel":{"FuelMain":6} }""");

        var callout = Assert.Single(raised);
        Assert.Equal(VoiceCalloutKind.FuelLow, callout.Kind);
        Assert.Contains("Fuel low", callout.Text);
    }

    [Fact]
    public void Fuel_above_threshold_does_not_raise_a_callout()
    {
        var (bus, _, _, raised) = NewTracker();

        Publish(bus, """{ "timestamp":"2026-01-01T00:00:00Z", "event":"Loadout", "FuelCapacity":{"Main":32} }""");
        Publish(bus, """{ "timestamp":"2026-01-01T00:00:01Z", "event":"Status", "Fuel":{"FuelMain":20} }""");

        Assert.Empty(raised);
    }

    [Fact]
    public void Repeated_low_status_ticks_do_not_repeat_the_callout_until_refuelled()
    {
        var (bus, _, _, raised) = NewTracker();

        Publish(bus, """{ "timestamp":"2026-01-01T00:00:00Z", "event":"Loadout", "FuelCapacity":{"Main":32} }""");
        Publish(bus, """{ "timestamp":"2026-01-01T00:00:01Z", "event":"Status", "Fuel":{"FuelMain":6} }""");
        Publish(bus, """{ "timestamp":"2026-01-01T00:00:02Z", "event":"Status", "Fuel":{"FuelMain":5} }""");
        Assert.Single(raised);

        // Refuelling above the threshold re-arms it, so the next drop calls out again.
        Publish(bus, """{ "timestamp":"2026-01-01T00:00:03Z", "event":"Status", "Fuel":{"FuelMain":30} }""");
        Publish(bus, """{ "timestamp":"2026-01-01T00:00:04Z", "event":"Status", "Fuel":{"FuelMain":6} }""");
        Assert.Equal(2, raised.Count);
    }

    [Fact]
    public void Historical_replay_does_not_raise_a_callout()
    {
        var (bus, _, _, raised) = NewTracker();

        Publish(bus, """{ "timestamp":"2026-01-01T00:00:00Z", "event":"Loadout", "FuelCapacity":{"Main":32} }""", historical: true);
        Publish(bus, """{ "timestamp":"2026-01-01T00:00:01Z", "event":"Status", "Fuel":{"FuelMain":6} }""", historical: true);

        Assert.Empty(raised);
    }

    // --- Scan complete ---

    // A deliberately made-up symbol/name — unknown to the real exobiology catalog, so the tracker
    // falls back to the event's own text rather than resolving a real species name off it.
    private static string ScanOrganic(string stage) => $$"""
        { "timestamp":"2026-01-01T00:00:00Z", "event":"ScanOrganic", "ScanType":"{{stage}}",
          "Genus":"$Codex_Ent_Test_Genus_Name;", "Genus_Localised":"Test Genus",
          "Species":"$Codex_Ent_Test_Species_Name;", "Species_Localised":"Test Species Aurasus",
          "SystemAddress":123456789, "Body":4 }
        """;

    [Fact]
    public void Completing_a_three_sample_run_raises_scan_complete_once()
    {
        var (bus, _, _, raised) = NewTracker();

        Publish(bus, ScanOrganic("Log"));
        Publish(bus, ScanOrganic("Sample"));
        Assert.Empty(raised);          // not complete until the Analyse step

        Publish(bus, ScanOrganic("Analyse"));

        var callout = Assert.Single(raised);
        Assert.Equal(VoiceCalloutKind.ScanComplete, callout.Kind);
        Assert.Contains("Test Species Aurasus", callout.Text);
    }

    [Fact]
    public void Re_running_the_analyse_step_does_not_repeat_the_callout()
    {
        var (bus, _, _, raised) = NewTracker();

        Publish(bus, ScanOrganic("Log"));
        Publish(bus, ScanOrganic("Sample"));
        Publish(bus, ScanOrganic("Analyse"));
        Publish(bus, ScanOrganic("Analyse"));

        Assert.Single(raised);
    }

    // --- Shopping-list item acquired ---

    private const string Depot = """
        { "timestamp":"2026-01-01T00:00:00Z", "event":"ColonisationConstructionDepot", "MarketID":42,
          "ConstructionProgress":0.1, "ConstructionComplete":false, "ConstructionFailed":false,
          "ResourcesRequired":[
            { "Name":"$aluminium_name;", "Name_Localised":"Aluminium", "RequiredAmount":50, "ProvidedAmount":0, "Payment":100 }
          ] }
        """;

    private static string CargoOf(int aluminium) => $$"""
        { "timestamp":"2026-01-01T00:00:01Z", "event":"Cargo", "Vessel":"Ship", "Count":{{aluminium}},
          "Inventory":[ { "Name":"aluminium", "Name_Localised":"Aluminium", "Count":{{aluminium}}, "Stolen":0 } ] }
        """;

    [Fact]
    public void Cargo_covering_the_full_shortfall_raises_the_callout()
    {
        var (bus, _, _, raised) = NewTracker();

        Publish(bus, Depot);
        Assert.Empty(raised);   // nothing in the hold yet

        Publish(bus, CargoOf(50));

        var callout = Assert.Single(raised);
        Assert.Equal(VoiceCalloutKind.ShoppingListItemAcquired, callout.Kind);
        Assert.Contains("Aluminium", callout.Text);
    }

    [Fact]
    public void Partial_cargo_does_not_raise_the_callout()
    {
        var (bus, _, _, raised) = NewTracker();

        Publish(bus, Depot);
        Publish(bus, CargoOf(20));

        Assert.Empty(raised);
    }

    [Fact]
    public void Repeated_full_cargo_does_not_repeat_the_callout()
    {
        var (bus, _, _, raised) = NewTracker();

        Publish(bus, Depot);
        Publish(bus, CargoOf(50));
        Publish(bus, CargoOf(50));

        Assert.Single(raised);
    }
}
