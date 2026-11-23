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

    /// <summary>Rows come from a <see cref="UnifiedLayout"/>: each row shows the side it has.</summary>
    public bool Unified { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Rows.Count == 0 || !textView.VisualLinesValid)
            return;

        var colors = DiffColors.For(textView.ActualThemeVariant);
        var width = Math.Max(textView.Bounds.Width, 1);
        foreach (var visualLine in textView.VisualLines)
        {
            var rowIndex = visualLine.FirstDocumentLine.LineNumber - 1;
            if (rowIndex >= Rows.Count)
                continue;

            var row = Rows[rowIndex];
            var top = visualLine.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, top, width, visualLine.Height);
            var lineSide = Unified ? UnifiedRow.SideOf(row) : side;
            var line = lineSide == Side.Left ? row.Left : row.Right;

            if (line is null)
            {
                DrawFiller(drawingContext, colors, rect);
                continue;
            }

            if (colors.RowBackground(Unified ? UnifiedRow.KindOf(row) : row.Kind, lineSide) is { } bg)
                drawingContext.FillRectangle(bg, rect);

            if (row.Kind == RowKind.Changed)
            {
                var lineStart = visualLine.FirstDocumentLine.Offset;
                foreach (var change in line.Changes)
                {
                    var segment = new TextSegment { StartOffset = lineStart + change.Start, Length = change.Length };
                    foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment, false))
                        drawingContext.FillRectangle(colors.CharHighlight(lineSide), r, 2);
                }
            }
        }

        DrawHiddenChanges(textView, drawingContext, colors, width);
        DrawCurrentBlock(textView, drawingContext, colors, width);
    }

    private void DrawHiddenChanges(TextView textView, DrawingContext dc, DiffColors colors, double width)
    {
        foreach (var visualLine in textView.VisualLines)
        {
            if (HiddenChange(visualLine) is not { } kind)
                continue;
            var top = visualLine.VisualTop - textView.VerticalOffset;
            dc.DrawLine(colors.HiddenChangePen(kind), new Point(0, top + visualLine.Height - 1), new Point(width, top + visualLine.Height - 1));
            dc.FillRectangle(colors.Marker(kind), new Rect(0, top, 3, visualLine.Height));
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

    private void DrawCurrentBlock(TextView textView, DrawingContext dc, DiffColors colors, double width)
    {
        if (CurrentBlock is not { } block)
            return;

        var top = textView.GetVisualTopByDocumentLine(block.StartRow + 1) - textView.VerticalOffset;
        var bottom = textView.GetVisualTopByDocumentLine(block.EndRow + 2 <= Rows.Count ? block.EndRow + 2 : block.EndRow + 1)
                     - textView.VerticalOffset;
        if (block.EndRow + 2 > Rows.Count)
            bottom += textView.DefaultLineHeight;

        dc.DrawLine(colors.CurrentBlockPen, new Point(0, top), new Point(width, top));
        dc.DrawLine(colors.CurrentBlockPen, new Point(0, bottom), new Point(width, bottom));
    }

    private static void DrawFiller(DrawingContext dc, DiffColors colors, Rect rect)
    {
        dc.FillRectangle(colors.Filler, rect);
        dc.FillRectangle(colors.FillerHatch, rect);
    }
}
