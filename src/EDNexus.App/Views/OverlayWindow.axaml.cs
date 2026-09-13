using Avalonia.Controls;
using EDNexus.Core.Overlay;

namespace EDNexus.App.Views;

/// <summary>
/// The in-game HUD overlay window: a small, always-on-top panel with no title bar, positioned in the
/// corner of the screen. <see cref="Services.Overlay.WindowsOverlay"/> owns the instance and makes it
/// click-through once the native handle exists.
/// </summary>
public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        Position = new Avalonia.PixelPoint(24, 24);
    }

    /// <summary>Push a fresh content snapshot onto the panel. Must be called on the UI thread.</summary>
    public void UpdateContent(OverlayContent content)
    {
        SystemLine.Text = "System: " + (content.StarSystem ?? "—");

        if (content.NextJumpSystem is { Length: > 0 } next)
        {
            NextJumpLine.Text = "Next jump: " + next;
            NextJumpLine.IsVisible = true;
        }
        else
        {
            NextJumpLine.IsVisible = false;
        }

        FuelLine.Text = content.FuelCapacity > 0
            ? $"Fuel: {content.FuelMain:N1} / {content.FuelCapacity:N1} t ({content.FuelPercent:P0})"
            : "Fuel: —";
        FuelLine.Foreground = content.FuelLow
            ? Avalonia.Media.Brush.Parse("#F0453B")
            : Avalonia.Media.Brush.Parse("#E7E9EE");

        if (content.HasBioSignals)
        {
            BioLine.Text = $"Bio signals: {content.BioSignalCount} — {content.BioSignalBody}";
            BioLine.IsVisible = true;
        }
        else
        {
            BioLine.IsVisible = false;
        }

        if (content.HasColonisationShortfall)
        {
            ShortfallHeader.IsVisible = true;
            ShortfallList.IsVisible = true;
            ShortfallList.ItemsSource = content.ColonisationShortfalls
                .Select(l => $"{l.Name}: {l.Remaining}")
                .ToList();
        }
        else
        {
            ShortfallHeader.IsVisible = false;
            ShortfallList.IsVisible = false;
            ShortfallList.ItemsSource = null;
        }
    }
}
