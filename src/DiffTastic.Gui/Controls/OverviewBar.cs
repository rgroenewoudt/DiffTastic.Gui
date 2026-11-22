using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DiffTastic.Core.Ai;
using DiffTastic.Core.Diff;

namespace DiffTastic.Gui.Controls;

/// <summary>KDiff3-style overview: all differences of the file in one column, plus the visible range.</summary>
public sealed class OverviewBar : Control
{
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

    /// <summary>AI notes per block; important blocks get a strip on the left.</summary>
    public IReadOnlyList<(DiffBlock Block, HunkNote Note)> Notes { get; set { field = value; InvalidateVisual(); } } = [];

    private const double StripWidth = 4;

    public OverviewBar()
    {
        Width = 22;
        Cursor = new Cursor(StandardCursorType.Hand);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var colors = DiffColors.For(ActualThemeVariant);
        var h = Bounds.Height;
        context.FillRectangle(colors.OverviewBackground, new Rect(Bounds.Size));
        if (Rows.Count == 0)
            return;

        var rowHeight = h / Rows.Count;
        var left = StripWidth + 3;
        foreach (var (start, count, kind) in _runs)
        {
            var height = Math.Max(3, count * rowHeight);
            context.FillRectangle(colors.Marker(kind), new Rect(left, start * rowHeight, Bounds.Width - left - 3, height));
        }

        foreach (var (block, note) in Notes)
        {
            var height = Math.Max(4, (block.EndRow - block.StartRow + 1) * rowHeight);
            if (note.IsImportant)
                context.FillRectangle(colors.ImportantMarker, new Rect(1, block.StartRow * rowHeight, StripWidth, height));
            else if (note.IsNoise)
                context.FillRectangle(colors.OverviewNoiseFade, new Rect(left, block.StartRow * rowHeight, Bounds.Width - left - 3, height));
        }

        var vTop = Viewport.First * rowHeight;
        var vHeight = Math.Max(4, (Viewport.Last - Viewport.First) * rowHeight);
        context.DrawRectangle(colors.ViewportFill, colors.ViewportPen, new Rect(0.5, vTop, Bounds.Width - 1, vHeight));
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
