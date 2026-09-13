namespace EDNexus.Plugins.Abstractions;

/// <summary>
/// Simple key/value persistence scoped to a single plugin, so plugins can save state across
/// sessions without touching the filesystem directly or colliding with the host's own data.
/// </summary>
/// <remarks>
/// This is a stub surface for Phase 11's initial SDK contract; the storage backend, schema
/// versioning and quota rules are fleshed out alongside the loader/storage host work.
/// </remarks>
public interface IPluginStorage
{
    /// <summary>Reads a previously saved string value, or <see langword="null"/> if <paramref name="key"/> is unset.</summary>
    string? GetString(string key);

    /// <summary>Saves a string value under <paramref name="key"/>, overwriting any existing value.</summary>
    void SetString(string key, string value);

    /// <summary>Removes any value saved under <paramref name="key"/>. A no-op if none exists.</summary>
    void Remove(string key);
}
