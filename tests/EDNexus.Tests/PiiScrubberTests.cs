using EDNexus.Core.Telemetry;
using Xunit;

namespace EDNexus.Tests;

public class PiiScrubberTests
{
    [Fact]
    public void Redacts_known_literals_case_insensitively()
    {
        var scrubber = new PiiScrubber(new[] { "demortes", "kdeth" });

        var result = scrubber.Scrub("CMDR Demortes docked; user KDETH signed in")!;

        var lower = result.ToLowerInvariant();
        Assert.DoesNotContain("demortes", lower);
        Assert.DoesNotContain("kdeth", lower);
        Assert.Contains(PiiScrubber.RedactedToken, result);
    }

    [Theory]
    [InlineData(@"C:\Users\kdeth\Saved Games\Frontier Developments\Elite Dangerous\Journal.log", @"C:\Users\[user]\Saved")]
    [InlineData("/home/demortes/.local/share/EDNexus/x", "/home/[user]/")]
    [InlineData("/Users/jane/Library/y", "/Users/[user]/")]
    public void Redacts_home_directory_paths(string input, string expectedFragment)
    {
        var result = new PiiScrubber().Scrub(input)!;

        Assert.Contains(expectedFragment, result);
        Assert.Contains(PiiScrubber.UserToken, result);
    }

    [Fact]
    public void Leaves_ordinary_diagnostic_text_untouched()
    {
        var scrubber = new PiiScrubber(new[] { "demortes" });
        const string input = "NullReferenceException in ColonisationTracker.Update at line 42";

        Assert.Equal(input, scrubber.Scrub(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Passes_through_null_and_empty(string? input)
        => Assert.Equal(input, new PiiScrubber().Scrub(input));

    [Fact]
    public void Ignores_too_short_literals_to_avoid_over_redaction()
    {
        // A 2-char literal is ignored, so substrings of unrelated words are not mangled.
        var scrubber = new PiiScrubber(new[] { "AB" });

        Assert.Equal("ABC DEF GAB", scrubber.Scrub("ABC DEF GAB"));
    }
}
