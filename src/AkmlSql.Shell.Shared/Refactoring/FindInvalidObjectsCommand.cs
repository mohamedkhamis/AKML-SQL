#nullable enable
using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>
    /// Spec 030 T059 (FR-019) — "Find Invalid Objects". Scans the active editor's connected database
    /// for objects whose definitions reference an entity SQL Server can no longer resolve (dropped
    /// table, renamed column, missing synonym target, …) via the engine's <c>FindInvalidObjects</c>
    /// handler, and shows the results in <see cref="FindInvalidObjectsDialog"/>.
    /// </summary>
    internal sealed class FindInvalidObjectsCommand
    {
        private FindInvalidObjectsCommand(Package package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdShowFindInvalidObjects);
            var item  = new OleMenuCommand(Execute, cmdId);
            item.BeforeQueryStatus += (s, _) => { if (s is OleMenuCommand c) { c.Visible = true; c.Enabled = true; } };
            commandService.AddCommand(item);
        }

        public static FindInvalidObjectsCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
            => Instance = new FindInvalidObjectsCommand(package, commandService);

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var ctx = RefactorCommandHelper.TryGetActiveEditor();
                if (ctx == null)
                {
                    MessageBox.Show("Open a connected SQL editor, then run Find Invalid Objects.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    MessageBox.Show("The AKML SQL engine is not running yet — try again in a moment.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                FindInvalidObjectsResponse? response = null;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    response = await client.SendRequestAsync<FindInvalidObjectsResponse, FindInvalidObjectsRequest>(
                        MessageTypes.FindInvalidObjects,
                        new FindInvalidObjectsRequest { SessionId = ctx.SessionId, DatabaseName = string.Empty },
                        timeoutMs: 60_000);
                });

                if (response == null)
                {
                    MessageBox.Show("No response from the engine — the scan timed out.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Status: 0 = Ok, 1 = PermissionDenied, 2 = Error.
                if (response.Status != 0)
                {
                    MessageBox.Show(response.ErrorMessage ?? "Find Invalid Objects failed.",
                        Constants.ProductName, MessageBoxButtons.OK,
                        response.Status == 1 ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
                    return;
                }

                using var dialog = new FindInvalidObjectsDialog(response.Records, response.TotalScanned);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "FindInvalidObjectsCommand.Execute failed");
                MessageBox.Show("Find Invalid Objects failed: " + ex.Message,
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
