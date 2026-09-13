using System.Text.Json;
using EDNexus.Core.Trade;
using EliteDangerous.Galnet;

namespace EDNexus.Core.News;

/// <summary>
/// The engine-side <see cref="INewsFeed"/> adapter over the reusable <see cref="GalnetClient"/>. It
/// maps Galnet-shaped records to the engine's <see cref="NewsArticle"/> and caches the fetched feed
/// through the same <see cref="IResponseCache"/> the trade search and route plotter use.
/// </summary>
/// <remarks>
/// Galnet publishes a few times a week, so the cache TTL is set generously by the caller: refetching
/// on every dashboard tick would hammer Frontier's site for content that has not changed. A refresh
/// the commander asks for still goes through the cache — the TTL is what bounds staleness, and the
/// card exposes when the articles were last fetched.
/// </remarks>
public sealed class GalnetNewsFeed : INewsFeed
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string CacheKey = "galnet|feed";

    private readonly GalnetClient _client;
    private readonly IResponseCache? _cache;

    public string SourceName => "Galnet";

    public GalnetNewsFeed(GalnetClient client, IResponseCache? cache = null)
    {
        _client = client;
        _cache = cache;
    }

    public async Task<IReadOnlyList<NewsArticle>> GetLatestAsync(CancellationToken ct = default)
    {
        if (TryDeserialize(_cache?.Get(CacheKey)) is { } cached) return cached;

        var result = await _client.GetLatestAsync(ct).ConfigureAwait(false);
        if (result.IsOk && result.Value is { Count: > 0 } articles)
        {
            var mapped = articles
                .Select(a => new NewsArticle(a.Id, a.Title, a.Body, a.Published))
                .ToList();

            // Only cache a feed that actually held something: caching an empty result would hide the
            // news for the whole TTL over one bad fetch.
            _cache?.Put(CacheKey, JsonSerializer.Serialize(mapped, Json));
            return mapped;
        }

        // The live fetch failed, or came back empty — fall back to the last-known cache even past its
        // TTL, so the commander still sees the news while offline rather than a blank card. If there
        // never was a cache entry (or it can't be read), there is genuinely nothing to show.
        return TryDeserialize(_cache?.GetStale(CacheKey)) ?? (IReadOnlyList<NewsArticle>)Array.Empty<NewsArticle>();
    }

    private static List<NewsArticle>? TryDeserialize(string? json)
    {
        if (json is null) return null;
        try
        {
            return JsonSerializer.Deserialize<List<NewsArticle>>(json, Json);
        }
        catch (JsonException)
        {
            // A cache file from an older shape, or a corrupt entry — treat it as no cache at all.
            return null;
        }
    }
}
