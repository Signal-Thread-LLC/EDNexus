using System.Collections.Concurrent;
using System.Text.Json;

namespace EDNexus.Ebs.Services;

/// <summary>
/// Process-local, in-memory implementation of <see cref="IChannelStateStore"/>. Sufficient for a
/// single EBS instance; a multi-instance deployment should back this with a shared cache (e.g.
/// Redis) keyed the same way.
/// </summary>
public sealed class InMemoryChannelStateStore : IChannelStateStore
{
    private readonly ConcurrentDictionary<string, JsonElement> _state = new();

    /// <inheritdoc />
    public void Set(string channelId, JsonElement state) => _state[channelId] = state.Clone();

    /// <inheritdoc />
    public bool TryGet(string channelId, out JsonElement state) => _state.TryGetValue(channelId, out state);
}
