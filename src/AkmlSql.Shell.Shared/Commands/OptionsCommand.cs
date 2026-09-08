#nullable enable
using System;
using System.ComponentModel.Design;
using System.Windows;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    internal sealed class OptionsCommand
    {
        private OptionsCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdOptions);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static OptionsCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new OptionsCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ShowOptions(null, null);
        }

        /// <summary>
        /// Spec 037 (US1, FR-017, research R8): the ONE open-Options-and-save path. Opens the
        /// settings dialog (deep-linked to <paramref name="pageKey"/> and, for the AI Assistance
        /// page, to <paramref name="agentId"/> when given), then on OK performs
        /// <c>ConfigManager.Save</c> AND the <see cref="MessageTypes.AnalysisSettingsChanged"/>
        /// notification that makes the engine drop its settings cache — the mechanism FR-019's
        /// "no restart" stands on. Callers (the chat panel's onboarding card, the no-agent gate)
        /// must use this rather than saving settings themselves, or the engine keeps serving
        /// stale settings. Returns true when settings were saved.
        /// </summary>
        internal static bool ShowOptions(string? pageKey, string? agentId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var settings = ConfigManager.Load();

                // Loop to support theme-change reopen: when the user switches between
                // Dark and Light themes, the window closes and immediately reopens with
                // the new theme applied.
                while (true)
                {
                    var window = new SettingsWindow(settings) { InitialAgentId = agentId };
                    if (window.ShowDialog(pageKey))
                    {
                        if (window.ThemeChangeRequested)
                        {
                            // Settings were already saved by the theme handler —
                            // reload them so the next window instance picks up the new theme
                            settings = ConfigManager.Load();
                            continue;
                        }

                        SaveAndNotify(window.GetSettings());
                        return true;
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open settings dialog");
                MessageBox.Show(
                    "Failed to load settings: " + ex.Message,
                    Constants.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Spec 037 (US3, FR-040): the save-and-notify half of the shared path, for callers that
        /// change settings WITHOUT opening the dialog — the chat panel's agent picker persisting
        /// <c>FeatureAgents.Chat</c>. Keeping the body here means there is still exactly one
        /// save path: <c>ConfigManager.Save</c>, the tab-color repaint, and the
        /// <see cref="MessageTypes.AnalysisSettingsChanged"/> notification that makes the engine
        /// drop its settings cache. The panel must never call <c>ConfigManager.Save</c> itself.
        /// </summary>
        internal static void SaveAndNotify(AppSettings settings)
        {
            ConfigManager.Save(settings);
            Log.Information("Settings saved successfully.");

            // FR-042: Live re-render tab colors after settings change
            try { Tabs.TabColoringManager.RepaintAllTabs(); } catch { }

            // T066: Notify the engine to reload its settings cache (fire-and-forget)
            var client = EngineLifecycle.Manager?.Client;
            if (client != null && client.IsConnected)
            {
                _ = client.SendNotificationAsync(
                    MessageTypes.AnalysisSettingsChanged,
                    new { });
            }
        }
    }
}
