using System.Text.Json.Nodes;

namespace EDNexus.Core.Dev;

/// <summary>Shared, realistic name pools so the samplers emit plausible Elite Dangerous data.</summary>
internal static class SamplePools
{
    public static readonly string[] Systems =
    {
        "Sol", "Shinrarta Dezhra", "Deciat", "Colonia", "Maia", "Diaguandri", "Ratraii",
        "HIP 22460", "Synuefe XR-H d11-102", "Sagittarius A*", "Beagle Point", "Jackson's Lighthouse",
    };

    public static readonly string[] BodySuffixes = { " A 1", " A 2 a", " B 3", " 5 c", " AB 1 a", " 2", " A" };

    public static readonly string[] Stations =
    {
        "Jameson Memorial", "Ray Gateway", "Dubois Orbital", "Ehrenfried Gateway", "Garay Terminal",
        "Q3Z-BQL", "Robigo Mines", "Farseer Inc", "Ackerman Market",
    };

    // (journal symbol, localised) — the game stores the symbol in Ship and the label in Ship_Localised.
    public static readonly (string Sym, string Loc)[] Ships =
    {
        ("anaconda", "Anaconda"), ("python", "Python"), ("federation_corvette", "Federal Corvette"),
        ("cutter", "Imperial Cutter"), ("krait_mkii", "Krait Mk II"), ("type9", "Type-9 Heavy"),
        ("asp", "Asp Explorer"), ("cobramkiii", "Cobra Mk III"), ("ferdelance", "Fer-de-Lance"),
    };

    public static readonly string[] ShipNames =
    {
        "Massive Bone Yard", "Stellar Nomad", "Void Runner", "Iron Duke", "Nightingale",
        "Sual's Fortune", "Deep Six", "Halcyon", "Wandering Star", "Last Light",
    };

    public static readonly string[] Commanders =
    {
        "Demortes", "Jameson", "Aisling", "Salome", "Zorgon", "Brace", "Nova", "Ryder", "Kaz", "Halsey",
    };

    public static readonly string[] RawMaterials =
    {
        "iron", "nickel", "carbon", "sulphur", "phosphorus", "manganese",
        "zinc", "chromium", "vanadium", "tin", "arsenic", "cadmium",
    };

    public static readonly string[] ManufacturedMaterials =
    {
        "mechanicalcomponents", "heatdispersionplate", "gridresistors", "conductivecomponents",
        "shieldemitters", "chemicalprocessors", "focuscrystals", "compoundshielding", "militarygradealloys",
    };

    public static readonly string[] EncodedMaterials =
    {
        "shielddensityreports", "scrambledemissiondata", "bulkscandata", "disruptedwakeechoes",
        "emissiondata", "wakesolutions", "legacyfirmware", "hyperspacetrajectories",
    };

    // (journal symbol, localised) for ordinary trade commodities.
    public static readonly (string Sym, string Loc)[] Commodities =
    {
        ("gold", "Gold"), ("silver", "Silver"), ("palladium", "Palladium"), ("tritium", "Tritium"),
        ("painite", "Painite"), ("lowtemperaturediamond", "Low Temperature Diamonds"),
        ("bertrandite", "Bertrandite"), ("beryllium", "Beryllium"), ("water", "Water"),
        ("agriculturalmedicines", "Agricultural Medicines"),
    };

    // (journal symbol, localised) for the commodities colonisation depots ask for.
    public static readonly (string Sym, string Loc)[] Construction =
    {
        ("aluminium", "Aluminium"), ("steel", "Steel"), ("titanium", "Titanium"),
        ("cmmcomposite", "CMM Composite"), ("ceramiccomposites", "Ceramic Composites"),
        ("computercomponents", "Computer Components"), ("copper", "Copper"),
        ("foodcartridges", "Food Cartridges"), ("fruitandvegetables", "Fruit and Vegetables"),
        ("insulatingmembrane", "Insulating Membrane"), ("liquidoxygen", "Liquid oxygen"),
        ("medicaldiagnosticequipment", "Medical Diagnostic Equipment"),
        ("nonlethalweapons", "Non-Lethal Weapons"), ("polymers", "Polymers"),
        ("powergenerators", "Power Generators"), ("semiconductors", "Semiconductors"),
        ("superconductors", "Superconductors"), ("water", "Water"),
        ("waterpurifiers", "Water Purifiers"), ("titanium", "Titanium"),
    };

