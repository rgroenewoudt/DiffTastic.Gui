using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel(StartupArgs.Parse(desktop.Args ?? []));
            desktop.MainWindow = new MainWindow { DataContext = vm };
            _ = vm.LoadAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
