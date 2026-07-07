using System.Globalization;
using System.Text.Json;
using EDNexus.Core.Trade;
using EliteDangerous.Spansh;

namespace EDNexus.Core.Routes;

/// <summary>
/// The engine-side <see cref="IRoutePlotter"/> adapter over the reusable <see cref="SpanshClient"/>.
/// The library is pure transport and returns Spansh-shaped waypoints; this adapter maps them to the
/// engine's <see cref="RoutePlan"/> and caches plotted routes on disk so repeating a plot skips the
/// (slow, job-based) Spansh round-trip — reusing the same <see cref="IResponseCache"/> the trade
/// search uses.
/// </summary>
public sealed class SpanshRoutePlotter : IRoutePlotter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SpanshClient _client;
    private readonly IResponseCache? _cache;

    public string SourceName => "Spansh";

    public SpanshRoutePlotter(SpanshClient client, IResponseCache? cache = null)
    {
        _client = client;
        _cache = cache;
    }

    public async Task<RoutePlan?> PlotAsync(RoutePlotRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.To) || request.JumpRangeLy <= 0)
            return null;

        var key = CacheKey(request);
        if (_cache?.Get(key) is string cached && Deserialize(cached, request) is { } hit)
            return hit;

        var result = await _client.PlotRouteAsync(new SpanshRouteQuery
        {
            From = request.From,
            To = request.To,
            RangeLy = request.JumpRangeLy,
            Efficiency = request.Efficiency,
        }, ct).ConfigureAwait(false);

        // A failed or empty plot is transient/uninteresting — surface it as "no route" without caching,
        // so the next attempt retries rather than serving an empty answer for the whole TTL.
        if (!result.IsOk || result.Waypoints.Count == 0) return null;

        var hops = result.Waypoints.Select(w => new RouteHop(
            w.System, w.Jumps, w.IsNeutron, w.DistanceJumpedLy, w.DistanceRemainingLy)).ToList();
        var plan = new RoutePlan(request.From, request.To, hops);

        _cache?.Put(key, JsonSerializer.Serialize(hops, Json));
        return plan;
    }

    private static RoutePlan? Deserialize(string json, RoutePlotRequest request)
    {
        var hops = JsonSerializer.Deserialize<List<RouteHop>>(json, Json);
        return hops is { Count: > 0 } ? new RoutePlan(request.From, request.To, hops) : null;
    }

    private static string CacheKey(RoutePlotRequest r) =>
        "spansh|route|" + string.Join("|", new[]
        {
            r.From.Trim().ToLowerInvariant(),
            r.To.Trim().ToLowerInvariant(),
            r.JumpRangeLy.ToString("0.##", CultureInfo.InvariantCulture),
            r.Efficiency.ToString(CultureInfo.InvariantCulture),
        });
}
