using EDNexus.Core.Discord;

namespace EDNexus.Tests.Discord;

/// <summary>
/// In-memory stand-in for <see cref="IDiscordRpcClient"/>, used to validate
/// <see cref="DiscordPresenceService"/> without a live Discord client connected.
/// </summary>
internal sealed class FakeDiscordRpcClient : IDiscordRpcClient
{
    public bool InitializeResult { get; set; } = true;
    public int InitializeCalls { get; private set; }
    public int ClearCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public List<DiscordPresencePayload> Sent { get; } = new();

    public bool TryInitialize()
    {
        InitializeCalls++;
        return InitializeResult;
    }

    public void SetPresence(DiscordPresencePayload payload) => Sent.Add(payload);

    public void Clear() => ClearCalls++;

    public void Dispose() => DisposeCalls++;
}
