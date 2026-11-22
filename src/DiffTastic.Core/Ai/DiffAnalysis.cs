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

    public static string BuildPrompt(DiffDocument doc, string fileName, int budget = PromptBudget)
    {
        var maxLines = 200;
        while (true)
        {
            var prompt = Render(doc, fileName, maxLines);
            if (prompt.Length <= budget || maxLines <= 4)
                return prompt;
            maxLines /= 2;
        }
    }

    private static string Render(DiffDocument doc, string fileName, int maxLinesPerSide)
    {
        var sb = new StringBuilder(Instructions);
        sb.Append($"\nFile: {fileName} ({doc.Language}), {doc.Blocks.Count} hunks.\n");
        for (var i = 0; i < doc.Blocks.Count; i++)
        {
            var block = doc.Blocks[i];
            sb.Append($"\n### Hunk {i}\n");
            for (var r = Math.Max(0, block.StartRow - ContextRows); r < block.StartRow; r++)
                AppendLine(sb, ' ', doc.Rows[r].Right?.Text);

            var rows = Enumerable.Range(block.StartRow, block.EndRow - block.StartRow + 1).Select(r => doc.Rows[r]).ToList();
            AppendSide(sb, '-', rows.Select(r => r.Left?.Text), maxLinesPerSide);
            AppendSide(sb, '+', rows.Select(r => r.Right?.Text), maxLinesPerSide);

            for (var r = block.EndRow + 1; r <= Math.Min(doc.Rows.Count - 1, block.EndRow + ContextRows); r++)
                AppendLine(sb, ' ', doc.Rows[r].Right?.Text);
        }
        return sb.ToString();
    }

    private static void AppendSide(StringBuilder sb, char prefix, IEnumerable<string?> texts, int maxLines)
    {
        var lines = texts.Where(t => t is not null).ToList();
        foreach (var line in lines.Take(maxLines))
            AppendLine(sb, prefix, line);
        if (lines.Count > maxLines)
            sb.Append($"{prefix}… ({lines.Count - maxLines} more lines)\n");
    }

    private static void AppendLine(StringBuilder sb, char prefix, string? text)
    {
        if (text is not null)
            sb.Append(prefix).Append(text).Append('\n');
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
