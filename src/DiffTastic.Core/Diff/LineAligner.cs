using System.Text;
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
        foreach (var change in list)
        {
            var start = Utf8ToUtf16Index(text, change.Start);
            var end = Utf8ToUtf16Index(text, change.End);
            if (end > start)
                ranges.Add(new TextRange(start, end - start));
        }
        return new DiffLine(index, text, ranges);
    }

    internal static int Utf8ToUtf16Index(string text, int byteOffset)
    {
        var bytes = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (bytes >= byteOffset)
                return i;

            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                bytes += 4;
                i++;
            }
            else
            {
                bytes += Encoding.UTF8.GetByteCount(text.AsSpan(i, 1));
            }
        }
        return text.Length;
    }
}
