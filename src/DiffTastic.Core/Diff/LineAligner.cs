using DiffTastic.Core.Difft;

namespace DiffTastic.Core.Diff;

public static class LineAligner
{
    /// <summary>Shown instead of a CR inside a line, so editors do not split the line.</summary>
    public const char CarriageReturnSymbol = '␍';

    public static string[] SplitLines(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd('\r');

        if (lines.Length > 1 && text.EndsWith('\n'))
            Array.Resize(ref lines, lines.Length - 1);
        return lines;
    }

    public static IReadOnlyList<AlignedRow> Align(string[] left, string[] right, DifftResult result)
    {
        var leftChanges = new Dictionary<int, List<DifftChange>>();
        var rightChanges = new Dictionary<int, List<DifftChange>>();
        foreach (var line in result.Chunks?.SelectMany(c => c) ?? [])
        {
            if (line.Lhs is { } l)
                leftChanges[l.LineNumber] = l.Changes;
            if (line.Rhs is { } r)
                rightChanges[r.LineNumber] = r.Changes;
        }

        var pairs = result.AlignedLines ?? OneToOne(left.Length, right.Length);
        var rows = new List<AlignedRow>(pairs.Count);
        foreach (var pair in pairs)
        {
            var li = pair[0] is { } a && a < left.Length ? a : (int?)null;
            var ri = pair[1] is { } b && b < right.Length ? b : (int?)null;
            if (li is null && ri is null)
                continue;

            var leftLine = li is { } x ? MakeLine(x, left[x], leftChanges) : null;
            var rightLine = ri is { } y ? MakeLine(y, right[y], rightChanges) : null;

            var kind = (leftLine, rightLine) switch
            {
                (null, _) => RowKind.Added,
                (_, null) => RowKind.Removed,
                _ when leftChanges.ContainsKey(li!.Value) || rightChanges.ContainsKey(ri!.Value) => RowKind.Changed,
                _ => RowKind.Equal,
            };
            rows.Add(new AlignedRow(leftLine, rightLine, kind));
        }
        return rows;
    }

    private static List<int?[]> OneToOne(int leftCount, int rightCount)
    {
        var count = Math.Max(leftCount, rightCount);
        var pairs = new List<int?[]>(count);
        for (var i = 0; i < count; i++)
            pairs.Add([i < leftCount ? i : null, i < rightCount ? i : null]);
        return pairs;
    }

    private static DiffLine MakeLine(int index, string text, Dictionary<int, List<DifftChange>> changes)
    {
        if (!changes.TryGetValue(index, out var list) || list.Count == 0)
            return new DiffLine(index, text, []);

        var ranges = new List<TextRange>(list.Count);
        var cursor = new Utf8Cursor(text);
        foreach (var change in list)
        {
            var start = cursor.ToUtf16(change.Start);
            var end = cursor.ToUtf16(change.End);
            if (end > start)
                ranges.Add(new TextRange(start, end - start));
        }
        return new DiffLine(index, text, ranges);
    }

    internal static int Utf8ToUtf16Index(string text, int byteOffset) => new Utf8Cursor(text).ToUtf16(byteOffset);

    /// <summary>Maps UTF-8 byte offsets to UTF-16 indexes; walks forward from the last offset.</summary>
    private struct Utf8Cursor(string text)
    {
        private int _index;
        private int _bytes;

        public int ToUtf16(int byteOffset)
        {
            if (byteOffset < _bytes)
                (_index, _bytes) = (0, 0);

            while (_index < text.Length && _bytes < byteOffset)
            {
                var c = text[_index];
                if (char.IsHighSurrogate(c) && _index + 1 < text.Length && char.IsLowSurrogate(text[_index + 1]))
                {
                    _bytes += 4;
                    _index += 2;
                }
                else
                {
                    _bytes += c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
                    _index++;
                }
            }
            return _index;
        }
    }
}
