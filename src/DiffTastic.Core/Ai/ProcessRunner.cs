using System.Diagnostics;
using System.Text;

namespace DiffTastic.Core.Ai;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

/// <summary>Runs a process with optional stdin, a timeout and UTF-8 output.</summary>
public static class ProcessRunner
{
    private static readonly char[] CmdSpecial = ['&', '|', '<', '>', '^', '%', '!', '\n', '\r'];

    public static async Task<ProcessResult> RunAsync(
        string executable, IReadOnlyList<string> args, string? stdin, string workingDirectory, TimeSpan timeout,
        CancellationToken ct = default)
    {
        var psi = CreateStartInfo(executable, args);
        psi.WorkingDirectory = workingDirectory;
        psi.RedirectStandardInput = stdin is not null;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {executable}.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            if (stdin is not null)
                await WriteStdinAsync(process, stdin, cts.Token);
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"{Path.GetFileNameWithoutExtension(executable)} did not finish within {timeout.TotalSeconds:0}s.");
        }

        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    /// <summary>A process that exits early closes its stdin; its own output then explains why.</summary>
    private static async Task WriteStdinAsync(Process process, string stdin, CancellationToken ct)
    {
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(new UTF8Encoding(false).GetBytes(stdin), ct);
        }
        catch (IOException)
        {
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Batch files (npm shims) need cmd.exe; arguments must then be free of cmd special characters.</summary>
    internal static ProcessStartInfo CreateStartInfo(string executable, IReadOnlyList<string> args)
    {
        var extension = Path.GetExtension(executable);
        if (!extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var psi = new ProcessStartInfo(executable);
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            return psi;
        }

        if (args.Any(a => a.IndexOfAny(CmdSpecial) >= 0))
            throw new InvalidOperationException($"Argument contains characters that cmd.exe would interpret: {executable}");

        var line = string.Join(' ', new[] { executable }.Concat(args).Select(Quote));
        return new ProcessStartInfo("cmd.exe", $"/d /s /c \"{line}\"");
    }

    /// <summary>Quotes one argument the way the MSVC runtime parses it.</summary>
    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
            return arg;

        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }
}
