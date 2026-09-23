namespace EDNexus.Plugins.Hosting;

/// <summary>
/// Validation for relative paths that come from untrusted plugin content — archive entry names
/// and the manifest's <c>entryAssembly</c>. The rules are the <em>union</em> of what is unsafe or
/// ambiguous on Windows, Linux and macOS, so a package that validates on one OS extracts to the
/// same tree on every OS and can never escape its install folder (zip-slip).
/// </summary>
public static class PluginPathRules
{
    /// <summary>Longest relative path accepted (characters, after normalising separators).</summary>
    public const int MaxRelativePathLength = 240;

    /// <summary>Deepest directory nesting accepted (number of path segments).</summary>
    public const int MaxDepth = 16;

    // Invalid in Windows file names (plus ':' which also means drive / NTFS alternate data stream).
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '|', '?', '*'];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>
    /// Converts backslashes to <c>/</c> so Windows-authored archives are validated the same way
    /// on every OS (and <c>..\evil</c> is seen for what it is).
    /// </summary>
    public static string Normalize(string path) => path.Replace('\\', '/');

    /// <summary>
    /// Checks that <paramref name="path"/> is a safe, relative, forward-slash path that stays
    /// inside its root. Returns <see langword="null"/> when safe, otherwise a human-readable reason.
    /// </summary>
    /// <param name="path">The (already <see cref="Normalize">normalised</see>) relative path.</param>
    /// <param name="allowTrailingSlash">
    /// Whether a single trailing <c>/</c> is allowed (zip directory entries end with one).
    /// </param>
    public static string? CheckRelativePath(string? path, bool allowTrailingSlash = false)
    {
        if (string.IsNullOrEmpty(path))
            return "path is empty";
        if (path.Length > MaxRelativePathLength)
            return $"path is longer than {MaxRelativePathLength} characters";
        if (path.Contains('\\'))
            return "path contains a backslash";
        if (path.StartsWith('/'))
            return "path is absolute";

        var body = allowTrailingSlash && path.EndsWith('/') ? path[..^1] : path;
        if (body.Length == 0)
            return "path is empty";

        if (TextRules.FindInvisibleOrInvalid(body, allowLineBreaks: false) is { } textProblem)
            return "path " + textProblem;

        foreach (var ch in body)
        {
            if (Array.IndexOf(InvalidChars, ch) >= 0)
                return ch == ':' ? "path contains ':' (drive letter or alternate data stream)" : $"path contains the invalid character '{ch}'";
        }

        var segments = body.Split('/');
        if (segments.Length > MaxDepth)
            return $"path is nested deeper than {MaxDepth} levels";

        foreach (var segment in segments)
        {
            var reason = CheckSegment(segment);
            if (reason is not null)
                return reason;
        }
        return null;
    }

    /// <summary>Whether <paramref name="path"/> passes <see cref="CheckRelativePath"/>.</summary>
    public static bool IsSafeRelativePath(string? path, bool allowTrailingSlash = false)
        => CheckRelativePath(path, allowTrailingSlash) is null;

    private static string? CheckSegment(string segment)
    {
        if (segment.Length == 0)
            return "path contains an empty segment ('//')";
        if (segment is "." or "..")
            return $"path contains a '{segment}' segment (directory traversal)";
        // Windows silently strips trailing dots/spaces, so "a." and "a" would alias.
        if (segment.EndsWith('.') || segment.EndsWith(' '))
            return $"path segment '{segment}' ends with a dot or space";
        if (segment.StartsWith(' '))
            return $"path segment '{segment}' starts with a space";

        // "CON", "con.txt", "nul.tar.gz" all resolve to the device on Windows.
        var stem = segment;
        var dot = stem.IndexOf('.');
        if (dot >= 0) stem = stem[..dot];
        if (ReservedDeviceNames.Contains(stem.TrimEnd(' ')))
            return $"path segment '{segment}' is a reserved Windows device name";

        return null;
    }

    /// <summary>
    /// Defence in depth for extraction: lexically combines <paramref name="relativePath"/> with
    /// <paramref name="root"/> and returns the normalised full path only if, <em>as a string</em>,
    /// it is strictly inside <paramref name="root"/>; otherwise <see langword="null"/>.
    /// <para>
    /// This is a string check only — it does not resolve symbolic links or junctions. It is safe
    /// for extraction because packages may not contain links (<see cref="PluginPackage"/> rejects
    /// them) and extraction always writes into a folder it has just created.
    /// </para>
    /// </summary>
    public static string? ResolveInside(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(fullRoot))
            fullRoot += Path.DirectorySeparatorChar;

        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        // String prefix comparison on the normalised paths (no filesystem access).
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return candidate.StartsWith(fullRoot, comparison) && candidate.Length > fullRoot.Length
            ? candidate
            : null;
    }
}
