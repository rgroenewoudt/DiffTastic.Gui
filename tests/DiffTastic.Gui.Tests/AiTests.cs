using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DiffTastic.Core.Ai;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui.Tests;

public sealed partial class AiTests
{
    private static string Sample(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples", name));

    private static async Task<(MainWindow Window, MainWindowViewModel Vm, FakeAi Ai)> OpenAsync(
        bool aiEnabled = true, string left = "long-old.cs", string right = "long-new.cs")
    {
        var ai = new FakeAi();
        var settings = new AppSettings { AiEnabled = aiEnabled };
        var vm = new MainWindowViewModel(new StartupArgs(Sample(left), Sample(right), null), settings, ai);
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        await vm.AnalysisReady;
        Dispatcher.UIThread.RunJobs();
        return (window, vm, ai);
    }

    [AvaloniaFact]
    public async Task Shows_summary_after_load()
    {
        var (window, vm, ai) = await OpenAsync();

        Assert.Equal(1, ai.Calls);
        Assert.Equal("Fake summary of the change.", vm.Summary);
        Assert.True(window.FindControl<Border>("AiBar")!.IsVisible);
        Assert.Contains("AI: Fake", vm.StatusText);
        Screenshots.Save(window, "20-ai-summary");
    }

    [AvaloniaFact]
    public async Task Shows_note_and_hints_for_current_difference()
    {
        var (window, vm, _) = await OpenAsync();

        vm.FirstDiffCommand.Execute(null);
        Assert.StartsWith("Difference 1   ·   ●●● critical   ·   Hunk 0 changes behavior.", vm.CurrentNoteText);
        Assert.Equal("⚠ Check callers of hunk 0.", vm.CurrentHintsText);

        var firstBlock = vm.Document!.Blocks[0];
        Assert.Contains(firstBlock.StartRow, window.LeftPane.HintRows);
        Assert.Contains(firstBlock.StartRow, window.RightPane.HintRows);
        Screenshots.Save(window, "21-ai-note");

        vm.CurrentBlock = 1;
        Assert.Contains("noise", vm.CurrentNoteText);
        Assert.Null(vm.CurrentHintsText);
    }

    [AvaloniaFact]
    public async Task Navigation_skips_noise()
    {
        var (window, vm, _) = await OpenAsync();
        Assert.Equal(5, vm.Document!.Blocks.Count);
        Assert.True(vm.HasNoise);
        Assert.Equal(0, vm.CurrentBlock);

        vm.NextDiffCommand.Execute(null);
        Assert.Equal(2, vm.CurrentBlock);
        vm.LastDiffCommand.Execute(null);
        Assert.Equal(4, vm.CurrentBlock);
        vm.PreviousDiffCommand.Execute(null);
        Assert.Equal(2, vm.CurrentBlock);
        Screenshots.Save(window, "22-ai-skip-noise");

        vm.SkipNoise = false;
        vm.PreviousDiffCommand.Execute(null);
        Assert.Equal(1, vm.CurrentBlock);
    }

    [AvaloniaFact]
    public async Task Disabled_ai_does_not_call_the_tool()
    {
        var (window, vm, ai) = await OpenAsync(aiEnabled: false);

        Assert.Equal(0, ai.Calls);
        Assert.Null(vm.Analysis);
        Assert.False(window.FindControl<Border>("AiBar")!.IsVisible);

        vm.AiEnabled = true;
        await vm.AnalysisReady;
        Assert.Equal(1, ai.Calls);
        Assert.NotNull(vm.Analysis);

        vm.AiEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(vm.Analysis);
        Assert.DoesNotContain("AI", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Reload_uses_cached_answer()
    {
        var (_, vm, ai) = await OpenAsync();

        await vm.LoadAsync();
        await vm.AnalysisReady;

        Assert.Equal(1, ai.Calls);
        Assert.NotNull(vm.Analysis);
    }

    [AvaloniaFact]
    public async Task Failure_is_shown_in_status_bar()
    {
        var ai = new FakeAi { Error = "Not logged in" };
        var vm = new MainWindowViewModel(new StartupArgs(Sample("old.cs"), Sample("new.cs"), null), new AppSettings { AiEnabled = true }, ai);
        await vm.LoadAsync();
        await vm.AnalysisReady;

        Assert.Null(vm.Analysis);
        Assert.Contains("AI failed: Not logged in", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Failed_reload_drops_analysis_started_during_compare()
    {
        var (_, vm, ai) = await OpenAsync(aiEnabled: false);
        var locked = Path.Combine(Path.GetTempPath(), $"difftastic-locked-{Guid.NewGuid():N}.cs");
        await File.WriteAllTextAsync(locked, "x");
        try
        {
            using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                vm.LeftPath = locked;
                var load = vm.LoadAsync();
                vm.AiEnabled = true;
                await load;
                await vm.AnalysisReady;
            }
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(vm.Error);
            Assert.Null(vm.Document);
            Assert.Null(vm.Analysis);
        }
        finally
        {
            File.Delete(locked);
        }
    }

    [AvaloniaFact]
    public async Task Closing_the_window_stops_the_analysis()
    {
        var ai = new FakeAi { Gate = new TaskCompletionSource() };
        var vm = new MainWindowViewModel(new StartupArgs(Sample("old.cs"), Sample("new.cs"), null), new AppSettings { AiEnabled = true }, ai);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        await vm.LoadAsync();

        window.Close();
        await vm.AnalysisReady;

        Assert.True(ai.Cancelled);
        Assert.Null(vm.Analysis);
    }

    /// <summary>Answers for every hunk in the prompt: even hunks important with a hint, odd hunks noise.</summary>
    public sealed partial class FakeAi : AiProvider
    {
        public int Calls { get; private set; }
        public string? Error { get; init; }
        public string? LastPrompt { get; private set; }

        /// <summary>When set, answers wait for it, so tests can act while a call runs.</summary>
        public TaskCompletionSource? Gate { get; set; }
        public bool Cancelled { get; private set; }
        public override string Name => "Fake";
        public override string Model => "fake-1";
        public override string Vendor => "nobody";

        public override async Task<string> CompleteJsonAsync(string prompt, string schema, CancellationToken ct = default)
        {
            Calls++;
            LastPrompt = prompt;
            if (Gate is not null)
            {
                try
                {
                    await Gate.Task.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    Cancelled = true;
                    throw;
                }
            }
            if (Error is not null)
                throw new InvalidOperationException(Error);
            if (schema == CommitExplainer.Schema)
                return JsonSerializer.Serialize(new { explanation = "Fake explanation." });

            var hunks = HunkHeader().Matches(prompt).Select(m => int.Parse(m.Groups[1].Value)).Select(i => new
            {
                hunk = i,
                importance = i % 2 == 0 ? 3 : 0,
                noise = i % 2 == 1,
                reason = i % 2 == 0 ? $"Hunk {i} changes behavior." : $"Hunk {i} only moves code.",
                reviewHints = i % 2 == 0 ? new[] { $"Check callers of hunk {i}." } : [],
            });
            return JsonSerializer.Serialize(new { summary = "Fake summary of the change.", hunks });
        }

        [GeneratedRegex(@"^### Hunk (\d+)$", RegexOptions.Multiline)]
        private static partial Regex HunkHeader();
    }
}
