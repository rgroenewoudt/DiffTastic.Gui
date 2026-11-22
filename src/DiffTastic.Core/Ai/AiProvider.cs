using System.Text.Json;

namespace DiffTastic.Core.Ai;

/// <summary>An AI command line tool that answers a prompt with JSON matching a schema.</summary>
public abstract class AiProvider
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    public abstract string Name { get; }
    public abstract string Model { get; }

    /// <summary>Company that receives the prompt, shown to the user.</summary>
    public abstract string Vendor { get; }

    public abstract Task<string> CompleteJsonAsync(string prompt, string schema, CancellationToken ct = default);

    /// <summary>Empty folder to run in, so the tool does not pick up project files or instructions.</summary>
    protected static string CreateWorkDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "difftastic-ai", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    protected static void DeleteQuietly(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    protected static string LastErrorLine(string stderr, string stdout)
    {
        var lines = (stderr + "\n" + stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.LastOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)) ?? lines.LastOrDefault() ?? "no output";
    }
}

public sealed class ClaudeProvider(string executable, string model) : AiProvider
{
    public const string DefaultModel = "haiku";

    private const string SystemPrompt =
        "You review code diffs for a desktop diff viewer. Answer only with the requested JSON, in English.";

    public override string Name => "Claude";
    public override string Model => model;
    public override string Vendor => "Anthropic";

    public override async Task<string> CompleteJsonAsync(string prompt, string schema, CancellationToken ct = default)
    {
        string[] args =
        [
            "-p", "--model", model, "--output-format", "json", "--system-prompt", SystemPrompt,
            "--json-schema", MinifyJson(schema), "--tools", "", "--no-session-persistence", "--setting-sources", "",
        ];
        var dir = CreateWorkDirectory();
        try
        {
            var result = await ProcessRunner.RunAsync(executable, args, prompt, dir, DefaultTimeout, ct);
            if (!result.Stdout.TrimStart().StartsWith('{'))
                throw new InvalidOperationException($"claude failed: {LastErrorLine(result.Stderr, result.Stdout)}");
            return ParseEnvelope(result.Stdout);
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    /// <summary>Takes the structured answer out of <c>claude -p --output-format json</c> output.</summary>
    public static string ParseEnvelope(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        var result = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        if (root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException($"claude failed: {result ?? "unknown error"}");
        if (root.TryGetProperty("structured_output", out var structured) && structured.ValueKind == JsonValueKind.Object)
            return structured.GetRawText();
        return result ?? throw new InvalidOperationException("claude returned no result.");
    }

    internal static string MinifyJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement);
    }
}

public sealed class CodexProvider(string executable, string model) : AiProvider
{
    public const string DefaultModel = "gpt-5.4-mini";

    public override string Name => "Codex";
    public override string Model => model;
    public override string Vendor => "OpenAI";

    public override async Task<string> CompleteJsonAsync(string prompt, string schema, CancellationToken ct = default)
    {
        var dir = CreateWorkDirectory();
        try
        {
            var schemaFile = Path.Combine(dir, "schema.json");
            var outputFile = Path.Combine(dir, "answer.json");
            await File.WriteAllTextAsync(schemaFile, schema, ct);
            string[] args =
            [
                "exec", "-", "-m", model, "-c", "model_reasoning_effort=low", "--sandbox", "read-only",
                "--skip-git-repo-check", "--ephemeral", "--color", "never",
                "--output-schema", schemaFile, "-o", outputFile,
            ];
            var result = await ProcessRunner.RunAsync(executable, args, prompt, dir, DefaultTimeout, ct);
            var answer = File.Exists(outputFile) ? (await File.ReadAllTextAsync(outputFile, ct)).Trim() : "";
            if (result.ExitCode != 0 || answer.Length == 0)
                throw new InvalidOperationException($"codex failed: {LastErrorLine(result.Stderr, result.Stdout)}");
            return answer;
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }
}
