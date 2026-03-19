using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// T078: Manual schema cache refresh command.
    /// Sends a SchemaRefreshRequest to the engine process via the named pipe RPC client.
    /// </summary>
    internal sealed class RefreshCacheCommand
    {
        private readonly Package _package;

        private RefreshCacheCommand(Package package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdRefreshCache);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static RefreshCacheCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new RefreshCacheCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Fire and forget — send refresh request to the engine
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var manager = Ipc.EngineLifecycle.Manager;
                    if (manager == null || manager.Client == null || !manager.Client.IsConnected)
                    {
                        Log.Warning("RefreshCacheCommand: engine is not connected.");
                        return;
                    }

                    var request = new RefreshRequest { SessionId = string.Empty };
                    await manager.Client.SendNotificationAsync<RefreshRequest>(MessageTypes.SchemaRefreshRequest, request);
                    Log.Information("RefreshCacheCommand: schema refresh request sent to engine.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "RefreshCacheCommand: failed to send refresh request.");
                }
            });
        }
    }
}
