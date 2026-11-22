using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DiffTastic.Core.Diff;
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
    public async Task Next_difference_moves_current_block()
    {
        var (window, vm) = await OpenAsync("old.cs", "new.cs");

        window.KeyPress(Key.Down, RawInputModifiers.Control, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, vm.CurrentBlock);
        Screenshots.Save(window, "02-next-diff");
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
}
