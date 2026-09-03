#nullable enable
using System;
using System.ComponentModel.Design;
using System.Text;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Refactoring;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// AI Index Analysis command: sends the selected SQL text to the engine for AI-powered
    /// index improvement suggestions. Displays CREATE INDEX scripts with metadata (estimated
    /// improvement, size, write impact) in a results window.
    /// </summary>
    internal sealed class AiIndexAnalysisCommand
    {
        private readonly AsyncPackage? _asyncPackage;
        private readonly Package? _syncPackage;

        private AiIndexAnalysisCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _asyncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdAiIndexAnalysis);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += AiCommandVisibility.OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        private AiIndexAnalysisCommand(Package package, OleMenuCommandService commandService)
        {
            _syncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdAiIndexAnalysis);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += AiCommandVisibility.OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static AiIndexAnalysisCommand? Instance { get; private set; }

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new AiIndexAnalysisCommand(package, commandService);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new AiIndexAnalysisCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ExecuteAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "AiIndexAnalysisCommand: failed");
                }
            });
        }

        private async Task ExecuteAsync()
        {
            var manager = EngineLifecycle.Manager;
            if (manager?.Client == null || !manager.Client.IsConnected)
            {
                Log.Warning("AiIndexAnalysisCommand: engine not connected");
                await ShowStatusBarMessageAsync("AI Index Analysis: engine not connected");
                return;
            }

            // Get selected text from the active editor on the UI thread
            string selectedSql = string.Empty;
            string sessionId = string.Empty;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                if (dte?.ActiveDocument == null)
                {
                    await ShowStatusBarMessageAsync("AI Index Analysis: no active document");
                    return;
                }

                var textDocument = dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    await ShowStatusBarMessageAsync("AI Index Analysis: no text document");
                    return;
                }

                selectedSql = textDocument.Selection.Text;
                // Spec 036 (US1, T014): the editor's real session id, never a fabricated one (R1).
                sessionId = RefactorCommandHelper.TryGetActiveRealSessionId() ?? string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiIndexAnalysisCommand: failed to get selected text");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedSql))
            {
                await ShowStatusBarMessageAsync("AI Index Analysis: select SQL to analyze");
                return;
            }

            await ShowStatusBarMessageAsync("AI Index Analysis: analyzing query...");

            try
            {
                var request = new AiIndexAnalysisRequest
                {
                    SessionId = sessionId,
                    SelectedSql = selectedSql,
                    ExecutionPlanXml = null // Execution plan integration deferred to future enhancement
                };

                var response = await manager.Client.SendRequestAsync<AiIndexAnalysisResponse, AiIndexAnalysisRequest>(
                    MessageTypes.AiIndexAnalysis, request,
                    timeoutMs: Ai.AiIpcTimeouts.ForAiRequestMs(AkmlSql.Core.Config.ConfigManager.Load()));

                if (!response.Success)
                {
                    Log.Information("AiIndexAnalysisCommand: analysis failed - {Error}", response.ErrorMessage);
                    await ShowStatusBarMessageAsync($"AI Index Analysis: {response.ErrorMessage}");
                    return;
                }

                // Show the results on the UI thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowIndexAnalysisResults(response);

                await ShowStatusBarMessageAsync(
                    $"AI Index Analysis: completed ({response.TokensUsed} tokens, {response.LatencyMs}ms)");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiIndexAnalysisCommand: IPC request failed");
                await ShowStatusBarMessageAsync($"AI Index Analysis: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows the index analysis results by writing the summary and CREATE INDEX scripts
        /// into a new temporary SQL file and opening it in the editor.
        /// </summary>
        private static void ShowIndexAnalysisResults(AiIndexAnalysisResponse response)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                if (dte == null) return;

                var sb = new StringBuilder();
                sb.AppendLine("-- ========================================");
                sb.AppendLine("-- AI INDEX ANALYSIS RESULTS");
                sb.AppendLine("-- ========================================");
                sb.AppendLine();

                // Summary
                if (!string.IsNullOrWhiteSpace(response.Summary))
                {
                    sb.AppendLine("-- SUMMARY:");
                    foreach (var line in response.Summary.Split('\n'))
                    {
                        sb.AppendLine($"-- {line.TrimEnd()}");
                    }
                    sb.AppendLine();
                }

                // Index suggestions
                if (response.Suggestions != null && response.Suggestions.Count > 0)
                {
                    sb.AppendLine("-- ========================================");
                    sb.AppendLine("-- SUGGESTED INDEXES");
                    sb.AppendLine("-- ========================================");
                    sb.AppendLine();

                    for (int i = 0; i < response.Suggestions.Count; i++)
                    {
                        var idx = response.Suggestions[i];
                        sb.AppendLine($"-- Suggestion {i + 1}:");
                        sb.AppendLine($"--   Table: {idx.TableName}");
                        if (idx.Columns.Count > 0)
                        {
                            sb.AppendLine($"--   Key columns: {string.Join(", ", idx.Columns)}");
                        }
                        if (idx.IncludeColumns != null && idx.IncludeColumns.Count > 0)
                        {
                            sb.AppendLine($"--   Include columns: {string.Join(", ", idx.IncludeColumns)}");
                        }
                        if (!string.IsNullOrWhiteSpace(idx.EstimatedImprovement))
                        {
                            sb.AppendLine($"--   Estimated improvement: {idx.EstimatedImprovement}");
                        }
                        if (idx.EstimatedSizeKb > 0)
                        {
                            sb.AppendLine($"--   Estimated size: {idx.EstimatedSizeKb} KB");
                        }
                        if (!string.IsNullOrWhiteSpace(idx.WriteImpact))
                        {
                            sb.AppendLine($"--   Write impact: {idx.WriteImpact}");
                        }
                        sb.AppendLine();
                        sb.AppendLine(idx.CreateScript);
                        sb.AppendLine("GO");
                        sb.AppendLine();
                    }
                }
                else
                {
                    sb.AppendLine("-- No index suggestions — existing indexes are sufficient for this query.");
                    sb.AppendLine();
                }

                sb.AppendLine($"-- Analysis completed: {response.TokensUsed} tokens, {response.LatencyMs}ms");

                // Write to temp file and open
                var tempDir = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "AkmlSql", "AiIndexAnalysis");
                System.IO.Directory.CreateDirectory(tempDir);

                var fileName = $"IndexAnalysis_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
                var filePath = System.IO.Path.Combine(tempDir, fileName);

                System.IO.File.WriteAllText(filePath, sb.ToString());
                dte.ItemOperations.OpenFile(filePath, EnvDTE.Constants.vsViewKindCode);

                Log.Information("AiIndexAnalysisCommand: opened index analysis results in {Path}", filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiIndexAnalysisCommand: failed to show results");
            }
        }

        private static async Task ShowStatusBarMessageAsync(string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var statusBar = (IVsStatusbar?)Package.GetGlobalService(typeof(SVsStatusbar));
                statusBar?.SetText(message);
            }
            catch
            {
                // Best-effort status bar update
            }
        }
    }
}
