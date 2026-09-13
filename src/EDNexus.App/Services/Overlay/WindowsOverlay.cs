using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using EDNexus.App.Views;
using EDNexus.Core.Overlay;

namespace EDNexus.App.Services.Overlay;

/// <summary>
/// Windows overlay: a transparent, click-through, always-on-top Avalonia window drawn over Elite
/// Dangerous in borderless/windowed mode. Click-through is applied via the Win32 extended window
/// styles once the native handle exists, so the game keeps input focus and the overlay never
/// intercepts a click meant for the cockpit. All calls are best-effort: a failure here degrades to
/// "no overlay shown" rather than crashing the app.
/// </summary>
public sealed class WindowsOverlay : IOverlay
{
    private OverlayWindow? _window;

    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public bool IsVisible => _window?.IsVisible == true;

    public void Show()
    {
        if (!IsSupported) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_window is null)
                {
                    _window = new OverlayWindow();
                    _window.Opened += (_, _) => MakeClickThrough(_window);
                }
                _window.Show();
            }
            catch
            {
                // Best-effort — see class remarks.
            }
        });
    }

    public void Hide()
    {
        if (!IsSupported) return;
        Dispatcher.UIThread.Post(() =>
        {
            try { _window?.Hide(); }
            catch { /* best-effort */ }
        });
    }

    public void Update(OverlayContent content)
    {
        if (!IsSupported || _window is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            try { _window?.UpdateContent(content); }
            catch { /* best-effort */ }
        });
    }

    // --- Win32: WS_EX_LAYERED | WS_EX_TRANSPARENT makes the window click-through, and
    // WS_EX_TOOLWINDOW keeps it out of the taskbar / alt-tab list alongside ShowInTaskbar="False". ---

    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x80000;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;

    private static void MakeClickThrough(Window window)
    {
        try
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            var style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, style | WsExLayered | WsExTransparent | WsExToolWindow);
        }
        catch
        {
            // Best-effort: worst case the overlay stays interactive rather than crashing the app.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
