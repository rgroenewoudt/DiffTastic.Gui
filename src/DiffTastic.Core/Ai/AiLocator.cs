namespace DiffTastic.Core.Ai;

public static class AiLocator
{
    public const string Claude = "claude";
    public const string Codex = "codex";

    /// <summary>
    /// The preferred tool when it is installed, otherwise the first one found (Claude, then Codex).
    /// Models fall back to the cheapest default per tool.
    /// </summary>
    public static AiProvider? Locate(string? preferred = null, string? claudeModel = null, string? codexModel = null)
    {
        var order = preferred?.ToLowerInvariant() == Codex ? new[] { Codex, Claude } : [Claude, Codex];
        foreach (var name in order)
        {
            if (FindOnPath(name) is not { } path)
                continue;
            return name == Claude
                ? new ClaudeProvider(path, claudeModel ?? ClaudeProvider.DefaultModel)
                : new CodexProvider(path, codexModel ?? CodexProvider.DefaultModel);
        }
        return null;
    }

    /// <summary>Full path of a command on PATH. On Windows an .exe anywhere on PATH wins over an npm .cmd shim.</summary>
    public static string? FindOnPath(string command, string? pathVariable = null)
    {
        string[] extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat"] : [""];
        var dirs = (pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var extension in extensions)
        {
            foreach (var dir in dirs)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim().Trim('"'), command + extension);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                }
            }
        }
        return null;
    }
}
