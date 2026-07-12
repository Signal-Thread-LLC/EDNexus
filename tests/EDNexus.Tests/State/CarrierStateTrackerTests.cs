using EDNexus.Core.Journal;
using EDNexus.Core.State;
using Xunit;

namespace EDNexus.Tests.State;

public class CarrierStateTrackerTests
{
    private static (JournalEventBus bus, CommanderState state) NewTracker()
    {
        var bus = new JournalEventBus();
        var state = new CommanderState();
        _ = new StateTracker(bus, state);
        return (bus, state);
    }

    private static void Publish(JournalEventBus bus, string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var entry), "sample JSON failed to parse");
        bus.Publish(entry);
    }

    [Fact]
    public void CarrierStats_updates_tritium_and_jump_range()
    {
        var (bus, state) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-07-12T02:55:06Z", "event":"CarrierStats", "FuelLevel":638, "JumpRangeCurr":500.0 }""");

        Assert.Equal(638, state.CarrierFuel);
        Assert.Equal(500.0, state.CarrierJumpRange);
    }

    [Fact]
    public void CarrierJumpRequest_records_pending_destination_and_departure()
    {
        var (bus, state) = NewTracker();
        Publish(bus, """
        { "timestamp":"2026-07-12T02:55:01Z", "event":"CarrierJumpRequest",
          "SystemName":"Ga Gu", "Body":"Ga Gu 2 a", "DepartureTime":"2026-07-12T03:11:10Z" }
        """);

        Assert.Equal("Ga Gu", state.CarrierPendingSystem);
        Assert.Equal(new DateTimeOffset(2026, 7, 12, 3, 11, 10, TimeSpan.Zero), state.CarrierPendingDeparture);
    }

    [Fact]
    public void Completing_the_carrier_jump_clears_the_pending_request()
    {
        var (bus, state) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-07-12T02:55:01Z", "event":"CarrierJumpRequest", "SystemName":"Ga Gu", "DepartureTime":"2026-07-12T03:11:10Z" }""");
        Assert.Equal("Ga Gu", state.CarrierPendingSystem);

        Publish(bus, """{ "timestamp":"2026-07-12T03:11:10Z", "event":"CarrierJump", "StarSystem":"Ga Gu", "Body":"Ga Gu 2 a" }""");

        Assert.Equal("Ga Gu", state.StarSystem);
        Assert.Null(state.CarrierPendingSystem);
        Assert.Null(state.CarrierPendingDeparture);
    }

    [Fact]
    public void Cancelling_the_carrier_jump_clears_the_pending_request()
    {
        var (bus, state) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-07-12T02:55:01Z", "event":"CarrierJumpRequest", "SystemName":"Ga Gu", "DepartureTime":"2026-07-12T03:11:10Z" }""");
        Publish(bus, """{ "timestamp":"2026-07-12T02:57:00Z", "event":"CarrierJumpCancelled" }""");

        Assert.Null(state.CarrierPendingSystem);
        Assert.Null(state.CarrierPendingDeparture);
    }

    [Fact]
    public void A_ship_FSDJump_does_not_clear_a_pending_carrier_jump()
    {
        var (bus, state) = NewTracker();
        Publish(bus, """{ "timestamp":"2026-07-12T02:55:01Z", "event":"CarrierJumpRequest", "SystemName":"Ga Gu", "DepartureTime":"2026-07-12T03:11:10Z" }""");
        Publish(bus, """{ "timestamp":"2026-07-12T02:58:00Z", "event":"FSDJump", "StarSystem":"Merope" }""");

        Assert.Equal("Merope", state.StarSystem);
        Assert.Equal("Ga Gu", state.CarrierPendingSystem);   // the carrier is still booked to move
    }
}
