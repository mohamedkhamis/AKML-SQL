using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace AkmlSql.Installer.Tests;

/// <summary>
/// Spec 026 (M4 closure) US5. Shared, dependency-free helpers for the installer smoke suite:
/// the host-capability gate (admin + IIS), well-known paths, a process runner, free-port
/// selection, and file hashing. No Microsoft.Win32.Registry / WindowsIdentity references --
/// <see cref="Environment.IsPrivilegedProcess"/> (BCL since .NET 8) covers the admin check, and
/// IIS presence is the existence of <c>%SystemRoot%\System32\inetsrv\appcmd.exe</c>.
/// </summary>
internal static class InstallerSmokeEnv
{
    /// <summary>Real installs are allowed only on explicitly opted-in disposable VMs.</summary>
    public static bool IsOptedIn => Environment.GetEnvironmentVariable("AKML_INSTALLER_SMOKE") == "1";

    public static bool CanRunService => IsOptedIn && IsAdministrator();
    public static bool CanRunIis => CanRunService && IsIisReady();
    public static bool CanRun => CanRunIis;

    public static string SkipReason => IsOptedIn
        ? "Requires administrator rights (and IIS Ready for IIS cases) on a disposable VM"
        : "installer smoke tests run the real installer; set AKML_INSTALLER_SMOKE=1 on a disposable VM";

    public static bool IsAdministrator() => Environment.IsPrivilegedProcess;

