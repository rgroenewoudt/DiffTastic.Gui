using System.Globalization;
using Avalonia;
using Avalonia.Controls;
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

    public IReadOnlySet<int> HintRows
    {
        get;
        set
        {
            field = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    } = new HashSet<int>();

    /// <summary>Rows come from a <see cref="UnifiedLayout"/>: shows the old and the new line number.</summary>
    public bool Unified
    {
        get;
        set
        {
            field = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private const double HintSize = 7;
    private const double ColumnGap = 8;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty)
        {
            _numbers.Clear();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var hintSpace = HintRows.Count > 0 ? HintSize + 3 : 0;
        var numbers = Unified ? NumberWidth * 2 + ColumnGap : NumberWidth;
        return new Size(numbers + 14 + MarkerWidth + hintSpace, 0);
    }

    public override void Render(DrawingContext context)
    {
        var colors = DiffColors.For(ActualThemeVariant);
        var textView = TextView;
        context.FillRectangle(colors.Gutter, new Rect(Bounds.Size));
        if (textView is not { VisualLinesValid: true })
            return;

        var numberWidth = Unified ? NumberWidth : 0;
        foreach (var visualLine in textView.VisualLines)
        {
            var rowIndex = visualLine.FirstDocumentLine.LineNumber - 1;
            if (rowIndex >= Rows.Count)
                continue;

            var row = Rows[rowIndex];
            var top = visualLine.VisualTop - textView.VerticalOffset;
            var kind = Unified ? UnifiedRow.KindOf(row) : row.Kind;

            if (kind != RowKind.Equal)
                context.FillRectangle(colors.Marker(kind), new Rect(Bounds.Width - MarkerWidth, top, MarkerWidth, visualLine.Height));

            if (HintRows.Contains(rowIndex))
            {
                var center = new Point(3 + HintSize / 2, top + visualLine.Height / 2);
                context.DrawEllipse(colors.ImportantMarker, null, center, HintSize / 2, HintSize / 2);
            }

            var right = Bounds.Width - MarkerWidth - 6;
            if (Unified)
            {
                DrawNumber(context, row.Right, right, top, visualLine.Height);
                DrawNumber(context, row.Left, right - numberWidth - ColumnGap, top, visualLine.Height);
            }
            else
                DrawNumber(context, side == Side.Left ? row.Left : row.Right, right, top, visualLine.Height);
        }
    }

    private double NumberWidth => Format(new string('9', Math.Max(3, Rows.Count.ToString(CultureInfo.InvariantCulture).Length))).Width;

    private void DrawNumber(DrawingContext context, DiffLine? line, double right, double top, double height)
    {
        if (line is null)
            return;
        var text = Number(line.LineIndex + 1);
        context.DrawText(text, new Point(right - text.Width, top + (height - text.Height) / 2));
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

    private FormattedText Format(string s) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, FontSize, DiffColors.For(ActualThemeVariant).GutterText);
}
