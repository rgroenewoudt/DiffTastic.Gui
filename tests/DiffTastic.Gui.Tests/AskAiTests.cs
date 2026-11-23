using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DiffTastic.Core.Ai;
using DiffTastic.Gui.Controls;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui.Tests;

public sealed class AskAiTests
{
    private static string Sample(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples", name));

    private static async Task<(MainWindow Window, MainWindowViewModel Vm, AiTests.FakeAi Ai)> OpenAsync(bool aiEnabled = true)
    {
        var ai = new AiTests.FakeAi();
        var settings = new AppSettings { AiEnabled = aiEnabled };
        var vm = new MainWindowViewModel(new StartupArgs(Sample("long-old.cs"), Sample("long-new.cs"), null), settings, ai);
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        await vm.AnalysisReady;
        Dispatcher.UIThread.RunJobs();
        return (window, vm, ai);
    }

    private static AskAiViewModel Current(MainWindow window) => Assert.IsType<AskAiViewModel>(window.AskAiContent.DataContext);

    private static void SelectRows(DiffPane pane, int first, int last)
    {
        var document = pane.Editor.Document;
        var start = document.GetLineByNumber(first + 1).Offset;
        pane.Editor.Select(start, document.GetLineByNumber(last + 1).EndOffset - start);
    }

    [AvaloniaFact]
    public async Task Asks_about_the_difference_under_the_pointer()
    {
        var (window, vm, ai) = await OpenAsync();
        var block = vm.Document!.Blocks[2];

        window.OpenAskAi(window.RightPane, block.StartRow, 120);
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsAskAiOpen);
        var ask = Current(window);
        Assert.Equal("Difference 3", ask.ContextLabel);
        Assert.False(ask.AskCommand.CanExecute(null));

        ask.Question = "Why was this changed?";
        await ask.AskCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Fake answer.", ask.Answer);
        Assert.Contains(DiffQuestioner.BlockContext(vm.Document, 2).TrimEnd(), ai.LastPrompt);
        Assert.Contains("Question: Why was this changed?", ai.LastPrompt);
        Screenshots.Save(window, "23-ask-ai-block");

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(window.IsAskAiOpen);
    }

    [AvaloniaFact]
    public async Task Asks_about_selected_lines()
    {
        var (window, vm, ai) = await OpenAsync();
        var block = vm.Document!.Blocks[1];
        var first = block.StartRow - 1;
        SelectRows(window.RightPane, first, block.EndRow);

        window.OpenAskAi(window.RightPane, block.StartRow, 120);
        var ask = Current(window);
        var expected = Enumerable.Range(first, block.EndRow - first + 1).Count(r => vm.Document.Rows[r].Right is not null);
        Assert.Equal($"Selection, {expected} lines", ask.ContextLabel);

        ask.Question = "What does this do?";
        await ask.AskCommand.ExecuteAsync(null);
        var firstNew = vm.Document.Rows.Skip(block.StartRow).First(r => r.Right is not null).Right!.Text;
        Assert.Contains("+" + firstNew + "\n", ai.LastPrompt);
        Assert.DoesNotContain("Selected text:", ai.LastPrompt);
    }

    [AvaloniaFact]
    public async Task Click_outside_the_selection_asks_about_the_difference()
    {
        var (window, vm, _) = await OpenAsync();
        SelectRows(window.RightPane, 0, 0);

        window.OpenAskAi(window.RightPane, vm.Document!.Blocks[1].StartRow, 120);

        Assert.Equal("Difference 2", Current(window).ContextLabel);
    }

    [AvaloniaFact]
    public async Task Partly_selected_line_is_sent_as_selected_text()
    {
        var (window, vm, ai) = await OpenAsync();
        var row = vm.Document!.Blocks[2].StartRow;
        var line = window.RightPane.Editor.Document.GetLineByNumber(row + 1);
        var text = window.RightPane.Editor.Document.GetText(line).TrimEnd();
        window.RightPane.Editor.Select(line.Offset + text.Length - 4, 4);

        window.OpenAskAi(window.RightPane, row, 120);
        var ask = Current(window);
        ask.Question = "What is this?";
        await ask.AskCommand.ExecuteAsync(null);

        Assert.Equal("Selection, 1 lines", ask.ContextLabel);
        Assert.Contains($"Selected text: {text[^4..].Trim()}", ai.LastPrompt);
    }

    [AvaloniaFact]
    public async Task Unified_selection_keeps_old_and_new_prefixes()
    {
        var (window, vm, ai) = await OpenAsync();
        vm.UnifiedView = true;
        Dispatcher.UIThread.RunJobs();
        var pane = window.RightPane;
        var rows = pane.Rows;
        var changed = Enumerable.Range(0, rows.Count).First(r => rows[r].Kind == Core.Diff.RowKind.Changed);
        SelectRows(pane, changed, changed + 1);

        window.OpenAskAi(pane, changed, 120);
        var ask = Current(window);
        ask.Question = "Explain";
        await ask.AskCommand.ExecuteAsync(null);

        Assert.Equal("Selection, 2 lines", ask.ContextLabel);
        Assert.Matches(@"(?m)^-.*center: false", ai.LastPrompt!);
        Assert.Matches(@"(?m)^\+.*center: true", ai.LastPrompt!);
    }

    [AvaloniaFact]
    public async Task Right_click_shows_ask_ai_menu()
    {
        var (window, vm, _) = await OpenAsync();
        var pane = window.RightPane;
        var view = pane.Editor.TextArea.TextView;
        var row = vm.Document!.Blocks[0].StartRow;
        var y = view.GetVisualTopByDocumentLine(row + 1) - view.VerticalOffset + view.DefaultLineHeight / 2;
        var point = view.TranslatePoint(new Point(80, y), window)!.Value;

        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        var item = Assert.Single(OpenMenuItems(window));
        Assert.Equal("Ask AI…", item.Header);
        Assert.True(item.IsEnabled);

        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsAskAiOpen);
        Assert.Equal("Difference 1", Current(window).ContextLabel);
    }

    [AvaloniaFact]
    public async Task Disabled_ai_shows_no_menu()
    {
        var (window, vm, _) = await OpenAsync(aiEnabled: false);
        var view = window.RightPane.Editor.TextArea.TextView;
        var point = view.TranslatePoint(new Point(80, 10), window)!.Value;

        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(OpenMenuItems(window));
        window.OpenAskAi(window.RightPane, vm.Document!.Blocks[0].StartRow, 120);
        Assert.False(window.IsAskAiOpen);
    }

    private static List<MenuItem> OpenMenuItems(MainWindow window) =>
        window.GetVisualDescendants().OfType<MenuItem>()
            .Concat(TopLevelsOf(window).SelectMany(t => t.GetVisualDescendants().OfType<MenuItem>()))
            .Where(i => i.Name == "AskAiItem")
            .Distinct()
            .ToList();

    private static IEnumerable<Visual> TopLevelsOf(MainWindow window) =>
        Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(window) is { } overlay ? [overlay] : [];
}
