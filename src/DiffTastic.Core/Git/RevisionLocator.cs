namespace DiffTastic.Core.Git;

/// <summary>Finds which version of a repo file a (possibly temporary) file holds.</summary>
public sealed class RevisionLocator(GitRunner git, string repoRoot)
{
    public async Task<BlameTarget> LocateAsync(string filePath, string repoPath, CancellationToken ct = default)
    {
        var workingFile = Path.GetFullPath(Path.Combine(repoRoot, repoPath));
        if (string.Equals(Path.GetFullPath(filePath), workingFile, StringComparison.OrdinalIgnoreCase))
            return new BlameTarget.WorkingTree(repoPath);

        var gitPath = repoPath.Replace('\\', '/');
        var blob = (await git.RunAsync(["hash-object", $"--path={gitPath}", "--", filePath], ct)).Trim();

        if (await BlobAtAsync("HEAD", repoPath, ct) == blob)
            return new BlameTarget.Commit("HEAD", repoPath);

        var candidates = await git.RunAsync(["log", "--all", "--format=%H", $"--find-object={blob}", "--", repoPath], ct);
        foreach (var sha in candidates.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (await BlobAtAsync(sha, repoPath, ct) == blob)
                return new BlameTarget.Commit(sha, repoPath);
            if (await BlobAtAsync(sha + "^", repoPath, ct) == blob)
                return new BlameTarget.Commit((await git.RunAsync(["rev-parse", sha + "^"], ct)).Trim(), repoPath);
        }

        return new BlameTarget.Contents(filePath, repoPath);
    }

    private async Task<string?> BlobAtAsync(string rev, string path, CancellationToken ct)
    {
        try
        {
            return (await git.RunAsync(["rev-parse", "--verify", "--quiet", $"{rev}:{path.Replace('\\', '/')}"], ct)).Trim();
        }
        catch (GitException)
        {
            return null;
        }
    }
}
