using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

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
    /// <summary>True only when the suite can actually drive an install: elevated AND IIS present.</summary>
    public static bool CanRun => IsAdministrator() && IsIisInstalled();

    public static bool IsAdministrator() => Environment.IsPrivilegedProcess;

    public static bool IsIisInstalled()
    {
        var appcmd = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "inetsrv", "appcmd.exe");
        return File.Exists(appcmd);
    }

    /// <summary>The prebuilt installer (FR-032). Located by walking up from the test bin dir.</summary>
    public static string? InstallerExe => FindUp(Path.Combine("src", "AkmlSql.Installer", "Output", "AKMLSQLSetup.exe"));

    public static string ProgramFilesApp =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AKML SQL");

    public static string UninstallerExe => Path.Combine(ProgramFilesApp, "unins000.exe");

    public static string CommonAppDataWeb =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AKML SQL Web");

    public static string InstallSummaryPath => Path.Combine(CommonAppDataWeb, "INSTALL-SUMMARY.txt");

    /// <summary>The IDE-plugin config -- must be byte-for-byte preserved across a web install/uninstall (SC-007).</summary>
    public static string PluginConfig =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AKML SQL", "config.json");

    /// <summary>Run a process to completion, returning (exitCode, stdout+stderr).</summary>
    public static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
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
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout + stderr);
    }

    /// <summary>Run a PowerShell command and return (exitCode, stdout+stderr).</summary>
    public static (int ExitCode, string Output) RunPowerShell(string command) =>
        RunProcess("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + command + "\"");

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
