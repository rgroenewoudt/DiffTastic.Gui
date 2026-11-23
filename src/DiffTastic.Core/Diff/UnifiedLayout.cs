namespace DiffTastic.Core.Diff;

/// <summary>
/// Rows for a one-column view. Equal rows stay as they are; in each block first all old lines are listed
/// (Right is null), then all new lines (Left is null). Blocks keep their index.
/// </summary>
public sealed class UnifiedLayout
{
    private readonly int[] _sourceRows;
    private readonly int[] _firstRows;

    public UnifiedLayout(DiffDocument document)
    {
        var rows = new List<AlignedRow>();
        var sources = new List<int>();
        var i = 0;
        while (i < document.Rows.Count)
        {
            if (document.Rows[i].Kind == RowKind.Equal)
            {
                rows.Add(document.Rows[i]);
                sources.Add(i);
                i++;
                continue;
            }

            var end = i;
            while (end < document.Rows.Count && document.Rows[end].Kind != RowKind.Equal)
                end++;
            for (var r = i; r < end; r++)
            {
                if (document.Rows[r] is { Left: not null } row)
                {
                    rows.Add(row with { Right = null });
                    sources.Add(r);
                }
            }
            for (var r = i; r < end; r++)
            {
                if (document.Rows[r] is { Right: not null } row)
                {
                    rows.Add(row with { Left = null });
                    sources.Add(r);
                }
            }
            i = end;
        }

        Rows = rows;
        _sourceRows = sources.ToArray();
        _firstRows = new int[document.Rows.Count + 1];
        Array.Fill(_firstRows, -1);
        for (var r = _sourceRows.Length - 1; r >= 0; r--)
            _firstRows[_sourceRows[r]] = r;
        _firstRows[^1] = rows.Count;
        for (var r = _firstRows.Length - 2; r >= 0; r--)
        {
            if (_firstRows[r] < 0)
                _firstRows[r] = _firstRows[r + 1];
        }

        Blocks = document.Blocks.Select(b => new DiffBlock(RowOf(b.StartRow), RowOf(b.EndRow + 1) - 1)).ToList();
    }

    public IReadOnlyList<AlignedRow> Rows { get; }
    public IReadOnlyList<DiffBlock> Blocks { get; }

    /// <summary>Row of the side-by-side document this unified row comes from.</summary>
    public int SourceRow(int row) => _sourceRows.Length == 0 ? 0 : _sourceRows[Math.Clamp(row, 0, _sourceRows.Length - 1)];

    /// <summary>First unified row of a side-by-side row; the row count for the end.</summary>
    public int RowOf(int sourceRow) => _firstRows[Math.Clamp(sourceRow, 0, _firstRows.Length - 1)];
}
