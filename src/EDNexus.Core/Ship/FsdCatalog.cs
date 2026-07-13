namespace EDNexus.Core.Ship;

/// <summary>
/// The Frame Shift Drive physics constants for every stock drive, keyed by the journal module symbol,
/// plus the Guardian FSD booster jump bonuses. Values are transcribed from the community
/// <c>coriolis-data</c> reference (the same source every third-party jump-range tool uses). Engineered
/// drives keep these base constants but override optimal mass / max fuel — the journal reports those
/// overrides on the module, so the catalog only needs the stock numbers.
/// </summary>
public static class FsdCatalog
{
    /// <summary>Stock physics for one FSD: optimal mass (t), max fuel per jump (t), and the rating's linear/power fuel constants.</summary>
    public readonly record struct Fsd(double OptimalMass, double MaxFuelPerJump, double FuelMultiplier, double FuelPower);

    // Symbols are matched case-insensitively; the journal emits them lower-cased already.
    private static readonly Dictionary<string, Fsd> Drives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["int_hyperdrive_size7_class1"] = new(1440, 8.5, 0.011, 2.75),
        ["int_hyperdrive_size7_class2"] = new(1620, 8.5, 0.01, 2.75),
        ["int_hyperdrive_size7_class3"] = new(1800, 8.5, 0.008, 2.75),
        ["int_hyperdrive_size7_class4"] = new(2250, 10.6, 0.01, 2.75),
        ["int_hyperdrive_size7_class5"] = new(2700, 12.8, 0.012, 2.75),
        ["int_hyperdrive_size6_class1"] = new(960, 5.3, 0.011, 2.6),
        ["int_hyperdrive_size6_class2"] = new(1080, 5.3, 0.01, 2.6),
        ["int_hyperdrive_size6_class3"] = new(1200, 5.3, 0.008, 2.6),
        ["int_hyperdrive_size6_class4"] = new(1500, 6.6, 0.01, 2.6),
        ["int_hyperdrive_size6_class5"] = new(1800, 8, 0.012, 2.6),
        ["int_hyperdrive_size5_class1"] = new(560, 3.3, 0.011, 2.45),
        ["int_hyperdrive_size5_class2"] = new(630, 3.3, 0.01, 2.45),
        ["int_hyperdrive_size5_class3"] = new(700, 3.3, 0.008, 2.45),
        ["int_hyperdrive_size5_class4"] = new(875, 4.1, 0.01, 2.45),
        ["int_hyperdrive_size5_class5"] = new(1050, 5, 0.012, 2.45),
        ["int_hyperdrive_size4_class1"] = new(280, 2, 0.011, 2.3),
        ["int_hyperdrive_size4_class2"] = new(315, 2, 0.01, 2.3),
        ["int_hyperdrive_size4_class3"] = new(350, 2, 0.008, 2.3),
        ["int_hyperdrive_size4_class4"] = new(437.5, 2.5, 0.01, 2.3),
        ["int_hyperdrive_size4_class5"] = new(525, 3, 0.012, 2.3),
        ["int_hyperdrive_size3_class1"] = new(80, 1.2, 0.011, 2.15),
        ["int_hyperdrive_size3_class2"] = new(90, 1.2, 0.01, 2.15),
        ["int_hyperdrive_size3_class3"] = new(100, 1.2, 0.008, 2.15),
        ["int_hyperdrive_size3_class4"] = new(125, 1.5, 0.01, 2.15),
        ["int_hyperdrive_size3_class5"] = new(150, 1.8, 0.012, 2.15),
        ["int_hyperdrive_size2_class1"] = new(48, 0.6, 0.011, 2),
        ["int_hyperdrive_size2_class2"] = new(54, 0.6, 0.01, 2),
        ["int_hyperdrive_size2_class3"] = new(60, 0.6, 0.008, 2),
        ["int_hyperdrive_size2_class4"] = new(75, 0.8, 0.01, 2),
        ["int_hyperdrive_size2_class5"] = new(90, 0.9, 0.012, 2),
        // SCO (V1 "overcharge") drives.
        ["int_hyperdrive_overcharge_size8_class5_overchargebooster_mkii"] = new(4670, 6.8, 0.011, 2.5025),
        ["int_hyperdrive_overcharge_size8_class5"] = new(4670, 20.7, 0.013, 2.9),
        ["int_hyperdrive_overcharge_size8_class4"] = new(4200, 20.4, 0.012, 2.9),
        ["int_hyperdrive_overcharge_size8_class3"] = new(4200, 20.4, 0.012, 2.9),
        ["int_hyperdrive_overcharge_size8_class2"] = new(4200, 20.4, 0.012, 2.9),
        ["int_hyperdrive_overcharge_size8_class1"] = new(2800, 13.6, 0.008, 2.9),
        ["int_hyperdrive_overcharge_size7_class5"] = new(3000, 13.1, 0.013, 2.75),
        ["int_hyperdrive_overcharge_size7_class4"] = new(2700, 12.8, 0.012, 2.75),
        ["int_hyperdrive_overcharge_size7_class3"] = new(2700, 12.8, 0.012, 2.75),
        ["int_hyperdrive_overcharge_size7_class2"] = new(2700, 12.8, 0.012, 2.75),
        ["int_hyperdrive_overcharge_size7_class1"] = new(1800, 8.5, 0.008, 2.75),
        ["int_hyperdrive_overcharge_size6_class5"] = new(2000, 8.3, 0.013, 2.6),
        ["int_hyperdrive_overcharge_size6_class4"] = new(1800, 8, 0.012, 2.6),
        ["int_hyperdrive_overcharge_size6_class3"] = new(1800, 8, 0.012, 2.6),
        ["int_hyperdrive_overcharge_size6_class2"] = new(1800, 8, 0.012, 2.6),
        ["int_hyperdrive_overcharge_size6_class1"] = new(1200, 5.3, 0.008, 2.6),
        ["int_hyperdrive_overcharge_size5_class5"] = new(1175, 5.2, 0.013, 2.45),
        ["int_hyperdrive_overcharge_size5_class4"] = new(1050, 5, 0.012, 2.45),
        ["int_hyperdrive_overcharge_size5_class3"] = new(1050, 5, 0.012, 2.45),
        ["int_hyperdrive_overcharge_size5_class2"] = new(1050, 5, 0.012, 2.45),
        ["int_hyperdrive_overcharge_size5_class1"] = new(700, 3.3, 0.008, 2.45),
        ["int_hyperdrive_overcharge_size4_class5"] = new(585, 3.2, 0.013, 2.3),
        ["int_hyperdrive_overcharge_size4_class4"] = new(525, 3, 0.012, 2.3),
        ["int_hyperdrive_overcharge_size4_class3"] = new(525, 3, 0.012, 2.3),
        ["int_hyperdrive_overcharge_size4_class2"] = new(525, 3, 0.012, 2.3),
        ["int_hyperdrive_overcharge_size4_class1"] = new(350, 2, 0.008, 2.3),
        ["int_hyperdrive_overcharge_size3_class5"] = new(167, 1.9, 0.013, 2.15),
        ["int_hyperdrive_overcharge_size3_class4"] = new(150, 1.8, 0.012, 2.15),
        ["int_hyperdrive_overcharge_size3_class3"] = new(150, 1.8, 0.012, 2.15),
        ["int_hyperdrive_overcharge_size3_class2"] = new(150, 1.8, 0.012, 2.15),
        ["int_hyperdrive_overcharge_size3_class1"] = new(100, 1.2, 0.008, 2.15),
        ["int_hyperdrive_overcharge_size2_class5"] = new(100, 1, 0.013, 2.0),
        ["int_hyperdrive_overcharge_size2_class4"] = new(90, 0.9, 0.012, 2.0),
        ["int_hyperdrive_overcharge_size2_class3"] = new(90, 0.9, 0.012, 2.0),
        ["int_hyperdrive_overcharge_size2_class2"] = new(90, 0.9, 0.012, 2.0),
        ["int_hyperdrive_overcharge_size2_class1"] = new(60, 0.6, 0.008, 2.0),
    };

    // Guardian FSD booster flat jump bonus (ly), by module size.
    private static readonly Dictionary<string, double> GuardianBoosters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["int_guardianfsdbooster_size1"] = 4.0,
        ["int_guardianfsdbooster_size2"] = 6.0,
        ["int_guardianfsdbooster_size3"] = 7.75,
        ["int_guardianfsdbooster_size4"] = 9.25,
        ["int_guardianfsdbooster_size5"] = 10.5,
    };

    /// <summary>
    /// Look up a drive by journal module symbol. Falls back to the longest known symbol the item starts
    /// with, so an unrecognised engineered/experimental suffix still resolves to the right base drive.
    /// Returns null for a non-FSD symbol.
    /// </summary>
    public static Fsd? Lookup(string symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return null;
        if (Drives.TryGetValue(symbol, out var exact)) return exact;

        Fsd? best = null;
        var bestLen = 0;
        foreach (var (key, value) in Drives)
            if (key.Length > bestLen && symbol.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                (best, bestLen) = (value, key.Length);
        return best;
    }

    /// <summary>The Guardian FSD booster jump bonus for the given module symbol, or null if it isn't one.</summary>
    public static double? GuardianBoosterBoost(string symbol)
        => GuardianBoosters.TryGetValue(symbol, out var boost) ? boost : null;
}
