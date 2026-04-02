#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Toggles the "pinned" state of the active document tab. Pinned tabs are
    /// excluded from bulk-close operations (e.g. <see cref="CloseUnmodifiedCommand"/>).
    /// <para>
    /// Bound to <see cref="CommandIds.CmdPinTab"/> (0x0504).
    /// The command text toggles between "Pin Tab" and "Unpin Tab" via <c>BeforeQueryStatus</c>.
    /// </para>
    /// <para>
    /// Pin state is maintained in memory only (not persisted across sessions).
    /// </para>
    /// </summary>
    internal sealed class PinTabCommand
    {
        /// <summary>
        /// Set of document full paths that are currently pinned.
        /// Thread-safe for read access; modifications occur only on the UI thread.
        /// </summary>
        private static readonly HashSet<string> PinnedDocuments =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private PinTabCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdPinTab);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static PinTabCommand? Instance { get; private set; }

        /// <summary>
        /// Creates the singleton command instance and registers it with the command service.
        /// </summary>
        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new PinTabCommand(package, commandService);
        }

        /// <summary>
        /// Returns <c>true</c> if the document at the specified path is pinned.
        /// </summary>
        /// <param name="documentPath">Full path of the document.</param>
        public static bool IsPinned(string? documentPath)
        {
            if (string.IsNullOrEmpty(documentPath)) return false;
            return PinnedDocuments.Contains(documentPath!);
        }

        /// <summary>
        /// Updates command text and enabled state based on the active document.
        /// </summary>
        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is not OleMenuCommand cmd) return;

            try
            {
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                var activeDoc = dte?.ActiveDocument;

                if (activeDoc == null)
                {
                    cmd.Enabled = false;
                    cmd.Text = "Pin Tab";
                    return;
                }

                cmd.Enabled = true;
                var isPinned = IsPinned(activeDoc.FullName);
                cmd.Text = isPinned ? "Unpin Tab" : "Pin Tab";
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "PinTabCommand: BeforeQueryStatus error");
                cmd.Enabled = false;
            }
        }

        /// <summary>
        /// Toggles pin state on the active document.
        /// </summary>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                var activeDoc = dte?.ActiveDocument;

                if (activeDoc == null)
                {
                    Log.Debug("PinTabCommand: no active document");
                    return;
                }

                var docPath = activeDoc.FullName;
                if (string.IsNullOrEmpty(docPath)) return;

                if (PinnedDocuments.Contains(docPath))
                {
                    PinnedDocuments.Remove(docPath);
                    Log.Information("PinTabCommand: unpinned '{Document}'", activeDoc.Name);
                }
                else
                {
                    PinnedDocuments.Add(docPath);
                    Log.Information("PinTabCommand: pinned '{Document}'", activeDoc.Name);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PinTabCommand: failed to toggle pin state");
            }
        }
    }
}
