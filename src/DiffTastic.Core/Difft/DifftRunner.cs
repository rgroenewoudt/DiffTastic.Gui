using System.Diagnostics;

namespace DiffTastic.Core.Difft;

public sealed class DifftRunner(string executablePath)
{
    private static readonly string[] Names = ["difft.exe", "difft"];

    public string ExecutablePath { get; } = executablePath;

    /// <summary>Looks next to the app first, then on PATH.</summary>
    public static DifftRunner? Locate()
    {
        foreach (var name in Names)
        {
            var local = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(local))
                return new DifftRunner(local);
        }

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in pathDirs)
        {
            foreach (var name in Names)
            {
                var candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate))
                    return new DifftRunner(candidate);
            }
        }

        return null;
    }

    public async Task<DifftResult> RunAsync(string leftPath, string rightPath, TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(ExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--display");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("--color");
        psi.ArgumentList.Add("never");
        psi.ArgumentList.Add(leftPath);
        psi.ArgumentList.Add(rightPath);
        psi.Environment["DFT_UNSTABLE"] = "yes";

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start difft.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"difft did not finish within {timeout.TotalSeconds:0}s.");
        }

        var output = (await stdout).Trim();
        if (!output.StartsWith('{'))
            throw new InvalidOperationException($"difft failed: {(await stderr).Trim()}");

        return DifftResult.Parse(output);
    }
}
