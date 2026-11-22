using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffTastic.Core.Ai;
using DiffTastic.Core.Diff;
using DiffTastic.Core.Git;
using DiffTastic.Gui.Controls;
using DiffTastic.Gui.Resources;

namespace DiffTastic.Gui.ViewModels;

public sealed partial class BlamePopupViewModel : ObservableObject, IDisposable
{
    private readonly BlameService _service;
    private readonly BlameLine _line;
    private readonly CommitExplainer? _explainer;
    private readonly string _lineText;
    private readonly CancellationTokenSource _cts = new();

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
        LineLabel = Localization.Format(Strings.LineLabel, side == Side.Left ? "A" : "B", lineNumber);
    }

    public bool CanExplain => _explainer is not null && !Commit.IsUncommitted;

    public string ExplainToolTip => _explainer is { Provider: var p }
        ? Localization.Format(Strings.ExplainTip, p.Name, p.Vendor)
        : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExplanation))]
    private string? _explanation;

    /// <summary>Commits with an explanation request still running.</summary>
    private readonly HashSet<string> _explaining = [];

    public bool IsExplaining => _explaining.Contains(Commit.Sha);

    public bool HasExplanation => Explanation is not null;

    partial void OnCommitChanged(CommitInfo value)
    {
        OnPropertyChanged(nameof(CanExplain));
        ExplainCommand.NotifyCanExecuteChanged();
        if (IsExplaining)
        {
            Explanation = Strings.Explaining;
            return;
        }
        Explanation = _explainer is not null && _explainer.TryGetCached(value.Sha, _lineText, out var cached) ? cached : null;
    }

    /// <summary>Stops a running explanation; called when the popup goes away.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }

    private bool _disposed;

    private bool CanRunExplain() => CanExplain && !IsExplaining && !_disposed;

    [RelayCommand(CanExecute = nameof(CanRunExplain), AllowConcurrentExecutions = true)]
    private async Task ExplainAsync()
    {
        if (_explainer is null || _disposed)
            return;
        var commit = Commit;
        var ct = _cts.Token;
        _explaining.Add(commit.Sha);
        ExplainCommand.NotifyCanExecuteChanged();
        Explanation = Strings.Explaining;
        try
        {
            var diff = await _service.GetCommitDiffAsync(commit.Sha, commit.Path ?? _line.OriginalPath, ct);
            var full = await _service.GetCommitAsync(commit, ct);
            var text = await Task.Run(() => _explainer.ExplainAsync(full, diff, _lineText, ct), ct);
            if (Commit.Sha == commit.Sha)
                Explanation = text;
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested && Commit.Sha == commit.Sha)
                Explanation = null;
            else if (Commit.Sha == commit.Sha)
                Explanation = Localization.Format(Strings.AiFailed, ex.Message.Split('\n', 2)[0].Trim());
        }
        finally
        {
            _explaining.Remove(commit.Sha);
            if (!_disposed)
                ExplainCommand.NotifyCanExecuteChanged();
        }
    }

    public RowKind Kind { get; }
    public string KindLabel => Kind switch
    {
        RowKind.Added => Strings.KindAdded,
        RowKind.Removed => Strings.KindRemoved,
        RowKind.Changed => Strings.KindChanged,
        _ => Strings.KindUnchanged,
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
        ? Strings.NotCommitted
        : Index == 0
            ? Localization.Format(Strings.LineFromCommit, Commit.ShortSha, Commit.Author, FormatTime(Commit.AuthorTime))
            : Localization.Format(Strings.BeforeThatCommit, Commit.ShortSha, Commit.Author, FormatTime(Commit.AuthorTime));

    public string Message => Commit.IsUncommitted ? Strings.UncommittedMessage : Commit.Message;

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
        var relative = age.TotalMinutes < 60 ? Localization.Format(Strings.MinutesAgo, Math.Max(1, (int)age.TotalMinutes))
            : age.TotalHours < 24 ? Localization.Format(Strings.HoursAgo, (int)age.TotalHours)
            : age.TotalDays < 60 ? Localization.Format(Strings.DaysAgo, (int)age.TotalDays)
            : age.TotalDays < 730 ? Localization.Format(Strings.MonthsAgo, (int)(age.TotalDays / 30))
            : Localization.Format(Strings.YearsAgo, (int)(age.TotalDays / 365));
        return Localization.Format(Strings.TimeFormat, time, relative);
    }
}
