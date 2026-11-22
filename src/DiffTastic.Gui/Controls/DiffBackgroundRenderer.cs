using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using DiffTastic.Core.Diff;

namespace DiffTastic.Gui.Controls;

/// <summary>Paints row colors, filler rows and changed characters. Document line N maps to row N-1.</summary>
public sealed class DiffBackgroundRenderer(Side side) : IBackgroundRenderer
{
    public IReadOnlyList<AlignedRow> Rows { get; set; } = [];
    public DiffBlock? CurrentBlock { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (Rows.Count == 0 || !textView.VisualLinesValid)
            return;

        var width = Math.Max(textView.Bounds.Width, 1);
        foreach (var visualLine in textView.VisualLines)
        {
            var rowIndex = visualLine.FirstDocumentLine.LineNumber - 1;
            if (rowIndex >= Rows.Count)
                continue;

            var row = Rows[rowIndex];
            var top = visualLine.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, top, width, visualLine.Height);
            var line = side == Side.Left ? row.Left : row.Right;

            if (line is null)
            {
                DrawFiller(dc, rect);
                continue;
            }

            if (DiffColors.RowBackground(row.Kind, side) is { } bg)
                dc.FillRectangle(bg, rect);

            if (row.Kind == RowKind.Changed)
            {
                var lineStart = visualLine.FirstDocumentLine.Offset;
                foreach (var change in line.Changes)
                {
                    var segment = new TextSegment { StartOffset = lineStart + change.Start, Length = change.Length };
                    foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment, false))
                        dc.FillRectangle(DiffColors.CharHighlight(side), r, 2);
                }
            }
        }

        DrawHiddenChanges(textView, dc, width);
        DrawCurrentBlock(textView, dc, width);
    }

    private void DrawHiddenChanges(TextView textView, DrawingContext dc, double width)
    {
        foreach (var visualLine in textView.VisualLines)
        {
            if (HiddenChange(visualLine) is not { } kind)
                continue;
            var top = visualLine.VisualTop - textView.VerticalOffset;
            dc.DrawLine(new Pen(DiffColors.Marker(kind), 1, DashStyle.Dash), new Point(0, top + visualLine.Height - 1), new Point(width, top + visualLine.Height - 1));
            dc.FillRectangle(DiffColors.Marker(kind), new Rect(0, top, 3, visualLine.Height));
        }
    }

    /// <summary>Kind of the first non-equal row inside a folded visual line, or null.</summary>
    private RowKind? HiddenChange(VisualLine visualLine)
    {
        var last = Math.Min(visualLine.LastDocumentLine.LineNumber, Rows.Count);
        for (var i = visualLine.FirstDocumentLine.LineNumber; i < last; i++)
        {
            if (Rows[i].Kind != RowKind.Equal)
                return Rows[i].Kind;
        }
        return null;
    }

    private void DrawCurrentBlock(TextView textView, DrawingContext dc, double width)
    {
        if (CurrentBlock is not { } block)
            return;

        var top = textView.GetVisualTopByDocumentLine(block.StartRow + 1) - textView.VerticalOffset;
        var bottom = textView.GetVisualTopByDocumentLine(block.EndRow + 2 <= Rows.Count ? block.EndRow + 2 : block.EndRow + 1)
                     - textView.VerticalOffset;
        if (block.EndRow + 2 > Rows.Count)
            bottom += textView.DefaultLineHeight;

        dc.DrawLine(DiffColors.CurrentBlockPen, new Point(0, top), new Point(width, top));
        dc.DrawLine(DiffColors.CurrentBlockPen, new Point(0, bottom), new Point(width, bottom));
    }

    private static void DrawFiller(DrawingContext dc, Rect rect)
    {
        dc.FillRectangle(DiffColors.Filler, rect);
        using (dc.PushClip(rect))
        {
            const double step = 8;
            for (var x = rect.Left - rect.Height; x < rect.Right; x += step)
                dc.DrawLine(DiffColors.FillerHatch, new Point(x, rect.Bottom), new Point(x + rect.Height, rect.Top));
        }
    }
}
