#nullable enable
using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using AkmlSql.Core.Snippets;
using AkmlSql.Shell.Shared.Refactoring;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Snippets
{
    /// <summary>
    /// Spec 030 T044 / FR-033 — "Create Snippet from Selection". Takes the selected SQL in the active
    /// editor, derives an auto shortcode from the selection's initials, and opens the Snippet Manager in
    /// new-snippet mode pre-seeded with the body + shortcode. Reuses the (T046 variable-preserving) Save
    /// path. Surfaced via the Command Palette (no VSCT menu button), matching the T067 command precedent.
    /// Bound to <see cref="CommandIds.CmdSnippetCreateFromSelection"/> (0x091B).
    /// </summary>
    internal sealed class CreateFromSelectionCommand
    {
        private CreateFromSelectionCommand(Package package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdSnippetCreateFromSelection);
            var item  = new OleMenuCommand(Execute, cmdId);
            item.BeforeQueryStatus += (s, _) => { if (s is OleMenuCommand c) { c.Visible = true; c.Enabled = true; } };
            commandService.AddCommand(item);
        }

        public static CreateFromSelectionCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
            => Instance = new CreateFromSelectionCommand(package, commandService);

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var ctx = RefactorCommandHelper.TryGetActiveEditor();
                if (ctx == null || string.IsNullOrEmpty(ctx.DocumentText))
                {
                    MessageBox.Show("Open a SQL document and select the code you want to save as a snippet.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (ctx.SelectionLength <= 0)
                {
                    MessageBox.Show("Select the code you want to save as a snippet first.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int start = ctx.SelectionStart;
                int len   = ctx.SelectionLength;
                // Bounds guard against a stale snapshot/selection.
                if (start < 0 || len < 0 || start + len > ctx.DocumentText.Length)
                {
                    MessageBox.Show("The selection is no longer valid — try selecting the code again.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var selection = ctx.DocumentText.Substring(start, len);
                if (string.IsNullOrWhiteSpace(selection))
                {
                    MessageBox.Show("Select the code you want to save as a snippet first.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var shortcode = SnippetShortcodeGenerator.FromSelection(selection);

                // Construct the dialog FIRST (it wires PropertyChanged in its ctor and has no initial
                // VM→textbox push), THEN seed the VM so the field setters populate the textboxes.
                var viewModel = new SnippetManagerViewModel();
                var dialog = new SnippetManagerDialog(viewModel);
                viewModel.NewSnippetFromSelection(selection, shortcode);
                dialog.ShowModal();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CreateFromSelectionCommand.Execute failed");
                MessageBox.Show("Create Snippet from Selection failed: " + ex.Message,
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
