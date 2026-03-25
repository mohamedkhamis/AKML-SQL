#nullable enable
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Execution
{
    /// <summary>
    /// T081: Wires the multi-database execution flow: opens the database selector,
    /// runs the query against selected databases, and displays the results.
    /// This is not bound to a VSCT command — it is invoked from the Command Palette
    /// or programmatically.
    /// </summary>
    internal static class MultiDatabaseCommand
    {
        /// <summary>
        /// Registers a menu command for the multi-database execution feature.
        /// Currently invoked via the Command Palette; may be wired to a VSCT button in the future.
        /// </summary>
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            // Multi-database is invoked through the Command Palette, not a dedicated menu button.
            // This method is a placeholder for future direct command registration.
            Log.Debug("MultiDatabaseCommand: initialized (Command Palette entry)");
        }

        /// <summary>
        /// Executes the multi-database workflow: selector dialog -> parallel execution -> results view.
        /// Must be called on the UI thread.
        /// </summary>
        public static async Task ExecuteAsync(string serverName, string baseConnectionString, string sql)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Check feature flag
                var settings = ConfigManager.Load();
                if (!settings.ExecutionProductivity.MultiDatabase)
                {
                    Log.Debug("MultiDatabaseCommand: feature disabled");
                    return;
                }

                // Show database selector
                using var selector = new MultiDatabaseSelector(serverName);
                selector.LoadDatabases(baseConnectionString);

                if (selector.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                var selectedDatabases = selector.SelectedDatabases;
                if (selectedDatabases.Count == 0) return;

                Log.Information("MultiDatabaseCommand: executing against {Count} databases on {Server}",
                    selectedDatabases.Count, serverName);

                // Update status bar
                var statusBar = (IVsStatusbar?)Package.GetGlobalService(typeof(SVsStatusbar));
                if (statusBar != null)
                {
                    StatusBar.StatusBarManager.SetTransactionIndicator(statusBar,
                        $"Executing on {selectedDatabases.Count} databases...");
                }

                // Run execution off the UI thread
                var executor = new MultiDatabaseExecutor(baseConnectionString);
                var results = await executor.ExecuteAsync(sql, selectedDatabases)
                    .ConfigureAwait(true);

                // Clear status bar
                if (statusBar != null)
                {
                    StatusBar.StatusBarManager.ClearTransactionIndicator(statusBar);
                }

                // Show results
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                using var resultsView = new MultiDatabaseResultsView(results);
                resultsView.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MultiDatabaseCommand: execution failed");
            }
        }
    }
}
