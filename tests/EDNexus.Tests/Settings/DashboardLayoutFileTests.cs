using System.Linq;
using EDNexus.Core.Settings;
using Xunit;

namespace EDNexus.Tests.Settings;

public class DashboardLayoutFileTests
{
    private static CardLayout Card(string id, int order, bool visible = true, double width = 452, bool collapsed = false)
        => new() { Id = id, Order = order, Visible = visible, Width = width, Collapsed = collapsed };

    [Fact]
    public void An_exported_layout_reads_back_exactly()
    {
        var original = new[]
        {
            Card("market", 0, width: 920, collapsed: true),
            Card("location", 1, visible: false),
            Card("exobio", 2),
        };

        var read = DashboardLayoutFile.TryRead(DashboardLayoutFile.Write(original));

        Assert.NotNull(read);
        Assert.Equal(original.Select(c => c.Id), read!.Select(c => c.Id));
        Assert.Equal(920, read[0].Width);
        Assert.True(read[0].Collapsed);
        Assert.False(read[1].Visible);
    }

    [Fact]
    public void An_export_carries_the_layout_and_nothing_else()
    {
        // The point of a separate file is that a credential never travels with the arrangement.
        var json = DashboardLayoutFile.Write(new[] { Card("market", 0) });

        Assert.Contains("ednexus.dashboard-layout", json);
        Assert.Contains("market", json);
        Assert.DoesNotContain("ApiKey", json);
        Assert.DoesNotContain("InstallId", json);
        Assert.DoesNotContain("Inara", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ }")]
    [InlineData("[]")]
    public void Junk_is_rejected_rather_than_throwing(string? json)
        => Assert.Null(DashboardLayoutFile.TryRead(json));

    [Fact]
    public void Another_apps_json_is_rejected_by_the_kind_marker()
    {
        const string other = """{ "Kind": "some.other.app", "Version": 1, "Cards": [ { "Id": "market" } ] }""";
        Assert.Null(DashboardLayoutFile.TryRead(other));
    }

    [Fact]
    public void A_newer_format_version_is_rejected_rather_than_guessed_at()
    {
        var future = $$"""
        { "Kind": "{{DashboardLayoutFile.KindMarker}}", "Version": 99, "Cards": [ { "Id": "market" } ] }
        """;
        Assert.Null(DashboardLayoutFile.TryRead(future));
    }

    [Fact]
    public void Entries_without_an_id_are_dropped_because_they_match_no_card()
    {
        var json = $$"""
        { "Kind": "{{DashboardLayoutFile.KindMarker}}", "Version": 1,
          "Cards": [ { "Id": "", "Order": 0 }, { "Id": "market", "Order": 1 } ] }
        """;

        var read = DashboardLayoutFile.TryRead(json);
        Assert.NotNull(read);
        Assert.Equal("market", Assert.Single(read!).Id);
    }

    [Fact]
    public void A_document_with_no_usable_cards_is_treated_as_not_a_layout()
    {
        var json = $$"""{ "Kind": "{{DashboardLayoutFile.KindMarker}}", "Version": 1, "Cards": [] }""";
        Assert.Null(DashboardLayoutFile.TryRead(json));
    }

    [Fact]
    public void An_import_from_another_machine_still_merges_tolerantly()
    {
        // The exporting machine had a card this build no longer has, and lacked one this build added.
        var exported = DashboardLayoutFile.Write(new[]
        {
            Card("retired_card", 0),
            Card("market", 1, width: 920),
            Card("location", 2, visible: false),
        });

        var imported = DashboardLayoutFile.TryRead(exported);
        Assert.NotNull(imported);

        var merged = DashboardLayout.Merge(
            new[]
            {
                new CardDefaults("location", 452),
                new CardDefaults("market", 452),
                new CardDefaults("engineers", 452),
            },
            imported);

        Assert.Equal(new[] { "market", "location", "engineers" }, merged.Select(c => c.Id));
        Assert.Equal(920, merged.Single(c => c.Id == "market").Width);
        Assert.False(merged.Single(c => c.Id == "location").Visible);
        Assert.True(merged.Single(c => c.Id == "engineers").Visible);   // new card, never imported hidden
        Assert.DoesNotContain(merged, c => c.Id == "retired_card");
    }

    [Fact]
    public void An_imported_document_does_not_alias_the_layout_it_was_written_from()
    {
        var source = new[] { Card("market", 0, width: 452) };
        var read = DashboardLayoutFile.TryRead(DashboardLayoutFile.Write(source))!;

        read[0].Width = 920;
        Assert.Equal(452, source[0].Width);
    }
}
