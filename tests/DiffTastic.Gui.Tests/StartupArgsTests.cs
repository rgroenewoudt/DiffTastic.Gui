namespace DiffTastic.Gui.Tests;

public class StartupArgsTests
{
    [Fact]
    public void Reads_files_and_repo_path()
    {
        var args = StartupArgs.Parse(["a.cs", "b.cs", "--path", "src/x.cs"]);

        Assert.Equal(Path.GetFullPath("a.cs"), args.Left);
        Assert.Equal(Path.GetFullPath("b.cs"), args.Right);
        Assert.Equal("src/x.cs", args.RepoPath);
    }

    [Fact]
    public void Trailing_path_flag_is_not_a_file()
    {
        var args = StartupArgs.Parse(["a.cs", "--path"]);

        Assert.Equal(Path.GetFullPath("a.cs"), args.Left);
        Assert.Null(args.Right);
        Assert.Null(args.RepoPath);
    }
}
