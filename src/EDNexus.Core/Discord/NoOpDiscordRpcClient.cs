namespace EDNexus.Core.Discord;

/// <summary>
/// The fallback used whenever Discord Rich Presence isn't available or wanted: the platform doesn't
/// support the DiscordRPC transport, the commander has the integration switched off, or the real
/// client failed to connect. Every call is a silent no-op so callers never need to branch on whether
/// Discord is actually present.
/// </summary>
public sealed class NoOpDiscordRpcClient : IDiscordRpcClient
{
    public static readonly NoOpDiscordRpcClient Instance = new();

    public bool TryInitialize() => false;

    public void SetPresence(DiscordPresencePayload payload) { }

    public void Clear() { }

    public void Dispose() { }
}
