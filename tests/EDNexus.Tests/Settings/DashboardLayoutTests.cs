using System.Collections.Generic;
using System.Linq;
using EDNexus.Core.Settings;
using Xunit;

namespace EDNexus.Tests.Settings;

public class DashboardLayoutTests
{
    private static CardDefaults[] Known(params string[] ids)
        => ids.Select(id => new CardDefaults(id, 452)).ToArray();

    private static CardLayout Saved(string id, int order, bool visible = true, double width = 452, bool collapsed = false)
        => new() { Id = id, Order = order, Visible = visible, Width = width, Collapsed = collapsed };

    // --- Defaults and round-tripping. ---

    [Fact]
    public void With_nothing_saved_the_shipped_order_is_kept_and_everything_is_visible()
    {
        var merged = DashboardLayout.Merge(Known("location", "ship", "market"), null);

        Assert.Equal(new[] { "location", "ship", "market" }, merged.Select(c => c.Id));
        Assert.Equal(new[] { 0, 1, 2 }, merged.Select(c => c.Order));
        Assert.All(merged, c => Assert.True(c.Visible));
        Assert.All(merged, c => Assert.Equal(452, c.Width));
    }

    [Fact]
    public void A_saved_arrangement_is_applied_in_full()
    {
        var merged = DashboardLayout.Merge(
            Known("location", "ship", "market"),
            new[]
            {
                Saved("market", 0, width: 920, collapsed: true),
                Saved("location", 1, visible: false),
                Saved("ship", 2),
            });

        Assert.Equal(new[] { "market", "location", "ship" }, merged.Select(c => c.Id));

        var market = merged[0];
        Assert.Equal(920, market.Width);
        Assert.True(market.Collapsed);
        Assert.False(merged[1].Visible);
    }

    // --- Tolerance, which is the whole point: the app and the saved file drift apart. ---

    [Fact]
    public void A_card_added_since_the_layout_was_saved_appears_at_the_end_rather_than_vanishing()
    {
        var merged = DashboardLayout.Merge(
            Known("location", "ship", "engineers"),
            new[] { Saved("ship", 0), Saved("location", 1) });

        Assert.Equal(new[] { "ship", "location", "engineers" }, merged.Select(c => c.Id));
        Assert.True(merged.Single(c => c.Id == "engineers").Visible);
    }

    [Fact]
    public void A_saved_entry_for_a_card_that_no_longer_exists_is_dropped()
    {
        var merged = DashboardLayout.Merge(
            Known("location", "ship"),
            new[] { Saved("retired_card", 0), Saved("ship", 1), Saved("location", 2) });

        Assert.Equal(new[] { "ship", "location" }, merged.Select(c => c.Id));
        Assert.DoesNotContain(merged, c => c.Id == "retired_card");
    }

    [Fact]
    public void A_new_card_never_lands_hidden_even_when_everything_saved_was_hidden()
    {
        var merged = DashboardLayout.Merge(
            Known("location", "engineers"),
            new[] { Saved("location", 0, visible: false) });

        Assert.False(merged.Single(c => c.Id == "location").Visible);
        Assert.True(merged.Single(c => c.Id == "engineers").Visible);
    }

    [Fact]
    public void A_saved_width_of_zero_falls_back_to_the_cards_own_default()
    {
        var merged = DashboardLayout.Merge(
            new[] { new CardDefaults("market", 920) },
            new[] { Saved("market", 0, width: 0) });

        Assert.Equal(920, merged.Single().Width);
    }

    [Fact]
    public void Saved_ids_match_case_insensitively()
    {
        var merged = DashboardLayout.Merge(Known("market"), new[] { Saved("MARKET", 0, width: 920) });
        Assert.Equal(920, merged.Single().Width);
    }

    [Fact]
    public void Blank_saved_ids_are_ignored_rather_than_shifting_the_order()
    {
        var merged = DashboardLayout.Merge(
            Known("location", "ship"),
            new[] { new CardLayout { Id = "", Order = 0 }, Saved("ship", 1), Saved("location", 2) });

        Assert.Equal(new[] { "ship", "location" }, merged.Select(c => c.Id));
    }

    [Fact]
    public void Order_is_always_renumbered_densely_so_gaps_never_accumulate()
    {
        var merged = DashboardLayout.Merge(
            Known("a", "b", "c"),
            new[] { Saved("c", 5), Saved("a", 40), Saved("b", 900) });

        Assert.Equal(new[] { "c", "a", "b" }, merged.Select(x => x.Id));
        Assert.Equal(new[] { 0, 1, 2 }, merged.Select(x => x.Order));
    }

