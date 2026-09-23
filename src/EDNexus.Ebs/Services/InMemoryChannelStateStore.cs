using System.Collections.Concurrent;
using System.Text.Json;

namespace EDNexus.Ebs.Services;

/// <summary>
/// Process-local, in-memory implementation of <see cref="IChannelStateStore"/>, for tests and
/// throwaway local runs (<c>Ebs:StorageProvider = InMemory</c>): the cache is lost on restart.
/// Production uses <see cref="SqliteChannelStateStore"/>.
/// </summary>
public sealed class InMemoryChannelStateStore : IChannelStateStore
{
    private readonly ConcurrentDictionary<string, JsonElement> _state = new();

    /// <inheritdoc />
    public void Set(string channelId, JsonElement state) => _state[channelId] = state.Clone();

    /// <inheritdoc />
    public bool TryGet(string channelId, out JsonElement state) => _state.TryGetValue(channelId, out state);
}
