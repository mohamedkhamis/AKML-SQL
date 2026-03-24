#nullable enable
using System;
using System.ComponentModel.Design;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using AkmlSql.Shell.Shared.Tabs;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Restores the most recently closed tab by popping the top entry from
    /// <see cref="ClosedTabStack"/>, opening a new editor window, and inserting
    /// the captured content.
    /// <para>
    /// Bound to <see cref="CommandIds.CmdRestoreClosedTab"/> (0x0501).
    /// Disabled when the stack is empty (via <c>BeforeQueryStatus</c>).
    /// </para>
    /// </summary>
    internal sealed class RestoreClosedTabCommand
    {
        private RestoreClosedTabCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdRestoreClosedTab);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static RestoreClosedTabCommand? Instance { get; private set; }

        /// <summary>
        /// Creates the singleton command instance and registers it with the command service.
        /// Must be called on the UI thread during package initialization.
        /// </summary>
        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new RestoreClosedTabCommand(package, commandService);
        }

        /// <summary>
        /// Disables the command when the closed tab stack is empty.
        /// </summary>
        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand cmd)
            {
                cmd.Enabled = ClosedTabStack.Instance.Count > 0;
            }
        }

        /// <summary>
        /// Pops the most recent entry and opens a new editor with restored content.
        /// </summary>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var entry = ClosedTabStack.Instance.Pop();
                if (entry == null)
                {
                    Log.Debug("RestoreClosedTabCommand: stack is empty, nothing to restore");
                    return;
                }

                Log.Information("RestoreClosedTabCommand: restoring '{Title}' (closed at {ClosedAt})",
                    entry.TabTitle ?? "(untitled)", entry.ClosedAt);

                // Get DTE to create a new editor window
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                if (dte == null)
                {
                    Log.Error("RestoreClosedTabCommand: DTE service unavailable");
                    return;
                }

                // Open a new untitled SQL file
                dte.ItemOperations.NewFile(
                    @"General\Sql File",
                    entry.TabTitle ?? "Restored.sql",
                    EnvDTE.Constants.vsViewKindCode);

                // Insert restored content using the TextDocument interface
                var activeDoc = dte.ActiveDocument;
                if (activeDoc == null)
                {
                    Log.Warning("RestoreClosedTabCommand: no active document after NewFile");
                    return;
                }

                var textDocument = activeDoc.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    Log.Warning("RestoreClosedTabCommand: new document has no TextDocument; content not restored");
                    return;
                }

                var editPoint = textDocument.StartPoint.CreateEditPoint();
                editPoint.Insert(entry.Content);

                // Move cursor to the beginning of the document
                textDocument.Selection.StartOfDocument();

                Log.Information("RestoreClosedTabCommand: successfully restored '{Title}'",
                    entry.TabTitle ?? "(untitled)");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "RestoreClosedTabCommand: failed to restore tab");
                System.Windows.Forms.MessageBox.Show(
                    "Unable to restore the closed tab: " + ex.Message,
                    Constants.ProductName,
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
            }
        }
    }
}
