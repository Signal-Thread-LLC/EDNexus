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

    private const string CompatDataRelative =
        "steamapps/compatdata/" + SteamAppId + "/pfx/drive_c/users/steamuser/" + SavedGamesRelative;

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
        if (OperatingSystem.IsLinux())
        {
            // Inside a Flatpak sandbox $HOME is redirected to <real-home>/.var/app/<app-id>,
            // so we must recover the real host home for the Steam paths (which are exposed
            // read-only via --filesystem) to resolve.
            var hostHome = HostHome(home, Environment.GetEnvironmentVariable("FLATPAK_ID"));

            foreach (var steamRoot in SteamRoots(hostHome))
                yield return Combine(steamRoot, CompatDataRelative);
        }
    }

    /// <summary>
    /// Resolves the real host home directory. Outside Flatpak this is just <paramref name="home"/>;
    /// inside a Flatpak sandbox <c>$HOME</c> is <c>&lt;real-home&gt;/.var/app/&lt;app-id&gt;</c>, so we
    /// strip that suffix to recover the host home.
    /// </summary>
    internal static string? HostHome(string? home, string? flatpakId)
    {
        if (string.IsNullOrEmpty(home) || string.IsNullOrEmpty(flatpakId))
            return home;

        // Flatpak always lays the per-app directory out with forward slashes: .var/app/<id>.
        var suffix = ".var/app/" + flatpakId;
        var normalized = home.Replace('\\', '/').TrimEnd('/');
        if (normalized.EndsWith("/" + suffix, StringComparison.Ordinal))
            return normalized[..^(suffix.Length + 1)];

        return home;
    }

    private static IEnumerable<string> SteamRoots(string? hostHome)
    {
        if (!string.IsNullOrEmpty(hostHome))
        {
            yield return Combine(hostHome, ".local/share/Steam");
            yield return Combine(hostHome, ".steam/steam");
        }

        // Steam Deck microSD / external Steam library folders are mounted under /run/media.
        // Each library folder contains "steamapps" directly, so it acts as a Steam root.
        foreach (var library in ExternalSteamLibraries())
            yield return library;
    }

    /// <summary>
    /// Discovers external Steam library folders (Steam Deck microSD cards, etc.) mounted under
    /// <c>/run/media</c>. Filesystem errors are swallowed so a single unreadable mount never
    /// breaks detection.
    /// </summary>
    private static IEnumerable<string> ExternalSteamLibraries()
    {
        const string mediaRoot = "/run/media";
        var libraries = new List<string>();
        if (!Directory.Exists(mediaRoot))
            return libraries;

        try
        {
            // Mounts appear as /run/media/<label> or /run/media/<user>/<label>.
            var mounts = new List<string>();
            foreach (var entry in Directory.EnumerateDirectories(mediaRoot))
            {
                mounts.Add(entry);
                try { mounts.AddRange(Directory.EnumerateDirectories(entry)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            foreach (var mount in mounts)
                if (Directory.Exists(Path.Combine(mount, "steamapps", "compatdata")))
                    libraries.Add(mount);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return libraries;
    }

    private static string Combine(string root, string relative)
        => Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
}
