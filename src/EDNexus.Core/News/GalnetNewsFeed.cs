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
        if (_cache?.Get(CacheKey) is string cached)
        {
            try
            {
                if (JsonSerializer.Deserialize<List<NewsArticle>>(cached, Json) is { } articles) return articles;
            }
            catch (JsonException)
            {
                // A cache file from an older shape — fall through and refetch.
            }
        }

        var result = await _client.GetLatestAsync(ct).ConfigureAwait(false);
        if (!result.IsOk || result.Value is null) return Array.Empty<NewsArticle>();

        var mapped = result.Value
            .Select(a => new NewsArticle(a.Id, a.Title, a.Body, a.Published))
            .ToList();

        // Only cache a feed that actually held something: caching an empty result would hide the
        // news for the whole TTL over one bad fetch.
        if (mapped.Count > 0) _cache?.Put(CacheKey, JsonSerializer.Serialize(mapped, Json));
        return mapped;
    }
}
