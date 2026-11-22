using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using DiffTastic.Core.Diff;

namespace DiffTastic.Gui.Controls;

/// <summary>Shows the original file line number per row (blank for filler rows) and a change marker.</summary>
public sealed class RowNumberMargin(Side side) : AbstractMargin
{
    private const double MarkerWidth = 4;
    private static readonly Typeface Font = new("Cascadia Mono, Consolas, monospace");
    private const double FontSize = 12;

    private readonly Dictionary<int, FormattedText> _numbers = [];

    public IReadOnlyList<AlignedRow> Rows
    {
        get;
        set
        {
            field = value;
            _numbers.Clear();
            InvalidateMeasure();
            InvalidateVisual();
        }
    } = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var digits = Math.Max(3, Rows.Count.ToString(CultureInfo.InvariantCulture).Length);
        var text = Format(new string('9', digits));
        return new Size(text.Width + 14 + MarkerWidth, 0);
    }

    public override void Render(DrawingContext context)
    {
        var textView = TextView;
        context.FillRectangle(DiffColors.Gutter, new Rect(Bounds.Size));
        if (textView is not { VisualLinesValid: true })
            return;

        foreach (var visualLine in textView.VisualLines)
        {
            var rowIndex = visualLine.FirstDocumentLine.LineNumber - 1;
            if (rowIndex >= Rows.Count)
                continue;

            var row = Rows[rowIndex];
            var line = side == Side.Left ? row.Left : row.Right;
            var top = visualLine.VisualTop - textView.VerticalOffset;

            if (row.Kind != RowKind.Equal)
                context.FillRectangle(DiffColors.Marker(row.Kind), new Rect(Bounds.Width - MarkerWidth, top, MarkerWidth, visualLine.Height));

            if (line is null)
                continue;

            var text = Number(line.LineIndex + 1);
            var x = Bounds.Width - MarkerWidth - 6 - text.Width;
            context.DrawText(text, new Point(x, top + (visualLine.Height - text.Height) / 2));
        }
    }

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null)
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null)
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    private FormattedText Number(int number)
    {
        if (!_numbers.TryGetValue(number, out var text))
            _numbers[number] = text = Format(number.ToString(CultureInfo.InvariantCulture));
        return text;
    }

    private static FormattedText Format(string s) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, FontSize, DiffColors.GutterText);
}
