namespace EliteDangerous.Galnet;

/// <summary>
/// One Galnet article as the feed reports it.
/// </summary>
/// <param name="Id">
/// The feed's <c>guid</c> — a stable opaque id, not a URL (the feed marks it
/// <c>isPermaLink="false"</c>). Use it to tell articles apart across refreshes.
/// </param>
/// <param name="Title">Headline, plain text.</param>
/// <param name="Body">
/// Article text with the feed's HTML removed: <c>&lt;br /&gt;</c> becomes a line break, other tags are
/// dropped and entities decoded, so it is ready to put straight in a text block.
/// </param>
/// <param name="Published">
/// Publication timestamp from <c>pubDate</c>, or null when it is missing or unparseable. Note the
/// live feed stamps every article in a batch with the same time, so this orders batches, not the
/// articles within one — keep the feed's own order for that.
/// </param>
public sealed record GalnetArticle(
    string Id,
    string Title,
    string Body,
    DateTimeOffset? Published);

/// <summary>
/// The result of a Galnet fetch. Mirrors the EDSM and Spansh clients' convention: transport, HTTP and
/// parse failures never throw — they surface as <see cref="IsOk"/> false with an <see cref="Error"/>,
/// and a well-formed but empty feed comes back as <see cref="IsOk"/> true with an empty list.
/// </summary>
public sealed class GalnetResult<T> where T : class
{
    /// <summary>True when the request reached the feed and parsed cleanly (even if it held nothing).</summary>
    public bool IsOk { get; init; }

    /// <summary>The parsed payload, or null when there was nothing to return.</summary>
    public T? Value { get; init; }

    /// <summary>Failure detail when <see cref="IsOk"/> is false; null on success.</summary>
    public string? Error { get; init; }

    public static GalnetResult<T> Ok(T? value) => new() { IsOk = true, Value = value };

    public static GalnetResult<T> Failure(string message) => new() { IsOk = false, Error = message };
}
