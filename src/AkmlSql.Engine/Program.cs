using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace AkmlSql.Engine;

/// <summary>
/// Spec 021 (web edition) -- M0 task T022. <c>Program</c> is a thin CLI front-end: parses
/// args, picks a hosting mode, and hands off to <see cref="EngineHost"/>. Two modes:
/// <list type="bullet">
/// <item><b>IDE-plugin (default):</b> <c>--pipe &lt;name&gt; --parent-pid &lt;pid&gt;</c> ->
/// <see cref="EngineHost.RunAsync"/> over a named pipe, tied to the host process lifetime.</item>
/// <item><b>Web-edition service (spec 026 M4 closure, C2):</b> <c>--web --config &lt;path&gt;</c> ->
/// hosted via the generic host + <c>AddWindowsService</c> so the SCM sees SERVICE_RUNNING (a bare
/// console process started by <c>sc.exe</c> would otherwise time out with error 1053). Runs ONLY
/// the WebSocket bridge via <see cref="EngineHost.RunWebAsync"/>.</item>
/// </list>
/// All startup logic lives on <see cref="EngineHost"/> so tests and both launchers reuse it.
/// </summary>
[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        string? pipeName = null;
        int parentPid = 0;
        string? configPath = null;
        var webMode = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe" when i + 1 < args.Length:
                    pipeName = args[++i];
                    break;
                case "--parent-pid" when i + 1 < args.Length:
                    int.TryParse(args[++i], out parentPid);
                    break;
                case "--config" when i + 1 < args.Length:
                    configPath = args[++i];
                    break;
                case "--web":
                    webMode = true;
                    break;
            }
        }

        // Spec 026 (M4 closure) C2: web/service mode. The AkmlSqlWebEngine Windows service launches
        // the engine with `--web --config <path>` and NO `--pipe`. We also infer web mode when a
        // --config is supplied without a --pipe, so a stray invocation still does the sane thing.
        if (webMode || (!string.IsNullOrEmpty(configPath) && string.IsNullOrEmpty(pipeName)))
        {
            return await RunWebServiceAsync(configPath).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(pipeName))
        {
            await Console.Error.WriteLineAsync(
                "Usage:\n" +
                "  AkmlSql.Engine --pipe <name> --parent-pid <pid>   (IDE-plugin mode)\n" +
                "  AkmlSql.Engine --web --config <path>              (web-edition service mode)");
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        };

        return await EngineHost.RunAsync(pipeName!, parentPid, cts.Token);
    }

    /// <summary>
    /// Spec 026 (M4 closure) C2. Hosts <see cref="EngineHost.RunWebAsync"/> as the
    /// <c>AkmlSqlWebEngine</c> Windows service. <c>AddWindowsService</c> integrates with the SCM
    /// when started by it; when run interactively (e.g. a developer testing <c>--web</c> from a
    /// console) it falls back to console lifetime so the same binary works both ways.
    /// </summary>
    private static async Task<int> RunWebServiceAsync(string? configPath)
    {
        var service = new WebEngineBackgroundService(configPath);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddWindowsService(options => options.ServiceName = "AkmlSqlWebEngine");
        builder.Services.AddHostedService(_ => service);

        using var host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);

        // Console mode only (a developer running `--web` by hand): the service path never gets here
        // on failure, see WebEngineBackgroundService. Returning the real code rather than a fixed 0
        // keeps a scripted launch honest about whether the bridge came up.
        return service.FailureExitCode;
    }

    /// <summary>
    /// Bridges the generic-host lifetime to <see cref="EngineHost.RunWebAsync"/>.
    /// </summary>
    private sealed class WebEngineBackgroundService : BackgroundService
    {
        private readonly string? _configPath;

        public WebEngineBackgroundService(string? configPath) => _configPath = configPath;

        /// <summary>Non-zero when the web host failed; read after the host stops in console mode.</summary>
        public int FailureExitCode { get; private set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var exit = await EngineHost.RunWebAsync(_configPath, stoppingToken).ConfigureAwait(false);
            if (exit == 0 || stoppingToken.IsCancellationRequested)
            {
                return;
            }

            FailureExitCode = exit;

            if (WindowsServiceHelpers.IsWindowsService())
            {
                // Terminate the PROCESS with the failure code. This used to throw, and throwing is
                // exactly what kept the service stopped.
                //
                // An exception out of a BackgroundService makes the generic host stop CLEANLY
                // (BackgroundServiceExceptionBehavior.StopHost), and a clean stop is reported to
                // the service control manager as exit code 0 -- indistinguishable from someone
                // pressing Stop. Windows service recovery actions only act on failures, so they
                // never fired, and the service sat Stopped until a person noticed and started it
                // by hand. On the machine this was diagnosed on, the one stop Windows ever
                // restarted in thirty days was a genuine crash; every failure reported as a clean
                // stop was left alone.
                //
                // Microsoft's own guidance for BackgroundService-based Windows services is this
                // call, for this reason: the process must end with a non-zero exit code for the
                // SCM to apply its configured recovery options. The installer configures those
                // (restart, with failure actions enabled for non-crash failures too).
                //
                // RunWebAsync has already logged the cause and flushed the logger in its finally,
                // so nothing is lost by not unwinding further.
                Environment.Exit(exit);
            }

            // Console mode: stop the host normally; RunWebServiceAsync returns FailureExitCode.
            throw new InvalidOperationException(
                $"AkmlSqlWebEngine web host exited with code {exit} (see the engine log for details).");
        }
    }
}
