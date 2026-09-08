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

    /// <summary>
    /// Shaped like the CMS-backed feed: the article text sits in a Drupal "Body" field, followed by
    /// more field blocks (in-lore date, GUID, image, slug) that are metadata, not article text — all
    /// crammed into the same &lt;description&gt;. Unlike the classic feed, pubDate here is the article's
    /// own publish time, not a shared build timestamp (the bug this feed switch fixes: #123).
    /// </summary>
    private const string CmsFeed = """
    <?xml version="1.0" encoding="utf-8"?>
    <rss version="2.0"><channel>
      <item>
        <guid isPermaLink="false">6a993628a96f951e83045c7e</guid>
        <title>Wreaken Calls for Surface Mining Support Tests</title>
        <description><![CDATA[<span class="field field--name-title field--type-string field--label-hidden">Wreaken Calls for Surface Mining Support Tests</span>
    <span class="field field--name-created field--type-created field--label-hidden"><time datetime="2026-09-03T12:00:46+01:00">Thu, 09/03/2026 - 12:00</time>
    </span>
      <div class="clearfix text-formatted field field--name-body field--type-text-with-summary field--label-above">
        <div class="field__label">Body</div>
                  <div class="field__item"><p>Independent pilots are needed for field trials.<br />
    The rig tests new mining laser technology.</p>
    </div>
              </div>

      <div class="field field--name-field-galnet-date field--type-string field--label-above">
        <div class="field__label">Date</div>
                  <div class="field__item">03 SEP 3312</div>
              </div>

      <div class="field field--name-field-galnet-guid field--type-string field--label-above">
        <div class="field__label">GUID</div>
                  <div class="field__item">6a993628a96f951e83045c7e</div>
              </div>
    ]]></description>
        <pubDate>Thu, 03 Sep 2026 11:00:46 +0000</pubDate>
      </item>
      <item>
        <guid isPermaLink="false">older-article</guid>
        <title>Colonia Tenth Anniversary Celebrations Get Underway</title>
        <description><![CDATA[<span class="field field--name-title field--type-string field--label-hidden">Colonia Tenth Anniversary Celebrations Get Underway</span>
      <div class="clearfix text-formatted field field--name-body field--type-text-with-summary field--label-above">
        <div class="field__label">Body</div>
                  <div class="field__item"><p>The festival has begun in earnest.</p>
    </div>
              </div>

      <div class="field field--name-field-galnet-date field--type-string field--label-above">
        <div class="field__label">Date</div>
                  <div class="field__item">24 AUG 3312</div>
              </div>
    ]]></description>
        <pubDate>Mon, 24 Aug 2026 14:00:46 +0000</pubDate>
      </item>
    </channel></rss>
    """;

    [Fact]
    public void The_CMS_feeds_metadata_fields_do_not_leak_into_the_article_body()
    {
        var article = GalnetClient.Parse(CmsFeed).Value!.First();

        Assert.Equal(
            "Independent pilots are needed for field trials.\nThe rig tests new mining laser technology.",
            article.Body);
        Assert.DoesNotContain("GUID", article.Body);
        Assert.DoesNotContain("03 SEP 3312", article.Body);
        Assert.DoesNotContain("Wreaken Calls", article.Body);   // the duplicated title span, also metadata
    }

    [Fact]
    public void The_CMS_feeds_articles_keep_their_own_distinct_publish_dates()
    {
        var articles = GalnetClient.Parse(CmsFeed).Value!;

        // The bug this feed switch fixes: the classic feed stamped every item in a fetch with the same
        // build timestamp, so every headline showed "today" regardless of when it actually ran.
        Assert.Equal(new DateTimeOffset(2026, 9, 3, 11, 0, 46, TimeSpan.Zero), articles[0].Published);
        Assert.Equal(new DateTimeOffset(2026, 8, 24, 14, 0, 46, TimeSpan.Zero), articles[1].Published);
        Assert.NotEqual(articles[0].Published, articles[1].Published);
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
