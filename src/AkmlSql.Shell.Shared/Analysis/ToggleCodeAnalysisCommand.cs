using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Refactoring;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// Spec 030 T056 (FR-029) — toggles static code analysis on/off by flipping
    /// <c>codeAnalysis.enabled</c> in config.json and notifying the engine (which returns no
    /// diagnostics when disabled). The menu item shows a check mark for the current state, and the
    /// active editor is re-analysed immediately so squiggles clear/appear without an edit.
    /// </summary>
    internal sealed class ToggleCodeAnalysisCommand
    {
        private ToggleCodeAnalysisCommand(Package package, OleMenuCommandService commandService)
        {
            var cmdId    = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdToggleCodeAnalysis);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static ToggleCodeAnalysisCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
            => Instance = new ToggleCodeAnalysisCommand(package, commandService);

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is not OleMenuCommand cmd) return;
            cmd.Visible = true;
            cmd.Enabled = true;
            try { cmd.Checked = ConfigManager.Load().CodeAnalysis.Enabled; }
            catch { /* best-effort check-state */ }
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var settings = ConfigManager.Load();
                settings.CodeAnalysis.Enabled = !settings.CodeAnalysis.Enabled;
                ConfigManager.Save(settings);
                bool nowEnabled = settings.CodeAnalysis.Enabled;
                Log.Information("ToggleCodeAnalysisCommand: code analysis {State}", nowEnabled ? "enabled" : "disabled");

                // Notify the engine to invalidate its analysis-settings cache.
                var client = EngineLifecycle.Manager?.Client;
                if (client != null && client.IsConnected)
                    _ = client.SendNotificationAsync(MessageTypes.AnalysisSettingsChanged, new { });

                // Re-analyse the active editor now so squiggles update without an edit.
                TryReanalyzeActiveEditor();

                SetStatusBar($"AKML SQL: code analysis {(nowEnabled ? "enabled" : "disabled")}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ToggleCodeAnalysisCommand.Execute failed");
                MessageBox.Show("Toggle Code Analysis failed: " + ex.Message,
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void TryReanalyzeActiveEditor()
        {
            try
            {
                var view = RefactorCommandHelper.TryGetActiveEditor()?.View;
                if (view != null &&
                    view.TextBuffer.Properties.TryGetProperty(typeof(AnalysisController), out AnalysisController controller) &&
                    controller != null)
                {
                    controller.TriggerReanalysis();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ToggleCodeAnalysisCommand: re-analyze active editor failed (non-fatal)");
            }
        }

        private static void SetStatusBar(string text)
        {
            try
            {
                var sp = ServiceProvider.GlobalProvider;
                var statusBar = sp?.GetService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar))
                    as Microsoft.VisualStudio.Shell.Interop.IVsStatusbar;
                statusBar?.SetText(text);
            }
            catch { /* best-effort */ }
        }
    }
}
