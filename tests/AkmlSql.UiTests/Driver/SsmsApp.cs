using System.Diagnostics;
using FlaUI.Core;
using FlaUiApplication = FlaUI.Core.Application;   // UseWindowsForms also brings System.Windows.Forms.Application into scope
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace AkmlSql.UiTests.Driver;

/// <summary>
/// Launches or attaches to SSMS 22 — the <c>Browser</c> of this harness.
///
/// <para>
/// UIA3 rather than UIA2 (<c>System.Windows.Automation</c>): the newer provider handles WPF
/// virtualised trees and out-of-process patterns markedly better, which matters in an IDE where
/// almost everything is virtualised.
/// </para>
/// </summary>
public sealed class SsmsApp : IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly FlaUiApplication _app;
    private readonly bool _ownsProcess;

    private SsmsApp(FlaUiApplication app, UIA3Automation automation, bool ownsProcess)
    {
        _app = app;
        _automation = automation;
        _ownsProcess = ownsProcess;
    }

    /// <summary>
    /// Starts a dedicated SSMS instance with <paramref name="sqlFile"/> already open and connected.
    ///
    /// <para>
    /// Opening a file from the command line rather than driving File → New → Query is a deliberate
    /// robustness choice: it skips the connection dialog and the New Query keyboard dance, which are
    /// the two least deterministic moments in SSMS startup. The document simply exists, connected,
    /// by the time the main window appears.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para><b>SSMS 22's command line is not SSMS 19's.</b> Verified against 22.9.12120.119, the
    /// accepted switches are:</para>
    /// <code>
    /// SSMS.exe [file_name[,file_name]*] [switches]
    ///   -S &lt;server&gt;    instance to connect to
    ///   -d &lt;database&gt;  database to connect to
    ///   -U &lt;user&gt;      SQL login
    ///   -A &lt;method&gt;    auth method (ActiveDirectoryDefault, SqlPassword, ...)
    ///   -C             trust the server certificate without validation
    ///   -N &lt;option&gt;    encryption: Optional | Mandatory | Strict   (DEFAULT: Mandatory)
    ///   -i &lt;hostname&gt;  expected CN/SAN during certificate validation
    ///   -dn &lt;name&gt;     connection display name
    ///   -nosplash, -log &lt;file&gt;
    /// </code>
    /// <para>
    /// Two removals bite. <c>-E</c> (Windows authentication) and <c>-P</c> are gone — passing
    /// <c>-E</c> does not degrade gracefully, it puts up a modal usage dialog and the shell never
    /// finishes loading, so the failure surfaces much later as "the editor never appeared". Windows
    /// authentication is now simply the default when no <c>-U</c>/<c>-A</c> is given.
    /// </para>
    /// <para>
    /// And <c>-N</c> now defaults to <c>Mandatory</c>, so SSMS 22 demands an encrypted connection.
    /// A local instance with a self-signed certificate is refused unless the caller opts out, which
    /// is why <c>-C</c> is passed by default here.
    /// </para>
    /// </remarks>
    /// <param name="sqlFile">Absolute path to a .sql file to open.</param>
    /// <param name="server">Server to connect to. Defaults to the local default instance.</param>
    /// <param name="ssmsPath">Override the SSMS executable location.</param>
    /// <param name="trustServerCertificate">
    /// Pass <c>-C</c>. On by default because the usual automation target is a local development
    /// instance with a self-signed certificate. Set false against a properly certificated server.
    /// </param>
    public static SsmsApp Launch(
        string sqlFile,
        string server = "(local)",
        string? ssmsPath = null,
        bool trustServerCertificate = true)
    {
        Preconditions.RequireInteractiveDesktop();

        var exe = ssmsPath ?? Preconditions.DefaultSsmsPath;
        if (!File.Exists(exe))
            throw new FileNotFoundException($"SSMS 22 not found at {exe}.", exe);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(sqlFile);
        psi.ArgumentList.Add("-S");
        psi.ArgumentList.Add(server);
        if (trustServerCertificate) psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add("-nosplash");

        var automation = new UIA3Automation();
        var app = FlaUiApplication.Launch(psi);
        return new SsmsApp(app, automation, ownsProcess: true);
    }

    /// <summary>
    /// Attaches to an SSMS instance that is already running. Handy for exploring interactively, but
    /// note that a shared instance carries whatever state the last run left behind — a suite that
    /// must be reproducible should launch its own.
    /// </summary>
    public static SsmsApp AttachToRunning()
    {
        Preconditions.RequireInteractiveDesktop();

        var proc = Process.GetProcessesByName("Ssms").FirstOrDefault()
            ?? throw new InvalidOperationException("No running Ssms.exe to attach to.");

        return new SsmsApp(FlaUiApplication.Attach(proc), new UIA3Automation(), ownsProcess: false);
    }

    /// <summary>
    /// The main window, once the shell is up.
    ///
    /// <para>
    /// SSMS reports a main window early, then spends a long time loading packages — including this
    /// extension, which starts its out-of-process engine. Waiting for the window is necessary but
    /// nowhere near sufficient; callers should follow up by waiting on something that only exists
    /// once the thing under test is ready, such as the editor or the AKML SQL menu.
    /// </para>
    /// </summary>
    public SsmsWindow MainWindow(int timeoutSeconds = 180)
    {
        var window = Retry.WhileNull(
            () =>
            {
                try
                {
                    var w = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(5));
                    return w is not null && !string.IsNullOrEmpty(w.Title) ? w : null;
                }
                catch (Exception) { return null; }
            },
            timeout: TimeSpan.FromSeconds(timeoutSeconds),
            interval: TimeSpan.FromMilliseconds(500),
            throwOnTimeout: false,
            ignoreException: true).Result
            ?? throw new TimeoutException($"SSMS main window did not appear within {timeoutSeconds}s.");

        ThrowIfStartupDialog(window);
        return new SsmsWindow(window, _automation);
    }

    /// <summary>
    /// Turns a modal startup dialog into an immediate, quotable error.
    ///
    /// <para>
    /// When SSMS rejects its command line it puts up a plain Win32 dialog (window class
    /// <c>#32770</c>) that <em>is</em> the process's main window. Everything downstream then looks
    /// like a hang: the shell never loads, so the editor never appears, and the run dies two minutes
    /// later complaining about the editor — nowhere near the actual mistake. Reading the dialog's
    /// own text and failing on it turns that into one line naming the bad switch.
    /// </para>
    /// </summary>
    private static void ThrowIfStartupDialog(Window window)
    {
        var className = window.Properties.ClassName.ValueOrDefault;
        if (!string.Equals(className, "#32770", StringComparison.Ordinal)) return;

        var text = string.Join(
            " ",
            window.FindAllDescendants()
                  .Select(e => e.Properties.Name.ValueOrDefault)
                  .Where(n => !string.IsNullOrWhiteSpace(n)));

        throw new InvalidOperationException(
            "SSMS stopped on a startup dialog instead of loading the shell. It said:\n\n" +
            text.Trim());
    }

    /// <summary>The SSMS process id, for logging and for the engine's per-shell pipe name.</summary>
    public int ProcessId => _app.ProcessId;

    public void Dispose()
    {
        try
        {
            // Only close what we started. Killing a developer's own SSMS — with unsaved query
            // windows in it — would be an unpleasant surprise.
            if (_ownsProcess && !_app.HasExited)
            {
                _app.Close();
                if (!_app.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(5)))
                {
                    // A modal "save changes?" prompt blocks a clean close; the scratch file is
                    // disposable, so take the process down rather than hang the run.
                    _app.Kill();
                }
            }
        }
        catch (Exception) { /* teardown must never fail a run */ }
        finally
        {
            _automation.Dispose();
            _app.Dispose();
        }
    }
}
