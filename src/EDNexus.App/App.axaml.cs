using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EDNexus.App.ViewModels;
using EDNexus.App.Views;
using EDNexus.Core;

namespace EDNexus.App;

public partial class App : Application
{
    private EngineHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = new EngineHost();
            var vm = new MainWindowViewModel(_host);
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.ShutdownRequested += (_, _) => _host.Dispose();

            _host.Start();   // warm state + begin watching
            vm.Start();      // begin UI refresh pump
        }

        base.OnFrameworkInitializationCompleted();
    }
}
