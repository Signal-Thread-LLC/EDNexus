using System.Text.Json;

namespace EDNexus.Ebs.Services;

/// <summary>
/// Caches the most recent state payload broadcast for each channel, so
/// <c>GET /api/initial-state</c> can serve the extension frontend on load without waiting for the
/// next PubSub event.
/// </summary>
public interface IChannelStateStore
{
    /// <summary>Records the latest state payload published for a channel.</summary>
    void Set(string channelId, JsonElement state);

    /// <summary>Attempts to retrieve the last known state payload for a channel.</summary>
    bool TryGet(string channelId, out JsonElement state);
}
