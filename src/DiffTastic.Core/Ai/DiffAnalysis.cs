using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiffTastic.Core.Diff;

namespace DiffTastic.Core.Ai;

/// <summary>AI notes for one hunk (index into <see cref="DiffDocument.Blocks"/>). Importance is 0 (trivial) to 3 (critical).</summary>
public sealed record HunkNote(int Hunk, int Importance, bool IsNoise, string Reason, IReadOnlyList<string> ReviewHints)
{
    public bool IsImportant => Importance >= 2 && !IsNoise;
}

public sealed record DiffAnalysis(string Summary, IReadOnlyList<HunkNote> Hunks)
{
    public HunkNote? NoteFor(int hunk) => Hunks.FirstOrDefault(h => h.Hunk == hunk);

    public bool IsNoise(int hunk) => NoteFor(hunk)?.IsNoise == true;

    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "summary": { "type": "string" },
            "hunks": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "hunk": { "type": "integer" },
                  "importance": { "type": "integer" },
                  "noise": { "type": "boolean" },
                  "reason": { "type": "string" },
                  "reviewHints": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["hunk", "importance", "noise", "reason", "reviewHints"]
              }
            }
          },
          "required": ["summary", "hunks"]
        }
        """;

    public const int PromptBudget = 60_000;
    private const int ContextRows = 3;
    private const int OmittedNoteLength = 40;

    private const string Instructions = """
        Analyze this diff of one file. Each hunk starts with "### Hunk <id>". Lines start with "-" (old version),
        "+" (new version) or " " (unchanged context).

        Return:
        - summary: 1 to 3 sentences about what the change does as a whole.
        - hunks: one entry per hunk id, with
          - importance: 0 trivial, 1 minor, 2 notable, 3 critical change in behavior.
          - noise: true when the hunk does not change behavior, for example only formatting, whitespace,
            comments, renames, reordering or import changes.
          - reason: one short sentence that explains the importance.
          - reviewHints: concrete risks or possible bugs a reviewer should check, at most 3. Empty when there are none.
            Do not invent problems.
        """;

    public static string BuildPrompt(DiffDocument doc, string fileName, string language = "English", int budget = PromptBudget)
    {
        var maxLines = 200;
        while (true)
        {
            var prompt = Render(doc, fileName, language, maxLines);
            if (prompt.Length <= budget)
                return prompt;
            if (maxLines <= 4)
                return Render(doc, fileName, language, maxLines, budget);
            maxLines /= 2;
        }
    }

    /// <summary>Renders hunks until <paramref name="budget"/> is reached; the rest is left out.</summary>
    private static string Render(DiffDocument doc, string fileName, string language, int maxLinesPerSide, int budget = int.MaxValue)
    {
        var sb = new StringBuilder(Instructions);
        sb.Append(CultureInfo.InvariantCulture, $"Write summary, reason and reviewHints in {language}.\n");
        sb.Append(CultureInfo.InvariantCulture, $"\nFile: {fileName} ({doc.Language}), {doc.Blocks.Count} hunks.\n");
        for (var i = 0; i < doc.Blocks.Count; i++)
        {
            var block = doc.Blocks[i];
            var hunkStart = sb.Length;
            sb.Append(CultureInfo.InvariantCulture, $"\n### Hunk {i}\n");
            AppendHunk(sb, doc, block, maxLinesPerSide);

            if (sb.Length > budget - OmittedNoteLength)
            {
                sb.Length = hunkStart;
                sb.Append(CultureInfo.InvariantCulture, $"\n… {doc.Blocks.Count - i} more hunks not shown.\n");
                break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Old lines ("-"), new lines ("+") and a few unchanged lines (" ") around them.</summary>
    internal static void AppendHunk(StringBuilder sb, DiffDocument doc, DiffBlock block, int maxLinesPerSide)
    {
        var first = block.StartRow;
        while (first > 0 && block.StartRow - first < ContextRows && doc.Rows[first - 1].Kind == RowKind.Equal)
            first--;
        for (var r = first; r < block.StartRow; r++)
            AppendLine(sb, ' ', doc.Rows[r].Right?.Text);

        var rows = Enumerable.Range(block.StartRow, block.EndRow - block.StartRow + 1).Select(r => doc.Rows[r]).ToList();
        AppendSide(sb, '-', rows.Select(r => r.Left?.Text), maxLinesPerSide);
        AppendSide(sb, '+', rows.Select(r => r.Right?.Text), maxLinesPerSide);

        for (var r = block.EndRow + 1; r < doc.Rows.Count && r <= block.EndRow + ContextRows && doc.Rows[r].Kind == RowKind.Equal; r++)
            AppendLine(sb, ' ', doc.Rows[r].Right?.Text);
    }

    private static void AppendSide(StringBuilder sb, char prefix, IEnumerable<string?> texts, int maxLines)
    {
        var lines = texts.Where(t => t is not null).ToList();
        foreach (var line in lines.Take(maxLines))
            AppendLine(sb, prefix, line);
        if (lines.Count > maxLines)
            sb.Append(CultureInfo.InvariantCulture, $"{prefix}… ({lines.Count - maxLines} more lines)\n");
    }

    public const int MaxLineLength = 500;

    internal static void AppendLine(StringBuilder sb, char prefix, string? text)
    {
        if (text is null)
            return;
        sb.Append(prefix);
        if (text.Length <= MaxLineLength)
            sb.Append(text);
        else
            sb.Append(text, 0, MaxLineLength).Append(CultureInfo.InvariantCulture, $"… ({text.Length - MaxLineLength} more characters)");
        sb.Append('\n');
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Parses the AI answer; drops unknown or duplicate hunk ids.</summary>
    public static DiffAnalysis Parse(string json, int hunkCount)
    {
        var raw = JsonSerializer.Deserialize<RawAnalysis>(json, JsonOptions)
                  ?? throw new JsonException("Empty AI answer.");
        var notes = (raw.Hunks ?? [])
            .Where(h => h.Hunk >= 0 && h.Hunk < hunkCount)
            .DistinctBy(h => h.Hunk)
            .OrderBy(h => h.Hunk)
            .Select(h => new HunkNote(
                h.Hunk,
                Math.Clamp(h.Importance, 0, 3),
                h.Noise,
                h.Reason?.Trim() ?? "",
                (h.ReviewHints ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList()))
            .ToList();
        return new DiffAnalysis(raw.Summary?.Trim() ?? "", notes);
    }

    private sealed record RawAnalysis(
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("hunks")] List<RawHunk>? Hunks);

    private sealed record RawHunk(
        [property: JsonPropertyName("hunk")] int Hunk,
        [property: JsonPropertyName("importance")] int Importance,
        [property: JsonPropertyName("noise")] bool Noise,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("reviewHints")] List<string>? ReviewHints);
}
