namespace DiffTastic.Core.Diff;

/// <summary>Finds code that was removed in one block and added in another block.</summary>
public static class MoveDetector
{
    /// <summary>Minimum number of non-blank lines of a move.</summary>
    public const int MinLines = 3;

    private readonly record struct Entry(int Row, int Block, string Text, bool Changed);

    /// <summary>The rows, with <see cref="DiffLine.Moved"/> set on lines that are part of a move.</summary>
    public static IReadOnlyList<AlignedRow> Mark(IReadOnlyList<AlignedRow> rows)
    {
        var blocks = BlockIds(rows);
        var left = Entries(rows, blocks, r => r.Left);
        var right = Entries(rows, blocks, r => r.Right);
        if (left.Count < MinLines || right.Count < MinLines)
            return rows;

        var rightByText = new Dictionary<string, List<int>>();
        for (var j = 0; j < right.Count; j++)
        {
            if (!right[j].Changed)
                continue;
            if (!rightByText.TryGetValue(right[j].Text, out var list))
                rightByText[right[j].Text] = list = [];
            list.Add(j);
        }

        var usedRight = new bool[right.Count];
        var movedLeft = new HashSet<int>();
        var movedRight = new HashSet<int>();
        var i = 0;
        while (i < left.Count)
        {
            var (bestStart, bestLength) = (-1, 0);
            if (left[i].Changed && rightByText.TryGetValue(left[i].Text, out var candidates))
            {
                foreach (var j in candidates)
                {
                    var length = RunLength(left, right, usedRight, i, j);
                    if (length > bestLength && IsMove(left, right, i, j, length))
                        (bestStart, bestLength) = (j, length);
                }
            }

            if (bestLength == 0)
            {
                i++;
                continue;
            }

            for (var k = 0; k < bestLength; k++)
            {
                movedLeft.Add(left[i + k].Row);
                movedRight.Add(right[bestStart + k].Row);
                usedRight[bestStart + k] = true;
            }
            i += bestLength;
        }

        if (movedLeft.Count == 0)
            return rows;

        var result = new List<AlignedRow>(rows.Count);
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (movedLeft.Contains(r))
                row = row with { Left = row.Left! with { Moved = true } };
            if (movedRight.Contains(r))
                row = row with { Right = row.Right! with { Moved = true } };
            result.Add(row);
        }
        return result;
    }

    /// <summary>Block number per row; -1 for equal rows.</summary>
    private static int[] BlockIds(IReadOnlyList<AlignedRow> rows)
    {
        var ids = new int[rows.Count];
        var block = -1;
        for (var r = 0; r < rows.Count; r++)
        {
            var changed = rows[r].Kind != RowKind.Equal;
            if (changed && (r == 0 || rows[r - 1].Kind == RowKind.Equal))
                block++;
            ids[r] = changed ? block : -1;
        }
        return ids;
    }

    /// <summary>Non-blank lines of one side, trimmed.</summary>
    private static List<Entry> Entries(IReadOnlyList<AlignedRow> rows, int[] blocks, Func<AlignedRow, DiffLine?> side)
    {
        var entries = new List<Entry>();
        for (var r = 0; r < rows.Count; r++)
        {
            var text = side(rows[r])?.Text.Trim();
            if (!string.IsNullOrEmpty(text))
                entries.Add(new Entry(r, blocks[r], text, blocks[r] >= 0));
        }
        return entries;
    }

    private static int RunLength(List<Entry> left, List<Entry> right, bool[] usedRight, int i, int j)
    {
        var k = 0;
        while (i + k < left.Count && j + k < right.Count && !usedRight[j + k]
               && left[i + k].Changed && right[j + k].Changed && left[i + k].Text == right[j + k].Text)
            k++;
        return k;
    }

    /// <summary>Long enough, has real code, and the two sides are in different blocks.</summary>
    private static bool IsMove(List<Entry> left, List<Entry> right, int i, int j, int length)
    {
        if (length < MinLines)
            return false;

        var code = 0;
        for (var k = 0; k < length; k++)
        {
            if (left[i + k].Text.Any(char.IsLetterOrDigit))
                code++;
        }
        if (code < 2)
            return false;

        var (leftFirst, leftLast) = (left[i].Block, left[i + length - 1].Block);
        var (rightFirst, rightLast) = (right[j].Block, right[j + length - 1].Block);
        return leftLast < rightFirst || rightLast < leftFirst;
    }
}
