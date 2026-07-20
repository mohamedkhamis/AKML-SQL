#nullable enable
using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ai;
using AkmlSql.Shell.Shared.Ipc;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Text-to-SQL command (Phase 9, US1): generates SQL from a natural-language prompt.
    /// Supports two input modes:
    /// <list type="bullet">
    ///   <item><c>--ai:</c> prefix on the current editor line — prompt is extracted inline.</item>
    ///   <item>No prefix — opens <see cref="TextToSqlInputDialog"/> for user input.</item>
    /// </list>
    /// The generated SQL is shown in a <see cref="DiffPreviewPanel"/> for acceptance/rejection.
    /// </summary>
    internal sealed class TextToSqlCommand
    {
        private readonly AsyncPackage? _asyncPackage;
        private readonly Package? _syncPackage;

        private TextToSqlCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _asyncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdTextToSql);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += AiCommandVisibility.OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        private TextToSqlCommand(Package package, OleMenuCommandService commandService)
        {
            _syncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdTextToSql);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += AiCommandVisibility.OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static TextToSqlCommand? Instance { get; private set; }

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new TextToSqlCommand(package, commandService);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new TextToSqlCommand(package, commandService);
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
                    Log.Error(ex, "TextToSqlCommand: failed");
                }
            });
        }

        private async Task ExecuteAsync()
        {
            // Step 1: Check AI configuration
            AiSettings aiSettings;
            try
            {
                var settings = ConfigManager.Load();
                aiSettings = settings.Ai;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TextToSqlCommand: failed to load settings");
                await ShowMessageBoxAsync("AKML SQL", "Failed to load settings. Please check the configuration file.");
                return;
            }

            if (!aiSettings.Enabled || string.IsNullOrWhiteSpace(aiSettings.Provider))
            {
                await ShowMessageBoxAsync("AI Not Configured",
                    "AI assistance is not configured.\n\n" +
                    "Please go to AKML SQL > Options to configure your AI provider (API key, model, etc.).");
                return;
            }

            // Step 2: Check engine connectivity
            var manager = EngineLifecycle.Manager;
            if (manager?.Client == null || !manager.Client.IsConnected)
            {
                Log.Warning("TextToSqlCommand: engine not connected");
                await ShowMessageBoxAsync("AKML SQL", "The AKML SQL engine is not connected. Please wait for it to start.");
                return;
            }

            // Step 3: Get prompt — either from --ai: prefix or from input dialog
            string prompt;
            string sessionId;
            bool hasInlinePrefix = false;
            int inlinePrefixLine = -1;
            string? inlinePrefixFullLine = null;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var (extractedPrompt, session, isInline, lineNumber, fullLine) = ExtractPromptFromEditor();
                prompt = extractedPrompt;
                sessionId = session;
                hasInlinePrefix = isInline;
                inlinePrefixLine = lineNumber;
                inlinePrefixFullLine = fullLine;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TextToSqlCommand: failed to extract prompt from editor");
                prompt = string.Empty;
                sessionId = Guid.NewGuid().ToString("N");
            }

            // If no --ai: prefix found, show input dialog
            if (string.IsNullOrWhiteSpace(prompt))
            {
                using var inputDialog = new TextToSqlInputDialog();
                if (inputDialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(inputDialog.Prompt))
                {
                    return; // User cancelled
                }
                prompt = inputDialog.Prompt;
            }

            // Step 4: Send IPC request to engine
            try
            {
                await ShowStatusBarMessageAsync("AI: Generating SQL...");

                var request = new AiTextToSqlRequest
                {
                    SessionId = sessionId,
                    Prompt = prompt
                };

                var response = await manager.Client.SendRequestAsync<AiTextToSqlResponse, AiTextToSqlRequest>(
                    MessageTypes.AiTextToSql, request,
                    timeoutMs: Ai.AiIpcTimeouts.ForAiRequestMs(ConfigManager.Load()));

                if (!response.Success || string.IsNullOrEmpty(response.GeneratedSql))
                {
                    var errorMsg = response.ErrorMessage ?? "No SQL was generated";
                    Log.Information("TextToSqlCommand: generation failed - {Error}", errorMsg);
                    await ShowStatusBarMessageAsync($"AI: {errorMsg}");
                    await ShowMessageBoxAsync("Text to SQL", $"Generation failed: {errorMsg}");
                    return;
                }

                await ShowStatusBarMessageAsync(
                    $"AI: Generated in {response.LatencyMs}ms ({response.TokensUsed} tokens)");

                // Step 5: Show preview dialog
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                using var preview = new DiffPreviewPanel(
                    response.GeneratedSql,
                    prompt,
                    response.TokensUsed,
                    response.LatencyMs);

                var dialogResult = preview.ShowDialog();

                if (dialogResult == DialogResult.OK)
                {
                    // Accept: insert/replace in current editor
                    InsertSqlIntoEditor(preview.GeneratedSql, hasInlinePrefix, inlinePrefixLine, inlinePrefixFullLine);
                }
                else if (dialogResult == DialogResult.Yes)
                {
                    // Edit: open in new tab
                    OpenSqlInNewTab(preview.GeneratedSql, prompt);
                }
                // Cancel/Reject: do nothing
            }
            catch (TimeoutException)
            {
                Log.Warning("TextToSqlCommand: request timed out");
                await ShowStatusBarMessageAsync("AI: Request timed out");
                await ShowMessageBoxAsync("Text to SQL", "The AI request timed out. Try again or check your provider settings.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TextToSqlCommand: IPC request failed");
                await ShowStatusBarMessageAsync("AI: Generation failed");
                await ShowMessageBoxAsync("Text to SQL", $"An error occurred: {ex.Message}");
            }
        }

        /// <summary>
        /// Extracts a prompt from the current editor line if it starts with <c>--ai:</c>.
        /// Returns the prompt text, session ID, whether inline prefix was found,
        /// the line number, and the full line text.
        /// </summary>
        private static (string Prompt, string SessionId, bool IsInline, int LineNumber, string? FullLine) ExtractPromptFromEditor()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
            if (dte?.ActiveDocument == null)
                return (string.Empty, Guid.NewGuid().ToString("N"), false, -1, null);

            var textDocument = dte.ActiveDocument.Object("TextDocument") as TextDocument;
            if (textDocument == null)
                return (string.Empty, Guid.NewGuid().ToString("N"), false, -1, null);

            var editPoint = textDocument.Selection.ActivePoint.CreateEditPoint();
            var lineText = editPoint.GetLines(editPoint.Line, editPoint.Line + 1);
            var lineNumber = editPoint.Line;

            var sessionId = Guid.NewGuid().ToString("N");

            if (string.IsNullOrWhiteSpace(lineText))
                return (string.Empty, sessionId, false, lineNumber, lineText);

            // Check for --ai: prefix (case-insensitive, trimmed)
            var trimmedLine = lineText.TrimStart();
            if (trimmedLine.StartsWith("--ai:", StringComparison.OrdinalIgnoreCase))
            {
                var prompt = trimmedLine.Substring(5).Trim(); // Skip past "--ai:"
                return (prompt, sessionId, true, lineNumber, lineText);
            }

            // Also support "-- ai:" with a space
            if (trimmedLine.StartsWith("-- ai:", StringComparison.OrdinalIgnoreCase))
            {
                var prompt = trimmedLine.Substring(6).Trim(); // Skip past "-- ai:"
                return (prompt, sessionId, true, lineNumber, lineText);
            }

            return (string.Empty, sessionId, false, lineNumber, lineText);
        }

        /// <summary>
        /// Inserts the generated SQL into the active editor. If the prompt came from an
        /// <c>--ai:</c> line, replaces that line; otherwise appends at the cursor position.
        /// </summary>
        private static void InsertSqlIntoEditor(string sql, bool replaceInline, int lineNumber, string? originalLine)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                if (dte?.ActiveDocument == null) return;

                var textDocument = dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (textDocument == null) return;

                if (replaceInline && lineNumber > 0)
                {
                    // Replace the --ai: line with the generated SQL
                    var startPoint = textDocument.CreateEditPoint(textDocument.StartPoint);
                    startPoint.MoveToLineAndOffset(lineNumber, 1);

                    var endPoint = startPoint.CreateEditPoint();
                    endPoint.EndOfLine();

                    startPoint.ReplaceText(endPoint, sql, (int)vsEPReplaceTextOptions.vsEPReplaceTextAutoformat);
                }
                else
                {
                    // Insert at current cursor position
                    var selection = textDocument.Selection;
                    selection.Insert(sql, (int)vsInsertFlags.vsInsertFlagsInsertAtEnd);
                }

                Log.Information("TextToSqlCommand: inserted generated SQL ({Length} chars)", sql.Length);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TextToSqlCommand: failed to insert SQL into editor");
            }
        }

        /// <summary>
        /// Opens the generated SQL in a new temporary editor tab.
        /// </summary>
        private static void OpenSqlInNewTab(string sql, string prompt)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                if (dte == null) return;

                var tempDir = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "AkmlSql", "AI");
                System.IO.Directory.CreateDirectory(tempDir);

                // Create a descriptive filename from the first few words of the prompt
                var safePrompt = prompt.Length > 40 ? prompt.Substring(0, 40) : prompt;
                safePrompt = System.Text.RegularExpressions.Regex.Replace(safePrompt, @"[^a-zA-Z0-9 _-]", "");
                safePrompt = safePrompt.Trim();
                if (string.IsNullOrEmpty(safePrompt)) safePrompt = "Generated";

                var fileName = $"{safePrompt} (AI).sql";
                var filePath = System.IO.Path.Combine(tempDir, fileName);

                System.IO.File.WriteAllText(filePath, sql);
                dte.ItemOperations.OpenFile(filePath, EnvDTE.Constants.vsViewKindCode);

                Log.Information("TextToSqlCommand: opened generated SQL in new tab");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TextToSqlCommand: failed to open new tab");
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

        private static async Task ShowMessageBoxAsync(string title, string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsShellUtilities.ShowMessageBox(
                (IServiceProvider)Package.GetGlobalService(typeof(SVsShell))
                    ?? throw new InvalidOperationException("Shell service not available"),
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
