namespace EDNexus.Plugins.Abstractions;

/// <summary>
/// Everything a plugin needs to interact with the host, handed to
/// <see cref="IEDNexusPlugin.Initialize"/>. A plugin should hold onto this for the lifetime of
/// its session and must not use it after <see cref="IEDNexusPlugin.Shutdown"/> is called.
/// </summary>
public interface IPluginContext
{
    /// <summary>The current commander's read-only state.</summary>
    IReadOnlyCommanderState State { get; }

    /// <summary>The journal event feed the plugin can subscribe to.</summary>
    IPluginEvents Events { get; }

    /// <summary>The plugin's scoped logger.</summary>
    IPluginLog Log { get; }

    /// <summary>The plugin's scoped key/value storage.</summary>
    IPluginStorage Storage { get; }

    /// <summary>The seam for contributing UI back to the host shell.</summary>
    IUiRegistry Ui { get; }

    /// <summary>The manifest describing this plugin, as loaded by the host.</summary>
    PluginManifest Manifest { get; }
}
