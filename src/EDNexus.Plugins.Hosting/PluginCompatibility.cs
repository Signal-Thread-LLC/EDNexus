using System.Globalization;
using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting;

/// <summary>
/// Gates a validated <see cref="PluginManifest"/> against the running host: its declared
/// <see cref="PluginManifest.SdkVersion"/> must be compatible with the host's SDK contract
/// (<see cref="PluginSdk.IsCompatible"/>), and the app must be at least
/// <see cref="PluginManifest.MinAppVersion"/>.
/// </summary>
public static class PluginCompatibility
{
    /// <summary>
    /// Checks <paramref name="manifest"/> against <paramref name="appVersion"/> and the current
    /// SDK. Returns <see langword="null"/> when compatible, otherwise a human-readable reason.
    /// Never throws for malformed manifest versions (they are reported as the reason).
    /// </summary>
    /// <param name="manifest">A manifest produced by <see cref="PluginManifestParser"/>.</param>
    /// <param name="appVersion">The running EDNexus version.</param>
    public static string? Check(PluginManifest manifest, SemanticVersion appVersion)
        => Check(manifest, appVersion, PluginSdk.CurrentVersion);

    /// <summary>As <see cref="Check(PluginManifest, SemanticVersion)"/>, against an explicit host SDK version.</summary>
    public static string? Check(PluginManifest manifest, SemanticVersion appVersion, Version hostSdkVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(appVersion);
        ArgumentNullException.ThrowIfNull(hostSdkVersion);

        if (!TryParseSdkVersion(manifest.SdkVersion, out var declared))
            return $"plugin declares an unreadable SDK version \"{manifest.SdkVersion}\"";

        if (declared.Major != hostSdkVersion.Major || declared.Minor > hostSdkVersion.Minor)
            return $"plugin targets SDK {declared.Major}.{declared.Minor}, but this EDNexus provides SDK {hostSdkVersion.Major}.{hostSdkVersion.Minor}";

        if (manifest.MinAppVersion is { } min)
        {
            if (!SemanticVersion.TryParse(min, out var minVersion))
                return $"plugin declares an unreadable minimum app version \"{min}\"";
            if (appVersion < minVersion!)
                return $"plugin requires EDNexus {minVersion} or newer (this is {appVersion})";
        }

        return null;
    }

    private static bool TryParseSdkVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('.');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
            return false;
        version = new Version(major, minor);
        return true;
    }
}