    // --- Moving. ---

    [Fact]
    public void Moving_a_card_earlier_swaps_it_with_its_predecessor()
    {
        var moved = DashboardLayout.Move(
            new[] { Saved("a", 0), Saved("b", 1), Saved("c", 2) }, "b", -1);

        Assert.Equal(new[] { "b", "a", "c" }, moved.Select(c => c.Id));
        Assert.Equal(new[] { 0, 1, 2 }, moved.Select(c => c.Order));
    }

    [Fact]
    public void Moving_a_card_later_swaps_it_with_its_successor()
    {
        var moved = DashboardLayout.Move(
            new[] { Saved("a", 0), Saved("b", 1), Saved("c", 2) }, "b", +1);

        Assert.Equal(new[] { "a", "c", "b" }, moved.Select(c => c.Id));
    }

    [Fact]
    public void Moving_past_either_end_is_a_no_op_rather_than_wrapping()
    {
        var layout = new[] { Saved("a", 0), Saved("b", 1), Saved("c", 2) };

        Assert.Equal(new[] { "a", "b", "c" }, DashboardLayout.Move(layout, "a", -1).Select(c => c.Id));
        Assert.Equal(new[] { "a", "b", "c" }, DashboardLayout.Move(layout, "c", +1).Select(c => c.Id));
    }

    [Fact]
    public void Moving_an_unknown_card_leaves_the_order_untouched()
    {
        var moved = DashboardLayout.Move(new[] { Saved("a", 0), Saved("b", 1) }, "nope", -1);
        Assert.Equal(new[] { "a", "b" }, moved.Select(c => c.Id));
    }

    // --- Reset. ---

    [Fact]
    public void Reset_restores_the_shipped_order_widths_and_visibility()
    {
        var defaults = DashboardLayout.Defaults(new[]
        {
            new CardDefaults("location", 452),
            new CardDefaults("market", 920),
        });

        Assert.Equal(new[] { "location", "market" }, defaults.Select(c => c.Id));
        Assert.Equal(new[] { 452d, 920d }, defaults.Select(c => c.Width));
        Assert.All(defaults, c => Assert.True(c.Visible));
        Assert.All(defaults, c => Assert.False(c.Collapsed));
    }

    [Fact]
    public void A_settings_file_written_before_the_dashboard_existed_still_loads()
    {
        // Upgrading must not wipe anyone's settings just because a new section appeared.
        const string legacy = """
        { "CrashReportingEnabled": true, "InstallId": "abc",
          "Engineering": { "PinnedBlueprintId": "fsd_increased_range", "PinnedGrade": 5 } }
        """;

        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(legacy);

        Assert.NotNull(settings);
        Assert.Equal("fsd_increased_range", settings!.Engineering.PinnedBlueprintId);
        Assert.NotNull(settings.Dashboard);
        Assert.Empty(settings.Dashboard.Cards);   // no arrangement saved yet — ship defaults apply
    }

    [Fact]
    public void A_merged_layout_survives_a_save_and_reload_round_trip_unchanged()
    {
        var known = Known("location", "ship", "market");
        var first = DashboardLayout.Merge(known, null).ToList();
        first[0].Visible = false;
        first[2].Width = 920;

        var second = DashboardLayout.Merge(known, first);

        Assert.Equal(first.Select(c => c.Id), second.Select(c => c.Id));
        Assert.False(second.Single(c => c.Id == "location").Visible);
        Assert.Equal(920, second.Single(c => c.Id == "market").Width);
    }

    // --- Hand placement: dropping a card into a column. ---

    [Fact]
    public void A_card_nobody_has_moved_is_left_for_the_packer_to_place()
    {
        var merged = DashboardLayout.Merge(Known("location", "ship"), null);

        Assert.All(merged, c => Assert.Equal(CardLayout.AutoColumn, c.Column));
    }

    [Fact]
    public void A_dropped_card_takes_the_column_and_lands_above_the_card_it_was_dropped_on()
    {
        var layout = DashboardLayout.Merge(Known("location", "ship", "market"), null);

        var moved = DashboardLayout.MoveTo(layout, "market", column: 1, beforeId: "ship");

        Assert.Equal(new[] { "location", "market", "ship" }, moved.Select(c => c.Id));
        Assert.Equal(1, moved.Single(c => c.Id == "market").Column);
    }

