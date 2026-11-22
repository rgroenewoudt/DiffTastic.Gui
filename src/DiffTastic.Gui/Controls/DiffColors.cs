using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using DiffTastic.Core.Diff;

namespace DiffTastic.Gui.Controls;

public enum Side { Left, Right }

public sealed class DiffColors
{
    public static readonly DiffColors Light = new(
        changedRow: "#FFF6D5", removedRow: "#FFE4E1", addedRow: "#E3F6E3",
        removedText: "#F7A8A8", addedText: "#9EDC9E",
        filler: "#EEEEEE", fillerHatch: "#D4D4D4", accent: "#3A6FD8",
        changedMarker: "#E0B000", removedMarker: "#D9534F", addedMarker: "#3FA34D", importantMarker: "#E8590C",
        leftHeader: "#DCE6F7", rightHeader: "#DDF1DD",
        editor: "#FFFFFF", gutter: "#F4F4F4", gutterText: "#8A8A8A", overview: "#F7F7F7");

    public static readonly DiffColors Dark = new(
        changedRow: "#3A3520", removedRow: "#4B2626", addedRow: "#233D27",
        removedText: "#7A3434", addedText: "#2F6B37",
        filler: "#252526", fillerHatch: "#38383C", accent: "#5B8DEF",
        changedMarker: "#C9A227", removedMarker: "#E06C66", addedMarker: "#4CB860", importantMarker: "#F07A3A",
        leftHeader: "#23324A", rightHeader: "#233A28",
        editor: "#1E1E1E", gutter: "#252526", gutterText: "#858585", overview: "#252526");

    public static DiffColors For(ThemeVariant variant) => variant == ThemeVariant.Dark ? Dark : Light;

    private DiffColors(
        string changedRow, string removedRow, string addedRow, string removedText, string addedText,
        string filler, string fillerHatch, string accent,
        string changedMarker, string removedMarker, string addedMarker, string importantMarker,
        string leftHeader, string rightHeader, string editor, string gutter, string gutterText, string overview)
    {
        ChangedRow = Brush(changedRow);
        RemovedRow = Brush(removedRow);
        AddedRow = Brush(addedRow);
        RemovedText = Brush(removedText);
        AddedText = Brush(addedText);
        Filler = Brush(filler);
        FillerHatch = Hatch(fillerHatch, 8);
        CurrentBlockPen = new ImmutablePen(Brush(accent), 1.5);
        ChangedMarker = Brush(changedMarker);
        RemovedMarker = Brush(removedMarker);
        AddedMarker = Brush(addedMarker);
        ImportantMarker = Brush(importantMarker);
        _changedHiddenPen = DashedPen(ChangedMarker);
        _removedHiddenPen = DashedPen(RemovedMarker);
        _addedHiddenPen = DashedPen(AddedMarker);
        LeftHeader = Brush(leftHeader);
        RightHeader = Brush(rightHeader);
        EditorBackground = Brush(editor);
        Gutter = Brush(gutter);
        GutterText = Brush(gutterText);
        var overviewColor = Color.Parse(overview);
        OverviewBackground = new ImmutableSolidColorBrush(overviewColor);
        OverviewNoiseFade = new ImmutableSolidColorBrush(Color.FromArgb(180, overviewColor.R, overviewColor.G, overviewColor.B));
        var accentColor = Color.Parse(accent);
        ViewportFill = new ImmutableSolidColorBrush(Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B));
        ViewportPen = new ImmutablePen(Brush(accent), 1);
    }

    public IBrush ChangedRow { get; }
    public IBrush RemovedRow { get; }
    public IBrush AddedRow { get; }
    public IBrush RemovedText { get; }
    public IBrush AddedText { get; }
    public IBrush Filler { get; }
    public IBrush FillerHatch { get; }
    public IPen CurrentBlockPen { get; }

    public IBrush ChangedMarker { get; }
    public IBrush RemovedMarker { get; }
    public IBrush AddedMarker { get; }

    public IBrush ImportantMarker { get; }

    private readonly IPen _changedHiddenPen;
    private readonly IPen _removedHiddenPen;
    private readonly IPen _addedHiddenPen;

    public IBrush LeftHeader { get; }
    public IBrush RightHeader { get; }
    public IBrush EditorBackground { get; }
    public IBrush Gutter { get; }
    public IBrush GutterText { get; }

    public IBrush OverviewBackground { get; }
    public IBrush OverviewNoiseFade { get; }
    public IBrush ViewportFill { get; }
    public IPen ViewportPen { get; }

    public IBrush? RowBackground(RowKind kind, Side side) => kind switch
    {
        RowKind.Changed => ChangedRow,
        RowKind.Removed when side == Side.Left => RemovedRow,
        RowKind.Added when side == Side.Right => AddedRow,
        _ => null,
    };

    public IBrush CharHighlight(Side side) => side == Side.Left ? RemovedText : AddedText;

    public IBrush Marker(RowKind kind) => kind switch
    {
        RowKind.Removed => RemovedMarker,
        RowKind.Added => AddedMarker,
        _ => ChangedMarker,
    };

    /// <summary>Dashed line under a folded line that hides changes of this kind.</summary>
    public IPen HiddenChangePen(RowKind kind) => kind switch
    {
        RowKind.Removed => _removedHiddenPen,
        RowKind.Added => _addedHiddenPen,
        _ => _changedHiddenPen,
    };

    private static ImmutablePen DashedPen(IBrush brush) => new Pen(brush, 1, DashStyle.Dash).ToImmutable();

    private static ImmutableSolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    /// <summary>Tiled 45° lines, <paramref name="step"/> pixels apart.</summary>
    private static DrawingBrush Hatch(string hex, double step)
    {
        var lines = new GeometryGroup
        {
            Children =
            {
                new LineGeometry(new Point(0, step), new Point(step, 0)),
                new LineGeometry(new Point(-1, 1), new Point(1, -1)),
                new LineGeometry(new Point(step - 1, step + 1), new Point(step + 1, step - 1)),
            },
        };
        var tile = new RelativeRect(0, 0, step, step, RelativeUnit.Absolute);
        return new DrawingBrush(new GeometryDrawing { Geometry = lines, Pen = new ImmutablePen(Brush(hex), 1) })
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            SourceRect = tile,
            DestinationRect = tile,
        };
    }
}
