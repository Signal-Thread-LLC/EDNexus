using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EDNexus.Core.News;
using EDNexus.Core.Trade;
using EDNexus.Tests.Reporting;   // reuse the shared RecordingHandler test double
using EliteDangerous.Galnet;
using Xunit;

namespace EDNexus.Tests.News;

public class GalnetNewsFeedTests
{
    private const string Feed = """
    <rss version="2.0"><channel>
      <item>
        <guid isPermaLink="false">a1</guid>
        <title>Colonia Celebrations Get Underway</title>
        <description><![CDATA[The festival has begun.<br />Visitors are welcome.]]></description>
        <pubDate>Sat, 15 Aug 2026 22:10:41 +0100</pubDate>
      </item>
    </channel></rss>
    """;

    private static readonly GalnetClientOptions Options = new()
    {
        SoftwareName = "EDNexus.Tests",
        SoftwareVersion = "1.0.0",
    };

    private static GalnetNewsFeed NewFeed(RecordingHandler handler, IResponseCache? cache = null)
        => new(new GalnetClient(Options, new HttpClient(handler)), cache);

    [Fact]
    public async Task Maps_the_feed_into_articles()
    {
        var feed = NewFeed(new RecordingHandler(body: Feed));

        var article = Assert.Single(await feed.GetLatestAsync());

        Assert.Equal("a1", article.Id);
        Assert.Equal("Colonia Celebrations Get Underway", article.Title);
        Assert.Equal("The festival has begun.\nVisitors are welcome.", article.Body);
        Assert.NotNull(article.Published);
    }

    [Fact]
    public async Task A_second_read_is_served_from_the_cache_rather_than_the_network()
    {
        var handler = new RecordingHandler(body: Feed);
        var feed = NewFeed(handler, new InMemoryCache());

        var first = await feed.GetLatestAsync();
        var second = await feed.GetLatestAsync();

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(first.Select(a => a.Id), second.Select(a => a.Id));
        Assert.Equal(first[0].Body, second[0].Body);          // round-trips through the cache intact
        Assert.Equal(first[0].Published, second[0].Published);
    }

    [Fact]
    public async Task An_unreachable_feed_yields_no_articles_rather_than_throwing()
    {
        // News is ambiance: it must never take the dashboard down with it.
        var feed = NewFeed(new RecordingHandler(HttpStatusCode.ServiceUnavailable, "down for maintenance"));

        Assert.Empty(await feed.GetLatestAsync());
    }

    [Fact]
    public async Task A_failed_fetch_is_not_cached_so_the_next_read_tries_again()
    {
        // Caching an empty result would hide the news for the whole TTL over one bad fetch.
        var handler = new RecordingHandler(n => (
            n == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
            n == 1 ? "down" : Feed));
        var feed = NewFeed(handler, new InMemoryCache());

        Assert.Empty(await feed.GetLatestAsync());
        Assert.Single(await feed.GetLatestAsync());
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task A_cache_entry_from_an_older_shape_is_refetched_rather_than_crashing()
    {
        var cache = new InMemoryCache();
        cache.Put("galnet|feed", "{ this is not the array we used to write }");
        var handler = new RecordingHandler(body: Feed);

        var articles = await NewFeed(handler, cache).GetLatestAsync();

        Assert.Equal("a1", Assert.Single(articles).Id);
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class InMemoryCache : IResponseCache
    {
        private readonly Dictionary<string, string> _store = new();
        public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public void Put(string key, string body) => _store[key] = body;
    }
}
