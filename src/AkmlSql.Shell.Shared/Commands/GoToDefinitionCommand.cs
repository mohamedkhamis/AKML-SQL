#nullable enable
using System;
using System.ComponentModel.Design;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
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
    /// Go to Definition command (F12): extracts the identifier at the cursor position,
    /// sends a <see cref="GetObjectDefinitionRequest"/> to the engine, and opens the
    /// result in a new editor tab.
    /// </summary>
    internal sealed class GoToDefinitionCommand
    {
        private readonly AsyncPackage? _asyncPackage;
        private readonly Package? _syncPackage;

        private GoToDefinitionCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _asyncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdGoToDefinition);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        private GoToDefinitionCommand(Package package, OleMenuCommandService commandService)
        {
            _syncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdGoToDefinition);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static GoToDefinitionCommand? Instance { get; private set; }

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new GoToDefinitionCommand(package, commandService);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new GoToDefinitionCommand(package, commandService);
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
                    Log.Error(ex, "GoToDefinitionCommand: failed");
                }
            });
        }

        private async Task ExecuteAsync()
        {
            var manager = EngineLifecycle.Manager;
            if (manager?.Client == null || !manager.Client.IsConnected)
            {
                Log.Warning("GoToDefinitionCommand: engine not connected");
                return;
            }

            // Extract identifier at cursor from UI thread
            string objectName = string.Empty;
            string? schemaName = null;
            string sessionId = string.Empty;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var (name, schema, session) = ExtractIdentifierAtCursor();
                objectName = name;
                schemaName = schema;
                sessionId = session;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GoToDefinitionCommand: failed to extract identifier");
                return;
            }

            if (string.IsNullOrEmpty(objectName))
            {
                Log.Debug("GoToDefinitionCommand: no identifier at cursor");
                return;
            }

            // Send IPC request
            try
            {
                var request = new GetObjectDefinitionRequest
                {
                    SessionId = sessionId,
                    ObjectName = objectName,
                    SchemaName = schemaName,
                    PeekOnly = false
                };

                var response = await manager.Client.SendRequestAsync<GetObjectDefinitionResponse, GetObjectDefinitionRequest>(
                    MessageTypes.GetObjectDefinition, request, timeoutMs: 10000);

                if (!response.Success || string.IsNullOrEmpty(response.Definition))
                {
                    Log.Information("GoToDefinitionCommand: object not found - {Error}", response.Error);
                    await ShowStatusBarMessageAsync($"Definition not found: {response.Error}");
                    return;
                }

                // Open the definition in a new tab on the UI thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OpenDefinitionInNewTab(response.Definition, response.FullName ?? objectName, response.ObjectType ?? "Object");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GoToDefinitionCommand: IPC request failed");
            }
        }

        /// <summary>
        /// Extracts the SQL identifier at the current cursor position in the active editor.
        /// Handles schema-qualified names like "dbo.MyTable" or "[dbo].[MyTable]".
        /// Returns (objectName, schemaName, sessionId).
        /// </summary>
        internal static (string ObjectName, string? SchemaName, string SessionId) ExtractIdentifierAtCursor()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
            if (dte?.ActiveDocument == null)
                return (string.Empty, null, string.Empty);

            var textDocument = dte.ActiveDocument.Object("TextDocument") as TextDocument;
            if (textDocument == null)
                return (string.Empty, null, string.Empty);

            var editPoint = textDocument.Selection.ActivePoint.CreateEditPoint();
            var lineText = editPoint.GetLines(editPoint.Line, editPoint.Line + 1);
            var col = editPoint.LineCharOffset - 1; // 0-based

            if (string.IsNullOrEmpty(lineText) || col < 0)
                return (string.Empty, null, string.Empty);

            // Extract the identifier at the cursor position
            var (name, schema) = ParseIdentifierAtPosition(lineText, col);

            // Get session ID from buffer properties if available
            var sessionId = Guid.NewGuid().ToString("N"); // fallback

            return (name, schema, sessionId);
        }

        /// <summary>
        /// Parses a SQL identifier at the given position, including schema prefix.
        /// Handles bracket-delimited identifiers like [dbo].[MyTable].
        /// </summary>
        internal static (string ObjectName, string? SchemaName) ParseIdentifierAtPosition(string lineText, int col)
        {
            if (col >= lineText.Length)
                col = lineText.Length - 1;
            if (col < 0)
                return (string.Empty, null);

            // Find the word boundaries
            int start = col;
            while (start > 0 && IsIdentifierChar(lineText[start - 1]))
                start--;

            int end = col;
            while (end < lineText.Length && IsIdentifierChar(lineText[end]))
                end++;

            // Handle bracket-delimited identifiers
            if (start > 0 && lineText[start - 1] == '[')
            {
                start--;
                int bracketEnd = lineText.IndexOf(']', end);
                if (bracketEnd >= 0)
                    end = bracketEnd + 1;
            }

            if (start == end)
                return (string.Empty, null);

            var identifier = lineText.Substring(start, end - start).Trim('[', ']');

            // Check if there's a schema prefix (dot before the start)
            string? schema = null;
            if (start > 1 && lineText[start - 1] == '.')
            {
                int schemaEnd = start - 1;
                int schemaStart = schemaEnd - 1;

                // Handle bracketed schema like [dbo].
                if (schemaStart >= 0 && lineText[schemaStart] == ']')
                {
                    int bracketOpen = lineText.LastIndexOf('[', schemaStart);
                    if (bracketOpen >= 0)
                    {
                        schema = lineText.Substring(bracketOpen, schemaStart - bracketOpen + 1).Trim('[', ']');
                    }
                }
                else
                {
                    // Plain schema name
                    while (schemaStart >= 0 && IsIdentifierChar(lineText[schemaStart]))
                        schemaStart--;
                    schemaStart++;
                    if (schemaStart < schemaEnd)
                        schema = lineText.Substring(schemaStart, schemaEnd - schemaStart);
                }
            }

            return (identifier, schema);
        }

        /// <summary>
        /// Opens the SQL definition text in a new editor tab.
        /// </summary>
        private void OpenDefinitionInNewTab(string definition, string objectName, string objectType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                if (dte == null) return;

                // Create a temporary file name for the definition
                var tempDir = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "AkmlSql", "Definitions");
                System.IO.Directory.CreateDirectory(tempDir);

                var safeName = objectName.Replace(".", "_").Replace("[", "").Replace("]", "");
                var fileName = $"{safeName} ({objectType}).sql";
                var filePath = System.IO.Path.Combine(tempDir, fileName);

                System.IO.File.WriteAllText(filePath, definition);

                dte.ItemOperations.OpenFile(filePath, EnvDTE.Constants.vsViewKindCode);

                Log.Information("GoToDefinitionCommand: opened definition for {Object} ({Type})", objectName, objectType);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GoToDefinitionCommand: failed to open definition tab");
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

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@';
        }
    }
}
