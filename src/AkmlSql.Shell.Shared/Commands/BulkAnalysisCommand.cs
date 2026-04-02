using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// AKML SQL → Run Code Analysis command: lets the user pick a scope (directory),
    /// sends <c>RequestAnalyze</c> messages to the engine for each .sql file,
    /// and shows the <see cref="BulkAnalysisResultDialog"/>.
    /// </summary>
    internal sealed class BulkAnalysisCommand
    {
        private BulkAnalysisCommand(Package package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdBulkAnalysis);
            var item  = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(item);
        }

        public static BulkAnalysisCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new BulkAnalysisCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                using var folderDlg = new FolderBrowserDialog
                {
                    Description         = "Select a folder to analyze",
                    ShowNewFolderButton = false
                };

                if (folderDlg.ShowDialog() != DialogResult.OK) return;

                var directory = folderDlg.SelectedPath;
                var sqlFiles  = Directory.GetFiles(directory, "*.sql", SearchOption.AllDirectories);

                if (sqlFiles.Length == 0)
                {
                    MessageBox.Show("No .sql files found in the selected directory.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    MessageBox.Show("Engine is not running. Please wait for the extension to initialize.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Fire analysis requests for each file (up to 20 at a time)
                var allIssues = new List<(string File, CodeIssueInfo Issue)>();
                var cts       = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                using var progressForm = new Form
                {
                    Text = "Running Code Analysis...",
                    Size = new System.Drawing.Size(380, 80),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition   = FormStartPosition.CenterParent,
                    ControlBox      = false
                };
                var progressLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    Text = $"Analyzing {sqlFiles.Length} files..."
                };
                progressForm.Controls.Add(progressLabel);
                progressForm.Show();
                Application.DoEvents();

                try
                {
                    int version = 1;
                    foreach (var file in sqlFiles)
                    {
                        if (cts.IsCancellationRequested) break;
                        progressLabel.Text = $"Analyzing: {Path.GetFileName(file)}...";
                        Application.DoEvents();

                        try
                        {
                            var sql      = File.ReadAllText(file);
                            var request  = new CodeAnalysisRequest
                            {
                                SessionId       = "bulk",
                                RequestId       = version.ToString(),
                                DocumentText    = sql,
                                DocumentVersion = version++
                            };

                            var response = client.SendRequestAsync<CodeAnalysisResponse, CodeAnalysisRequest>(
                                    MessageTypes.RequestAnalyze, request, timeoutMs: 15_000, ct: cts.Token)
                                .GetAwaiter().GetResult();

                            if (response?.Issues != null)
                                foreach (var issue in response.Issues)
                                    allIssues.Add((file, issue));
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "BulkAnalysisCommand: failed to analyze {File}", file);
                        }
                    }
                }
                finally
                {
                    progressForm.Close();
                }

                using var resultDialog = new BulkAnalysisResultDialog(allIssues, sqlFiles.Length);
                resultDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BulkAnalysisCommand.Execute failed");
                MessageBox.Show("Code analysis failed: " + ex.Message,
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
