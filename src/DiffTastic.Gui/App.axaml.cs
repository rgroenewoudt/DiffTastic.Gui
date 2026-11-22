using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using DiffTastic.Core.Ai;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>"light" or "dark"; anything else follows the system theme.</summary>
    public static void ApplyTheme(string? theme)
    {
        if (Current is { } app)
            app.RequestedThemeVariant = ThemeVariantFor(theme);
    }

    public static ThemeVariant ThemeVariantFor(string? theme) => theme?.ToLowerInvariant() switch
    {
        "light" => ThemeVariant.Light,
        "dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettings.Load(AppSettings.DefaultPath);
            Localization.Apply(settings.Language);
            ApplyTheme(settings.Theme);
            var ai = AiLocator.Locate(settings.AiProvider, settings.ClaudeModel, settings.CodexModel);
            var vm = new MainWindowViewModel(StartupArgs.Parse(desktop.Args ?? []), settings, ai);
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.Exit += (_, _) => vm.Dispose();
            _ = vm.LoadAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
