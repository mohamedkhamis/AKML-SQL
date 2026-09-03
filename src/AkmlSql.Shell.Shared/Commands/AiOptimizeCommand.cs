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
    /// AI Optimize command: sends the selected SQL text to the engine for AI-powered
    /// performance optimization. Displays the optimized SQL in a diff preview window
    /// alongside annotations and optional index suggestions.
    /// </summary>
    internal sealed class AiOptimizeCommand
    {
        private readonly AsyncPackage? _asyncPackage;
        private readonly Package? _syncPackage;

        private AiOptimizeCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _asyncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdAiOptimize);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += AiCommandVisibility.OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        private AiOptimizeCommand(Package package, OleMenuCommandService commandService)
        {
            _syncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdAiOptimize);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += AiCommandVisibility.OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static AiOptimizeCommand? Instance { get; private set; }

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new AiOptimizeCommand(package, commandService);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new AiOptimizeCommand(package, commandService);
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
                    Log.Error(ex, "AiOptimizeCommand: failed");
                }
            });
        }

        private async Task ExecuteAsync()
        {
            var manager = EngineLifecycle.Manager;
            if (manager?.Client == null || !manager.Client.IsConnected)
            {
                Log.Warning("AiOptimizeCommand: engine not connected");
                await ShowStatusBarMessageAsync("AI Optimize: engine not connected");
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
                    await ShowStatusBarMessageAsync("AI Optimize: no active document");
                    return;
                }

                var textDocument = dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    await ShowStatusBarMessageAsync("AI Optimize: no text document");
                    return;
                }

                selectedSql = textDocument.Selection.Text;
                // Spec 036 (US1, T014): the editor's real session id, never a fabricated one (R1).
                sessionId = RefactorCommandHelper.TryGetActiveRealSessionId() ?? string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiOptimizeCommand: failed to get selected text");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedSql))
            {
                await ShowStatusBarMessageAsync("AI Optimize: select SQL to optimize");
                return;
            }

            await ShowStatusBarMessageAsync("AI Optimize: analyzing query...");

            try
            {
                var request = new AiOptimizeRequest
                {
                    SessionId = sessionId,
                    SelectedSql = selectedSql
                };

                var response = await manager.Client.SendRequestAsync<AiOptimizeResponse, AiOptimizeRequest>(
                    MessageTypes.AiOptimize, request,
                    timeoutMs: Ai.AiIpcTimeouts.ForAiRequestMs(AkmlSql.Core.Config.ConfigManager.Load()));

                if (!response.Success)
                {
                    Log.Information("AiOptimizeCommand: optimization failed - {Error}", response.ErrorMessage);
                    await ShowStatusBarMessageAsync($"AI Optimize: {response.ErrorMessage}");
                    return;
                }

                // Show the results on the UI thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowOptimizeResults(selectedSql, response);

                await ShowStatusBarMessageAsync(
                    $"AI Optimize: completed ({response.TokensUsed} tokens, {response.LatencyMs}ms)");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiOptimizeCommand: IPC request failed");
                await ShowStatusBarMessageAsync($"AI Optimize: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows the optimization results by writing the optimized SQL, explanation,
        /// annotations, and index suggestions into a new temporary file and opening it.
        /// </summary>
        private static void ShowOptimizeResults(string originalSql, AiOptimizeResponse response)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                if (dte == null) return;

                var sb = new StringBuilder();
                sb.AppendLine("-- ========================================");
                sb.AppendLine("-- AI OPTIMIZE RESULTS");
                sb.AppendLine("-- ========================================");
                sb.AppendLine();

                // Explanation
                if (!string.IsNullOrWhiteSpace(response.Explanation))
                {
                    sb.AppendLine("-- EXPLANATION:");
                    foreach (var line in response.Explanation.Split('\n'))
                    {
                        sb.AppendLine($"-- {line.TrimEnd()}");
                    }
                    sb.AppendLine();
                }

                // Annotations
                if (response.Annotations != null && response.Annotations.Count > 0)
                {
                    sb.AppendLine("-- ANNOTATIONS:");
                    foreach (var ann in response.Annotations)
                    {
                        var lineRef = ann.StartLine == ann.EndLine
                            ? $"Line {ann.StartLine}"
                            : $"Lines {ann.StartLine}-{ann.EndLine}";
                        sb.AppendLine($"-- [{ann.Category.ToUpperInvariant()}] {lineRef}: {ann.Description}");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("-- ---- OPTIMIZED SQL ----");
                sb.AppendLine();
                sb.AppendLine(response.OptimizedSql ?? originalSql);
                sb.AppendLine();

                // Index suggestions
                if (response.IndexSuggestions != null && response.IndexSuggestions.Count > 0)
                {
                    sb.AppendLine("-- ========================================");
                    sb.AppendLine("-- INDEX SUGGESTIONS");
                    sb.AppendLine("-- ========================================");
                    sb.AppendLine();
                    foreach (var idx in response.IndexSuggestions)
                    {
                        if (!string.IsNullOrWhiteSpace(idx.EstimatedImprovement))
                        {
                            sb.AppendLine($"-- Estimated improvement: {idx.EstimatedImprovement}");
                        }
                        if (idx.EstimatedSizeKb > 0)
                        {
                            sb.AppendLine($"-- Estimated size: {idx.EstimatedSizeKb} KB");
                        }
                        if (!string.IsNullOrWhiteSpace(idx.WriteImpact))
                        {
                            sb.AppendLine($"-- Write impact: {idx.WriteImpact}");
                        }
                        sb.AppendLine(idx.CreateScript);
                        sb.AppendLine("GO");
                        sb.AppendLine();
                    }
                }

                // Write to temp file and open
                var tempDir = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "AkmlSql", "AiOptimize");
                System.IO.Directory.CreateDirectory(tempDir);

                var fileName = $"Optimized_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
                var filePath = System.IO.Path.Combine(tempDir, fileName);

                System.IO.File.WriteAllText(filePath, sb.ToString());
                dte.ItemOperations.OpenFile(filePath, EnvDTE.Constants.vsViewKindCode);

                Log.Information("AiOptimizeCommand: opened optimization results in {Path}", filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiOptimizeCommand: failed to show results");
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
