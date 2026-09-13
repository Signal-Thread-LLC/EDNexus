namespace EDNexus.Plugins.Abstractions;

/// <summary>
/// The entry point every EDNexus plugin implements. The host discovers exactly one
/// implementation per plugin assembly, constructs an <see cref="IPluginContext"/> for it, and
/// drives its lifecycle by calling <see cref="Initialize"/> once at load and <see cref="Shutdown"/>
/// once at unload.
/// </summary>
public interface IEDNexusPlugin
{
    /// <summary>
    /// Called once when the plugin is loaded. <paramref name="context"/> is valid for the
    /// lifetime of the plugin session; subscribe to events and register UI here.
    /// </summary>
    void Initialize(IPluginContext context);

    /// <summary>
    /// Called once when the plugin is being unloaded (app shutdown, plugin disabled, or reload).
    /// Release any resources and stop using the <see cref="IPluginContext"/> passed to
    /// <see cref="Initialize"/> after this returns.
    /// </summary>
    void Shutdown();
}
