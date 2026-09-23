using System.Text.Json;

namespace EDNexus.Ebs.Services;

/// <summary>
/// Takes a channel's card off the air: shared by <c>DELETE /api/update-state</c> (the broadcaster
/// switched the card off) and <c>POST /oauth/revoke</c> (they signed out). Leaving the last snapshot
/// in place would keep serving "Docked at …" to anyone hitting the unauthenticated
/// <c>/api/initial-state</c> after the broadcaster had asked for it to stop.
/// </summary>
public static class ChannelStateClearing
{
    /// <summary>
    /// Broadcast to viewers already watching so their open card hides immediately. The extension
    /// recognises <c>offline</c>; <c>v</c> matches the snapshot schema so older frontends don't
    /// reject it as a newer, unsupported version.
    /// </summary>
    public static readonly JsonElement OfflineMessage = JsonSerializer.SerializeToElement(new { v = 1, offline = true });

    /// <summary>
    /// Forgets the stored snapshot, then best-effort tells live viewers. A PubSub failure is not
    /// surfaced: the stored state is what leaks to future viewers, and it is already gone.
    /// </summary>
    public static async Task ClearAsync(string channelId, IChannelStateStore stateStore, ITwitchPubSubClient pubSubClient, CancellationToken ct)
    {
        stateStore.Remove(channelId);
        try { await pubSubClient.BroadcastAsync(channelId, OfflineMessage, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* best-effort */ }
    }
}
