using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting.Bridge;

/// <summary>Builds the exception every denied member throws, so the message names plugin and capability.</summary>
internal static class CapabilityDenied
{
    public static UnauthorizedAccessException For(string pluginId, string capability)
        => new($"Plugin '{pluginId}' was not granted the '{capability}' capability; declare it in plugin.json.");
}

/// <summary>
/// The <see cref="IPluginEvents"/> a plugin gets without the <c>events</c> capability: every call is
/// refused at the context boundary instead of silently never firing.
/// </summary>
internal sealed class DeniedPluginEvents(string pluginId) : IPluginEvents
{
    public void Subscribe(string eventName, Action<IJournalEvent> handler) => throw Denied();
    public void SubscribeAny(Action<IJournalEvent> handler) => throw Denied();
    public IDisposable On(string eventName, Action<IJournalEvent> handler) => throw Denied();
    public IDisposable OnAny(Action<IJournalEvent> handler) => throw Denied();

    private UnauthorizedAccessException Denied() => CapabilityDenied.For(pluginId, PluginCapabilities.Events);
}

/// <summary>
/// The <see cref="IReadOnlyCommanderState"/> a plugin gets without the <c>state</c> capability.
/// Every member — including the ones the SDK gives a default body — throws.
/// </summary>
internal sealed class DeniedCommanderState(string pluginId) : IReadOnlyCommanderState
{
    public string? Name => throw Denied();
    public long Balance => throw Denied();
    public string? Ship => throw Denied();
    public string? ShipName => throw Denied();
    public string? StarSystem => throw Denied();
    public string? Body => throw Denied();
    public bool Docked => throw Denied();
    public string? StationDisplayName => throw Denied();
    public DateTimeOffset LastUpdated => throw Denied();
    public IReadOnlyDictionary<string, int> Cargo => throw Denied();
    public IReadOnlyDictionary<string, int> RawMaterials => throw Denied();
    public IReadOnlyDictionary<string, int> ManufacturedMaterials => throw Denied();
    public IReadOnlyDictionary<string, int> EncodedMaterials => throw Denied();
    public IReadOnlyCommanderState Snapshot() => throw Denied();

    private UnauthorizedAccessException Denied() => CapabilityDenied.For(pluginId, PluginCapabilities.State);
}
