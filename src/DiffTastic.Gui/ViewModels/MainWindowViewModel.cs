using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffTastic.Core.Diff;
using DiffTastic.Core.Difft;
using DiffTastic.Core.Git;
using DiffTastic.Gui.Controls;

namespace DiffTastic.Gui.ViewModels;

public partial class MainWindowViewModel(StartupArgs args) : ObservableObject
{
    private readonly DiffService _diffService = new(DifftRunner.Locate());
    private BlameService? _blameService;
    private Task<BlameResult?> _leftBlame = Task.FromResult<BlameResult?>(null);
    private Task<BlameResult?> _rightBlame = Task.FromResult<BlameResult?>(null);
    private CancellationTokenSource? _blameCts;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _blameStatus;

    /// <summary>Completes when blame for both sides is loaded (or failed).</summary>
    public Task BlameReady { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftTitle))]
    private string? _leftRevision;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RightTitle))]
    private string? _rightRevision;

    public string LeftTitle => DisplayName(LeftPath, LeftRevision);
    public string RightTitle => DisplayName(RightPath, RightRevision);

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
            var text = $"{position}   ·   {Document.Language}   ·   {engine}";
            return BlameStatus is null ? text : $"{text}   ·   {BlameStatus}";
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
            BlameReady = LoadBlameAsync(LeftPath, RightPath);
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

    private async Task LoadBlameAsync(string left, string right)
    {
        _blameCts?.Cancel();
        var cts = _blameCts = new CancellationTokenSource();
        var ct = cts.Token;

        _blameService = null;
        _leftBlame = Task.FromResult<BlameResult?>(null);
        _rightBlame = Task.FromResult<BlameResult?>(null);
        BlameStatus = null;
        LeftRevision = null;
        RightRevision = null;

        var root = await GitRunner.FindRepoRootAsync(Path.GetDirectoryName(right)!)
                   ?? await GitRunner.FindRepoRootAsync(Environment.CurrentDirectory);
        var repoPath = RepoPath ?? (root is null ? null : RelativeInside(root, right) ?? RelativeInside(root, left));
        if (ct.IsCancellationRequested || root is null || repoPath is null)
            return;

        var git = new GitRunner(root);
        var locator = new RevisionLocator(git, root);
        var service = new BlameService(git);
        BlameStatus = "loading blame…";
        _leftBlame = BlameSideAsync(service, locator, left, repoPath, label => LeftRevision = label, ct);
        _rightBlame = BlameSideAsync(service, locator, right, repoPath, label => RightRevision = label, ct);
        _blameService = service;

        var l = await _leftBlame;
        var r = await _rightBlame;
        if (ct.IsCancellationRequested)
            return;
        BlameStatus = l is null && r is null ? "no blame" : $"blame: {repoPath}";
    }

    private static async Task<BlameResult?> BlameSideAsync(
        BlameService service, RevisionLocator locator, string file, string repoPath, Action<string> setRevision,
        CancellationToken ct)
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length == 0)
                return null;
            var target = await Task.Run(() => locator.LocateAsync(file, repoPath, ct), ct);
            if (ct.IsCancellationRequested)
                return null;
            setRevision(RevisionLabel(target));
            return await Task.Run(() => service.BlameAsync(target, ct), ct);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? RelativeInside(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        return relative.StartsWith("..") || Path.IsPathRooted(relative) ? null : relative.Replace('\\', '/');
    }

    /// <summary>Blame popup for a row, or null when there is no blame for that line.</summary>
    public async Task<BlamePopupViewModel?> CreateBlamePopupAsync(Side side, int row)
    {
        if (Document is null || _blameService is not { } service || row < 0 || row >= Document.Rows.Count)
            return null;

        var aligned = Document.Rows[row];
        var line = side == Side.Left ? aligned.Left : aligned.Right;
        var blame = await (side == Side.Left ? _leftBlame : _rightBlame);
        if (line is null || blame is null || line.LineIndex >= blame.Lines.Count)
            return null;

        var blameLine = blame.Lines[line.LineIndex];
        if (blameLine is null || !blame.Commits.TryGetValue(blameLine.Sha, out var commit))
            return null;

        return new BlamePopupViewModel(service, blameLine, commit, aligned.Kind, side, line.LineIndex + 1);
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

    private string DisplayName(string? path, string? revision)
    {
        if (path is null)
            return "";
        var name = RepoPath ?? path;
        return revision is null ? name : $"{name}   ·   {revision}";
    }

    private static string RevisionLabel(BlameTarget target) => target switch
    {
        BlameTarget.WorkingTree => "working tree",
        BlameTarget.Commit { Sha: "HEAD" } => "HEAD",
        BlameTarget.Commit c => c.Sha[..Math.Min(8, c.Sha.Length)],
        _ => "uncommitted",
    };
}
