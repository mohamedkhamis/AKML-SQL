using System.Diagnostics;
using Xunit;

namespace AkmlSql.Installer.Tests;

/// <summary>
/// Setup must close SSMS before it replaces <c>Engine\*.dll</c>. SSMS 22 runs as <c>SSMS.exe</c>, and the close loop in
/// <c>PrepareToInstall</c> asked for it by another spelling; <c>IsProcessRunning</c> (environment-scanner.iss) matched with
/// Pascal's case-sensitive <c>Pos</c>, so it answered "not running", setup never killed SSMS, and the SSMS left running
/// restarted the engine that locks the files ("DeleteFile failed; code 5. Access is denied.").
/// <para>These compile a probe setup around the REAL environment-scanner.iss and run it against a process this test starts
/// under an upper-case name (a copy of <c>ping.exe</c>). The probe only answers <c>IsProcessRunning</c> into a file and
/// aborts in <c>InitializeSetup</c>: nothing is installed or closed. Skipped when Inno Setup 7 is not installed.</para>
/// </summary>
public sealed class ProcessDetectionTests
{
    private const string Iscc = @"C:\Program Files\Inno Setup 7\ISCC.exe";
    private const string ImageName = "AKMLPROBECASE.exe";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static (int ExitCode, string Output) Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    /// <summary>Answers IsProcessRunning for each name, in order, as "name=True|False" lines.</summary>
    private static Dictionary<string, bool> Probe(string work, params string[] names)
    {
        var scanner = Path.Combine(FindRepoRoot(), "src", "AkmlSql.Installer", "environment-scanner.iss");
        var result = Path.Combine(work, "probe-result.txt");
        var checks = string.Concat(names.Select((n, i) => $"  R[{i}] := '{n}=' + B(IsProcessRunning('{n}'));\r\n"));
        File.WriteAllText(Path.Combine(work, "probe.iss"), $$"""
            [Setup]
            AppName=AkmlProcessProbe
            AppVersion=1
            DefaultDirName={tmp}\akmlprobe
            OutputDir=.
            OutputBaseFilename=probe
            Uninstallable=no
            PrivilegesRequired=lowest
            CreateAppDir=no

            #include "{{scanner}}"

            function B(V: Boolean): String;
            begin
              if V then Result := 'True' else Result := 'False';
            end;

            function InitializeSetup(): Boolean;
            var
              R: TArrayOfString;
            begin
              SetArrayLength(R, {{names.Length}});
            {{checks}}  SaveStringsToFile('{{result}}', R, False);
              Result := False;
            end;
            """);

        var compile = Run(Iscc, "/Q", Path.Combine(work, "probe.iss"));
        Assert.True(compile.ExitCode == 0, "probe did not compile: " + compile.Output);
        var run = Run(Path.Combine(work, "probe.exe"), "/VERYSILENT", "/SUPPRESSMSGBOXES");
        Assert.True(File.Exists(result), $"probe wrote no result (exit {run.ExitCode}): {run.Output}");
        return File.ReadAllLines(result).Select(l => l.Split('=')).ToDictionary(p => p[0], p => p[1] == "True");
    }

    [SkippableFact]
    public void A_running_process_is_found_whatever_case_the_caller_spells_it_in()
    {
        Skip.IfNot(File.Exists(Iscc), "Inno Setup 7 is not installed.");
        var work = Path.Combine(Path.GetTempPath(), "akml-procprobe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var exe = Path.Combine(work, ImageName);
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe);
        using var running = Process.Start(new ProcessStartInfo(exe, "-n 120 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        try
        {
            var found = Probe(work, "AKMLPROBECASE.exe", "AkmlProbeCase.exe", "akmlprobecase.exe", "AKMLPROBE", "NoSuchAkmlProcess.exe");

            Assert.True(found["AKMLPROBECASE.exe"]);
            Assert.True(found["AkmlProbeCase.exe"]);    // how setup asked for SSMS ("Ssms.exe") while it ran as "SSMS.exe"
            Assert.True(found["akmlprobecase.exe"]);
            Assert.False(found["AKMLPROBE"]);           // the whole image name, not a prefix of it
            Assert.False(found["NoSuchAkmlProcess.exe"]);
        }
        finally
        {
            try { running.Kill(); running.WaitForExit(5000); } catch (InvalidOperationException) { }
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The close step decides from what is running at install time, not from the scan page, and closes SSMS even
    /// when its target is unticked: Engine\*.dll is replaced on every run.</summary>
    [Fact]
    public void Setup_checks_for_SSMS_when_it_installs_whatever_targets_are_ticked()
    {
        var setup = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AkmlSql.Installer", "AkmlSqlSetup.iss"));
        var prepare = setup[setup.IndexOf("function PrepareToInstall(", StringComparison.Ordinal)..];
        prepare = prepare[..prepare.IndexOf("\nend;", StringComparison.Ordinal)];

        Assert.Contains("CheckRunningIDEs;", prepare, StringComparison.Ordinal);
        Assert.Contains("if IsProcessRunning('SSMS.exe') then", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSelected", prepare, StringComparison.Ordinal);
        // Restart Manager filters the files it checks; a process name there matched no installed file.
        Assert.Contains("CloseApplicationsFilter=*.exe,*.dll", setup, StringComparison.Ordinal);
    }
}
