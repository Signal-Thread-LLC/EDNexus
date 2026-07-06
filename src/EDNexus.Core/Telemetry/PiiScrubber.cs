using System.Text.RegularExpressions;

namespace EDNexus.Core.Telemetry;

/// <summary>
/// Redacts personally-identifying strings before anything leaves the machine. Pure and
/// deterministic so it can be unit-tested in isolation. Used by the crash reporter's BeforeSend
/// hook to scrub messages, exception text, and file paths.
/// </summary>
public sealed partial class PiiScrubber
{
    public const string RedactedToken = "[redacted]";
    public const string UserToken = "[user]";

    private readonly string[] _literals;

    /// <param name="sensitive">
    /// Known sensitive literals to redact wholesale — e.g. the OS user name, the CMDR name, the
    /// journal directory path. Short/blank entries are ignored to avoid over-redacting.
    /// </param>
    public PiiScrubber(IEnumerable<string>? sensitive = null)
    {
        _literals = (sensitive ?? Enumerable.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s) && s.Trim().Length >= 3)
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // Longest first so "Ada Lovelace" is redacted before a stray "Ada".
            .OrderByDescending(s => s.Length)
            .ToArray();
    }

    /// <summary>Matches a home-directory path and captures the prefix so the user segment can be
    /// replaced: <c>C:\Users\name</c>, <c>/home/name</c>, <c>/Users/name</c>.</summary>
    [GeneratedRegex(@"([A-Za-z]:\\Users\\|/home/|/Users/)[^\\/\r\n""]+", RegexOptions.IgnoreCase)]
    private static partial Regex HomePathRegex();

    public string? Scrub(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = input;
        foreach (var literal in _literals)
            result = result.Replace(literal, RedactedToken, StringComparison.OrdinalIgnoreCase);

        result = HomePathRegex().Replace(result, m => m.Groups[1].Value + UserToken);
        return result;
    }
}
