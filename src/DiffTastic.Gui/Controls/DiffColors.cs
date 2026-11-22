using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using DiffTastic.Core.Diff;

namespace DiffTastic.Gui.Controls;

public enum Side { Left, Right }

public static class DiffColors
{
    public static readonly IBrush ChangedRow = Brush("#FFF6D5");
    public static readonly IBrush RemovedRow = Brush("#FFE4E1");
    public static readonly IBrush AddedRow = Brush("#E3F6E3");
    public static readonly IBrush RemovedText = Brush("#F7A8A8");
    public static readonly IBrush AddedText = Brush("#9EDC9E");
    public static readonly IBrush Filler = Brush("#EEEEEE");
    public static readonly IBrush FillerHatch = Hatch("#D4D4D4", 8);
    public static readonly IPen CurrentBlockPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#3A6FD8")), 1.5);

    public static readonly IBrush ChangedMarker = Brush("#E0B000");
    public static readonly IBrush RemovedMarker = Brush("#D9534F");
    public static readonly IBrush AddedMarker = Brush("#3FA34D");

    private static readonly IPen ChangedHiddenPen = DashedPen(ChangedMarker);
    private static readonly IPen RemovedHiddenPen = DashedPen(RemovedMarker);
    private static readonly IPen AddedHiddenPen = DashedPen(AddedMarker);

    public static readonly IBrush LeftHeader = Brush("#DCE6F7");
    public static readonly IBrush RightHeader = Brush("#DDF1DD");
    public static readonly IBrush Gutter = Brush("#F4F4F4");
    public static readonly IBrush GutterText = Brush("#8A8A8A");

    public static IBrush? RowBackground(RowKind kind, Side side) => kind switch
    {
        RowKind.Changed => ChangedRow,
        RowKind.Removed when side == Side.Left => RemovedRow,
        RowKind.Added when side == Side.Right => AddedRow,
        _ => null,
    };

    public static IBrush CharHighlight(Side side) => side == Side.Left ? RemovedText : AddedText;

    public static IBrush Marker(RowKind kind) => kind switch
    {
        RowKind.Removed => RemovedMarker,
        RowKind.Added => AddedMarker,
        _ => ChangedMarker,
    };

    /// <summary>Dashed line under a folded line that hides changes of this kind.</summary>
    public static IPen HiddenChangePen(RowKind kind) => kind switch
    {
        RowKind.Removed => RemovedHiddenPen,
        RowKind.Added => AddedHiddenPen,
        _ => ChangedHiddenPen,
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
