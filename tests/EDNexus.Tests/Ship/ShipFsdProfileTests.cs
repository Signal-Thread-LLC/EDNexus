using EDNexus.Core.Journal;
using EDNexus.Core.Ship;
using Xunit;

namespace EDNexus.Tests.Ship;

public class ShipFsdProfileTests
{
    private static JournalEntry Loadout(string json)
    {
        Assert.True(JournalEntry.TryParse(json, historical: false, out var entry));
        return entry;
    }

    [Fact]
    public void Reads_stock_drive_constants_masses_and_guardian_booster()
    {
        var e = Loadout("""
        { "event": "Loadout", "Ship": "asp", "UnladenMass": 280.0, "CargoCapacity": 64,
          "MaxJumpRange": 37.0, "FuelCapacity": { "Main": 32.0, "Reserve": 0.63 },
          "Modules": [
            { "Slot": "FrameShiftDrive", "Item": "int_hyperdrive_size5_class5", "On": true },
            { "Slot": "Slot01_Size3", "Item": "int_guardianfsdbooster_size3", "On": true } ] }
        """);

        var fsd = ShipFsdProfile.FromLoadout(e);

        Assert.NotNull(fsd);
        Assert.Equal(1050, fsd!.OptimalMass);      // stock 5A optimal mass
        Assert.Equal(0.012, fsd.FuelMultiplier);
        Assert.Equal(2.45, fsd.FuelPower);
        Assert.Equal(5, fsd.MaxFuelPerJump);
        Assert.Equal(280.0, fsd.BaseMass);
        Assert.Equal(32.0, fsd.TankSize);
        Assert.Equal(0.63, fsd.ReserveSize);
        Assert.Equal(64, fsd.CargoCapacity);
        Assert.Equal(7.75, fsd.RangeBoost);        // size-3 Guardian booster
    }

    [Fact]
    public void Engineered_optimal_mass_and_max_fuel_override_the_stock_values()
    {
        var e = Loadout("""
        { "event": "Loadout", "UnladenMass": 300.0, "FuelCapacity": { "Main": 32.0, "Reserve": 0.63 },
          "Modules": [ { "Slot": "FrameShiftDrive", "Item": "int_hyperdrive_size5_class5", "On": true,
            "Engineering": { "Modifiers": [
              { "Label": "FSDOptimalMass", "Value": 1237.5 },
              { "Label": "MaxFuelPerJump", "Value": 5.5 } ] } } ] }
        """);

        var fsd = ShipFsdProfile.FromLoadout(e);

        Assert.NotNull(fsd);
        Assert.Equal(1237.5, fsd!.OptimalMass);    // engineered, not the stock 1050
        Assert.Equal(5.5, fsd.MaxFuelPerJump);
        Assert.Equal(2.45, fsd.FuelPower);         // rating constant is unchanged by engineering
    }

    [Fact]
    public void Sco_overcharge_drive_is_recognised()
    {
        var e = Loadout("""
        { "event": "Loadout", "UnladenMass": 400.0, "FuelCapacity": { "Main": 32.0, "Reserve": 0.63 },
          "Modules": [ { "Slot": "FrameShiftDrive", "Item": "int_hyperdrive_overcharge_size5_class5", "On": true } ] }
        """);

        var fsd = ShipFsdProfile.FromLoadout(e);

        Assert.NotNull(fsd);
        Assert.Equal(1175, fsd!.OptimalMass);
        Assert.Equal(0.013, fsd.FuelMultiplier);
    }

    [Fact]
    public void No_drive_yields_no_profile()
    {
        var e = Loadout("""
        { "event": "Loadout", "UnladenMass": 300.0, "FuelCapacity": { "Main": 32.0 },
          "Modules": [ { "Slot": "PowerPlant", "Item": "int_powerplant_size5_class5", "On": true } ] }
        """);

        Assert.Null(ShipFsdProfile.FromLoadout(e));
    }

    [Fact]
    public void Jump_range_falls_with_mass_and_matches_the_formula()
    {
        var fsd = new ShipFsdProfile(
            OptimalMass: 1050, BaseMass: 280, TankSize: 32, ReserveSize: 0.63,
            FuelMultiplier: 0.012, FuelPower: 2.45, MaxFuelPerJump: 5, RangeBoost: 0, CargoCapacity: 0);

        // range = optmass/mass * (1000*fuel/mul)^(1/power); fuel = min(maxfuel, tank) = 5.
        var expectedAt400 = 1050.0 / 400.0 * System.Math.Pow(1000 * 5 / 0.012, 1 / 2.45);
        Assert.Equal(expectedAt400, fsd.JumpRangeAt(400), 3);

        Assert.True(fsd.JumpRangeAt(300) > fsd.JumpRangeAt(500));   // heavier ships jump shorter
    }
}
