using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EDNexus.App.Views;

public partial class InfoWindow : Window
{
    public InfoWindow() => InitializeComponent();

    public void SetText(string title, string message)
    {
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
