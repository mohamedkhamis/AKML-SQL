using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace AkmlSql.Engine;

/// <summary>
/// Spec 021 (web edition) -- M0 task T022. <c>Program</c> is now a thin CLI front-end:
/// parses args, hooks Ctrl+C, and hands off to <see cref="EngineHost.RunAsync"/>. All
/// startup logic (logger init, parent-process monitoring, pending imports, RPC server
/// run) lives on <see cref="EngineHost"/> so other consumers (tests, future web-mode
/// launcher) can call <c>RunAsync</c> directly.
/// </summary>
[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        string? pipeName = null;
        int parentPid = 0;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--pipe" && i + 1 < args.Length)
            {
                pipeName = args[++i];
            }
            else if (args[i] == "--parent-pid" && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out parentPid);
            }
        }

        if (string.IsNullOrEmpty(pipeName))
        {
            await Console.Error.WriteLineAsync("Usage: AkmlSql.Engine --pipe <name> --parent-pid <pid>");
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
}
