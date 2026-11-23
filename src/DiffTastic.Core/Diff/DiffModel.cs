namespace DiffTastic.Core.Diff;

public enum RowKind { Equal, Changed, Added, Removed }

/// <summary>Character range in UTF-16 units.</summary>
public readonly record struct TextRange(int Start, int Length);

/// <summary>One line of a file. LineIndex is 0-based. Moved: the line is part of code moved to or from another block.</summary>
public sealed record DiffLine(int LineIndex, string Text, IReadOnlyList<TextRange> Changes, bool Moved = false);

public sealed record AlignedRow(DiffLine? Left, DiffLine? Right, RowKind Kind);

/// <summary>A run of consecutive non-equal rows.</summary>
public readonly record struct DiffBlock(int StartRow, int EndRow);

public sealed class DiffDocument
{
    public DiffDocument(IReadOnlyList<AlignedRow> rows, string language, string? engineNote,
        TextFormat? leftFormat = null, TextFormat? rightFormat = null)
    {
        Rows = MoveDetector.Mark(rows);
        Language = language;
        EngineNote = engineNote;
        LeftFormat = leftFormat;
        RightFormat = rightFormat;
        Blocks = BuildBlocks(rows);
        AddedLines = rows.Count(r => r.Kind != RowKind.Equal && r.Right is not null);
        RemovedLines = rows.Count(r => r.Kind != RowKind.Equal && r.Left is not null);
        MovedLines = Rows.Count(r => r.Right?.Moved == true);
        _whitespaceOnly = Blocks.Select(IsWhitespaceOnly).ToArray();
    }

    private readonly bool[] _whitespaceOnly;

    public IReadOnlyList<AlignedRow> Rows { get; }
    public IReadOnlyList<DiffBlock> Blocks { get; }
    public string Language { get; }

    /// <summary>Lines of the right file that are new or changed.</summary>
    public int AddedLines { get; }

    /// <summary>Lines of the left file that are removed or changed.</summary>
    public int RemovedLines { get; }

    /// <summary>Lines of the right file that were moved from another place.</summary>
    public int MovedLines { get; }

    /// <summary>Set when difftastic was not used, explains why.</summary>
    public string? EngineNote { get; }

    /// <summary>Null when the file does not exist.</summary>
    public TextFormat? LeftFormat { get; }

    public TextFormat? RightFormat { get; }

    /// <summary>True when both sides of the block are the same after removing all whitespace.</summary>
    public bool IsWhitespaceOnly(int block) => block >= 0 && block < _whitespaceOnly.Length && _whitespaceOnly[block];

    private bool IsWhitespaceOnly(DiffBlock block)
    {
        var rows = Rows.Skip(block.StartRow).Take(block.EndRow - block.StartRow + 1).ToList();
        return WithoutWhitespace(rows.Select(r => r.Left)) == WithoutWhitespace(rows.Select(r => r.Right));
    }

    private static string WithoutWhitespace(IEnumerable<DiffLine?> lines) =>
        string.Concat(lines.SelectMany(l => l?.Text ?? "").Where(c => !char.IsWhiteSpace(c)));

    private static List<DiffBlock> BuildBlocks(IReadOnlyList<AlignedRow> rows)
    {
        var blocks = new List<DiffBlock>();
        var start = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            var isDiff = rows[i].Kind != RowKind.Equal;
            if (isDiff && start < 0)
                start = i;
            else if (!isDiff && start >= 0)
            {
                blocks.Add(new DiffBlock(start, i - 1));
                start = -1;
            }
        }
        if (start >= 0)
            blocks.Add(new DiffBlock(start, rows.Count - 1));
        return blocks;
    }
}
