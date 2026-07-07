using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EDNexus.Tests.Reporting;   // reuse the shared RecordingHandler test double
using EliteDangerous.Edsm;
using Xunit;

namespace EDNexus.Tests.Navigation;

public class EdsmClientTests
{
    private static readonly EdsmClientOptions Options = new()
    {
        SoftwareName = "EDNexus.Tests",
        SoftwareVersion = "1.0.0",
    };

    private static EdsmClient NewClient(RecordingHandler handler) => new(Options, new HttpClient(handler));

    [Fact]
    public async Task Parses_a_known_system_with_coordinates()
    {
        using var client = NewClient(new RecordingHandler(
            body: """{ "name": "Sol", "coords": { "x": 0, "y": 0, "z": 0 }, "coordsLocked": true }"""));

        var result = await client.GetSystemAsync("Sol");

        Assert.True(result.IsOk);
        Assert.NotNull(result.Value);
        Assert.Equal("Sol", result.Value!.Name);
        Assert.Equal(new EdsmCoords(0, 0, 0), result.Value.Coords);
    }

    [Fact]
    public async Task Unknown_system_is_ok_with_a_null_value()
    {
        // EDSM answers "not found" with an empty array where a hit would be an object.
        using var client = NewClient(new RecordingHandler(body: "[]"));

        var result = await client.GetSystemAsync("Nowhere");

        Assert.True(result.IsOk);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Nearby_systems_are_returned_nearest_first()
    {
        // Deliberately out of distance order — the client must sort them.
        using var client = NewClient(new RecordingHandler(body: """
        [ { "name": "Barnard's Star", "coords": { "x": -3, "y": 1, "z": -3 }, "distance": 5.9 },
          { "name": "Alpha Centauri", "coords": { "x": 3, "y": -0.1, "z": 3 }, "distance": 4.38 } ]
        """));

        var result = await client.GetNearbySystemsAsync("Sol", 20);

        Assert.True(result.IsOk);
        Assert.Collection(result.Value!,
            s => Assert.Equal("Alpha Centauri", s.Name),
            s => Assert.Equal("Barnard's Star", s.Name));
        Assert.Equal(4.38, result.Value![0].DistanceLy);
    }

    [Fact]
    public async Task Nearby_clamps_radius_to_the_edsm_ceiling()
    {
        var handler = new RecordingHandler(body: "[]");
        using var client = NewClient(handler);

        await client.GetNearbySystemsAsync("Sol", 5000);

        Assert.Contains("radius=100", handler.Uris[0]!.ToString());
    }

    [Fact]
    public async Task Parses_system_bodies()
    {
        using var client = NewClient(new RecordingHandler(body: """
        { "name": "Sol", "bodyCount": 2, "bodies": [
            { "name": "Earth", "type": "Planet", "subType": "Earth-like world", "isLandable": false, "distanceToArrival": 499.9 },
            { "name": "Mars",  "type": "Planet", "subType": "High metal content world", "isLandable": true, "distanceToArrival": 750.1 } ] }
        """));

        var result = await client.GetBodiesAsync("Sol");

        Assert.True(result.IsOk);
        Assert.Equal("Sol", result.Value!.SystemName);
        Assert.Equal(2, result.Value.Bodies.Count);
        var mars = Assert.Single(result.Value.Bodies, b => b.Name == "Mars");
        Assert.True(mars.IsLandable);
        Assert.Equal(750.1, mars.DistanceToArrivalLs);
    }

    [Fact]
    public async Task Non_success_status_is_a_failure_not_an_exception()
    {
        using var client = NewClient(new RecordingHandler(HttpStatusCode.InternalServerError, "boom"));

        var result = await client.GetSystemAsync("Sol");

        Assert.False(result.IsOk);
        Assert.Contains("500", result.Error);
    }
}
