using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EDNexus.App.Views;

public partial class ConsentWindow : Window
{
    public ConsentWindow() => InitializeComponent();

    private void OnEnable(object? sender, RoutedEventArgs e) => Close(true);
    private void OnDecline(object? sender, RoutedEventArgs e) => Close(false);
}
