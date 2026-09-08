using EDNexus.Core.Settings;
using Xunit;

namespace EDNexus.Tests.Settings;

public class RouteSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ednexus-route-settings-").FullName;
    private string Path => System.IO.Path.Combine(_root, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void A_fresh_settings_file_has_no_saved_route()
    {
        var settings = new SettingsStore(Path).Load();

        Assert.Null(settings.Route.From);
        Assert.Empty(settings.Route.Hops);
    }

    [Fact]
    public void A_saved_route_round_trips_through_disk()
    {
        var store = new SettingsStore(Path);
        var settings = store.Load();
        settings.Route = new RouteSettings
        {
            From = "Sol",
            To = "Alpha Centauri",
            Mode = "FleetCarrier",
            JumpRangeText = "48.5",
            StepIndex = 2,
            Hops =
            {
                new SavedRouteHop { System = "Sol", Jumps = 0, DistanceRemainingLy = 4.4 },
                new SavedRouteHop
                {
                    System = "Alpha Centauri", Jumps = 1, DistanceJumpedLy = 4.4,
                    FuelUsed = 12.3, FuelInTank = 987.7, MustRestock = true, HasIcyRing = true,
                },
            },
        };
        store.Save(settings);

        var reloaded = new SettingsStore(Path).Load();

        Assert.Equal("Sol", reloaded.Route.From);
        Assert.Equal("Alpha Centauri", reloaded.Route.To);
        Assert.Equal("FleetCarrier", reloaded.Route.Mode);
        Assert.Equal("48.5", reloaded.Route.JumpRangeText);
        Assert.Equal(2, reloaded.Route.StepIndex);
        Assert.Equal(2, reloaded.Route.Hops.Count);
        Assert.Equal("Alpha Centauri", reloaded.Route.Hops[1].System);
        Assert.Equal(12.3, reloaded.Route.Hops[1].FuelUsed);
        Assert.True(reloaded.Route.Hops[1].MustRestock);
        Assert.True(reloaded.Route.Hops[1].HasIcyRing);
    }

    [Fact]
    public void Clearing_the_route_persists_as_empty()
    {
        var store = new SettingsStore(Path);
        var settings = store.Load();
        settings.Route = new RouteSettings { From = "Sol", To = "Sag A*", Hops = { new SavedRouteHop { System = "Sol" } } };
        store.Save(settings);

        settings.Route = new RouteSettings();
        store.Save(settings);

        var reloaded = new SettingsStore(Path).Load();

        Assert.Null(reloaded.Route.From);
        Assert.Empty(reloaded.Route.Hops);
    }
}
