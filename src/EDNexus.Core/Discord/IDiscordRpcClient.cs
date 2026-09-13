namespace EDNexus.Core.Discord;

/// <summary>
/// The thin surface <see cref="DiscordPresenceService"/> needs from a Discord Rich Presence client.
/// Kept separate from the third-party <c>DiscordRPC.DiscordRpcClient</c> type so the service — and
/// its tests — never depend on a live IPC connection to a running Discord client.
/// </summary>
public interface IDiscordRpcClient : IDisposable
{
    /// <summary>
    /// Attempts to open the IPC connection to Discord. Must never throw: on any failure (Discord not
    /// installed, not running, or the platform unsupported) this returns false and the service falls
    /// back to a silent no-op rather than blocking or crashing the app.
    /// </summary>
    bool TryInitialize();

    /// <summary>Pushes a new presence snapshot. Must never throw.</summary>
    void SetPresence(DiscordPresencePayload payload);

    /// <summary>Clears the presence (used on shutdown, so the commander doesn't "ghost" as still active).</summary>
    void Clear();
}
