using System.Diagnostics;

namespace EDNexus.Core.Twitch;

/// <summary>
/// Opens a URL in the commander's default web browser. OS-specific behind this interface per
/// AGENTS.md conventions, with <see cref="NoOpBrowserLauncher"/> as the headless/test fallback.
/// </summary>
public interface IBrowserLauncher
{
    /// <summary>Opens <paramref name="url"/> in the default browser. May throw if no browser could be launched.</summary>
    void Open(string url);
}

/// <summary>
/// Default cross-platform launcher. Tries the shell's URL association first (works out of the box on
/// Windows and macOS); Linux desktop environments typically also honour <c>UseShellExecute</c>, but
/// falls back to <c>xdg-open</c> when that fails.
/// </summary>
public sealed class SystemBrowserLauncher : IBrowserLauncher
{
    public void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return;
        }
        catch
        {
            // Fall through to a platform-specific opener below.
        }

        if (OperatingSystem.IsLinux())
            Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
        else if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false });
        else if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { UseShellExecute = false, CreateNoWindow = true });
        else
            throw new PlatformNotSupportedException("No known way to open a browser on this platform.");
    }
}

/// <summary>Headless fallback: records nothing and does not attempt to open a browser. Used by developer/test hosts.</summary>
public sealed class NoOpBrowserLauncher : IBrowserLauncher
{
    /// <summary>The most recent URL that would have been opened, for assertions in tests.</summary>
    public string? LastUrl { get; private set; }

    public void Open(string url) => LastUrl = url;
}
