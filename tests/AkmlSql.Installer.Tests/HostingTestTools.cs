using System.Diagnostics;
using Xunit;

namespace AkmlSql.Installer.Tests;

internal static class HostingTestTools
{
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AKML-SQL.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    internal static string Script(string name) => Path.Combine(Root, "src", "AkmlSql.Installer", name);

    internal static (int ExitCode, string Output) Run(string file, params string[] arguments)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        var processState = Path.Combine(Path.GetTempPath(), "akml-hosting-process-" + Guid.NewGuid().ToString("N"));
        info.Environment["ProgramData"] = processState;
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException($"{Path.GetFileName(file)} did not finish within 60 seconds.");
            }
            return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        }
        finally
        {
            if (Directory.Exists(processState)) Directory.Delete(processState, recursive: true);
        }
    }

    internal static string PowerShell(string body)
    {
        var result = Run(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "$ErrorActionPreference='Stop'; " + body);
        Assert.True(result.ExitCode == 0, result.Output);
        return result.Output.Trim();
    }

    internal static string DotSource(string name, string body)
    {
        var path = Script(name);
        // Never dot-source the old side-effecting scripts during the red test run.
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("#Requires", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$MyInvocation.InvocationName -ne '.'", source, StringComparison.Ordinal);
        var state = Path.Combine(Path.GetTempPath(), "akml-hosting-state-" + Guid.NewGuid().ToString("N"));
        try
        {
            return PowerShell($"$env:ProgramData='{state}'; function netsh {{ throw 'netsh must not run while dot-sourcing' }}; " +
                $". '{path.Replace("'", "''")}'; {body}");
        }
        finally
        {
            if (Directory.Exists(state)) Directory.Delete(state, recursive: true);
        }
    }
}
