namespace EDNexus.Core.Journal;

/// <summary>Locates the Elite Dangerous journal directory across platforms.</summary>
public static class JournalPaths
{
    /// <summary>Set this environment variable to point at a non-standard journal folder.</summary>
    public const string OverrideEnvVar = "EDNEXUS_JOURNAL_DIR";

    private const string SavedGamesRelative =
        "Saved Games/Frontier Developments/Elite Dangerous";

    /// <summary>Steam AppID for Elite Dangerous, used to find the Proton prefix on Linux.</summary>
    private const string SteamAppId = "359320";

    public static string? Resolve()
    {
        var overridden = Environment.GetEnvironmentVariable(OverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overridden) && Directory.Exists(overridden))
            return overridden;

        foreach (var candidate in Candidates())
            if (Directory.Exists(candidate))
                return candidate;

        return null;
    }

    public static IEnumerable<string> Candidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Windows (and the default on any OS): %USERPROFILE%\Saved Games\...
        if (!string.IsNullOrEmpty(home))
            yield return Combine(home, SavedGamesRelative);

        // Linux via Steam Proton: the game writes into its Windows prefix.
        if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(home))
        {
            foreach (var steamRoot in new[]
            {
                Combine(home, ".local/share/Steam"),
                Combine(home, ".steam/steam"),
            })
            {
                yield return Combine(steamRoot,
                    $"steamapps/compatdata/{SteamAppId}/pfx/drive_c/users/steamuser/{SavedGamesRelative}");
            }
        }
    }

    private static string Combine(string root, string relative)
        => Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
}
