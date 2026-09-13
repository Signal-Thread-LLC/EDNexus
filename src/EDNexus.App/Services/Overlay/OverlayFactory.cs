using System.Runtime.InteropServices;
using EDNexus.Core.Overlay;

namespace EDNexus.App.Services.Overlay;

/// <summary>Picks the right <see cref="IOverlay"/> implementation for the current OS.</summary>
public static class OverlayFactory
{
    /// <summary>Windows gets the transparent click-through overlay window; every other platform gets a no-op.</summary>
    public static IOverlay Create() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new WindowsOverlay() : new NullOverlay();
}
