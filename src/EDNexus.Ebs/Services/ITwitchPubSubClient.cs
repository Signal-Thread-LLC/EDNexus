using System.Text.Json;

namespace EDNexus.Ebs.Services;

/// <summary>Broadcasts state payloads to viewers of a channel via Twitch's Extensions PubSub API.</summary>
public interface ITwitchPubSubClient
{
    /// <summary>
    /// Sends <paramref name="state"/> to every viewer of <paramref name="broadcasterId"/> currently
    /// listening on the extension's "broadcast" PubSub target.
    /// </summary>
    /// <returns>True when Twitch accepted the message (HTTP 204).</returns>
    Task<bool> BroadcastAsync(string broadcasterId, JsonElement state, CancellationToken cancellationToken);
}
