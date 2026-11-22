using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using DiffTastic.Core.Diff;

namespace DiffTastic.Gui.Controls;

/// <summary>KDiff3-style overview: all differences of the file in one column, plus the visible range.</summary>
public sealed class OverviewBar : Control
{
    private static readonly IBrush Background = new ImmutableSolidColorBrush(Color.Parse("#F7F7F7"));
    private static readonly IBrush ViewportFill = new ImmutableSolidColorBrush(Color.FromArgb(40, 58, 111, 216));
    private static readonly IPen ViewportPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#3A6FD8")), 1);

    public event EventHandler<int>? RowRequested;

    private List<(int Start, int Count, RowKind Kind)> _runs = [];

    public IReadOnlyList<AlignedRow> Rows
    {
        get;
        set
        {
            field = value;
            _runs = ChangedRuns(value);
            InvalidateVisual();
        }
    } = [];

    /// <summary>First and last visible row.</summary>
    public (double First, double Last) Viewport { get; set { field = value; InvalidateVisual(); } }

    public OverviewBar()
    {
        Width = 18;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public override void Render(DrawingContext dc)
    {
        var h = Bounds.Height;
        dc.FillRectangle(Background, new Rect(Bounds.Size));
        if (Rows.Count == 0)
            return;

        var rowHeight = h / Rows.Count;
        foreach (var (start, count, kind) in _runs)
        {
            var height = Math.Max(3, count * rowHeight);
            dc.FillRectangle(DiffColors.Marker(kind), new Rect(3, start * rowHeight, Bounds.Width - 6, height));
        }

        var vTop = Viewport.First * rowHeight;
        var vHeight = Math.Max(4, (Viewport.Last - Viewport.First) * rowHeight);
        dc.DrawRectangle(ViewportFill, ViewportPen, new Rect(0.5, vTop, Bounds.Width - 1, vHeight));
    }

    /// <summary>Runs of consecutive rows with the same non-equal kind.</summary>
    private static List<(int Start, int Count, RowKind Kind)> ChangedRuns(IReadOnlyList<AlignedRow> rows)
    {
        var runs = new List<(int, int, RowKind)>();
        var i = 0;
        while (i < rows.Count)
        {
            var kind = rows[i].Kind;
            var start = i;
            while (i < rows.Count && rows[i].Kind == kind)
                i++;
            if (kind != RowKind.Equal)
                runs.Add((start, i - start, kind));
        }
        return runs;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        RequestAt(e.GetPosition(this).Y);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            RequestAt(e.GetPosition(this).Y);
    }

    private void RequestAt(double y)
    {
        if (Rows.Count == 0 || Bounds.Height <= 0)
            return;
        var row = (int)Math.Clamp(y / Bounds.Height * Rows.Count, 0, Rows.Count - 1);
        RowRequested?.Invoke(this, row);
    }
}
