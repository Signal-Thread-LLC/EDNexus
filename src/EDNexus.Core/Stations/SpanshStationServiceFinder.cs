using System.Text.Json;
using EDNexus.Core.Trade;
using EliteDangerous.Spansh;

namespace EDNexus.Core.Stations;

/// <summary>
/// The engine-side <see cref="IStationServiceFinder"/> adapter over the reusable
/// <see cref="EliteDangerous.Spansh"/> client. The library is pure transport; this applies the
/// EDNexus policy on top — mapping the curated service catalogue onto the source's own field names
/// and caching answers on disk so repeat lookups skip the network.
/// </summary>
/// <remarks>
/// Station services move far more slowly than commodity prices — a port does not stop having a
/// shipyard between sessions — so this tolerates a much longer cache TTL than the trade search does.
/// </remarks>
public sealed class SpanshStationServiceFinder : IStationServiceFinder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SpanshClient _client;
    private readonly IResponseCache? _cache;

    public string SourceName => "Spansh";

    public SpanshStationServiceFinder(SpanshClient client, IResponseCache? cache = null)
    {
        _client = client;
        _cache = cache;
    }

    public async Task<IReadOnlyList<StationServiceResult>> FindAsync(
        StationServiceQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.ReferenceSystem))
            return Array.Empty<StationServiceResult>();

        var key = CacheKey(query);
        if (_cache?.Get(key) is string cached)
            return Deserialize(cached);

        // Only pass a flavour the service actually has, so a stale setting can't filter everything out.
        var flavour = query.Service.HasFlavours ? query.Flavour : null;

        var result = await _client.SearchServicesAsync(new SpanshServiceQuery
        {
            ServiceName = query.Service.SourceName,
            ReferenceSystem = query.ReferenceSystem,
            SubtypeField = query.Service.SubtypeField,
            Subtype = flavour,
            RequireLargePad = query.RequireLargePad,
            MaxResults = query.MaxResults,
        }, ct).ConfigureAwait(false);

        // A transport failure is transient — surface it as "no results" without caching, so the next
        // attempt retries rather than serving an empty answer for the whole TTL.
        if (!result.IsOk) return Array.Empty<StationServiceResult>();

        var mapped = result.Stations.Select(s => new StationServiceResult(
            s.SystemName, s.StationName, s.DistanceLy, s.DistanceToArrivalLs,
            s.StationType, s.IsPlanetary, s.HasLargePad, s.Subtype, s.Updated)).ToList();

        _cache?.Put(key, JsonSerializer.Serialize(mapped, Json));
        return mapped;
    }

    private static IReadOnlyList<StationServiceResult> Deserialize(string json)
        => JsonSerializer.Deserialize<List<StationServiceResult>>(json, Json) ?? new List<StationServiceResult>();

    private static string CacheKey(StationServiceQuery q) =>
        $"spansh|services|{q.Service.Id}|{q.Flavour ?? "any"}|{(q.RequireLargePad ? "lg" : "any")}" +
        $"|{q.ReferenceSystem.ToLowerInvariant()}|{q.MaxResults}";
}
