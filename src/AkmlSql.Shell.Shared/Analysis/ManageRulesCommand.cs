using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// Spec 030 T053 (FR-026) — "Manage Code Analysis Rules…" command. Loads the rule catalog from
    /// the engine (<c>ListAnalysisRules</c>), shows <see cref="ManageRulesDialog"/>, and on OK writes
    /// the per-rule deviations to <c>config.json codeAnalysis.ruleOverrides</c> and notifies the
    /// engine via <c>AnalysisSettingsChanged</c> (so live analysis and the dialog's next open reflect
    /// the change). Mirrors <see cref="Commands.OptionsCommand"/>.
    /// </summary>
    internal sealed class ManageRulesCommand
    {
        private ManageRulesCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId    = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdManageCodeAnalysisRules);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static ManageRulesCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new ManageRulesCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    MessageBox.Show("The AKML SQL engine is not running yet — try again in a moment.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ListAnalysisRulesResponse? response = null;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    response = await client.SendRequestAsync<ListAnalysisRulesResponse, ListAnalysisRulesRequest>(
                        MessageTypes.ListAnalysisRules,
                        new ListAnalysisRulesRequest { FileDirectory = string.Empty },
                        timeoutMs: 10_000);
                });

                if (response == null || !response.Success)
                {
                    MessageBox.Show("Could not load the analysis rules: " + (response?.Error ?? "no response from the engine."),
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                using var dialog = new ManageRulesDialog(response.Rules);
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                var overrides = dialog.GetOverrides();

                var settings = ConfigManager.Load();
                settings.CodeAnalysis.RuleOverrides = overrides;
                ConfigManager.Save(settings);
                Log.Information("ManageRulesCommand: saved {Count} rule override(s)", overrides.Count);

                // Notify the engine to invalidate its analysis-settings cache (fire-and-forget).
                if (client.IsConnected)
                {
                    _ = client.SendNotificationAsync(MessageTypes.AnalysisSettingsChanged, new { });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ManageRulesCommand.Execute failed");
                MessageBox.Show("Manage Rules failed: " + ex.Message,
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
