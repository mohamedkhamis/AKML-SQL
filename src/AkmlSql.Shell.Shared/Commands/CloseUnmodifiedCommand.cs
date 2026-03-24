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
    /// Closes all open document tabs that are not dirty (unmodified) and not pinned.
    /// <para>
    /// Bound to <see cref="CommandIds.CmdCloseUnmodified"/> (0x0502).
    /// Pinned documents (tracked by <see cref="PinTabCommand"/>) are always skipped.
    /// </para>
    /// </summary>
    internal sealed class CloseUnmodifiedCommand
    {
        private CloseUnmodifiedCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdCloseUnmodified);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static CloseUnmodifiedCommand? Instance { get; private set; }

        /// <summary>
        /// Creates the singleton command instance and registers it with the command service.
        /// </summary>
        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new CloseUnmodifiedCommand(package, commandService);
        }

        /// <summary>
        /// Enables the command when at least one document is open.
        /// </summary>
        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is not OleMenuCommand cmd) return;

            try
            {
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                cmd.Enabled = dte?.Documents?.Count > 0;
            }
            catch
            {
                cmd.Enabled = false;
            }
        }

        /// <summary>
        /// Iterates all open documents and closes those that are not dirty and not pinned.
        /// </summary>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                if (dte?.Documents == null)
                {
                    Log.Debug("CloseUnmodifiedCommand: no DTE or documents collection");
                    return;
                }

                // Collect documents to close first (avoid modifying the collection during iteration)
                var toClose = new List<Document>();

                foreach (Document doc in dte.Documents)
                {
                    ThreadHelper.ThrowIfNotOnUIThread();

                    try
                    {
                        // Skip dirty (modified) documents
                        if (!doc.Saved)
                        {
                            Log.Debug("CloseUnmodifiedCommand: skipping modified '{Name}'", doc.Name);
                            continue;
                        }

                        // Skip pinned documents
                        if (PinTabCommand.IsPinned(doc.FullName))
                        {
                            Log.Debug("CloseUnmodifiedCommand: skipping pinned '{Name}'", doc.Name);
                            continue;
                        }

                        toClose.Add(doc);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "CloseUnmodifiedCommand: error checking document");
                    }
                }

                if (toClose.Count == 0)
                {
                    Log.Debug("CloseUnmodifiedCommand: no unmodified, unpinned documents to close");
                    return;
                }

                var closedCount = 0;
                foreach (var doc in toClose)
                {
                    try
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        doc.Close(vsSaveChanges.vsSaveChangesNo);
                        closedCount++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "CloseUnmodifiedCommand: failed to close '{Name}'", doc.Name);
                    }
                }

                Log.Information("CloseUnmodifiedCommand: closed {Count} unmodified tab(s)", closedCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CloseUnmodifiedCommand: failed");
            }
        }
    }
}
