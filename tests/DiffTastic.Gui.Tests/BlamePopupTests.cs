using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DiffTastic.Core.Git;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui.Tests;

public sealed class BlamePopupTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "difftastic-gui-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _oldCopy = Path.Combine(Path.GetTempPath(), $"difftool-{Guid.NewGuid():N}.cs");
    private GitRunner _git = null!;

    private const string V1 = """
        public class Calculator
        {
            public int Add(int a, int b) => a + b;
        }
        """;
    private const string V2 = """
        public class Calculator
        {
            public int Add(int a, int b) => checked(a + b);
        }
        """;
    private const string V3 = """
        public class Calculator
        {
            public int Add(int a, int b, int c = 0) => checked(a + b + c);

            public int Sum(params int[] values) => values.Sum();
        }
        """;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _git = new GitRunner(_root);
        await _git.RunAsync("init", "-q", "-b", "main");
        await _git.RunAsync("config", "user.name", "Robin Tester");
        await _git.RunAsync("config", "user.email", "test@example.com");
        await CommitAsync(V1, "Add calculator");
        await CommitAsync(V2, "Use checked arithmetic\n\n* Overflow now throws instead of wrapping\n* Callers already handle OverflowException");
        await CommitAsync(V3, "Support a third operand and add Sum");
        await File.WriteAllTextAsync(_oldCopy, V2);
    }

    private async Task CommitAsync(string content, string message)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Calculator.cs"), content);
        await _git.RunAsync("add", "-A");
        await _git.RunAsync("commit", "-q", "-m", message);
    }

    public ValueTask DisposeAsync()
    {
        File.Delete(_oldCopy);
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
        return ValueTask.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Hover_shows_commit_and_navigates_history()
    {
        var vm = new MainWindowViewModel(new StartupArgs(_oldCopy, Path.Combine(_root, "Calculator.cs"), "Calculator.cs"));
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        await vm.BlameReady;
        Dispatcher.UIThread.RunJobs();

        var row = vm.Document!.Rows.ToList().FindIndex(r => r.Right?.LineIndex == 2);
        await window.Blame.ShowAsync(window.RightPane, row, 300);
        Dispatcher.UIThread.RunJobs();

        var popup = window.Blame.Current!;
        Assert.True(window.Blame.IsOpen);
        Assert.Equal("Support a third operand and add Sum", popup.Message);
        Assert.Equal(3, popup.Commits.Count);
        Screenshots.Save(window, "10-blame-popup");

        popup.OlderCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Use checked arithmetic", popup.Message);
        Screenshots.Save(window, "11-blame-older");

        var leftRow = vm.Document.Rows.ToList().FindIndex(r => r.Left?.LineIndex == 0);
        await window.Blame.ShowAsync(window.LeftPane, leftRow, 100);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Add calculator", window.Blame.Current!.Message);
        Screenshots.Save(window, "12-blame-left");
    }

    [AvaloniaFact]
    public async Task Explain_asks_ai_about_the_shown_commit()
    {
        var ai = new AiTests.FakeAi();
        var vm = new MainWindowViewModel(
            new StartupArgs(_oldCopy, Path.Combine(_root, "Calculator.cs"), "Calculator.cs"),
            new AppSettings { AiEnabled = true }, ai);
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        await vm.BlameReady;
        await vm.AnalysisReady;
        Dispatcher.UIThread.RunJobs();

        var row = vm.Document!.Rows.ToList().FindIndex(r => r.Right?.LineIndex == 2);
        await window.Blame.ShowAsync(window.RightPane, row, 300);
        Dispatcher.UIThread.RunJobs();
        var popup = window.Blame.Current!;
        Assert.True(popup.CanExplain);
        Assert.False(popup.HasExplanation);

        await popup.ExplainCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Fake explanation.", popup.Explanation);
        Assert.Contains("Line: public int Add(int a, int b, int c = 0)", ai.LastPrompt);
        Assert.Contains("Support a third operand and add Sum", ai.LastPrompt);
        Assert.Contains("+    public int Sum(params int[] values)", ai.LastPrompt);
        Screenshots.Save(window, "13-blame-explain");

        popup.OlderCommand.Execute(null);
        Assert.Null(popup.Explanation);
        popup.NewerCommand.Execute(null);
        Assert.Equal("Fake explanation.", popup.Explanation);
    }

    [AvaloniaFact]
    public async Task Running_explanation_survives_navigation_and_stops_on_close()
    {
        var ai = new AiTests.FakeAi();
        var vm = new MainWindowViewModel(
            new StartupArgs(_oldCopy, Path.Combine(_root, "Calculator.cs"), "Calculator.cs"),
            new AppSettings { AiEnabled = true }, ai);
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        await vm.BlameReady;
        await vm.AnalysisReady;
        ai.Gate = new TaskCompletionSource();

        var row = vm.Document!.Rows.ToList().FindIndex(r => r.Right?.LineIndex == 2);
        await window.Blame.ShowAsync(window.RightPane, row, 300);
        var popup = window.Blame.Current!;
        var explain = popup.ExplainCommand.ExecuteAsync(null);
        Assert.Equal("Explaining…", popup.Explanation);
        for (var i = 0; i < 500 && ai.Calls < 2; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(2, ai.Calls);

        popup.OlderCommand.Execute(null);
        Assert.Null(popup.Explanation);
        popup.NewerCommand.Execute(null);
        Assert.Equal("Explaining…", popup.Explanation);

        window.Blame.Close();
        await explain;

        Assert.True(ai.Cancelled);
        Assert.Null(popup.Explanation);
    }

    [AvaloniaFact]
    public async Task Explain_is_hidden_without_ai()
    {
        var vm = new MainWindowViewModel(new StartupArgs(_oldCopy, Path.Combine(_root, "Calculator.cs"), "Calculator.cs"));
        await vm.LoadAsync();
        await vm.BlameReady;

        var row = vm.Document!.Rows.ToList().FindIndex(r => r.Right?.LineIndex == 2);
        var popup = await vm.CreateBlamePopupAsync(Controls.Side.Right, row);
        Assert.False(popup!.CanExplain);
    }

    [AvaloniaFact]
    public async Task Reload_ignores_blame_from_previous_load()
    {
        var working = Path.Combine(_root, "Calculator.cs");
        var v1Copy = Path.Combine(Path.GetTempPath(), $"difftool-{Guid.NewGuid():N}.cs");
        await File.WriteAllTextAsync(v1Copy, V1);
        var v2Sha = (await _git.RunAsync("rev-parse", "HEAD~1")).Trim();
        try
        {
            var vm = new MainWindowViewModel(new StartupArgs(v1Copy, working, "Calculator.cs"));
            await vm.LoadAsync();
            var first = vm.BlameReady;

            await vm.OpenAsync(_oldCopy, working, "Calculator.cs");
            await Task.WhenAll(first, vm.BlameReady);
            Dispatcher.UIThread.RunJobs();

            Assert.EndsWith(v2Sha[..8], vm.LeftTitle);
            var row = vm.Document!.Rows.ToList().FindIndex(r => r.Left?.LineIndex == 2);
            var popup = await vm.CreateBlamePopupAsync(Controls.Side.Left, row);
            Assert.Equal("Use checked arithmetic", popup!.Commit.Summary);
        }
        finally
        {
            File.Delete(v1Copy);
        }
    }
}
