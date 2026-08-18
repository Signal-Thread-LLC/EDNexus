namespace EDNexus.Core.Stations;

/// <summary>
/// One station service the commander can go looking for, and how to ask the data source for it.
/// </summary>
/// <param name="Id">Stable key used by settings and the dev-mode card hook.</param>
/// <param name="Label">Name shown in the UI.</param>
/// <param name="SourceName">The service name as the backing source spells it.</param>
/// <param name="SubtypeField">
/// Source field carrying the service's flavour, when it has one — a Material Trader deals in exactly
/// one of Raw/Manufactured/Encoded, so "nearest material trader" is the wrong question without it.
/// </param>
/// <param name="Subtypes">The flavours on offer, or empty when the service has none.</param>
/// <param name="Hint">One line on what the service is for, for the card's tooltip.</param>
public sealed record StationServiceKind(
    string Id,
    string Label,
    string SourceName,
    string? SubtypeField = null,
    IReadOnlyList<string>? Subtypes = null,
    string? Hint = null)
{
    public IReadOnlyList<string> Flavours => Subtypes ?? Array.Empty<string>();

    /// <summary>True when a flavour must be chosen for the answer to be useful.</summary>
    public bool HasFlavours => Flavours.Count > 0;
}

/// <summary>
/// The services EDNexus offers to search for. Deliberately a curated subset of the ~40 Spansh knows
/// about: these are the ones that answer a real "where do I go for X" question. Names and flavours
/// were taken from the source's own field-value listing, not guessed.
/// </summary>
public static class StationServices
{
    public static readonly IReadOnlyList<StationServiceKind> All = new[]
    {
        new StationServiceKind("material-trader", "Material Trader", "Material Trader",
            "material_trader", new[] { "Raw", "Manufactured", "Encoded" },
            "Trade materials up and down grades for engineering."),
        new StationServiceKind("technology-broker", "Technology Broker", "Technology Broker",
            "technology_broker", new[] { "Human", "Guardian" },
            "Unlock tech-broker modules with materials and data."),
        new StationServiceKind("interstellar-factors", "Interstellar Factors", "Interstellar Factors Contact",
            Hint: "Pay off fines and bounties anonymously."),
        new StationServiceKind("shipyard", "Shipyard", "Shipyard",
            Hint: "Buy, sell and store ships."),
        new StationServiceKind("outfitting", "Outfitting", "Outfitting",
            Hint: "Buy and swap ship modules."),
        new StationServiceKind("universal-cartographics", "Universal Cartographics", "Universal Cartographics",
            Hint: "Sell exploration and cartographic data."),
        new StationServiceKind("vista-genomics", "Vista Genomics", "Vista Genomics",
            Hint: "Sell organic samples from exobiology scans."),
        new StationServiceKind("bartender", "Bartender", "Bartender",
            Hint: "On-foot: trade goods, data and assets for suit upgrades."),
        new StationServiceKind("black-market", "Black Market", "Black Market",
            Hint: "Sell stolen and illegal cargo."),
        new StationServiceKind("search-and-rescue", "Search & Rescue", "Search and Rescue",
            Hint: "Hand in escape pods, black boxes and salvage."),
        new StationServiceKind("refinery", "Refinery Contact", "Refinery Contact",
            Hint: "Sell mining fragments and refine ore."),
        new StationServiceKind("apex", "Apex Interstellar", "Apex Interstellar",
            Hint: "On-foot: book shuttle transport between ports."),
    };

    public static StationServiceKind? ById(string id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The service the card opens on — the one commanders look up most.</summary>
    public static StationServiceKind Default => All[0];
}

/// <summary>A request for the nearest stations offering one service.</summary>
/// <param name="Service">Which service to look for.</param>
/// <param name="ReferenceSystem">System to measure from — usually the current one.</param>
/// <param name="Flavour">Which flavour of the service, when it has them. Null means any.</param>
/// <param name="RequireLargePad">Restrict to stations a large ship can dock at.</param>
public sealed record StationServiceQuery(
    StationServiceKind Service,
    string ReferenceSystem,
    string? Flavour = null,
    bool RequireLargePad = false,
    int MaxResults = 10);

/// <summary>One station offering the requested service, with the detail that decides the trip.</summary>
/// <param name="System">System the station is in.</param>
/// <param name="Station">Station name.</param>
/// <param name="DistanceLy">Jump distance from the reference system.</param>
/// <param name="DistanceToArrivalLs">Supercruise time-sink from the system entry point, in light seconds.</param>
/// <param name="StationType">Station type, e.g. "Coriolis Starport".</param>
/// <param name="IsPlanetary">True for surface ports.</param>
/// <param name="HasLargePad">Whether a large ship can dock.</param>
/// <param name="Flavour">The flavour this station offers, for services that have them.</param>
/// <param name="Updated">When the source last saw this station.</param>
public sealed record StationServiceResult(
    string System,
    string Station,
    double DistanceLy,
    double DistanceToArrivalLs,
    string? StationType,
    bool IsPlanetary,
    bool HasLargePad,
    string? Flavour,
    DateTimeOffset? Updated)
{
    /// <summary>
    /// True when the station sits a long supercruise from the entry point. 5,000 Ls is roughly where
    /// the trip stops being incidental and starts being the reason you didn't bother.
    /// </summary>
    public bool IsFarFromEntry => DistanceToArrivalLs >= 5_000;

    /// <summary>Age of the record, if the source reported when it last saw the station.</summary>
    public TimeSpan? Age(DateTimeOffset now) => Updated is { } t ? now - t : null;
}

/// <summary>
/// A provider that answers "where is the nearest station with X". Backed by an external aggregator
/// (Spansh); implementations are network-bound, cancellable, and never throw for transport problems.
/// </summary>
public interface IStationServiceFinder
{
    /// <summary>Human-readable name of the backing data source, e.g. "Spansh".</summary>
    string SourceName { get; }

    /// <summary>
    /// Stations offering the requested service, nearest first. An empty list means nothing matched —
    /// or the lookup failed transiently; the backing client never throws for network/HTTP problems.
    /// </summary>
    Task<IReadOnlyList<StationServiceResult>> FindAsync(StationServiceQuery query, CancellationToken ct = default);
}
