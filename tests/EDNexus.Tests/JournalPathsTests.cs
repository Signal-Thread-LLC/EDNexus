using EDNexus.Core.Journal;
using Xunit;

namespace EDNexus.Tests;

public class JournalPathsTests
{
    [Fact]
    public void HostHome_without_flatpak_returns_home_unchanged()
    {
        Assert.Equal("/home/deck", JournalPaths.HostHome("/home/deck", flatpakId: null));
        Assert.Equal("/home/deck", JournalPaths.HostHome("/home/deck", flatpakId: ""));
    }

    [Fact]
    public void HostHome_strips_flatpak_per_app_suffix()
    {
        var sandboxHome = "/home/deck/.var/app/io.github.Signal_Thread_LLC.EDNexus";

        var result = JournalPaths.HostHome(sandboxHome, "io.github.Signal_Thread_LLC.EDNexus");

        Assert.Equal("/home/deck", result);
    }

    [Fact]
    public void HostHome_leaves_home_untouched_when_suffix_does_not_match()
    {
        // FLATPAK_ID is set but $HOME is not the per-app dir (e.g. --filesystem=home granted,
        // so HOME is already the real home) — nothing to strip.
        var result = JournalPaths.HostHome("/home/deck", "io.github.Signal_Thread_LLC.EDNexus");

        Assert.Equal("/home/deck", result);
    }

    [Fact]
    public void HostHome_handles_trailing_separator()
    {
        var sandboxHome = "/home/deck/.var/app/io.github.Signal_Thread_LLC.EDNexus/";

        var result = JournalPaths.HostHome(sandboxHome, "io.github.Signal_Thread_LLC.EDNexus");

        Assert.Equal("/home/deck", result);
    }

    [Fact]
    public void HostHome_null_home_returns_null()
    {
        Assert.Null(JournalPaths.HostHome(null, "io.github.Signal_Thread_LLC.EDNexus"));
    }
}
