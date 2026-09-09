using System.Linq;
using EDNexus.Core.Journal;
using EDNexus.Core.Mining;
using Xunit;

namespace EDNexus.Tests.Mining;

public class MiningTrackerTests
{
    private static (JournalEventBus Bus, MiningTracker Tracker) NewTracker()
    {
        var bus = new JournalEventBus();
        return (bus, new MiningTracker(bus));
    }

    private static void Publish(JournalEventBus bus, string json, bool historical = false)
    {
        Assert.True(JournalEntry.TryParse(json, historical, out var entry));
        bus.Publish(entry);
    }

    [Fact]
    public void A_plain_deposit_records_its_materials_sorted_by_proportion()
    {
        var (bus, tracker) = NewTracker();

        // Shaped exactly like a live capture: internal symbol for materials with no _Localised (English
        // client omits it when the raw symbol already reads fine), Content as the internal token.
        Publish(bus, """
        { "timestamp":"2026-09-09T15:08:36Z", "event":"ProspectedAsteroid",
          "Materials":[ { "Name":"cobalt", "Proportion":23.067249 }, { "Name":"bauxite", "Proportion":25.287918 } ],
          "Content":"$AsteroidMaterialContent_Low;", "Content_Localised":"Material Content: Low", "Remaining":100.0 }
        """);

        var result = tracker.Latest!;
        Assert.False(result.HasMotherlode);
        Assert.Equal("Low", result.Content);
        Assert.Equal(100.0, result.Remaining);
        Assert.Equal(new[] { "bauxite", "cobalt" }, result.Materials.Select(m => m.Symbol));   // higher proportion first
        Assert.Equal(25.287918, result.Materials[0].Proportion);
    }

    [Fact]
    public void A_motherlode_uses_the_localised_name_for_display_but_the_raw_symbol_for_matching()
    {
        var (bus, tracker) = NewTracker();

        // Real capture: the raw symbol ("Opal") differs from its display name ("Void Opal").
        Publish(bus, """
        { "timestamp":"2026-09-09T15:40:37Z", "event":"ProspectedAsteroid",
          "Materials":[ { "Name":"HydrogenPeroxide", "Name_Localised":"Hydrogen Peroxide", "Proportion":24.941385 } ],
          "MotherlodeMaterial":"Opal", "MotherlodeMaterial_Localised":"Void Opal",
          "Content":"$AsteroidMaterialContent_High;", "Content_Localised":"Material Content: High", "Remaining":100 }
        """);

        var result = tracker.Latest!;
        Assert.True(result.HasMotherlode);
        Assert.Equal("Void Opal", result.MotherlodeName);
        Assert.Equal("opal", result.MotherlodeSymbol);
        Assert.Equal("High", result.Content);
    }

    [Fact]
    public void History_accumulates_oldest_first_and_latest_tracks_the_newest()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-09-09T15:00:00Z", "event":"ProspectedAsteroid", "Materials":[{"Name":"rutile","Proportion":10}], "Content":"$AsteroidMaterialContent_Low;", "Remaining":100 }""");
        Publish(bus, """{ "timestamp":"2026-09-09T15:01:00Z", "event":"ProspectedAsteroid", "Materials":[{"Name":"painite","Proportion":30}], "Content":"$AsteroidMaterialContent_High;", "Remaining":100 }""");

        Assert.Equal(2, tracker.History.Count);
        Assert.Equal("rutile", tracker.History[0].Materials[0].Symbol);
        Assert.Equal("painite", tracker.Latest!.Materials[0].Symbol);
    }

    [Fact]
    public void A_historical_replay_is_ignored_so_a_warm_up_does_not_dump_a_stale_history()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-09-01T00:00:00Z", "event":"ProspectedAsteroid", "Materials":[{"Name":"rutile","Proportion":10}], "Remaining":100 }""", historical: true);

        Assert.Empty(tracker.History);
        Assert.Null(tracker.Latest);
    }

    [Fact]
    public void Clear_empties_the_history_and_raises_changed()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-09-09T15:00:00Z", "event":"ProspectedAsteroid", "Materials":[{"Name":"rutile","Proportion":10}], "Remaining":100 }""");
        Assert.NotEmpty(tracker.History);

        var raised = false;
        tracker.Changed += () => raised = true;
        tracker.Clear();

        Assert.Empty(tracker.History);
        Assert.True(raised);
    }

    [Fact]
    public void A_barren_rock_with_no_materials_still_records_content_and_remaining()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-09-09T15:00:00Z", "event":"ProspectedAsteroid", "Materials":[], "Content":"$AsteroidMaterialContent_Low;", "Remaining":100 }""");

        var result = tracker.Latest!;
        Assert.Empty(result.Materials);
        Assert.False(result.HasMotherlode);
        Assert.Equal("Low", result.Content);
    }

    [Fact]
    public void MiningRefined_records_one_unit_with_the_symbol_unwrapped_and_the_localised_name_kept()
    {
        var (bus, tracker) = NewTracker();

        // Real capture shape: Type carries the "$..._name;" internal wrapper, same as EDDN's commodity fields.
        Publish(bus, """{ "timestamp":"2026-09-09T17:47:30Z", "event":"MiningRefined", "Type":"$gold_name;", "Type_Localised":"Gold" }""");

        var unit = Assert.Single(tracker.Refined);
        Assert.Equal("gold", unit.Symbol);
        Assert.Equal("Gold", unit.Name);
    }

    [Fact]
    public void MiningRefined_fires_once_per_unit_so_two_events_accumulate_two_entries()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-09-09T17:47:30Z", "event":"MiningRefined", "Type":"$gold_name;", "Type_Localised":"Gold" }""");
        Publish(bus, """{ "timestamp":"2026-09-09T17:48:10Z", "event":"MiningRefined", "Type":"$gold_name;", "Type_Localised":"Gold" }""");

        Assert.Equal(2, tracker.Refined.Count);
    }

    [Fact]
    public void A_historical_MiningRefined_replay_is_ignored_so_a_restart_does_not_double_count_the_day()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-09-09T17:47:30Z", "event":"MiningRefined", "Type":"$gold_name;", "Type_Localised":"Gold" }""", historical: true);

        Assert.Empty(tracker.Refined);
    }

    [Fact]
    public void Clear_empties_the_refined_history_too()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-09-09T17:47:30Z", "event":"MiningRefined", "Type":"$gold_name;", "Type_Localised":"Gold" }""");
        Assert.NotEmpty(tracker.Refined);

        tracker.Clear();

        Assert.Empty(tracker.Refined);
    }
}
