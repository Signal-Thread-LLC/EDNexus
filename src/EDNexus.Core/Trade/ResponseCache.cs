using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EDNexus.Core.Trade;

/// <summary>A keyed store of raw responses with a time-to-live, so repeat queries skip the network.</summary>
public interface IResponseCache
{
    /// <summary>Return the cached body for <paramref name="key"/> if present and not expired, else null.</summary>
    string? Get(string key);

    /// <summary>Store <paramref name="body"/> under <paramref name="key"/>, stamped at the current time.</summary>
    void Put(string key, string body);

    /// <summary>
    /// Return the cached body for <paramref name="key"/> even if its TTL has expired, or null when
    /// there is no entry at all. A last resort for sources that should keep showing something rather
    /// than nothing when the live fetch fails and the ordinary TTL-bound <see cref="Get"/> has already
    /// aged the entry out — e.g. Galnet news while offline. Caches that never remember beyond their TTL
    /// (or that have no on-disk backing, like an in-memory test double) can leave this at the default,
    /// which simply reports no stale value.
    /// </summary>
    string? GetStale(string key) => null;
}

/// <summary>
/// An on-disk <see cref="IResponseCache"/>: one file per key (named by a hash of the key) holding a
/// small JSON envelope of when it was cached plus the body. Entries older than the TTL are treated
/// as misses. The clock is injectable so expiry is deterministic under test.
/// </summary>
public sealed class DiskResponseCache : IResponseCache
{
    private readonly string _dir;
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _now;

    public DiskResponseCache(string directory, TimeSpan ttl, Func<DateTimeOffset>? now = null)
    {
        _dir = directory;
        _ttl = ttl;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(_dir);
    }

    public string? Get(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (!root.TryGetProperty("at", out var at) || !at.TryGetDateTimeOffset(out var cachedAt))
                return null;
            if (_now() - cachedAt >= _ttl) return null;
            return root.TryGetProperty("body", out var body) ? body.GetString() : null;
        }
        catch (JsonException)
        {
            return null; // a corrupt cache file is just a miss.
        }
    }

    public void Put(string key, string body)
    {
        var envelope = new { at = _now(), body };
        File.WriteAllText(PathFor(key), JsonSerializer.Serialize(envelope));
    }

    public string? GetStale(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("body", out var body) ? body.GetString() : null;
        }
        catch (JsonException)
        {
            return null; // a corrupt cache file is just a miss.
        }
    }

    private string PathFor(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_dir, hash + ".json");
    }
}
