using Avalonia;
using Avalonia.Controls;
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
            var boot = Program.Services;

            _host = new EngineHost();
            boot.Crash.Attach(_host.Bus); // report journal handler errors

            var vm = new MainWindowViewModel(_host, boot);
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => _host.Dispose();

            _host.Start();
            vm.Start();

            // First run only: ask for consent (opt-in). Closing the dialog leaves it unasked.
            if (boot.Settings.CrashReportingEnabled is null)
                window.Opened += async (_, _) => await PromptConsentAsync(window, boot);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task PromptConsentAsync(Window owner, Bootstrap boot)
    {
        var result = await new ConsentWindow().ShowDialog<bool?>(owner);
        if (result is bool choice)
            boot.ApplyCrashReportingChoice(choice);
    }
}
