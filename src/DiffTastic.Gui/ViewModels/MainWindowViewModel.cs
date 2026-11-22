using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffTastic.Core.Ai;
using DiffTastic.Core.Diff;
using DiffTastic.Core.Difft;
using DiffTastic.Core.Git;
using DiffTastic.Gui.Controls;

namespace DiffTastic.Gui.ViewModels;

public sealed partial class MainWindowViewModel(StartupArgs args, AppSettings? settings = null, AiProvider? ai = null)
    : ObservableObject
{
    private readonly DiffService _diffService = new(DifftRunner.Locate());
    private readonly AppSettings _settings = settings ?? new AppSettings();
    private readonly DiffAnalyzer? _analyzer = ai is null ? null : new DiffAnalyzer(ai);
    private readonly CommitExplainer? _explainer = ai is null ? null : new CommitExplainer(ai);
    private CancellationTokenSource? _aiCts;
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
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(CurrentNote), nameof(CurrentNoteText), nameof(CurrentHintsText))]
    [NotifyCanExecuteChangedFor(nameof(NextDiffCommand), nameof(PreviousDiffCommand))]
    private int _currentBlock = -1;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _blameStatus;

    /// <summary>Completes when blame for both sides is loaded (or failed).</summary>
    public Task BlameReady { get; private set; } = Task.CompletedTask;

    /// <summary>Completes when the AI analysis is done (or failed or skipped).</summary>
    public Task AnalysisReady { get; private set; } = Task.CompletedTask;

    [ObservableProperty] private bool _aiEnabled = (settings?.AiEnabled ?? false) && ai is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _aiStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(CurrentNote), nameof(CurrentNoteText), nameof(CurrentHintsText), nameof(HasNoise))]
    [NotifyCanExecuteChangedFor(nameof(NextDiffCommand), nameof(PreviousDiffCommand))]
    private DiffAnalysis? _analysis;

    /// <summary>Next/previous skip hunks the AI marked as noise.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextDiffCommand), nameof(PreviousDiffCommand))]
    private bool _skipNoise = true;

    public bool HasNoise => Analysis?.Hunks.Any(h => h.IsNoise) == true;

    public HunkNote? CurrentNote => Analysis?.NoteFor(CurrentBlock);

    public string? CurrentNoteText => CurrentNote is { } note
        ? $"Difference {note.Hunk + 1}   ·   {ImportanceLabel(note)}   ·   {note.Reason}"
        : null;

    public string? CurrentHintsText => CurrentNote is { ReviewHints.Count: > 0 } note
        ? string.Join('\n', note.ReviewHints.Select(h => "⚠ " + h))
        : null;

    private static string ImportanceLabel(HunkNote note) => note.IsNoise ? "noise" : note.Importance switch
    {
        3 => "●●● critical",
        2 => "●● notable",
        1 => "● minor",
        _ => "trivial",
    };

    public bool IsAiAvailable => _analyzer is not null;

    public string AiToolTip => _analyzer?.Provider is { } p
        ? $"AI analysis with {p.Name} ({p.Model}). Sends the changed code to {p.Vendor}."
        : "No AI tool found (claude or codex on PATH).";

    public string? Summary => Analysis?.Summary;

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
            if (BlameStatus is not null)
                text += $"   ·   {BlameStatus}";
            return AiStatus is null ? text : $"{text}   ·   {AiStatus}";
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
            CancelAnalysis();
            Document = await Task.Run(() => _diffService.CompareAsync(LeftPath, RightPath));
            if (Document.Blocks.Count > 0)
                CurrentBlock = 0;
            BlameReady = LoadBlameAsync(LeftPath, RightPath);
            AnalysisReady = AnalyzeAsync();
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

    partial void OnAiEnabledChanged(bool value)
    {
        _settings.AiEnabled = value;
        _settings.Save();
        AnalysisReady = value ? AnalyzeAsync() : Task.CompletedTask;
        if (!value)
            CancelAnalysis();
    }

    private void CancelAnalysis()
    {
        _aiCts?.Cancel();
        _aiCts = null;
        Analysis = null;
        AiStatus = null;
    }

    private async Task AnalyzeAsync()
    {
        CancelAnalysis();
        if (!AiEnabled || _analyzer is null || Document is not { } doc)
            return;

        var cts = _aiCts = new CancellationTokenSource();
        var provider = _analyzer.Provider;
        var fileName = RepoPath ?? Path.GetFileName(RightPath ?? LeftPath ?? "");
        AiStatus = $"AI: analyzing with {provider.Name}…";
        try
        {
            var analysis = await Task.Run(() => _analyzer.AnalyzeAsync(doc, fileName, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
                return;
            Analysis = analysis;
            if (IsSkipped(CurrentBlock) && FindBlock(0, 1) is >= 0 and var first)
                CurrentBlock = first;
            AiStatus = $"AI: {provider.Name} ({provider.Model})";
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
                AiStatus = "AI failed: " + FirstLine(ex.Message);
        }
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length <= 160 ? line : line[..160] + "…";
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
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : relative.Replace('\\', '/');
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

        return new BlamePopupViewModel(
            service, blameLine, commit, aligned.Kind, side, line.LineIndex + 1, AiEnabled ? _explainer : null, line.Text);
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
    private bool CanGoNext() => FindBlock(CurrentBlock + 1, 1) >= 0;
    private bool CanGoPrevious() => FindBlock(CurrentBlock - 1, -1) >= 0;

    /// <summary>First block from <paramref name="start"/> in <paramref name="step"/> direction, skipping noise when asked; -1 if none.</summary>
    private int FindBlock(int start, int step)
    {
        if (Document is null)
            return -1;
        for (var i = start; i >= 0 && i < Document.Blocks.Count; i += step)
        {
            if (!IsSkipped(i))
                return i;
        }
        return -1;
    }

    private bool IsSkipped(int block) => SkipNoise && Analysis?.IsNoise(block) == true;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextDiff() => CurrentBlock = FindBlock(CurrentBlock + 1, 1);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousDiff() => CurrentBlock = FindBlock(CurrentBlock - 1, -1);

    [RelayCommand(CanExecute = nameof(HasBlocks))]
    private void FirstDiff() => CurrentBlock = Math.Max(0, FindBlock(0, 1));

    [RelayCommand(CanExecute = nameof(HasBlocks))]
    private void LastDiff()
    {
        var last = Document!.Blocks.Count - 1;
        CurrentBlock = FindBlock(last, -1) is >= 0 and var found ? found : last;
    }

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
