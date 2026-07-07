using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace EliteDangerous.Spansh;

/// <summary>
/// Queries the Spansh <c>stations/search</c> endpoint and parses the reply into plain Spansh-shaped
/// records. This is pure transport: it does not decide which commodity to look up or how to rank the
/// answer — that policy belongs to the caller. Following the reporting clients' convention it never
/// throws for network/HTTP problems; failures surface as <see cref="SpanshStationsResult.TransportError"/>.
/// A single instance is safe to reuse across queries.
/// </summary>
/// <remarks>
/// The request/response shapes follow Spansh's documented <c>stations/search</c> schema. They are
/// isolated to <see cref="BuildRequest"/> and <see cref="Parse"/> so they are easy to reconcile
/// against the live API — useful because the field names are covered by fixture tests, not a live call.
/// </remarks>
public sealed class SpanshClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SpanshClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public SpanshClient(SpanshClientOptions options, HttpClient? http = null)
    {
        _options = options;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(Sanitize(_options.SoftwareName), Sanitize(_options.SoftwareVersion)));
    }

    /// <summary>
    /// Search stations near the reference system for the wanted side of a commodity. Never throws:
    /// a transport or HTTP failure comes back as <see cref="SpanshStationsResult.TransportError"/>.
    /// </summary>
    public async Task<SpanshStationsResult> SearchStationsAsync(SpanshStationQuery query, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"{_options.BaseUrl.TrimEnd('/')}/stations/search", BuildRequest(query), Json, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return SpanshStationsResult.TransportError($"HTTP {(int)response.StatusCode}");

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(text);
        }
        catch (Exception ex)
        {
            return SpanshStationsResult.TransportError(ex.Message);
        }
    }

    /// <summary>
    /// Plot a neutron-highway route. Spansh runs this as a background job: this submits the job, then
    /// polls for the result (spacing polls by <see cref="SpanshClientOptions.RoutePollInterval"/>) until
    /// it is ready, the attempt budget runs out, or the job errors. Never throws — every failure comes
    /// back as <see cref="SpanshRouteResult.Failure"/>.
    /// </summary>
    public async Task<SpanshRouteResult> PlotRouteAsync(SpanshRouteQuery query, CancellationToken ct = default)
    {
        string jobId;
        try
        {
            var submitUrl =
                $"{_options.BaseUrl.TrimEnd('/')}/route?efficiency={query.Efficiency}" +
                $"&range={query.RangeLy.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                $"&from={Uri.EscapeDataString(query.From)}&to={Uri.EscapeDataString(query.To)}";
            using var submit = await _http.PostAsync(submitUrl, content: null, ct).ConfigureAwait(false);
            if (!submit.IsSuccessStatusCode)
                return SpanshRouteResult.Failure($"HTTP {(int)submit.StatusCode}");

            var submitBody = await submit.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var submitDoc = JsonDocument.Parse(submitBody);
            if (ReadString(submitDoc.RootElement, "job") is not { Length: > 0 } id)
                return SpanshRouteResult.Failure(ReadString(submitDoc.RootElement, "error") ?? "no job id returned");
            jobId = id;
        }
        catch (JsonException ex) { return SpanshRouteResult.Failure("unparseable submit response: " + ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return SpanshRouteResult.Failure(ex.Message); }

        // Poll the job to completion. "queued" / "running" means keep waiting; "ok" carries the route.
        for (var attempt = 0; attempt < Math.Max(1, _options.RoutePollAttempts); attempt++)
        {
            if (attempt > 0 && _options.RoutePollInterval > TimeSpan.Zero)
                await Task.Delay(_options.RoutePollInterval, ct).ConfigureAwait(false);

            try
            {
                using var poll = await _http.GetAsync(
                    $"{_options.BaseUrl.TrimEnd('/')}/results/{Uri.EscapeDataString(jobId)}", ct).ConfigureAwait(false);
                if (!poll.IsSuccessStatusCode)
                    return SpanshRouteResult.Failure($"HTTP {(int)poll.StatusCode}");

                var pollBody = await poll.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var (done, result) = ParseRoutePoll(pollBody);
                if (done) return result;
            }
            catch (JsonException ex) { return SpanshRouteResult.Failure("unparseable poll response: " + ex.Message); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return SpanshRouteResult.Failure(ex.Message); }
        }

        return SpanshRouteResult.Failure("route timed out");
    }

    /// <summary>
    /// Inspect one poll reply. Returns <c>(done: false, …)</c> while the job is still queued/running,
    /// and <c>(done: true, result)</c> once it has finished — successfully or with an error status.
    /// Missing or reshaped fields degrade to failures rather than throwing.
    /// </summary>
    private static (bool Done, SpanshRouteResult Result) ParseRoutePoll(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var status = ReadString(root, "status");

        if (string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "running", StringComparison.OrdinalIgnoreCase))
            return (false, SpanshRouteResult.Failure("still running"));

        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            return (true, SpanshRouteResult.Failure(ReadString(root, "error") ?? $"job status '{status}'"));

        if (!root.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("system_jumps", out var jumps) || jumps.ValueKind != JsonValueKind.Array)
            return (true, SpanshRouteResult.Ok(Array.Empty<SpanshRouteWaypoint>()));

        var waypoints = new List<SpanshRouteWaypoint>();
        foreach (var hop in jumps.EnumerateArray())
            waypoints.Add(new SpanshRouteWaypoint(
                System: ReadString(hop, "system") ?? "Unknown",
                Jumps: ReadInt(hop, "jumps"),
                IsNeutron: hop.TryGetProperty("neutron_star", out var n) && n.ValueKind is JsonValueKind.True,
                DistanceJumpedLy: ReadDouble(hop, "distance_jumped"),
                DistanceRemainingLy: ReadDouble(hop, "distance_left")));

        return (true, SpanshRouteResult.Ok(waypoints));
    }

    /// <summary>
    /// Build the <c>stations/search</c> body: rank by distance from the reference system and filter
    /// to stations that have the wanted side of the commodity's market. (Spansh filter shape.)
    /// </summary>
    private static object BuildRequest(SpanshStationQuery query)
    {
        // Selling to a station needs demand there; buying from one needs supply. Spansh range filters
        // take a [min, max] pair; an open upper bound means "at least 1".
        var side = query.WantsDemand ? "demand" : "supply";
        var marketFilter = new Dictionary<string, object>
        {
            ["name"] = query.CommodityName,
            [side] = new { value = new[] { "1", "" } },
        };

        return new
        {
            filters = new { market = new[] { marketFilter } },
            sort = new[] { new Dictionary<string, object> { ["distance"] = new { direction = "asc" } } },
            size = query.MaxResults,
            reference_system = query.ReferenceSystem,
        };
    }

    /// <summary>
    /// Map the response into stations, defensively: missing or reshaped fields yield fewer results
    /// rather than throwing, so a schema drift degrades gracefully instead of crashing the caller.
    /// </summary>
    private static SpanshStationsResult Parse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return SpanshStationsResult.Ok(Array.Empty<SpanshStation>());

            var stations = new List<SpanshStation>();
            foreach (var station in results.EnumerateArray())
            {
                var commodities = new List<SpanshCommodity>();
                if (station.TryGetProperty("market", out var market) && market.ValueKind == JsonValueKind.Array)
                    foreach (var line in market.EnumerateArray())
                        commodities.Add(new SpanshCommodity(
                            Name: ReadString(line, "commodity") ?? "",
                            BuyPrice: ReadInt(line, "buy_price"),
                            SellPrice: ReadInt(line, "sell_price"),
                            Supply: ReadInt(line, "supply"),
                            Demand: ReadInt(line, "demand")));

                stations.Add(new SpanshStation(
                    SystemName: ReadString(station, "system_name") ?? "Unknown",
                    StationName: ReadString(station, "name") ?? "Unknown",
                    DistanceLy: ReadDouble(station, "distance"),
                    MarketUpdated: ReadDate(station, "market_updated_at"),
                    Commodities: commodities));
            }

            return SpanshStationsResult.Ok(stations);
        }
        catch (JsonException ex)
        {
            return SpanshStationsResult.TransportError("unparseable response: " + ex.Message);
        }
    }

    private static string? ReadString(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int ReadInt(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private static double ReadDouble(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0;

    private static DateTimeOffset? ReadDate(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d : null;

    /// <summary>User-Agent product tokens can't contain whitespace or separators; collapse them.</summary>
    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray());
        return string.IsNullOrEmpty(cleaned) ? "app" : cleaned;
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