    public static bool IsIisInstalled()
    {
        var appcmd = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "inetsrv", "appcmd.exe");
        return File.Exists(appcmd);
    }

    public static bool IsIisReady()
    {
        if (!IsIisInstalled()) return false;
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var inet = machine.OpenSubKey(@"SOFTWARE\Microsoft\InetStp");
        using var parts = machine.OpenSubKey(@"SOFTWARE\Microsoft\InetStp\Components");
        if (Convert.ToInt32(inet?.GetValue("MajorVersion", 0)) < 7) return false;
        foreach (var name in new[] { "W3SVC", "StaticContent", "DefaultDocument", "HttpErrors" })
            if (Convert.ToInt32(parts?.GetValue(name, 0)) != 1) return false;
        foreach (var name in new[] { "W3SVC", "WAS" })
        {
            using var service = machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
            if (Convert.ToInt32(service?.GetValue("Start", 4)) == 4) return false;
        }
        return RunProcess(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoProfile -NonInteractive -Command \"Import-Module WebAdministration -ErrorAction Stop; $null=Get-ChildItem IIS:\\Sites -ErrorAction Stop\"", 10).ExitCode == 0;
    }

    /// <summary>The prebuilt installer (FR-032). Located by walking up from the test bin dir.</summary>
    public static string? InstallerExe => FindUp(Path.Combine("src", "AkmlSql.Installer", "Output", "AKMLSQLSetup.exe"));

    public static string ProgramFilesApp =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AKML SQL");

    public static string UninstallerExe
    {
        get
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = machine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{F7E8A9B0-C1D2-E3F4-A5B6-C7D8E9F0A1B2}_is1");
                if (key?.GetValue("UninstallString") is not string value) continue;
                var end = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (end >= 0) return value[..(end + 4)].Trim().Trim('"');
            }
            return Path.Combine(ProgramFilesApp, "unins000.exe");
        }
    }

    public static string CommonAppDataWeb =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AKML SQL Web");

    public static string InstallSummaryPath => Path.Combine(CommonAppDataWeb, "INSTALL-SUMMARY.txt");

    /// <summary>The IDE-plugin config -- must be byte-for-byte preserved across a web install/uninstall (SC-007).</summary>
    public static string PluginConfig =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AKML SQL", "config.json");

    /// <summary>Run a process to completion, returning (exitCode, stdout+stderr).</summary>
    public static (int ExitCode, string Output) RunProcess(string fileName, string arguments, int timeoutSeconds = 600)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p == null) return (-1, string.Empty);
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutSeconds * 1000))
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit();
            return (-2, "Process timed out: " + stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }
        return (p.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }

    /// <summary>Run a PowerShell command and return (exitCode, stdout+stderr).</summary>
    public static (int ExitCode, string Output) RunPowerShell(string command) =>
        RunProcess("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + command + "\"");

    /// <summary>
    /// Spec 026 (M4 closure) M5: poll the named Windows service until it reports Running, up to
    /// <paramref name="timeoutSeconds"/>. Installer exit 0 alone does NOT mean the engine service
    /// came up (the install treats a non-running service as a non-fatal warning), so the smoke suite
    /// must verify the runtime outcome separately.
    /// </summary>
    public static bool WaitForServiceRunning(string serviceName, int timeoutSeconds = 20)
    {
        for (var i = 0; i < timeoutSeconds; i++)
        {
            var (exit, _) = RunPowerShell(
                "if ((Get-Service " + serviceName + " -ErrorAction SilentlyContinue).Status -eq 'Running') " +
                "{ exit 0 } else { exit 1 }");
            if (exit == 0) return true;
            System.Threading.Thread.Sleep(1000);
        }
        return false;
    }

    /// <summary>
    /// Spec 026 (M4 closure) M5: best-effort teardown of the web-edition service + IIS site, so a
    /// partially-provisioned machine (install that returned 0 but left a broken service) is not
    /// leaked to the next test run. Safe to call when nothing was installed.
    /// </summary>
    public static void BestEffortCleanup(int bridgePort = 0)
    {
        if (!CanRunService) return;
        RunProcess("sc.exe", "stop AkmlSqlWebEngine");
        RunProcess("sc.exe", "delete AkmlSqlWebEngine");
        foreach (var name in new[] { "AKML SQL Web Engine", "AKML SQL Web Site (HTTPS)" })
            RunProcess("netsh.exe", "advfirewall firewall delete rule name=\"" + name + "\"");
        RunPowerShell("Import-Module WebAdministration -ErrorAction SilentlyContinue; Remove-WebSite -Name AkmlSqlWeb -ErrorAction SilentlyContinue; if (!(Get-WebApplication -ErrorAction SilentlyContinue | Where-Object applicationPool -eq 'AkmlSqlWeb')) { Remove-WebAppPool -Name AkmlSqlWeb -ErrorAction SilentlyContinue }");
        if (bridgePort > 0) RunProcess("netsh.exe", "http delete sslcert ipport=0.0.0.0:" + bridgePort);
        RunPowerShell("$bindings=netsh http show sslcert | Out-String; if ($LASTEXITCODE -eq 0) { Get-ChildItem Cert:\\LocalMachine\\My | Where-Object Subject -eq 'CN=AKML SQL Web Engine' | ForEach-Object { if ($bindings -notmatch [regex]::Escape($_.Thumbprint)) { Remove-Item -LiteralPath $_.PSPath -Force } } }; Remove-Item 'HKLM:\\Software\\AKML SQL\\Web','HKLM:\\Software\\WOW6432Node\\AKML SQL\\Web' -Recurse -Force -ErrorAction SilentlyContinue");
    }

    /// <summary>Pick a free TCP port, optionally avoiding <paramref name="exclude"/>.</summary>
    public static int GetFreePort(int exclude = 0)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (port != exclude) return port;
        }
        throw new InvalidOperationException("Could not find a free TCP port.");
    }

    /// <summary>SHA-256 hex of a file, or null when the file is absent.</summary>
    public static string? HashFileOrNull(string path)
    {
        if (!File.Exists(path)) return null;
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static string ReadSummaryOrEmpty() =>
        File.Exists(InstallSummaryPath) ? File.ReadAllText(InstallSummaryPath) : string.Empty;

    /// <summary>Walk up from the test bin directory until <paramref name="relative"/> exists; null if not found.</summary>
    private static string? FindUp(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
