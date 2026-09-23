using EDNexus.Core.Discord;

namespace EDNexus.Tests.Discord;

/// <summary>
/// In-memory stand-in for <see cref="IDiscordRpcClient"/>, used to validate
/// <see cref="DiscordPresenceService"/> without a live Discord client connected. Thread-safe: the
/// service's delayed (throttled) sends arrive on a thread-pool thread while the test thread reads.
/// </summary>
internal sealed class FakeDiscordRpcClient : IDiscordRpcClient
{
    private readonly object _gate = new();
    private readonly List<DiscordPresencePayload> _sent = new();
    private int _initializeCalls;
    private int _clearCalls;
    private int _disposeCalls;

    public bool InitializeResult { get; set; } = true;
    public int InitializeCalls => Volatile.Read(ref _initializeCalls);
    public int ClearCalls => Volatile.Read(ref _clearCalls);
    public int DisposeCalls => Volatile.Read(ref _disposeCalls);

    /// <summary>A snapshot of every payload sent so far, in order.</summary>
    public IReadOnlyList<DiscordPresencePayload> Sent
    {
        get { lock (_gate) return _sent.ToArray(); }
    }

    public bool TryInitialize()
    {
        Interlocked.Increment(ref _initializeCalls);
        return InitializeResult;
    }

    public void SetPresence(DiscordPresencePayload payload)
    {
        lock (_gate) _sent.Add(payload);
    }

    public void Clear() => Interlocked.Increment(ref _clearCalls);

    public void Dispose() => Interlocked.Increment(ref _disposeCalls);
}
