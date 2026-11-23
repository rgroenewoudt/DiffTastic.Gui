using DiffTastic.Core.Diff;

namespace DiffTastic.Gui.Controls;

/// <summary>How a row of a <see cref="UnifiedLayout"/> is drawn.</summary>
public static class UnifiedRow
{
    /// <summary>Side whose line the row shows.</summary>
    public static Side SideOf(AlignedRow row) => row.Right is null ? Side.Left : Side.Right;

    /// <summary>Changed lines show as removed (old) or added (new).</summary>
    public static RowKind KindOf(AlignedRow row) => row.Kind == RowKind.Changed
        ? SideOf(row) == Side.Left ? RowKind.Removed : RowKind.Added
        : row.Kind;
}
