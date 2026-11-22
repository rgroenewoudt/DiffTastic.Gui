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
}
