using Avalonia.Controls;

namespace EDNexus.App.Views;

/// <summary>
/// The Galnet reader. Opened from the dashboard ticker and bound to the same
/// <see cref="ViewModels.GalnetCardViewModel"/>, so refreshing in either place updates both.
/// </summary>
public partial class GalnetWindow : Window
{
    public GalnetWindow() => InitializeComponent();
}
