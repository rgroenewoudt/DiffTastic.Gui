namespace DiffTastic.Core.Diff;

/// <summary>A foldable { } block in row space. StartRow is the header line, EndRow holds the closing brace.</summary>
public readonly record struct FoldRegion(int StartRow, int EndRow, bool HasChanges, bool IsDeclaration);

/// <summary>Finds brace blocks with a simple scanner that skips strings and comments.</summary>
public static class FoldRegionFinder
{
    private static readonly HashSet<string> ControlKeywords =
    [
        "if", "else", "for", "foreach", "while", "do", "switch", "try", "catch", "finally",
        "using", "lock", "case", "default", "return", "checked", "unchecked", "unsafe", "fixed",
        "new", "throw", "yield", "await", "delegate",
    ];

    public static IReadOnlyList<FoldRegion> Find(IReadOnlyList<AlignedRow> rows)
    {
        var found = new Dictionary<(int Start, int End), bool>();
        foreach (var side in new[] { Side.Left, Side.Right })
        {
            foreach (var (start, end, isDeclaration) in Scan(rows, side))
                found[(start, end)] = found.GetValueOrDefault((start, end)) || isDeclaration;
        }

        return found
            .Select(f => new FoldRegion(f.Key.Start, f.Key.End, HasChanges(rows, f.Key.Start, f.Key.End), f.Value))
            .OrderBy(r => r.StartRow)
            .ThenByDescending(r => r.EndRow)
            .ToList();
    }

    /// <summary>Unchanged declarations, outermost only.</summary>
    public static IReadOnlyList<FoldRegion> DefaultFolded(IReadOnlyList<FoldRegion> regions)
    {
        var folded = new List<FoldRegion>();
        var foldedEnd = -1;
        foreach (var region in regions.OrderBy(r => r.StartRow).ThenByDescending(r => r.EndRow))
        {
            if (region.StartRow <= foldedEnd || region.HasChanges || !region.IsDeclaration)
                continue;
            folded.Add(region);
            foldedEnd = region.EndRow;
        }
        return folded;
    }

    private enum Side { Left, Right }

    private enum State { Code, BlockComment, VerbatimString, RawString, TemplateString }

    private static List<(int Start, int End, bool IsDeclaration)> Scan(IReadOnlyList<AlignedRow> rows, Side side)
    {
        var result = new List<(int, int, bool)>();
        var open = new Stack<(int HeaderRow, bool IsDeclaration)>();
        var state = State.Code;
        var rawQuotes = 0;
        var previousRow = -1;

        for (var row = 0; row < rows.Count; row++)
        {
            var text = (side == Side.Left ? rows[row].Left : rows[row].Right)?.Text;
            if (text is null)
                continue;

            var hasCodeBefore = false;
            var hasBrace = false;
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';
                switch (state)
                {
                    case State.BlockComment:
                        if (c == '*' && next == '/')
                        {
                            state = State.Code;
                            i++;
                        }
                        break;
                    case State.VerbatimString:
                        if (c == '"' && next == '"')
                            i++;
                        else if (c == '"')
                            state = State.Code;
                        break;
                    case State.RawString:
                        if (c == '"')
                        {
                            var run = QuoteRun(text, i);
                            if (run >= rawQuotes)
                                state = State.Code;
                            i += run - 1;
                        }
                        break;
                    case State.TemplateString:
                        if (c == '\\')
                            i++;
                        else if (c == '`')
                            state = State.Code;
                        break;
                    default:
                        if (c == '/' && next == '/' || c == '#' && (next == '\0' || char.IsWhiteSpace(next)))
                        {
                            i = text.Length;
                            continue;
                        }
                        if (c == '/' && next == '*')
                        {
                            state = State.BlockComment;
                            i++;
                        }
                        else if (c == '@' && next == '"' || c == '$' && next == '@' || c == '@' && next == '$')
                        {
                            state = State.VerbatimString;
                            i = Math.Max(i, text.IndexOf('"', i));
                            hasCodeBefore = true;
                        }
                        else if (c == '"' && QuoteRun(text, i) >= 3)
                        {
                            rawQuotes = QuoteRun(text, i);
                            state = State.RawString;
                            i += rawQuotes - 1;
                            hasCodeBefore = true;
                        }
                        else if (c == '"')
                        {
                            i = SkipQuoted(text, i);
                            hasCodeBefore = true;
                        }
                        else if (c == '\'')
                        {
                            var close = SkipQuoted(text, i);
                            if (close < text.Length)
                                i = close;
                            hasCodeBefore = true;
                        }
                        else if (c == '`')
                        {
                            state = State.TemplateString;
                            hasCodeBefore = true;
                        }
                        else if (c == '{')
                        {
                            hasBrace = true;
                            var header = hasCodeBefore ? row : previousRow;
                            if (header >= 0)
                                open.Push((header, IsDeclaration(HeaderText(rows, side, header, hasCodeBefore ? i : -1))));
                            else
                                open.Push((row, false));
                        }
                        else if (c == '}')
                        {
                            hasBrace = true;
                            if (open.Count > 0)
                            {
                                var (header, isDeclaration) = open.Pop();
                                if (row > header + 1)
                                    result.Add((header, row, isDeclaration));
                            }
                        }
                        else if (!char.IsWhiteSpace(c))
                            hasCodeBefore = true;
                        break;
                }
                i++;
            }

            if (hasCodeBefore || hasBrace)
                previousRow = row;
        }
        return result;
    }

    private static int QuoteRun(string text, int start)
    {
        var end = start;
        while (end < text.Length && text[end] == '"')
            end++;
        return end - start;
    }

    private static int SkipQuoted(string text, int start)
    {
        var quote = text[start];
        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
                i++;
            else if (text[i] == quote)
                return i;
        }
        return text.Length;
    }

    private static string HeaderText(IReadOnlyList<AlignedRow> rows, Side side, int row, int braceIndex)
    {
        var text = (side == Side.Left ? rows[row].Left : rows[row].Right)?.Text ?? "";
        return braceIndex >= 0 ? text[..braceIndex] : text;
    }

    private static bool IsDeclaration(string header)
    {
        var comment = header.IndexOf("//", StringComparison.Ordinal);
        var text = (comment >= 0 ? header[..comment] : header).Trim().TrimStart('}').Trim();
        if (text.Length == 0 || text.EndsWith(';'))
            return false;

        var firstWord = new string(text.TakeWhile(char.IsLetter).ToArray());
        if (ControlKeywords.Contains(firstWord))
            return false;
        if (text.Contains("=>") || text.EndsWith('(') || text.EndsWith(',') || text.EndsWith('['))
            return false;

        var depth = 0;
        var parens = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            parens += c == '(' ? 1 : c == ')' ? -1 : 0;
            if (c is '(' or '[' or '<')
                depth++;
            else if (c is ')' or ']' or '>' && depth > 0 && text[i - 1] != '=')
                depth--;
            else if (c == '=' && depth == 0)
            {
                var prev = i > 0 ? text[i - 1] : '\0';
                var next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (next != '=' && prev is not ('=' or '!' or '<' or '>'))
                    return false;
            }
        }
        return parens == 0;
    }

    private static bool HasChanges(IReadOnlyList<AlignedRow> rows, int start, int end)
    {
        for (var i = start; i <= end; i++)
        {
            if (rows[i].Kind != RowKind.Equal)
                return true;
        }
        return false;
    }
}
