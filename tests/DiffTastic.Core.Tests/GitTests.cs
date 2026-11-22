using DiffTastic.Core.Git;

namespace DiffTastic.Core.Tests;

public sealed class GitTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "difftastic-git-" + Guid.NewGuid().ToString("N")[..8]);
    private GitRunner _git = null!;
    private readonly List<string> _shas = [];

    private const string V1 = "line one\nline two\nline three\n";
    private const string V2 = "line one\nline TWO\nline three\n";
    private const string V3 = "line one\nline TWO!\nline three\nline four\n";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        _git = new GitRunner(_root);
        await _git.RunAsync("init", "-q", "-b", "main");
        await _git.RunAsync("config", "user.name", "Test Author");
        await _git.RunAsync("config", "user.email", "test@example.com");
        await _git.RunAsync("config", "core.autocrlf", "false");

        await CommitAsync(V1, "First version\n\nWith a longer body.");
        await CommitAsync(V2, "Uppercase two");
        await CommitAsync(V3, "Add four");
    }

    private async Task CommitAsync(string content, string message)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "src", "file.txt"), content);
        await _git.RunAsync("add", "-A");
        await _git.RunAsync("commit", "-q", "-m", message);
        _shas.Add((await _git.RunAsync("rev-parse", "HEAD")).Trim());
    }

    public Task DisposeAsync()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Blame_working_tree_points_to_last_commit_per_line()
    {
        var blame = await new BlameService(_git).BlameAsync(new BlameTarget.WorkingTree("src/file.txt"));

        Assert.Equal(4, blame.Lines.Count);
        Assert.Equal(_shas[0], blame.Lines[0].Sha);
        Assert.Equal(_shas[2], blame.Lines[1].Sha);
        Assert.Equal(_shas[0], blame.Lines[2].Sha);
        Assert.Equal(_shas[2], blame.Lines[3].Sha);
        Assert.Equal("Test Author", blame.Commits[_shas[0]].Author);
        Assert.Equal("First version", blame.Commits[_shas[0]].Summary);
    }

    [Fact]
    public async Task Locator_finds_commit_of_temp_file()
    {
        var locator = new RevisionLocator(_git, _root);
        var temp = Path.Combine(Path.GetTempPath(), $"difftool-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(temp, V2);
            var target = await locator.LocateAsync(temp, "src/file.txt");
            var commit = Assert.IsType<BlameTarget.Commit>(target);
            Assert.Equal(_shas[1], commit.Sha);

            await File.WriteAllTextAsync(temp, V3);
            Assert.Equal("HEAD", Assert.IsType<BlameTarget.Commit>(await locator.LocateAsync(temp, "src/file.txt")).Sha);

            await File.WriteAllTextAsync(temp, "never committed\n");
            Assert.IsType<BlameTarget.Contents>(await locator.LocateAsync(temp, "src/file.txt"));

            var working = Path.Combine(_root, "src", "file.txt");
            Assert.IsType<BlameTarget.WorkingTree>(await locator.LocateAsync(working, "src/file.txt"));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task Blame_old_commit_and_contents()
    {
        var service = new BlameService(_git);

        var old = await service.BlameAsync(new BlameTarget.Commit(_shas[1], "src/file.txt"));
        Assert.Equal(_shas[1], old.Lines[1].Sha);

        var temp = Path.Combine(Path.GetTempPath(), $"difftool-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(temp, V3 + "uncommitted\n");
        try
        {
            var contents = await service.BlameAsync(new BlameTarget.Contents(temp, "src/file.txt"));
            Assert.True(contents.Commits[contents.Lines[4].Sha].IsUncommitted);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task Line_history_lists_older_commits()
    {
        var history = await new BlameService(_git).LineHistoryAsync(_shas[2], "src/file.txt", 2);

        Assert.Equal([_shas[2], _shas[1], _shas[0]], history.Select(c => c.Sha));
        Assert.Equal("First version\n\nWith a longer body.", history[2].Message);
    }

    [Fact]
    public async Task Full_message_is_loaded_on_demand()
    {
        var service = new BlameService(_git);
        var blame = await service.BlameAsync(new BlameTarget.WorkingTree("src/file.txt"));

        var full = await service.GetCommitAsync(blame.Commits[_shas[0]]);

        Assert.Equal("First version\n\nWith a longer body.", full.Message);
    }

    [Fact]
    public async Task Finds_repo_root()
    {
        var root = await GitRunner.FindRepoRootAsync(Path.Combine(_root, "src"));

        Assert.Equal(Path.GetFullPath(_root).TrimEnd('\\'), root?.Replace('/', '\\').TrimEnd('\\'), ignoreCase: true);
    }
}
