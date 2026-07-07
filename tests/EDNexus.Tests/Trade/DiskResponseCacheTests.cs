using System;
using System.IO;
using EDNexus.Core.Trade;
using Xunit;

namespace EDNexus.Tests;

public class DiskResponseCacheTests : IDisposable
{
    private readonly string _dir;

    public DiskResponseCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ednexus-cache-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Get_returns_null_before_anything_is_stored()
    {
        var cache = new DiskResponseCache(_dir, TimeSpan.FromMinutes(5));
        Assert.Null(cache.Get("missing"));
    }

    [Fact]
    public void Put_then_get_within_ttl_returns_the_body()
    {
        var cache = new DiskResponseCache(_dir, TimeSpan.FromMinutes(5));
        cache.Put("k", "hello");
        Assert.Equal("hello", cache.Get("k"));
    }

    [Fact]
    public void Entry_older_than_ttl_is_a_miss()
    {
        var now = new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero);
        var clock = now;
        var cache = new DiskResponseCache(_dir, TimeSpan.FromMinutes(10), () => clock);

        cache.Put("k", "stale");
        clock = now.AddMinutes(11);      // advance past the TTL

        Assert.Null(cache.Get("k"));
    }

    [Fact]
    public void Entry_just_inside_ttl_still_hits()
    {
        var now = new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero);
        var clock = now;
        var cache = new DiskResponseCache(_dir, TimeSpan.FromMinutes(10), () => clock);

        cache.Put("k", "fresh");
        clock = now.AddMinutes(9);

        Assert.Equal("fresh", cache.Get("k"));
    }

    [Fact]
    public void Distinct_keys_do_not_collide()
    {
        var cache = new DiskResponseCache(_dir, TimeSpan.FromMinutes(5));
        cache.Put("a", "one");
        cache.Put("b", "two");
        Assert.Equal("one", cache.Get("a"));
        Assert.Equal("two", cache.Get("b"));
    }
}
