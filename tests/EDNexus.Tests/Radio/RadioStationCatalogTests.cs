using System.Linq;
using EDNexus.Core.Radio;
using Xunit;

namespace EDNexus.Tests.Radio;

public class RadioStationCatalogTests
{
    [Fact]
    public void The_catalog_ships_all_six_specified_stations_with_unique_ids_and_urls()
    {
        var stations = RadioStationCatalog.Stations;

        Assert.Equal(6, stations.Count);
        Assert.Equal(stations.Count, stations.Select(s => s.Id).Distinct().Count());
        Assert.Equal(stations.Count, stations.Select(s => s.StreamUrl).Distinct().Count());
        Assert.All(stations, s => Assert.False(string.IsNullOrWhiteSpace(s.StreamUrl)));
        Assert.All(stations, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));

        Assert.Contains(stations, s => s.Id == "radio-sidewinder");
        Assert.Contains(stations, s => s.Id == "hutton-orbital-radio");
        Assert.Contains(stations, s => s.Id == "simulator-radio");
        Assert.Contains(stations, s => s.Id == "somafm-deep-space-one");
        Assert.Contains(stations, s => s.Id == "somafm-space-station-soma");
        Assert.Contains(stations, s => s.Id == "somafm-mission-control");
    }

    [Fact]
    public void Find_is_case_insensitive_and_returns_null_for_unknown_or_empty_ids()
    {
        var found = RadioStationCatalog.Find("RADIO-SIDEWINDER");

        Assert.NotNull(found);
        Assert.Equal("radio-sidewinder", found!.Id);
        Assert.Null(RadioStationCatalog.Find("not-a-real-station"));
        Assert.Null(RadioStationCatalog.Find(null));
        Assert.Null(RadioStationCatalog.Find(""));
    }
}
