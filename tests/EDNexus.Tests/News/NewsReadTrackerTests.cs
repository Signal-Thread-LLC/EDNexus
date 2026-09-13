using System;
using System.IO;
using EDNexus.Core.News;
using Xunit;

namespace EDNexus.Tests.News;

public class NewsReadTrackerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public NewsReadTrackerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ednexus-newsread-" + Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_dir, "read.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void An_article_never_seen_before_is_unread()
    {
        var tracker = new NewsReadTracker(_path);
        Assert.True(tracker.IsUnread("a1"));
    }

    [Fact]
    public void MarkRead_flips_an_article_to_read()
    {
        var tracker = new NewsReadTracker(_path);
        tracker.MarkRead("a1");
        Assert.False(tracker.IsUnread("a1"));
    }

    [Fact]
    public void Read_state_survives_a_new_tracker_instance_over_the_same_file()
    {
        new NewsReadTracker(_path).MarkRead("a1");

        var reopened = new NewsReadTracker(_path);

        Assert.False(reopened.IsUnread("a1"));
        Assert.True(reopened.IsUnread("a2"));   // unrelated article is unaffected
    }

    [Fact]
    public void MarkAllRead_clears_every_id_given_in_one_call()
    {
        var tracker = new NewsReadTracker(_path);
        tracker.MarkAllRead(new[] { "a1", "a2", "a3" });

        Assert.False(tracker.IsUnread("a1"));
        Assert.False(tracker.IsUnread("a2"));
        Assert.False(tracker.IsUnread("a3"));
    }

    [Fact]
    public void A_corrupt_read_state_file_is_treated_as_empty_rather_than_crashing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "{ not a valid id list }");

        var tracker = new NewsReadTracker(_path);

        Assert.True(tracker.IsUnread("a1"));   // everything just looks new again
    }

    [Fact]
    public void A_missing_file_yields_a_tracker_with_nothing_read()
    {
        var tracker = new NewsReadTracker(_path);
        Assert.False(File.Exists(_path));      // nothing written until MarkRead is called
        Assert.True(tracker.IsUnread("anything"));
    }
}
