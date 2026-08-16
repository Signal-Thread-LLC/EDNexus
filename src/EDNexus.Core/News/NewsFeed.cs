namespace EDNexus.Core.News;

/// <summary>One in-universe news article, as the dashboard consumes it.</summary>
/// <param name="Id">Stable id from the source, used to tell articles apart across refreshes.</param>
/// <param name="Title">Headline.</param>
/// <param name="Body">Article text, already stripped of any source markup.</param>
/// <param name="Published">When it was published, or null when the source did not say.</param>
public sealed record NewsArticle(string Id, string Title, string Body, DateTimeOffset? Published);

/// <summary>
/// A source of in-universe news. Backed by Galnet in the app and by a sample generator in developer
/// mode, so the news card can be exercised offline.
/// </summary>
public interface INewsFeed
{
    /// <summary>Human-readable source name for the card footer ("Galnet", "Galnet (dev)").</summary>
    string SourceName { get; }

    /// <summary>
    /// The latest articles, newest first. Returns an empty list rather than throwing when the source
    /// is unreachable — news is ambiance, and it must never take the dashboard down with it.
    /// </summary>
    Task<IReadOnlyList<NewsArticle>> GetLatestAsync(CancellationToken ct = default);
}
