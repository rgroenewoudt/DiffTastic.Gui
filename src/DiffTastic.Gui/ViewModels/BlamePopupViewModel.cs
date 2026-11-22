using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffTastic.Core.Ai;
using DiffTastic.Core.Diff;
using DiffTastic.Core.Git;
using DiffTastic.Gui.Controls;

namespace DiffTastic.Gui.ViewModels;

public sealed partial class BlamePopupViewModel : ObservableObject
{
    private readonly BlameService _service;
    private readonly BlameLine _line;
    private readonly CommitExplainer? _explainer;
    private readonly string _lineText;

    public BlamePopupViewModel(
        BlameService service, BlameLine line, CommitInfo commit, RowKind kind, Side side, int lineNumber,
        CommitExplainer? explainer = null, string lineText = "")
    {
        _service = service;
        _line = line;
        _commit = commit;
        _commits = [commit];
        _explainer = explainer;
        _lineText = lineText;
        Kind = kind;
        LineLabel = $"{(side == Side.Left ? "A" : "B")} · line {lineNumber}";
    }

    public bool CanExplain => _explainer is not null && !Commit.IsUncommitted;

    public string ExplainToolTip => _explainer is { Provider: var p }
        ? $"Explain this commit with {p.Name}. Sends the commit to {p.Vendor}."
        : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExplanation))]
    private string? _explanation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExplainCommand))]
    private bool _isExplaining;

    public bool HasExplanation => Explanation is not null;

    partial void OnCommitChanged(CommitInfo value)
    {
        OnPropertyChanged(nameof(CanExplain));
        ExplainCommand.NotifyCanExecuteChanged();
        if (IsExplaining && _explainingSha == value.Sha)
            return;
        Explanation = _explainer is not null && _explainer.TryGetCached(value.Sha, _lineText, out var cached) ? cached : null;
    }

    private string? _explainingSha;

    private bool CanRunExplain() => CanExplain && !IsExplaining;

    [RelayCommand(CanExecute = nameof(CanRunExplain))]
    private async Task ExplainAsync()
    {
        if (_explainer is null)
            return;
        var commit = Commit;
        _explainingSha = commit.Sha;
        IsExplaining = true;
        Explanation = "Explaining…";
        try
        {
            var diff = await _service.GetCommitDiffAsync(commit.Sha, _line.OriginalPath);
            var full = await _service.GetCommitAsync(commit);
            var text = await Task.Run(() => _explainer.ExplainAsync(full, diff, _lineText));
            if (Commit.Sha == commit.Sha)
                Explanation = text;
        }
        catch (Exception ex)
        {
            if (Commit.Sha == commit.Sha)
                Explanation = "AI failed: " + ex.Message.Split('\n', 2)[0].Trim();
        }
        finally
        {
            IsExplaining = false;
        }
    }

    public RowKind Kind { get; }
    public string KindLabel => Kind switch
    {
        RowKind.Added => "Added",
        RowKind.Removed => "Removed",
        RowKind.Changed => "Changed",
        _ => "Unchanged",
    };
    public string LineLabel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText), nameof(Message), nameof(Sha))]
    private CommitInfo _commit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    [NotifyCanExecuteChangedFor(nameof(OlderCommand), nameof(NewerCommand))]
    private IReadOnlyList<CommitInfo> _commits;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    [NotifyCanExecuteChangedFor(nameof(OlderCommand), nameof(NewerCommand))]
    private int _index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private bool _isLoadingHistory;

    public string Sha => Commit.Sha;

    public string HeaderText => Commit.IsUncommitted
        ? "This line is not committed yet."
        : Index == 0
            ? $"Line comes from commit {Commit.ShortSha} by {Commit.Author}, authored {FormatTime(Commit.AuthorTime)} with message:"
            : $"Before that, commit {Commit.ShortSha} by {Commit.Author} changed this line, authored {FormatTime(Commit.AuthorTime)} with message:";

    public string Message => Commit.IsUncommitted ? "Uncommitted change in the working copy." : Commit.Message;

    public string PositionText => IsLoadingHistory ? "…" : $"{Index + 1} / {Commits.Count}";

    public async Task LoadAsync()
    {
        if (Commit.IsUncommitted)
            return;

        IsLoadingHistory = true;
        try
        {
            Commit = await _service.GetCommitAsync(Commit);
            var history = await _service.LineHistoryAsync(_line.Sha, _line.OriginalPath, _line.OriginalLine);
            if (history.Count > 0)
            {
                Commits = history;
                Commit = history[Index];
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            IsLoadingHistory = false;
        }
    }

    private bool CanGoOlder() => Index < Commits.Count - 1;
    private bool CanGoNewer() => Index > 0;

    [RelayCommand(CanExecute = nameof(CanGoOlder))]
    private void Older() => Show(Index + 1);

    [RelayCommand(CanExecute = nameof(CanGoNewer))]
    private void Newer() => Show(Index - 1);

    private void Show(int index)
    {
        Index = index;
        Commit = Commits[index];
    }

    public static string FormatTime(DateTimeOffset time)
    {
        var age = DateTimeOffset.Now - time;
        var relative = age.TotalMinutes < 60 ? $"{Math.Max(1, (int)age.TotalMinutes)} min ago"
            : age.TotalHours < 24 ? $"{(int)age.TotalHours} h ago"
            : age.TotalDays < 60 ? $"{(int)age.TotalDays} days ago"
            : age.TotalDays < 730 ? $"{(int)(age.TotalDays / 30)} months ago"
            : $"{(int)(age.TotalDays / 365)} years ago";
        return string.Create(CultureInfo.InvariantCulture, $"{time:HH:mm} on {time:d MMM yyyy} ({relative})");
    }
}
