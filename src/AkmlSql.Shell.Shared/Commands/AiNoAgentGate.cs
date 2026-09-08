#nullable enable
using System;
using System.Windows;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Spec 037 (US1, FR-022) — the no-agent gate for the deliberately invoked AI surfaces
    /// (Explain, Fix, Optimize, Index Analysis, Text-to-SQL). The chat panel has its own card;
    /// ghost text stays silent by design (it fires on typing — the user never asked). These five
    /// report the chat card's reason (the FR-021 wordings) and offer the same route into Options
    /// instead of surfacing a raw provider error. The commands are normally hidden by
    /// <see cref="AiCommandVisibility"/> when no agent is usable, but a shortcut or a stale menu
    /// can still invoke them — and <see cref="AiSettings.Enabled"/> cannot see an undecryptable
    /// key (V23), which this check does see.
    /// </summary>
    internal static class AiNoAgentGate
    {
        /// <summary>
        /// Reports the no-agent state and offers the Options route when no agent can answer.
        /// Returns <c>true</c> when the caller must stop (nothing can answer the request).
        /// </summary>
        public static async System.Threading.Tasks.Task<bool> ShouldStopAsync(string featureLabel)
        {
            AppSettings settings;
            try
            {
                settings = ConfigManager.Load();
            }
            catch (Exception ex)
            {
                // A failed read must not turn into a false "no agent" claim — let the feature
                // proceed to its own error path.
                Log.Debug(ex, "AiNoAgentGate: settings read failed; letting the feature proceed");
                return false;
            }

            var agents = settings.Ai.Agents;
            if (agents != null)
            {
                foreach (var agent in agents)
                {
                    if (AiChatEmptyState.CanAnswer(agent))
                        return false;
                }
            }

            var (text, agentId) = AiChatEmptyState.DetectReason(settings.Ai);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var openOptions = MessageBox.Show(
                text + "\n\nOpen AKML SQL Options to set up an AI agent?",
                featureLabel,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
            if (openOptions)
            {
                // The shared save-and-notify path — never a bare ConfigManager.Save, or the
                // engine keeps serving the old configuration (R8).
                OptionsCommand.ShowOptions("AI Assistance", agentId);
            }
            return true;
        }
    }
}
