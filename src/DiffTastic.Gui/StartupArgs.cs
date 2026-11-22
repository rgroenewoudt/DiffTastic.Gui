namespace DiffTastic.Gui;

/// <summary>Usage: DiffTastic.Gui left right [--path repo/relative/path]</summary>
public sealed record StartupArgs(string? Left, string? Right, string? RepoPath)
{
    public static StartupArgs Parse(IReadOnlyList<string> args)
    {
        string? repoPath = null;
        var files = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--path" && i + 1 < args.Count)
                repoPath = args[++i];
            else
                files.Add(args[i]);
        }

        return new StartupArgs(
            files.Count > 0 ? Path.GetFullPath(files[0]) : null,
            files.Count > 1 ? Path.GetFullPath(files[1]) : null,
            repoPath);
    }
}
