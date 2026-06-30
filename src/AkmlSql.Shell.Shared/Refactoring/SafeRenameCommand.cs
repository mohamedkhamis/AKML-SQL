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
    /// Spec 030 / T062 / FR-018 / R8 — shell command for database-wide Smart Rename.
    /// <para>
    /// Dispatch foundation (the command was previously built-but-unwired): the ctor registers an
    /// <see cref="OleMenuCommand"/> on <see cref="CommandIds.CmdSafeRename"/> (mirroring
    /// <c>ScriptAsAlterCommand</c>/<c>InlineExecCommand</c>); <see cref="Execute(object, EventArgs)"/>
    /// resolves the caret object/column via <see cref="RefactorCommandHelper"/>, prompts for a new name,
    /// runs a <c>RefactorPreview</c> with <see cref="RefactorScope.Database"/>, shows
    /// <see cref="RefactoringPreviewDialog"/>, and on OK opens the engine-generated reviewable script
    /// (<see cref="RefactorPreviewResponse.GeneratedObjectTexts"/>) in a new editor tab.
    /// </para>
    /// <para>
    /// Apply = emit the reviewable script (the user runs it deliberately), NOT auto-execute against the
    /// database. The script is engine-generated (only the engine has the live connection to enumerate
    /// dependents) — the comment-only <c>RenameScriptGenerator</c> is intentionally NOT used.
    /// </para>
    /// </summary>
    internal sealed class SafeRenameCommand
    {
        public static SafeRenameCommand? Instance { get; private set; }

        private SafeRenameCommand(Package package, OleMenuCommandService commandService)
        {
            // (1) of the 3-part dispatch foundation: register the OleMenuCommand so the palette's
            // dte.Commands.Raise('{cmdSet}', CmdSafeRename) — and any future menu/context placement —
            // reaches Execute. Without this AddCommand the command was never invokable.
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdSafeRename);
            var item  = new OleMenuCommand(Execute, cmdId);
            item.BeforeQueryStatus += (s, _) => { if (s is OleMenuCommand c) { c.Visible = true; c.Enabled = true; } };
            commandService.AddCommand(item);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new SafeRenameCommand(package, commandService);
        }

        /// <summary>
        /// OleMenuCommand handler: resolves the active editor + the caret object/column, prompts for a
        /// new name, runs the database-wide preview, and opens the reviewable script.
        /// </summary>
        private void Execute(object sender, EventArgs e)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var ctx = RefactorCommandHelper.TryGetActiveEditor();
                if (ctx == null || string.IsNullOrEmpty(ctx.DocumentText))
                {
                    MessageBox.Show("Open a SQL document and place the cursor on the object or column to rename.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Resolve the caret target and classify object-vs-column. For a column the engine expects
                // OriginalIdentifier="tableSchema.column" + ExtractedUnitName="tableName".
                var (schema, parentTable, name, isColumn) =
                    RefactorCommandHelper.ExtractRenameTargetAtCaret(ctx.DocumentText, ctx.CaretOffset);

                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("Place the cursor on a table, view, procedure, function, or a column qualified by its table (schema.table.column).",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var displayName = isColumn ? $"{schema}.{parentTable}.{name}" : $"{schema}.{name}";

                var newName = PromptForNewName(name);
                if (string.IsNullOrWhiteSpace(newName) || newName == name)
                    return; // User cancelled or entered the same name

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    MessageBox.Show("The AKML SQL engine is not running yet — try again in a moment.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var request = new RefactorPreviewRequest
                {
                    SessionId          = ctx.SessionId,
                    OperationType      = (int)RefactorOperationType.SafeRename,
                    Scope              = (int)RefactorScope.Database,
                    DocumentText       = ctx.DocumentText,
                    SelectionStart     = ctx.SelectionStart,
                    SelectionLength    = ctx.SelectionLength,
                    NewName            = newName,
                    // For a column rename the engine reads the TABLE from ExtractedUnitName and the
                    // (tableSchema, column) from OriginalIdentifier; for an object it is just schema.name.
                    OriginalIdentifier = $"{schema}.{name}",
                    ExtractedUnitName  = isColumn ? (parentTable ?? string.Empty) : string.Empty
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

                // Blocking errors (no connection, permission denied, unresolved target) — show and stop.
                if (!response.CanApply)
                {
                    var msg = response.Errors.Length > 0
                        ? string.Join("\n", response.Errors)
                        : "Smart Rename could not produce a script.";
                    MessageBox.Show("Cannot rename:\n\n" + msg,
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // For DB-wide rename the reviewable SCRIPT lives in GeneratedObjectTexts (NOT Changes — a
                // valid object rename with zero dependents has an empty Changes list but a real script).
                if (response.GeneratedObjectTexts == null || response.GeneratedObjectTexts.Length == 0
                    || string.IsNullOrWhiteSpace(response.GeneratedObjectTexts[0]))
                {
                    MessageBox.Show("The engine did not return a rename script.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Show the preview dialog (lists the affected dependents from Changes; the script header
                // names the rename). On OK we emit the engine script for the user to review and run.
                using var previewDialog = new RefactoringPreviewDialog(
                    response, displayName, newName, applyButtonText: "Generate Script");
                if (previewDialog.ShowDialog() != DialogResult.OK)
                    return; // User cancelled

                OpenScriptInNewTab(response.GeneratedObjectTexts[0], $"SmartRename_{name}_to_{newName}.sql");

                Log.Information("SmartRename(DB-wide): generated script for '{Original}' → '{New}' ({DepCount} dependent(s))",
                    displayName, newName, response.Changes?.Length ?? 0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SafeRenameCommand: failed to execute Smart Rename");
                MessageBox.Show($"Smart Rename failed: {ex.Message}",
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
