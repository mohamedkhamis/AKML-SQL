#nullable enable
using System;
using System.ComponentModel.Design;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Duplicates the active editor tab by reading its content and opening a new
    /// editor window with the same text.
    /// <para>
    /// Bound to <see cref="CommandIds.CmdDuplicateTab"/> (0x0503).
    /// Disabled when no text editor is active.
    /// </para>
    /// </summary>
    internal sealed class DuplicateTabCommand
    {
        private DuplicateTabCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdDuplicateTab);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static DuplicateTabCommand? Instance { get; private set; }

        /// <summary>
        /// Creates the singleton command instance and registers it with the command service.
        /// </summary>
        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new DuplicateTabCommand(package, commandService);
        }

        /// <summary>
        /// Enables the command only when a text view is available.
        /// </summary>
        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is not OleMenuCommand cmd) return;

            try
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null)
                {
                    cmd.Enabled = false;
                    return;
                }

                textManager.GetActiveView(1, null, out var textView);
                cmd.Enabled = textView != null;
            }
            catch
            {
                cmd.Enabled = false;
            }
        }

        /// <summary>
        /// Reads the current editor content, opens a new file, and inserts the content.
        /// </summary>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Read content from the active view via IVsTextView
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null)
                {
                    Log.Warning("DuplicateTabCommand: IVsTextManager unavailable");
                    return;
                }

                textManager.GetActiveView(1, null, out var textView);
                if (textView == null)
                {
                    Log.Warning("DuplicateTabCommand: no active text view");
                    return;
                }

                textView.GetBuffer(out var buffer);
                if (buffer == null)
                {
                    Log.Warning("DuplicateTabCommand: no text buffer");
                    return;
                }

                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var content);

                if (string.IsNullOrEmpty(content))
                {
                    Log.Debug("DuplicateTabCommand: source document is empty");
                    return;
                }

                // Get the source document name for the duplicate
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                if (dte == null)
                {
                    Log.Error("DuplicateTabCommand: DTE service unavailable");
                    return;
                }

                var sourceDocName = dte.ActiveDocument?.Name ?? "Duplicate.sql";
                var duplicateName = "Copy of " + sourceDocName;

                // Open a new file
                dte.ItemOperations.NewFile(
                    @"General\Sql File",
                    duplicateName,
                    EnvDTE.Constants.vsViewKindCode);

                // Insert content into the new document using TextDocument interface
                var newDoc = dte.ActiveDocument;
                if (newDoc != null)
                {
                    var textDocument = newDoc.Object("TextDocument") as TextDocument;
                    if (textDocument != null)
                    {
                        var editPoint = textDocument.StartPoint.CreateEditPoint();
                        editPoint.Insert(content);

                        // Move cursor to the beginning
                        var startPoint = textDocument.StartPoint.CreateEditPoint();
                        startPoint.StartOfDocument();
                        textDocument.Selection.MoveToPoint(startPoint);

                        Log.Information("DuplicateTabCommand: duplicated '{Source}' as '{Duplicate}'",
                            sourceDocName, duplicateName);
                    }
                    else
                    {
                        Log.Warning("DuplicateTabCommand: new document has no TextDocument interface");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "DuplicateTabCommand: failed to duplicate tab");
                System.Windows.Forms.MessageBox.Show(
                    "Unable to duplicate the tab: " + ex.Message,
                    Constants.ProductName,
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
            }
        }
    }
}
