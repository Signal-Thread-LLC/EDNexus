using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EliteDangerous.Galnet;

/// <summary>
/// Reads the Galnet news feed and parses it into plain article records. This is pure transport: it
/// does not decide how many articles to keep, how long to cache them, or how to present them — that
/// policy belongs to the caller. Following the EDSM and Spansh clients' convention it never throws
/// for network/HTTP/parse problems; failures surface as <see cref="GalnetResult{T}.Failure"/>. A
/// single instance is safe to reuse across fetches.
/// </summary>
/// <remarks>
/// The feed is RSS 2.0 with one <c>item</c> per article carrying <c>guid</c>, <c>title</c>,
/// <c>pubDate</c> and a CDATA <c>description</c> of HTML. There is no per-article link, category or
/// image, so an article is only ever id + headline + text + date.
/// </remarks>
public sealed class GalnetClient : IDisposable
{
    private readonly GalnetClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public GalnetClient(GalnetClientOptions options, HttpClient? http = null)
    {
        _options = options;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(Sanitize(_options.SoftwareName), Sanitize(_options.SoftwareVersion)));
    }

    /// <summary>
    /// Fetch the current feed, newest first. An OK result with an empty list means the feed was
    /// well-formed but held no articles.
    /// </summary>
    public async Task<GalnetResult<IReadOnlyList<GalnetArticle>>> GetLatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(_options.FeedUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return GalnetResult<IReadOnlyList<GalnetArticle>>.Failure($"HTTP {(int)response.StatusCode}");

            // Read as bytes and decode leniently: the live feed occasionally carries a stray byte
            // that is not valid UTF-8, and one bad apostrophe should not cost the whole news card.
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return Parse(Decode(bytes));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return GalnetResult<IReadOnlyList<GalnetArticle>>.Failure(ex.Message); }
    }

    /// <summary>Parse a feed document that has already been fetched. Exposed for tests and offline replay.</summary>
    public static GalnetResult<IReadOnlyList<GalnetArticle>> Parse(string feed)
    {
        if (string.IsNullOrWhiteSpace(feed))
            return GalnetResult<IReadOnlyList<GalnetArticle>>.Failure("empty feed");

        XDocument doc;
        try
        {
            doc = XDocument.Parse(feed);
        }
        catch (System.Xml.XmlException ex)
        {
            return GalnetResult<IReadOnlyList<GalnetArticle>>.Failure("unparseable feed: " + ex.Message);
        }

        var articles = new List<GalnetArticle>();
        foreach (var item in doc.Descendants("item"))
        {
            var title = Text(item, "title");
            if (string.IsNullOrWhiteSpace(title)) continue;   // an article with no headline is unusable

            articles.Add(new GalnetArticle(
                Id: Text(item, "guid") is { Length: > 0 } id ? id : title,
                Title: PlainText(title),
                Body: PlainText(Text(item, "description")),
                Published: ParseDate(Text(item, "pubDate"))));
        }

        return GalnetResult<IReadOnlyList<GalnetArticle>>.Ok(articles);
    }

    private static string Text(XElement item, string name) => item.Element(name)?.Value?.Trim() ?? "";

    /// <summary>
    /// RFC-1123-ish dates as the feed writes them ("Sat, 15 Aug 2026 22:10:41 +0100"). Anything that
    /// will not parse becomes null rather than a wrong date.
    /// </summary>
    private static DateTimeOffset? ParseDate(string value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Turn the feed's HTML into text: line breaks survive as newlines, every other tag is dropped and
    /// entities decoded. Consecutive blank lines collapse so the reader pane has no gaping holes.
    /// </summary>
    private static string PlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        // The feed writes each break as "<br />" followed by a real newline. In HTML that trailing
        // whitespace is insignificant, so swallow it — keeping it would double-space every article.
        var text = Regex.Replace(html, @"<br\s*/?>\s*", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</p\s*>\s*", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    /// <summary>UTF-8 with replacement rather than throwing, so one malformed byte cannot fail a fetch.</summary>
    private static string Decode(byte[] bytes) => new UTF8Encoding(false, throwOnInvalidBytes: false).GetString(bytes);

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
