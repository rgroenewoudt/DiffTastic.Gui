namespace DiffTastic.Core.Git;

public sealed record CommitInfo(string Sha, string Author, DateTimeOffset AuthorTime, string Message)
{
    public bool IsUncommitted => Sha.Length > 0 && Sha.All(c => c == '0');
    public string ShortSha => Sha[..Math.Min(8, Sha.Length)];
    public string Summary => Message.Split('\n', 2)[0].Trim();
}

/// <summary>Where a line of the final file comes from. OriginalLine is 1-based.</summary>
public sealed record BlameLine(string Sha, int OriginalLine, string OriginalPath);

/// <summary>Blame for one file version. Lines are indexed by 0-based line number.</summary>
public sealed record BlameResult(IReadOnlyList<BlameLine> Lines, IReadOnlyDictionary<string, CommitInfo> Commits);

/// <summary>Which file version to blame.</summary>
public abstract record BlameTarget(string RepoPath)
{
    public sealed record WorkingTree(string RepoPath) : BlameTarget(RepoPath);
    public sealed record Commit(string Sha, string RepoPath) : BlameTarget(RepoPath);

    /// <summary>Unknown version: blame the given content on top of HEAD.</summary>
    public sealed record Contents(string FilePath, string RepoPath) : BlameTarget(RepoPath);
}
