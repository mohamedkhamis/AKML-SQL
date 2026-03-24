using System.Diagnostics;

namespace AkmlSql.E2E.Tests.Infrastructure;

/// <summary>Result of running a CLI process.</summary>
public sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>Combined stdout + stderr for assertions that don't care which stream.</summary>
    public string AllOutput => Stdout + Stderr;
}

/// <summary>Spawns CLI processes and captures output safely without deadlocking.</summary>
public static class CliRunner
{
    /// <summary>
    /// Runs <paramref name="exe"/> with <paramref name="args"/>, optionally writing
    /// <paramref name="stdinText"/> to stdin. Throws <see cref="TimeoutException"/> if
    /// the process does not exit within <paramref name="timeoutMs"/>.
    /// </summary>
    public static async Task<CliResult> RunAsync(
        string       exe,
        string[]     args,
        string?      stdinText  = null,
        string?      workingDir = null,
        int          timeoutMs  = 30_000)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            RedirectStandardInput  = stdinText is not null,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WorkingDirectory       = workingDir ?? Path.GetTempPath()
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: {exe}");

        // Start reading BEFORE writing stdin — prevents pipe-buffer deadlock.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        // Write stdin on a background task so it doesn't block if the process
        // is not draining its stdin pipe.
        if (stdinText is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await proc.StandardInput.WriteAsync(stdinText);
                    proc.StandardInput.Close();
                }
                catch { /* process may have exited before we finished writing */ }
            });
        }

        // Wait for the process to exit (timeout guard).
        bool exited = await Task.Run(() => proc.WaitForExit(timeoutMs));
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(
                $"'{Path.GetFileName(exe)}' did not exit within {timeoutMs} ms");
        }

        // Drain any remaining bytes from the streams.
        await Task.WhenAll(stdoutTask, stderrTask);

        return new CliResult(proc.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
