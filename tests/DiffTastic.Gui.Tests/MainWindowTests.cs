using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using DiffTastic.Core.Diff;
using DiffTastic.Gui.Controls;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui.Tests;

public class MainWindowTests
{
    private static string Sample(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples", name));

    private static async Task<(MainWindow Window, MainWindowViewModel Vm)> OpenAsync(string left, string right)
    {
        var vm = new MainWindowViewModel(new StartupArgs(Sample(left), Sample(right), null));
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    [AvaloniaFact]
    public async Task Shows_side_by_side_diff()
    {
        var (window, vm) = await OpenAsync("old.cs", "new.cs");

        Assert.NotNull(vm.Document);
        Assert.Null(vm.Document!.EngineNote);
        Assert.Equal(vm.Document.Rows.Count, window.LeftPane.Editor.LineCount);
        Assert.Equal(vm.Document.Rows.Count, window.RightPane.Editor.LineCount);
        Screenshots.Save(window, "01-side-by-side");
    }

    [AvaloniaFact]
    public async Task Shows_loader_until_first_diff_is_loaded()
    {
        var vm = new MainWindowViewModel(new StartupArgs(Sample("old.cs"), Sample("new.cs"), null));
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        var overlay = window.FindControl<Control>("LoadingOverlay")!;

        var load = vm.LoadAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(overlay.IsVisible);
        Screenshots.Save(window, "52-loading");

        await load;
        Dispatcher.UIThread.RunJobs();
        Assert.False(overlay.IsVisible);

        var reload = vm.LoadAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.False(overlay.IsVisible);
        await reload;
    }

    [AvaloniaFact]
    public async Task Dark_theme_switches_diff_colors_and_syntax_theme()
    {
        var (window, _) = await OpenAsync("old.cs", "new.cs");

        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(DiffColors.Dark.EditorBackground, window.LeftPane.Editor.Background);
        Assert.Same(DiffColors.Dark.EditorBackground, window.RightPane.Editor.Background);
        Screenshots.Save(window, "08-dark");

        window.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(DiffColors.Light.EditorBackground, window.LeftPane.Editor.Background);
    }

    [AvaloniaFact]
    public async Task Next_difference_moves_current_block()
    {
        var (window, vm) = await OpenAsync("old.cs", "new.cs");

        window.KeyPress(Key.Down, RawInputModifiers.Control, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, vm.CurrentBlock);
        Screenshots.Save(window, "02-next-diff");
    }

    [AvaloniaFact]
    public async Task Search_finds_matches_in_both_panes()
    {
        var (window, _) = await OpenAsync("old.cs", "new.cs");

        window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
        Dispatcher.UIThread.RunJobs();
        var panel = window.RightPane.Editor.SearchPanel!;
        Assert.True(panel.IsOpened);
        Assert.True(window.LeftPane.Editor.SearchPanel!.IsOpened);
        Assert.False(window.LeftPane.Editor.SearchPanel.IsVisible);

        panel.SearchPattern = "return";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("return", window.LeftPane.Editor.SearchPanel.SearchPattern);
        var matches = window.Search.Matches();
        Assert.Equal(4, matches.Count);
        Assert.Equal(2, matches.Count(m => m.Pane == window.LeftPane));

        var visited = new List<DiffPane>();
        for (var i = 0; i < matches.Count; i++)
        {
            window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
            Dispatcher.UIThread.RunJobs();
            var pane = window.LeftPane.Editor.SelectedText == "return" ? window.LeftPane : window.RightPane;
            Assert.Equal("return", pane.Editor.SelectedText);
            visited.Add(pane);
        }
        Assert.Contains(window.LeftPane, visited);
        Assert.Contains(window.RightPane, visited);
        Screenshots.Save(window, "09-search");

        panel.Close();
        Assert.False(window.LeftPane.Editor.SearchPanel.IsOpened);
    }

    [AvaloniaFact]
    public async Task Go_to_line_jumps_to_real_line_of_chosen_side()
    {
        var (window, vm) = await OpenAsync("old.cs", "new.cs");
        var rows = vm.Document!.Rows;
        var lastLeft = rows.Last(r => r.Left is not null).Left!.LineIndex + 1;

        window.LeftPane.Editor.TextArea.Focus();
        window.KeyPress(Key.G, RawInputModifiers.Control, PhysicalKey.G, null);
        Dispatcher.UIThread.RunJobs();
        var box = window.FindControl<TextBox>("GoToLineBox")!;
        Assert.True(box.IsEffectivelyVisible);

        box.Text = lastLeft.ToString(CultureInfo.InvariantCulture);
        Screenshots.Save(window, "10-go-to-line-bar");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(box.IsEffectivelyVisible);
        var row = window.LeftPane.Editor.TextArea.Caret.Line - 1;
        Assert.Equal(lastLeft - 1, rows[row].Left!.LineIndex);
        Screenshots.Save(window, "11-go-to-line");

        Assert.Equal(rows.Count - 1 - rows.Reverse().TakeWhile(r => r.Right is null).Count(), window.RightPane.RowOfLine(100000));
    }

    [AvaloniaFact]
    public async Task Moved_line_jumps_to_where_the_code_went()
    {
        var (window, vm) = await OpenAsync("moved-old.cs", "moved-new.cs");
        var rows = vm.Document!.Rows;
        var row = rows.ToList().FindIndex(r => r.Left?.Move is not null);
        var move = rows[row].Left!.Move!.Value;

        Assert.Equal("Moved to line 14", window.LeftPane.MoveTextAt(row));
        Assert.Equal(13, move.LineIndex);
        window.GoToMove(window.LeftPane, row);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(move.Row, window.RightPane.Editor.TextArea.Caret.Line - 1);
        Assert.Equal(vm.BlockAtRow(move.Row), vm.CurrentBlock);
        Screenshots.Save(window, "51-moved-code");

        window.GoToMove(window.RightPane, move.Row);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(row, window.LeftPane.Editor.TextArea.Caret.Line - 1);
    }

    [AvaloniaFact]
    public async Task Moved_line_jumps_in_unified_view()
    {
        var (window, vm) = await OpenAsync("moved-old.cs", "moved-new.cs");
        vm.UnifiedView = true;
        Dispatcher.UIThread.RunJobs();
        var pane = window.RightPane;
        var row = Enumerable.Range(0, pane.Rows.Count).First(r => pane.SideAt(r) == Side.Left && pane.LineAt(r)?.Move is not null);
        var move = pane.LineAt(row)!.Move!.Value;

        window.GoToMove(pane, row);
        Dispatcher.UIThread.RunJobs();

        var target = pane.Editor.TextArea.Caret.Line - 1;
        Assert.Equal(Side.Right, pane.SideAt(target));
        Assert.Equal(move.LineIndex, pane.LineAt(target)!.LineIndex);
    }

    [AvaloniaFact]
    public async Task Folds_unchanged_methods_in_both_panes()
    {
        var (window, vm) = await OpenAsync("long-old.cs", "long-new.cs");
        for (var i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();

        var left = window.LeftPane.GetFoldStates();
        Assert.Contains(true, left);
        Assert.Equal(left, window.RightPane.GetFoldStates());
        Assert.True(window.RightPane.Editor.ExtentHeight < vm.Document!.Rows.Count * window.RightPane.Editor.TextArea.TextView.DefaultLineHeight);

        var block = vm.Document.Blocks[vm.CurrentBlock];
        var view = window.RightPane.Editor.TextArea.TextView;
        var top = view.GetVisualTopByDocumentLine(block.StartRow + 1);
        Assert.InRange(top, view.VerticalOffset, view.VerticalOffset + view.Bounds.Height);
        Screenshots.Save(window, "06-folded");

        window.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(window.LeftPane.GetFoldStates(), window.RightPane.GetFoldStates());
        Assert.Equal(window.LeftPane.VerticalOffset, window.RightPane.VerticalOffset);

        window.ExpandAll();
        Assert.DoesNotContain(true, window.LeftPane.GetFoldStates());
        Assert.DoesNotContain(true, window.RightPane.GetFoldStates());

        window.CollapseUnchanged();
        Assert.Equal(left, window.LeftPane.GetFoldStates());
        Assert.Equal(left, window.RightPane.GetFoldStates());

        var manager = window.RightPane.Editor.TextArea.TextView.ElementGenerators
            .OfType<AvaloniaEdit.Folding.FoldingElementGenerator>().Single().FoldingManager!;
        manager.AllFoldings.First(f => f.IsFolded).IsFolded = false;
        for (var i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(left, window.LeftPane.GetFoldStates());
        Assert.Equal(window.RightPane.GetFoldStates(), window.LeftPane.GetFoldStates());

        window.CollapseUnchanged();
        var hidden = FoldRegionFinder.DefaultFolded(FoldRegionFinder.Find(vm.Document.Rows))[^1];
        window.ScrollToRow(hidden.StartRow + 1, center: true);
        Assert.Equal(left.Count(f => f) - 1, window.LeftPane.GetFoldStates().Count(f => f));
        Assert.Equal(window.LeftPane.GetFoldStates(), window.RightPane.GetFoldStates());

        var all = Enumerable.Repeat(true, left.Length).ToArray();
        window.LeftPane.ApplyFoldStates(all);
        window.RightPane.ApplyFoldStates(all);
        window.ScrollToRow(0, center: false);
        for (var i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();
        Screenshots.Save(window, "07-folded-changes");
        await vm.BlameReady;
    }

    [AvaloniaFact]
    public async Task Scrolls_to_first_difference_in_long_file()
    {
        var dir = Directory.CreateTempSubdirectory("difftastic-long").FullName;
        var lines = Enumerable.Range(0, 2000).Select(i => $"int value{i} = {i};").ToArray();
        await File.WriteAllLinesAsync(Path.Combine(dir, "a.cs"), lines);
        lines[1500] = "int value1500 = -1;";
        await File.WriteAllLinesAsync(Path.Combine(dir, "b.cs"), lines);

        var vm = new MainWindowViewModel(new StartupArgs(Path.Combine(dir, "a.cs"), Path.Combine(dir, "b.cs"), null));
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        for (var i = 0; i < 10; i++)
            Dispatcher.UIThread.RunJobs();

        var view = window.RightPane.Editor.TextArea.TextView;
        var firstVisible = view.GetDocumentLineByVisualTop(view.VerticalOffset)!.LineNumber - 1;
        Assert.InRange(1500, firstVisible, firstVisible + 30);
        Assert.Equal(window.LeftPane.VerticalOffset, window.RightPane.VerticalOffset);
        Screenshots.Save(window, "03-long-file");

        var before = window.RightPane.VerticalOffset;
        window.MouseWheel(new Avalonia.Point(300, 300), new Avalonia.Vector(0, 5));
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.LeftPane.VerticalOffset < before, "left pane should scroll up");
        Assert.Equal(window.LeftPane.VerticalOffset, window.RightPane.VerticalOffset);
        Screenshots.Save(window, "04-long-file-wheel");
        await vm.BlameReady;
        Directory.Delete(dir, recursive: true);
    }

    [AvaloniaFact]
    public async Task Reloads_when_file_changes_on_disk_and_keeps_position()
    {
        var dir = Directory.CreateTempSubdirectory("difftastic-reload").FullName;
        var lines = Enumerable.Range(0, 2000).Select(i => $"int value{i} = {i};").ToArray();
        var left = Path.Combine(dir, "a.cs");
        var right = Path.Combine(dir, "b.cs");
        await File.WriteAllLinesAsync(left, lines);
        lines[1500] = "int value1500 = -1;";
        await File.WriteAllLinesAsync(right, lines);

        var vm = new MainWindowViewModel(new StartupArgs(left, right, null));
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        for (var i = 0; i < 10; i++)
            Dispatcher.UIThread.RunJobs();
        var offset = window.RightPane.VerticalOffset;
        var before = vm.Document;

        lines[1502] = "int value1502 = -2;";
        await File.WriteAllLinesAsync(right, lines);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((vm.Document == before || vm.IsBusy) && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
        }
        for (var i = 0; i < 10; i++)
            Dispatcher.UIThread.RunJobs();

        Assert.NotSame(before, vm.Document);
        Assert.Contains(vm.Document!.Rows, r => r.Right?.Text == "int value1502 = -2;");
        Assert.Equal(0, vm.CurrentBlock);
        Assert.Equal(offset, window.RightPane.VerticalOffset, 1.0);
        Assert.Equal(window.LeftPane.VerticalOffset, window.RightPane.VerticalOffset);
        await vm.BlameReady;
        vm.Dispose();
        Directory.Delete(dir, recursive: true);
    }

    [AvaloniaFact]
    public async Task Bare_carriage_return_stays_on_one_row()
    {
        var dir = Directory.CreateTempSubdirectory("difftastic-cr").FullName;
        await File.WriteAllTextAsync(Path.Combine(dir, "a.cs"), "int a = 1;\nint b\r= 2;\nint c = 3;\n");
        await File.WriteAllTextAsync(Path.Combine(dir, "b.cs"), "int a = 1;\nint b\r= 2;\nint c = 4;\n");

        var vm = new MainWindowViewModel(new StartupArgs(Path.Combine(dir, "a.cs"), Path.Combine(dir, "b.cs"), null));
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(vm.Document!.Rows.Count, window.LeftPane.Editor.LineCount);
        Assert.Equal(vm.Document.Rows.Count, window.RightPane.Editor.LineCount);
        await vm.BlameReady;
        Directory.Delete(dir, recursive: true);
    }

    [AvaloniaFact]
    public async Task Horizontal_scroll_reaches_end_of_long_line_on_one_side()
    {
        var dir = Directory.CreateTempSubdirectory("difftastic-wide").FullName;
        await File.WriteAllLinesAsync(Path.Combine(dir, "a.cs"), ["int a = 1;", "int b = 2;"]);
        await File.WriteAllLinesAsync(Path.Combine(dir, "b.cs"), ["int a = 1;", "int b = " + new string('9', 600) + ";"]);

        var vm = new MainWindowViewModel(new StartupArgs(Path.Combine(dir, "a.cs"), Path.Combine(dir, "b.cs"), null));
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        for (var i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();

        window.RightPane.ScrollTo(1500, 0);
        for (var i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();

        Assert.Equal(1500, window.RightPane.Editor.TextArea.TextView.ScrollOffset.X, 1);
        Assert.True(window.LeftPane.Editor.TextArea.TextView.ScrollOffset.X < 1500, "left pane is clamped to its own width");
        Screenshots.Save(window, "05-wide-line");
        await vm.BlameReady;
        Directory.Delete(dir, recursive: true);
    }

    [AvaloniaFact]
    public async Task Whitespace_only_changes_count_as_noise_when_set()
    {
        var dir = Directory.CreateTempSubdirectory("difftastic-ws").FullName;
        var left = Path.Combine(dir, "old.txt");
        var right = Path.Combine(dir, "new.txt");
        await File.WriteAllTextAsync(left, "one\n    two\nthree\nfour\nfive\nsix\n");
        await File.WriteAllTextAsync(right, "one\n\ttwo\nthree\nfour\nfive\nSIX\n");
        var settings = new AppSettings { WhitespaceIsNoise = true, ShowWhitespace = true };
        var vm = new MainWindowViewModel(new StartupArgs(left, right, null), settings);
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.Document!.Blocks.Count);
        Assert.True(vm.IsNoise(0));
        Assert.False(vm.IsNoise(1));
        Assert.True(vm.HasNoise);
        Assert.Equal(1, vm.CurrentBlock);
        Assert.False(vm.PreviousDiffCommand.CanExecute(null));
        Assert.True(window.LeftPane.ShowWhitespace);
        Assert.True(window.RightPane.Editor.Options.ShowSpaces);
        Screenshots.Save(window, "40-whitespace");

        vm.ShowWhitespace = false;
        Assert.False(window.RightPane.Editor.Options.ShowTabs);
        Assert.False(settings.ShowWhitespace);

        settings.WhitespaceIsNoise = false;
        vm.ApplySettings();
        Assert.False(vm.HasNoise);
        Assert.True(vm.PreviousDiffCommand.CanExecute(null));
        await vm.BlameReady;
        Directory.Delete(dir, recursive: true);
    }

    [AvaloniaFact]
    public async Task Unified_view_shows_one_column_and_keeps_navigation()
    {
        var settings = new AppSettings();
        var vm = new MainWindowViewModel(new StartupArgs(Sample("old.cs"), Sample("new.cs"), null), settings);
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        Dispatcher.UIThread.RunJobs();
        vm.NextDiffCommand.Execute(null);
        var block = vm.CurrentBlock;

        vm.UnifiedView = true;
        Dispatcher.UIThread.RunJobs();

        var layout = new UnifiedLayout(vm.Document!);
        Assert.True(settings.UnifiedView);
        Assert.False(window.LeftPane.IsVisible);
        Assert.Equal(layout.Rows.Count, window.RightPane.Editor.LineCount);
        Assert.Equal(block, vm.CurrentBlock);
        Assert.True(window.RightPane.Bounds.Width > 800);
        Screenshots.Save(window, "50-unified");

        var removed = layout.Rows.ToList().FindIndex(r => r.Kind != RowKind.Equal && r.Right is null);
        window.GoToLine(Side.Left, layout.Rows[removed].Left!.LineIndex + 1);
        Assert.Equal(removed, window.RightPane.Editor.TextArea.Caret.Line - 1);

        vm.UnifiedView = false;
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.LeftPane.IsVisible);
        Assert.Equal(vm.Document!.Rows.Count, window.RightPane.Editor.LineCount);
        Assert.Equal(vm.Document.Rows.Count, window.LeftPane.Editor.LineCount);
        await vm.BlameReady;
    }
}