    public static readonly string[] ConstructionSites =
    {
        "Born's Pride", "New Horizon", "Kepler's Rest", "Vanguard Foothold", "Aurora Landing",
        "Meridian Anchorage", "Halifax Reach",
    };

    /// <summary>A ship ident like "DE-19L".</summary>
    public static string Ident(Random rng)
    {
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string digits = "0123456789";
        return $"{letters[rng.Next(26)]}{letters[rng.Next(26)]}-{digits[rng.Next(10)]}{letters[rng.Next(26)]}";
    }

    /// <summary>Pick <paramref name="count"/> distinct entries from a list.</summary>
    public static List<T> PickDistinct<T>(Random rng, IReadOnlyList<T> items, int count)
    {
        var pool = items.ToList();
        var chosen = new List<T>();
        count = Math.Min(count, pool.Count);
        for (var i = 0; i < count; i++)
        {
            var idx = rng.Next(pool.Count);
            chosen.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return chosen;
    }
}

/// <summary>Random star system, body, and docked/in-flight state for the Location card.</summary>
public sealed class LocationSampleSource : JournalSampleSource
{
    public override string CardKey => "location";
    public override string DisplayName => "Location";

    public override IReadOnlyList<string> Sample(Random rng)
    {
        var system = Pick(rng, SamplePools.Systems);
        if (rng.Next(2) == 0)
        {
            var station = Pick(rng, SamplePools.Stations);
            return new[]
            {
                Event("Location", o =>
                {
                    o["StarSystem"] = system;
                    o["Body"] = system + Pick(rng, SamplePools.BodySuffixes);
                    o["Docked"] = true;
                    o["StationName"] = station;
                }),
            };
        }

        return new[]
        {
            Event("FSDJump", o =>
            {
                o["StarSystem"] = system;
                o["Body"] = system + Pick(rng, SamplePools.BodySuffixes);
            }),
        };
    }
}

/// <summary>Random ship, name, ident, fuel and balance for the Ship card (and the header).</summary>
public sealed class ShipSampleSource : JournalSampleSource
{
    public override string CardKey => "ship";
    public override string DisplayName => "Ship";

    public override IReadOnlyList<string> Sample(Random rng)
    {
        var (sym, loc) = Pick(rng, SamplePools.Ships);
        var capacity = Pick(rng, new[] { 8.0, 16, 24, 32, 48, 64 });
        var main = Math.Round(rng.NextDouble() * capacity, 1);
        var name = Pick(rng, SamplePools.ShipNames);
        var ident = SamplePools.Ident(rng);
        var commander = Pick(rng, SamplePools.Commanders);
        var credits = rng.NextInt64(250_000, 5_000_000_000);

        return new[]
        {
            Event("LoadGame", o =>
            {
                o["Commander"] = commander;
                o["Ship"] = sym;
                o["Ship_Localised"] = loc;
                o["ShipName"] = name;
                o["ShipIdent"] = ident;
                o["Credits"] = credits;
            }),
            Event("Loadout", o =>
            {
                o["Ship"] = sym;
                o["Ship_Localised"] = loc;
                o["ShipName"] = name;
                o["ShipIdent"] = ident;
                o["FuelCapacity"] = new JsonObject { ["Main"] = capacity };
            }),
            Event("Status", o =>
            {
                o["Fuel"] = new JsonObject { ["FuelMain"] = main };
                o["Balance"] = credits;
            }),
        };
    }
}

/// <summary>Random engineering materials across all three categories for the Materials card.</summary>
public sealed class MaterialsSampleSource : JournalSampleSource
{
    public override string CardKey => "materials";
    public override string DisplayName => "Materials";

    public override IReadOnlyList<string> Sample(Random rng)
    {
        return new[]
        {
            Event("Materials", o =>
            {
                o["Raw"] = Category(rng, SamplePools.RawMaterials, 300);
                o["Manufactured"] = Category(rng, SamplePools.ManufacturedMaterials, 250);
                o["Encoded"] = Category(rng, SamplePools.EncodedMaterials, 200);
            }),
        };
    }

