using System.Collections.Generic;
using EDNexus.Core.Colonisation;
using EDNexus.Core.Journal;
using EDNexus.Core.State;
using Xunit;

namespace EDNexus.Tests;

public class ColonisationTrackerTests
{
    private static (JournalEventBus bus, CommanderState state, ColonisationTracker tracker) NewTracker()
    {
        var bus = new JournalEventBus();
        var state = new CommanderState();
        var tracker = new ColonisationTracker(bus, state);
        return (bus, state, tracker);
    }

    private static void Publish(JournalEventBus bus, string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var entry), "sample JSON failed to parse");
        bus.Publish(entry);
    }

    private const string Depot = """
    { "timestamp":"2026-06-03T00:47:02Z", "event":"ColonisationConstructionDepot", "MarketID":3956023042,
      "ConstructionProgress":0.5, "ConstructionComplete":false, "ConstructionFailed":false,
      "ResourcesRequired":[
        { "Name":"$aluminium_name;", "Name_Localised":"Aluminium", "RequiredAmount":100, "ProvidedAmount":40, "Payment":3239 },
        { "Name":"$steel_name;", "Name_Localised":"Steel", "RequiredAmount":200, "ProvidedAmount":200, "Payment":5057 },
        { "Name":"$cmmcomposite_name;", "Name_Localised":"CMM Composite", "RequiredAmount":500, "ProvidedAmount":0, "Payment":6788 }
      ] }
    """;

    [Fact]
    public void Depot_event_populates_required_provided_remaining()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Depot);

        var site = tracker.ActiveSite;
        Assert.NotNull(site);
        Assert.Equal(3956023042, site!.MarketId);
        Assert.Equal(3, site.Resources.Count);

        var aluminium = Assert.Single(site.Resources, r => r.Name == "Aluminium");
        Assert.Equal(100, aluminium.Required);
        Assert.Equal(40, aluminium.Provided);
        Assert.Equal(60, aluminium.Remaining);
        Assert.False(aluminium.IsComplete);

        Assert.Equal(1, site.CompletedCount);          // Steel is fully provided
        Assert.Equal(60 + 500, site.TotalRemaining);   // Aluminium + CMM Composite
    }

    [Fact]
    public void Shopping_list_excludes_complete_and_sorts_by_shortfall()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Depot);

        var list = tracker.ActiveSite!.BuildShoppingList();

        Assert.Equal(2, list.Count);                        // Steel (complete) dropped
        Assert.Equal("CMM Composite", list[0].Name);        // biggest shortfall first
        Assert.Equal("Aluminium", list[1].Name);
        Assert.DoesNotContain(list, i => i.Name == "Steel");
    }

    [Fact]
    public void Contribution_updates_provided_live_despite_symbol_case_mismatch()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Depot);

        // Note the capitalised "$Aluminium_name;" the game actually emits on contribution.
        Publish(bus, """
        { "timestamp":"2026-06-03T22:43:47Z", "event":"ColonisationContribution", "MarketID":3956023042,
          "Contributions":[ { "Name":"$Aluminium_name;", "Name_Localised":"Aluminium", "Amount":50 } ] }
        """);

        var aluminium = Assert.Single(tracker.ActiveSite!.Resources, r => r.Name == "Aluminium");
        Assert.Equal(90, aluminium.Provided);   // 40 + 50
        Assert.Equal(10, aluminium.Remaining);
    }

    [Fact]
    public void Contribution_never_overshoots_required()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Depot);

        Publish(bus, """
        { "timestamp":"2026-06-03T22:43:47Z", "event":"ColonisationContribution", "MarketID":3956023042,
          "Contributions":[ { "Name":"$aluminium_name;", "Amount":9999 } ] }
        """);

        var aluminium = Assert.Single(tracker.ActiveSite!.Resources, r => r.Name == "Aluminium");
        Assert.Equal(100, aluminium.Provided);
        Assert.True(aluminium.IsComplete);
    }

    [Fact]
    public void Contribution_for_unknown_site_is_ignored()
    {
        var (bus, _, tracker) = NewTracker();

        Publish(bus, """
        { "timestamp":"2026-06-03T22:43:47Z", "event":"ColonisationContribution", "MarketID":999,
          "Contributions":[ { "Name":"$aluminium_name;", "Amount":10 } ] }
        """);

        // No depot ever seen for market 999 → nothing to invent.
        Assert.Null(tracker.ActiveSite);
    }

    [Fact]
    public void Shopping_list_cross_references_cargo_across_name_forms()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Depot);

        // Cargo keys as the journal actually stores them: a bare symbol and a spaced localised label.
        var cargo = new Dictionary<string, int>
        {
            ["aluminium"] = 25,        // bare market symbol
            ["CMM Composite"] = 700,   // localised label with a space; exceeds the 500 needed
        };

        var list = tracker.ActiveSite!.BuildShoppingList(cargo);

        var cmm = Assert.Single(list, i => i.Name == "CMM Composite");
        Assert.Equal(700, cmm.InHold);
        Assert.Equal(500, cmm.Carrying);       // capped at the shortfall
        Assert.Equal(0, cmm.StillNeeded);      // fully covered by the hold
        Assert.True(cmm.CoveredByHold);

        var aluminium = Assert.Single(list, i => i.Name == "Aluminium");
        Assert.Equal(25, aluminium.InHold);
        Assert.Equal(35, aluminium.StillNeeded);  // remaining 60 - 25 aboard
        Assert.False(aluminium.CoveredByHold);
    }

    [Fact]
    public void Depot_snapshot_is_authoritative_and_replaces_prior_state()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Depot);
        Publish(bus, """
        { "timestamp":"2026-06-04T00:00:00Z", "event":"ColonisationConstructionDepot", "MarketID":3956023042,
          "ConstructionProgress":0.9, "ConstructionComplete":false, "ConstructionFailed":false,
          "ResourcesRequired":[
            { "Name":"$aluminium_name;", "Name_Localised":"Aluminium", "RequiredAmount":100, "ProvidedAmount":95, "Payment":3239 }
          ] }
        """);

        var site = tracker.ActiveSite!;
        var aluminium = Assert.Single(site.Resources);
        Assert.Equal(95, aluminium.Provided);          // reconciled to the latest snapshot
        Assert.Equal(0.9, site.Progress, precision: 3);
    }
}
