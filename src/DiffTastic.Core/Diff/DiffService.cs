using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using DiffTastic.Core.Difft;

namespace DiffTastic.Core.Diff;

public sealed class DiffService(DifftRunner? difft)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public async Task<DiffDocument> CompareAsync(string leftPath, string rightPath, CancellationToken ct = default)
    {
        var (leftText, leftFormat) = await ReadTextAsync(leftPath, ct);
        var (rightText, rightFormat) = await ReadTextAsync(rightPath, ct);
        var left = LineAligner.SplitLines(leftText);
        var right = LineAligner.SplitLines(rightText);

        if (difft is null)
            return Fallback(left, right, "difft.exe not found, using simple line diff.", leftFormat, rightFormat);

        try
        {
            var result = await difft.RunAsync(EmptyIfMissing(leftPath), EmptyIfMissing(rightPath), DefaultTimeout, ct);
            var rows = LineAligner.Align(left, right, result);
            return new DiffDocument(rows, result.Language ?? "Text", null, leftFormat, rightFormat);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Fallback(left, right, $"{ex.Message} Using simple line diff.", leftFormat, rightFormat);
        }
    }

    private static async Task<(string Text, TextFormat? Format)> ReadTextAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return ("", null);

        var bytes = await File.ReadAllBytesAsync(path, ct);
        var format = TextFormat.Detect(bytes);
        return (format.Decode(bytes), format);
    }

    private static string EmptyIfMissing(string path) =>
        File.Exists(path) ? path : (OperatingSystem.IsWindows() ? "NUL" : "/dev/null");

    public static DiffDocument Fallback(string[] left, string[] right, string note,
        TextFormat? leftFormat = null, TextFormat? rightFormat = null)
    {
        var model = SideBySideDiffBuilder.Instance.BuildDiffModel(
            JoinLines(left), JoinLines(right), ignoreWhitespace: false);

        var rows = new List<AlignedRow>();
        for (var i = 0; i < Math.Max(model.OldText.Lines.Count, model.NewText.Lines.Count); i++)
        {
            var o = i < model.OldText.Lines.Count ? model.OldText.Lines[i] : null;
            var n = i < model.NewText.Lines.Count ? model.NewText.Lines[i] : null;
            var l = ToLine(o);
            var r = ToLine(n);
            if (l is null && r is null)
                continue;

            var kind = (l, r) switch
            {
                (null, _) => RowKind.Added,
                (_, null) => RowKind.Removed,
                _ when o!.Type == ChangeType.Unchanged && n!.Type == ChangeType.Unchanged => RowKind.Equal,
                _ => RowKind.Changed,
            };
            rows.Add(new AlignedRow(l, r, kind));
        }
        return new DiffDocument(rows, "Text", note, leftFormat, rightFormat);
    }

    /// <summary>DiffPlex also splits on a bare CR, so it is replaced by a visible symbol.</summary>
    private static string JoinLines(string[] lines) =>
        string.Join('\n', lines.Select(l => l.Replace('\r', LineAligner.CarriageReturnSymbol)));

    private static DiffLine? ToLine(DiffPiece? piece)
    {
        if (piece is null || piece.Type == ChangeType.Imaginary || piece.Position is null)
            return null;

        var text = piece.Text ?? "";
        var ranges = new List<TextRange>();
        if (piece.Type is ChangeType.Modified or ChangeType.Inserted or ChangeType.Deleted)
        {
            var pos = 0;
            foreach (var sub in piece.SubPieces)
            {
                var len = sub.Text?.Length ?? 0;
                if (sub.Type is ChangeType.Inserted or ChangeType.Deleted or ChangeType.Modified && len > 0)
                    ranges.Add(new TextRange(pos, len));
                if (sub.Type != ChangeType.Imaginary)
                    pos += len;
            }
            if (ranges.Count == 0 && piece.Type != ChangeType.Modified && text.Length > 0)
                ranges.Add(new TextRange(0, text.Length));
        }
        return new DiffLine(piece.Position.Value - 1, text, ranges);
    }
}
