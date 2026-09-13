using System.IO.Compression;
using System.Text;
using EDNexus.Core.Ship;
using Xunit;

namespace EDNexus.Tests.Ship;

public class CoriolisShareLinkTests
{
    private const string LoadoutJson = """
    { "event": "Loadout", "Ship": "asp", "ShipName": "Test", "ShipIdent": "AB-12C",
      "UnladenMass": 280.0, "CargoCapacity": 64, "MaxJumpRange": 37.0,
      "FuelCapacity": { "Main": 32.0, "Reserve": 0.63 },
      "Modules": [
        { "Slot": "FrameShiftDrive", "Item": "int_hyperdrive_size5_class5", "On": true, "Priority": 0 } ] }
    """;

    [Fact]
    public void Builds_an_import_link_that_decodes_back_to_the_loadout()
    {
        var url = CoriolisShareLink.Build(LoadoutJson);

        Assert.NotNull(url);
        Assert.StartsWith("https://coriolis.io/import?data=", url);

        var decoded = Decode(url!["https://coriolis.io/import?data=".Length..]);
        Assert.Contains("\"event\"", decoded);
        Assert.Contains("\"Loadout\"", decoded);
        Assert.Contains("int_hyperdrive_size5_class5", decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "event": "Docked" }""")]
    [InlineData("""{ "event": "Loadout", "Modules": [] }""")]
    public void Refuses_input_that_is_not_a_populated_loadout(string? json)
    {
        Assert.Null(CoriolisShareLink.Build(json));
    }

    private static string Decode(string urlEncoded)
    {
        var base64 = urlEncoded.Replace("%3D", "=").Replace('-', '+').Replace('_', '/');
        var gzipped = Convert.FromBase64String(base64);
        using var input = new MemoryStream(gzipped);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
