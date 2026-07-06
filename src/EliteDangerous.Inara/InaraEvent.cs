namespace EliteDangerous.Inara;

/// <summary>
/// One event in an Inara request. <see cref="EventData"/> is serialised verbatim, so the convenience
/// factories build it with the exact key names Inara expects. Callers can also construct arbitrary
/// events via <see cref="Create"/> for anything the factories don't cover.
/// </summary>
public sealed class InaraEvent
{
    public required string EventName { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
    public object? EventData { get; init; }

    /// <summary>Generic factory for any Inara event.</summary>
    public static InaraEvent Create(string eventName, DateTimeOffset timestamp, object? eventData)
        => new() { EventName = eventName, EventTimestamp = timestamp, EventData = eventData };

    // --- Convenience factories for the common commander/travel events. ---

    public static InaraEvent SetCommanderCredits(DateTimeOffset ts, long credits, long? loan = null)
    {
        var data = new Dictionary<string, object?> { ["commanderCredits"] = credits };
        if (loan is long l) data["commanderLoan"] = l;
        return Create("setCommanderCredits", ts, data);
    }

    /// <summary><paramref name="ranks"/>: pilot rank name → (value, progress 0..1).</summary>
    public static InaraEvent SetCommanderRankPilot(DateTimeOffset ts, IEnumerable<(string Name, int Value, double Progress)> ranks)
        => Create("setCommanderRankPilot", ts, ranks.Select(r => new Dictionary<string, object?>
        {
            ["rankName"] = r.Name,
            ["rankValue"] = r.Value,
            ["rankProgress"] = r.Progress,
        }).ToList());

    /// <summary><paramref name="factions"/>: major-faction name → reputation (-100..100).</summary>
    public static InaraEvent SetCommanderReputationMajorFaction(DateTimeOffset ts, IEnumerable<(string Faction, double Reputation)> factions)
        => Create("setCommanderReputationMajorFaction", ts, factions.Select(f => new Dictionary<string, object?>
        {
            ["majorfactionName"] = f.Faction,
            ["majorfactionReputation"] = f.Reputation,
        }).ToList());

    public static InaraEvent SetCommanderShip(DateTimeOffset ts, string shipType, long? shipGameId = null, string? shipName = null, string? shipIdent = null)
    {
        var data = new Dictionary<string, object?> { ["shipType"] = shipType };
        if (shipGameId is long id) data["shipGameID"] = id;
        if (shipName is not null) data["shipName"] = shipName;
        if (shipIdent is not null) data["shipIdent"] = shipIdent;
        return Create("setCommanderShip", ts, data);
    }

    public static InaraEvent SetCommanderTravelLocation(DateTimeOffset ts, string starSystem, string? station = null, long? marketId = null)
    {
        var data = new Dictionary<string, object?> { ["starsystemName"] = starSystem };
        if (station is not null) data["stationName"] = station;
        if (marketId is long m) data["marketID"] = m;
        return Create("setCommanderTravelLocation", ts, data);
    }

    public static InaraEvent AddCommanderTravelDock(DateTimeOffset ts, string starSystem, string station, long? marketId = null, string? shipType = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["starsystemName"] = starSystem,
            ["stationName"] = station,
        };
        if (marketId is long m) data["marketID"] = m;
        if (shipType is not null) data["shipType"] = shipType;
        return Create("addCommanderTravelDock", ts, data);
    }

    public static InaraEvent AddCommanderTravelFSDJump(DateTimeOffset ts, string starSystem, double jumpDistanceLy, string? shipType = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["starsystemName"] = starSystem,
            ["jumpDistance"] = jumpDistanceLy,
        };
        if (shipType is not null) data["shipType"] = shipType;
        return Create("addCommanderTravelFSDJump", ts, data);
    }
}
