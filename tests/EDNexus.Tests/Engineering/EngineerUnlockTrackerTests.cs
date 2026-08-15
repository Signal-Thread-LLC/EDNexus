using System.Linq;
using EDNexus.Core.Engineering;
using EDNexus.Core.Journal;
using Xunit;

namespace EDNexus.Tests.Engineering;

public class EngineerUnlockTrackerTests
{
    private static (JournalEventBus bus, EngineeringTracker tracker) NewTracker()
    {
        var bus = new JournalEventBus();
        return (bus, new EngineeringTracker(bus));
    }

    private static void Publish(JournalEventBus bus, string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var entry), "sample JSON failed to parse");
        bus.Publish(entry);
    }

    private static EngineerStanding Standing(EngineeringTracker t, string id)
        => t.Standing(EngineeringCatalog.Default.Engineer(id)!);

    /// <summary>A startup snapshot, the shape EngineerProgress takes on login.</summary>
    private static string Snapshot(params string[] entries) => $$"""
    { "timestamp":"2026-08-10T12:00:00Z", "event":"EngineerProgress", "Engineers":[{{string.Join(",", entries)}}] }
    """;

    private const string FarseerUnlocked =
        """{ "Engineer":"Felicity Farseer", "EngineerID":300100, "Progress":"Unlocked", "RankProgress":40, "Rank":3 }""";

    private const string IshmaakInvited =
        """{ "Engineer":"Juri Ishmaak", "EngineerID":300000, "Progress":"Invited" }""";

    private const string DwellerKnown =
        """{ "Engineer":"The Dweller", "EngineerID":300180, "Progress":"Known" }""";

    // --- Acceptance: every engineer shows a status and a concrete next step. ---

    [Fact]
    public void Every_engineer_has_a_standing_and_a_non_empty_next_step()
    {
        var (_, tracker) = NewTracker();
        var standings = tracker.Standings();

        Assert.Equal(25, standings.Count);
        Assert.All(standings, s => Assert.False(string.IsNullOrWhiteSpace(s.NextStep)));
    }

    [Fact]
    public void An_engineer_the_journal_has_never_mentioned_is_unknown()
    {
        var (_, tracker) = NewTracker();
        var farseer = Standing(tracker, "farseer");

        Assert.Equal(EngineerStatus.Unknown, farseer.Status);
        Assert.Equal("UNKNOWN", farseer.StatusLabel);
        Assert.Equal(0, farseer.Rank);
        // Farseer needs no referral, so the next step is her own invitation requirement.
        Assert.Contains("Earn the invitation", farseer.NextStep);
        Assert.Contains("exploration rank Scout", farseer.NextStep);
    }

    [Fact]
    public void Known_but_not_invited_still_points_at_the_invitation()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot(DwellerKnown));

        var dweller = Standing(tracker, "dweller");
        Assert.Equal(EngineerStatus.Known, dweller.Status);
        Assert.Equal("KNOWN", dweller.StatusLabel);
        Assert.Contains("Earn the invitation", dweller.NextStep);
    }

    [Fact]
    public void Invited_points_at_the_unlock_contribution()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot(IshmaakInvited));

        var ishmaak = Standing(tracker, "ishmaak");
        Assert.Equal(EngineerStatus.Invited, ishmaak.Status);
        Assert.Equal("INVITED", ishmaak.StatusLabel);
        Assert.Contains("Gain access", ishmaak.NextStep);
        Assert.Contains("combat bonds", ishmaak.NextStep);
    }

    [Fact]
    public void An_unlocked_engineer_below_grade_five_points_at_the_next_grade()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot(FarseerUnlocked));

        var farseer = Standing(tracker, "farseer");
        Assert.Equal(EngineerStatus.Unlocked, farseer.Status);
        Assert.Equal(3, farseer.Rank);
        Assert.Equal("G3", farseer.StatusLabel);
        Assert.False(farseer.IsMaxed);
        Assert.Contains("grade 4 of 5", farseer.NextStep);
    }

    [Fact]
    public void A_grade_five_engineer_has_nothing_left_to_do()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot(
            """{ "Engineer":"Felicity Farseer", "Progress":"Unlocked", "Rank":5, "RankProgress":0 }"""));

        var farseer = Standing(tracker, "farseer");
        Assert.True(farseer.IsMaxed);
        Assert.Equal("G5", farseer.StatusLabel);
        Assert.Contains("Fully levelled", farseer.NextStep);
        Assert.Equal(1.0, farseer.Progress);
    }

    // --- The referral chain: the reason "next step" is not just "go do the unlock". ---

    [Fact]
    public void A_referral_only_engineer_names_the_engineer_blocking_them()
    {
        var (_, tracker) = NewTracker();

        // Juri Ishmaak is reached through Felicity Farseer, who is not unlocked here.
        var ishmaak = Standing(tracker, "ishmaak");
        Assert.NotNull(ishmaak.BlockedBy);
        Assert.Equal("Felicity Farseer", ishmaak.BlockedBy!.Name);
        Assert.Contains("Unlock Felicity Farseer first", ishmaak.NextStep);
    }

    [Fact]
    public void Unlocking_the_referrer_opens_the_path_and_changes_the_next_step()
    {
        var (bus, tracker) = NewTracker();
        Assert.NotNull(Standing(tracker, "ishmaak").BlockedBy);

        Publish(bus, Snapshot(FarseerUnlocked));

        var ishmaak = Standing(tracker, "ishmaak");
        Assert.Null(ishmaak.BlockedBy);
        Assert.Contains("Earn the invitation", ishmaak.NextStep);
    }

    [Fact]
    public void A_referral_chain_two_deep_blocks_on_the_nearest_locked_link()
    {
        var (bus, tracker) = NewTracker();

        // Lori Jameson comes via Marco Qwent. Qwent needs no referral of his own.
        var jameson = Standing(tracker, "jameson");
        Assert.Equal("Marco Qwent", jameson.BlockedBy!.Name);

        Publish(bus, Snapshot("""{ "Engineer":"Marco Qwent", "Progress":"Unlocked", "Rank":5 }"""));
        Assert.Null(Standing(tracker, "jameson").BlockedBy);
    }

    [Fact]
    public void Being_known_already_means_the_referral_is_no_longer_blocking()
    {
        var (bus, tracker) = NewTracker();
        // The journal knows about Ishmaak even though Farseer is not unlocked in this save.
        Publish(bus, Snapshot("""{ "Engineer":"Juri Ishmaak", "Progress":"Known" }"""));

        var ishmaak = Standing(tracker, "ishmaak");
        Assert.Null(ishmaak.BlockedBy);
        Assert.Contains("Earn the invitation", ishmaak.NextStep);
    }

    // --- Acceptance: standings update as EngineerProgress arrives. ---

    [Fact]
    public void A_live_single_engineer_update_moves_the_standing_on()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot(FarseerUnlocked));
        Assert.Equal(3, Standing(tracker, "farseer").Rank);

        // Live updates arrive without the Engineers array wrapper.
        Publish(bus, """
        { "timestamp":"2026-08-10T13:00:00Z", "event":"EngineerProgress",
          "Engineer":"Felicity Farseer", "EngineerID":300100, "Progress":"Unlocked", "Rank":4, "RankProgress":10 }
        """);

        var farseer = Standing(tracker, "farseer");
        Assert.Equal(4, farseer.Rank);
        Assert.Contains("grade 5 of 5", farseer.NextStep);
    }

    [Fact]
    public void Rank_progress_is_normalised_to_a_fraction_whichever_form_the_journal_uses()
    {
        var (bus, tracker) = NewTracker();

        Publish(bus, Snapshot("""{ "Engineer":"Felicity Farseer", "Progress":"Unlocked", "Rank":2, "RankProgress":40 }"""));
        Assert.Equal(0.4, Standing(tracker, "farseer").RankProgress, 3);

        Publish(bus, Snapshot("""{ "Engineer":"Felicity Farseer", "Progress":"Unlocked", "Rank":2, "RankProgress":0.6 }"""));
        Assert.Equal(0.6, Standing(tracker, "farseer").RankProgress, 3);
    }

    [Fact]
    public void Unlocked_engineers_still_answer_the_existing_IsUnlocked_check()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot(FarseerUnlocked, IshmaakInvited));

        Assert.True(tracker.IsUnlocked(EngineeringCatalog.Default.Engineer("farseer")!));
        Assert.False(tracker.IsUnlocked(EngineeringCatalog.Default.Engineer("ishmaak")!));
        Assert.Equal(3, tracker.UnlockedRanks["Felicity Farseer"]);
    }

    // --- Ordering: the list has to lead with what is worth doing. ---

    [Fact]
    public void Standings_lead_with_the_actionable_and_sink_the_finished()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot(
            """{ "Engineer":"Felicity Farseer", "Progress":"Unlocked", "Rank":5 }""",
            """{ "Engineer":"Elvira Martuuk", "Progress":"Invited" }"""));

        var standings = tracker.Standings();

        // Maxed engineers go last; blocked-by-referral sit behind everything reachable.
        Assert.True(standings.Last().IsMaxed);
        var martuuk = standings.First(s => s.Engineer.Id == "martuuk");
        var blockedIndex = standings.ToList().FindIndex(s => s.BlockedBy is not null);
        Assert.True(standings.ToList().IndexOf(martuuk) < blockedIndex);
    }

    // --- Linking blueprints to the engineers that gate them (#12's other half). ---

    [Fact]
    public void A_blueprint_grade_reports_the_engineers_that_gate_it()
    {
        var (bus, tracker) = NewTracker();
        Publish(bus, Snapshot(FarseerUnlocked));

        var gating = tracker.GatingEngineers("fsd_increased_range", 5);

        Assert.NotEmpty(gating);
        Assert.Contains(gating, g => g.Engineer.Id == "farseer");
        Assert.True(gating[0].IsUnlocked);   // an unlocked engineer is listed first
    }

    [Fact]
    public void An_unknown_blueprint_gates_on_nobody_rather_than_throwing()
    {
        var (_, tracker) = NewTracker();
        Assert.Empty(tracker.GatingEngineers("no_such_blueprint", 5));
        Assert.Empty(tracker.GatingEngineers("fsd_increased_range", 99));
    }
}
