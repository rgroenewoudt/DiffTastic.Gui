using System.Collections.Concurrent;
using System.Text.Json;
using DiffTastic.Core.Git;

namespace DiffTastic.Core.Ai;

/// <summary>Asks the AI what a commit did to one line; answers are cached per commit and line.</summary>
public sealed class CommitExplainer(AiProvider provider, string language = "English")
{
    public const int DiffBudget = 30_000;

    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": { "explanation": { "type": "string" } },
          "required": ["explanation"]
        }
        """;

    private readonly ConcurrentDictionary<(string Sha, string Line), string> _cache = new();

    public AiProvider Provider => provider;

    public bool TryGetCached(string sha, string lineText, out string explanation) =>
        _cache.TryGetValue((sha, lineText), out explanation!);

    public async Task<string> ExplainAsync(CommitInfo commit, string commitDiff, string lineText, CancellationToken ct = default)
    {
        if (TryGetCached(commit.Sha, lineText, out var cached))
            return cached;

        var json = await provider.CompleteJsonAsync(BuildPrompt(commit, commitDiff, lineText, language), Schema, ct);
        using var doc = JsonDocument.Parse(json);
        var explanation = doc.RootElement.TryGetProperty("explanation", out var e) ? e.GetString()?.Trim() ?? "" : "";
        if (explanation.Length == 0)
            throw new InvalidOperationException("The AI gave no explanation.");
        _cache[(commit.Sha, lineText)] = explanation;
        return explanation;
    }

    public static string BuildPrompt(CommitInfo commit, string commitDiff, string lineText, string language = "English")
    {
        var diff = commitDiff.Length <= DiffBudget
            ? commitDiff
            : commitDiff[..DiffBudget] + $"\n… ({commitDiff.Length - DiffBudget} more characters)";
        return $"""
            Explain in 2 to 4 sentences what this commit changed and why, focused on the line below.
            Use the commit message and the diff. Say so when the reason is not clear from them.
            Write the explanation in {language}.

            Line: {lineText.Trim()}

            Commit {commit.ShortSha} by {commit.Author}:
            {commit.Message}

            Diff:
            {diff}
            """;
    }
}
