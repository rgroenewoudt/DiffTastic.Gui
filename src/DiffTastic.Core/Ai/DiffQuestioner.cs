using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DiffTastic.Core.Diff;

namespace DiffTastic.Core.Ai;

/// <summary>Answers a free question about part of a diff; answers are cached per prompt.</summary>
public sealed class DiffQuestioner(AiProvider provider, string language = "English")
{
    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": { "answer": { "type": "string" } },
          "required": ["answer"]
        }
        """;

    public const int MaxContextLines = 400;

    private readonly ConcurrentDictionary<string, string> _cache = new();

    public AiProvider Provider => provider;

    public async Task<string> AskAsync(string fileName, string context, string question, CancellationToken ct = default)
    {
        var prompt = BuildPrompt(fileName, context, question, language);
        var key = DiffAnalyzer.Hash($"{provider.Name}\n{provider.Model}\n{prompt}");
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var json = await provider.CompleteJsonAsync(prompt, Schema, ct);
        using var doc = JsonDocument.Parse(json);
        var answer = doc.RootElement.TryGetProperty("answer", out var a) ? a.GetString()?.Trim() ?? "" : "";
        if (answer.Length == 0)
            throw new InvalidOperationException("The AI gave no answer.");
        _cache[key] = answer;
        return answer;
    }

    public static string BuildPrompt(string fileName, string context, string question, string language = "English") => $"""
        Answer the question about this part of a diff of the file {fileName}.
        Lines start with "-" (old version), "+" (new version) or " " (unchanged).
        Be short and concrete. Use plain text, no Markdown. Say so when the code shown is not enough to answer.
        Write the answer in {language}.

        {context.TrimEnd()}

        Question: {question.Trim()}
        """.ReplaceLineEndings("\n");

    /// <summary>One difference block with a few unchanged lines around it.</summary>
    public static string BlockContext(DiffDocument doc, int block)
    {
        var sb = new StringBuilder();
        DiffAnalysis.AppendHunk(sb, doc, doc.Blocks[block], MaxContextLines);
        return sb.ToString();
    }

    /// <summary>Selected lines with their diff prefix; <paramref name="selectedText"/> is added when only part of a line is selected.</summary>
    public static string SelectionContext(IEnumerable<(char Prefix, string Text)> lines, string? selectedText = null)
    {
        var sb = new StringBuilder();
        var count = 0;
        foreach (var (prefix, text) in lines)
        {
            if (++count > MaxContextLines)
            {
                sb.Append("… (more lines not shown)\n");
                break;
            }
            DiffAnalysis.AppendLine(sb, prefix, text);
        }
        if (!string.IsNullOrWhiteSpace(selectedText))
            sb.Append("\nSelected text: ").Append(selectedText.Trim()).Append('\n');
        return sb.ToString();
    }
}
