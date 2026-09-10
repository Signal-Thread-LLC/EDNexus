using Avalonia.Controls;

namespace EDNexus.App.Views;

/// <summary>
/// The War Board — every held mission listed individually with its expiry countdown, target, reward
/// and ready-to-turn-in status. Opened from the dashboard's Missions card and bound to the same
/// <see cref="ViewModels.MissionsCardViewModel"/>, so it always reflects the same live state.
/// </summary>
public partial class WarBoardWindow : Window
{
    public WarBoardWindow() => InitializeComponent();
}
