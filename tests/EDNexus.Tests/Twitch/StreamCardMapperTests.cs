using System.Text;
using System.Text.Json;
using EDNexus.Core.State;
using EDNexus.Core.Twitch;
using Xunit;

namespace EDNexus.Tests.Twitch;

public class StreamCardMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Docked_headline_shows_the_station_display_name()
    {
        var state = new CommanderState
        {
            Docked = true,
            StationName = "Jameson Memorial",
            StarSystem = "Shinrarta Dezhra",
        };

        var card = StreamCardMapper.Map(state, now: Now);

        Assert.Equal("Docked at Jameson Memorial", card.Headline);
    }

    [Fact]
    public void Docked_at_own_carrier_headlines_the_carrier_name_not_the_callsign()
    {
        var state = new CommanderState
        {
            Docked = true,
            StationName = "K7Q-B3L",
            CarrierName = "Gone Sampling",
            CarrierCallsign = "K7Q-B3L",
        };

        var card = StreamCardMapper.Map(state, now: Now);

        Assert.Equal("Docked at Gone Sampling", card.Headline);
    }

    [Fact]
    public void Undocked_headline_pairs_the_system_with_a_distinct_body()
    {
        var state = new CommanderState { StarSystem = "Nervi", Body = "Nervi 2 a" };

        Assert.Equal("Nervi / Nervi 2 a", StreamCardMapper.Map(state, now: Now).Headline);
    }

    [Fact]
    public void Undocked_headline_omits_a_body_that_repeats_the_system()
    {
        var state = new CommanderState { StarSystem = "Sol", Body = "Sol" };

        Assert.Equal("Sol", StreamCardMapper.Map(state, now: Now).Headline);
    }

    [Fact]
    public void No_known_location_falls_back_to_a_generic_headline()
    {
        Assert.Equal("In the black", StreamCardMapper.Map(new CommanderState(), now: Now).Headline);
    }

    [Fact]
    public void Credits_are_withheld_by_default()
    {
        var state = new CommanderState { Name = "Hadfield", Balance = 1_842_900_311 };

        var card = StreamCardMapper.Map(state, now: Now);

        Assert.Equal("Hadfield", card.Commander!.Name);
        Assert.Null(card.Commander.Credits);
    }

    [Fact]
    public void Credits_are_published_once_the_broadcaster_opts_in()
    {
        var state = new CommanderState { Balance = 1_842_900_311 };
        var visibility = StreamCardVisibility.Default with { Credits = true };

        var card = StreamCardMapper.Map(state, visibility: visibility, now: Now);

        Assert.Equal(1_842_900_311, card.Commander!.Credits);
    }

    [Fact]
    public void A_hidden_section_is_absent_from_the_payload_entirely()
    {
        var state = new CommanderState
        {
            StarSystem = "Nervi",
            Ship = "Krait Phantom",
            Docked = true,
            StationName = "Jameson Memorial",
        };
        var visibility = StreamCardVisibility.Default with { Location = false };

        var card = StreamCardMapper.Map(state, visibility: visibility, now: Now);
        var json = JsonSerializer.Serialize(card, StreamCardSnapshot.SerializerOptions);

        Assert.Null(card.Location);
        // Not merely null on the wire — the property (and the station name inside it) never leaves
        // the commander's machine.
        Assert.DoesNotContain("\"loc\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Jameson Memorial", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Hiding_the_location_also_drops_it_from_the_headline()
    {
        var state = new CommanderState { Docked = true, StationName = "Jameson Memorial", StarSystem = "Sol" };
        var visibility = StreamCardVisibility.Default with { Location = false };

        var card = StreamCardMapper.Map(state, visibility: visibility, now: Now);

        Assert.Equal("Elite Dangerous", card.Headline);
    }

    [Fact]
    public void Station_details_are_dropped_once_the_commander_undocks()
    {
        var state = new CommanderState { StarSystem = "Sol", StationName = "Abraham Lincoln", StationType = "Orbis" };

        var card = StreamCardMapper.Map(state, now: Now);

        Assert.False(card.Location!.Docked);
        Assert.Null(card.Location.Station);
        Assert.Null(card.Location.StationType);
    }

    [Fact]
    public void Carrier_is_omitted_until_one_has_been_seen()
    {
        Assert.Null(StreamCardMapper.Map(new CommanderState(), now: Now).Carrier);
    }

    [Fact]
    public void Carrier_reports_its_pending_jump()
    {
        var departure = Now.AddMinutes(15);
        var state = new CommanderState
        {
            CarrierName = "Gone Sampling",
            CarrierCallsign = "K7Q-B3L",
            CarrierFuel = 812,
            CarrierJumpRange = 500,
            CarrierPendingSystem = "Colonia",
            CarrierPendingDeparture = departure,
        };

        var carrier = StreamCardMapper.Map(state, now: Now).Carrier!;

        Assert.Equal("Gone Sampling", carrier.Name);
        Assert.Equal("Colonia", carrier.PendingSystem);
        Assert.Equal(departure, carrier.DepartsAt);
    }

    [Fact]
    public void Cargo_lists_the_biggest_lots_first_and_is_capped()
    {
        var state = new CommanderState();
        for (var i = 0; i < StreamCardMapper.MaxCargoItems + 5; i++)
            state.Cargo[$"Commodity{i}"] = i + 1;

        var cargo = StreamCardMapper.Map(state, now: Now).Cargo!;

        Assert.Equal(StreamCardMapper.MaxCargoItems, cargo.Count);
        Assert.Equal(StreamCardMapper.MaxCargoItems + 5, cargo[0].Tons);
        Assert.True(cargo[0].Tons > cargo[^1].Tons);
    }

    [Fact]
    public void Empty_sections_are_omitted_rather_than_sent_blank()
    {
        var card = StreamCardMapper.Map(new CommanderState(), now: Now);

        Assert.Null(card.Cargo);
        Assert.Null(card.Exobiology);
        Assert.Null(card.Mining);
        Assert.Null(card.Missions);
    }

    [Fact]
    public void A_full_card_stays_inside_the_five_kibibyte_pubsub_cap()
    {
        var state = new CommanderState
        {
            Name = "Hadfield",
            Balance = 1_842_900_311,
            Ship = "Krait Phantom",
            ShipName = "Wandering Albatross",
            ShipIdent = "PH-01",
            StarSystem = "Hypiae Aescs FB-W c1-1046",
            Body = "Hypiae Aescs FB-W c1-1046 A 3 f",
            FuelMain = 9.4,
            FuelCapacity = 32,
            CargoTons = 96,
            CarrierName = "Gone Sampling",
            CarrierCallsign = "K7Q-B3L",
            CarrierFuel = 812,
            CarrierJumpRange = 500,
            CarrierPendingSystem = "Colonia",
            CarrierPendingDeparture = Now.AddMinutes(15),
        };
        for (var i = 0; i < StreamCardMapper.MaxCargoItems; i++)
            state.Cargo[$"Low Temperature Diamonds {i}"] = 64;

        var visibility = StreamCardVisibility.Default with { Credits = true };
        var json = JsonSerializer.Serialize(
            StreamCardMapper.Map(state, visibility: visibility, now: Now), StreamCardSnapshot.SerializerOptions);

        // Twitch rejects a PubSub message over 5 KiB outright, and the EBS refuses one before it
        // ever gets there — so the shape the mapper emits has to fit with room to spare.
        Assert.True(Encoding.UTF8.GetByteCount(json) < 5000, $"Payload was {Encoding.UTF8.GetByteCount(json)} bytes.");
    }

    [Fact]
    public void Fingerprint_ignores_the_timestamp_but_not_the_content()
    {
        var state = new CommanderState { StarSystem = "Sol" };

        var first = StreamCardMapper.Map(state, now: Now);
        var later = StreamCardMapper.Map(state, now: Now.AddMinutes(5));

        Assert.Equal(first.ContentFingerprint(), later.ContentFingerprint());

        state.StarSystem = "Nervi";
        Assert.NotEqual(first.ContentFingerprint(), StreamCardMapper.Map(state, now: Now).ContentFingerprint());
    }
}
