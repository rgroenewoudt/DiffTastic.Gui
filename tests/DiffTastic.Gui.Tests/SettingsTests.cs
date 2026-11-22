using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DiffTastic.Core.Ai;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui.Tests;

public sealed class SettingsTests
{
    private static string Sample(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples", name));

    [AvaloniaFact]
    public void Dialog_shows_current_settings()
    {
        var settings = new AppSettings { AiEnabled = true, AiProvider = "codex", ClaudeModel = "sonnet" };
        var vm = new SettingsViewModel(settings, name => name == AiLocator.Claude ? "claude.exe" : null);
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.FindControl<CheckBox>("AiEnabledBox")!.IsChecked);
        Assert.Equal("Codex", window.FindControl<ComboBox>("ProviderBox")!.SelectedItem);
        Assert.Equal("sonnet", window.FindControl<TextBox>("ClaudeModelBox")!.Text);
        Assert.Equal("claude found on PATH", vm.ClaudeStatus);
        Assert.Equal("codex not found on PATH", vm.CodexStatus);
        Screenshots.Save(window, "30-settings");
    }

    [AvaloniaFact]
    public void ApplyTo_writes_edits_back()
    {
        var settings = new AppSettings { ClaudeModel = "sonnet" };
        var vm = new SettingsViewModel(settings, _ => null)
        {
            AiEnabled = true,
            Provider = "Claude",
            ClaudeModel = "  ",
            CodexModel = " gpt-x ",
        };

        vm.ApplyTo(settings);

        Assert.True(settings.AiEnabled);
        Assert.Equal(AiLocator.Claude, settings.AiProvider);
        Assert.Null(settings.ClaudeModel);
        Assert.Equal("gpt-x", settings.CodexModel);

        vm.Provider = SettingsViewModel.Auto;
        vm.ApplyTo(settings);
        Assert.Null(settings.AiProvider);
    }

    [AvaloniaFact]
    public async Task ApplySettings_switches_provider_and_reanalyzes()
    {
        var first = new AiTests.FakeAi();
        var second = new AiTests.FakeAi();
        var settings = new AppSettings { AiEnabled = true };
        var vm = new MainWindowViewModel(new StartupArgs(Sample("old.cs"), Sample("new.cs"), null), settings, first)
        {
            LocateAi = _ => second,
        };
        await vm.LoadAsync();
        await vm.AnalysisReady;
        Assert.Equal(1, first.Calls);

        settings.ClaudeModel = "other";
        vm.ApplySettings();
        await vm.AnalysisReady;

        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.NotNull(vm.Analysis);
    }

    [AvaloniaFact]
    public async Task ApplySettings_without_tool_turns_ai_off()
    {
        var settings = new AppSettings { AiEnabled = true };
        var vm = new MainWindowViewModel(new StartupArgs(Sample("old.cs"), Sample("new.cs"), null), settings, new AiTests.FakeAi())
        {
            LocateAi = _ => null,
        };
        await vm.LoadAsync();
        await vm.AnalysisReady;

        vm.ApplySettings();

        Assert.False(vm.IsAiAvailable);
        Assert.False(vm.AiEnabled);
        Assert.Null(vm.Analysis);
    }

    [AvaloniaFact]
    public async Task ApplySettings_turns_ai_on()
    {
        var ai = new AiTests.FakeAi();
        var settings = new AppSettings();
        var vm = new MainWindowViewModel(new StartupArgs(Sample("old.cs"), Sample("new.cs"), null), settings, ai)
        {
            LocateAi = _ => ai,
        };
        await vm.LoadAsync();
        Assert.Equal(0, ai.Calls);

        settings.AiEnabled = true;
        vm.ApplySettings();
        await vm.AnalysisReady;

        Assert.True(vm.AiEnabled);
        Assert.Equal(1, ai.Calls);
    }
}
