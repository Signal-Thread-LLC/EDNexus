using System.Text.Json;
using EliteDangerous.Eddn;
using Xunit;

namespace EDNexus.Tests.Reporting;

public class EddnTransformerTests
{
    private static readonly EddnClientOptions Options = new()
    {
        SoftwareName = "EDNexus.Tests",
        SoftwareVersion = "1.0.0",
    };

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    private static (EddnJournalTransformer, EddnState) NewPair()
    {
        var state = new EddnState();
        return (new EddnJournalTransformer(Options), state);
    }

    [Fact]
    public void Journal_event_strips_localised_and_private_fields_and_keeps_location()
    {
        var (t, state) = NewPair();
        var raw = Json("""
        {
          "timestamp": "2020-01-01T00:00:00Z", "event": "FSDJump",
          "StarSystem": "Sol", "SystemAddress": 10477373803, "StarPos": [0.0, 0.0, 0.0],
          "JumpDist": 8.5, "FuelUsed": 1.2, "FuelLevel": 30.0,
          "SystemFaction": { "Name": "Foo", "Name_Localised": "Foo Loc" }
        }
        """);
        state.Observe(raw);

        var msg = t.Transform(raw, state);

        Assert.NotNull(msg);
        var message = msg!.Envelope["message"]!;
        Assert.Equal("Sol", (string?)message["StarSystem"]);
        Assert.NotNull(message["SystemAddress"]);
        Assert.NotNull(message["StarPos"]);
        // Private / financial / positional keys must be gone.
        Assert.Null(message["JumpDist"]);
        Assert.Null(message["FuelUsed"]);
        Assert.Null(message["FuelLevel"]);
        // No key anywhere may end in _Localised.
        Assert.DoesNotContain("_Localised", msg.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Docked_gets_starpos_augmented_from_prior_jump()
    {
        var (t, state) = NewPair();
        // A jump establishes the current system's coordinates.
        state.Observe(Json("""
        { "timestamp": "2020-01-01T00:00:00Z", "event": "FSDJump",
          "StarSystem": "Sol", "SystemAddress": 10477373803, "StarPos": [1.0, 2.0, 3.0] }
        """));

        var docked = Json("""
        { "timestamp": "2020-01-01T00:05:00Z", "event": "Docked",
          "StarSystem": "Sol", "SystemAddress": 10477373803,
          "StationName": "Abraham Lincoln", "MarketID": 128666762, "ActiveFine": true }
        """);
        state.Observe(docked);

        var msg = t.Transform(docked, state);

        Assert.NotNull(msg);
        var message = msg!.Envelope["message"]!;
        var pos = message["StarPos"]!.AsArray();
        Assert.Equal(3, pos.Count);
        Assert.Equal(1.0, (double)pos[0]!);
        Assert.Null(message["ActiveFine"]);   // private field stripped
    }

    [Fact]
    public void Event_in_different_system_without_coordinates_is_dropped()
    {
        var (t, state) = NewPair();
        state.Observe(Json("""
        { "timestamp": "2020-01-01T00:00:00Z", "event": "FSDJump",
          "StarSystem": "Sol", "SystemAddress": 1, "StarPos": [0.0, 0.0, 0.0] }
        """));

        // A scan tagged with a different system but no coordinates: we can't know its StarPos, so
        // borrowing the previous system's would be wrong — the message must be dropped.
        var scan = Json("""
        { "timestamp": "2020-01-01T00:01:00Z", "event": "Scan",
          "StarSystem": "Alpha Centauri", "SystemAddress": 2, "BodyName": "AC 1" }
        """);
        state.Observe(scan);

        Assert.Null(t.Transform(scan, state));
    }

    [Fact]
    public void Non_whitelisted_event_produces_no_message()
    {
        var (t, state) = NewPair();
        var raw = Json("""{ "timestamp": "2020-01-01T00:00:00Z", "event": "Music", "MusicTrack": "MainMenu" }""");
        state.Observe(raw);
        Assert.Null(t.Transform(raw, state));
    }

    [Fact]
    public void Market_event_becomes_commodity_message_with_cleaned_names()
    {
        var (t, state) = NewPair();
        var raw = Json("""
        {
          "timestamp": "2020-01-01T00:00:00Z", "event": "Market",
          "MarketID": 128666762, "StationName": "Abraham Lincoln", "StarSystem": "Sol",
          "Items": [
            { "id": 1, "Name": "$gold_name;", "Name_Localised": "Gold",
              "BuyPrice": 100, "SellPrice": 90, "MeanPrice": 95,
              "StockBracket": 2, "DemandBracket": 0, "Stock": 10, "Demand": 0 }
          ]
        }
        """);
        state.Observe(raw);

        var msg = t.Transform(raw, state);

        Assert.NotNull(msg);
        Assert.Equal(EddnSchemas.Commodity, msg!.SchemaRef);
        var commodities = msg.Envelope["message"]!["commodities"]!.AsArray();
        Assert.Single(commodities);
        Assert.Equal("gold", (string?)commodities[0]!["name"]);
        Assert.Equal(100L, (long)commodities[0]!["buyPrice"]!);
        Assert.DoesNotContain("_Localised", msg.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Header_carries_software_identity_and_uploader()
    {
        var (t, state) = NewPair();
        state.Observe(Json("""{ "timestamp": "2020-01-01T00:00:00Z", "event": "Commander", "Name": "Jameson" }"""));
        var raw = Json("""
        { "timestamp": "2020-01-01T00:00:00Z", "event": "FSDJump",
          "StarSystem": "Sol", "SystemAddress": 1, "StarPos": [0.0, 0.0, 0.0] }
        """);
        state.Observe(raw);

        var header = t.Transform(raw, state)!.Envelope["header"]!;

        Assert.Equal("Jameson", (string?)header["uploaderID"]);
        Assert.Equal("EDNexus.Tests", (string?)header["softwareName"]);
        Assert.Equal("1.0.0", (string?)header["softwareVersion"]);
    }
}
