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

        public static OptionsCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new OptionsCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            try
            {
                var settings = ConfigManager.Load();

                // Loop to support theme-change reopen: when the user switches between
                // Dark and Light themes, the window closes and immediately reopens with
                // the new theme applied.
                while (true)
                {
                    var window = new SettingsWindow(settings);
                    if (window.ShowDialog())
                    {
                        if (window.ThemeChangeRequested)
                        {
                            // Settings were already saved by the theme handler —
                            // reload them so the next window instance picks up the new theme
                            settings = ConfigManager.Load();
                            continue;
                        }

                        var updated = window.GetSettings();
                        ConfigManager.Save(updated);
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

                    break;
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
            }
        }
    }
}
