using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffTastic.Core.Ai;
using DiffTastic.Core.Diff;
using DiffTastic.Core.Difft;
using DiffTastic.Core.Git;
using DiffTastic.Gui.Controls;
using DiffTastic.Gui.Resources;

namespace DiffTastic.Gui.ViewModels;

public sealed partial class MainWindowViewModel(StartupArgs args, AppSettings? settings = null, AiProvider? ai = null)
    : ObservableObject, IDisposable
{
    private readonly DiffService _diffService = new(DifftRunner.Locate());
    private readonly AppSettings _settings = settings ?? new AppSettings();
    private DiffAnalyzer? _analyzer = ai is null ? null : new DiffAnalyzer(ai, Localization.AiLanguage);
    private CommitExplainer? _explainer = ai is null ? null : new CommitExplainer(ai, Localization.AiLanguage);
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
    private Func<string>? _blameStatus;

    /// <summary>Completes when blame for both sides is loaded (or failed).</summary>
    public Task BlameReady { get; private set; } = Task.CompletedTask;

    /// <summary>Completes when the AI analysis is done (or failed or skipped).</summary>
    public Task AnalysisReady { get; private set; } = Task.CompletedTask;

    [ObservableProperty] private bool _aiEnabled = (settings?.AiEnabled ?? false) && ai is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private Func<string>? _aiStatus;

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
        ? $"{Localization.Format(Strings.Difference, note.Hunk + 1)}   ·   {ImportanceLabel(note)}   ·   {note.Reason}"
        : null;

    public string? CurrentHintsText => CurrentNote is { ReviewHints.Count: > 0 } note
        ? string.Join('\n', note.ReviewHints.Select(h => "⚠ " + h))
        : null;

    private static string ImportanceLabel(HunkNote note) => note.IsNoise ? Strings.ImportanceNoise : note.Importance switch
    {
        3 => "●●● " + Strings.ImportanceCritical,
        2 => "●● " + Strings.ImportanceNotable,
        1 => "● " + Strings.ImportanceMinor,
        _ => Strings.ImportanceTrivial,
    };

    public bool IsAiAvailable => _analyzer is not null;

    public AppSettings Settings => _settings;

    /// <summary>Finds the AI tool for the settings; replaceable in tests.</summary>
    public Func<AppSettings, AiProvider?> LocateAi { get; init; } =
        s => AiLocator.Locate(s.AiProvider, s.ClaudeModel, s.CodexModel);

    /// <summary>Saves the settings and switches to the AI tool and model they ask for.</summary>
    public void ApplySettings()
    {
        _settings.Save();
        var languageChanged = Localization.Resolve(_settings.Language) != Localization.Current;
        Localization.Apply(_settings.Language);
        if (languageChanged)
            OnPropertyChanged(string.Empty);
        var ai = LocateAi(_settings);
        var changed = languageChanged || ai?.Name != _analyzer?.Provider.Name || ai?.Model != _analyzer?.Provider.Model;
        if (changed)
        {
            CancelAnalysis();
            _analyzer = ai is null ? null : new DiffAnalyzer(ai, Localization.AiLanguage);
            _explainer = ai is null ? null : new CommitExplainer(ai, Localization.AiLanguage);
            OnPropertyChanged(nameof(IsAiAvailable));
            OnPropertyChanged(nameof(AiToolTip));
        }

        var enabled = _settings.AiEnabled && ai is not null;
        if (AiEnabled != enabled)
            AiEnabled = enabled;
        else if (changed)
            AnalysisReady = AnalyzeAsync();
    }

    public string AiToolTip => _analyzer?.Provider is { } p
        ? Localization.Format(Strings.AiToolTip, p.Name, p.Model, p.Vendor)
        : Strings.AiNotFound;

    public string? Summary => Analysis?.Summary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftTitle))]
    private BlameTarget? _leftRevision;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RightTitle))]
    private BlameTarget? _rightRevision;

    public string LeftTitle => DisplayName(LeftPath, LeftRevision);
    public string RightTitle => DisplayName(RightPath, RightRevision);

    public string StatusText
    {
        get
        {
            if (Document is null)
                return Error ?? (IsBusy ? Strings.Comparing : Strings.OpenTwoFiles);

            var count = Document.Blocks.Count;
            var position = count == 0 ? Strings.FilesIdentical :
                CurrentBlock >= 0 ? Localization.Format(Strings.DifferenceOf, CurrentBlock + 1, count)
                : Localization.Format(Strings.DifferenceCount, count);
            var engine = Document.EngineNote ?? "difftastic";
            var text = $"{position}   ·   {Document.Language}   ·   {engine}";
            if (BlameStatus is not null)
                text += $"   ·   {BlameStatus()}";
            return AiStatus is null ? text : $"{text}   ·   {AiStatus()}";
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
            CancelAnalysis();
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

    /// <summary>Stops a running AI analysis and clears its result.</summary>
    public void CancelAnalysis()
    {
        _aiCts?.Cancel();
        _aiCts = null;
        Analysis = null;
        AiStatus = null;
    }

    private async Task AnalyzeAsync()
    {
        CancelAnalysis();
        if (!AiEnabled || _analyzer is not { } analyzer || Document is not { } doc)
            return;

        var cts = _aiCts = new CancellationTokenSource();
        var provider = analyzer.Provider;
        var fileName = RepoPath ?? Path.GetFileName(RightPath ?? LeftPath ?? "");
        AiStatus = () => Localization.Format(Strings.AiAnalyzing, provider.Name);
        try
        {
            var analysis = doc.Blocks.Count == 0
                ? new DiffAnalysis(Strings.FilesIdentical, [])
                : await Task.Run(() => analyzer.AnalyzeAsync(doc, fileName, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
                return;
            Analysis = analysis;
            if (IsSkipped(CurrentBlock) && NearestBlock(CurrentBlock) is >= 0 and var nearest)
                CurrentBlock = nearest;
            AiStatus = () => Localization.Format(Strings.AiDone, provider.Name, provider.Model);
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                var message = FirstLine(ex.Message);
                AiStatus = () => Localization.Format(Strings.AiFailed, message);
            }
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
        BlameStatus = () => Strings.BlameLoading;
        _leftBlame = BlameSideAsync(service, locator, left, repoPath, label => LeftRevision = label, ct);
        _rightBlame = BlameSideAsync(service, locator, right, repoPath, label => RightRevision = label, ct);
        _blameService = service;

        var l = await _leftBlame;
        var r = await _rightBlame;
        if (ct.IsCancellationRequested)
            return;
        BlameStatus = l is null && r is null ? () => Strings.BlameNone : () => Localization.Format(Strings.BlameFor, repoPath);
    }

    private static async Task<BlameResult?> BlameSideAsync(
        BlameService service, RevisionLocator locator, string file, string repoPath, Action<BlameTarget> setRevision,
        CancellationToken ct)
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length == 0)
                return null;
            var target = await Task.Run(() => locator.LocateAsync(file, repoPath, ct), ct);
            if (ct.IsCancellationRequested)
                return null;
            setRevision(target);
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

    /// <summary>First block that is not skipped at or after <paramref name="block"/>, else before it; -1 if none.</summary>
    private int NearestBlock(int block) => FindBlock(block, 1) is >= 0 and var next ? next : FindBlock(block, -1);

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

    private string DisplayName(string? path, BlameTarget? revision)
    {
        if (path is null)
            return "";
        var name = RepoPath ?? path;
        return revision is null ? name : $"{name}   ·   {RevisionLabel(revision)}";
    }

    public void Dispose()
    {
        _blameCts?.Cancel();
        _blameCts?.Dispose();
        _blameCts = null;
        _aiCts?.Cancel();
        _aiCts = null;
    }

    private static string RevisionLabel(BlameTarget target) => target switch
    {
        BlameTarget.WorkingTree => Strings.RevisionWorkingTree,
        BlameTarget.Commit { Sha: "HEAD" } => "HEAD",
        BlameTarget.Commit c => c.Sha[..Math.Min(8, c.Sha.Length)],
        _ => Strings.RevisionUncommitted,
    };
}
