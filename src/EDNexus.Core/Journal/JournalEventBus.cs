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
    private readonly List<Action<JournalEntry>> _completed = new();
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

    /// <summary>
    /// Registers a handler that runs once every <see cref="Subscribe"/> and <see cref="SubscribeAny"/>
    /// handler has seen an entry, so it observes the state that entry produced — which is what an
    /// observer outside the engine (the plugin bridge) wants. Errors are isolated like any other
    /// handler's. Dispose the result to remove it again.
    /// </summary>
    /// <remarks>
    /// Ordering is only guaranteed per publishing thread: entries published from different threads
    /// (e.g. the watcher and a developer-mode source) reach completed handlers in whatever order those
    /// calls interleave. A handler that itself calls <see cref="Publish"/> nests: the inner entry's
    /// completed handlers run before the outer entry's, so they see the outer entry fully applied only
    /// if the nested publish happened after it.
    /// </remarks>
    public IDisposable SubscribeCompleted(Action<JournalEntry> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate) _completed.Add(handler);
        return new Subscription(() => { lock (_gate) _completed.Remove(handler); });
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

        Action<JournalEntry>[] completedSnapshot;
        lock (_gate) completedSnapshot = _completed.Count == 0 ? [] : _completed.ToArray();
        foreach (var h in completedSnapshot) Invoke(h, entry);
    }

    private void Invoke(Action<JournalEntry> handler, JournalEntry entry)
    {
        try { handler(entry); }
        catch (Exception ex) { HandlerError?.Invoke(entry, ex); }
    }

    private sealed class Subscription(Action remove) : IDisposable
    {
        private Action? _remove = remove;

        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }
}
