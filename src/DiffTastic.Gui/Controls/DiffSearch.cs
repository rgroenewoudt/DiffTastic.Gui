using Avalonia.Threading;
using AvaloniaEdit.Search;

namespace DiffTastic.Gui.Controls;

/// <summary>
/// Ctrl+F search over both panes. One pane shows its AvaloniaEdit SearchPanel; the other pane's panel
/// stays hidden but follows the same pattern, so both panes highlight matches. Next/previous walks the
/// matches of both panes in row order.
/// </summary>
public sealed class DiffSearch
{
    private readonly DiffPane _left;
    private readonly DiffPane _right;
    private readonly Action<int> _scrollToRow;
    private DiffPane _active;
    private DiffPane _lastPane;
    private Position _anchor;
    private bool _wired;
    private bool _syncing;

    private readonly record struct Position(int Row, int Side, int Offset) : IComparable<Position>
    {
        public int CompareTo(Position other) => (Row, Side, Offset).CompareTo((other.Row, other.Side, other.Offset));
    }

    public readonly record struct Match(DiffPane Pane, int Row, int Offset, int Length);

    public DiffSearch(DiffPane left, DiffPane right, Action<int> scrollToRow)
    {
        _left = left;
        _right = right;
        _scrollToRow = scrollToRow;
        _active = right;
        _lastPane = right;
    }

    public bool IsOpen => _active.Editor.SearchPanel is { IsOpened: true };

    /// <summary>Opens the search box in the given pane (right pane when null).</summary>
    public void Open(DiffPane? pane)
    {
        if (_left.Editor.SearchPanel is null || _right.Editor.SearchPanel is null)
            return;
        Wire();

        _active = pane ?? _right;
        var active = _active.Editor.SearchPanel!;
        var other = Other(_active).Editor.SearchPanel!;
        _lastPane = _active;
        _anchor = CaretPosition(_active);

        var selection = _active.Editor.TextArea.Selection;
        var pattern = !selection.IsEmpty && !selection.IsMultiline ? selection.GetText() : null;

        active.IsVisible = true;
        other.IsVisible = false;
        other.Open();
        active.Open();
        if (pattern is not null)
            active.SearchPattern = pattern;
        CopyOptions(active, other);
        Dispatcher.UIThread.Post(active.Reactivate, DispatcherPriority.Input);
    }

    /// <summary>Selects the next (or previous) match in either pane, wrapping around.</summary>
    public void FindNext(bool backwards = false)
    {
        var matches = Matches();
        if (matches.Count == 0)
            return;

        var from = CaretPosition(_lastPane);
        var next = backwards
            ? matches.LastOrDefault(m => Key(m).CompareTo(from) < 0, matches[^1])
            : matches.FirstOrDefault(m => Key(m).CompareTo(from) > 0, matches[0]);
        Show(next);
    }

    /// <summary>Matches of the current pattern in both panes, in row order (left before right).</summary>
    public IReadOnlyList<Match> Matches()
    {
        var panel = _active.Editor.SearchPanel;
        if (panel is null || panel.IsClosed || string.IsNullOrEmpty(panel.SearchPattern))
            return [];

        ISearchStrategy strategy;
        try
        {
            strategy = SearchStrategyFactory.Create(panel.SearchPattern, !panel.MatchCase, panel.WholeWords,
                panel.UseRegex ? SearchMode.RegEx : SearchMode.Normal);
        }
        catch (SearchPatternException)
        {
            return [];
        }

        var matches = new List<Match>();
        foreach (var pane in new[] { _left, _right })
        {
            var document = pane.Editor.Document;
            foreach (var result in strategy.FindAll(document, 0, document.TextLength))
                matches.Add(new Match(pane, document.GetLineByOffset(result.Offset).LineNumber - 1, result.Offset, result.Length));
        }
        return matches.OrderBy(Key).ToList();
    }

    private void Wire()
    {
        if (_wired)
            return;
        _wired = true;

        foreach (var pane in new[] { _left, _right })
        {
            var panel = pane.Editor.SearchPanel!;
            panel.SearchOptionsChanged += (_, _) => OnOptionsChanged(panel);
            panel.DetachedFromVisualTree += (_, _) => CloseOther(pane);
            pane.Editor.TextArea.Caret.PositionChanged += (_, _) => _lastPane = pane;
        }
    }

    private void OnOptionsChanged(SearchPanel source)
    {
        if (_syncing)
            return;
        CopyOptions(source, source == _left.Editor.SearchPanel ? _right.Editor.SearchPanel! : _left.Editor.SearchPanel!);
        Dispatcher.UIThread.Post(() =>
        {
            var first = Matches().FirstOrDefault(m => Key(m).CompareTo(_anchor) >= 0);
            if (first.Pane is not null)
                Show(first);
            else if (Matches() is [var wrapped, ..])
                Show(wrapped);
        }, DispatcherPriority.Background);
    }

    private void CopyOptions(SearchPanel source, SearchPanel target)
    {
        _syncing = true;
        try
        {
            target.MatchCase = source.MatchCase;
            target.WholeWords = source.WholeWords;
            target.UseRegex = source.UseRegex;
            target.SearchPattern = source.SearchPattern;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void CloseOther(DiffPane closed)
    {
        if (Other(closed).Editor.SearchPanel is { IsOpened: true } other)
            other.Close();
    }

    private void Show(Match match)
    {
        var area = match.Pane.Editor.TextArea;
        Other(match.Pane).Editor.TextArea.ClearSelection();
        _scrollToRow(match.Row);
        area.Caret.Offset = match.Offset + match.Length;
        area.Selection = AvaloniaEdit.Editing.Selection.Create(area, match.Offset, match.Offset + match.Length);
        _lastPane = match.Pane;
    }

    private DiffPane Other(DiffPane pane) => pane == _left ? _right : _left;

    private static Position Key(Match m) => new(m.Row, m.Pane.Side == Side.Left ? 0 : 1, m.Offset);

    private static Position CaretPosition(DiffPane pane)
    {
        var area = pane.Editor.TextArea;
        var offset = area.Selection.IsEmpty ? area.Caret.Offset : area.Selection.SurroundingSegment.Offset;
        var row = pane.Editor.Document.GetLineByOffset(offset).LineNumber - 1;
        return new Position(row, pane.Side == Side.Left ? 0 : 1, offset);
    }
}
