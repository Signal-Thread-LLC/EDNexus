namespace EDNexus.Core.Dev;

/// <summary>
/// Central switches for optional subsystems that may later be stripped from a build. Today it gates
/// the in-app developer tools (dev mode + the random-state samplers) behind one flag, so every call
/// site can ask <see cref="DeveloperTools"/> instead of hard-coding availability. This is the seam
/// to grow into a richer flag/config system later.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// Whether the in-app developer tools are available at all. When false, the Developer Options
    /// settings section and every dev control are hidden and inert. Resolution order:
    /// <list type="number">
    /// <item>the <c>DISABLE_DEVTOOLS</c> build symbol forces it off (compile-time strip);</item>
    /// <item>otherwise the <c>EDNEXUS_DEVTOOLS</c> env var (<c>true</c>/<c>false</c>) wins;</item>
    /// <item>otherwise it defaults on.</item>
    /// </list>
    /// </summary>
    public static bool DeveloperTools { get; } = Resolve();

    private static bool Resolve()
    {
#if DISABLE_DEVTOOLS
        return false;
#else
        return Environment.GetEnvironmentVariable("EDNEXUS_DEVTOOLS") is { } v && bool.TryParse(v, out var parsed)
            ? parsed
            : true;
#endif
    }
}
