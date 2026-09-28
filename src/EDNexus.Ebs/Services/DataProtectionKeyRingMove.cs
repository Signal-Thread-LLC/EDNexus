using Microsoft.Extensions.Logging;

namespace EDNexus.Ebs.Services;

/// <summary>
/// Moves an existing key ring out of the default <c>{DataDirectory}/keys</c> when the service is
/// pointed at a separate key directory. Without this, moving the keys to their own volume would
/// start a fresh, empty ring and every stored Twitch grant would become undecryptable, logging every
/// broadcaster out. The old copy is deleted afterwards: separation that leaves the keys next to the
/// database is not separation.
/// </summary>
public static class DataProtectionKeyRingMove
{
    /// <returns>How many key files were moved; zero when there was nothing to do.</returns>
    public static int MoveIfNeeded(string defaultDirectory, string configuredDirectory, ILogger logger)
    {
        var from = Path.GetFullPath(defaultDirectory);
        var to = Path.GetFullPath(configuredDirectory);
        if (string.Equals(from.TrimEnd(Path.DirectorySeparatorChar), to.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
            return 0;
        if (!Directory.Exists(from)) return 0;

        var legacy = Directory.GetFiles(from, "*.xml");
        if (legacy.Length == 0) return 0;

        Directory.CreateDirectory(to);
        if (Directory.EnumerateFiles(to, "*.xml").Any())
        {
            // Both hold keys: someone already moved them, or the new directory was seeded by hand.
            // Leave both alone rather than guess which ring is current.
            logger.LogWarning(
                "Data Protection keys exist in both {Default} and {Configured}; using {Configured} and leaving {Default} untouched. Delete it once you are sure it is not needed.",
                from, to, to, from);
            return 0;
        }

        // Copy everything first and delete only once all copies landed, so a failure part-way
        // leaves the original ring intact.
        foreach (var file in legacy)
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var file in legacy)
            File.Delete(file);

        logger.LogInformation("Moved {Count} Data Protection key file(s) from {Default} to {Configured}.", legacy.Length, from, to);
        return legacy.Length;
    }
}
