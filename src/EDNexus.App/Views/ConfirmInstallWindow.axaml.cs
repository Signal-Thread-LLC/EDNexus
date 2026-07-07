using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EDNexus.App.Views;

public partial class ConfirmInstallWindow : Window
{
    public ConfirmInstallWindow() => InitializeComponent();

    public void SetFilePath(string path)
    {
        FilePath.Text = path;
    }

    private void OnEnable(object? sender, RoutedEventArgs e) => Close(true);
    private void OnDecline(object? sender, RoutedEventArgs e) => Close(false);
}
