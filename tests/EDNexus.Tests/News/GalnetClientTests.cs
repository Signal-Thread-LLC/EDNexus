using System.Linq;
using EliteDangerous.Galnet;
using Xunit;

namespace EDNexus.Tests.News;

public class GalnetClientTests
{
    /// <summary>Shaped exactly like the live feed: CDATA HTML body, guid, pubDate, no link or category.</summary>
    private const string Feed = """
    <?xml version="1.0" encoding="UTF-8"?>
    <rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom"><channel>
      <title>Elite Dangerous Galnet News</title>
      <item>
        <guid isPermaLink="false">6a74632b3e1bab203a0c66ae</guid>
        <title>Colonia Tenth Anniversary Celebrations Get Underway</title>
        <description><![CDATA[The festival has begun in earnest.<br />
    Colonists are welcoming visitors with open arms.<br />
    &ldquo;My bar stands ready,&rdquo; stated Jaques.]]></description>
        <pubDate>Sat, 15 Aug 2026 22:10:41 +0100</pubDate>
      </item>
      <item>
        <guid isPermaLink="false">second-article</guid>
        <title>Pilots' Federation Directs Members to Colonia</title>
        <description><![CDATA[A short notice.]]></description>
        <pubDate>Sat, 15 Aug 2026 22:10:41 +0100</pubDate>
      </item>
    </channel></rss>
    """;

    [Fact]
    public void The_feed_parses_into_articles_in_the_order_it_lists_them()
    {
        var result = GalnetClient.Parse(Feed);

        Assert.True(result.IsOk);
        Assert.Equal(
            new[] { "6a74632b3e1bab203a0c66ae", "second-article" },
            result.Value!.Select(a => a.Id));
        Assert.Equal("Colonia Tenth Anniversary Celebrations Get Underway", result.Value![0].Title);
    }

    [Fact]
    public void An_articles_html_becomes_plain_text_with_its_line_breaks_kept()
    {
        var article = GalnetClient.Parse(Feed).Value![0];

        Assert.Equal(
            "The festival has begun in earnest.\n" +
            "Colonists are welcoming visitors with open arms.\n" +
            "“My bar stands ready,” stated Jaques.",
            article.Body);
        Assert.DoesNotContain("<br", article.Body);
    }

    [Fact]
    public void The_publication_date_is_read_with_its_offset()
    {
        var article = GalnetClient.Parse(Feed).Value![0];

        Assert.NotNull(article.Published);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 22, 10, 41, TimeSpan.FromHours(1)), article.Published!.Value);
    }

    [Fact]
    public void An_article_with_an_unreadable_date_still_loads_without_one()
    {
        const string feed = """
        <rss version="2.0"><channel><item>
          <guid>x</guid><title>Headline</title><description><![CDATA[Body.]]></description>
          <pubDate>whenever</pubDate>
        </item></channel></rss>
        """;

        var article = GalnetClient.Parse(feed).Value!.Single();

        Assert.Equal("Headline", article.Title);
        Assert.Null(article.Published);   // no date beats a wrong date
    }

    [Fact]
    public void An_item_with_no_headline_is_dropped_rather_than_shown_blank()
    {
        const string feed = """
        <rss version="2.0"><channel>
          <item><guid>a</guid><description><![CDATA[Orphaned body.]]></description></item>
          <item><guid>b</guid><title>Real headline</title><description><![CDATA[Body.]]></description></item>
        </channel></rss>
        """;

        var articles = GalnetClient.Parse(feed).Value!;

        Assert.Equal("Real headline", Assert.Single(articles).Title);
    }

    [Fact]
    public void An_item_with_no_guid_falls_back_to_its_headline_for_an_id()
    {
        const string feed = """
        <rss version="2.0"><channel><item>
          <title>Headline</title><description><![CDATA[Body.]]></description>
        </item></channel></rss>
        """;

        Assert.Equal("Headline", GalnetClient.Parse(feed).Value!.Single().Id);
    }

    [Fact]
    public void A_well_formed_but_empty_feed_is_a_success_with_no_articles()
    {
        var result = GalnetClient.Parse("""<rss version="2.0"><channel><title>Galnet</title></channel></rss>""");

        Assert.True(result.IsOk);
        Assert.Empty(result.Value!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<rss><channel><item><title>Truncated")]
    [InlineData("this is not xml at all")]
    public void Junk_in_place_of_a_feed_is_a_failure_not_an_exception(string junk)
    {
        var result = GalnetClient.Parse(junk);

        Assert.False(result.IsOk);
        Assert.NotNull(result.Error);
    }
}
