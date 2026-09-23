using System.Globalization;
using System.Text.RegularExpressions;

namespace EDNexus.Plugins.Hosting;

/// <summary>
/// A strict <see href="https://semver.org/spec/v2.0.0.html">SemVer 2.0.0</see> version:
/// <c>MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]</c>, with SemVer precedence rules for comparison
/// (build metadata is ignored when ordering and for equality).
/// </summary>
public sealed partial class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    // The official semver.org regex (numbered-group form), with two .NET-specific hardenings:
    // [0-9] instead of \d (which matches any Unicode decimal digit, e.g. Arabic-Indic), and \z
    // instead of $ (which also matches before a trailing newline).
    [GeneratedRegex(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)" +
        @"(?:-((?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*))?" +
        @"(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemVerPattern();

    /// <summary>Longest version string accepted; anything longer is rejected before matching.</summary>
    internal const int MaxLength = 128;

    private SemanticVersion(int major, int minor, int patch, string? preRelease, string? build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
        Build = build;
    }

    /// <summary>The major version.</summary>
    public int Major { get; }

    /// <summary>The minor version.</summary>
    public int Minor { get; }

    /// <summary>The patch version.</summary>
    public int Patch { get; }

    /// <summary>The pre-release label (without the leading <c>-</c>), or <see langword="null"/>.</summary>
    public string? PreRelease { get; }

    /// <summary>The build metadata (without the leading <c>+</c>), or <see langword="null"/>.</summary>
    public string? Build { get; }

    /// <summary>Whether this is a pre-release version.</summary>
    public bool IsPreRelease => PreRelease is not null;

    /// <summary>Parses <paramref name="text"/> as strict SemVer 2.0.0; never throws.</summary>
    /// <param name="text">The candidate version text.</param>
    /// <param name="version">The parsed version, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="true"/> if <paramref name="text"/> is valid SemVer.</returns>
    public static bool TryParse(string? text, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
            return false;

        var match = SemVerPattern().Match(text);
        if (!match.Success)
            return false;

        // The regex guarantees digits; int.TryParse additionally rejects overflow.
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return false;

        version = new SemanticVersion(
            major, minor, patch,
            match.Groups[4].Success ? match.Groups[4].Value : null,
            match.Groups[5].Success ? match.Groups[5].Value : null);
        return true;
    }

    /// <summary>Parses <paramref name="text"/> as strict SemVer 2.0.0.</summary>
    /// <exception cref="FormatException"><paramref name="text"/> is not valid SemVer.</exception>
    public static SemanticVersion Parse(string text)
        => TryParse(text, out var version)
            ? version!
            : throw new FormatException($"'{text}' is not a valid SemVer 2.0.0 version.");

    /// <inheritdoc />
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // A pre-release has lower precedence than the associated normal version.
        if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null) return -1;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    private static int ComparePreRelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = IsNumericIdentifier(a[i]);
            var bNumeric = IsNumericIdentifier(b[i]);

            int result;
            // Numeric identifiers have no leading zeros (the regex guarantees it), so comparing by
            // length then ordinally is exact for any size — no overflow for huge values.
            if (aNumeric && bNumeric) result = a[i].Length != b[i].Length ? a[i].Length.CompareTo(b[i].Length) : string.CompareOrdinal(a[i], b[i]);
            else if (aNumeric) result = -1; // numeric identifiers sort below alphanumeric ones
            else if (bNumeric) result = 1;
            else result = string.CompareOrdinal(a[i], b[i]);

            if (result != 0) return result;
        }
        return a.Length.CompareTo(b.Length);
    }

    private static bool IsNumericIdentifier(string identifier)
        => identifier.Length > 0 && identifier.All(char.IsAsciiDigit);

    /// <inheritdoc />
    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    /// <inheritdoc />
    public override string ToString()
        => $"{Major}.{Minor}.{Patch}"
           + (PreRelease is null ? "" : "-" + PreRelease)
           + (Build is null ? "" : "+" + Build);

    /// <summary>SemVer precedence: less than.</summary>
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    /// <summary>SemVer precedence: greater than.</summary>
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    /// <summary>SemVer precedence: less than or equal.</summary>
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    /// <summary>SemVer precedence: greater than or equal.</summary>
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
}
