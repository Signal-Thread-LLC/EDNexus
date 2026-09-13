namespace EDNexus.Plugins.Abstractions;

/// <summary>
/// The event feed a plugin subscribes to. Mirrors the host's internal journal event bus, but
/// only ever hands out read-only <see cref="IJournalEvent"/> instances, and isolates handler
/// exceptions from the rest of the host and other plugins the same way the internal bus does.
/// </summary>
public interface IPluginEvents
{
    /// <summary>
    /// Registers <paramref name="handler"/> to run whenever an event named <paramref name="eventName"/>
    /// is observed (e.g. <c>"FSDJump"</c>). Matching is case-sensitive and exact, matching the
    /// journal's own <c>event</c> field.
    /// </summary>
    void Subscribe(string eventName, Action<IJournalEvent> handler);

    /// <summary>
    /// Registers <paramref name="handler"/> to run for every event observed, regardless of name.
    /// Useful for plugins that log, mirror, or filter events generically rather than reacting to
    /// a fixed set of event names.
    /// </summary>
    void SubscribeAny(Action<IJournalEvent> handler);
}
