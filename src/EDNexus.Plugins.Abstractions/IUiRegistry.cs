namespace EDNexus.Plugins.Abstractions;

/// <summary>
/// The seam a plugin uses to contribute UI to the host shell (e.g. a dashboard card). Deliberately
/// framework-agnostic here — this assembly must not reference Avalonia — so plugins can be loaded
/// and validated (manifest, storage, event wiring) even in a host build with no UI at all.
/// </summary>
/// <remarks>
/// This is a stub surface for Phase 11's initial SDK contract; concrete UI contribution points
/// (cards, panels, menu entries) land with the dashboard extensibility work.
/// </remarks>
public interface IUiRegistry
{
    /// <summary>
    /// Registers a named UI contribution described only by an opaque <paramref name="descriptor"/>.
    /// The host interprets the descriptor once concrete UI contracts exist; until then this is a
    /// placeholder that lets plugins compile and register intent without a UI contract to target.
    /// </summary>
    void Register(string id, object descriptor);
}
