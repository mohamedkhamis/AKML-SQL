using System.Runtime.InteropServices;

namespace AkmlSql.UiTests.Driver;

/// <summary>
/// The environment checks that decide whether UI automation can work at all here.
///
/// <para>
/// These are worth failing fast on. Every one of them, left unchecked, produces a downstream error
/// that points somewhere else entirely: a disconnected session yields black screenshots and
/// "element not found"; the wrong DPI yields coordinates that miss their target by a consistent
/// fraction; a missing extension yields an editor with no squiggles and a test that concludes,
/// wrongly, that analysis is broken.
/// </para>
/// </summary>
public static class Preconditions
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    /// <summary>Default SSMS 22 install location.</summary>
    public const string DefaultSsmsPath =
        @"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe";

    /// <summary>Where the SSMS 22 build of the extension is deployed.</summary>
    public const string ExtensionDirectory =
        @"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql";

    /// <summary>
    /// Throws with an actionable message if the desktop cannot be driven.
    ///
    /// <para>
    /// The session check is the important one. An RDP session that has been disconnected — closing
    /// the client window rather than logging off — keeps its processes running but tears down the
    /// rendering surface, so screen captures return black and hit-testing stops behaving. Either
    /// stay connected for the run, or redirect the session to the physical console first:
    /// </para>
    /// <code>
    /// tscon.exe %SESSIONNAME% /dest:console
    /// </code>
    /// </summary>
    public static void RequireInteractiveDesktop()
    {
        if (!Environment.UserInteractive)
            throw new InvalidOperationException(
                "No interactive desktop: this process is running as a service or in session 0. " +
                "UI automation needs a real logged-on session.");

        // GetForegroundWindow returns zero when the session owns no rendering desktop, which is the
        // cheapest reliable signal that an RDP session has been disconnected.
        if (GetForegroundWindow() == IntPtr.Zero)
            throw new InvalidOperationException(
                "The session has no foreground window, which usually means a disconnected RDP session. " +
                "Screenshots would come back black. Reconnect, or run: tscon.exe %SESSIONNAME% /dest:console");
    }

    /// <summary>
    /// Verifies the extension is deployed and reports how old the build is. A stale deployment is
    /// the single most common reason a UI test contradicts a green unit suite — the tests exercise
    /// code that was never copied into the IDE.
    /// </summary>
    public static (bool Deployed, DateTime? BuiltUtc, string Message) CheckExtension()
    {
        var dll = Path.Combine(ExtensionDirectory, "AkmlSql.Ssms22.dll");
        if (!File.Exists(dll))
            return (false, null, $"AKML SQL is not deployed to SSMS 22 (looked for {dll}).");

        var built = File.GetLastWriteTimeUtc(dll);
        var age = DateTime.UtcNow - built;
        var msg = $"AKML SQL deployed, built {built:yyyy-MM-dd HH:mm}Z ({age.TotalDays:F1} days ago).";
        return (true, built, msg);
    }

    /// <summary>
    /// True when nobody has touched the keyboard or mouse for <paramref name="idleSeconds"/>.
    /// Synthetic input and a human sharing the desktop fight each other, so a long unattended run
    /// should check this rather than silently stealing focus mid-keystroke.
    /// </summary>
    public static bool DesktopIsIdle(int idleSeconds = 5)
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return true;
        var idleMs = (uint)Environment.TickCount - info.dwTime;
        return idleMs >= idleSeconds * 1000u;
    }
}
