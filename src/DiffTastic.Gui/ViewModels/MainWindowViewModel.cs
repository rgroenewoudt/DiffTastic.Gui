using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffTastic.Core.Diff;
using DiffTastic.Core.Difft;

namespace DiffTastic.Gui.ViewModels;

public partial class MainWindowViewModel(StartupArgs args) : ObservableObject
{
    private readonly DiffService _diffService = new(DifftRunner.Locate());

    [ObservableProperty] private string? _leftPath = args.Left;
    [ObservableProperty] private string? _rightPath = args.Right;
    [ObservableProperty] private string? _repoPath = args.RepoPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextDiffCommand), nameof(PreviousDiffCommand), nameof(FirstDiffCommand), nameof(LastDiffCommand))]
    private DiffDocument? _document;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(NextDiffCommand), nameof(PreviousDiffCommand))]
    private int _currentBlock = -1;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    public string LeftTitle => DisplayName(LeftPath);
    public string RightTitle => DisplayName(RightPath);

    public string StatusText
    {
        get
        {
            if (Document is null)
                return Error ?? (IsBusy ? "Comparing…" : "Open two files to compare.");

            var count = Document.Blocks.Count;
            var position = count == 0 ? "Files are identical" :
                CurrentBlock >= 0 ? $"Difference {CurrentBlock + 1} of {count}" : $"{count} differences";
            var engine = Document.EngineNote ?? "difftastic";
            return $"{position}   ·   {Document.Language}   ·   {engine}";
        }
    }

    partial void OnLeftPathChanged(string? value) => OnPropertyChanged(nameof(LeftTitle));
    partial void OnRightPathChanged(string? value) => OnPropertyChanged(nameof(RightTitle));
    partial void OnDocumentChanged(DiffDocument? value) => OnPropertyChanged(nameof(StatusText));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(StatusText));

    public async Task LoadAsync()
    {
        if (LeftPath is null || RightPath is null)
            return;

        IsBusy = true;
        Error = null;
        try
        {
            CurrentBlock = -1;
            Document = await Task.Run(() => _diffService.CompareAsync(LeftPath, RightPath));
            if (Document.Blocks.Count > 0)
                CurrentBlock = 0;
        }
        catch (Exception ex)
        {
            Document = null;
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenAsync(string left, string right, string? repoPath = null)
    {
        LeftPath = left;
        RightPath = right;
        RepoPath = repoPath;
        await LoadAsync();
    }

    public void SelectBlockAtRow(int row)
    {
        if (Document is null)
            return;
        for (var i = 0; i < Document.Blocks.Count; i++)
        {
            if (Document.Blocks[i].StartRow <= row && row <= Document.Blocks[i].EndRow)
            {
                CurrentBlock = i;
                return;
            }
        }
    }

    private bool HasBlocks() => Document is { Blocks.Count: > 0 };
    private bool CanGoNext() => Document is not null && CurrentBlock < Document.Blocks.Count - 1;
    private bool CanGoPrevious() => Document is not null && CurrentBlock > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextDiff() => CurrentBlock++;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousDiff() => CurrentBlock--;

    [RelayCommand(CanExecute = nameof(HasBlocks))]
    private void FirstDiff() => CurrentBlock = 0;

    [RelayCommand(CanExecute = nameof(HasBlocks))]
    private void LastDiff() => CurrentBlock = Document!.Blocks.Count - 1;

    private string DisplayName(string? path)
    {
        if (path is null)
            return "";
        return RepoPath is not null ? $"{RepoPath}   ({path})" : path;
    }
}
