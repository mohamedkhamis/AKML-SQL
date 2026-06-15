#nullable enable
using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using AkmlSql.Shell.Shared.Refactoring;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 030 T068 (FR-023) — wraps the current selection in the formatter's noformat directives
    /// (<c>-- AKML formatting off</c> … <c>-- AKML formatting on</c>) so the layout/casing pipeline
    /// leaves that region untouched. Recognised by <c>NoformatScanner</c> (pipeline stage 1).
    /// </summary>
    internal sealed class DisableFormattingForSelectionCommand
    {
        private const string OpenMarker  = "-- AKML formatting off";
        private const string CloseMarker = "-- AKML formatting on";

        private DisableFormattingForSelectionCommand(Package package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdDisableFormattingForSelection);
            var item  = new OleMenuCommand(Execute, cmdId);
            item.BeforeQueryStatus += (s, _) => { if (s is OleMenuCommand c) { c.Visible = true; c.Enabled = true; } };
            commandService.AddCommand(item);
        }

        public static DisableFormattingForSelectionCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
            => Instance = new DisableFormattingForSelectionCommand(package, commandService);

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var ctx = RefactorCommandHelper.TryGetActiveEditor();
                if (ctx == null)
                {
                    MessageBox.Show("Open a SQL document and select the text to exclude from formatting.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (ctx.SelectionLength <= 0)
                {
                    MessageBox.Show("Select the text to exclude from formatting first.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int start = ctx.SelectionStart;
                int end   = ctx.SelectionStart + ctx.SelectionLength;
                int len   = ctx.View.TextBuffer.CurrentSnapshot.Length;
                if (start < 0 || end > len) return;

                // ITextEdit positions are relative to the original snapshot, so the two inserts at
                // distinct offsets both land correctly regardless of order.
                using var edit = ctx.View.TextBuffer.CreateEdit();
                edit.Insert(end, "\r\n" + CloseMarker);
                edit.Insert(start, OpenMarker + "\r\n");
                edit.Apply();

                Log.Information("DisableFormattingForSelection: wrapped {Len} chars in noformat markers", ctx.SelectionLength);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "DisableFormattingForSelectionCommand.Execute failed");
                MessageBox.Show("Disable Formatting failed: " + ex.Message,
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
