using System.Linq;
using EDNexus.Core.Exobio;
using EDNexus.Core.Journal;
using EDNexus.Core.State;
using Xunit;

namespace EDNexus.Tests.Exobio;

public class ExobiologyTrackerTests
{
    private static (JournalEventBus bus, CommanderState state, ExobiologyTracker tracker) NewTracker()
    {
        var bus = new JournalEventBus();
        var state = new CommanderState();
        return (bus, state, new ExobiologyTracker(bus, state));
    }

    private static void Publish(JournalEventBus bus, string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var entry), "sample JSON failed to parse");
        bus.Publish(entry);
    }

    private const long System = 2871051298217;
    private const int BodyId = 12;

    private const string Dss = """
    { "timestamp":"2026-08-01T10:00:00Z", "event":"SAASignalsFound", "BodyName":"Nervi 2 a",
      "SystemAddress":2871051298217, "BodyID":12,
      "Signals":[{"Type":"$SAA_SignalType_Biological;","Type_Localised":"Biological","Count":3}],
      "Genuses":[{"Genus":"$Codex_Ent_Stratum_Genus_Name;","Genus_Localised":"Stratum"},
                 {"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Genus_Localised":"Bacterium"}] }
    """;

    private const string Fss = """
    { "timestamp":"2026-08-01T09:00:00Z", "event":"FSSBodySignals", "BodyName":"Nervi 2 a",
      "SystemAddress":2871051298217, "BodyID":12,
      "Signals":[{"Type":"$SAA_SignalType_Biological;","Type_Localised":"Biological","Count":3}] }
    """;

    private static string Scan(string scanType, string timestamp = "2026-08-01T11:00:00Z") => $$"""
    { "timestamp":"{{timestamp}}", "event":"ScanOrganic", "ScanType":"{{scanType}}",
      "Genus":"$Codex_Ent_Stratum_Genus_Name;", "Genus_Localised":"Stratum",
      "Species":"$Codex_Ent_Stratum_07_Name;", "Species_Localised":"Stratum Tectonicas",
      "SystemAddress":2871051298217, "Body":12 }
    """;

    // --- Scanners. ---

    [Fact]
    public void A_DSS_pass_records_the_signal_count_and_names_the_genera()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Dss);

        var body = Assert.Single(tracker.Bodies);
        Assert.Equal("Nervi 2 a", body.BodyName);
        Assert.Equal(3, body.SignalCount);
        Assert.True(body.Mapped);
        Assert.Equal(new[] { "Stratum", "Bacterium" }, body.Genera.Select(g => g.Name));
    }

    [Fact]
    public void A_mapped_body_carries_an_estimated_value_range()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Dss);

        var range = tracker.Bodies.Single().ValueRange;
        Assert.NotNull(range);
        // Stratum 1,362,000–19,010,800 plus Bacterium 1,000,000–8,418,000.
        Assert.Equal(2362000, range!.Value.Min);
        Assert.Equal(27428800, range.Value.Max);
    }

    [Fact]
    public void An_FSS_pass_gives_a_count_but_no_value_estimate()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Fss);

        var body = Assert.Single(tracker.Bodies);
        Assert.Equal(3, body.SignalCount);
        Assert.False(body.Mapped);
        Assert.Empty(body.Genera);
        Assert.Null(body.ValueRange);   // a bare count says nothing about what is down there
    }

    [Fact]
    public void A_later_FSS_pass_never_discards_genera_the_DSS_established()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Dss);
        Publish(bus, Fss);

        var body = tracker.Bodies.Single();
        Assert.True(body.Mapped);
        Assert.Equal(2, body.Genera.Count);
    }

    [Fact]
    public void Bodies_without_biological_signals_are_ignored()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-08-01T09:00:00Z", "event":"FSSBodySignals", "BodyName":"Nervi 3",
          "SystemAddress":2871051298217, "BodyID":13,
          "Signals":[{"Type":"$SAA_SignalType_Geological;","Type_Localised":"Geological","Count":4}] }
        """);

        Assert.Empty(tracker.Bodies);
    }

    // --- Sampler: the 1/3 → 2/3 → 3/3 progression. ---

    [Fact]
    public void Sampling_progresses_one_third_at_a_time_and_completes_on_analyse()
    {
        var (bus, _, tracker) = NewTracker();

        Publish(bus, Scan("Log", "2026-08-01T11:00:00Z"));
        Assert.Equal("1/3", tracker.ActiveScan!.Progress);
        Assert.False(tracker.ActiveScan.Complete);

        Publish(bus, Scan("Sample", "2026-08-01T11:05:00Z"));
        Assert.Equal("2/3", tracker.ActiveScan!.Progress);
        Assert.False(tracker.ActiveScan.Complete);

        Publish(bus, Scan("Analyse", "2026-08-01T11:10:00Z"));
        Assert.Null(tracker.ActiveScan);   // nothing left in progress

        var done = Assert.Single(tracker.Scans);
        Assert.Equal("3/3", done.Progress);
        Assert.True(done.Complete);
        Assert.Equal("Stratum Tectonicas", done.SpeciesName);
        Assert.Equal(19010800, done.Value);
    }

    [Fact]
    public void A_repeated_sample_never_walks_the_progression_backwards()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Scan("Log"));
        Publish(bus, Scan("Sample", "2026-08-01T11:05:00Z"));
        Publish(bus, Scan("Analyse", "2026-08-01T11:10:00Z"));

        // Resuming a run re-emits "Sample"; the completed run must stay completed.
        Publish(bus, Scan("Sample", "2026-08-01T11:20:00Z"));

        var scan = Assert.Single(tracker.Scans);
        Assert.Equal(3, scan.Samples);
        Assert.True(scan.Complete);
    }

    [Fact]
    public void The_same_species_on_a_different_body_is_a_separate_run()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Scan("Log"));
        Publish(bus, """
        { "timestamp":"2026-08-01T12:00:00Z", "event":"ScanOrganic", "ScanType":"Log",
          "Genus":"$Codex_Ent_Stratum_Genus_Name;", "Species":"$Codex_Ent_Stratum_07_Name;",
          "Species_Localised":"Stratum Tectonicas", "SystemAddress":2871051298217, "Body":99 }
        """);

        Assert.Equal(2, tracker.Scans.Count);
        Assert.All(tracker.Scans, s => Assert.Equal(1, s.Samples));
    }

    [Fact]
    public void An_unknown_species_still_tracks_progress_but_is_worth_nothing_known()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-08-01T11:00:00Z", "event":"ScanOrganic", "ScanType":"Log",
          "Genus":"$Codex_Ent_Newthing_Genus_Name;", "Genus_Localised":"Newthing",
          "Species":"$Codex_Ent_Newthing_01_Name;", "Species_Localised":"Newthing Mysterius",
          "SystemAddress":2871051298217, "Body":12 }
        """);

        var scan = Assert.Single(tracker.Scans);
        Assert.Equal("Newthing Mysterius", scan.SpeciesName);
        Assert.Equal(0, scan.Value);
    }

    // --- Session tally. ---

    [Fact]
    public void Completed_scans_sit_in_the_pending_tally_until_sold()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Scan("Log"));
        Publish(bus, Scan("Analyse", "2026-08-01T11:10:00Z"));

        var session = tracker.Session;
        Assert.Single(session.Pending);
        Assert.Equal(19010800, session.PendingValue);
        Assert.Equal(0, session.SoldValue);
    }

    [Fact]
    public void Selling_banks_the_real_payout_and_clears_what_was_sold()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Scan("Log"));
        Publish(bus, Scan("Analyse", "2026-08-01T11:10:00Z"));

        Publish(bus, """
        { "timestamp":"2026-08-01T13:00:00Z", "event":"SellOrganicData", "MarketID":3228024320,
          "BioData":[{"Genus":"$Codex_Ent_Stratum_Genus_Name;","Species":"$Codex_Ent_Stratum_07_Name;",
                      "Species_Localised":"Stratum Tectonicas","Value":19010800,"Bonus":76043200}] }
        """);

        var session = tracker.Session;
        Assert.Empty(session.Pending);          // no longer riding on the trip
        Assert.Equal(0, session.PendingValue);
        Assert.Equal(95054000, session.SoldValue);   // base plus the first-logged bonus
        Assert.Equal(76043200, session.SoldBonus);
        Assert.Equal(1, session.SoldCount);
    }

    [Fact]
    public void Selling_with_no_bonus_banks_the_base_value_alone()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-08-01T13:00:00Z", "event":"SellOrganicData", "MarketID":1,
          "BioData":[{"Species":"$Codex_Ent_Bacterial_05_Name;","Value":1000000,"Bonus":0}] }
        """);

        Assert.Equal(1000000, tracker.Session.SoldValue);
        Assert.Equal(0, tracker.Session.SoldBonus);
    }

    // --- Where the commander is. ---

    [Fact]
    public void Approaching_a_body_surfaces_its_signals_as_the_current_body()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, Dss);
        Assert.Null(tracker.CurrentBody);

        Publish(bus, """
        { "timestamp":"2026-08-01T10:30:00Z", "event":"ApproachBody", "StarSystem":"Nervi",
          "SystemAddress":2871051298217, "Body":"Nervi 2 a", "BodyID":12 }
        """);

        Assert.Equal("Nervi 2 a", tracker.CurrentBody?.BodyName);
        Assert.Equal(3, tracker.CurrentBody?.SignalCount);

        Publish(bus, """
        { "timestamp":"2026-08-01T14:00:00Z", "event":"LeaveBody", "StarSystem":"Nervi",
          "SystemAddress":2871051298217, "Body":"Nervi 2 a", "BodyID":12 }
        """);

        Assert.Null(tracker.CurrentBody);
    }

    [Fact]
    public void A_first_discovery_codex_entry_is_counted()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-08-01T11:00:00Z", "event":"CodexEntry", "Name":"$Codex_Ent_Stratum_07_Name;",
          "Name_Localised":"Stratum Tectonicas", "SubCategory":"$Codex_SubCategory_Organic_Structures;",
          "SystemAddress":2871051298217, "IsNewEntry":true }
        """);

        Assert.Equal(1, tracker.NewDiscoveries);
    }

    [Fact]
    public void An_already_known_codex_entry_is_not_counted()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-08-01T11:00:00Z", "event":"CodexEntry", "Name":"$Codex_Ent_Stratum_07_Name;",
          "SubCategory":"$Codex_SubCategory_Organic_Structures;", "SystemAddress":2871051298217 }
        """);

        Assert.Equal(0, tracker.NewDiscoveries);
    }

    [Fact]
    public void Events_without_a_body_are_ignored_rather_than_throwing()
    {
        var (bus, _, tracker) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-08-01T11:00:00Z", "event":"ScanOrganic", "ScanType":"Log" }""");
        Publish(bus, """{ "timestamp":"2026-08-01T11:00:00Z", "event":"SAASignalsFound" }""");
        Publish(bus, """{ "timestamp":"2026-08-01T11:00:00Z", "event":"SellOrganicData" }""");

        Assert.Empty(tracker.Scans);
        Assert.Empty(tracker.Bodies);
        Assert.Equal(0, tracker.Session.SoldValue);
    }
}
