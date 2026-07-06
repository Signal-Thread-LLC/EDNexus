using EDNexus.Core.Dev;

namespace EDNexus.App;

/// <summary>
/// Process-wide runtime state for the in-app developer tools. Deliberately <b>not</b> persisted — it
/// resets to off on every launch. Whether the tools exist at all is gated separately by
/// <see cref="FeatureFlags.DeveloperTools"/>, so a stripped build can never turn this on.
/// </summary>
public sealed class DeveloperOptions
{
    /// <summary>True when the developer tools are compiled in / enabled for this build.</summary>
    public bool Available => FeatureFlags.DeveloperTools;

    private bool _enabled;

    /// <summary>
    /// Whether developer mode is currently switched on. Forced false whenever the tools are
    /// unavailable, so callers never need to check <see cref="Available"/> separately.
    /// </summary>
    public bool Enabled
    {
        get => _enabled && Available;
        set => _enabled = value && Available;
    }
}
