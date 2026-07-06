using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EDNexus.App.ViewModels;
using EDNexus.App.Views;

namespace EDNexus.App;

public partial class App : Application
{
    private MainWindowViewModel? _vm;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var boot = Program.Services;

            var vm = _vm = new MainWindowViewModel(boot);
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => vm.Dispose();

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
