using System.Diagnostics;
using AkmlSql.Core.Logging;
using Serilog;

namespace AkmlSql.Engine;

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
            Console.Error.WriteLine("Usage: AkmlSql.Engine --pipe <name> --parent-pid <pid>");
            return 1;
        }

        LoggerFactory.Initialize();
        Log.Information("AkmlSql.Engine starting. Pipe={Pipe}, ParentPid={ParentPid}", pipeName, parentPid);

        var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Orphan protection: monitor parent process
        if (parentPid > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var parent = Process.GetProcessById(parentPid);
                    while (!parent.HasExited && !token.IsCancellationRequested)
                    {
                        await Task.Delay(2000, token);
                    }
                }
                catch (ArgumentException)
                {
                    // Parent process not found — already exited
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error monitoring parent process {Pid}", parentPid);
                }

                Log.Warning("Parent process {Pid} exited. Engine shutting down.", parentPid);
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            });
        }

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        };

        try
        {
            var server = new Server.PipeRpcServer(pipeName);
            await server.RunAsync(token);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Engine shutdown requested.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Engine crashed.");
            return 2;
        }
        finally
        {
            cts.Dispose();
            LoggerFactory.Shutdown();
        }

        return 0;
    }
}
