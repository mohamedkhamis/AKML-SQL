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
    /// Shell command for the Split Table heavyweight refactoring operation.
    /// Flow: get table identifier under cursor → prompt for new table name and column names →
    /// send preview request to engine → show preview dialog → generate SQL script → open in new tab.
    /// Per spec: does NOT execute against the database directly.
    /// </summary>
    internal sealed class SplitTableCommand
    {
        public static SplitTableCommand? Instance { get; private set; }

        private SplitTableCommand(Package package, OleMenuCommandService commandService)
        {
            // Split Table is invoked from the editor context menu or the Command Palette.
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new SplitTableCommand(package, commandService);
        }

        /// <summary>
        /// Executes the Split Table flow. Can be called from context menu or Command Palette.
        /// </summary>
        /// <param name="sessionId">The active editor session ID.</param>
        /// <param name="documentText">Full text of the active document.</param>
        /// <param name="selectionStart">Character offset of the selection/cursor.</param>
        /// <param name="selectionLength">Length of the selection (0 if just a cursor).</param>
        /// <param name="originalIdentifier">The table name under the cursor to split.</param>
        public static void Execute(
            string sessionId,
            string documentText,
            int selectionStart,
            int selectionLength,
            string originalIdentifier)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (string.IsNullOrWhiteSpace(originalIdentifier))
                {
                    MessageBox.Show("Place the cursor on a table name to split.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Step 1: Prompt for new table name and columns to move
                var input = PromptForSplitDetails(originalIdentifier);
                if (input == null)
                    return; // User cancelled

                string newTableName = input.Value.NewTableName;
                string[] columnsToMove = input.Value.ColumnsToMove;

                if (string.IsNullOrWhiteSpace(newTableName))
                {
                    MessageBox.Show("A new table name is required.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (columnsToMove.Length == 0)
                {
                    MessageBox.Show("At least one column must be specified to move.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Step 2: Send preview request to engine
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    MessageBox.Show("The AKML SQL engine is not running. Please wait for it to start.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var request = new RefactorPreviewRequest
                {
                    SessionId = sessionId,
                    OperationType = (int)RefactorOperationType.SplitTable,
                    DocumentText = documentText,
                    SelectionStart = selectionStart,
                    SelectionLength = selectionLength > 0 ? selectionLength : originalIdentifier.Length,
                    NewName = newTableName,
                    OriginalIdentifier = originalIdentifier,
                    AdditionalFilePaths = columnsToMove // Column names passed via AdditionalFilePaths
                };

                RefactorPreviewResponse? response = null;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    response = await client.SendRequestAsync<RefactorPreviewResponse, RefactorPreviewRequest>(
                        MessageTypes.RequestRefactorPreview,
                        request,
                        timeoutMs: 30_000);
                });

                if (response == null)
                {
                    MessageBox.Show("No response from the engine. The operation timed out.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (response.Changes.Length == 0 && response.Errors.Length == 0)
                {
                    MessageBox.Show($"No changes were generated for splitting '{originalIdentifier}'.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Check for blocking errors before showing preview
                if (response.Errors.Length > 0 && !response.CanApply)
                {
                    MessageBox.Show("Cannot split table:\n\n" + string.Join("\n", response.Errors),
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Step 3: Show preview dialog
                using var previewDialog = new RefactoringPreviewDialog(response, originalIdentifier, newTableName);
                var dialogResult = previewDialog.ShowDialog();

                if (dialogResult != DialogResult.OK)
                    return; // User cancelled

                // Step 4: Generate SQL script from approved changes
                var approvedChanges = previewDialog.ApprovedChanges;
                if (approvedChanges == null || approvedChanges.Length == 0)
                {
                    MessageBox.Show("No changes were selected.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var script = RenameScriptGenerator.Generate(
                    approvedChanges, originalIdentifier, newTableName, response.Warnings);

                // Step 5: Open script in a new editor tab
                OpenScriptInNewTab(script, $"SplitTable_{originalIdentifier}_to_{newTableName}.sql");

                Log.Information(
                    "SplitTable: generated script for '{Source}' → '{NewTable}' moving {ColCount} column(s), {ChangeCount} change(s)",
                    originalIdentifier, newTableName, columnsToMove.Length, approvedChanges.Length);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SplitTableCommand: failed to execute Split Table");
                MessageBox.Show($"Split Table failed: {ex.Message}",
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Shows an input dialog prompting for the new table name and columns to move.
        /// Returns null if the user cancels.
        /// </summary>
        private static SplitTableInput? PromptForSplitDetails(string sourceTable)
        {
            var consolasFont = new System.Drawing.Font("Consolas", 10);
            var hintFont = new System.Drawing.Font("Segoe UI", 8f);

            using var form = new Form
            {
                Text = Constants.ProductName + $" - Split Table \u2014 {sourceTable}",
                Width = 480,
                Height = 280,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false
            };

            // Dispose fonts when form closes
            form.Disposed += (_, __) => { consolasFont.Dispose(); hintFont.Dispose(); };

            // New table name
            var lblNewTable = new Label
            {
                Text = "New table name:",
                Left = 12, Top = 16, Width = 440, AutoSize = false
            };

            var txtNewTable = new TextBox
            {
                Left = 12, Top = 38, Width = 440,
                Font = consolasFont
            };

            // Columns to move
            var lblColumns = new Label
            {
                Text = "Columns to move (comma-separated):",
                Left = 12, Top = 74, Width = 440, AutoSize = false
            };

            var txtColumns = new TextBox
            {
                Left = 12, Top = 96, Width = 440, Height = 80,
                Font = consolasFont,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = false
            };

            // Hint
            var lblHint = new Label
            {
                Text = "Primary key columns will remain in the source table. A foreign key linking both tables will be generated.",
                Left = 12, Top = 184, Width = 440, Height = 32,
                AutoSize = false,
                ForeColor = System.Drawing.SystemColors.GrayText,
                Font = hintFont
            };

            var btnSplit = new Button
            {
                Text = "Split",
                Left = 270, Top = 220, Width = 85, Height = 28,
                DialogResult = DialogResult.OK
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                Left = 365, Top = 220, Width = 85, Height = 28,
                DialogResult = DialogResult.Cancel
            };

            form.Controls.AddRange(new Control[] { lblNewTable, txtNewTable, lblColumns, txtColumns, lblHint, btnSplit, btnCancel });
            form.AcceptButton = btnSplit;
            form.CancelButton = btnCancel;
            form.ActiveControl = txtNewTable;

            if (form.ShowDialog() != DialogResult.OK)
                return null;

            string newTableName = txtNewTable.Text.Trim();
            string columnsRaw = txtColumns.Text.Trim();

            if (string.IsNullOrWhiteSpace(newTableName) || string.IsNullOrWhiteSpace(columnsRaw))
                return null;

            // Parse comma-separated column names, trimming whitespace
            var columns = columnsRaw
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < columns.Length; i++)
                columns[i] = columns[i].Trim();

            // Filter out empty entries after trimming
            var filtered = Array.FindAll(columns, c => c.Length > 0);
            if (filtered.Length == 0)
                return null;

            return new SplitTableInput(newTableName, filtered);
        }

        /// <summary>
        /// Opens a SQL script in a new SSMS/VS editor tab via DTE.
        /// </summary>
        private static void OpenScriptInNewTab(string scriptContent, string suggestedFileName)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte == null)
                {
                    Log.Warning("SplitTableCommand: DTE not available, cannot open script tab");
                    return;
                }

                // Create a temporary file with sanitized name + unique suffix to prevent overwrites
                var tempDir = System.IO.Path.GetTempPath();
                var safeName = SanitizeFileName(suggestedFileName);
                var uniqueSuffix = System.IO.Path.GetRandomFileName().Substring(0, 6);
                var tempFile = System.IO.Path.Combine(tempDir, $"{safeName}_{uniqueSuffix}.sql");
                System.IO.File.WriteAllText(tempFile, scriptContent, System.Text.Encoding.UTF8);

                dte.ItemOperations.OpenFile(tempFile, EnvDTE.Constants.vsViewKindCode);

                Log.Debug("SplitTableCommand: opened split table script in new tab: {Path}", tempFile);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SplitTableCommand: failed to open script tab, copying to clipboard instead");

                try
                {
                    System.Windows.Clipboard.SetText(scriptContent);
                    MessageBox.Show(
                        "The split table script has been copied to the clipboard.\nPaste it into a new query window.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch
                {
                    // Last resort
                }
            }
        }

        /// <summary>
        /// Strips characters invalid in Windows file names from a suggested file name.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Holds the user's input from the Split Table dialog.
        /// </summary>
        private readonly struct SplitTableInput
        {
            public string NewTableName { get; }
            public string[] ColumnsToMove { get; }

            public SplitTableInput(string newTableName, string[] columnsToMove)
            {
                NewTableName = newTableName;
                ColumnsToMove = columnsToMove;
            }
        }
    }
}
