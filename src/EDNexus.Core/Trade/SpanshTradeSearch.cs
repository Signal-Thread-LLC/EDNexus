using System.Net.Http.Json;
using System.Text.Json;
using EDNexus.Core.Colonisation;

namespace EDNexus.Core.Trade;

/// <summary>
/// <see cref="ITradeSearch"/> backed by the public Spansh API (<c>spansh.co.uk/api</c>), which
/// aggregates the EDDN firehose and keeps system coordinates — so a single station search answers
/// "nearest place to buy/sell commodity X" without EDNexus maintaining its own market database.
/// </summary>
/// <remarks>
/// The request/response shapes below follow Spansh's documented <c>stations/search</c> schema.
/// Because this environment has no egress to spansh.co.uk, the mapping is exercised by fixture-based
/// unit tests rather than a live call; the field names are isolated to <see cref="ParseResults"/>
/// and <see cref="BuildRequest"/> so they are easy to reconcile against the live API.
/// </remarks>
public sealed class SpanshTradeSearch : ITradeSearch
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IResponseCache? _cache;
    private readonly string _baseUrl;

    public string SourceName => "Spansh";

    public SpanshTradeSearch(HttpClient http, IResponseCache? cache = null, string baseUrl = "https://spansh.co.uk/api")
    {
        _http = http;
        _cache = cache;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<IReadOnlyList<TradeStationQuote>> SearchAsync(TradeQuery query, CancellationToken ct = default)
    {
        var key = CacheKey(query);
        var body = _cache?.Get(key);
        if (body is null)
        {
            using var response = await _http.PostAsJsonAsync(
                $"{_baseUrl}/stations/search", BuildRequest(query), Json, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _cache?.Put(key, body);
        }

        return ParseResults(body, query);
    }

    private static string CacheKey(TradeQuery q) =>
        $"spansh|stations|{q.Mode}|{q.ReferenceSystem}|{CommodityName.Canonicalize(q.Commodity)}|{q.MaxResults}";

    /// <summary>
    /// Build the <c>stations/search</c> request: rank stations by distance from the reference system
    /// and filter to those with the wanted side of the commodity's market. (Spansh filter shape.)
    /// </summary>
    private static object BuildRequest(TradeQuery query)
    {
        // Sell → the station must have demand for it; Buy → it must have supply.
        var marketFilter = query.Mode == TradeMode.Sell
            ? new Dictionary<string, object> { ["name"] = query.Commodity, ["demand"] = new { value = new[] { "1", "" } } }
            : new Dictionary<string, object> { ["name"] = query.Commodity, ["supply"] = new { value = new[] { "1", "" } } };

        return new
        {
            filters = new { market = new[] { marketFilter } },
            sort = new[] { new Dictionary<string, object> { ["distance"] = new { direction = "asc" } } },
            size = query.MaxResults,
            reference_system = query.ReferenceSystem,
        };
    }

    /// <summary>
    /// Map the <c>stations/search</c> response into ranked quotes. Parsing is deliberately defensive:
    /// missing or reshaped fields yield fewer results rather than throwing, so a schema drift degrades
    /// gracefully instead of crashing the app.
    /// </summary>
    private static IReadOnlyList<TradeStationQuote> ParseResults(string body, TradeQuery query)
    {
        var wanted = CommodityName.Canonicalize(query.Commodity);
        var quotes = new List<TradeStationQuote>();

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return quotes;

        foreach (var station in results.EnumerateArray())
        {
            if (!station.TryGetProperty("market", out var market) || market.ValueKind != JsonValueKind.Array)
                continue;

            // Find this station's line for the commodity we asked about.
            JsonElement line = default;
            var found = false;
            foreach (var entry in market.EnumerateArray())
            {
                if (CommodityName.Canonicalize(ReadString(entry, "commodity")) == wanted)
                {
                    line = entry;
                    found = true;
                    break;
                }
            }
            if (!found) continue;

            var price = query.Mode == TradeMode.Sell ? ReadInt(line, "sell_price") : ReadInt(line, "buy_price");
            var quantity = query.Mode == TradeMode.Sell ? ReadInt(line, "demand") : ReadInt(line, "supply");
            if (price <= 0) continue;

            quotes.Add(new TradeStationQuote(
                System: ReadString(station, "system_name") ?? "Unknown",
                Station: ReadString(station, "name") ?? "Unknown",
                DistanceLy: ReadDouble(station, "distance"),
                Price: price,
                Quantity: quantity,
                MarketUpdated: ReadDate(station, "market_updated_at")));
        }

        // Preserve the source's nearest-first ordering (the request sorts by distance ascending); the
        // caller shows price per row so the best deal among the nearby stations is easy to spot.
        return quotes;
    }

    private static string? ReadString(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int ReadInt(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private static double ReadDouble(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0;

    private static DateTimeOffset? ReadDate(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d : null;
}
