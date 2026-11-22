using System.Diagnostics;
using System.Text;

namespace DiffTastic.Core.Git;

public sealed class GitException(string message) : Exception(message);

public sealed class GitRunner(string workingDirectory)
{
    public string WorkingDirectory { get; } = workingDirectory;

    public async Task<string> RunAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=off");
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new GitException("Could not start git.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new GitException((await stderr).Trim());
        return await stdout;
    }

    public Task<string> RunAsync(params string[] args) => RunAsync(args, CancellationToken.None);

    /// <summary>Returns the work tree root containing <paramref name="directory"/>, or null.</summary>
    public static async Task<string?> FindRepoRootAsync(string directory)
    {
        if (!Directory.Exists(directory))
            return null;
        try
        {
            var root = (await new GitRunner(directory).RunAsync("rev-parse", "--show-toplevel")).Trim();
            return root.Length == 0 ? null : Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is GitException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
