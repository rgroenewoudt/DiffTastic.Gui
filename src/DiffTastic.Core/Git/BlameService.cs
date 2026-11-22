using System.Buffers;
using System.Collections.Concurrent;

namespace DiffTastic.Core.Git;

public sealed class BlameService(GitRunner git)
{
    private static readonly SearchValues<char> LowerHex = SearchValues.Create("0123456789abcdef");

    private readonly ConcurrentDictionary<string, CommitInfo> _messages = new();

    public async Task<BlameResult> BlameAsync(BlameTarget target, CancellationToken ct = default)
    {
        List<string> args = ["blame", "--porcelain"];
        switch (target)
        {
            case BlameTarget.Commit c:
                args.Add(c.Sha);
                break;
            case BlameTarget.Contents c:
                args.AddRange(["--contents", c.FilePath, "HEAD"]);
                break;
        }
        args.AddRange(["--", target.RepoPath]);

        return ParsePorcelain(await git.RunAsync(args, ct));
    }

    /// <summary>Commit with its full message (blame only gives the summary).</summary>
    public async Task<CommitInfo> GetCommitAsync(CommitInfo commit, CancellationToken ct = default)
    {
        if (commit.IsUncommitted)
            return commit;
        if (_messages.TryGetValue(commit.Sha, out var cached))
            return cached;

        var message = (await git.RunAsync(["show", "-s", "--format=%B", commit.Sha], ct)).TrimEnd();
        var full = commit with { Message = message };
        _messages[commit.Sha] = full;
        return full;
    }

    /// <summary>Patch of a commit for one path, or the whole commit when that path was not touched (e.g. after a rename).</summary>
    public async Task<string> GetCommitDiffAsync(string sha, string path, CancellationToken ct = default)
    {
        string[] show = ["show", "--format=", "--no-color", "--no-ext-diff", "--diff-merges=first-parent", sha];
        var diff = await git.RunAsync([.. show, "--", path], ct);
        if (string.IsNullOrWhiteSpace(diff))
            diff = await git.RunAsync(show, ct);
        return diff;
    }

    /// <summary>Commits that touched this line, newest first, starting at <paramref name="sha"/>.</summary>
    public async Task<IReadOnlyList<CommitInfo>> LineHistoryAsync(string sha, string path, int line, CancellationToken ct = default)
    {
        var output = await git.RunAsync(
            ["log", $"-L{line},{line}:{path}", "--no-patch", "--format=%H%x1f%an%x1f%at%x1f%B%x1e", sha], ct);
        return ParseLog(output);
    }

    /// <summary>Parses records of <c>%H%x1f%an%x1f%at%x1f%B%x1e</c>; patch text between records is skipped.</summary>
    public static IReadOnlyList<CommitInfo> ParseLog(string output)
    {
        var commits = new List<CommitInfo>();
        foreach (var record in output.Split('\x1e'))
        {
            var fields = record.Split('\x1f');
            if (fields.Length < 4)
                continue;
            var sha = fields[0].Split('\n')[^1].Trim();
            if (!IsSha(sha) || !long.TryParse(fields[2], out var seconds))
                continue;
            var time = DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
            commits.Add(new CommitInfo(sha, fields[1], time, string.Join('\x1f', fields[3..]).TrimEnd()));
        }
        return commits;
    }

    private static bool IsSha(string value) =>
        value.Length is 40 or 64 && !value.AsSpan().ContainsAnyExcept(LowerHex);

    public static BlameResult ParsePorcelain(string output)
    {
        var byFinalLine = new SortedDictionary<int, BlameLine>();
        var commits = new Dictionary<string, CommitInfo>();
        var headers = new Dictionary<string, Dictionary<string, string>>();
        var fileNames = new Dictionary<string, string>();

        string? sha = null;
        var origLine = 0;
        var finalLine = 0;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('\t'))
            {
                if (sha is null)
                    continue;
                byFinalLine[finalLine] = new BlameLine(sha, origLine, fileNames.GetValueOrDefault(sha, ""));
                sha = null;
                continue;
            }

            if (sha is null)
            {
                var parts = line.Split(' ');
                if (parts.Length >= 3 && IsSha(parts[0]))
                {
                    sha = parts[0];
                    origLine = int.Parse(parts[1]);
                    finalLine = int.Parse(parts[2]);
                    if (!headers.ContainsKey(sha))
                        headers[sha] = new Dictionary<string, string>();
                }
                continue;
            }

            var space = line.IndexOf(' ');
            var key = space < 0 ? line : line[..space];
            var value = space < 0 ? "" : line[(space + 1)..];
            if (key == "filename")
                fileNames[sha] = value;
            else
                headers[sha][key] = value;
        }

        foreach (var (commitSha, h) in headers)
        {
            var time = long.TryParse(h.GetValueOrDefault("author-time"), out var t)
                ? DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime()
                : DateTimeOffset.Now;
            commits[commitSha] = new CommitInfo(commitSha, h.GetValueOrDefault("author", ""), time, h.GetValueOrDefault("summary", ""));
        }

        var lines = new BlameLine[byFinalLine.Count == 0 ? 0 : byFinalLine.Keys.Max()];
        foreach (var (final, blameLine) in byFinalLine)
            lines[final - 1] = blameLine;
        return new BlameResult(lines, commits);
    }
}
