using System.Collections.Concurrent;

namespace EDNexus.Core.Journal;

/// <summary>
/// Lightweight synchronous publish/subscribe hub. Feature modules subscribe to the
/// specific event names they care about (or to <see cref="SubscribeAny"/>) and the
/// watcher publishes entries as they are read. Handler exceptions are isolated so one
/// misbehaving subscriber can't stall the pump.
/// </summary>
public sealed class JournalEventBus
{
    private readonly ConcurrentDictionary<string, List<Action<JournalEntry>>> _byEvent =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action<JournalEntry>> _any = new();
    private readonly object _gate = new();

    /// <summary>Raised when a subscriber throws, so hosts can log without crashing the pump.</summary>
    public event Action<JournalEntry, Exception>? HandlerError;

    public void Subscribe(string eventName, Action<JournalEntry> handler)
    {
        var list = _byEvent.GetOrAdd(eventName, _ => new List<Action<JournalEntry>>());
        lock (_gate) list.Add(handler);
    }

    public void SubscribeAny(Action<JournalEntry> handler)
    {
        lock (_gate) _any.Add(handler);
    }

    public void Publish(JournalEntry entry)
    {
        Action<JournalEntry>[] anySnapshot;
        lock (_gate) anySnapshot = _any.ToArray();
        foreach (var h in anySnapshot) Invoke(h, entry);

        if (_byEvent.TryGetValue(entry.Event, out var list))
        {
            Action<JournalEntry>[] snapshot;
            lock (_gate) snapshot = list.ToArray();
            foreach (var h in snapshot) Invoke(h, entry);
        }
    }

    private void Invoke(Action<JournalEntry> handler, JournalEntry entry)
    {
        try { handler(entry); }
        catch (Exception ex) { HandlerError?.Invoke(entry, ex); }
    }
}
