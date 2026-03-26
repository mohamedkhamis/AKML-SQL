#nullable enable
using System;
using System.ComponentModel.Design;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Productivity
{
    /// <summary>
    /// T103: Shell command for CRUD procedure generation.
    /// Sends a CrudGenerationRequest to the engine and opens the generated SQL in a new editor tab.
    ///
    /// NOTE: Object Explorer context menu integration requires SSMS COM interop that varies
    /// across SSMS 20/21/22. For now this command is available via the AKML SQL menu and
    /// Command Palette. Future work: hook into SSMS Object Explorer right-click via
    /// IObjectExplorerEventProvider.
    /// </summary>
    internal sealed class CrudGenerationCommand
    {
        private readonly Package _package;

        private CrudGenerationCommand(Package package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdCrudGeneration);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static CrudGenerationCommand? Instance { get; private set; }

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new CrudGenerationCommand(package, commandService);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new CrudGenerationCommand(package, commandService);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand cmd)
            {
                cmd.Visible = true;
                cmd.Enabled = true;
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ExecuteAsync();
            });
        }

        private async Task ExecuteAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // TODO: Show a dialog to collect schema name, table name, and operation options.
                // For now, we prompt with a simple input dialog or use the word at caret.

                var dte = (EnvDTE.DTE?)Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte?.ActiveDocument == null)
                {
                    Log.Warning("CrudGenerationCommand: no active document");
                    return;
                }

                // Get the word at the caret position as a table name hint
                var textDoc = dte.ActiveDocument.Object("TextDocument") as EnvDTE.TextDocument;
                var tableName = textDoc?.Selection?.Text?.Trim();

                if (string.IsNullOrWhiteSpace(tableName))
                {
                    // Try to get the word at cursor
                    var editPoint = textDoc?.Selection?.ActivePoint?.CreateEditPoint();
                    if (editPoint != null)
                    {
                        var wordStart = editPoint.GetText(-50);
                        // Simple word extraction — take last identifier
                        var parts = wordStart.Split(new[] { ' ', '\t', '\r', '\n', '(', ')', '[', ']' },
                            StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                            tableName = parts[parts.Length - 1];
                    }
                }

                if (string.IsNullOrWhiteSpace(tableName))
                {
                    System.Windows.Forms.MessageBox.Show(
                        "Select or place the cursor on a table name to generate CRUD procedures.",
                        Core.Constants.ProductName,
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Information);
                    return;
                }

                // Parse schema.table
                var schema = "dbo";
                var table = tableName;
                if (tableName.Contains("."))
                {
                    var parts = tableName.Split('.');
                    schema = parts[0].Trim('[', ']', ' ');
                    table = parts[1].Trim('[', ']', ' ');
                }

                Log.Information("CrudGenerationCommand: generating CRUD for [{Schema}].[{Table}]",
                    schema, table);

                var manager = EngineLifecycle.Manager;
                var client = manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    Log.Warning("CrudGenerationCommand: engine not available");
                    return;
                }

                var request = new CrudGenerationRequest
                {
                    SessionId = "active",
                    SchemaName = schema,
                    TableName = table,
                    Operations = "CRUD"
                };

                var response = await client.SendRequestAsync<CrudGenerationResponse, CrudGenerationRequest>(
                    MessageTypes.CrudGeneration, request, timeoutMs: 10000);

                if (!response.Success)
                {
                    System.Windows.Forms.MessageBox.Show(
                        $"CRUD generation failed: {response.Error}",
                        Core.Constants.ProductName,
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                    return;
                }

                // Combine all generated SQL and open in a new tab
                var combinedSql = string.Join("\n\n",
                    response.Procedures.ConvertAll(p =>
                        $"-- {p.Operation} procedure: {p.ProcedureName}\n{p.Sql}"));

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Open a new document and paste the SQL
                dte.ItemOperations.NewFile("General\\SQL File", $"CRUD_{table}.sql");
                var newDoc = dte.ActiveDocument?.Object("TextDocument") as EnvDTE.TextDocument;
                newDoc?.StartPoint?.CreateEditPoint()?.Insert(combinedSql);

                Log.Information("CrudGenerationCommand: generated {Count} procedures for {Table}",
                    response.Procedures.Count, table);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CrudGenerationCommand: execution failed");
            }
        }
    }
}
