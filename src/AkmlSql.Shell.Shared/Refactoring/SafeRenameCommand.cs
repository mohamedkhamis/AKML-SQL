#nullable enable
using System;
using System.ComponentModel.Design;
using System.Linq;
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
    /// Shell command for the Safe Rename heavyweight refactoring operation.
    /// Flow: prompt for new name → send preview request to engine → show preview dialog →
    /// generate SQL script → open script in new editor tab.
    /// Per spec clarification: does NOT execute against the database directly.
    /// </summary>
    internal sealed class SafeRenameCommand
    {
        public static SafeRenameCommand? Instance { get; private set; }

        private SafeRenameCommand(Package package, OleMenuCommandService commandService)
        {
            // Safe Rename is invoked from the editor context menu or via F2.
            // For now, register as an OleMenuCommand so it can be triggered from
            // the Command Palette and future context menu integration.
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new SafeRenameCommand(package, commandService);
        }

        /// <summary>
        /// Executes the Safe Rename flow. Can be called from context menu, F2, or Command Palette.
        /// </summary>
        /// <param name="sessionId">The active editor session ID.</param>
        /// <param name="documentText">Full text of the active document.</param>
        /// <param name="selectionStart">Character offset of the selection/cursor.</param>
        /// <param name="selectionLength">Length of the selection (0 if just a cursor).</param>
        /// <param name="originalIdentifier">The identifier under the cursor to rename.</param>
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
                    MessageBox.Show("Place the cursor on an identifier to rename.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Step 1: Prompt for the new name
                var newName = PromptForNewName(originalIdentifier);
                if (string.IsNullOrWhiteSpace(newName) || newName == originalIdentifier)
                    return; // User cancelled or entered the same name

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
                    OperationType = 0, // SafeRename
                    DocumentText = documentText,
                    SelectionStart = selectionStart,
                    SelectionLength = selectionLength > 0 ? selectionLength : originalIdentifier.Length,
                    NewName = newName,
                    OriginalIdentifier = originalIdentifier
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
                    MessageBox.Show($"No references to '{originalIdentifier}' were found in the current scope.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Check for blocking errors before showing preview
                if (response.Errors.Length > 0 && !response.CanApply)
                {
                    MessageBox.Show("Cannot rename:\n\n" + string.Join("\n", response.Errors),
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Step 3: Show preview dialog
                using var previewDialog = new RefactoringPreviewDialog(response, originalIdentifier, newName);
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
                    approvedChanges, originalIdentifier, newName, response.Warnings);

                // Step 5: Open script in a new editor tab
                OpenScriptInNewTab(script, $"Rename_{originalIdentifier}_to_{newName}.sql");

                Log.Information("SafeRename: generated script for '{Original}' → '{New}' with {Count} change(s)",
                    originalIdentifier, newName, approvedChanges.Length);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SafeRenameCommand: failed to execute Safe Rename");
                MessageBox.Show($"Safe Rename failed: {ex.Message}",
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Shows a simple input dialog prompting for the new identifier name.
        /// Returns null if the user cancels.
        /// </summary>
        private static string? PromptForNewName(string currentName)
        {
            using var form = new Form
            {
                Text = Constants.ProductName + " - Safe Rename",
                Width = 420,
                Height = 160,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false
            };

            var label = new Label
            {
                Text = $"Rename '{currentName}' to:",
                Left = 12, Top = 16, Width = 380, AutoSize = false
            };

            var textBox = new TextBox
            {
                Text = currentName,
                Left = 12, Top = 40, Width = 380,
                Font = new System.Drawing.Font("Consolas", 10)
            };
            textBox.SelectAll();

            var btnOk = new Button
            {
                Text = "Rename",
                Left = 210, Top = 80, Width = 85, Height = 28,
                DialogResult = DialogResult.OK
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                Left = 305, Top = 80, Width = 85, Height = 28,
                DialogResult = DialogResult.Cancel
            };

            form.Controls.AddRange(new Control[] { label, textBox, btnOk, btnCancel });
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;
            form.ActiveControl = textBox;

            return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : null;
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
                    Log.Warning("SafeRenameCommand: DTE not available, cannot open script tab");
                    return;
                }

                // Create a temporary file with sanitized name + unique suffix to prevent overwrites
                var tempDir = System.IO.Path.GetTempPath();
                var safeName = SanitizeFileName(suggestedFileName);
                var uniqueSuffix = System.IO.Path.GetRandomFileName().Substring(0, 6);
                var tempFile = System.IO.Path.Combine(tempDir, $"{safeName}_{uniqueSuffix}.sql");
                System.IO.File.WriteAllText(tempFile, scriptContent, System.Text.Encoding.UTF8);

                dte.ItemOperations.OpenFile(tempFile, EnvDTE.Constants.vsViewKindCode);

                Log.Debug("SafeRenameCommand: opened rename script in new tab: {Path}", tempFile);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SafeRenameCommand: failed to open script tab, copying to clipboard instead");

                try
                {
                    System.Windows.Clipboard.SetText(scriptContent);
                    MessageBox.Show(
                        "The rename script has been copied to the clipboard.\nPaste it into a new query window.",
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
    }
}
