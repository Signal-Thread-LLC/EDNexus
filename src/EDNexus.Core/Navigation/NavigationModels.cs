namespace EDNexus.Core.Navigation;

/// <summary>Galactic coordinates of a system, in light years from Sol.</summary>
public sealed record SystemCoords(double X, double Y, double Z);

/// <summary>A system's identity and confirmed galactic position, as far as the lookup source knows it.</summary>
/// <param name="Coords">Null when the source has no confirmed position for the system.</param>
/// <param name="DistanceLy">Populated only by nearby lookups — distance from the reference system.</param>
public sealed record SystemInfo(string Name, SystemCoords? Coords, double? DistanceLy = null);

/// <summary>
/// Looks up star-system positions and neighbours for the app. Backed by an external catalogue (EDSM);
/// implementations are network-bound and cancellable, and return null / empty rather than throwing
/// when a system is unknown or a lookup fails transiently.
/// </summary>
public interface ISystemLookup
{
    /// <summary>Human-readable name of the backing data source, e.g. "EDSM".</summary>
    string SourceName { get; }

    /// <summary>The system's confirmed position, or null when unknown / failed.</summary>
    Task<SystemInfo?> GetSystemAsync(string systemName, CancellationToken ct = default);

    /// <summary>Systems within <paramref name="radiusLy"/> of the named one, nearest first (empty on miss).</summary>
    Task<IReadOnlyList<SystemInfo>> GetNearbyAsync(string systemName, double radiusLy, CancellationToken ct = default);

    /// <summary>
    /// Straight-line distance in light years between two systems, or null when either position is
    /// unknown. A convenience over two <see cref="GetSystemAsync"/> lookups.
    /// </summary>
    Task<double?> DistanceBetweenAsync(string from, string to, CancellationToken ct = default);
}