    private static JsonArray Category(Random rng, IReadOnlyList<string> names, int cap)
    {
        var arr = new JsonArray();
        var count = rng.Next(names.Count / 2, names.Count + 1);
        foreach (var name in SamplePools.PickDistinct(rng, names, count))
            arr.Add(new JsonObject { ["Name"] = name, ["Count"] = rng.Next(1, cap) });
        return arr;
    }
}

/// <summary>A random cargo hold for the Cargo card.</summary>
public sealed class CargoSampleSource : JournalSampleSource
{
    public override string CardKey => "cargo";
    public override string DisplayName => "Cargo";

    public override IReadOnlyList<string> Sample(Random rng)
    {
        var picks = SamplePools.PickDistinct(rng, SamplePools.Commodities, rng.Next(2, 7));
        var inventory = new JsonArray();
        var total = 0;
        foreach (var (sym, locName) in picks)
        {
            var count = rng.Next(1, 500);
            total += count;
            inventory.Add(new JsonObject
            {
                ["Name"] = sym,
                ["Name_Localised"] = locName,
                ["Count"] = count,
                ["Stolen"] = 0,
            });
        }

        return new[]
        {
            Event("Cargo", o =>
            {
                o["Vessel"] = "Ship";
                o["Count"] = total;
                o["Inventory"] = inventory;
            }),
            Event("Status", o => o["Cargo"] = total),
        };
    }
}

/// <summary>
/// A random colonisation construction depot for the Colonisation card, plus a small cargo hold
/// carrying some of what the depot still needs — so the "in hold" cross-reference highlight shows.
/// </summary>
public sealed class ColonisationSampleSource : JournalSampleSource
{
    public override string CardKey => "colonisation";
    public override string DisplayName => "Colonisation";

    public override IReadOnlyList<string> Sample(Random rng)
    {
        var system = Pick(rng, SamplePools.Systems);
        var station = "Orbital Construction Site: " + Pick(rng, SamplePools.ConstructionSites);
        var marketId = 3_900_000_000L + rng.Next(0, 99_999_999);

        var picks = SamplePools.PickDistinct(rng, SamplePools.Construction, rng.Next(8, 15));
        var resources = new JsonArray();
        long totalReq = 0, totalProv = 0;
        var outstanding = new List<(string Sym, string Loc, int Remaining)>();

        foreach (var (sym, loc) in picks)
        {
            var required = rng.Next(50, 15_000);
            var provided = rng.Next(0, required + 1);
            totalReq += required;
            totalProv += provided;
            if (required - provided > 0) outstanding.Add((sym, loc, required - provided));

            resources.Add(new JsonObject
            {
                ["Name"] = $"${sym}_name;",
                ["Name_Localised"] = loc,
                ["RequiredAmount"] = required,
                ["ProvidedAmount"] = provided,
                ["Payment"] = rng.Next(500, 12_000),
            });
        }

        var progress = totalReq > 0 ? Math.Round((double)totalProv / totalReq, 4) : 0;

        var events = new List<string>
        {
            // Docked first so the tracker can stamp the site's station name from live state.
            Event("Docked", o =>
            {
                o["StationName"] = station;
                o["StationType"] = "SurfaceStation";
                o["StarSystem"] = system;
            }),
            Event("ColonisationConstructionDepot", o =>
            {
                o["MarketID"] = marketId;
                o["ConstructionProgress"] = progress;
                o["ConstructionComplete"] = false;
                o["ConstructionFailed"] = false;
                o["ResourcesRequired"] = resources;
            }),
        };

        // Stock the hold with a couple of the needed commodities: one fully covered (green ✓),
        // the rest partial — so both cross-reference states are visible.
        var carried = SamplePools.PickDistinct(rng, outstanding, Math.Min(3, outstanding.Count));
        if (carried.Count > 0)
        {
            var inventory = new JsonArray();
            var total = 0;
            for (var i = 0; i < carried.Count; i++)
            {
                var (sym, loc, remaining) = carried[i];
                var count = i == 0 ? remaining + rng.Next(0, 50) : Math.Max(1, remaining / 2);
                total += count;
                inventory.Add(new JsonObject
                {
                    ["Name"] = sym,
                    ["Name_Localised"] = loc,
                    ["Count"] = count,
                    ["Stolen"] = 0,
                });
            }

            events.Add(Event("Cargo", o =>
            {
                o["Vessel"] = "Ship";
                o["Count"] = total;
                o["Inventory"] = inventory;
            }));
            events.Add(Event("Status", o => o["Cargo"] = total));
        }

        return events;
    }
}