    [Fact]
    public void A_drop_below_a_column_parks_the_card_at_its_foot()
    {
        var layout = DashboardLayout.Merge(Known("location", "ship", "market"), null);

        var moved = DashboardLayout.MoveTo(layout, "location", column: 2, beforeId: null);

        Assert.Equal(new[] { "ship", "market", "location" }, moved.Select(c => c.Id));
        Assert.Equal(2, moved.Single(c => c.Id == "location").Column);
    }

    [Fact]
    public void A_card_dropped_on_itself_changes_column_without_being_flung_to_the_end()
    {
        // A click on a card title that travels a pixel arrives as a drop onto itself.
        var layout = DashboardLayout.Merge(Known("location", "ship", "market"), null);

        var moved = DashboardLayout.MoveTo(layout, "location", column: 0, beforeId: "location");

        Assert.Equal(new[] { "location", "ship", "market" }, moved.Select(c => c.Id));
        Assert.Equal(0, moved.Single(c => c.Id == "location").Column);
    }

    [Fact]
    public void Placing_a_card_fixes_the_untouched_ones_where_they_are_already_drawn()
    {
        var layout = DashboardLayout.Merge(Known("location", "ship", "market"), null);

        var pinned = DashboardLayout.Pin(layout, new Dictionary<string, int>
        {
            ["location"] = 0,
            ["ship"] = 1,
            ["market"] = 1,
        });

        Assert.Equal(0, pinned.Single(c => c.Id == "location").Column);
        Assert.Equal(1, pinned.Single(c => c.Id == "ship").Column);
        Assert.Equal(1, pinned.Single(c => c.Id == "market").Column);
    }

    [Fact]
    public void Pinning_leaves_a_column_a_commander_chose_alone_and_skips_cards_that_are_not_drawn()
    {
        var layout = DashboardLayout.Merge(Known("location", "ship"), null).ToList();
        layout[0].Column = 3;   // already placed by hand

        // "ship" is hidden, so it never rendered and has no column to be fixed to.
        var pinned = DashboardLayout.Pin(layout, new Dictionary<string, int> { ["location"] = 0 });

        Assert.Equal(3, pinned.Single(c => c.Id == "location").Column);
        Assert.Equal(CardLayout.AutoColumn, pinned.Single(c => c.Id == "ship").Column);
    }

    [Fact]
    public void Move_steps_through_the_cards_sharing_a_column_not_the_flat_list()
    {
        // Two columns interleaved in the flat order: moving "market" up has to step over "ship",
        // which is in the other column, and swap it with "location".
        var layout = DashboardLayout.Merge(Known("location", "ship", "market"), null).ToList();
        layout[0].Column = 0;   // location
        layout[1].Column = 1;   // ship
        layout[2].Column = 0;   // market

        var moved = DashboardLayout.Move(layout, "market", -1);

        Assert.Equal(new[] { "market", "location", "ship" }, moved.Select(c => c.Id));
    }

    [Fact]
    public void Move_does_nothing_for_a_card_already_at_the_top_of_its_column()
    {
        var layout = DashboardLayout.Merge(Known("location", "ship"), null).ToList();
        layout[0].Column = 0;
        layout[1].Column = 1;

        var moved = DashboardLayout.Move(layout, "ship", -1);

        Assert.Equal(new[] { "location", "ship" }, moved.Select(c => c.Id));
    }

    [Fact]
    public void Reset_hands_every_card_back_to_the_packer()
    {
        var layout = DashboardLayout.Merge(Known("location", "ship"), null).ToList();
        layout[0].Column = 2;
        layout[1].Column = 3;

        var defaults = DashboardLayout.Defaults(Known("location", "ship"));

        Assert.All(defaults, c => Assert.Equal(CardLayout.AutoColumn, c.Column));
    }

    [Fact]
    public void A_hand_placed_column_survives_a_save_and_reload_round_trip()
    {
        var known = Known("location", "ship");
        var placed = DashboardLayout.MoveTo(DashboardLayout.Merge(known, null), "ship", 2, null);

        var reloaded = DashboardLayout.Merge(known, placed);

        Assert.Equal(2, reloaded.Single(c => c.Id == "ship").Column);
        Assert.Equal(CardLayout.AutoColumn, reloaded.Single(c => c.Id == "location").Column);
    }
}
